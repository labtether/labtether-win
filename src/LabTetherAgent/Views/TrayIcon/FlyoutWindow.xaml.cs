using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.Shared;

namespace LabTetherAgent.Views.TrayIcon;

/// <summary>
/// The tray flyout: a borderless panel above the tray that closes when it
/// loses focus, like the Windows quick settings flyout.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const double PanelWidth = 360;
    private const double EdgeGap = 12;
    private const double DialogWidth = 420;
    private const double DialogHeight = 520;
    private bool _holdOpen;
    private double _requestedHeight;
    private double _frameExtra;

    public FlyoutViewModel ViewModel { get; }

    /// <summary>Close when the flyout loses focus (the normal tray behaviour).</summary>
    internal bool DismissOnDeactivate { get; set; } = true;

    public FlyoutWindow(AppState appState)
    {
        this.InitializeComponent();
        ViewModel = new FlyoutViewModel(appState.ApiClient)
        {
            HubUrl = appState.Settings.HubUrl,
            HubSettings = appState.Settings,
        };

        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        Activated += OnActivated;
        Closed += (_, _) => ViewModel.OnFlyoutClosed();
        FlyoutPanel.SizeChanged += (_, _) => PlaceNearTray();
        Root.SizeChanged += OnRootSizeChanged;
    }

    /// <summary>Places the flyout above the tray and brings it to the front.</summary>
    public void ShowNearTray()
    {
        PlaceNearTray();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>Keeps the flyout open, and big enough, while a dialog it owns is shown.</summary>
    public void HoldOpenForDialog(bool hold)
    {
        _holdOpen = hold;
        PlaceNearTray();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (DismissOnDeactivate && !_holdOpen)
                Close();
            return;
        }

        ViewModel.OnFlyoutOpened();
    }

    private void PlaceNearTray()
    {
        var height = FlyoutPanel.ActualHeight > 0 ? FlyoutPanel.ActualHeight : 320;
        var width = PanelWidth;
        if (_holdOpen)
        {
            width = DialogWidth;
            height = Math.Max(DialogHeight, height);
        }

        // The content extends over Windows' top frame, so it is taller than the
        // Win32 client area by _frameExtra DIPs (measured on the first layout).
        _requestedHeight = height;
        var scale = WindowChrome.Scale(this);
        var contentWidth = (int)Math.Ceiling(width * scale);
        var contentHeight = (int)Math.Ceiling(height * scale);
        AppWindow.ResizeClient(new SizeInt32(contentWidth, Math.Max(1, (int)Math.Ceiling((height - _frameExtra) * scale))));

        // Line the visible panel up with the work area; the frame border
        // (outer size minus content size) sits outside it.
        var border = Math.Max(0, (AppWindow.Size.Width - contentWidth) / 2);
        var top = Math.Max(0, AppWindow.Size.Height - contentHeight - border);
        var gap = (int)Math.Round(EdgeGap * scale);
        GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - contentWidth - gap - border,
            area.Y + area.Height - contentHeight - gap - top));
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var extra = Root.ActualHeight - (_requestedHeight - _frameExtra);
        if (Math.Abs(extra - _frameExtra) < 1)
            return;
        _frameExtra = extra;
        PlaceNearTray();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var win = new LogViewer.LogViewerWindow(AppState.Shared);
        win.Activate();
    }

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        var win = new PopOut.PopOutWindow(AppState.Shared);
        win.Activate();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);
}
