using LabTetherAgent.Presentation;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Presentation;

public class LogViewerViewModelTests
{
    [Fact]
    public void BusyViewerCoalescesEventsAndUsesReadersRetainedWindow()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new QueuedContext();
        var reader = new AgentLogReader(maxLines: 3);
        SynchronizationContext.SetSynchronizationContext(context);
        using var viewModel = new LogViewerViewModel(reader) { FilterText = "keep" };
        var signals = 0;
        viewModel.OnNewLine += () => signals++;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            for (var index = 0; index < 1000; index++)
                reader.AppendRaw($"{(index % 2 == 1 ? "keep" : "skip")}-{index}");

            Assert.Equal(1, context.PendingCount);
            context.Drain();
            Assert.Equal(3, viewModel.TotalCount);
            Assert.Equal(new[] { "keep-997", "keep-999" }, viewModel.FilteredLines.Select(line => line.Message));
            Assert.Equal(1, signals);

            for (var index = 1000; index < 1003; index++) reader.AppendRaw($"skip-{index}");
            Assert.Equal(1, context.PendingCount);
            context.Drain();
            Assert.Empty(viewModel.FilteredLines);
            Assert.Equal(3, viewModel.TotalCount);
            Assert.Equal(1, signals); // Eviction without a new match does not scroll.

            viewModel.FilterText = "";
            Assert.Equal(new[] { "skip-1000", "skip-1001", "skip-1002" }, viewModel.FilteredLines.Select(line => line.Message));
            var export = viewModel.BuildExportContent(new AgentSettings());
            Assert.DoesNotContain("keep-997", export, StringComparison.Ordinal);
            Assert.Contains("skip-1002", export, StringComparison.Ordinal);

            reader.AppendRaw("pending-at-close");
            viewModel.Dispose();
            context.Drain();
            reader.AppendRaw("after-close");
            Assert.Equal(0, context.PendingCount);
            Assert.Equal("skip-1002", viewModel.FilteredLines[^1].Message);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    [Fact]
    public void ClosedViewerStopsReceivingProcessLogEvents()
    {
        var reader = new AgentLogReader();
        var viewModel = new LogViewerViewModel(reader);
        reader.AppendRaw("visible before close");
        Assert.Equal(1, viewModel.TotalCount);

        viewModel.Dispose();
        reader.AppendRaw("must not reach closed viewer");

        Assert.Equal(1, viewModel.TotalCount);
        Assert.DoesNotContain(
            viewModel.FilteredLines,
            line => line.Raw.Contains("must not reach", StringComparison.Ordinal));
    }

    [Fact]
    public void NewMatchingLineSignalsAutoScrollConsumer()
    {
        var reader = new AgentLogReader();
        using var viewModel = new LogViewerViewModel(reader);
        var signals = 0;
        viewModel.OnNewLine += () => signals++;

        reader.AppendRaw("line from child");

        Assert.Equal(1, signals);
        Assert.Single(viewModel.FilteredLines);
    }

    [Fact]
    public void ExportContentRedactsKnownAndStructuredSecrets()
    {
        var reader = new AgentLogReader();
        using var viewModel = new LogViewerViewModel(reader);
        reader.AppendRaw("Authorization: Bearer configured-secret");
        reader.AppendRaw("{\"password\":\"json-secret\"}");
        var settings = new AgentSettings { ApiToken = "configured-secret" };

        var exported = viewModel.BuildExportContent(settings);

        Assert.DoesNotContain("configured-secret", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", exported, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", exported, StringComparison.Ordinal);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
        public int PendingCount => _pending.Count;
        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));

        public void Drain()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_pending.TryDequeue(out var work)) work.Callback(work.State);
            }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
