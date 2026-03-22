using CommunityToolkit.Mvvm.ComponentModel;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the pop-out (always-on-top) metrics window.
/// Shares the same status data as the flyout but in a persistent window.
/// </summary>
public partial class PopOutViewModel : ObservableObject
{
    private readonly LocalApiClient _apiClient;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionState = "Disconnected";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _memoryText = "--";
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private string _hubUrl = "--";
    [ObservableProperty] private string _uptime = "--";

    public PopOutViewModel(LocalApiClient apiClient)
    {
        _apiClient = apiClient;
        _apiClient.OnStatusUpdated += UpdateFromStatus;
        _apiClient.OnConnectionStateChanged += connected =>
        {
            IsConnected = connected;
            ConnectionState = connected ? "Connected" : "Disconnected";
        };
    }

    private void UpdateFromStatus(AgentStatus status)
    {
        IsConnected = status.IsConnected;
        ConnectionState = status.IsConnected ? "Connected" : "Disconnected";
        CpuPercent = status.CpuPercent;
        MemoryText = status.MemoryDisplayText;
        DiskPercent = status.DiskPercent;
        Uptime = status.Uptime ?? "--";
    }

    public void OnWindowOpened()
    {
        _apiClient.SetVisible(true);
    }

    public void OnWindowClosed()
    {
        _apiClient.SetVisible(false);
    }
}
