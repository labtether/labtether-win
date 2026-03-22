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
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        return AllowedSchemes.Contains(uri.Scheme.ToLowerInvariant());
    }

    public static bool IsValidToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token);
    }

    public static bool IsValidPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port))
            return false;

        return int.TryParse(port.Trim(), out var p) && p is > 0 and <= 65535;
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
        if (!IsValidHubUrl(url))
            return null;

        var trimmed = url!.Trim().TrimEnd('/');

        // Convert http(s) to ws(s)
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            trimmed = "wss://" + trimmed[8..];
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            trimmed = "ws://" + trimmed[7..];

        // Append /ws/agent if not already present
        if (!trimmed.EndsWith("/ws/agent", StringComparison.OrdinalIgnoreCase))
            trimmed += "/ws/agent";

        return trimmed;
    }

    /// <summary>
    /// Derive HTTP API base URL from a WebSocket URL.
    /// wss://host:8443/ws/agent -> https://host:8443
    /// </summary>
    public static string? DeriveApiBaseUrl(string? wsUrl)
    {
        if (string.IsNullOrWhiteSpace(wsUrl))
            return null;

        var apiBase = wsUrl.Trim();
        apiBase = apiBase.Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase);
        apiBase = apiBase.Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);

        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri) { Path = string.Empty };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return null;
    }
}
