using H.NotifyIcon;
using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Views.Onboarding;

namespace LabTetherAgent.Views.TrayIcon;

/// <summary>
/// Manages the system tray icon, context menu, and flyout window.
/// Uses H.NotifyIcon.WinUI for tray icon support (WinUI 3 has no native API).
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly AppState _appState;
    private TaskbarIcon? _taskbarIcon;
    private FlyoutWindow? _flyoutWindow;
    private bool _disposed;

    public TrayIconManager(AppState appState)
    {
        _appState = appState;
    }

    public void Initialize()
    {
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "LabTether Agent",
        };

        // Set initial icon
        UpdateIcon(false);

        // Left-click → show flyout
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => ShowFlyout();
        _taskbarIcon.TrayLeftMouseUp += (_, _) => ShowFlyout();

        // Context menu
        _taskbarIcon.ContextFlyout = BuildContextMenu();

        // Subscribe to connection state changes
        _appState.ApiClient.OnConnectionStateChanged += connected => UpdateIcon(connected);
    }

    public void ShowFlyout()
    {
        if (_flyoutWindow == null)
        {
            _flyoutWindow = new FlyoutWindow(_appState);
            _flyoutWindow.Closed += (_, _) => _flyoutWindow = null;
        }

        _flyoutWindow.Activate();
    }

    public void ShowOnboarding()
    {
        var onboarding = new OnboardingWindow(_appState);
        onboarding.OnCompleted += () =>
        {
            _appState.StartAgent();
        };
        onboarding.Activate();
    }

    private void UpdateIcon(bool connected)
    {
        if (_taskbarIcon == null) return;

        // TODO: Load actual .ico files from Resources/Icons/
        // _taskbarIcon.IconSource = connected
        //     ? new BitmapImage(new Uri("ms-appx:///Resources/Icons/tray-connected.ico"))
        //     : new BitmapImage(new Uri("ms-appx:///Resources/Icons/tray-disconnected.ico"));

        _taskbarIcon.ToolTipText = connected
            ? "LabTether Agent — Connected"
            : "LabTether Agent — Disconnected";
    }

    private Microsoft.UI.Xaml.Controls.MenuFlyout BuildContextMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();

        var openConsole = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Open Console" };
        openConsole.Click += (_, _) =>
        {
            // Open hub URL in browser
            var hubUrl = _appState.Settings.HubUrl
                .Replace("wss://", "https://")
                .Replace("ws://", "http://")
                .Replace("/ws/agent", "");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hubUrl) { UseShellExecute = true }); } catch { }
        };
        menu.Items.Add(openConsole);

        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());

        var settings = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Settings" };
        settings.Click += (_, _) =>
        {
            var win = new Settings.SettingsWindow(_appState);
            win.Activate();
        };
        menu.Items.Add(settings);

        var logs = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "View Logs" };
        logs.Click += (_, _) =>
        {
            var win = new LogViewer.LogViewerWindow(_appState);
            win.Activate();
        };
        menu.Items.Add(logs);

        var popOut = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Pop Out" };
        popOut.Click += (_, _) =>
        {
            var win = new PopOut.PopOutWindow(_appState);
            win.Activate();
        };
        menu.Items.Add(popOut);

        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());

        var about = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "About" };
        about.Click += (_, _) =>
        {
            // Show about dialog — will be implemented as ContentDialog
        };
        menu.Items.Add(about);

        var quit = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Quit" };
        quit.Click += async (_, _) =>
        {
            if (Application.Current is App app)
                await app.ShutdownAsync();
            Application.Current.Exit();
        };
        menu.Items.Add(quit);

        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _taskbarIcon?.Dispose();
        _flyoutWindow?.Close();
    }
}
