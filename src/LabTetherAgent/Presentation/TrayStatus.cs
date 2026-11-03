using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>What the tray glyph shows: no dot, an amber or red dot, or the offline ring.</summary>
public enum TrayGlyphState
{
    Ok,
    Warning,
    Critical,
    Offline,
}

/// <summary>The tray glyph state and its two-line tooltip.</summary>
public readonly record struct TrayStatus(TrayGlyphState State, string Tooltip)
{
    public const string AppName = "LabTether Agent";

    // NOTIFYICONDATA keeps at most 127 characters of tooltip text.
    private const int MaxTooltipLength = 127;

    /// <summary>
    /// Picks the tray state. A stopped agent comes first, then a lost Hub
    /// connection, then firing alerts (critical before warning).
    /// </summary>
    public static TrayStatus Resolve(
        bool connected,
        AgentStatus? status,
        DateTimeOffset? offlineSince,
        DateTimeOffset? stoppedAt,
        DateTimeOffset now)
    {
        if (stoppedAt is { } stopped)
            return Make(TrayGlyphState.Critical, "Agent stopped at " + AgentStatusText.FormatClock(stopped, now));

        if (!connected || status is { IsConnected: false })
        {
            var since = status?.DisconnectedAt ?? offlineSince;
            if (since is { } at)
                return Make(TrayGlyphState.Offline, "Offline since " + AgentStatusText.FormatClock(at, now));
            var connecting = status?.ConnectionDisplayText == "Connecting";
            return Make(TrayGlyphState.Offline, connecting ? "Connecting" : "Offline");
        }

        var alerts = status?.FiringAlerts ?? [];
        if (alerts.Count == 0)
            return Make(TrayGlyphState.Ok, "Connected");

        var state = status!.HasCriticalFiring ? TrayGlyphState.Critical : TrayGlyphState.Warning;
        var line = alerts.Count > 1 ? $"{alerts.Count} alerts"
            : string.IsNullOrWhiteSpace(alerts[0].Name) ? "1 alert" : alerts[0].Name.Trim();
        return Make(state, line);
    }

    private static TrayStatus Make(TrayGlyphState state, string line)
    {
        var tooltip = AppName + "\n" + line;
        return new TrayStatus(state, tooltip.Length <= MaxTooltipLength
            ? tooltip
            : tooltip[..(MaxTooltipLength - 1)] + "…");
    }
}
