using LabTetherAgent.Api;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using LabTetherAgent.State;
using System.Net.NetworkInformation;
using System.Security.Cryptography;

namespace LabTetherAgent.App;

/// <summary>
/// Global application state container. Owns all core services and coordinates lifecycle.
/// Mirrors mac-agent/Sources/LabTetherAgent/App/AppState.swift.
/// </summary>
public partial class AppState : IDisposable
{
    private static readonly TimeSpan SetupConnectionTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SetupReadinessPollInterval = TimeSpan.FromMilliseconds(200);
    private static AppState? _instance;
    public static AppState Shared => _instance ?? throw new InvalidOperationException("AppState not initialized.");

    // Core services
    public AgentSettings Settings { get; }
    public CredentialStore CredentialStore { get; }
    public AgentProcess AgentProcess { get; }
    public LocalApiClient ApiClient { get; }
    public ConnectionTester ConnectionTester { get; }
    public UpdateChecker UpdateChecker { get; }

    // Derived state
    public bool ShouldShowOnboarding => !Settings.IsEnrolled;
    public bool IsAgentRunning => AgentProcess.IsRunning;

    private string? _localApiPort;
    private string? _localApiAuthToken;
    private string? _activeSecretDirectory;
    private AgentSettings? _activeLaunchSettings;
    private bool _activeLaunchCompletesEnrollment;
    private bool _setupTransactionActive;
    private readonly CrashRestartCancellation _crashRestartCancellation = new();
    private readonly NetworkAvailabilityChangedEventHandler _networkAvailabilityChangedHandler;
    private bool _disposed;
    private long _activeLaunchGeneration;
    private long _launchEpoch;
    private long? _preparingEpoch;
    private readonly object _launchFilesGate = new();
    private readonly Func<string?> _findAgentBinary;
    private readonly Action<string> _killOrphanedAgents;
    private object LifecycleGate => AgentProcess.LifecycleGate;

    private AppState(AppStateLocation location) : this(AgentSettings.Load(location), new CredentialStore(location), new AgentProcess(location), new LocalApiClient(),
        () => FindAgentBinary(), loadCredentials: true, monitorNetwork: true) { }

    internal AppState(AgentSettings settings, CredentialStore credentialStore, AgentProcess process,
        LocalApiClient apiClient, Func<string?> findAgentBinary, bool loadCredentials = false, bool monitorNetwork = false,
        Action<string>? killOrphanedAgents = null)
    {
        Settings = settings;
        CredentialStore = credentialStore;
        if (loadCredentials) CredentialStore.LoadInto(Settings);
        AgentProcess = process;
        ApiClient = apiClient;
        _findAgentBinary = findAgentBinary;
        _killOrphanedAgents = killOrphanedAgents ?? process.KillOrphanedAgents;
        ConnectionTester = new ConnectionTester();

        // Native-wrapper releases have their own version stream. The bundled
        // Go child's AGENT_VERSION is intentionally separate and self-updates
        // through the hub.
        UpdateChecker = new UpdateChecker(CurrentAppVersion);

        // Wire crash restart
        AgentProcess.OnOwnedExited += OnAgentExited;
        AgentProcess.OnOwnedStarted += OnAgentStarted;

        // Wire network change monitoring for immediate poll on reconnect
        _networkAvailabilityChangedHandler = (_, args) =>
        {
            if (!_disposed && args.IsAvailable)
                ApiClient.PollNow();
        };
        if (monitorNetwork) NetworkChange.NetworkAvailabilityChanged += _networkAvailabilityChangedHandler;
    }

    public static AppState Initialize(AppStateLocation? location = null)
    {
        _instance = new AppState(location ?? AppStateLocation.Installed);
        return _instance;
    }

    /// <summary>
    /// Start the agent process with current settings.
    /// </summary>
    public void StartAgent() => StartAgent(null);

