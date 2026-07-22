using LabTetherAgent.App;

namespace LabTetherAgent.Tests.App;

public class AppStateVersionTests
{
    [Fact]
    public void NativeUpdateChecksUseWrapperAssemblyVersion()
    {
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+$", AppState.CurrentAppVersion);
        Assert.Equal(
            typeof(AppState).Assembly.GetName().Version!.ToString(3),
            AppState.CurrentAppVersion);
    }
}
