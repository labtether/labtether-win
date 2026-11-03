using System.Runtime.InteropServices;

namespace LabTetherAgent.Views.TrayIcon;

/// <summary>
/// Draws the native tray menu light or dark to match the taskbar. Windows
/// themes Win32 popup menus only for processes that opt in through uxtheme's
/// SetPreferredAppMode (ordinal 135, Windows 10 1903 and later); the tray
/// menu is such a popup menu, so without this it stays light on a dark taskbar.
/// </summary>
internal static class TrayMenuTheme
{
    private const int ForceDark = 2;
    private const int ForceLight = 3;
    private const int FirstSupportedBuild = 18362;

    public static void Apply(bool lightTaskbar)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstSupportedBuild))
            return;
        try
        {
            SetPreferredAppMode(lightTaskbar ? ForceLight : ForceDark);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"Tray menu theme unavailable: {ex.Message}");
        }
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
