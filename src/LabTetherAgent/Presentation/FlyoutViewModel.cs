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

    private bool _isConnected;
    private string _connectionState = "Disconnected";
    private double _cpuPercent;
    private string _memoryText = "--";
    private double _diskPercent;
    private string _hubUrl = "--";
    private string _uptime = "--";
    private List<AlertSnapshot> _firingAlerts = [];
    private bool _hasAlerts;
    private HyperVStatus? _hyperVStatus;
    private WindowsUpdateStatus? _windowsUpdateStatus;
    private bool _hasHyperV;
    private bool _hasWindowsUpdates;

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public string ConnectionState
    {
        get => _connectionState;
        set => SetProperty(ref _connectionState, value);
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        set => SetProperty(ref _cpuPercent, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        set => SetProperty(ref _memoryText, value);
    }

    public double DiskPercent
    {
        get => _diskPercent;
        set => SetProperty(ref _diskPercent, value);
    }

    public string HubUrl
    {
        get => _hubUrl;
        set => SetProperty(ref _hubUrl, value);
    }

    public string Uptime
    {
        get => _uptime;
        set => SetProperty(ref _uptime, value);
    }

    public List<AlertSnapshot> FiringAlerts
    {
        get => _firingAlerts;
        set => SetProperty(ref _firingAlerts, value);
    }

    public bool HasAlerts
    {
        get => _hasAlerts;
        set => SetProperty(ref _hasAlerts, value);
    }

    public HyperVStatus? HyperVStatus
    {
        get => _hyperVStatus;
        set => SetProperty(ref _hyperVStatus, value);
    }

    public WindowsUpdateStatus? WindowsUpdateStatus
    {
        get => _windowsUpdateStatus;
        set => SetProperty(ref _windowsUpdateStatus, value);
    }

    public bool HasHyperV
    {
        get => _hasHyperV;
        set => SetProperty(ref _hasHyperV, value);
    }

    public bool HasWindowsUpdates
    {
        get => _hasWindowsUpdates;
        set => SetProperty(ref _hasWindowsUpdates, value);
    }

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
        ConnectionState = status.ConnectionDisplayText;
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
        if (!string.IsNullOrEmpty(HubUrl) && HubUrl != "--")
        {
            var consoleUrl = SettingsValidator.DeriveApiBaseUrl(HubUrl);
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
