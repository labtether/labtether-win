using System.Runtime.CompilerServices;
using System.Text;
using LabTetherAgent.State;

namespace LabTetherAgent.Process;

/// <summary>
/// Keeps an unterminated child log line from growing without limit. Oversized
/// lines are emitted in full as tagged fragments; UTF-16 pairs stay together.
/// </summary>
internal static class BoundedLogLineReader
{
    internal const int MaximumFragmentCharacters = 64 * 1024;
    private static long _nextGroupId;

    internal readonly record struct Line(string Text, LogLineFragment? Fragment = null);

    internal static async IAsyncEnumerable<Line> ReadAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // StreamReader already buffers decoded characters. Asking for a larger
        // block can wait for a second pipe read even after a complete line was
        // decoded, so request one character without disabling its own buffer.
        var input = new char[1];
        var pending = new StringBuilder(4096);
        var skipLineFeed = false;
        long? groupId = null;
        long fragmentIndex = 0;

        while (true)
        {
            var count = await reader.ReadAsync(input.AsMemory(), cancellationToken);
            if (count == 0)
                break;
            for (var index = 0; index < count; index++)
            {
                var character = input[index];
                if (skipLineFeed)
                {
                    skipLineFeed = false;
                    if (character == '\n')
                        continue;
                }
                if (character is '\r' or '\n')
                {
                    yield return new Line(pending.ToString(), groupId is { } completedGroup
                        ? new LogLineFragment(completedGroup, fragmentIndex, IsFinal: true)
                        : null);
                    pending.Clear();
                    skipLineFeed = character == '\r';
                    groupId = null;
                    fragmentIndex = 0;
                    continue;
                }
                if (pending.Length == MaximumFragmentCharacters)
                {
                    var end = pending.Length;
                    if (char.IsHighSurrogate(pending[end - 1]))
                        end--;
                    groupId ??= Interlocked.Increment(ref _nextGroupId);
                    yield return new Line(pending.ToString(0, end),
                        new LogLineFragment(groupId.Value, fragmentIndex++, IsFinal: false));
                    pending.Remove(0, end);
                }
                pending.Append(character);
            }
        }
        if (pending.Length > 0)
            yield return new Line(pending.ToString(), groupId is { } finalGroup
                ? new LogLineFragment(finalGroup, fragmentIndex, IsFinal: true)
                : null);
    }
}
