using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

public class AppStateCaFileCheckTests
{
    // An install updated from an earlier version may hold a CA file chosen by
    // hand that was never checked against the Hub. Making it the agent's only
    // root unchecked could take a working device offline, so at launch the
    // app asks the Hub once: only a file that verifies the Hub on its own is
    // turned into the only root, anything else keeps the earlier trust, says
    // so in one log line and is asked again at the next launch.
    [Fact]
    public async Task SavedCaFileBecomesTheOnlyRootOnlyAfterItVerifiesTheHub()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var location = AppStateLocation.ForDirectory(Path.Combine(root, "state"));
        location.EnsureDirectory();
        var caFile = Path.Combine(root, "hub-ca.pem");
        File.WriteAllText(caFile, "a CA file saved by an earlier version");
        var settings = new AgentSettings(location) { HubUrl = "https://hub.example.com", TlsCaFile = caFile };
        var owner = new AppState(settings, new CredentialStore(false, Path.Combine(root, "credentials")),
            new AgentProcess(location), new LocalApiClient(), () => null);
        try
        {
            var verdict = HubCaFileVerdict.NotChecked;
            var asked = 0;
            owner.CheckCaFileOverride = (hub, file) =>
            {
                asked++;
                Assert.Equal(("https://hub.example.com", caFile), (hub, file));
                return Task.FromResult(verdict);
            };
            string[] Log() => [.. owner.AgentProcess.LogReader.GetSnapshot().Select(line => line.Message)];
            bool OnlyRoot() => AgentEnvironmentBuilder.BuildEnvironment(settings, "8091", "auth")
                .ContainsKey("LABTETHER_TLS_CA_ONLY");

            await owner.CheckSavedCaFileAsync();
            Assert.False(OnlyRoot());
            Assert.Equal(HubCaFileCheck.StartNotChecked, Assert.Single(Log()));

            verdict = HubCaFileVerdict.DoesNotMatch;
            await owner.CheckSavedCaFileAsync();
            Assert.False(OnlyRoot());
            Assert.Equal(HubCaFileCheck.StartDoesNotMatch, Log()[^1]);
            Assert.Empty(AgentSettings.Load(location).VerifiedCaFileSha256);

            verdict = HubCaFileVerdict.Verified;
            await owner.CheckSavedCaFileAsync();
            Assert.True(OnlyRoot());
            Assert.Equal(HubCaFileCheck.NowOnlyRoot, Log()[^1]);
            Assert.True(AgentSettings.Load(location).UsesVerifiedCaFile);

            // Checked once: the next launch does not ask again.
            await owner.CheckSavedCaFileAsync();
            Assert.Equal(3, asked);
        }
        finally
        {
            owner.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
