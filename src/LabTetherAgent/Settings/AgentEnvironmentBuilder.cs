namespace LabTetherAgent.Settings;

/// <summary>
/// Builds the environment variable dictionary for the Go agent process from settings.
/// Mirrors mac-agent/Sources/LabTetherAgent/Settings/AgentEnvironmentBuilder.swift.
/// </summary>
public static class AgentEnvironmentBuilder
{
    public static Dictionary<string, string> BuildEnvironment(
        AgentSettings settings,
        string localApiPort,
        string localApiAuthToken)
    {
        var env = new Dictionary<string, string>();

        // Hub connection
        var wsUrl = SettingsValidator.NormalizeHubWebSocketUrl(settings.HubUrl);
        if (!string.IsNullOrEmpty(wsUrl))
        {
            env["LABTETHER_WS_URL"] = wsUrl;
            var apiBase = SettingsValidator.DeriveApiBaseUrl(wsUrl);
            if (!string.IsNullOrEmpty(apiBase))
                env["LABTETHER_API_BASE_URL"] = apiBase;
        }

        // Identity
        if (!string.IsNullOrWhiteSpace(settings.AssetId))
            env["AGENT_ASSET_ID"] = settings.AssetId.Trim();
        if (!string.IsNullOrWhiteSpace(settings.GroupId))
            env["AGENT_GROUP_ID"] = settings.GroupId.Trim();

        // Local API
        env["AGENT_PORT"] = localApiPort;
        env["LABTETHER_LOCAL_API_AUTH_TOKEN"] = localApiAuthToken;

        // TLS
        if (settings.TlsSkipVerify)
            env["LABTETHER_TLS_SKIP_VERIFY"] = "true";

        // Docker
        env["LABTETHER_DOCKER_ENABLED"] = settings.DockerEnabled;
        if (!string.IsNullOrWhiteSpace(settings.DockerEndpoint))
            env["LABTETHER_DOCKER_SOCKET"] = settings.DockerEndpoint.Trim();
        if (!string.IsNullOrWhiteSpace(settings.DockerDiscoveryInterval))
            env["LABTETHER_DOCKER_DISCOVERY_INTERVAL"] = settings.DockerDiscoveryInterval.Trim();

        // Files
        env["LABTETHER_FILES_ROOT_MODE"] = settings.FilesRootMode;

        // Feature toggles
        env["LABTETHER_AUTO_UPDATE"] = settings.AutoUpdateEnabled ? "true" : "false";
        env["LABTETHER_ALLOW_REMOTE_OVERRIDES"] = settings.AllowRemoteOverrides ? "true" : "false";
        env["LABTETHER_LOW_POWER_MODE"] = settings.LowPowerMode ? "true" : "false";

        // Logging
        if (!string.IsNullOrWhiteSpace(settings.LogLevel))
            env["LABTETHER_LOG_LEVEL"] = settings.LogLevel.Trim().ToLowerInvariant();

        // Disable background log streaming in tray mode (CPU hotspot, same as macOS)
        env["LABTETHER_LOG_STREAM_ENABLED"] = "false";

        // WebRTC
        env["LABTETHER_WEBRTC_ENABLED"] = settings.WebRtcEnabled ? "true" : "false";
        if (!string.IsNullOrWhiteSpace(settings.WebRtcStunUrl))
            env["LABTETHER_WEBRTC_STUN_URL"] = settings.WebRtcStunUrl.Trim();
        if (!string.IsNullOrWhiteSpace(settings.WebRtcTurnUrl))
            env["LABTETHER_WEBRTC_TURN_URL"] = settings.WebRtcTurnUrl.Trim();
        if (!string.IsNullOrWhiteSpace(settings.WebRtcTurnUser))
            env["LABTETHER_WEBRTC_TURN_USER"] = settings.WebRtcTurnUser.Trim();

        // Parent PID for graceful shutdown on parent exit
        env["LABTETHER_PARENT_PID"] = Environment.ProcessId.ToString();

        return env;
    }
}
