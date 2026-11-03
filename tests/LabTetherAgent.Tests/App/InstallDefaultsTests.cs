using LabTetherAgent.App;

namespace LabTetherAgent.Tests.App;

// A reinstall kept the enrollment but came back with Start at sign-in off.
// Each fresh-install marker applies the default once; a later "off" in
// Settings must survive every following start.
public class InstallDefaultsTests
{
    [Fact]
    public void AMarkerCountsOnce()
    {
        Assert.True(InstallDefaults.IsNew("{A} 3/11/2026 21:04:18", ""));
        Assert.False(InstallDefaults.IsNew("{A} 3/11/2026 21:04:18", "{A} 3/11/2026 21:04:18"));
        Assert.True(InstallDefaults.IsNew("{A} 3/11/2026 22:10:02", "{A} 3/11/2026 21:04:18"));
        Assert.False(InstallDefaults.IsNew(null, ""));
    }
}
