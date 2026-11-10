using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Presentation;

public class SettingsViewModelTests
{
    [Fact]
    public void LoadsActualWindowsLoginRegistrationInsteadOfStaleJsonValue()
    {
        var settings = new AgentSettings { StartAtLogin = false };
        var loginItems = new FakeLoginItemManager { Enabled = true };
        var viewModel = Create(settings, loginItems, out _);

        Assert.True(viewModel.StartAtLogin);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void SaveAppliesAndVerifiesWindowsLoginRegistrationBeforePersisting()
    {
        var settings = new AgentSettings { StartAtLogin = false };
        var loginItems = new FakeLoginItemManager { Enabled = false, SetResult = true };
        var viewModel = Create(settings, loginItems, out var persisted);

        viewModel.StartAtLogin = true;
        viewModel.SaveCommand.Execute(null);

        Assert.True(loginItems.Enabled);
        Assert.True(settings.StartAtLogin);
        Assert.Single(persisted);
        Assert.False(viewModel.IsDirty);
        Assert.Null(viewModel.SaveError);
    }

    [Fact]
    public void FailedWindowsLoginRegistrationLeavesSettingsDirtyAndUnpersisted()
    {
        var settings = new AgentSettings { StartAtLogin = false };
        var loginItems = new FakeLoginItemManager { Enabled = false, SetResult = false };
        var viewModel = Create(settings, loginItems, out var persisted);

        viewModel.StartAtLogin = true;
        viewModel.SaveCommand.Execute(null);

        Assert.False(settings.StartAtLogin);
        Assert.Empty(persisted);
        Assert.True(viewModel.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SaveError));
    }

