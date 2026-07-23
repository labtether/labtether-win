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

    private bool _isConnected;
    private string _connectionState = "Disconnected";
    private double _cpuPercent;
    private string _memoryText = "--";
    private double _diskPercent;
    private string _hubUrl = "--";
    private string _uptime = "--";

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
