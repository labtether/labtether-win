using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Settings;
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
    private System.Drawing.Icon? _trayDrawingIcon;
    private Window? _lifetimeWindow;
    private FlyoutWindow? _flyoutWindow;
    private OnboardingWindow? _onboardingWindow;
    private SynchronizationContext? _uiContext;
    private Action<bool>? _connectionStateHandler;
    private bool _aboutDialogVisible;
    private bool _disposed;

    internal readonly record struct TrayIconVisual(string Glyph, byte Red, byte Green, byte Blue);

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
        // This manager creates the icon entirely in code, so it is never
        // attached to a XAML visual tree that could trigger creation for us.
        // Force registration with Shell_NotifyIcon after all visual and
        // interaction properties are configured.
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

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

    private void UpdateIcon(bool connected)
    {
        if (_taskbarIcon == null) return;

        // GeneratedIconSource depends on a live WinUI render root. This tray
        // icon is created entirely in code and therefore rendered as an empty,
        // clickable slot on Windows 11. Produce a native HICON instead so the
        // shell always receives visible pixels in unpackaged builds.
        var visual = ResolveTrayIconVisual(connected);
        var nextIcon = CreateTrayIcon(visual);
        _taskbarIcon.IconSource = null;
        _taskbarIcon.Icon = nextIcon;
        _trayDrawingIcon?.Dispose();
        _trayDrawingIcon = nextIcon;

        _taskbarIcon.ToolTipText = connected
            ? "LabTether Agent — Connected"
            : "LabTether Agent — Disconnected";
    }

    internal static TrayIconVisual ResolveTrayIconVisual(bool connected) => connected
        ? new TrayIconVisual("L", 46, 160, 67)
        : new TrayIconVisual("L", 117, 117, 117);

    internal static System.Drawing.Icon CreateTrayIcon(TrayIconVisual visual)
    {
        const int iconSize = 32;
        using var bitmap = new System.Drawing.Bitmap(
            iconSize,
            iconSize,
            PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var background = new System.Drawing.SolidBrush(
                   System.Drawing.Color.FromArgb(255, visual.Red, visual.Green, visual.Blue)))
        {
            graphics.FillEllipse(background, 1, 1, iconSize - 2, iconSize - 2);
        }

        using var font = new System.Drawing.Font(
            "Segoe UI",
            21,
            System.Drawing.FontStyle.Bold,
            System.Drawing.GraphicsUnit.Pixel);
        using var foreground = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var format = new System.Drawing.StringFormat
        {
            Alignment = System.Drawing.StringAlignment.Center,
            LineAlignment = System.Drawing.StringAlignment.Center,
        };
        graphics.DrawString(
            visual.Glyph,
            font,
            foreground,
            new System.Drawing.RectangleF(0, -1, iconSize, iconSize),
            format);

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);

    private Microsoft.UI.Xaml.Controls.MenuFlyout BuildContextMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();

        var openConsole = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Open Console",
            Command = new RelayCommand(OpenConsole),
        };
        menu.Items.Add(openConsole);

        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());

        var setup = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Setup / Re-enroll",
            Command = new RelayCommand(ShowOnboarding),
        };
        menu.Items.Add(setup);

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
            Text = "View Logs",
            Command = new RelayCommand(() =>
            {
                var win = new LogViewer.LogViewerWindow(_appState);
                win.Activate();
            }),
        };
        menu.Items.Add(logs);

        var popOut = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem
        {
            Text = "Pop Out",
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

    private void OpenConsole()
    {
        var hubUrl = SettingsValidator.DeriveApiBaseUrl(_appState.Settings.HubUrl);
        if (string.IsNullOrEmpty(hubUrl))
            return;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(hubUrl) { UseShellExecute = true });
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
        ShowFlyout();
        if (_flyoutWindow?.Content is not FrameworkElement owner)
            throw new InvalidOperationException("The tray flyout has no XAML content root.");

        var xamlRoot = await WaitForXamlRootAsync(owner);
        if (xamlRoot == null)
            throw new InvalidOperationException("The tray flyout did not acquire a XAML root.");

        _aboutDialogVisible = true;
        try
        {
            var ownerWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_flyoutWindow);
            var dialog = new AboutDialog(_appState, ownerWindowHandle)
            {
                XamlRoot = xamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
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

    private static void LogOpenUrlFailure(string url, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Failed to open URL '{url}': {ex.GetType().Name}: {ex.Message}");
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
        _taskbarIcon?.Dispose();
        _trayDrawingIcon?.Dispose();
        _flyoutWindow?.Close();
        _onboardingWindow?.Close();
        _lifetimeWindow?.Close();
        _lifetimeWindow = null;
    }
}
