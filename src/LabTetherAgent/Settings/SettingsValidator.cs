using System.Globalization;

namespace LabTetherAgent.Settings;

/// <summary>
/// Validates agent configuration values.
/// </summary>
public static class SettingsValidator
{
    private static readonly HashSet<string> AllowedSchemes = ["https", "wss", "http", "ws"];
    private static readonly HashSet<string> AllowedLogLevels = ["debug", "info", "warn", "error"];

    /// <summary>Shown when a typed Hub address cannot be used. Same forms as the Mac app.</summary>
    public const string HubAddressError = "Use a Hub address like hub.example.com:8443 or https://hub.example.com.";

    public static bool IsValidHubUrl(string? url)
    {
        return TryCreateHubUri(url, out _);
    }

    public static bool IsValidToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token);
    }

    public static bool IsValidPort(string? port)
    {
        return TryParseIntegerInRange(port, 1, 65535, out _);
    }

    public static bool TryParseIntegerInRange(string? value, int min, int max, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        foreach (var c in trimmed)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) &&
               parsed >= min &&
               parsed <= max;
    }

    /// <summary>
    /// The agent's bounds for docker_discovery_interval_sec, in seconds
    /// (labtether-agent internal/agentcore/sysconfig/settings_definitions.go).
    /// </summary>
    public const int DockerDiscoveryIntervalMin = 5;
    public const int DockerDiscoveryIntervalMax = 3600;

    public const string DockerDiscoveryIntervalError = "Discovery interval must be 5 to 3600 seconds.";

    /// <summary>
    /// The interval as the agent reads it, or false when the agent would
    /// refuse it. Never replaces a value with another number.
    /// </summary>
    public static bool TryNormalizeDockerDiscoveryInterval(string? interval, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseIntegerInRange(interval, DockerDiscoveryIntervalMin, DockerDiscoveryIntervalMax, out var seconds))
            return false;
        normalized = seconds.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public static bool IsValidLogLevel(string? level)
    {
        return !string.IsNullOrWhiteSpace(level) &&
               AllowedLogLevels.Contains(level.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Normalize a hub URL to a WebSocket URL with /ws/agent path. Accepts the
    /// same input as the Mac app's canonicalHubWebSocketURL, including an
    /// address without a scheme ("hub.example.com:8443" becomes
    /// "wss://hub.example.com:8443/ws/agent"). Unlike the Mac app it drops a
    /// trailing slash: the Hub serves the agent socket at /ws/agent only.
    /// </summary>
    public static string? NormalizeHubWebSocketUrl(string? url)
    {
        if (!TryCreateHubUri(url, out var uri))
            return null;

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" :
                uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "ws" :
                uri.Scheme.ToLowerInvariant(),
            Path = NormalizeHubPath(uri),
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    /// <summary>
    /// Transport, host and port of a Hub URL. An agent token is valid only for
    /// the Hub that issued it; the path does not matter.
    /// </summary>
    public static string? HubCredentialScope(string? url)
    {
        var normalized = NormalizeHubWebSocketUrl(url);
        if (normalized == null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return null;
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool IsSameHub(string? first, string? second)
    {
        var scope = HubCredentialScope(first);
        return scope != null && string.Equals(scope, HubCredentialScope(second), StringComparison.Ordinal);
    }

    /// <summary>
    /// Derive HTTP API base URL from a WebSocket URL.
    /// wss://host:8443/ws/agent -> https://host:8443
    /// </summary>
    public static string? DeriveApiBaseUrl(string? wsUrl)
    {
        if (string.IsNullOrWhiteSpace(wsUrl))
            return null;

        if (Uri.TryCreate(WithDefaultScheme(wsUrl), UriKind.Absolute, out var uri) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment))
        {
            var scheme = uri.Scheme.ToLowerInvariant() switch
            {
                "wss" => "https",
                "ws" => "http",
                "https" => "https",
                "http" => "http",
                _ => null,
            };
            if (scheme != null)
            {
                var builder = new UriBuilder(uri)
                {
                    Scheme = scheme,
                    Path = string.Empty,
                    Query = string.Empty,
                    Fragment = string.Empty,
                };
                return builder.Uri.ToString().TrimEnd('/');
            }
        }

        return null;
    }

    private static bool TryCreateHubUri(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(WithDefaultScheme(url), UriKind.Absolute, out var parsed))
            return false;

        if (!AllowedSchemes.Contains(parsed.Scheme.ToLowerInvariant()))
            return false;

        if (string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
            return false;

        uri = parsed;
        return true;
    }

    // An address typed without a scheme means a TLS Hub, as in the Mac app.
    private static string WithDefaultScheme(string url)
    {
        var trimmed = url.Trim();
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "wss://" + trimmed;
    }

    private static string NormalizeHubPath(Uri uri)
    {
        var path = uri.AbsolutePath.Trim();
        if (string.IsNullOrEmpty(path) || path == "/")
            return "ws/agent";

        return path.TrimEnd('/').TrimStart('/');
    }
}
