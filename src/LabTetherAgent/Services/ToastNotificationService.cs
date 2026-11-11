using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using LabTetherAgent.Presentation;
using LabTetherAgent.State;

namespace LabTetherAgent.Services;

/// <summary>
/// Windows notifications for connection changes, a stopped agent, updates and
/// alerts. Uses the Windows toast API with the app's own notification ID.
/// The Windows App SDK's AppNotificationManager cannot register this
/// self-contained, unpackaged app ("The specified module could not be
/// found"), so it showed no notification at all.
/// </summary>
public class ToastNotificationService
{
    internal const string WindowsReleaseUrl = "https://github.com/labtether/labtether-win/releases/latest";
    internal const string DisplayName = "LabTether Agent";

    /// <summary>The notification ID; Windows shows notifications under its DisplayName and icon.</summary>
    internal const string AppId = "LabTether.Agent";
    internal const string AppIdKey = @"Software\Classes\AppUserModelId\" + AppId;
    private const string ViewUpdateArgument = "action=view-update";

    private readonly Dictionary<string, DateTime> _throttle = new();
    private readonly HashSet<string> _alertsShown = new();
    private string? _updateShown;
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);
    private readonly Action<string>? _log;
    private ToastNotifier? _notifier;

    /// <param name="log">Writes a failure to the app's log (View logs), which Release builds keep.</param>
    public ToastNotificationService(Action<string>? log = null)
    {
        _log = log;
    }

    public bool TryInitialize()
    {
        if (_notifier != null) return true;
        try
        {
            // Name and icon for the notification and its Notification Center group.
            using (var key = Registry.CurrentUser.CreateSubKey(AppIdKey))
            {
                key.SetValue("DisplayName", DisplayName);
                var icon = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "Square44x44Logo.png");
                if (File.Exists(icon))
                    key.SetValue("IconUri", icon);
            }
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            return true;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or SecurityException)
        {
            _log?.Invoke($"Windows notifications are unavailable: {ex.Message}");
            return false;
        }
    }

    public void NotifyDisconnected(AgentStatus? latest)
    {
        if (!ShouldNotify("disconnected")) return;
        TryShow(ToastText.Disconnected(latest, DateTimeOffset.Now));
    }

    public void NotifyReconnected()
    {
        if (!ShouldNotify("reconnected")) return;
        TryShow(ToastText.Reconnected(DateTimeOffset.Now));
    }

    public void NotifyAgentError(string message)
    {
        if (!ShouldNotify("agent-error")) return;
        TryShow(ToastText.AgentStopped(message, DateTimeOffset.Now));
    }

    public void NotifyUpdateAvailable(string version)
    {
        if (!ShouldNotifyUpdate(version)) return;
        TryShow(ToastText.UpdateAvailable(version), ("View release", ViewUpdateArgument));
    }

    // The app checks for an update every 12 hours while it runs; each new
    // version is announced once, not again at every check.
    internal bool ShouldNotifyUpdate(string version)
    {
        if (string.Equals(_updateShown, version, StringComparison.OrdinalIgnoreCase)) return false;
        _updateShown = version;
        return true;
    }

    /// <summary>Once per firing alert: the agent reports it again on every status poll.</summary>
    public void NotifyAlert(AlertSnapshot alert)
    {
        if (!ShouldNotifyAlert(alert)) return;
        TryShow(ToastText.Alert(alert, DateTimeOffset.Now));
    }

    // An alert is the same firing alert while its name and start time match;
    // it used to toast again every five minutes for as long as it fired.
    internal bool ShouldNotifyAlert(AlertSnapshot alert)
    {
        var key = $"{alert.Name}\n{alert.Since?.UtcTicks}";
        if (_alertsShown.Count > 200)
            _alertsShown.Clear();
        return _alertsShown.Add(key);
    }

    public void Cleanup()
    {
        _notifier = null;
    }

    /// <summary>The toast XML: a title, an optional second line and an optional button.</summary>
    internal static string BuildXml(ToastMessage message, (string Label, string Argument)? button)
    {
        static string Text(string value) => $"<text>{SecurityElement.Escape(value)}</text>";
        var launch = button is { } b ? $" launch=\"{SecurityElement.Escape(b.Argument)}\"" : "";
        var body = message.Body.Length > 0 ? Text(message.Body) : "";
        var actions = button is { } a
            ? $"<actions><action content=\"{SecurityElement.Escape(a.Label)}\" arguments=\"{SecurityElement.Escape(a.Argument)}\" /></actions>"
            : "";
        return $"<toast{launch}><visual><binding template=\"ToastGeneric\">{Text(message.Title)}{body}</binding></visual>{actions}</toast>";
    }

    private bool ShouldNotify(string key)
    {
        if (_throttle.TryGetValue(key, out var lastTime) &&
            DateTime.UtcNow - lastTime < ThrottleInterval)
            return false;

        _throttle[key] = DateTime.UtcNow;
        return true;
    }

    private void TryShow(ToastMessage message, (string Label, string Argument)? button = null)
    {
        if (_notifier == null)
            return;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(BuildXml(message, button));
            var toast = new ToastNotification(xml);
            toast.Activated += OnActivated;
            _notifier.Show(toast);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            _log?.Invoke($"Could not show a Windows notification: {ex.Message}");
        }
    }

    // Works while the app runs; the update toast's body and button carry the same argument.
    private void OnActivated(ToastNotification sender, object args)
    {
        if (args is not ToastActivatedEventArgs activated || activated.Arguments != ViewUpdateArgument)
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
            _log?.Invoke($"Could not open the release page: {ex.Message}");
        }
    }
}
