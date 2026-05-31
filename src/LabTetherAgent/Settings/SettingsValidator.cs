using System.Globalization;

namespace LabTetherAgent.Settings;

/// <summary>
/// Validates agent configuration values.
/// </summary>
public static class SettingsValidator
{
    private static readonly HashSet<string> AllowedSchemes = ["https", "wss", "http", "ws"];
    private static readonly HashSet<string> AllowedLogLevels = ["debug", "info", "warn", "error"];

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

    public static string NormalizeDockerDiscoveryInterval(string? interval)
    {
        return TryParseIntegerInRange(interval, 10, 600, out var seconds)
            ? seconds.ToString(CultureInfo.InvariantCulture)
            : "30";
    }

    public static bool IsValidLogLevel(string? level)
    {
        return !string.IsNullOrWhiteSpace(level) &&
               AllowedLogLevels.Contains(level.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Normalize a hub URL to a WebSocket URL with /ws/agent path.
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
    /// Derive HTTP API base URL from a WebSocket URL.
    /// wss://host:8443/ws/agent -> https://host:8443
    /// </summary>
    public static string? DeriveApiBaseUrl(string? wsUrl)
    {
        if (string.IsNullOrWhiteSpace(wsUrl))
            return null;

        if (Uri.TryCreate(wsUrl.Trim(), UriKind.Absolute, out var uri) &&
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

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
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

    private static string NormalizeHubPath(Uri uri)
    {
        var path = uri.AbsolutePath.Trim();
        if (string.IsNullOrEmpty(path) || path == "/")
            return "ws/agent";

        return path.TrimEnd('/').TrimStart('/');
    }
}
