namespace LabTetherAgent.Tests.App;

public class AppShutdownTests
{
    [Fact]
    public void ShutdownDetachesConnectionNotificationsBeforeStoppingAgent()
    {
        var source = File.ReadAllText(FindAppSource());
        var shutdownStart = source.IndexOf(
            "public async Task ShutdownAsync()",
            StringComparison.Ordinal);
        var nextMethod = source.IndexOf(
            "private void OnNotificationConnectionStateChanged",
            shutdownStart,
            StringComparison.Ordinal);

        Assert.True(shutdownStart >= 0, "ShutdownAsync source was not found.");
        Assert.True(nextMethod > shutdownStart, "ShutdownAsync source boundary was not found.");

        var shutdown = source[shutdownStart..nextMethod];
        var unsubscribe = shutdown.IndexOf(
            "OnConnectionStateChanged -= _notificationConnectionHandler",
            StringComparison.Ordinal);
        var stop = shutdown.IndexOf(
            "await _appState.StopAgentAsync()",
            StringComparison.Ordinal);

        Assert.True(unsubscribe >= 0, "Connection notification unsubscribe was not found.");
        Assert.True(stop > unsubscribe, "Agent stopped before connection notifications were detached.");
    }

    private static string FindAppSource()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current != null;
             current = current.Parent)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "LabTetherAgent",
                "App",
                "App.xaml.cs");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Unable to locate App.xaml.cs from the test output directory.");
    }
}
