using CommunityToolkit.Mvvm.ComponentModel;
using LabTetherAgent.Api;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// Status, metrics and alerts for a visible window (flyout or pop-out).
/// Polls faster while the window is open and shows the last known status at once.
/// </summary>
public class AgentStatusViewModel : ObservableObject, IDisposable
{
    private readonly LocalApiClient _apiClient;
    private readonly SynchronizationContext? _uiContext;
    private readonly Action<AgentStatus> _statusUpdatedHandler;
    private readonly Action<bool> _connectionStateHandler;
    private IDisposable? _visibleScope;
    private bool _disposed;
    private AgentStatus? _status;
    private bool _apiConnected;

    private string _hubUrl = "";
    private string? _startProblem;
    private AgentHeader _header = new("Offline", "", true);
    private double _cpuPercent;
    private double _memoryPercent;
    private double _diskPercent;
    private List<AlertRow> _alerts = [];

    public string HubUrl
    {
        get => _hubUrl;
        set
        {
            if (SetProperty(ref _hubUrl, value))
                Refresh();
        }
    }

    /// <summary>Why the agent could not be started; shown instead of the Hub while it is stopped.</summary>
    public string? StartProblem
    {
        get => _startProblem;
        set
        {
            if (SetProperty(ref _startProblem, value))
                Refresh();
        }
    }

    public string StatusWord => _header.StatusWord;
    public string Detail => _header.Detail;
    public bool IsProblem => _header.IsProblem;
    public bool IsConnected => _status?.IsConnected == true && _apiConnected;

    public double CpuPercent { get => _cpuPercent; private set => SetProperty(ref _cpuPercent, value); }
    public double MemoryPercent { get => _memoryPercent; private set => SetProperty(ref _memoryPercent, value); }
    public double DiskPercent { get => _diskPercent; private set => SetProperty(ref _diskPercent, value); }

    public List<AlertRow> Alerts
    {
        get => _alerts;
        private set
        {
            if (SetProperty(ref _alerts, value))
                OnPropertyChanged(nameof(HasAlerts));
        }
    }

    public bool HasAlerts => _alerts.Count > 0;

    public AgentStatusViewModel(LocalApiClient apiClient)
    {
        _apiClient = apiClient;
        _uiContext = SynchronizationContext.Current;
        _status = apiClient.LatestStatus;
        _apiConnected = apiClient.IsConnected;
        _statusUpdatedHandler = status => RunOnUiThread(() =>
        {
            _status = status;
            Refresh();
        });
        _connectionStateHandler = connected => RunOnUiThread(() =>
        {
            _apiConnected = connected;
            Refresh();
        });
        _apiClient.OnStatusUpdated += _statusUpdatedHandler;
        _apiClient.OnConnectionStateChanged += _connectionStateHandler;
        Refresh();
    }

    public void UpdateFromStatus(AgentStatus status) => RunOnUiThread(() =>
    {
        _status = status;
        _apiConnected = status.IsConnected;
        Refresh();
    });

    public void OnWindowOpened()
    {
        if (_disposed) return;
        _visibleScope ??= _apiClient.EnterVisibleScope();
    }

    public void OnWindowClosed() => Dispose();

    private void Refresh()
    {
        // A failed poll reports "not connected" without a new status. The last
        // status then still says connected, so the header must not trust it.
        var current = _status is { IsConnected: true } && !_apiConnected ? null : _status;
        _header = AgentStatusText.Header(current, HubUrl, DateTimeOffset.Now, StartProblem);
        OnPropertyChanged(nameof(StatusWord));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsProblem));
        OnPropertyChanged(nameof(IsConnected));
        CpuPercent = _status?.CpuPercent ?? 0;
        MemoryPercent = AgentStatusText.MemoryPercent(_status);
        DiskPercent = _status?.DiskPercent ?? 0;
        Alerts = AgentStatusText.AlertRows(_status, DateTimeOffset.Now);
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
