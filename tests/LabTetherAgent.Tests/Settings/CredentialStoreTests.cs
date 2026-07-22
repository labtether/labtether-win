using System.Security.AccessControl;
using System.Text;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class CredentialStoreTests
{
    [Fact]
    public void DpapiFallbackEncryptsAndRoundTripsWithProtectedAcl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ".credentials");
        var secret = $"credential-{Guid.NewGuid():N}";

        var store = new CredentialStore(vaultAvailable: false, fallbackPath: path);
        store.Store(CredentialStore.ApiTokenResource, secret);

        var raw = File.ReadAllBytes(path);
        Assert.StartsWith("LTDPAPI1\n", Encoding.ASCII.GetString(raw));
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(raw));
        Assert.True(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);

        var reloaded = new CredentialStore(vaultAvailable: false, fallbackPath: path);
        Assert.Equal(secret, reloaded.Retrieve(CredentialStore.ApiTokenResource));
    }

    [Fact]
    public void LegacyPlaintextFallbackIsMigratedImmediately()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".credentials");
        var secret = $"legacy-{Guid.NewGuid():N}";
        File.WriteAllText(path, $"{CredentialStore.ApiTokenResource}={secret}\n");

        var store = new CredentialStore(vaultAvailable: false, fallbackPath: path);

        Assert.Equal(secret, store.Retrieve(CredentialStore.ApiTokenResource));
        var migrated = File.ReadAllBytes(path);
        Assert.StartsWith("LTDPAPI1\n", Encoding.ASCII.GetString(migrated));
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(migrated));
        Assert.True(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public void LocalApiCredentialIsNeverPersistedAndLegacyValueIsRemoved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LabTetherAgentTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, ".credentials");
        var store = new CredentialStore(vaultAvailable: false, fallbackPath: path);
        store.Store(CredentialStore.LocalApiAuthResource, "legacy-local-secret");

        var settings = new AgentSettings { LocalApiAuthToken = "runtime-only-secret" };
        store.LoadInto(settings);

        Assert.Empty(settings.LocalApiAuthToken);
        Assert.Null(store.Retrieve(CredentialStore.LocalApiAuthResource));

        settings.LocalApiAuthToken = "another-runtime-secret";
        store.SaveFrom(settings);
        Assert.Null(store.Retrieve(CredentialStore.LocalApiAuthResource));
    }
}
