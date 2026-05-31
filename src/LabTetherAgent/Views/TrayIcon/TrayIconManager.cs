using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Settings;
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
    private SynchronizationContext? _uiContext;
    private Icon? _currentIcon;
    private Action<bool>? _connectionStateHandler;
    private bool _disposed;

    public TrayIconManager(AppState appState)
    {
        _appState = appState;
    }

    public void Initialize()
    {
        _uiContext = SynchronizationContext.Current;
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "LabTether Agent",
        };

        // Set initial icon
        UpdateIcon(false);

        // Left-click or double-click → show flyout
        // H.NotifyIcon.WinUI 2.x uses command properties instead of routed events
        _taskbarIcon.DoubleClickCommand = new RelayCommand(ShowFlyout);
        _taskbarIcon.LeftClickCommand = new RelayCommand(ShowFlyout);
        _taskbarIcon.NoLeftClickDelay = true;

        // Context menu
        _taskbarIcon.ContextFlyout = BuildContextMenu();

        // Subscribe to connection state changes
        _connectionStateHandler = connected => RunOnUiThread(() => UpdateIcon(connected));
        _appState.ApiClient.OnConnectionStateChanged += _connectionStateHandler;
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

        var iconName = connected ? "tray-connected.ico" : "tray-disconnected.ico";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", iconName);

        if (File.Exists(iconPath))
        {
            var previousIcon = _currentIcon;
            _currentIcon = new Icon(iconPath);
            _taskbarIcon.Icon = _currentIcon;
            previousIcon?.Dispose();
        }

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
            var hubUrl = SettingsValidator.DeriveApiBaseUrl(_appState.Settings.HubUrl);
            if (string.IsNullOrEmpty(hubUrl))
                return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hubUrl) { UseShellExecute = true });
            }
            catch (InvalidOperationException ex)
            {
                LogOpenUrlFailure(hubUrl, ex);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                LogOpenUrlFailure(hubUrl, ex);
            }
            catch (PlatformNotSupportedException ex)
            {
                LogOpenUrlFailure(hubUrl, ex);
            }
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
            if (Application.Current is LabTetherAgent.App.App app)
                await app.ShutdownAsync();
            Application.Current.Exit();
        };
        menu.Items.Add(quit);

        return menu;
    }

    private static void LogOpenUrlFailure(string url, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to open URL '{url}': {ex.GetType().Name}: {ex.Message}");
    }

    private void RunOnUiThread(Action update)
    {
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            update();
            return;
        }

        _uiContext.Post(_ => update(), null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connectionStateHandler != null)
            _appState.ApiClient.OnConnectionStateChanged -= _connectionStateHandler;
        _taskbarIcon?.Dispose();
        _flyoutWindow?.Close();
        _currentIcon?.Dispose();
    }
}
