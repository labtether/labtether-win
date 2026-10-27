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
        Assert.Contains("Setup / Re-enroll", viewModel.SaveError);
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
