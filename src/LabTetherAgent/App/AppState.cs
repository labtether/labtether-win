using LabTetherAgent.Api;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.App;

/// <summary>
/// Global application state container. Owns all core services and coordinates lifecycle.
/// Mirrors mac-agent/Sources/LabTetherAgent/App/AppState.swift.
/// </summary>
public class AppState : IDisposable
{
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
    private bool _disposed;

    private AppState()
    {
        CredentialStore = new CredentialStore();
        Settings = AgentSettings.Load();
        CredentialStore.LoadInto(Settings);

        AgentProcess = new AgentProcess();
        ApiClient = new LocalApiClient();
        ConnectionTester = new ConnectionTester();

        var version = ReadAgentVersion();
        UpdateChecker = new UpdateChecker(version);

        // Wire crash restart
        AgentProcess.OnExited += OnAgentExited;
        AgentProcess.OnStarted += OnAgentStarted;

        // Wire network change monitoring for immediate poll on reconnect
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, args) =>
        {
            if (args.IsAvailable)
                ApiClient.PollNow();
        };
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

        var binaryPath = FindAgentBinary();
        if (binaryPath == null)
        {
            // Binary not found — can't start
            return;
        }

        // Keep the local API port reserved until the agent is about to start to
        // minimize the handoff window where another process can claim it.
        using var localApiReservation = ReserveAvailablePort();
        _localApiPort = localApiReservation.Port.ToString();
        _localApiAuthToken = Guid.NewGuid().ToString("N");

        Settings.LocalApiAuthToken = _localApiAuthToken;

        var env = AgentEnvironmentBuilder.BuildEnvironment(Settings, _localApiPort, _localApiAuthToken);

        AgentProcess.KillOrphanedAgents(binaryPath);
        localApiReservation.Dispose();
        AgentProcess.Start(binaryPath, env);
    }

    /// <summary>
    /// Stop the agent process.
    /// </summary>
    public async Task StopAgentAsync()
    {
        ApiClient.StopPolling();
        await AgentProcess.StopAsync();
    }

    /// <summary>
    /// Restart the agent with current settings.
    /// </summary>
    public async Task RestartAgentAsync()
    {
        await StopAgentAsync();
        StartAgent();
    }

    private void OnAgentStarted()
    {
        if (_localApiPort != null && _localApiAuthToken != null)
        {
            ApiClient.Configure(_localApiPort, _localApiAuthToken);
            // Brief delay for the agent to start its HTTP server
            Task.Delay(1000).ContinueWith(_ =>
            {
                ApiClient.StartPolling();
                _ = ApiClient.FetchInfoAsync();
            });
        }
    }

    private async void OnAgentExited(int exitCode)
    {
        ApiClient.StopPolling();

        if (_disposed || exitCode == 0 || AgentProcess.LastExitWasUserInitiated)
            return;

        // Crash — wait for backoff delay then restart
        var delay = AgentProcess.CrashCoordinator.NextDelay();
        AgentProcess.LogReader.AppendRaw($"Crash detected, restarting in {delay.TotalSeconds:F0}s (attempt {AgentProcess.CrashCoordinator.AttemptCount})");
        await Task.Delay(delay);
        StartAgent();
    }

    private static string? FindAgentBinary()
    {
        // Look for labtether-agent.exe in the app directory
        var appDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "labtether-agent.exe"),
            Path.Combine(appDir, "Assets", "labtether-agent.exe"),
            Path.Combine(appDir, "..", "labtether-agent.exe"),
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

    private static string ReadAgentVersion()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(dir, "AGENT_VERSION");
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return "0.0.0";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ApiClient.Dispose();
        AgentProcess.Dispose();
    }
}
