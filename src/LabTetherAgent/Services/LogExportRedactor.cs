using LabTetherAgent.Settings;
using LabTetherAgent.State;

namespace LabTetherAgent.Services;

/// <summary>
/// Redacts whole logical lines from a bounded retained/filtered log snapshot.
/// Fragment strings remain shared until export; no unbounded live assembly is kept.
/// </summary>
internal static class LogExportRedactor
{
    internal const string IncompleteLineMarker = "[REDACTED] Incomplete oversized log line omitted.";

    internal static IEnumerable<string> RedactLines(IReadOnlyList<LogLine> lines, AgentSettings settings)
    {
        Dictionary<long, FragmentGroup>? groups = null;
        foreach (var line in lines)
        {
            if (line.Fragment is not { } fragment)
                continue;
            groups ??= new Dictionary<long, FragmentGroup>();
            if (!groups.TryGetValue(fragment.GroupId, out var group))
            {
                group = new FragmentGroup();
                groups.Add(fragment.GroupId, group);
            }
            group.Add(line.Raw, fragment);
        }

        foreach (var line in lines)
        {
            if (line.Fragment is not { } fragment)
            {
                yield return DiagnosticsCollector.RedactLogLine(line.Raw, settings);
            }
            else if (groups!.Remove(fragment.GroupId, out var group))
            {
                // Emit once at the group's first retained position, even when
                // another stream's records appeared between its fragments.
                yield return group.IsComplete
                    ? DiagnosticsCollector.RedactLogLine(group.Join(), settings)
                    : IncompleteLineMarker;
            }
        }
    }

    private sealed class FragmentGroup
    {
        private readonly List<string> _parts = [];
        private long _nextIndex;
        private bool _valid = true;
        private bool _final;

        internal bool IsComplete => _valid && _final;

        internal void Add(string raw, LogLineFragment fragment)
        {
            // A missing first/middle/last part may contain the secret's key or
            // prefix. Never export any raw part when that context is absent.
            _valid &= !_final && fragment.Index == _nextIndex;
            _nextIndex++;
            _final = fragment.IsFinal;
            _parts.Add(raw);
        }

        internal string Join() => string.Concat(_parts);
    }
}
