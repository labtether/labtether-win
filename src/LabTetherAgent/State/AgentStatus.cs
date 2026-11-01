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
    public string? Uptime { get; set; }

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

    public string ConnectionDisplayText
    {
        get
        {
            if (IsConnected)
                return "Connected";

            var reason = ConnectReasonText(LastErrorReason);
            if (reason != null)
                return reason;

            return (HubConnectionState ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "connecting" => "Connecting",
                "auth_failed" => "Authentication Failed",
                "disconnected" or "" => "Disconnected",
                _ => "Disconnected",
            };
        }
    }

    // Plain text for the agent's last_error_reason code. An empty or unknown
    // code gives null, so the generic state text stays.
    private static string? ConnectReasonText(string? code) =>
        (code ?? string.Empty).Trim() switch
        {
            "name_lookup_failed" => "Can't find the Hub's address",
            "connection_refused" or "host_unreachable" or "timed_out" => "Can't reach the Hub",
            "certificate_untrusted" => "The Hub's certificate isn't trusted",
            "token_rejected" => "The Hub refused this device. Enroll it again.",
            _ => null,
        };
}

public record AlertSnapshot(string Name, string State, string Severity, string? Message);