    private void StartAgent(long? expectedGeneration)
    {
        lock (LifecycleGate)
        {
            if (_disposed || (expectedGeneration.HasValue && AgentProcess.Generation != expectedGeneration)) return;
        }

        _crashRestartCancellation.Cancel();

        // Older wrapper versions persisted onboarding's group field and sent
        // it on every durable-token heartbeat. Migrate that one-time intent
        // before constructing the child environment. Even if persistence is
        // temporarily unavailable, the in-memory clear keeps this launch from
        // replaying a stale group and a future launch will retry the migration.
        string? migrationDiagnostic;
        lock (_launchFilesGate) { TryClearCompletedEnrollmentGroupIntent(out migrationDiagnostic); }
        if (migrationDiagnostic != null) AgentProcess.LogReader.AppendRaw(migrationDiagnostic);

        var settingsDirectory = Settings.Location.DirectoryPath;
        Settings.Location.Validate();
        try
        {
            if (!Settings.Location.IsIsolated && AgentManagedState.MigrateLegacyDeviceIdentityIfNeeded(settingsDirectory))
            {
                AgentProcess.LogReader.AppendRaw(
                    "Migrated the enrolled native agent identity out of machine-wide service storage.");
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            PlatformNotSupportedException or
            System.Security.SecurityException)
        {
            AgentProcess.ReportError(
                $"The enrolled device identity could not be migrated safely: {ex.Message}");
            return;
        }

        StartAgentWithSettings(
            Settings,
            settingsDirectory,
            completesEnrollment: true, expectedGeneration);
    }

    internal void StartAgentWithSettings(
        AgentSettings launchSettings, string secretDirectory, bool completesEnrollment,
        long? expectedGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(launchSettings);
        AgentProcess.RefreshExitedChild();
        long epoch;
        Action notifyRetired;
        lock (LifecycleGate)
        {
            if (_disposed || AgentProcess.IsRunning || AgentProcess.IsStarting || _preparingEpoch.HasValue ||
                (expectedGeneration.HasValue && AgentProcess.Generation != expectedGeneration)) return;
            epoch = ++_launchEpoch;
            _preparingEpoch = epoch;
            _crashRestartCancellation.Cancel();
            notifyRetired = ApiClient.StopPollingState();
        }
        Action? start = null;
        Func<Task>? abandonedStop = null;
        string? error = null;
        string? binaryPath = null;
        try
        {
            notifyRetired();
            // File work has a separate order: files gate -> short lifecycle
            // gate. Stop/exit never wait for files before retiring the child.
            lock (_launchFilesGate)
            {
                lock (LifecycleGate) { if (_disposed || epoch != _launchEpoch) return; }
                binaryPath = _findAgentBinary();
                if (binaryPath == null)
                {
                    error = "The bundled LabTether agent core is missing. Reinstall LabTether Agent from a verified release.";
                }
                else
                {
                    using var reservation = ReserveAvailablePort();
                    var port = reservation.Port.ToString();
                    var auth = Guid.NewGuid().ToString("N");
                    var published = false;
                    try
                    {
                        launchSettings.LocalApiAuthToken = auth;
                        var env = AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(
                            launchSettings, port, auth, secretDirectory);
                        lock (LifecycleGate) { if (_disposed || epoch != _launchEpoch) return; }
                        // Factory and source-path checks run outside LifecycleGate.
                        start = AgentProcess.PrepareStart(binaryPath, env, expectedGeneration);
                        lock (LifecycleGate)
                        {
                            if (_disposed || epoch != _launchEpoch)
                            {
                                abandonedStop = AgentProcess.PrepareStop();
                            }
                            else if (start != null)
                            {
                                _localApiPort = port;
                                _localApiAuthToken = auth;
                                _activeSecretDirectory = secretDirectory;
                                _activeLaunchSettings = launchSettings;
                                _activeLaunchCompletesEnrollment = completesEnrollment;
                                _activeLaunchGeneration = AgentProcess.Generation;
                                published = true;
                            }
                        }
                    }
                    finally
                    {
                        if (!published)
                        {
                            var authPath = Path.Combine(secretDirectory, "local-api-auth-token");
                            if (File.Exists(authPath) && File.ReadAllText(authPath).Trim() == auth)
                                SecureFile.DeleteIfExists(authPath);
                            if (launchSettings.LocalApiAuthToken == auth) launchSettings.LocalApiAuthToken = string.Empty;
                        }
                    }
                }
            }
        }
        finally
        {
            lock (LifecycleGate) { if (_preparingEpoch == epoch) _preparingEpoch = null; }
            // Every successful reservation must be completed, even if stop or
            // dispose cancelled it during preparation. Cancelled starts skip OS launch.
            if (start != null)
            {
                try { if (abandonedStop == null) _killOrphanedAgents(binaryPath!); }
                catch (Exception)
                {
                    abandonedStop = AgentProcess.PrepareStop();
                    error = "The previous state-owned agent could not be recovered safely.";
                }
                finally { start(); if (abandonedStop != null) _ = abandonedStop(); }
            }
        }
        if (error != null) AgentProcess.ReportError(error);
    }

    public async Task StopAgentAsync()
    {
        long generation, epoch;
        Action notify;
        Func<Task> stop;
        lock (LifecycleGate)
        {
            generation = _activeLaunchGeneration;
            epoch = ++_launchEpoch;
            _preparingEpoch = null;
            _crashRestartCancellation.Cancel();
            notify = ApiClient.StopPollingState();
            stop = AgentProcess.PrepareStop();
        }
        try { notify(); }
        finally { await stop(); }
        lock (_launchFilesGate)
        {
            AgentSettings? settings;
            string? directory;
            lock (LifecycleGate)
            {
                if (_activeLaunchGeneration != generation || _launchEpoch != epoch || AgentProcess.IsRunning || AgentProcess.IsStarting) return;
                settings = _activeLaunchSettings;
                directory = _activeSecretDirectory;
                _activeLaunchSettings = null;
                _activeSecretDirectory = null;
                _activeLaunchCompletesEnrollment = false;
                _localApiAuthToken = null;
                _localApiPort = null;
            }
            if (settings != null) settings.LocalApiAuthToken = string.Empty;
            if (!string.IsNullOrWhiteSpace(directory)) SecureFile.DeleteIfExists(Path.Combine(directory, "local-api-auth-token"));
        }
    }

    /// <summary>
    /// Restart the agent with current settings.
    /// </summary>
    public async Task RestartAgentAsync()
    {
        var generation = AgentProcess.Generation;
        await StopAgentAsync();
        StartAgent(generation);
    }

    private void OnAgentStarted(long generation)
    {
        string? port, authToken, secretDirectory;
        AgentSettings? launchSettings;
        bool completesEnrollment;
        Action? notify = null;
        lock (LifecycleGate)
        {
            if (_disposed || generation != _activeLaunchGeneration || !AgentProcess.IsRunning) return;
            port = _localApiPort;
            authToken = _localApiAuthToken;
            launchSettings = _activeLaunchSettings;
            secretDirectory = _activeSecretDirectory;
            completesEnrollment = _activeLaunchCompletesEnrollment;
            if (port != null && authToken != null) notify = ApiClient.ConfigureState(port, authToken);
        }
        notify?.Invoke();
        if (port != null && authToken != null && launchSettings != null && secretDirectory != null)
        {
            _ = StartPollingWhenAgentReadyAsync(
                port,
                authToken,
                launchSettings,
                secretDirectory,
                completesEnrollment);
        }
    }

    private async Task StartPollingWhenAgentReadyAsync(
        string port,
        string authToken,
        AgentSettings launchSettings,
        string secretDirectory,
        bool completesEnrollment)
    {
        // Brief delay for the agent to start its HTTP server.
        await Task.Delay(1000);
        Action notify;
        lock (LifecycleGate)
        {
            if (!OwnsLaunch(port, authToken)) return;
            notify = ApiClient.StartPollingState();
        }
        notify();
        if (!completesEnrollment)
            return;

        // Enrollment may outlive local API startup, especially while the Hub
        // is recovering. Wait a bounded interval for both the local status API
        // and the durable credential rather than checking only once and
        // retaining consumed enrollment intent forever on a slow first run.
        var enrollmentPending = !string.IsNullOrWhiteSpace(launchSettings.EnrollmentToken);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            lock (LifecycleGate) { if (!OwnsLaunch(port, authToken)) return; }
            var info = await ApiClient.FetchInfoAsync();
            bool completed, removedEnrollmentToken = false, removedGroupIntent = false;
            long generation;
            string? diagnostic = null;
            lock (_launchFilesGate)
            {
                lock (LifecycleGate)
                {
                    if (!OwnsLaunch(port, authToken)) return;
                    generation = _activeLaunchGeneration;
                }
                var hasPersistedAgentToken = SecureFile.IsPrivateRegularFile(Path.Combine(secretDirectory, "agent-token"));
                completed = info != null && (!enrollmentPending || hasPersistedAgentToken);
                if (completed)
                {
                    removedEnrollmentToken = launchSettings.ClearConsumedEnrollmentTokenPreservingAgentToken(CredentialStore);
                    removedGroupIntent = TryClearCompletedEnrollmentGroupIntent(out diagnostic);
                }
            }
            if (diagnostic != null) AgentProcess.LogReader.AppendRaw(diagnostic);
            if (removedEnrollmentToken)
                AgentProcess.LogReader.AppendRaw("Removed consumed enrollment credential after durable agent-token persistence.");
            if (removedGroupIntent)
            {
                AgentProcess.LogReader.AppendRaw("Removed one-time enrollment group intent; restarting against the Hub's canonical placement.");
                await RestartLaunchIfCurrentAsync(generation);
            }
            if (completed) return;

            await Task.Delay(500);
        }
    }

