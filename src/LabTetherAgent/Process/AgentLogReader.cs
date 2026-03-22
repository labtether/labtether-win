using LabTetherAgent.State;

namespace LabTetherAgent.Process;

/// <summary>
/// Reads stdout/stderr from the Go agent process, parses log lines,
/// and maintains a bounded buffer of recent entries.
/// Mirrors mac-agent LogParser + LogBuffer.
/// </summary>
public class AgentLogReader
{
    private readonly List<LogLine> _buffer = [];
    private readonly object _lock = new();
    private readonly int _maxLines;

    public event Action<LogLine>? OnLogLine;

    public AgentLogReader(int maxLines = 5000)
    {
        _maxLines = maxLines;
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
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break; // stream closed

                    var parsed = LogLine.Parse(line);
                    Append(parsed);
                    OnLogLine?.Invoke(parsed);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { } // process exited
        }, ct);
    }

    /// <summary>
    /// Append a log line to the buffer, evicting oldest entries if over capacity.
    /// </summary>
    public void Append(LogLine line)
    {
        lock (_lock)
        {
            _buffer.Add(line);
            if (_buffer.Count > _maxLines)
                _buffer.RemoveRange(0, _buffer.Count - _maxLines);
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
