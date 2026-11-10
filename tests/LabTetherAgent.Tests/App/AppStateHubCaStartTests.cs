using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

public class AppStateHubCaStartTests
{
    // The agent trusts only the managed Hub CA and stops at start when that
    // file cannot be used. The app must say so in words and start nothing,
    // or the tray would only show a stopped agent restarting again and again.
    // With the file in place the child gets the CA-only option.
    [Fact]
    public async Task UnusableManagedHubCaIsReportedInWordsAndNothingStarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var location = AppStateLocation.ForDirectory(Path.Combine(root, "state"));
        location.EnsureDirectory();
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.True(File.Exists(shell));

        var launched = new TaskCompletionSource<Dictionary<string, string?>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new AgentProcess(info =>
        {
            launched.TrySetResult(new Dictionary<string, string?>(info.Environment));
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("exit 0");
            return new System.Diagnostics.Process { StartInfo = info };
        });
        var reported = new List<string>();
        process.OnError += message => { lock (reported) reported.Add(message); };
        var settings = new AgentSettings(location) { HubUrl = "https://hub.example.com" };
        settings.TlsCaFile = settings.ManagedCaFilePath;
        var owner = new AppState(settings, new CredentialStore(false, Path.Combine(root, "credentials")),
            process, new LocalApiClient(), () => shell,
            // Never enumerate or kill unrelated PowerShell processes.
            killOrphanedAgents: _ => { });
        try
        {
            owner.StartAgentWithSettings(settings, location.DirectoryPath, completesEnrollment: false);

            string[] errors;
            lock (reported) errors = [.. reported];
            Assert.Equal(HubCertificateTrust.SavedCaUnusable, Assert.Single(errors));
            Assert.Equal(HubCertificateTrust.SavedCaUnusable, process.StartProblem);
            Assert.False(launched.Task.IsCompleted);
            Assert.False(owner.IsAgentRunning);

            using var ca = LabTetherAgent.Tests.Services.TestCertificates.Ca("Hub CA", DateTimeOffset.UtcNow);
            File.WriteAllText(settings.ManagedCaFilePath, ca.ExportCertificatePem());
            owner.StartAgentWithSettings(settings, location.DirectoryPath, completesEnrollment: false);

            var environment = await launched.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("true", environment["LABTETHER_TLS_CA_ONLY"]);
            Assert.Equal(settings.ManagedCaFilePath, environment["LABTETHER_TLS_CA_FILE"]);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (process.StartProblem != null && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            Assert.Null(process.StartProblem);
        }
        finally
        {
            owner.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
