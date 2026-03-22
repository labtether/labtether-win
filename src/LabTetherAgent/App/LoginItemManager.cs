namespace LabTetherAgent.App;

/// <summary>
/// Manages Start at Login registration.
///
/// When running as an MSIX package, uses the Windows StartupTask API.
/// When running as an unpackaged app (dev builds), uses the registry
/// at HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
///
/// The actual Windows API calls require WinRT interop and will be
/// implemented when building on Windows. This file provides the
/// interface and registry-based fallback logic.
/// </summary>
public class LoginItemManager
{
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LabTetherAgent";

    /// <summary>
    /// Check if Start at Login is enabled.
    /// </summary>
    public bool IsEnabled()
    {
#if WINDOWS
        // Try MSIX StartupTask first
        if (IsPackaged())
            return IsStartupTaskEnabled();

        // Fallback to registry
        return IsRegistryRunEnabled();
#else
        return false;
#endif
    }

    /// <summary>
    /// Enable or disable Start at Login.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
#if WINDOWS
        if (IsPackaged())
        {
            SetStartupTaskEnabled(enabled);
            return;
        }

        SetRegistryRunEnabled(enabled);
#endif
    }

    private static bool IsPackaged()
    {
        // Check if running as an MSIX package
        try
        {
            // Windows.ApplicationModel.Package.Current throws if not packaged
            return Environment.GetEnvironmentVariable("MSIX") != null;
        }
        catch
        {
            return false;
        }
    }

#if WINDOWS
    private static bool IsRegistryRunEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryRunKey);
        return key?.GetValue(AppName) != null;
    }

    private static void SetRegistryRunEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    // MSIX StartupTask methods — require Windows.ApplicationModel.StartupTask
    // Stubbed here; will be fully implemented with WinRT interop on Windows.
    private static bool IsStartupTaskEnabled() => false;
    private static void SetStartupTaskEnabled(bool enabled) { }
#endif
}
