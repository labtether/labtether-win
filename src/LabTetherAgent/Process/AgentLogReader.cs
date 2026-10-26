using LabTetherAgent.State;

namespace LabTetherAgent.Process;

/// <summary>
/// Reads stdout/stderr from the Go agent process, parses log lines,
/// and maintains a bounded buffer of recent entries.
/// Mirrors mac-agent LogParser + LogBuffer.
/// </summary>
public class AgentLogReader
{
    private readonly Queue<LogLine> _buffer = new();
    private readonly object _lock = new();
    private readonly int _maxLines;
    private readonly long _maxRetainedCharacters;
    private long _retainedCharacters;

    public event Action<LogLine>? OnLogLine;

    public AgentLogReader(int maxLines = 5000, int maxRetainedBytes = 4 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLines);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedBytes);
        _maxLines = maxLines;
        _maxRetainedCharacters = maxRetainedBytes / sizeof(char);
    }

    /// <summary>
    /// Start reading from a stream (stdout or stderr) on a background thread.
    /// Returns the task for the reading loop.
    /// </summary>
    public Task ReadAsync(StreamReader reader, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                await foreach (var line in BoundedLogLineReader.ReadAsync(reader, ct))
                {
                    var parsed = LogLine.Parse(line.Text, line.Fragment);
                    Append(parsed);
                    OnLogLine?.Invoke(parsed);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("Agent log reader stopped after cancellation.");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Agent log reader stopped after stream error: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Append a log line to the buffer, evicting oldest entries if over capacity.
    /// </summary>
    public void Append(LogLine line)
    {
        lock (_lock)
        {
            _buffer.Enqueue(line);
            _retainedCharacters += line.Raw.Length;
            // Huge lines evict older history sooner. The event stream still
            // emits every fragment, including one larger than the history cap.
            while (_buffer.Count > _maxLines || _retainedCharacters > _maxRetainedCharacters)
                _retainedCharacters -= _buffer.Dequeue().Raw.Length;
        }
    }

    /// <summary>
    /// Append a raw string (e.g., from the app itself, not the agent process).
    /// </summary>
    public void AppendRaw(string message)
    {
        var line = new LogLine(DateTime.Now, "info", message, $"[app] {message}");
        Append(line);
        OnLogLine?.Invoke(line);
    }

    /// <summary>
    /// Get a snapshot of all buffered log lines.
    /// </summary>
    public List<LogLine> GetSnapshot()
    {
        lock (_lock)
        {
            return [.. _buffer];
        }
    }

    /// <summary>
    /// Clear the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _retainedCharacters = 0;
        }
    }

    /// <summary>
    /// Current line count.
    /// </summary>
    public int Count
    {
        get { lock (_lock) { return _buffer.Count; } }
    }
}
