using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the main flyout window.
/// Binds to AgentStatus from the LocalApiClient polling loop.
/// </summary>
public partial class FlyoutViewModel : ObservableObject
{
    private readonly LocalApiClient _apiClient;

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
        _apiClient.OnStatusUpdated += UpdateFromStatus;
        _apiClient.OnConnectionStateChanged += connected =>
        {
            IsConnected = connected;
            ConnectionState = connected ? "Connected" : "Disconnected";
        };
    }

    public void UpdateFromStatus(AgentStatus status)
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

    public void OnFlyoutOpened()
    {
        _apiClient.SetVisible(true);
    }

    public void OnFlyoutClosed()
    {
        _apiClient.SetVisible(false);
    }

    [RelayCommand]
    private void OpenConsole()
    {
        if (!string.IsNullOrEmpty(_hubUrl) && _hubUrl != "--")
        {
            var consoleUrl = _hubUrl
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)
                .Replace("/ws/agent", "", StringComparison.OrdinalIgnoreCase);
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
        catch { }
    }
}
