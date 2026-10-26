using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

public class AppStateRestartFailureTests
{
    // A crash restart runs detached on the thread pool. If preparing the
    // relaunch throws (for example a state file cannot be written), the
    // failure must reach the user through OnError instead of silently
    // leaving the agent down or ending the tray app.
    [Fact]
    public async Task FailedCrashRestartIsReportedAndLeavesAgentStopped()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var location = AppStateLocation.ForDirectory(Path.Combine(root, "state"));
        location.EnsureDirectory();
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(shell));

        var launches = 0;
        var process = new AgentProcess(info =>
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("exit 7");
            return new System.Diagnostics.Process { StartInfo = info };
        });
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OnError += message => reported.TrySetResult(message);
        var settings = new AgentSettings(location) { HubUrl = "https://restart-failure.invalid" };
        var owner = new AppState(settings, new CredentialStore(false, Path.Combine(root, "credentials")),
            process, new LocalApiClient(),
            () => Interlocked.Increment(ref launches) == 1
                ? shell
                : throw new IOException("simulated relaunch failure"),
            // Never enumerate or kill unrelated PowerShell processes.
            killOrphanedAgents: _ => { });
        try
        {
            owner.StartAgentWithSettings(settings, location.DirectoryPath, completesEnrollment: false);

            var message = await reported.Task.WaitAsync(TimeSpan.FromSeconds(15));

            Assert.Contains("could not be started", message, StringComparison.Ordinal);
            Assert.Contains("simulated relaunch failure", message, StringComparison.Ordinal);
            Assert.Equal(2, Volatile.Read(ref launches));
            Assert.False(owner.IsAgentRunning);
        }
        finally
        {
            owner.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
