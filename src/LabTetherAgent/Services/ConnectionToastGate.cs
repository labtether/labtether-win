namespace LabTetherAgent.Services;

/// <summary>
/// Decides when a lost or restored Hub connection is worth a notification.
/// The app restarts the agent itself on Save in Settings, setup and
/// re-enroll; those gaps last seconds. "Offline since" waits until the
/// connection has been down for <see cref="Grace"/>, and "Reconnected" follows
/// only an "Offline since" that was shown. Without this every Save gave an
/// Offline and a Reconnected notification, and their five-minute throttle then
/// hid a real outage that started soon after.
/// </summary>
internal sealed class ConnectionToastGate
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(20);

    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action _showOffline;
    private readonly Action _showReconnected;
    private bool? _connected;
    private bool _offlineShown;
    private CancellationTokenSource? _pendingOffline;

    public ConnectionToastGate(Action showOffline, Action showReconnected,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _showOffline = showOffline;
        _showReconnected = showReconnected;
        _delay = delay ?? Task.Delay;
    }

    public void OnConnectionChanged(bool connected)
    {
        CancellationTokenSource? pending;
        bool showReconnected = false;
        lock (_gate)
        {
            var previous = _connected;
            _connected = connected;
            if (previous == null || previous == connected)
                return;
            _pendingOffline?.Cancel();
            _pendingOffline = null;
            if (connected)
            {
                showReconnected = _offlineShown;
                _offlineShown = false;
                pending = null;
            }
            else
            {
                pending = _pendingOffline = new CancellationTokenSource();
            }
        }

        if (showReconnected)
            _showReconnected();
        if (pending != null)
            _ = ShowOfflineAfterGraceAsync(pending);
    }

    private async Task ShowOfflineAfterGraceAsync(CancellationTokenSource pending)
    {
        try
        {
            await _delay(Grace, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (pending.IsCancellationRequested || _connected != false)
                return;
            _offlineShown = true;
        }
        _showOffline();
    }
}
