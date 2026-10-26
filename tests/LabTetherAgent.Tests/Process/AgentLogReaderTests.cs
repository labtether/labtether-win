using LabTetherAgent.Process;
using LabTetherAgent.State;

namespace LabTetherAgent.Tests.Process;

public class AgentLogReaderTests
{
    [Fact]
    public void Append_AddsToBuffer()
    {
        var reader = new AgentLogReader();
        reader.Append(new LogLine(DateTime.Now, "info", "test", "test"));

        Assert.Equal(1, reader.Count);
    }

    [Fact]
    public void Append_RespectsMaxLines()
    {
        var reader = new AgentLogReader(maxLines: 3);
        reader.Append(new LogLine(DateTime.Now, "info", "1", "1"));
        reader.Append(new LogLine(DateTime.Now, "info", "2", "2"));
        reader.Append(new LogLine(DateTime.Now, "info", "3", "3"));
        reader.Append(new LogLine(DateTime.Now, "info", "4", "4"));

        Assert.Equal(3, reader.Count);
        var snapshot = reader.GetSnapshot();
        Assert.Equal("2", snapshot[0].Message);
        Assert.Equal("4", snapshot[2].Message);
    }

    [Fact]
    public void AppendRaw_CreatesInfoLogLine()
    {
        var reader = new AgentLogReader();
        LogLine? received = null;
        reader.OnLogLine += line => received = line;

        reader.AppendRaw("test message");

        Assert.NotNull(received);
        Assert.Equal("info", received.Level);
        Assert.Equal("test message", received.Message);
        Assert.Contains("[app]", received.Raw);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var reader = new AgentLogReader();
        reader.Append(new LogLine(DateTime.Now, "info", "test", "test"));
        reader.Clear();

        Assert.Equal(0, reader.Count);
    }

    [Fact]
    public void GetSnapshot_ReturnsIndependentCopy()
    {
        var reader = new AgentLogReader();
        reader.Append(new LogLine(DateTime.Now, "info", "1", "1"));
        var snapshot = reader.GetSnapshot();
        reader.Append(new LogLine(DateTime.Now, "info", "2", "2"));

        Assert.Single(snapshot); // snapshot is not affected
        Assert.Equal(2, reader.Count);
    }

    [Fact]
    public void OnLogLine_FiresOnAppend()
    {
        var reader = new AgentLogReader();
        var fired = false;
        reader.OnLogLine += _ => fired = true;

        reader.AppendRaw("trigger");

        Assert.True(fired);
    }

    [Fact]
    public async Task ReadAsync_ParsesStreamLines()
    {
        var reader = new AgentLogReader();
        var lines = new List<LogLine>();
        reader.OnLogLine += line => lines.Add(line);

        var text = "2026/03/21 14:30:45 line one\n2026/03/21 14:30:46 line two\n";
        using var stream = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));

        using var cts = new CancellationTokenSource();
        var task = reader.ReadAsync(stream, cts.Token);
        await task; // stream will end naturally

        Assert.Equal(2, lines.Count);
        Assert.Equal("line one", lines[0].Message);
        Assert.Equal("line two", lines[1].Message);
    }

    [Fact]
    public async Task ReadAsync_PreservesLargeTextAndSurrogatePairsWhileBoundingHistory()
    {
        const int fragmentCharacters = 64 * 1024;
        var text = new string('x', fragmentCharacters - 1) + "🦀"
            + new string('y', fragmentCharacters * 3);
        var reader = new AgentLogReader(maxRetainedBytes: fragmentCharacters * sizeof(char) * 2);
        var fragments = new List<string>();
        reader.OnLogLine += line => fragments.Add(line.Raw);
        using var stream = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));

        await reader.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(text, string.Concat(fragments));
        Assert.All(fragments, fragment =>
        {
            Assert.InRange(fragment.Length, 1, fragmentCharacters);
            Assert.False(char.IsLowSurrogate(fragment[0]));
            Assert.False(char.IsHighSurrogate(fragment[^1]));
        });
        var retained = reader.GetSnapshot();
        Assert.True(retained.Sum(line => line.Raw.Length) <= fragmentCharacters * 2);
        Assert.Equal(fragments[^1], retained[^1].Raw);
        Assert.True(retained.Count < fragments.Count);
    }

    [Fact]
    public async Task ReadAsync_PreservesBlankLinesCrLfAndExactBoundary()
    {
        var exact = new string('x', 64 * 1024);
        var reader = new AgentLogReader();
        using var stream = new StreamReader(new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(exact + "\r\n\nfirst\rsecond\n\r\nlast")), bufferSize: 128);

        await reader.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(new[] { exact, "", "first", "second", "", "last" },
            reader.GetSnapshot().Select(line => line.Raw));
    }

    [Fact]
    public async Task ReadAsync_CancelsWhileWaitingForMoreText()
    {
        using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var input = new System.IO.Pipes.AnonymousPipeClientStream(
            System.IO.Pipes.PipeDirection.In, pipe.GetClientHandleAsString());
        using var stream = new StreamReader(input);
        using var cancellation = new CancellationTokenSource();
        var reader = new AgentLogReader();
        var task = reader.ReadAsync(stream, cancellation.Token);

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(reader.GetSnapshot());
    }

    [Fact]
    public async Task ReadAsync_PublishesACompletePipeBufferWithoutWaitingForAnotherWrite()
    {
        using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var input = new System.IO.Pipes.AnonymousPipeClientStream(
            System.IO.Pipes.PipeDirection.In, pipe.GetClientHandleAsString());
        using var stream = new StreamReader(input);
        using var cancellation = new CancellationTokenSource();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new AgentLogReader();
        reader.OnLogLine += line => received.TrySetResult(line.Raw);
        var task = reader.ReadAsync(stream, cancellation.Token);
        try
        {
            await pipe.WriteAsync(System.Text.Encoding.UTF8.GetBytes(new string('x', 1023) + "\n"));
            await pipe.FlushAsync();
            Assert.Equal(new string('x', 1023), await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            cancellation.Cancel();
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
