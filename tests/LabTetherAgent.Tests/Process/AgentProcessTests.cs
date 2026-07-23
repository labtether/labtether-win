using LabTetherAgent.Process;

namespace LabTetherAgent.Tests.Process;

public class AgentProcessTests
{
    [Fact]
    public void CreateStartInfoLaunchesAgentDaemonWithoutCliArguments()
    {
        var info = AgentProcess.CreateStartInfo(
            @"C:\Program Files\LabTether\Assets\labtether-agent.exe",
            new Dictionary<string, string> { ["LABTETHER_API_TOKEN_FILE"] = @"C:\secure\token" }
        );

        Assert.Empty(info.Arguments);
        Assert.Empty(info.ArgumentList);
        Assert.Equal(@"C:\secure\token", info.Environment["LABTETHER_API_TOKEN_FILE"]);
        Assert.Equal(Environment.ProcessId.ToString(), info.Environment["LABTETHER_PARENT_PID"]);
        Assert.Equal("false", info.Environment["LABTETHER_AUTO_UPDATE"]);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void CreateStartInfoOverridesChildSelfUpdateAndParentLifecycleValues()
    {
        var info = AgentProcess.CreateStartInfo(
            @"C:\Program Files\LabTether\Assets\labtether-agent.exe",
            new Dictionary<string, string>
            {
                ["LABTETHER_PARENT_PID"] = "1",
                ["LABTETHER_AUTO_UPDATE"] = "true",
            }
        );

        Assert.Equal(Environment.ProcessId.ToString(), info.Environment["LABTETHER_PARENT_PID"]);
        Assert.Equal("false", info.Environment["LABTETHER_AUTO_UPDATE"]);
    }

    [Fact]
    public void StartCleansProcessStateWhenExecutableCannotLaunch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"labtether-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var invalidExe = Path.Combine(tempDir, "labtether-agent.exe");
            File.WriteAllText(invalidExe, "not a windows executable");

            using var process = new AgentProcess();
            string? error = null;
            process.OnError += message => error = message;

            process.Start(invalidExe, []);

            Assert.False(process.IsStarting);
            Assert.False(process.IsRunning);
            Assert.Contains("Failed to start agent", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
