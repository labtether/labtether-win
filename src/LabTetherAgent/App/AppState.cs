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
public class AppState : IDisposable
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

    private AppState()
    {
        CredentialStore = new CredentialStore();
        Settings = AgentSettings.Load();
        CredentialStore.LoadInto(Settings);

        AgentProcess = new AgentProcess();
        ApiClient = new LocalApiClient();
        ConnectionTester = new ConnectionTester();

        // Native-wrapper releases have their own version stream. The bundled
        // Go child's AGENT_VERSION is intentionally separate and self-updates
        // through the hub.
        UpdateChecker = new UpdateChecker(CurrentAppVersion);

        // Wire crash restart
        AgentProcess.OnExited += OnAgentExited;
        AgentProcess.OnStarted += OnAgentStarted;

        // Wire network change monitoring for immediate poll on reconnect
        _networkAvailabilityChangedHandler = (_, args) =>
        {
            if (!_disposed && args.IsAvailable)
                ApiClient.PollNow();
        };
        NetworkChange.NetworkAvailabilityChanged += _networkAvailabilityChangedHandler;
    }

    public static AppState Initialize()
    {
        _instance = new AppState();
        return _instance;
    }

    /// <summary>
    /// Start the agent process with current settings.
    /// </summary>
    public void StartAgent()
    {
        if (_disposed)
            return;

        _crashRestartCancellation.Cancel();

        // Older wrapper versions persisted onboarding's group field and sent
        // it on every durable-token heartbeat. Migrate that one-time intent
        // before constructing the child environment. Even if persistence is
        // temporarily unavailable, the in-memory clear keeps this launch from
        // replaying a stale group and a future launch will retry the migration.
        TryClearCompletedEnrollmentGroupIntent();

        StartAgentWithSettings(
            Settings,
            AgentSettings.GetSettingsDirectory(),
            completesEnrollment: true);
    }

    private void StartAgentWithSettings(
        AgentSettings launchSettings,
        string secretDirectory,
        bool completesEnrollment)
    {
        ArgumentNullException.ThrowIfNull(launchSettings);
        if (_disposed)
            return;

        var binaryPath = FindAgentBinary();
        if (binaryPath == null)
        {
            AgentProcess.ReportError(
                "The bundled LabTether agent core is missing. Reinstall LabTether Agent from a verified release."
            );
            return;
        }

        // Keep the local API port reserved until the agent is about to start to
        // minimize the handoff window where another process can claim it.
        using var localApiReservation = ReserveAvailablePort();
        _localApiPort = localApiReservation.Port.ToString();
        _localApiAuthToken = Guid.NewGuid().ToString("N");

        launchSettings.LocalApiAuthToken = _localApiAuthToken;

        var env = AgentEnvironmentBuilder.BuildEnvironmentForTrustedDirectory(
            launchSettings,
            _localApiPort,
            _localApiAuthToken,
            secretDirectory);

        _activeSecretDirectory = secretDirectory;
        _activeLaunchSettings = launchSettings;
        _activeLaunchCompletesEnrollment = completesEnrollment;

        AgentProcess.KillOrphanedAgents(binaryPath);
        localApiReservation.Dispose();
        AgentProcess.Start(binaryPath, env);
    }

    /// <summary>
    /// Stop the agent process.
    /// </summary>
    public async Task StopAgentAsync()
    {
        _crashRestartCancellation.Cancel();
        ApiClient.StopPolling();
        await AgentProcess.StopAsync();
        var activeSettings = _activeLaunchSettings;
        if (activeSettings != null)
            activeSettings.LocalApiAuthToken = string.Empty;
        var activeSecretDirectory = _activeSecretDirectory;
        if (!string.IsNullOrWhiteSpace(activeSecretDirectory))
        {
            SecureFile.DeleteIfExists(Path.Combine(activeSecretDirectory, "local-api-auth-token"));
        }
        _activeLaunchSettings = null;
        _activeSecretDirectory = null;
        _activeLaunchCompletesEnrollment = false;
        _localApiAuthToken = null;
        _localApiPort = null;
    }

    /// <summary>
    /// Restart the agent with current settings.
    /// </summary>
    public async Task RestartAgentAsync()
    {
        await StopAgentAsync();
        StartAgent();
    }

    /// <summary>
    /// Starts the Go child for setup and waits for real authenticated Hub
    /// connectivity. Enrollment setup additionally requires the one-use
    /// credential to be replaced by a durable agent token and removed from the
    /// wrapper store before success is reported.
    /// </summary>
    internal async Task<AgentConnectionAttemptResult> ConnectAgentForSetupAsync(
        AgentSettings candidate,
        bool requiresDurableEnrollment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_setupTransactionActive)
        {
            return AgentConnectionAttemptResult.Failed(
                "Another setup attempt is already running. Wait for it to finish and try again.");
        }

        _setupTransactionActive = true;
        var priorWasRunning = AgentProcess.IsRunning;
        var persistenceTouched = false;
        var stagingDirectory = Path.Combine(
            AgentSettings.GetSettingsDirectory(),
            $".setup-{Guid.NewGuid():N}");
        byte[]? stagedBearer = null;

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using var snapshot = SetupPersistenceSnapshot.Capture(Settings, CredentialStore);

            return await AgentSetupTransaction.ExecuteAsync(
                attempt: async token =>
                {
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();
                    token.ThrowIfCancellationRequested();
                    return await RunSetupCandidateAsync(
                        candidate,
                        requiresDurableEnrollment,
                        stagingDirectory,
                        token);
                },
                commit: async token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();
                    token.ThrowIfCancellationRequested();

                    var stagedBearerPath = Path.Combine(stagingDirectory, "agent-token");
                    if (!SecureFile.IsPrivateRegularFile(stagedBearerPath))
                    {
                        throw new InvalidOperationException(
                            "The staged setup did not produce a protected durable credential.");
                    }

                    stagedBearer = File.ReadAllBytes(stagedBearerPath);
                    if (stagedBearer.Length == 0)
                        throw new InvalidOperationException("The staged durable credential was empty.");

                    // Remove every staged copy, including the one-use token,
                    // before publishing any replacement state.
                    DeleteSetupDirectory(stagingDirectory);
                    token.ThrowIfCancellationRequested();

                    var committed = candidate.CloneForSetup();
                    committed.LocalApiAuthToken = string.Empty;
                    if (requiresDurableEnrollment)
                    {
                        committed.EnrollmentToken = string.Empty;
                        committed.GroupId = string.Empty;
                    }

                    persistenceTouched = true;
                    PersistCommittedSetup(committed, stagedBearer);
                    token.ThrowIfCancellationRequested();

                    StartAgent();
                    if (!AgentProcess.IsRunning)
                    {
                        throw new InvalidOperationException(
                            "The committed agent core did not start.");
                    }
                },
                rollback: async () =>
                {
                    if (AgentProcess.IsRunning)
                        await StopAgentAsync();

                    if (persistenceTouched)
                        snapshot.Restore(Settings, CredentialStore);

                    DeleteSetupDirectory(stagingDirectory);
                    if (priorWasRunning)
                    {
                        StartAgent();
                        if (!AgentProcess.IsRunning)
                            throw new InvalidOperationException("The previous agent core could not be restarted.");
                    }
                },
                cancellationToken);
        }
        catch (Exception ex) when (AgentSetupTransaction.IsRecoverable(ex))
        {
            return AgentConnectionAttemptResult.Failed(
                "The staged setup could not be prepared securely. The active setup was not replaced. Check Windows permissions and try again.");
        }
        finally
        {
            if (stagedBearer != null)
                CryptographicOperations.ZeroMemory(stagedBearer);
            try
            {
                DeleteSetupDirectory(stagingDirectory);
            }
            catch (Exception ex) when (AgentSetupTransaction.IsRecoverable(ex))
            {
                AgentProcess.LogReader.AppendRaw(
                    "Could not remove the abandoned setup staging directory; no setup credentials were committed.");
            }
            _setupTransactionActive = false;
        }
    }

    private async Task<AgentConnectionAttemptResult> RunSetupCandidateAsync(
        AgentSettings candidate,
        bool requiresDurableEnrollment,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var terminalFailure = new TaskCompletionSource<AgentConnectionAttemptResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleStatus(AgentStatus status)
        {
            var message = AgentSetupStatusClassifier.TerminalFailureMessage(status);
            if (message != null)
                terminalFailure.TrySetResult(AgentConnectionAttemptResult.Failed(message));
        }

        void HandleStartError(string _)
        {
            terminalFailure.TrySetResult(AgentConnectionAttemptResult.Failed(
                "The agent core could not be started. Reinstall LabTether Agent from a verified release and try again."));
        }

        ApiClient.OnStatusUpdated += HandleStatus;
        AgentProcess.OnError += HandleStartError;
        try
        {
            StartAgentWithSettings(candidate, stagingDirectory, completesEnrollment: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SetupConnectionTimeout);

            while (true)
            {
                if (terminalFailure.Task.IsCompleted)
                    return await terminalFailure.Task;

                var durableCredentialReady = !requiresDurableEnrollment ||
                    SecureFile.IsPrivateRegularFile(Path.Combine(stagingDirectory, "agent-token"));
                if (ApiClient.IsConnected && durableCredentialReady)
                    return AgentConnectionAttemptResult.Connected();

                if (!AgentProcess.IsRunning && !AgentProcess.IsStarting)
                {
                    return AgentConnectionAttemptResult.Failed(
                        "The agent core exited before setup established an authenticated Hub connection. Check the agent logs and try again.");
                }

                try
                {
                    var readinessDelay = Task.Delay(SetupReadinessPollInterval, timeout.Token);
                    var completed = await Task.WhenAny(terminalFailure.Task, readinessDelay);
                    if (completed == terminalFailure.Task)
                        return await terminalFailure.Task;
                    await readinessDelay;
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return AgentConnectionAttemptResult.Failed(
                        "The agent started, but did not establish an authenticated, durable Hub connection. Check the token and agent logs, then try again.");
                }
            }
        }
        finally
        {
            ApiClient.OnStatusUpdated -= HandleStatus;
            AgentProcess.OnError -= HandleStartError;
        }
    }

    private void OnAgentStarted()
    {
        var port = _localApiPort;
        var authToken = _localApiAuthToken;
        var launchSettings = _activeLaunchSettings;
        var secretDirectory = _activeSecretDirectory;
        var completesEnrollment = _activeLaunchCompletesEnrollment;
        if (port != null && authToken != null && launchSettings != null && secretDirectory != null)
        {
            ApiClient.Configure(port, authToken);
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
        if (_disposed || _localApiPort != port || _localApiAuthToken != authToken || !AgentProcess.IsRunning)
            return;

        ApiClient.StartPolling();
        if (!completesEnrollment)
            return;

        // Enrollment may outlive local API startup, especially while the Hub
        // is recovering. Wait a bounded interval for both the local status API
        // and the durable credential rather than checking only once and
        // retaining consumed enrollment intent forever on a slow first run.
        var enrollmentPending = !string.IsNullOrWhiteSpace(launchSettings.EnrollmentToken);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (_disposed || _localApiPort != port || _localApiAuthToken != authToken || !AgentProcess.IsRunning)
                return;

            var info = await ApiClient.FetchInfoAsync();
            var hasPersistedAgentToken = SecureFile.IsPrivateRegularFile(
                Path.Combine(secretDirectory, "agent-token"));
            if (info != null && (!enrollmentPending || hasPersistedAgentToken))
            {
                var removedEnrollmentToken =
                    launchSettings.ClearConsumedEnrollmentTokenPreservingAgentToken(CredentialStore);
                if (removedEnrollmentToken)
                {
                    AgentProcess.LogReader.AppendRaw(
                        "Removed consumed enrollment credential after durable agent-token persistence.");
                }

                var removedGroupIntent = TryClearCompletedEnrollmentGroupIntent();
                if (removedGroupIntent)
                {
                    AgentProcess.LogReader.AppendRaw(
                        "Removed one-time enrollment group intent; restarting against the Hub's canonical placement.");
                    await RestartAgentAsync();
                }
                return;
            }

            await Task.Delay(500);
        }
    }

    private bool TryClearCompletedEnrollmentGroupIntent()
    {
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
            AgentProcess.LogReader.AppendRaw(
                $"Could not persist the completed-enrollment group migration: {ex.Message}");
            // ClearPersistedGroupIntentAfterEnrollment clears memory before
            // its atomic save. Restarting is still safe and prevents this
            // process from replaying stale group intent; the next launch will
            // retry the on-disk migration.
            return string.IsNullOrWhiteSpace(Settings.GroupId);
        }
    }

    private async void OnAgentExited(int exitCode)
    {
        ApiClient.StopPolling();

        if (_setupTransactionActive || exitCode == 0 || AgentProcess.LastExitWasUserInitiated)
            return;

        // Crash — wait for backoff delay then restart
        var delay = AgentProcess.CrashCoordinator.NextDelay();
        var restartCts = _crashRestartCancellation.Begin();
        var restartToken = restartCts.Token;
        AgentProcess.LogReader.AppendRaw($"Crash detected, restarting in {delay.TotalSeconds:F0}s (attempt {AgentProcess.CrashCoordinator.AttemptCount})");

        try
        {
            await Task.Delay(delay, restartToken);
            if (_disposed ||
                restartToken.IsCancellationRequested ||
                !_crashRestartCancellation.IsCurrent(restartCts) ||
                AgentProcess.IsRunning ||
                AgentProcess.LastExitWasUserInitiated)
            {
                return;
            }

            _crashRestartCancellation.ClearIfCurrent(restartCts);
            StartAgent();
        }
        catch (OperationCanceledException) when (restartToken.IsCancellationRequested)
        {
            // User stopped or manually restarted the agent before backoff expired.
        }
        finally
        {
            _crashRestartCancellation.ClearIfCurrent(restartCts);
            restartCts.Dispose();
        }
    }

    private void PersistCommittedSetup(AgentSettings committed, byte[] stagedBearer)
    {
        var settingsDirectory = AgentSettings.GetSettingsDirectory();
        SecureFile.WriteAllBytes(Path.Combine(settingsDirectory, "agent-token"), stagedBearer);
        SecureFile.DeleteIfExists(Path.Combine(settingsDirectory, "enrollment-token"));
        SecureFile.DeleteIfExists(Path.Combine(settingsDirectory, "enrollment-token.sha256"));

        committed.Save();
        CredentialStore.SaveFrom(committed);
        Settings.ApplyCommittedSetup(committed);
    }

    private static void DeleteSetupDirectory(string stagingDirectory)
    {
        var fullPath = Path.GetFullPath(stagingDirectory);
        var settingsDirectory = Path.GetFullPath(AgentSettings.GetSettingsDirectory());
        if (!string.Equals(Path.GetDirectoryName(fullPath), settingsDirectory, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullPath).StartsWith(".setup-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to remove an untrusted setup staging directory.");
        }

        if (!Directory.Exists(fullPath))
            return;

        var root = new DirectoryInfo(fullPath);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to remove a redirected setup staging directory.");

        foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to remove redirected setup state.");
        }
        Directory.Delete(fullPath, recursive: true);
    }

    private sealed class SetupPersistenceSnapshot : IDisposable
    {
        private readonly AgentSettings _settings;
        private readonly FileSnapshot[] _files;
        private readonly string? _apiToken;
        private readonly string? _enrollmentToken;
        private readonly string? _turnPassword;

        private SetupPersistenceSnapshot(
            AgentSettings settings,
            FileSnapshot[] files,
            string? apiToken,
            string? enrollmentToken,
            string? turnPassword)
        {
            _settings = settings;
            _files = files;
            _apiToken = apiToken;
            _enrollmentToken = enrollmentToken;
            _turnPassword = turnPassword;
        }

        public static SetupPersistenceSnapshot Capture(
            AgentSettings settings,
            CredentialStore credentialStore)
        {
            var directory = AgentSettings.GetSettingsDirectory();
            return new SetupPersistenceSnapshot(
                settings.CloneForSetup(),
                new[]
                {
                    FileSnapshot.Capture(AgentSettings.GetSettingsPath()),
                    FileSnapshot.Capture(Path.Combine(directory, "agent-token")),
                    FileSnapshot.Capture(Path.Combine(directory, "enrollment-token")),
                    FileSnapshot.Capture(Path.Combine(directory, "enrollment-token.sha256")),
                    FileSnapshot.Capture(Path.Combine(directory, "local-api-auth-token")),
                    FileSnapshot.Capture(Path.Combine(directory, "webrtc-turn-password")),
                },
                credentialStore.Retrieve(CredentialStore.ApiTokenResource),
                credentialStore.Retrieve(CredentialStore.EnrollmentTokenResource),
                credentialStore.Retrieve(CredentialStore.WebRtcTurnPassResource));
        }

        public void Restore(AgentSettings settings, CredentialStore credentialStore)
        {
            foreach (var file in _files)
                file.Restore();

            credentialStore.Store(CredentialStore.ApiTokenResource, _apiToken ?? string.Empty);
            credentialStore.Store(
                CredentialStore.EnrollmentTokenResource,
                _enrollmentToken ?? string.Empty);
            credentialStore.Store(
                CredentialStore.WebRtcTurnPassResource,
                _turnPassword ?? string.Empty);
            credentialStore.Remove(CredentialStore.LocalApiAuthResource);
            settings.ApplyCommittedSetup(_settings);
        }

        public void Dispose()
        {
            foreach (var file in _files)
                file.Dispose();
        }
    }

    private sealed class FileSnapshot : IDisposable
    {
        private readonly string _path;
        private readonly byte[]? _contents;

        private FileSnapshot(string path, byte[]? contents)
        {
            _path = path;
            _contents = contents;
        }

        public static FileSnapshot Capture(string path)
        {
            if (!File.Exists(path))
                return new FileSnapshot(path, null);

            var info = new FileInfo(path);
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Refusing to snapshot redirected setup state.");
            return new FileSnapshot(path, File.ReadAllBytes(path));
        }

        public void Restore()
        {
            if (_contents == null)
                SecureFile.DeleteIfExists(_path);
            else
                SecureFile.WriteAllBytes(_path, _contents);
        }

        public void Dispose()
        {
            if (_contents != null)
                CryptographicOperations.ZeroMemory(_contents);
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
        if (_disposed) return;
        _disposed = true;
        NetworkChange.NetworkAvailabilityChanged -= _networkAvailabilityChangedHandler;
        AgentProcess.OnExited -= OnAgentExited;
        AgentProcess.OnStarted -= OnAgentStarted;
        _crashRestartCancellation.Dispose();
        ApiClient.Dispose();
        AgentProcess.Dispose();
    }
}
