namespace LabTetherAgent.Process;

/// <summary>
/// Manages exponential backoff for agent process crash restarts.
/// Mirrors mac-agent/Sources/LabTetherAgent/Process/CrashRestartCoordinator.swift.
/// </summary>
public class CrashRestartCoordinator
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StabilityThreshold = TimeSpan.FromMinutes(5);

    private int _attemptCount;
    private DateTime _lastStartTime = DateTime.MinValue;

    /// <summary>
    /// Get the next restart delay with exponential backoff.
    /// </summary>
    public TimeSpan NextDelay()
    {
        _attemptCount++;
        var delay = TimeSpan.FromSeconds(BaseDelay.TotalSeconds * Math.Pow(2, _attemptCount - 1));
        return delay > MaxDelay ? MaxDelay : delay;
    }

    /// <summary>
    /// Record that the agent process was started.
    /// </summary>
    public void RecordStart() => _lastStartTime = DateTime.UtcNow;

    /// <summary>
    /// Check if the process has been stable long enough to reset backoff.
    /// Call this periodically while the process is running.
    /// </summary>
    public void CheckStability()
    {
        if (_lastStartTime != DateTime.MinValue &&
            DateTime.UtcNow - _lastStartTime > StabilityThreshold)
        {
            Reset();
        }
    }

    /// <summary>
    /// Reset the backoff counter.
    /// </summary>
    public void Reset() => _attemptCount = 0;

    /// <summary>
    /// Current attempt count (for diagnostics).
    /// </summary>
    public int AttemptCount => _attemptCount;
}
