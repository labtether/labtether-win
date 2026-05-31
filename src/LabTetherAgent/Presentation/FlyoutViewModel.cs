using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Api;
using LabTetherAgent.Settings;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the main flyout window.
/// Binds to AgentStatus from the LocalApiClient polling loop.
/// </summary>
public partial class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly LocalApiClient _apiClient;
    private readonly SynchronizationContext? _uiContext;
    private readonly Action<AgentStatus> _statusUpdatedHandler;
    private readonly Action<bool> _connectionStateHandler;
    private IDisposable? _visibleScope;
    private bool _disposed;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionState = "Disconnected";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _memoryText = "--";
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private string _hubUrl = "--";
    [ObservableProperty] private string _uptime = "--";
    [ObservableProperty] private List<AlertSnapshot> _firingAlerts = [];
    [ObservableProperty] private bool _hasAlerts;
    [ObservableProperty] private HyperVStatus? _hyperVStatus;
    [ObservableProperty] private WindowsUpdateStatus? _windowsUpdateStatus;
    [ObservableProperty] private bool _hasHyperV;
    [ObservableProperty] private bool _hasWindowsUpdates;

    public FlyoutViewModel(LocalApiClient apiClient)
    {
        _apiClient = apiClient;
        _uiContext = SynchronizationContext.Current;
        _statusUpdatedHandler = status => RunOnUiThread(() => ApplyStatus(status));
        _connectionStateHandler = connected =>
        {
            RunOnUiThread(() =>
            {
                IsConnected = connected;
                ConnectionState = connected ? "Connected" : "Disconnected";
            });
        };
        _apiClient.OnStatusUpdated += _statusUpdatedHandler;
        _apiClient.OnConnectionStateChanged += _connectionStateHandler;
    }

    public void UpdateFromStatus(AgentStatus status)
    {
        RunOnUiThread(() => ApplyStatus(status));
    }

    private void ApplyStatus(AgentStatus status)
    {
        IsConnected = status.IsConnected;
        ConnectionState = status.IsConnected ? "Connected" : "Disconnected";
        CpuPercent = status.CpuPercent;
        MemoryText = status.MemoryDisplayText;
        DiskPercent = status.DiskPercent;
        Uptime = status.Uptime ?? "--";
        FiringAlerts = status.FiringAlerts;
        HasAlerts = status.FiringAlerts.Count > 0;

        // Windows-exclusive cards
        HyperVStatus = status.HyperV;
        HasHyperV = status.HyperV != null;
        WindowsUpdateStatus = status.WindowsUpdate;
        HasWindowsUpdates = status.WindowsUpdate != null;
    }

    private void RunOnUiThread(Action update)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            update();
            return;
        }

        _uiContext.Post(_ => update(), null);
    }

    public void OnFlyoutOpened()
    {
        if (_disposed) return;
        _visibleScope ??= _apiClient.EnterVisibleScope();
    }

    public void OnFlyoutClosed()
    {
        Dispose();
    }

    [RelayCommand]
    private void OpenConsole()
    {
        if (!string.IsNullOrEmpty(_hubUrl) && _hubUrl != "--")
        {
            var consoleUrl = SettingsValidator.DeriveApiBaseUrl(_hubUrl);
            if (!string.IsNullOrEmpty(consoleUrl))
                OpenUrl(consoleUrl);
        }
    }

    [RelayCommand]
    private void CopyHubUrl()
    {
        // Platform-specific clipboard — will be wired in XAML code-behind
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (InvalidOperationException ex)
        {
            LogOpenUrlFailure(url, ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LogOpenUrlFailure(url, ex);
        }
        catch (PlatformNotSupportedException ex)
        {
            LogOpenUrlFailure(url, ex);
        }
    }

    private static void LogOpenUrlFailure(string url, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to open URL '{url}': {ex.GetType().Name}: {ex.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visibleScope?.Dispose();
        _visibleScope = null;
        _apiClient.OnStatusUpdated -= _statusUpdatedHandler;
        _apiClient.OnConnectionStateChanged -= _connectionStateHandler;
    }
}
