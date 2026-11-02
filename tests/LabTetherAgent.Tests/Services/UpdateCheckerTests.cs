using LabTetherAgent.Services;

namespace LabTetherAgent.Tests.Services;

public class UpdateCheckerTests
{
    [Fact]
    public void DefaultUpdateUrlPointsAtWindowsAgentRepository()
    {
        Assert.Equal(
            "https://api.github.com/repos/labtether/labtether-win/releases/latest",
            UpdateChecker.DefaultUpdateUrl
        );
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    // Versions compare as numbers, not text (as text, 1.9.0 sorts above
    // 1.10.0), and a 1.7.1 app must be offered the 2.0.0 release.
    [InlineData("2.0.0", "1.7.1", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.9.0", "1.10.0", false)]
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

    [Fact]
    public void ExtractLatestVersion_ReadsGitHubReleaseTagName()
    {
        const string json = """{"tag_name":"v1.2.3"}""";

        Assert.Equal("1.2.3", UpdateChecker.ExtractLatestVersion(json));
    }
}
