using Microsoft.Win32;

namespace LabTetherAgent.App;

/// <summary>
/// The MSI writes a new marker on every fresh install (not on an upgrade).
/// The first start that sees a marker it has not handled applies the
/// install-time defaults once: Start at sign-in turns on when the kept setup
/// is valid. An uninstall removes the Run entry while the enrollment stays,
/// so a reinstall otherwise came back with Start at sign-in off.
/// </summary>
internal static class InstallDefaults
{
    internal const string InstallerKey = @"Software\LabTether\Agent Installer";
    internal const string FreshInstallValue = "FreshInstall";

    public static string? ReadMarker()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallerKey);
            return key?.GetValue(FreshInstallValue) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A marker counts once, so a later choice in Settings is kept.</summary>
    public static bool IsNew(string? marker, string? handled) =>
        !string.IsNullOrWhiteSpace(marker) && !string.Equals(marker, handled, StringComparison.Ordinal);
}
