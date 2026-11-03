using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Services;
using LabTetherAgent.State;
using LabTetherAgent.Views.About;
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
    private Microsoft.UI.Xaml.Controls.MenuFlyoutItem? _setupMenuItem;
    private System.Drawing.Icon? _trayDrawingIcon;
    private (TrayGlyphState State, bool Light, int Size)? _renderedGlyph;
    private Window? _lifetimeWindow;
    private FlyoutWindow? _flyoutWindow;
    private DateTimeOffset _flyoutDismissedAt;
    private OnboardingWindow? _onboardingWindow;
    private SynchronizationContext? _uiContext;
    private Windows.UI.ViewManagement.UISettings? _uiSettings;
    private Action<bool>? _connectionStateHandler;
    private Action<AgentStatus>? _statusHandler;
    private Action<string>? _agentErrorHandler;
    private Action? _agentStartedHandler;
    private AgentStatus? _lastStatus;
    private DateTimeOffset? _offlineSince;
    private DateTimeOffset? _stoppedAt;
    private bool _aboutDialogVisible;
    private bool _disposed;

    // A click on the tray icon first dismisses an open flyout; treat a show
    // request that close behind it as the second half of a toggle.
    private static readonly TimeSpan FlyoutToggleWindow = TimeSpan.FromMilliseconds(400);

    public TrayIconManager(AppState appState)
    {
        _appState = appState;
    }

    public void Initialize()
    {
        _uiContext = SynchronizationContext.Current;
        // WinUI exits when its last Window closes, even if a native tray icon
        // is still registered. Keep one never-shown window alive so completing
        // onboarding or closing a flyout cannot silently stop the wrapper and
        // its child agent.
        _lifetimeWindow = new Window();
        _lifetimeWindow.AppWindow.Hide();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = TrayStatus.AppName,
        };

        // Set initial icon
        UpdateIcon();

        // Left-click or double-click → show flyout
        // H.NotifyIcon.WinUI 2.x uses command properties instead of routed events
        _taskbarIcon.DoubleClickCommand = new RelayCommand(ShowFlyout);
        _taskbarIcon.LeftClickCommand = new RelayCommand(ShowFlyout);
        _taskbarIcon.NoLeftClickDelay = true;

        // Context menu
        _taskbarIcon.ContextFlyout = BuildContextMenu();
        // This manager creates the icon entirely in code, so it is never
        // attached to a XAML visual tree that could trigger creation for us.
        // Force registration with Shell_NotifyIcon after all visual and
        // interaction properties are configured.
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

        _connectionStateHandler = connected => RunOnUiThread(() =>
        {
            _offlineSince = connected ? null : _offlineSince ?? DateTimeOffset.Now;
            if (connected) _stoppedAt = null;
            UpdateIcon();
        });
        _appState.ApiClient.OnConnectionStateChanged += _connectionStateHandler;

        // Alerts change the glyph while the flyout is closed (polled every 30 s).
        _statusHandler = status => RunOnUiThread(() =>
        {
            _lastStatus = status;
            UpdateIcon();
        });
        _appState.ApiClient.OnStatusUpdated += _statusHandler;

        // A failed start or crash restart leaves no child to report status.
        // Keep that failure visible until the next start or connection.
        _agentErrorHandler = _ => RunOnUiThread(() =>
        {
            _stoppedAt ??= DateTimeOffset.Now;
            UpdateIcon();
        });
        _agentStartedHandler = () => RunOnUiThread(() =>
        {
            _stoppedAt = null;
            UpdateIcon();
        });
        _appState.AgentProcess.OnError += _agentErrorHandler;
        _appState.AgentProcess.OnStarted += _agentStartedHandler;

        // Redraw for a light or dark taskbar when the Windows theme changes.
        _uiSettings = new Windows.UI.ViewManagement.UISettings();
        _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
    }

    public void ShowFlyout() => ShowFlyout(toggle: true);

    private void ShowFlyout(bool toggle)
    {
        if (_flyoutWindow == null)
        {
            if (toggle && DateTimeOffset.Now - _flyoutDismissedAt < FlyoutToggleWindow)
                return;
            _flyoutWindow = new FlyoutWindow(_appState);
            _flyoutWindow.Closed += (_, _) =>
            {
                _flyoutDismissedAt = DateTimeOffset.Now;
                _flyoutWindow = null;
            };
        }

        _flyoutWindow.ShowNearTray();
    }

    public void ShowOnboarding()
    {
        if (_onboardingWindow != null)
        {
            _onboardingWindow.Activate();
            return;
        }

        _onboardingWindow = new OnboardingWindow(_appState);
        _onboardingWindow.Closed += OnOnboardingClosed;
        _onboardingWindow.Activate();
    }

    private void OnOnboardingClosed(object sender, WindowEventArgs args)
    {
        if (_onboardingWindow == null)
            return;
        _onboardingWindow.Closed -= OnOnboardingClosed;
        _onboardingWindow = null;
    }

    private void OnSystemColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        RunOnUiThread(UpdateIcon);

    private void UpdateIcon()
    {
        if (_taskbarIcon == null || _disposed) return;

        var tray = TrayStatus.Resolve(
            _appState.ApiClient.IsConnected,
            _lastStatus ?? _appState.ApiClient.LatestStatus,
            _offlineSince,
            _stoppedAt,
            DateTimeOffset.Now);
        _taskbarIcon.ToolTipText = tray.Tooltip;
        if (_setupMenuItem != null)
            _setupMenuItem.Text = SetupMenuText();

        // GeneratedIconSource depends on a live WinUI render root. This tray
        // icon is created entirely in code and therefore rendered as an empty,
        // clickable slot on Windows 11. Produce a native HICON instead so the
        // shell always receives visible pixels in unpackaged builds.
        (TrayGlyphState State, bool Light, int Size) glyph =
            (tray.State, TaskbarEnvironment.IsLightTaskbar(), TaskbarEnvironment.SmallIconSize());
        if (_renderedGlyph == glyph)
            return;
        var nextIcon = CreateTrayIcon(glyph.State, TaskbarEnvironment.Palette(glyph.Light), glyph.Size);
        _taskbarIcon.IconSource = null;
        _taskbarIcon.Icon = nextIcon;
        _trayDrawingIcon?.Dispose();
        _trayDrawingIcon = nextIcon;
        _renderedGlyph = glyph;
    }

    internal static System.Drawing.Icon CreateTrayIcon(TrayGlyphState state, TrayPalette palette, int size)
    {
        using var bitmap = TrayGlyph.Render(state, palette, size);
        return TrayGlyph.ToIcon(bitmap);
    }

    private Microsoft.UI.Xaml.Controls.MenuFlyout BuildContextMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();

        var openConsole = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Open console",
            Command = new RelayCommand(OpenConsole),
        };
        menu.Items.Add(openConsole);

        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());

        _setupMenuItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = SetupMenuText(),
            Command = new RelayCommand(ShowOnboarding),
        };
        menu.Items.Add(_setupMenuItem);

        var settings = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Settings",
            Command = new RelayCommand(() =>
            {
                var win = new Settings.SettingsWindow(_appState);
                win.Activate();
            }),
        };
        menu.Items.Add(settings);

        var logs = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "View logs",
            Command = new RelayCommand(() =>
            {
                var win = new LogViewer.LogViewerWindow(_appState);
                win.Activate();
            }),
        };
        menu.Items.Add(logs);

        var popOut = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Pop out",
            Command = new RelayCommand(() =>
            {
                var win = new PopOut.PopOutWindow(_appState);
                win.Activate();
            }),
        };
        menu.Items.Add(popOut);

        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());

        var about = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "About",
            Command = new AsyncRelayCommand(ShowAboutWithErrorHandlingAsync),
        };
        menu.Items.Add(about);

        var quit = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Quit",
            Command = new AsyncRelayCommand(async () =>
            {
                if (Application.Current is LabTetherAgent.App.App app)
                    await app.ShutdownAsync();
                Application.Current.Exit();
            }),
        };
        menu.Items.Add(quit);

        return menu;
    }

    private string SetupMenuText() => _appState.Settings.IsEnrolled ? "Re-enroll…" : "Set up…";

    private void OpenConsole()
    {
        _ = new ConsoleLauncher().OpenAsync(_appState.Settings);
    }

    private async Task ShowAboutWithErrorHandlingAsync()
    {
        try
        {
            await ShowAboutAsync();
        }
        catch (COMException ex)
        {
            LogAboutFailure(ex);
        }
        catch (InvalidOperationException ex)
        {
            LogAboutFailure(ex);
        }
    }

    internal async Task ShowAboutAsync()
    {
        if (_aboutDialogVisible)
            return;

        // ContentDialog needs a live XamlRoot. The tray app has no persistent
        // main window, so use the existing flyout as the owner and bring it up
        // before resolving its visual root.
        ShowFlyout(toggle: false);
        var flyout = _flyoutWindow;
        if (flyout?.Content is not FrameworkElement owner)
            throw new InvalidOperationException("The tray flyout has no XAML content root.");

        var xamlRoot = await WaitForXamlRootAsync(owner);
        if (xamlRoot == null)
            throw new InvalidOperationException("The tray flyout did not acquire a XAML root.");

        _aboutDialogVisible = true;
        // The dialog and its save picker take focus; keep the flyout open and
        // large enough to hold the dialog until it closes.
        flyout.HoldOpenForDialog(true);
        try
        {
            var ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(flyout);
            var dialog = new AboutDialog(_appState, ownerWindowHandle)
            {
                XamlRoot = xamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            flyout.HoldOpenForDialog(false);
            _aboutDialogVisible = false;
        }
    }

    private static async Task<XamlRoot?> WaitForXamlRootAsync(FrameworkElement owner)
    {
        if (owner.XamlRoot is { } existingRoot)
            return existingRoot;

        var completion = new TaskCompletionSource<XamlRoot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            if (owner.XamlRoot is { } loadedRoot)
                completion.TrySetResult(loadedRoot);
        };
        owner.Loaded += loaded;
        try
        {
            // Close the narrow race between the initial check and subscription.
            if (owner.XamlRoot is { } subscribedRoot)
                return subscribedRoot;

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            owner.Loaded -= loaded;
        }
    }

    private static void LogAboutFailure(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to show About dialog: {ex.GetType().Name}: {ex.Message}");
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
        if (_statusHandler != null)
            _appState.ApiClient.OnStatusUpdated -= _statusHandler;
        if (_uiSettings != null)
            _uiSettings.ColorValuesChanged -= OnSystemColorsChanged;
        if (_agentErrorHandler != null)
            _appState.AgentProcess.OnError -= _agentErrorHandler;
        if (_agentStartedHandler != null)
            _appState.AgentProcess.OnStarted -= _agentStartedHandler;
        _taskbarIcon?.Dispose();
        _trayDrawingIcon?.Dispose();
        _flyoutWindow?.Close();
        _onboardingWindow?.Close();
        _lifetimeWindow?.Close();
        _lifetimeWindow = null;
    }
}
