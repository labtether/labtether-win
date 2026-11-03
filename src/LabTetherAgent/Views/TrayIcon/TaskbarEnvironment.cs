using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using LabTetherAgent.Theme;

namespace LabTetherAgent.Views.TrayIcon;

/// <summary>The taskbar's light or dark theme and its small-icon size.</summary>
internal static class TaskbarEnvironment
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int SmallIconMetric = 49; // SM_CXSMICON

    /// <summary>True when Windows draws the taskbar light (SystemUsesLightTheme = 1).</summary>
    public static bool IsLightTaskbar()
    {
        try
        {
            return Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", 0) is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The tray icon size in pixels: 16 at 100 % scaling, 20, 24, 32, 40 above it.</summary>
    public static int SmallIconSize()
    {
        var dpi = GetDpiForSystem();
        if (dpi == 0) dpi = 96;
        var size = GetSystemMetricsForDpi(SmallIconMetric, dpi);
        return size > 0 ? size : (int)Math.Round(16 * dpi / 96.0);
    }

    /// <summary>Glyph colours for the taskbar theme, read from the generated tokens.</summary>
    public static TrayPalette Palette(bool lightTaskbar)
    {
        var theme = lightTaskbar ? ElementTheme.Light : ElementTheme.Dark;
        return new TrayPalette(
            ToDrawing(ThemeColors.Get("LTTextColor", theme)),
            ToDrawing(ThemeColors.Get("LTText2Color", theme)),
            ToDrawing(ThemeColors.Get("LTWarnColor", theme)),
            ToDrawing(ThemeColors.Get("LTBadColor", theme)));
    }

    private static System.Drawing.Color ToDrawing(Windows.UI.Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
