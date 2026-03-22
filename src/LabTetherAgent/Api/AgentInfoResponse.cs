using System.Text.Json.Serialization;

namespace LabTetherAgent.Api;

/// <summary>
/// JSON deserialization model for GET /agent/info.
/// </summary>
public class AgentInfoResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }

    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("update_available")]
    public bool UpdateAvailable { get; set; }

    [JsonPropertyName("update_version")]
    public string? UpdateVersion { get; set; }
}
