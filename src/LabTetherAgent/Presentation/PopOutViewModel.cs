using CommunityToolkit.Mvvm.ComponentModel;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the pop-out (always-on-top) metrics window.
/// Shares the same status data as the flyout but in a persistent window.
/// </summary>
public partial class PopOutViewModel : ObservableObject, IDisposable
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

    public PopOutViewModel(LocalApiClient apiClient)
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

    private void UpdateFromStatus(AgentStatus status)
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

    public void OnWindowOpened()
    {
        if (_disposed) return;
        _visibleScope ??= _apiClient.EnterVisibleScope();
    }

    public void OnWindowClosed()
    {
        Dispose();
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
