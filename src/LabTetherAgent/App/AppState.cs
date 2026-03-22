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
        var binaryPath = FindAgentBinary();
        if (binaryPath == null)
        {
            // Binary not found — can't start
            return;
        }

        // Generate a random local API port and auth token for this session
        _localApiPort = FindAvailablePort().ToString();
        _localApiAuthToken = Guid.NewGuid().ToString("N");

        Settings.LocalApiAuthToken = _localApiAuthToken;

        var env = AgentEnvironmentBuilder.BuildEnvironment(Settings, _localApiPort, _localApiAuthToken);

        AgentProcess.KillOrphanedAgents(binaryPath);
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

        if (exitCode != 0)
        {
            // Crash — wait for backoff delay then restart
            var delay = AgentProcess.CrashCoordinator.NextDelay();
            await Task.Delay(delay);
            StartAgent();
        }
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

    private static int FindAvailablePort()
    {
        // Bind to port 0 to get an available port from the OS
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
