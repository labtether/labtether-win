using System.Diagnostics;
using System.Runtime.InteropServices;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;
using Xunit.Abstractions;

namespace LabTetherAgent.Tests.Process;

// Stop must let the real agent end by itself (its own clean stop, exit code
// 0), not kill it. Runs the bundled Go agent against a stand-in Hub on
// loopback. The app sent CTRL_BREAK to a child in another console, which
// cannot arrive, so every stop was a kill.
// The test host takes the tray app's place in the agent's console while the
// break is sent; an earlier version of the fix ended the host here.
public class AgentGracefulStopTests(ITestOutputHelper output)
{
    [Fact]
    public async Task StopLetsTheAgentExitByItself()
    {
        var binary = Environment.GetEnvironmentVariable("LABTETHER_TEST_AGENT_EXE") is { Length: > 0 } given
            ? given
            : AppState.FindAgentBinary(AppContext.BaseDirectory);
        if (binary == null)
        {
            // CI builds with the bundled agent; a plain local build may not have it.
            output.WriteLine("No bundled agent next to the tests. Build it with scripts/build-bundled-agent.sh.");
            return;
        }

        // The tray app has no console of its own; give up the test host's so
        // the stop runs as it does in the app.
        FreeConsole();
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        using var hub = new StandInHub(19500, 19549);
        try
        {
            var settings = new AgentSettings(AppStateLocation.ForDirectory(root))
            {
                HubUrl = $"ws://127.0.0.1:{hub.Port}/ws/agent",
                // Not a secret: only this stand-in Hub sees it.
                ApiToken = "stand-in-hub-token",
                DockerEnabled = "false",
            };
            var env = AgentEnvironmentBuilder.BuildEnvironment(
                settings, StandInHub.FreePort(19550, 19599).ToString(), "local-test-auth");
            using var agent = new AgentProcess();
            var exited = new TaskCompletionSource<AgentProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
            agent.OnOwnedExited += exit => exited.TrySetResult(exit);

            agent.Start(binary, env);
            await hub.Connected.WaitAsync(TimeSpan.FromSeconds(60));
            var clock = Stopwatch.StartNew();
            await agent.StopAsync();
            clock.Stop();
            var exit = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var ending = await hub.Ended.WaitAsync(TimeSpan.FromSeconds(10));
            var log = agent.LogReader.GetSnapshot().Select(line => line.Raw).ToList();

            output.WriteLine($"stop took {clock.ElapsedMilliseconds} ms; exit code {exit.ExitCode}; Hub saw: {ending}");
            foreach (var line in log.TakeLast(12)) output.WriteLine(line);
            Assert.Equal(0, exit.ExitCode);
            // The agent's own shutdown lines (runtime.go), which a kill never prints.
            Assert.Contains(log, line => line.EndsWith("collector stopped", StringComparison.Ordinal) ||
                line.EndsWith("heartbeat loop stopped", StringComparison.Ordinal));
            Assert.DoesNotContain(log, line => line.Contains("forcing kill", StringComparison.Ordinal));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Stop took {clock.Elapsed}.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}
