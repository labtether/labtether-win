using System.IO.Compression;
using System.Text;
using LabTetherAgent.Api;
using LabTetherAgent.Presentation;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Services;

public class LogFragmentExportTests
{
    private const int FragmentSize = 64 * 1024;
    private const string Secret = "leftSecret4321rightSecret8765";

    [Theory]
    [InlineData("configured")]
    [InlineData("named-key")]
    [InlineData("quoted-value")]
    [InlineData("query-key")]
    public async Task CompleteFragmentedSecretsAreRedactedInViewerAndZip(string kind)
    {
        var (line, settings) = SplitSecretLine(kind);
        var reader = await ReadLogAsync(line + "\n");
        Assert.Equal(2, reader.Count);
        using var viewer = new LogViewerViewModel(reader);

        AssertSafe(viewer.BuildExportContent(settings));
        AssertSafe(await ExportZipLogAsync(reader, settings));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartialFragmentGroupCannotExposeASplitSecret(bool evictPrefix)
    {
        var (line, settings) = SplitSecretLine("configured");
        var reader = await ReadLogAsync(line + "\n", evictPrefix ? 1 : 5000);
        using var viewer = new LogViewerViewModel(reader);
        if (!evictPrefix)
            viewer.FilterText = "rightSecret8765";
        Assert.Single(viewer.FilteredLines);

        AssertSafe(viewer.BuildExportContent(settings));
        if (evictPrefix)
            AssertSafe(await ExportZipLogAsync(reader, settings));
    }

    [Fact]
    public async Task InterleavedStreamsKeepTheirOwnRedactionContext()
    {
        var (line, settings) = SplitSecretLine("configured");
        var first = (await ReadLogAsync(line + "\n")).GetSnapshot();
        var otherLine = new string('y', FragmentSize - 4) + "ordinary-tail";
        var second = (await ReadLogAsync(otherLine + "\n")).GetSnapshot();
        var reader = new AgentLogReader();
        reader.Append(first[0]);
        reader.Append(second[0]);
        reader.Append(first[1]);
        reader.Append(second[1]);
        using var viewer = new LogViewerViewModel(reader);

        var exported = viewer.BuildExportContent(settings);

        AssertSafe(exported);
        Assert.Equal(line.Replace(Secret, "[REDACTED]", StringComparison.Ordinal)
            + Environment.NewLine + otherLine, exported);
    }

    [Fact]
    public async Task CompleteNonSecretLongLineIsExportedWithoutInsertedBreaks()
    {
        var line = new string('x', FragmentSize - 1) + "🦀" + new string('y', FragmentSize);
        var reader = await ReadLogAsync(line + "\n");
        using var viewer = new LogViewerViewModel(reader);

        Assert.Equal(line, viewer.BuildExportContent(new AgentSettings()));
    }

    [Fact]
    public async Task CanceledUnterminatedGroupDoesNotExportUnredactedPrefix()
    {
        using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var input = new System.IO.Pipes.AnonymousPipeClientStream(
            System.IO.Pipes.PipeDirection.In, pipe.GetClientHandleAsString());
        using var stream = new StreamReader(input);
        using var cancellation = new CancellationTokenSource();
        var reader = new AgentLogReader();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.OnLogLine += _ => received.TrySetResult();
        var reading = reader.ReadAsync(stream, cancellation.Token);
        try
        {
            var (line, settings) = SplitSecretLine("configured");
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(line[..(FragmentSize + 1)]));
            await pipe.FlushAsync();
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await reading.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(reader.GetSnapshot());
            using var viewer = new LogViewerViewModel(reader);
            AssertSafe(viewer.BuildExportContent(settings));
        }
        finally
        {
            cancellation.Cancel();
            await reading.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static (string, AgentSettings) SplitSecretLine(string kind)
    {
        var settings = new AgentSettings();
        var payload = kind switch
        {
            "configured" => Secret,
            "named-key" => "password=" + Secret,
            "quoted-value" => "{\"password\":\"" + Secret + "\"}",
            "query-key" => "?token=" + Secret + "&safe=true",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var splitAt = kind switch
        {
            "configured" => "leftSecret4321".Length,
            "named-key" => 4,
            "quoted-value" => "{\"password\":\"leftSecret4321".Length,
            _ => 4,
        };
        if (kind == "configured")
            settings.ApiToken = Secret;
        return (new string('x', FragmentSize - splitAt - 1) + " " + payload, settings);
    }

    private static async Task<AgentLogReader> ReadLogAsync(string text, int maxLines = 5000)
    {
        var reader = new AgentLogReader(maxLines);
        using var stream = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        await reader.ReadAsync(stream, CancellationToken.None);
        return reader;
    }

    private static void AssertSafe(string exported)
    {
        Assert.DoesNotContain("leftSecret4321", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("rightSecret8765", exported, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", exported, StringComparison.Ordinal);
    }

    private static async Task<string> ExportZipLogAsync(AgentLogReader reader, AgentSettings settings)
    {
        using var api = new LocalApiClient(); // Unconfigured: no API request is made.
        var file = Path.Combine(Path.GetTempPath(), $"labtether-fragment-test-{Guid.NewGuid():N}.zip");
        try
        {
            await new DiagnosticsCollector(settings, reader, api).ExportAsync(file);
            using var archive = ZipFile.OpenRead(file);
            using var text = new StreamReader(archive.GetEntry("agent-logs.txt")!.Open());
            return await text.ReadToEndAsync();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
