using LabTetherAgent.Presentation;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Presentation;

public class LogViewerViewModelTests
{
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
    public void OpenViewerEvictsOldLinesWithTheSourceBuffer()
    {
        var reader = new AgentLogReader(maxLines: 3);
        using var viewModel = new LogViewerViewModel(reader) { FilterText = "keep" };
        reader.AppendRaw("keep expired");
        reader.AppendRaw("keep current");
        reader.AppendRaw("skip one");
        reader.AppendRaw("skip two");

        Assert.Equal(3, viewModel.TotalCount);
        Assert.Equal("keep current", Assert.Single(viewModel.FilteredLines).Message);
        viewModel.FilterText = string.Empty;
        Assert.Equal(reader.GetSnapshot(), viewModel.FilteredLines);
    }

    [Fact]
    public void BackgroundLogBurstQueuesOneBoundedRefresh()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new QueuedSynchronizationContext();
        var reader = new AgentLogReader(maxLines: 3);
        LogViewerViewModel viewModel;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            viewModel = new LogViewerViewModel(reader);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        using (viewModel)
        {
            for (var index = 0; index < 1_000; index++)
                reader.AppendRaw($"line {index}");

            var update = Assert.Single(context.Pending);
            update();
            Assert.Equal(3, viewModel.TotalCount);
            Assert.Equal(reader.GetSnapshot(), viewModel.FilteredLines);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        public List<Action> Pending { get; } = [];

        public override void Post(SendOrPostCallback callback, object? state) =>
            Pending.Add(() => callback(state));
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
}
