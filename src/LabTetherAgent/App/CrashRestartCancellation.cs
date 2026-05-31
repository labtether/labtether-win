namespace LabTetherAgent.App;

/// <summary>
/// Tracks a delayed crash restart so user-initiated stops or manual starts can
/// cancel the pending restart before it relaunches the agent.
/// </summary>
internal sealed class CrashRestartCancellation : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _pending;
    private bool _disposed;

    public CancellationTokenSource Begin()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            _pending?.Cancel();
            _pending = new CancellationTokenSource();
            return _pending;
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _pending?.Cancel();
            _pending = null;
        }
    }

    public bool IsCurrent(CancellationTokenSource cts)
    {
        lock (_lock)
        {
            return ReferenceEquals(_pending, cts);
        }
    }

    public void ClearIfCurrent(CancellationTokenSource cts)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_pending, cts))
                _pending = null;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? pending;
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        if (pending == null)
            return;

        pending.Cancel();
        pending.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CrashRestartCancellation));
    }
}
