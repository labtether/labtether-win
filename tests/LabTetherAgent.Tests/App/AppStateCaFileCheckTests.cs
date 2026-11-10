using LabTetherAgent.Api;
using LabTetherAgent.App;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.App;

// A CA file chosen by hand becomes the agent's only root once it has verified
// the Hub on its own, and stays so. Only a file that was never checked (an
// install updated from an earlier version) keeps the earlier trust, so the
// update cannot take a working device offline.
public sealed class AppStateCaFileCheckTests : IDisposable
{
    private const string Hub = "https://hub.example.com";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
    private readonly AppStateLocation _location;
    private readonly string _caFile;
    private readonly List<System.Diagnostics.ProcessStartInfo> _launched = [];

    public AppStateCaFileCheckTests()
    {
        _location = AppStateLocation.ForDirectory(Path.Combine(_root, "state"));
        _location.EnsureDirectory();
        _caFile = Path.Combine(_root, "hub-ca.pem");
        WriteCa(_caFile, "Hub CA");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // Never checked: the Hub cannot be reached, or says the file does not
    // match. The agent keeps the earlier trust, the app asks again at the
    // next launch and after a reconnect, and a mismatch is shown in Settings.
    // Verified: the record is made and nothing asks or falls back after that.
    [Fact]
    public async Task NeverCheckedCaFileKeepsEarlierTrustUntilItVerifiesTheHub()
    {
        var settings = new AgentSettings(_location) { HubUrl = Hub, TlsCaFile = _caFile };
        using var owner = Owner(settings);
        var verdict = HubCaFileVerdict.NotChecked;
        var asked = 0;
        owner.CheckCaFileOverride = (hub, file) =>
        {
            asked++;
            Assert.Equal((Hub, _caFile), (hub, file));
            return Task.FromResult(verdict);
        };
        string[] Log() => [.. owner.AgentProcess.LogReader.GetSnapshot().Select(line => line.Message)];
        bool OnlyRoot() => AgentEnvironmentBuilder.BuildEnvironment(settings, "8091", "auth")
            .ContainsKey("LABTETHER_TLS_CA_ONLY");

        await owner.CheckSavedCaFileAsync();
        Assert.False(OnlyRoot());
        Assert.Equal(HubCaFileCheck.StartNotChecked, Assert.Single(Log()));
        Assert.Null(owner.CaFileNotice);

        // A reconnect asks again.
        owner.OnHubConnectionChanged(connected: true);
        Assert.Equal(2, asked);

        verdict = HubCaFileVerdict.DoesNotMatch;
        owner.OnHubConnectionChanged(connected: true);
        Assert.Equal(3, asked);
        Assert.False(OnlyRoot());
        Assert.Equal("This CA certificate does not match this Hub's certificate.", owner.CaFileNotice);
        Assert.Equal(HubCaFileCheck.StartDoesNotMatch, Log()[^1]);
        // Shown once: later reconnects leave it at that until Settings or a launch asks.
        owner.OnHubConnectionChanged(connected: true);
        Assert.Equal(3, asked);
        Assert.Empty(AgentSettings.Load(_location).EnrolledHubCaFingerprint);

        verdict = HubCaFileVerdict.Verified;
        await owner.RecheckCaFileAsync();
        Assert.Equal(4, asked);
        Assert.True(OnlyRoot());
        Assert.Null(owner.CaFileNotice);
        Assert.Equal(HubCaFileCheck.NowOnlyRoot, Log()[^1]);
        Assert.Equal(HubCaFileCheck.Fingerprint(_caFile), AgentSettings.Load(_location).EnrolledHubCaFingerprint);

        // Verified, then the Hub cannot be reached or seems not to match:
        // nobody asks, and the CA stays the only root.
        verdict = HubCaFileVerdict.NotChecked;
        await owner.CheckSavedCaFileAsync();
        owner.OnHubConnectionChanged(connected: true);
        Assert.Equal(4, asked);
        Assert.True(OnlyRoot());
    }

    // A CA file on record that is gone, or is no longer that CA, stops the
    // start with words. It never starts the agent with Windows' roots added.
    [Fact]
    public void CaFileOnRecordThatIsGoneOrChangedStopsTheStartWithWords()
    {
        var settings = new AgentSettings(_location)
        {
            HubUrl = Hub,
            TlsCaFile = _caFile,
            EnrolledHubCaFingerprint = HubCaFileCheck.Fingerprint(_caFile),
        };
        using var owner = Owner(settings);
        var reported = new List<string>();
        owner.AgentProcess.OnError += message => { lock (reported) reported.Add(message); };

        WriteCa(_caFile, "Another CA");
        owner.StartAgentWithSettings(settings, _location.DirectoryPath, completesEnrollment: false);
        File.Delete(_caFile);
        owner.StartAgentWithSettings(settings, _location.DirectoryPath, completesEnrollment: false);

        lock (reported)
        {
            Assert.Equal(2, reported.Count);
            Assert.Equal(HubCaFileCheck.FileChanged, reported[0]);
            Assert.Equal(HubCertificateTrust.SavedCaUnusable, reported[1]);
        }
        Assert.Empty(_launched);
        Assert.False(owner.IsAgentRunning);
        Assert.Equal(HubCertificateTrust.SavedCaUnusable, owner.AgentProcess.StartProblem);
    }

    // An update must not weaken what an earlier build saved. labtether-win
    // main before this change wrote the record for Trust this Hub and for a
    // CA file chosen in setup, both verified with that CA alone. Both load as
    // the agent's only root, with no new question to the Hub. A CA file saved
    // without a record (chosen in Settings, or by 1.x) was never checked: it
    // keeps the earlier trust until the Hub has been asked.
    [Fact]
    public async Task SettingsAnEarlierBuildSavedKeepTheirVerifiedState()
    {
        using var hubCa = LabTetherAgent.Tests.Services.TestCertificates.Ca("Hub CA", DateTimeOffset.UtcNow);
        var managed = Path.Combine(_location.DirectoryPath, "ca.crt");
        File.WriteAllText(managed, hubCa.ExportCertificatePem());
        var shapes = new (string Name, string CaFile, string Record, bool OnlyRoot)[]
        {
            ("Trust this Hub", managed, HubCertificateTrust.Fingerprint(hubCa), true),
            ("CA file chosen in setup", _caFile, HubCaFileCheck.Fingerprint(_caFile), true),
            ("CA file never checked", _caFile, "", false),
        };
        foreach (var shape in shapes)
        {
            var saved = new Dictionary<string, string>
            {
                ["HubUrl"] = "wss://hub.example.com/ws/agent",
                ["AssetId"] = "nas01",
                ["TlsCaFile"] = shape.CaFile,
            };
            if (shape.Record.Length > 0)
                saved["EnrolledHubCaFingerprint"] = shape.Record;
            File.WriteAllText(_location.SettingsPath, System.Text.Json.JsonSerializer.Serialize(saved));

            var settings = AgentSettings.Load(_location);
            using var owner = Owner(settings);
            var asked = 0;
            owner.CheckCaFileOverride = (_, _) =>
            {
                asked++;
                return Task.FromResult(HubCaFileVerdict.NotChecked);
            };
            await owner.CheckSavedCaFileAsync();
            var env = AgentEnvironmentBuilder.BuildEnvironment(settings, "8091", "auth");

            Assert.True(shape.OnlyRoot == env.ContainsKey("LABTETHER_TLS_CA_ONLY"), shape.Name);
            Assert.True(shape.OnlyRoot == (asked == 0), shape.Name);
            Assert.True(AgentEnvironmentBuilder.HubCaStartProblem(settings, env) == null, shape.Name);
            Assert.Equal(shape.Record, AgentSettings.Load(_location).EnrolledHubCaFingerprint);
        }
    }

    private AppState Owner(AgentSettings settings)
    {
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var process = new AgentProcess(info =>
        {
            lock (_launched) _launched.Add(info);
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("exit 0");
            return new System.Diagnostics.Process { StartInfo = info };
        });
        return new AppState(settings, new CredentialStore(false, Path.Combine(_root, "credentials")),
            process, new LocalApiClient(), () => shell,
            // Never enumerate or kill unrelated PowerShell processes.
            killOrphanedAgents: _ => { });
    }

    private static void WriteCa(string path, string name)
    {
        using var ca = LabTetherAgent.Tests.Services.TestCertificates.Ca(name, DateTimeOffset.UtcNow);
        File.WriteAllText(path, ca.ExportCertificatePem());
    }
}
