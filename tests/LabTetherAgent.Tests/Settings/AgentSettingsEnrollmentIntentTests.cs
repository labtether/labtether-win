using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class AgentSettingsEnrollmentIntentTests
{
    [Fact]
    public void DurableCredentialClearsAndPersistsOneTimeGroupIntent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"));
        var tokenPath = Path.Combine(directory, "agent-token");
        SecureFile.WriteAllText(tokenPath, "issued-agent-token\n");
        var settings = new AgentSettings { GroupId = "qa" };
        var persistCalls = 0;
        string? persistedGroupId = null;

        var changed = settings.ClearPersistedGroupIntentAfterEnrollmentAt(
            tokenPath,
            () =>
            {
                persistCalls++;
                persistedGroupId = settings.GroupId;
            });

        Assert.True(changed);
        Assert.Equal(string.Empty, settings.GroupId);
        Assert.Equal(string.Empty, persistedGroupId);
        Assert.Equal(1, persistCalls);
    }

    [Fact]
    public void MissingDurableCredentialDoesNotDiscardEnrollmentIntent()
    {
        var tokenPath = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"),
            "agent-token");
        var settings = new AgentSettings { GroupId = "qa" };
        var persistCalls = 0;

        var changed = settings.ClearPersistedGroupIntentAfterEnrollmentAt(
            tokenPath,
            () => persistCalls++);

        Assert.False(changed);
        Assert.Equal("qa", settings.GroupId);
        Assert.Equal(0, persistCalls);
    }

    [Fact]
    public void FreshReEnrollmentDoesNotDiscardNewGroupIntent()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"));
        var tokenPath = Path.Combine(directory, "agent-token");
        SecureFile.WriteAllText(tokenPath, "prior-issued-agent-token\n");
        var settings = new AgentSettings
        {
            GroupId = "new-placement",
            EnrollmentToken = "fresh-re-enrollment-token",
        };
        var persistCalls = 0;

        var changed = settings.ClearPersistedGroupIntentAfterEnrollmentAt(
            tokenPath,
            () => persistCalls++);

        Assert.False(changed);
        Assert.Equal("new-placement", settings.GroupId);
        Assert.Equal(0, persistCalls);
    }

    [Fact]
    public void UnprotectedCredentialDoesNotAuthorizeGroupIntentMigration()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "LabTetherAgentTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var tokenPath = Path.Combine(directory, "agent-token");
        File.WriteAllText(tokenPath, "untrusted-token-file");
        var settings = new AgentSettings { GroupId = "qa" };

        var changed = settings.ClearPersistedGroupIntentAfterEnrollmentAt(
            tokenPath,
            () => throw new InvalidOperationException("must not persist"));

        Assert.False(changed);
        Assert.Equal("qa", settings.GroupId);
    }
}
