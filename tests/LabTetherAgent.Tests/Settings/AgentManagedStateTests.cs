using System.Security.AccessControl;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class AgentManagedStateTests
{
    [Fact]
    public void CaptureAndCommitCarriesIdentityAndCanonicalEnrollmentState()
    {
        var root = NewTestDirectory();
        var staging = Path.Combine(root, "staging");
        var destination = Path.Combine(root, "destination");
        try
        {
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(destination);
            SecureFile.WriteAllText(
                Path.Combine(staging, AgentManagedState.AgentTokenFileName),
                "issued-agent-token\n");
            SecureFile.WriteAllText(
                Path.Combine(staging, AgentManagedState.DeviceKeyFileName),
                "device-private-key\n");
            SecureFile.WriteAllText(
                Path.Combine(staging, AgentManagedState.EnrollmentStateFileName),
                """{"version":1,"asset_id":"canonical-asset"}""");
            File.WriteAllText(
                Path.Combine(staging, AgentManagedState.DevicePublicKeyFileName),
                "device-public-key\n");
            File.WriteAllText(
                Path.Combine(staging, AgentManagedState.DeviceFingerprintFileName),
                "device-fingerprint\n");

            File.WriteAllText(
                Path.Combine(destination, AgentManagedState.CaCertificateFileName),
                "stale-ca");

            var captured = AgentManagedState.CaptureSetupArtifacts(staging);
            try
            {
                AgentManagedState.CommitArtifacts(destination, captured);
            }
            finally
            {
                AgentManagedState.ZeroArtifacts(captured);
            }

            Assert.Equal(
                "issued-agent-token",
                File.ReadAllText(
                    Path.Combine(destination, AgentManagedState.AgentTokenFileName)).Trim());
            Assert.Contains(
                "canonical-asset",
                File.ReadAllText(
                    Path.Combine(destination, AgentManagedState.EnrollmentStateFileName)));
            Assert.Equal(
                "device-private-key",
                File.ReadAllText(
                    Path.Combine(destination, AgentManagedState.DeviceKeyFileName)).Trim());
            Assert.False(
                File.Exists(Path.Combine(destination, AgentManagedState.CaCertificateFileName)));

            foreach (var file in Directory.EnumerateFiles(destination))
            {
                Assert.True(new FileInfo(file).GetAccessControl().AreAccessRulesProtected);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CaptureSetupArtifactsFailsClosedWithoutPrivateDeviceKey()
    {
        var root = NewTestDirectory();
        try
        {
            SecureFile.WriteAllText(
                Path.Combine(root, AgentManagedState.AgentTokenFileName),
                "issued-agent-token\n");
            File.WriteAllText(
                Path.Combine(root, AgentManagedState.DeviceKeyFileName),
                "inherited-device-key\n");

            Assert.Throws<InvalidOperationException>(
                () => AgentManagedState.CaptureSetupArtifacts(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyMigrationRequiresTokenBoundEnrollmentState()
    {
        var root = NewTestDirectory();
        var destination = Path.Combine(root, "destination");
        var legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(destination);
            Directory.CreateDirectory(legacy);
            SecureFile.WriteAllText(
                Path.Combine(destination, AgentManagedState.AgentTokenFileName),
                "issued-agent-token\n");
            File.WriteAllText(
                Path.Combine(legacy, AgentManagedState.DeviceKeyFileName),
                "legacy-device-key\n");

            Assert.False(
                AgentManagedState.MigrateLegacyDeviceIdentityIfNeeded(destination, legacy));
            Assert.False(
                File.Exists(Path.Combine(destination, AgentManagedState.DeviceKeyFileName)));

            SecureFile.WriteAllText(
                Path.Combine(destination, AgentManagedState.EnrollmentStateFileName),
                """{"version":1,"asset_id":"existing-native-agent"}""");

            Assert.True(
                AgentManagedState.MigrateLegacyDeviceIdentityIfNeeded(destination, legacy));
            Assert.Equal(
                "legacy-device-key",
                File.ReadAllText(
                    Path.Combine(destination, AgentManagedState.DeviceKeyFileName)).Trim());
            Assert.True(
                new FileInfo(
                    Path.Combine(destination, AgentManagedState.DeviceKeyFileName))
                    .GetAccessControl()
                    .AreAccessRulesProtected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTestDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
