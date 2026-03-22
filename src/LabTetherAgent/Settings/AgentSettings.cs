using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabTetherAgent.Settings;

/// <summary>
/// Agent configuration model. Persisted to settings.json.
/// Secrets stored separately in Windows Credential Manager via CredentialStore.
///
/// Mirrors mac-agent/Sources/LabTetherAgent/Settings/AgentSettings.swift.
/// </summary>
public class AgentSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabTether");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "settings.json");

    // Connection
    public string HubUrl { get; set; } = "wss://localhost:8443/ws/agent";
    public string AssetId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string AgentPort { get; set; } = "8091";

    // TLS
    public bool TlsSkipVerify { get; set; }
    public string TlsCaFile { get; set; } = string.Empty;

    // Docker
    public string DockerEnabled { get; set; } = "auto";
    public string DockerEndpoint { get; set; } = @"\\.\pipe\docker_engine";
    public string DockerDiscoveryInterval { get; set; } = "30";

    // Files
    public string FilesRootMode { get; set; } = "home";

    // Feature toggles
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool AllowRemoteOverrides { get; set; }
    public bool LowPowerMode { get; set; }
    public bool StartAtLogin { get; set; }

    // Logging
    public string LogLevel { get; set; } = "info";

    // WebRTC
    public bool WebRtcEnabled { get; set; } = true;
    public string WebRtcStunUrl { get; set; } = "stun:stun.l.google.com:19302";
    public string WebRtcTurnUrl { get; set; } = string.Empty;
    public string WebRtcTurnUser { get; set; } = string.Empty;

    // Secrets (not persisted in JSON — stored in Credential Manager)
    [JsonIgnore] public string ApiToken { get; set; } = string.Empty;
    [JsonIgnore] public string EnrollmentToken { get; set; } = string.Empty;
    [JsonIgnore] public string WebRtcTurnPass { get; set; } = string.Empty;
    [JsonIgnore] public string LocalApiAuthToken { get; set; } = string.Empty;

    // Change tracking
    [JsonIgnore] public int SettingsVersion { get; private set; }

    [JsonIgnore]
    public bool IsEnrolled =>
        SettingsValidator.IsValidHubUrl(HubUrl) &&
        (SettingsValidator.IsValidToken(ApiToken) || SettingsValidator.IsValidToken(EnrollmentToken));

    public void IncrementVersion() => SettingsVersion++;

    /// <summary>
    /// Save settings to disk (secrets excluded — they go to Credential Manager).
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        });
        File.WriteAllText(SettingsPath, json);
        IncrementVersion();
    }

    /// <summary>
    /// Load settings from disk. Returns default settings if file doesn't exist.
    /// </summary>
    public static AgentSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AgentSettings();

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AgentSettings>(json) ?? new AgentSettings();
    }

    /// <summary>
    /// Get the settings directory path.
    /// </summary>
    public static string GetSettingsDirectory() => SettingsDir;
}
