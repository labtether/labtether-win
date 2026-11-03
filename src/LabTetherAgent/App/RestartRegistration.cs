using System.Runtime.InteropServices;

namespace LabTetherAgent.App;

/// <summary>
/// Lets Windows start the app again after an installer closes it. An MSI
/// upgrade closes the running tray app through Restart Manager, and Restart
/// Manager restarts only applications registered here; without it the device
/// stayed offline after an upgrade until the user opened the app again.
/// </summary>
internal static class RestartRegistration
{
    // Restart after an update only: not after a crash or hang (the app has its
    // own agent restart) and not after a reboot (Start at sign-in covers that).
    private const int RestartNoCrash = 1;
    private const int RestartNoHang = 2;
    private const int RestartNoReboot = 8;

    public static void Register()
    {
        var result = RegisterApplicationRestart(null, RestartNoCrash | RestartNoHang | RestartNoReboot);
        if (result != 0)
            System.Diagnostics.Debug.WriteLine($"RegisterApplicationRestart failed: 0x{result:X8}");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);
}
