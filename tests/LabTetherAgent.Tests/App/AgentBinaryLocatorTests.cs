using LabTetherAgent.App;

namespace LabTetherAgent.Tests.App;

public class AgentBinaryLocatorTests
{
    [Fact]
    public void FindsPackagedAssetBeforeLegacyRootBinary()
    {
        WithTemporaryDirectory(directory =>
        {
            var assetsDirectory = Path.Combine(directory, "Assets");
            Directory.CreateDirectory(assetsDirectory);
            var packaged = Path.Combine(assetsDirectory, "labtether-agent.exe");
            var legacy = Path.Combine(directory, "labtether-agent.exe");
            File.WriteAllText(packaged, "packaged");
            File.WriteAllText(legacy, "legacy");

            Assert.Equal(packaged, AppState.FindAgentBinary(directory));
        });
    }

    [Fact]
    public void SupportsLegacyRootBinaryInsideApplicationDirectory()
    {
        WithTemporaryDirectory(directory =>
        {
            var legacy = Path.Combine(directory, "labtether-agent.exe");
            File.WriteAllText(legacy, "legacy");

            Assert.Equal(legacy, AppState.FindAgentBinary(directory));
        });
    }

    [Fact]
    public void DoesNotSearchParentDirectory()
    {
        WithTemporaryDirectory(directory =>
        {
            var appDirectory = Path.Combine(directory, "app");
            Directory.CreateDirectory(appDirectory);
            File.WriteAllText(Path.Combine(directory, "labtether-agent.exe"), "untrusted parent");

            Assert.Null(AppState.FindAgentBinary(appDirectory));
        });
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"labtether-agent-locator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
