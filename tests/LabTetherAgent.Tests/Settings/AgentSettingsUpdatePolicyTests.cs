using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Settings;

public class AgentSettingsUpdatePolicyTests
{
    [Fact]
    public void NormalizeWrapperManagedUpdatePolicy_MigratesLegacyTrueOnce()
    {
        var settings = new AgentSettings { AutoUpdateEnabled = true };

        Assert.True(settings.NormalizeWrapperManagedUpdatePolicy());
        Assert.False(settings.AutoUpdateEnabled);
        Assert.False(settings.NormalizeWrapperManagedUpdatePolicy());
    }

    [Fact]
    public void ApplyCommittedSetup_DoesNotRestoreLegacyChildSelfUpdate()
    {
        var current = new AgentSettings();
        var candidate = new AgentSettings { AutoUpdateEnabled = true };

        current.ApplyCommittedSetup(candidate);

        Assert.False(current.AutoUpdateEnabled);
    }
}
