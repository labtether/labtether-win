using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void IsNewerVersion(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewerVersion(candidate, current));
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v2.0", "1.0.0", true)]
    [InlineData("1", "0", true)]
    public void IsNewerVersion_HandlesVariousFormats(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewerVersion(candidate, current));
    }
}
