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
