using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using LabTetherAgent.Theme;

namespace LabTetherAgent.Views.Shared;

/// <summary>
/// Window frame helpers: the app draws its own title row (mascot and title) on
/// the canvas colour, Windows draws the caption buttons in the theme's text
/// colour, and window sizes are given in device-independent pixels.
/// </summary>
internal static class WindowChrome
{
    public static void Apply(Window window, FrameworkElement root, UIElement titleBar)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        ColorCaptionButtons(window, root.ActualTheme);
        root.ActualThemeChanged += (sender, _) => ColorCaptionButtons(window, sender.ActualTheme);
    }

    /// <summary>
    /// Sizes the window so its XAML content is width x height DIPs. With the
    /// content extended into the title bar, the content is taller than the
    /// Win32 client area; the first layout measures that difference and the
    /// window is corrected once.
    /// </summary>
    public static void SizeContent(Window window, FrameworkElement root, double width, double height)
    {
        ResizeClientDips(window, width, height);
        void Correct(object sender, RoutedEventArgs args)
        {
            root.Loaded -= Correct;
            var extraWidth = root.ActualWidth - width;
            var extraHeight = root.ActualHeight - height;
            if (Math.Abs(extraWidth) >= 1 || Math.Abs(extraHeight) >= 1)
                ResizeClientDips(window, width - extraWidth, height - extraHeight);
        }
        root.Loaded += Correct;
    }

    private static void ResizeClientDips(Window window, double width, double height)
    {
        var scale = Scale(window);
        window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    public static double Scale(Window window)
    {
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static void ColorCaptionButtons(Window window, ElementTheme theme)
    {
        var bar = window.AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        if (new AccessibilitySettings().HighContrast)
        {
            // Windows' own high contrast colours.
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            return;
        }

        var text = ThemeColors.Get("LTTextColor", theme);
        bar.ButtonForegroundColor = text;
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedForegroundColor = text;
        bar.ButtonHoverBackgroundColor = ThemeColors.Get("LTHoverColor", theme);
        bar.ButtonPressedBackgroundColor = ThemeColors.Get("LTSelColor", theme);
        bar.ButtonInactiveForegroundColor = ThemeColors.Get("LTText3Color", theme);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
