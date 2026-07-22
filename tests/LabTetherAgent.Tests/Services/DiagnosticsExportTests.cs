using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class DiagnosticsExportTests
{
    [Fact]
    public void SuggestedFileNameIsAPlainWindowsSafeBasename()
    {
        var name = DiagnosticsExport.CreateSuggestedFileName(
            new DateTimeOffset(2026, 7, 15, 11, 41, 5, TimeSpan.FromHours(10)));

        Assert.Equal("labtether-diagnostics-20260715-114105", name);
        Assert.All(
            Path.GetInvalidFileNameChars(),
            invalid => Assert.DoesNotContain(invalid.ToString(), name, StringComparison.Ordinal));
        Assert.Equal(name, Path.GetFileName(name));
        Assert.False(Path.IsPathRooted(name));
    }
}
