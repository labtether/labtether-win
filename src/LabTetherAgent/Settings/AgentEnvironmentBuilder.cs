using System.Security.Cryptography;
using System.Text;

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
        string localApiAuthToken) =>
        BuildEnvironmentForTrustedDirectory(
            settings,
            localApiPort,
            localApiAuthToken,
            AgentSettings.GetSettingsDirectory());

    /// <summary>
    /// Internal test seam for selecting the secret root. Production callers
    /// always use the application-owned settings directory above; this path is
    /// not populated from command-line, network, or persisted user input.
    /// </summary>
    internal static Dictionary<string, string> BuildEnvironmentForTrustedDirectory(
        AgentSettings settings,
        string localApiPort,
        string localApiAuthToken,
        string secretDirectory)
    {
        var env = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(secretDirectory))
            throw new ArgumentException("Secret directory is required.", nameof(secretDirectory));
        secretDirectory = Path.GetFullPath(secretDirectory);

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
        // A group selected in onboarding is enrollment intent, not an
        // enduring heartbeat override. The Hub commits the canonical group
        // (or safely normalizes an unknown group to unplaced) when it issues
        // the durable agent credential. Re-exporting a stale group on every
        // later launch makes otherwise healthy authenticated heartbeats fail
        // validation before the Hub can preserve their canonical placement.
        if (!string.IsNullOrWhiteSpace(settings.GroupId) &&
            !string.IsNullOrWhiteSpace(settings.EnrollmentToken))
            env["AGENT_GROUP_ID"] = settings.GroupId.Trim();

        // Local API
        env["AGENT_PORT"] = localApiPort;
        var localAuthPath = Path.Combine(secretDirectory, "local-api-auth-token");
        SecureFile.WriteAllText(localAuthPath, localApiAuthToken.Trim() + Environment.NewLine);
        env["LABTETHER_AGENT_LOCAL_AUTH_TOKEN_FILE"] = localAuthPath;

        // File-backed secrets avoid exposing bearer tokens in the child
        // process environment and receive a protected current-user DACL.
        var apiTokenPath = Path.Combine(secretDirectory, "agent-token");
        var enrollmentTokenPath = Path.Combine(secretDirectory, "enrollment-token");
        var enrollmentMarkerPath = Path.Combine(secretDirectory, "enrollment-token.sha256");
        var hasApiToken = !string.IsNullOrWhiteSpace(settings.ApiToken);
        var hasEnrollmentToken = !string.IsNullOrWhiteSpace(settings.EnrollmentToken);
        var hasPersistedAgentToken = SecureFile.IsPrivateRegularFile(apiTokenPath);
        if (hasApiToken && hasEnrollmentToken)
            throw new InvalidOperationException("API token and enrollment token cannot both be configured.");

        if (hasApiToken)
        {
            SecureFile.WriteAllText(apiTokenPath, settings.ApiToken.Trim() + Environment.NewLine);
            SecureFile.DeleteIfExists(enrollmentTokenPath);
            SecureFile.DeleteIfExists(enrollmentMarkerPath);
        }
        else if (hasEnrollmentToken)
        {
            var marker = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.EnrollmentToken.Trim())));
            var previousMarker = File.Exists(enrollmentMarkerPath)
                ? File.ReadAllText(enrollmentMarkerPath).Trim()
                : string.Empty;
            if (!string.Equals(previousMarker, marker, StringComparison.Ordinal))
            {
                // A newly configured enrollment token must not be shadowed by
                // a bearer token persisted from an earlier enrollment/account.
                SecureFile.DeleteIfExists(apiTokenPath);
            }
            SecureFile.WriteAllText(enrollmentTokenPath, settings.EnrollmentToken.Trim() + Environment.NewLine);
            SecureFile.WriteAllText(enrollmentMarkerPath, marker + Environment.NewLine);
        }
        else if (!hasPersistedAgentToken)
        {
            SecureFile.DeleteIfExists(apiTokenPath);
            SecureFile.DeleteIfExists(enrollmentTokenPath);
            SecureFile.DeleteIfExists(enrollmentMarkerPath);
        }
        else
        {
            // The one-use enrollment credential has been removed. Preserve
            // the durable bearer written by the Go child, but remove all
            // remaining enrollment material.
            SecureFile.DeleteIfExists(enrollmentTokenPath);
            SecureFile.DeleteIfExists(enrollmentMarkerPath);
        }
        env["LABTETHER_TOKEN_FILE"] = apiTokenPath;

        if (hasEnrollmentToken)
        {
            env["LABTETHER_ENROLLMENT_TOKEN_FILE"] = enrollmentTokenPath;
        }

        // TLS
        var hasCustomCa = !string.IsNullOrWhiteSpace(settings.TlsCaFile);
        // A current UI cannot save both options. If an older settings file has
        // both, prefer the explicit trust root instead of silently disabling
        // certificate verification.
        if (settings.TlsSkipVerify && !hasCustomCa)
            env["LABTETHER_TLS_SKIP_VERIFY"] = "true";
        if (hasCustomCa)
            env["LABTETHER_TLS_CA_FILE"] = settings.TlsCaFile.Trim();

        // Docker
        env["LABTETHER_DOCKER_ENABLED"] = settings.DockerEnabled;
        if (!string.IsNullOrWhiteSpace(settings.DockerEndpoint))
            env["LABTETHER_DOCKER_SOCKET"] = settings.DockerEndpoint.Trim();
        env["LABTETHER_DOCKER_DISCOVERY_INTERVAL"] =
            SettingsValidator.NormalizeDockerDiscoveryInterval(settings.DockerDiscoveryInterval);

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
        if (!string.IsNullOrWhiteSpace(settings.WebRtcTurnPass))
        {
            var turnPasswordPath = Path.Combine(secretDirectory, "webrtc-turn-password");
            SecureFile.WriteAllText(turnPasswordPath, settings.WebRtcTurnPass.Trim() + Environment.NewLine);
            env["LABTETHER_WEBRTC_TURN_PASS_FILE"] = turnPasswordPath;
        }
        else
        {
            SecureFile.DeleteIfExists(Path.Combine(secretDirectory, "webrtc-turn-password"));
        }

        // Parent PID for graceful shutdown on parent exit
        env["LABTETHER_PARENT_PID"] = Environment.ProcessId.ToString();

        return env;
    }
}
