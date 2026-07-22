using LabTetherAgent.App;

namespace LabTetherAgent.Tests.App;

public class ProgramTests
{
    [Theory]
    [InlineData("--winui-runtime-probe")]
    [InlineData("--WINUI-RUNTIME-PROBE")]
    public void HasWinUiRuntimeProbeArgument_AcceptsExactProbeArgument(string argument)
    {
        Assert.True(Program.HasWinUiRuntimeProbeArgument([argument]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--winui-runtime-probe-extra")]
    [InlineData("winui-runtime-probe")]
    public void HasWinUiRuntimeProbeArgument_RejectsOtherArguments(string argument)
    {
        Assert.False(Program.HasWinUiRuntimeProbeArgument([argument]));
    }

    [Fact]
    public void NormalizeWorkingDirectory_UsesPublishedApplicationDirectory()
    {
        var appDirectory = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            "publish",
            ".");
        string? selectedDirectory = null;

        var normalized = Program.NormalizeWorkingDirectory(
            appDirectory,
            path => selectedDirectory = path);

        var expected = Path.GetFullPath(appDirectory);
        Assert.Equal(expected, normalized);
        Assert.Equal(expected, selectedDirectory);
    }
}
