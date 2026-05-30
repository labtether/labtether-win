using LabTetherAgent.Process;

namespace LabTetherAgent.Tests.Process;

public class AgentProcessTests
{
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
