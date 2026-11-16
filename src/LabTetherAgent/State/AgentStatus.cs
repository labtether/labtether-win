namespace LabTetherAgent.State;

/// <summary>
/// Parsed agent status from the Go agent's /agent/status endpoint.
/// </summary>
public class AgentStatus
{
    public bool IsConnected { get; set; }
    public string HubConnectionState { get; set; } = "disconnected";
    public string LastError { get; set; } = string.Empty;
    public string LastErrorReason { get; set; } = string.Empty;
    /// <summary>The Hub's reason for refusing an enrollment, when it gave one.</summary>
    public string EnrollmentError { get; set; } = string.Empty;
    public string? Uptime { get; set; }
    /// <summary>When the Hub connection dropped, as the agent reports it.</summary>
    public DateTimeOffset? DisconnectedAt { get; set; }

    // Metrics
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryTotalBytes { get; set; }
    public double DiskPercent { get; set; }
    public long NetworkRxBytesPerSec { get; set; }
    public long NetworkTxBytesPerSec { get; set; }

    // Alerts
    public List<AlertSnapshot> Alerts { get; set; } = [];
    public List<AlertSnapshot> FiringAlerts => Alerts.Where(a => a.State == "firing").ToList();
    public bool HasCriticalFiring => FiringAlerts.Any(a => a.Severity == "critical");

    // Capabilities metadata
    public Dictionary<string, string> Metadata { get; set; } = [];

    public string MemoryDisplayText =>
        MemoryTotalBytes > 0
            ? $"{MemoryUsedBytes / (1024.0 * 1024 * 1024):F1} GB"
            : $"{MemoryPercent:F0}%";

    /// <summary>True while the Hub has not yet approved this device's enrollment.</summary>
    public bool IsWaitingForApproval =>
        !IsConnected && AgentLastError.Has(LastError, AgentLastError.WaitingForApproval);

    /// <summary>A problem to show beside "Connected", such as an agent token that was not saved.</summary>
    public string? ConnectedProblemText => IsConnected ? AgentLastError.Words(LastError) : null;

    public string ConnectionDisplayText
    {
        get
        {
            if (IsConnected)
                return "Connected";

            var state = (HubConnectionState ?? string.Empty).Trim().ToLowerInvariant();
            var lastError = AgentLastError.Words(LastError);
            var reason = AgentLastError.ReasonWords(LastErrorReason);
            // A refused token: which token and what to do come first. Otherwise
            // the cause of the last failed connect says more.
            if (state == "auth_failed")
                return lastError ?? reason ?? AgentLastError.AgentTokenRefusedText;
            if (reason != null || lastError != null)
                return reason ?? lastError!;

            return state switch
            {
                "connecting" => "Connecting",
                _ => "Disconnected",
            };
        }
    }
}

/// <param name="Since">When the alert started firing, as the agent reports it.</param>
public record AlertSnapshot(string Name, string State, string Severity, string? Message, DateTimeOffset? Since = null);
