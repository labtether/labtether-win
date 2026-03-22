using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LabTetherAgent.Services;

/// <summary>
/// Windows toast notification service for connection state changes and alerts.
/// Uses Windows App SDK AppNotificationManager.
/// </summary>
public class ToastNotificationService
{
    private readonly Dictionary<string, DateTime> _throttle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    public void Initialize()
    {
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();
    }

    public void NotifyDisconnected()
    {
        if (!ShouldNotify("disconnected")) return;

        var builder = new AppNotificationBuilder()
            .AddText("Hub Connection Lost")
            .AddText("LabTether Agent has lost connection to the hub. Retrying...");

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public void NotifyReconnected()
    {
        if (!ShouldNotify("reconnected")) return;

        var builder = new AppNotificationBuilder()
            .AddText("Hub Connection Restored")
            .AddText("LabTether Agent has reconnected to the hub.");

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public void NotifyUpdateAvailable(string version)
    {
        if (!ShouldNotify("update")) return;

        var builder = new AppNotificationBuilder()
            .AddText("Agent Update Available")
            .AddText($"LabTether Agent v{version} is available.");

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public void NotifyAlert(string name, string severity, string? message)
    {
        var key = $"alert:{name}";
        if (!ShouldNotify(key)) return;

        var title = severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
            ? $"Critical Alert: {name}"
            : $"Alert: {name}";

        var builder = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message ?? "An alert is firing on this device.");

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    public void Cleanup()
    {
        AppNotificationManager.Default.Unregister();
    }

    private bool ShouldNotify(string key)
    {
        if (_throttle.TryGetValue(key, out var lastTime) &&
            DateTime.UtcNow - lastTime < ThrottleInterval)
            return false;

        _throttle[key] = DateTime.UtcNow;
        return true;
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Handle notification click — bring app to foreground
        // The notification arguments can specify which view to open
    }
}
