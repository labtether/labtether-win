using System.Globalization;

namespace LabTetherAgent.State;

/// <summary>
/// Parsed agent status from the Go agent's /agent/status endpoint.
/// </summary>
public class AgentStatus
{
    public bool IsConnected { get; set; }
    public string HubConnectionState { get; set; } = "disconnected";
    public string LastError { get; set; } = string.Empty;
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

    // Windows-exclusive status
    public HyperVStatus? HyperV { get; set; }
    public WindowsUpdateStatus? WindowsUpdate { get; set; }

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

            return (HubConnectionState ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "connecting" => "Connecting",
                "auth_failed" => "Authentication Failed",
                "disconnected" or "" => "Disconnected",
                _ => "Disconnected",
            };
        }
    }

    /// <summary>
    /// Extract Windows-specific status from capabilities metadata.
    /// </summary>
    public void ExtractWindowsStatus()
    {
        // Hyper-V
        if (Metadata.TryGetValue("hyperv_enabled", out var hvEnabled) &&
            string.Equals(hvEnabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            var vmCount = 0;
            var runningCount = 0;
            if (Metadata.TryGetValue("hyperv_vm_count", out var vmCountStr))
                vmCount = ParseNonNegativeCount(vmCountStr);
            if (Metadata.TryGetValue("hyperv_running_count", out var runningStr))
                runningCount = ParseNonNegativeCount(runningStr);
            HyperV = new HyperVStatus(true, vmCount, runningCount);
        }
        else
        {
            HyperV = null;
        }

        // Windows Update
        var pendingCount = 0;
        var rebootRequired = false;
        if (Metadata.TryGetValue("windows_update_pending", out var pendingStr))
            pendingCount = ParseNonNegativeCount(pendingStr);
        if (Metadata.TryGetValue("windows_update_reboot_required", out var rebootStr))
            rebootRequired = string.Equals(rebootStr, "true", StringComparison.OrdinalIgnoreCase);
        if (pendingCount > 0 || rebootRequired)
            WindowsUpdate = new WindowsUpdateStatus(pendingCount, rebootRequired);
        else
            WindowsUpdate = null;
    }

    private static int ParseNonNegativeCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var trimmed = value.Trim();
        foreach (var c in trimmed)
        {
            if (c is < '0' or > '9')
                return 0;
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }
}

public record AlertSnapshot(string Name, string State, string Severity, string? Message);
public record HyperVStatus(bool Enabled, int VmCount, int RunningCount);
public record WindowsUpdateStatus(int PendingCount, bool RebootRequired);
