using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>The bold first line and the optional second line of one Windows notification.</summary>
public readonly record struct ToastMessage(string Title, string Body);

/// <summary>
/// Plain words for the Windows notifications. A state change reads as a
/// status word plus a time, the same as the tray tooltip ("Offline since 21:12").
/// </summary>
public static class ToastText
{
    /// <summary>Uses the agent's own disconnect time when its latest status has one.</summary>
    public static ToastMessage Disconnected(AgentStatus? latest, DateTimeOffset now)
    {
        var since = latest is { IsConnected: false, DisconnectedAt: { } at } ? at : now;
        return new("Offline since " + AgentStatusText.FormatClock(since, now),
            "Lost the connection to the Hub. Reconnecting.");
    }

    public static ToastMessage Reconnected(DateTimeOffset now) =>
        new("Reconnected at " + AgentStatusText.FormatClock(now, now), "Connected to the Hub.");

    public static ToastMessage AgentStopped(string? message, DateTimeOffset now) =>
        new("Agent stopped at " + AgentStatusText.FormatClock(now, now), message?.Trim() ?? "");

    public static ToastMessage UpdateAvailable(string version) =>
        new("Update available", "LabTether Agent " + version.Trim());

    public static ToastMessage Alert(AlertSnapshot alert, DateTimeOffset now)
    {
        var kind = string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase)
            ? "Critical alert" : "Alert";
        var title = string.IsNullOrWhiteSpace(alert.Name) ? kind : $"{kind}: {alert.Name.Trim()}";

        var message = alert.Message?.Trim() ?? "";
        var since = alert.Since is { } at ? "since " + AgentStatusText.FormatClock(at, now) : "";
        var body = message.Length > 0 ? AgentStatusText.Join(message, since)
            : since.Length > 0 ? "Firing " + since
            : "Firing";
        return new(title, body);
    }
}