    [Fact]
    public void InvalidHubUrlCannotBeSavedOrChangeLoginRegistration()
    {
        var settings = new AgentSettings { HubUrl = "wss://valid.example/ws/agent" };
        var loginItems = new FakeLoginItemManager { Enabled = false };
        var viewModel = Create(settings, loginItems, out var persisted);

        viewModel.HubUrl = "not a hub URL";
        viewModel.StartAtLogin = true;
        viewModel.SaveCommand.Execute(null);

        Assert.Equal("wss://valid.example/ws/agent", settings.HubUrl);
        Assert.False(loginItems.Enabled);
        Assert.Equal(0, loginItems.SetCalls);
        Assert.Empty(persisted);
        Assert.True(viewModel.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SaveError));
    }

    [Fact]
    public void MissingCustomCaCannotBeSavedOrChangeLoginRegistration()
    {
        var settings = new AgentSettings { HubUrl = "wss://valid.example/ws/agent" };
        var loginItems = new FakeLoginItemManager { Enabled = false };
        var viewModel = Create(settings, loginItems, out var persisted);
        viewModel.TlsCaFile = Path.Combine(
            Path.GetTempPath(),
            $"missing-labtether-ca-{Guid.NewGuid():N}.pem");
        viewModel.StartAtLogin = true;

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(string.Empty, settings.TlsCaFile);
        Assert.Equal(0, loginItems.SetCalls);
        Assert.Empty(persisted);
        Assert.True(viewModel.IsDirty);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SaveError));
    }

    [Fact]
    public void ConflictingTlsTrustOptionsCannotBeSavedOrChangeLoginRegistration()
    {
        var settings = new AgentSettings { HubUrl = "wss://valid.example/ws/agent" };
        var loginItems = new FakeLoginItemManager { Enabled = false };
        var viewModel = Create(settings, loginItems, out var persisted);
        viewModel.TlsSkipVerify = true;
        viewModel.TlsCaFile = @"C:\LabTether\ca.pem";
        viewModel.StartAtLogin = true;

        viewModel.SaveCommand.Execute(null);

        Assert.False(settings.TlsSkipVerify);
        Assert.Equal(string.Empty, settings.TlsCaFile);
        Assert.Equal(0, loginItems.SetCalls);
        Assert.Empty(persisted);
        Assert.True(viewModel.IsDirty);
        Assert.Contains("not both", viewModel.SaveError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidPemCustomCaCannotBeSavedOrChangeLoginRegistration()
    {
        var settings = new AgentSettings { HubUrl = "wss://valid.example/ws/agent" };
        var loginItems = new FakeLoginItemManager { Enabled = false };
        var viewModel = Create(settings, loginItems, out var persisted);
        var invalidCa = Path.Combine(
            Path.GetTempPath(),
            $"invalid-labtether-ca-{Guid.NewGuid():N}.pem");

        try
        {
            File.WriteAllText(invalidCa, "this is not a PEM certificate");
            viewModel.TlsCaFile = invalidCa;
            viewModel.StartAtLogin = true;

            viewModel.SaveCommand.Execute(null);

            Assert.Equal(string.Empty, settings.TlsCaFile);
            Assert.Equal(0, loginItems.SetCalls);
            Assert.Empty(persisted);
            Assert.True(viewModel.IsDirty);
            Assert.Contains("valid PEM", viewModel.SaveError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(invalidCa);
        }
    }

    [Fact]
    public void HubChangeIsRefusedWhileATokenForTheCurrentHubIsSaved()
    {
        // Risk: saving another Hub's URL sends this Hub's agent token there,
        // and that Hub can replay it to act as this device.
        var settings = new AgentSettings
        {
            HubUrl = "wss://nas01.example.com:8443/ws/agent",
            ApiToken = "nas01-agent-token",
        };
        var loginItems = new FakeLoginItemManager { Enabled = false };
        var viewModel = Create(settings, loginItems, out var persisted);

        viewModel.HubUrl = "https://NAS01.example.com:8443";
        viewModel.SaveCommand.Execute(null);
        Assert.Null(viewModel.SaveError);
        Assert.Single(persisted);

        viewModel.HubUrl = "wss://192.0.2.10:8443/ws/agent";
        viewModel.SaveCommand.Execute(null);
        Assert.Equal("https://NAS01.example.com:8443", settings.HubUrl);
        Assert.Single(persisted);
        Assert.True(viewModel.IsDirty);
        Assert.Contains("Re-enroll in the tray menu", viewModel.SaveError);
    }

    // Before setup Settings showed the placeholder Hub "wss://localhost:8443/ws/agent".
    [Fact]
    public void BeforeSetupHubUrlIsEmptyAndOtherSettingsStillSave()
    {
        // Not the installed app's token: this PC may have an enrolled copy.
        var settings = new AgentSettings
        {
            PersistedAgentTokenPathOverride = Path.Combine(
                Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"), "agent-token"),
        };
        var stored = settings.HubUrl;
        var viewModel = Create(settings, new FakeLoginItemManager(), out var persisted);

        Assert.Equal("", viewModel.HubUrl);
        viewModel.LowPowerMode = true;
        viewModel.SaveCommand.Execute(null);

        Assert.Null(viewModel.SaveError);
        Assert.Single(persisted);
        Assert.Equal(stored, settings.HubUrl);
        Assert.True(settings.LowPowerMode);
    }

    // The token was issued under one Hub CA. Settings must not point it at
    // another CA or switch the certificate check off; that needs Re-enroll.
    [Fact]
    public void SavedTokenKeepsTheHubCertificateItEnrolledWith()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var enrolledCa = LabTetherAgent.Tests.Services.TestCertificates.Ca("Hub CA", DateTimeOffset.UtcNow);
            using var otherCa = LabTetherAgent.Tests.Services.TestCertificates.Ca("Another CA", DateTimeOffset.UtcNow);
            var enrolledFile = Path.Combine(root, "hub-ca.pem");
            var otherFile = Path.Combine(root, "other-ca.pem");
            File.WriteAllText(enrolledFile, enrolledCa.ExportCertificatePem());
            File.WriteAllText(otherFile, otherCa.ExportCertificatePem());
            var settings = new AgentSettings
            {
                HubUrl = "wss://nas01.example.com:8443/ws/agent",
                ApiToken = "nas01-agent-token",
                TlsCaFile = enrolledFile,
                EnrolledHubCaFingerprint = LabTetherAgent.Services.HubCertificateTrust.Fingerprint(enrolledCa),
            };
            var viewModel = Create(settings, new FakeLoginItemManager(), out var persisted);

            viewModel.TlsCaFile = otherFile;
            viewModel.SaveCommand.Execute(null);
            Assert.Contains("Re-enroll in the tray menu", viewModel.SaveError);
            viewModel.TlsCaFile = string.Empty;
            viewModel.TlsSkipVerify = true;
            viewModel.SaveCommand.Execute(null);
            Assert.Contains("Re-enroll in the tray menu", viewModel.SaveError);
            Assert.Empty(persisted);
            Assert.Equal(enrolledFile, settings.TlsCaFile);
            Assert.False(settings.TlsSkipVerify);

            // Other settings still save while the certificate stays the same.
            viewModel.TlsSkipVerify = false;
            viewModel.TlsCaFile = enrolledFile;
            viewModel.LowPowerMode = true;
            viewModel.SaveCommand.Execute(null);
            Assert.Null(viewModel.SaveError);
            Assert.Single(persisted);

            // A CA file replaced in place is not a Settings change: a later
            // save of something else is not refused over it.
            File.WriteAllText(enrolledFile, otherCa.ExportCertificatePem());
            viewModel.LowPowerMode = false;
            viewModel.SaveCommand.Execute(null);
            Assert.Null(viewModel.SaveError);
            Assert.Equal(2, persisted.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SettingsViewModel Create(
        AgentSettings settings,
        FakeLoginItemManager loginItems,
        out List<AgentSettings> persisted)
    {
        persisted = [];
        var persistenceSink = persisted;
        var credentialPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            ".credentials");
        var credentialStore = new CredentialStore(vaultAvailable: false, fallbackPath: credentialPath);
        return new SettingsViewModel(
            settings,
            credentialStore,
            loginItems,
            (saved, _) => persistenceSink.Add(saved));
    }

    private sealed class FakeLoginItemManager : ILoginItemManager
    {
        public bool Enabled { get; set; }
        public bool SetResult { get; set; } = true;
        public int SetCalls { get; private set; }

        public bool IsEnabled() => Enabled;

        public bool SetEnabled(bool enabled)
        {
            SetCalls++;
            if (!SetResult) return false;
            Enabled = enabled;
            return true;
        }
    }
}