    private bool OwnsLaunch(string port, string authToken) =>
        !_disposed && _localApiPort == port && _localApiAuthToken == authToken && AgentProcess.IsRunning;

    private async Task RestartLaunchIfCurrentAsync(long generation)
    {
        Func<Task> stop;
        Action notify;
        lock (LifecycleGate)
        {
            if (_disposed || _activeLaunchGeneration != generation) return;
            notify = ApiClient.StopPollingState();
            stop = AgentProcess.PrepareStop();
        }
        notify();
        await stop();
        StartAgent(generation);
    }

    private bool TryClearCompletedEnrollmentGroupIntent(out string? diagnostic)
    {
        diagnostic = null;
        var hadDurableGroupIntent =
            Settings.HasPersistedAgentToken && !string.IsNullOrWhiteSpace(Settings.GroupId);
        if (!hadDurableGroupIntent)
            return false;

        try
        {
            return Settings.ClearPersistedGroupIntentAfterEnrollment();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            PlatformNotSupportedException or
            System.Security.SecurityException)
        {
            diagnostic = $"Could not persist the completed-enrollment group migration: {ex.Message}";
            // ClearPersistedGroupIntentAfterEnrollment clears memory before
            // its atomic save. Restarting is still safe and prevents this
            // process from replaying stale group intent; the next launch will
            // retry the on-disk migration.
            return string.IsNullOrWhiteSpace(Settings.GroupId);
        }
    }

