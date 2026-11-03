using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using LabTetherAgent.Presentation;
using LabTetherAgent.State;

namespace LabTetherAgent.Services;

/// <summary>
/// Windows toast notification service for connection state changes and alerts.
/// Uses Windows App SDK AppNotificationManager.
/// </summary>
public class ToastNotificationService
{
    internal const string WindowsReleaseUrl = "https://github.com/labtether/labtether-win/releases/latest";
    internal const string DisplayName = "LabTether Agent";
    private readonly Dictionary<string, DateTime> _throttle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);
    private readonly Action<string>? _log;
    private bool _registered;

    /// <param name="log">Writes a failure to the app's log (View logs), which Release builds keep.</param>
    public ToastNotificationService(Action<string>? log = null)
    {
        _log = log;
    }

    public bool TryInitialize()
    {
        if (_registered) return true;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            // Name and icon for the notification and its Notification Center
            // group; without them Windows shows the EXE name "LabTetherAgent".
            var icon = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "Square44x44Logo.png");
            if (File.Exists(icon))
                AppNotificationManager.Default.Register(DisplayName, new Uri(icon));
            else
                AppNotificationManager.Default.Register();
            _registered = true;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            _log?.Invoke($"Windows notifications are unavailable: {ex.Message}");
            return false;
        }
    }

    public void NotifyDisconnected(AgentStatus? latest)
    {
        if (!ShouldNotify("disconnected")) return;
        TryShow(Build(ToastText.Disconnected(latest, DateTimeOffset.Now)).BuildNotification());
    }

    public void NotifyReconnected()
    {
        if (!ShouldNotify("reconnected")) return;
        TryShow(Build(ToastText.Reconnected(DateTimeOffset.Now)).BuildNotification());
    }

    public void NotifyAgentError(string message)
    {
        if (!ShouldNotify("agent-error")) return;
        TryShow(Build(ToastText.AgentStopped(message, DateTimeOffset.Now)).BuildNotification());
    }

    public void NotifyUpdateAvailable(string version)
    {
        if (!ShouldNotify("update")) return;

        var builder = Build(ToastText.UpdateAvailable(version))
            .AddArgument("action", "view-update")
            .AddButton(new AppNotificationButton("View release")
                .AddArgument("action", "view-update"));

        TryShow(builder.BuildNotification());
    }

    public void NotifyAlert(AlertSnapshot alert)
    {
        if (!ShouldNotify($"alert:{alert.Name}")) return;
        TryShow(Build(ToastText.Alert(alert, DateTimeOffset.Now)).BuildNotification());
    }

    public void Cleanup()
    {
        if (!_registered) return;
        AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
        AppNotificationManager.Default.Unregister();
        _registered = false;
    }

    private static AppNotificationBuilder Build(ToastMessage message)
    {
        var builder = new AppNotificationBuilder().AddText(message.Title);
        if (message.Body.Length > 0)
            builder.AddText(message.Body);
        return builder;
    }

    private bool ShouldNotify(string key)
    {
        if (_throttle.TryGetValue(key, out var lastTime) &&
            DateTime.UtcNow - lastTime < ThrottleInterval)
            return false;

        _throttle[key] = DateTime.UtcNow;
        return true;
    }

    private void TryShow(AppNotification notification)
    {
        if (!_registered)
            return;
        try
        {
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _log?.Invoke($"Could not show a Windows notification: {ex.Message}");
        }
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("action", out var action) || action != "view-update")
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WindowsReleaseUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open update release: {ex.Message}");
        }
    }
}
