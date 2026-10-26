namespace LabTetherAgent.State;

/// <summary>
/// Identifies every part of an oversized logical line, including its first part.
/// Separate stdout/stderr readers use unique groups so exports can join safely.
/// </summary>
internal sealed record LogLineFragment(long GroupId, long Index, bool IsFinal);

/// <summary>
/// A parsed log line from the Go agent's stdout/stderr.
/// </summary>
public record LogLine(DateTime Timestamp, string Level, string Message, string Raw)
{
    internal LogLineFragment? Fragment { get; init; }

    /// <summary>
    /// Parse a Go log line in the format "YYYY/MM/DD HH:MM:SS message".
    /// Falls back to raw line with "info" level if parsing fails.
    /// </summary>
    public static LogLine Parse(string raw) => Parse(raw, null);

    internal static LogLine Parse(string raw, LogLineFragment? fragment)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new LogLine(DateTime.UtcNow, "info", string.Empty, raw ?? string.Empty) { Fragment = fragment };

        // Go default log format: "2026/03/21 14:30:45 message"
        if (raw.Length >= 20 &&
            DateTime.TryParseExact(raw[..19], "yyyy/MM/dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var ts))
        {
            var message = raw[20..].TrimStart();
            var level = InferLevel(message);
            return new LogLine(ts, level, message, raw) { Fragment = fragment };
        }

        return new LogLine(DateTime.UtcNow, InferLevel(raw), raw, raw) { Fragment = fragment };
    }

    private static string InferLevel(string message)
    {
        var upper = message.AsSpan();
        if (upper.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
            upper.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
            upper.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase))
            return "error";

        if (upper.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) ||
            upper.StartsWith("[WARN", StringComparison.OrdinalIgnoreCase))
            return "warning";

        if (upper.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase) ||
            upper.StartsWith("[DEBUG]", StringComparison.OrdinalIgnoreCase))
            return "debug";

        return "info";
    }
}
