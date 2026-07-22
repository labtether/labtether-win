using System.Security.AccessControl;
using System.Security.Principal;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class SecureFileTests
{
    [Fact]
    public void IsPrivateRegularFileAcceptsManagedAclAndRejectsExplicitEveryoneAccess()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "agent-token");

        try
        {
            SecureFile.WriteAllText(path, "issued-agent-token\n");
            Assert.True(SecureFile.IsPrivateRegularFile(path));

            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);

            Assert.True(info.GetAccessControl().AreAccessRulesProtected);
            Assert.False(SecureFile.IsPrivateRegularFile(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
