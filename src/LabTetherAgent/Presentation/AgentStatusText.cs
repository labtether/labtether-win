using System.Globalization;
using System.Text.RegularExpressions;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>Status word, second line and problem flag for the flyout and pop-out header.</summary>
public sealed record AgentHeader(string StatusWord, string Detail, bool IsProblem);

/// <summary>One firing alert as a list row: title, then summary and start time.</summary>
public sealed record AlertRow(string Title, string Detail, bool IsCritical);

/// <summary>
/// Plain words for what the agent reports. Status is a word plus a time
/// ("Offline since 21:12"); a problem replaces the second line.
/// </summary>
public static partial class AgentStatusText
{
    public const string Separator = " · ";

    /// <summary>Shown instead of a Hub before the first enrollment.</summary>
    public const string NotSetUp = "Not set up";

    /// <param name="startProblem">Why the agent could not be started, when it is not running.</param>
    public static AgentHeader Header(
        AgentStatus? status, string? hubUrl, DateTimeOffset now, string? startProblem = null)
    {
        // An agent that could not start reports nothing; say why it is stopped.
        if (!string.IsNullOrWhiteSpace(startProblem) && status?.IsConnected != true)
            return new AgentHeader("Stopped", startProblem.Trim(), true);

        var host = HubHost(hubUrl);
        if (status == null)
            return host.Length == 0
                ? new AgentHeader(NotSetUp, "", false)
                : new AgentHeader("Offline", host, true);

        if (status.IsConnected)
        {
            var uptime = FormatUptime(status.Uptime);
            return new AgentHeader("Connected", Join(host, uptime.Length > 0 ? "up " + uptime : ""), false);
        }

        var since = status.DisconnectedAt is { } at ? "since " + FormatClock(at, now) : "";
        var reason = status.ConnectionDisplayText;
        if (reason == "Connecting" && since.Length == 0)
            return new AgentHeader("Connecting", host, false);
        if (reason is "Connecting" or "Disconnected")
            reason = host;
        return new AgentHeader("Offline", Join(reason, since), true);
    }

    public static List<AlertRow> AlertRows(AgentStatus? status, DateTimeOffset now) =>
        status?.FiringAlerts.Select(alert => new AlertRow(
            string.IsNullOrWhiteSpace(alert.Name) ? "Alert" : alert.Name.Trim(),
            Join(alert.Message?.Trim() ?? "", alert.Since is { } at ? "since " + FormatClock(at, now) : ""),
            string.Equals(alert.Severity, "critical", StringComparison.OrdinalIgnoreCase))).ToList() ?? [];

    /// <summary>Memory as a percent; older agents only send used and total bytes.</summary>
    public static double MemoryPercent(AgentStatus? status)
    {
        if (status == null) return 0;
        if (status.MemoryPercent > 0 || status.MemoryTotalBytes <= 0) return status.MemoryPercent;
        return 100.0 * status.MemoryUsedBytes / status.MemoryTotalBytes;
    }

    /// <summary>The Hub's host name, without scheme, port or path.</summary>
    public static string HubHost(string? hubUrl)
    {
        var text = (hubUrl ?? "").Trim();
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : text;
    }

    /// <summary>A Go duration ("52h14m0s") as "2 d 4 h", "4 h 12 min", "12 min" or "45 s".</summary>
    public static string FormatUptime(string? goDuration)
    {
        var text = (goDuration ?? "").Trim();
        var match = GoDuration().Match(text);
        if (text.Length == 0 || !match.Success)
            return text;

        static long Part(Group group) => group.Success ? long.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
        var total = TimeSpan.FromHours(Part(match.Groups[1])) + TimeSpan.FromMinutes(Part(match.Groups[2]))
            + TimeSpan.FromSeconds(Part(match.Groups[3]));
        if (total.TotalDays >= 1)
            return total.Hours > 0 ? $"{(int)total.TotalDays} d {total.Hours} h" : $"{(int)total.TotalDays} d";
        if (total.TotalHours >= 1)
            return total.Minutes > 0 ? $"{total.Hours} h {total.Minutes} min" : $"{total.Hours} h";
        if (total.TotalMinutes >= 1)
            return $"{total.Minutes} min";
        return $"{total.Seconds} s";
    }

    /// <summary>A local clock time; adds the day when it is not today.</summary>
    public static string FormatClock(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;
        var time = local.ToString("t", culture);
        return local.Date == now.ToLocalTime().Date ? time : $"{local.ToString("M", culture)} {time}";
    }

    public static string Join(params string[] parts) =>
        string.Join(Separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)(?:\.\d+)?s)?$", RegexOptions.CultureInvariant)]
    private static partial Regex GoDuration();
}
