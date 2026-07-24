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
    // Retained only so older settings.json files deserialize without losing
    // their remaining values. Native releases update the bundled, attested Go
    // core only as part of a whole-app update; this value is always normalized
    // to false and is never allowed to enable child self-update.
    public bool AutoUpdateEnabled { get; set; }
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
    [JsonIgnore] internal string? PersistedAgentTokenPathOverride { get; set; }

    // Change tracking
    [JsonIgnore] public int SettingsVersion { get; private set; }

    [JsonIgnore]
    public bool IsEnrolled =>
        SettingsValidator.IsValidHubUrl(HubUrl) &&
        MinimumCredentialConfigured(ApiToken, EnrollmentToken, HasPersistedAgentToken);

    /// <summary>
    /// Whether the Go child has persisted its durable, asset-bound bearer.
    /// This is authoritative after one-use enrollment and must survive native
    /// wrapper relaunches without retaining the consumed enrollment secret.
    /// </summary>
    [JsonIgnore]
    public bool HasPersistedAgentToken => HasPrivatePersistedAgentTokenAt(
        PersistedAgentTokenPathOverride ?? Path.Combine(SettingsDir, "agent-token"));

    internal static bool MinimumCredentialConfigured(
        string apiToken,
        string enrollmentToken,
        bool hasPersistedAgentToken) =>
        SettingsValidator.IsValidToken(apiToken) ||
        SettingsValidator.IsValidToken(enrollmentToken) ||
        hasPersistedAgentToken;

    internal static bool HasPrivatePersistedAgentTokenAt(string path) =>
        SecureFile.IsPrivateRegularFile(path);

    /// <summary>
    /// Remove a consumed one-use enrollment secret while preserving the
    /// durable bearer written by the Go child.
    /// </summary>
    public bool ClearConsumedEnrollmentTokenPreservingAgentToken(CredentialStore credentialStore)
    {
        if (!HasPersistedAgentToken || string.IsNullOrWhiteSpace(EnrollmentToken))
            return false;

        credentialStore.Remove(CredentialStore.EnrollmentTokenResource);
        EnrollmentToken = string.Empty;
        SecureFile.DeleteIfExists(Path.Combine(SettingsDir, "enrollment-token"));
        SecureFile.DeleteIfExists(Path.Combine(SettingsDir, "enrollment-token.sha256"));
        return true;
    }

    /// <summary>
    /// Clear the one-time group placement requested during enrollment after a
    /// durable credential exists. The Hub's stored asset placement is now
    /// authoritative; omitting AGENT_GROUP_ID from later heartbeats preserves
    /// that placement and avoids reviving a group that was renamed or removed.
    /// </summary>
    public bool ClearPersistedGroupIntentAfterEnrollment() =>
        ClearPersistedGroupIntentAfterEnrollmentAt(
            Path.Combine(SettingsDir, "agent-token"),
            Save);

    internal bool ClearPersistedGroupIntentAfterEnrollmentAt(
        string agentTokenPath,
        Action persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        // A configured enrollment token may be a deliberate re-enrollment
        // against an existing durable installation. Preserve its new group
        // intent until the child replaces the durable credential and the
        // completion path clears the consumed token first.
        if (!string.IsNullOrWhiteSpace(EnrollmentToken) ||
            !HasPrivatePersistedAgentTokenAt(agentTokenPath) ||
            string.IsNullOrWhiteSpace(GroupId))
        {
            return false;
        }

        // Clear in memory before persisting so this process fails safe even if
        // a transient filesystem error prevents the migration from being
        // committed. A later launch will retry the persisted migration, while
        // AgentEnvironmentBuilder still refuses to export durable stale intent.
        GroupId = string.Empty;
        persist();
        return true;
    }

    /// <summary>Remove the per-process localhost API credential after stop.</summary>
    public void CleanupEphemeralSecrets()
    {
        LocalApiAuthToken = string.Empty;
        SecureFile.DeleteIfExists(Path.Combine(SettingsDir, "local-api-auth-token"));
    }

    public void IncrementVersion() => SettingsVersion++;

    /// <summary>
    /// Save settings to disk (secrets excluded — they go to Credential Manager).
    /// </summary>
    public void Save()
    {
        NormalizeWrapperManagedUpdatePolicy();
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        });
        SecureFile.WriteAllText(SettingsPath, json);
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
        var settings = JsonSerializer.Deserialize<AgentSettings>(json) ?? new AgentSettings();
        settings.NormalizeWrapperManagedUpdatePolicy();
        return settings;
    }

    /// <summary>
    /// Migrate the legacy child self-update setting to the native app's
    /// whole-package update policy. The environment builder independently
    /// enforces the same boundary so a stale in-memory value also fails safe.
    /// </summary>
    internal bool NormalizeWrapperManagedUpdatePolicy()
    {
        if (!AutoUpdateEnabled)
            return false;

        AutoUpdateEnabled = false;
        return true;
    }

    /// <summary>
    /// Get the settings directory path.
    /// </summary>
    public static string GetSettingsDirectory() => SettingsDir;

    internal static string GetSettingsPath() => SettingsPath;

    /// <summary>
    /// Create an independent setup candidate. Secrets are copied in memory so
    /// onboarding can replace them without mutating the active installation.
    /// </summary>
    internal AgentSettings CloneForSetup() => (AgentSettings)MemberwiseClone();

    /// <summary>
    /// Publish an already-persisted setup candidate into the long-lived app
    /// state. This is deliberately separate from Save so failed setup never
    /// makes partially entered values appear active in the tray UI.
    /// </summary>
    internal void ApplyCommittedSetup(AgentSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);

        HubUrl = source.HubUrl;
        AssetId = source.AssetId;
        GroupId = source.GroupId;
        AgentPort = source.AgentPort;
        TlsSkipVerify = source.TlsSkipVerify;
        TlsCaFile = source.TlsCaFile;
        DockerEnabled = source.DockerEnabled;
        DockerEndpoint = source.DockerEndpoint;
        DockerDiscoveryInterval = source.DockerDiscoveryInterval;
        FilesRootMode = source.FilesRootMode;
        AutoUpdateEnabled = false;
        AllowRemoteOverrides = source.AllowRemoteOverrides;
        LowPowerMode = source.LowPowerMode;
        StartAtLogin = source.StartAtLogin;
        LogLevel = source.LogLevel;
        WebRtcEnabled = source.WebRtcEnabled;
        WebRtcStunUrl = source.WebRtcStunUrl;
        WebRtcTurnUrl = source.WebRtcTurnUrl;
        WebRtcTurnUser = source.WebRtcTurnUser;
        ApiToken = source.ApiToken;
        EnrollmentToken = source.EnrollmentToken;
        WebRtcTurnPass = source.WebRtcTurnPass;
        LocalApiAuthToken = source.LocalApiAuthToken;
        PersistedAgentTokenPathOverride = source.PersistedAgentTokenPathOverride;
        SettingsVersion = source.SettingsVersion;
    }
}