    private void OnAgentExited(AgentProcessExit exited)
    {
        Action notify;
        CancellationTokenSource? restartCts = null;
        TimeSpan delay = default;
        lock (LifecycleGate)
        {
            if (_disposed || exited.Generation != _activeLaunchGeneration) return;
            notify = ApiClient.StopPollingState();
            if (!_setupTransactionActive && exited.ExitCode != 0 && !exited.UserInitiated)
            {
                delay = AgentProcess.CrashCoordinator.NextDelay();
                restartCts = _crashRestartCancellation.Begin();
            }
        }
        notify();
        if (restartCts != null) _ = RestartAfterExitAsync(exited.Generation, delay, restartCts);
    }

    private async Task RestartAfterExitAsync(long generation, TimeSpan delay, CancellationTokenSource restartCts)
    {
        var token = restartCts.Token;
        try
        {
            await Task.Delay(delay, token);
            lock (LifecycleGate)
            {
                if (_disposed || token.IsCancellationRequested ||
                    !_crashRestartCancellation.IsCurrent(restartCts) ||
                    AgentProcess.Generation != generation || AgentProcess.IsRunning || AgentProcess.LastExitWasUserInitiated) return;
                _crashRestartCancellation.ClearIfCurrent(restartCts);
            }
            StartAgent(generation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            _crashRestartCancellation.ClearIfCurrent(restartCts);
            restartCts.Dispose();
        }
    }

    internal static string? FindAgentBinary(string? appBaseDirectory = null)
    {
        var appDir = Path.GetFullPath(appBaseDirectory ?? AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(appDir, "Assets", "labtether-agent.exe"),
            Path.Combine(appDir, "labtether-agent.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static PortReservation ReserveAvailablePort()
    {
        return new PortReservation();
    }

    private sealed class PortReservation : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;
        private bool _disposed;

        public PortReservation()
        {
            _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
            {
                ExclusiveAddressUse = true
            };
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listener.Stop();
        }
    }

    internal static string CurrentAppVersion =>
        typeof(AppState).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public void Dispose()
    {
        lock (LifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            _launchEpoch++;
            _preparingEpoch = null;
        }
        NetworkChange.NetworkAvailabilityChanged -= _networkAvailabilityChangedHandler;
        AgentProcess.OnOwnedExited -= OnAgentExited;
        AgentProcess.OnOwnedStarted -= OnAgentStarted;
        _crashRestartCancellation.Dispose();
        ApiClient.Dispose();
        AgentProcess.Dispose();
    }
}
