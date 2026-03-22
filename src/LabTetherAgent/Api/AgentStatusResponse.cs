using System.Text.Json.Serialization;

namespace LabTetherAgent.Api;

/// <summary>
/// JSON deserialization model for GET /agent/status.
/// </summary>
public class AgentStatusResponse
{
    [JsonPropertyName("hub_connection_state")]
    public string HubConnectionState { get; set; } = "disconnected";

    [JsonPropertyName("uptime")]
    public string? Uptime { get; set; }

    [JsonPropertyName("cpu_percent")]
    public double CpuPercent { get; set; }

    [JsonPropertyName("memory_percent")]
    public double MemoryPercent { get; set; }

    [JsonPropertyName("memory_used_bytes")]
    public long MemoryUsedBytes { get; set; }

    [JsonPropertyName("memory_total_bytes")]
    public long MemoryTotalBytes { get; set; }

    [JsonPropertyName("disk_percent")]
    public double DiskPercent { get; set; }

    [JsonPropertyName("network_rx_bytes_per_sec")]
    public long NetworkRxBytesPerSec { get; set; }

    [JsonPropertyName("network_tx_bytes_per_sec")]
    public long NetworkTxBytesPerSec { get; set; }

    [JsonPropertyName("alerts")]
    public List<AlertResponse>? Alerts { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class AlertResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
