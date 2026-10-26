using System.Runtime.InteropServices;

namespace LabTetherAgent.App;

internal interface ILoginItemManager
{
    bool IsEnabled();
    bool SetEnabled(bool enabled);
}

/// <summary>
/// Manages Start at Login.
///
/// Released builds are unpackaged (the ZIP and the per-user MSI), so they
/// register their own executable under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run. Only a process that
/// really has MSIX package identity uses the manifest's StartupTask.
/// </summary>
public class LoginItemManager : ILoginItemManager
{
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunValueName = "LabTetherAgent";
    internal const string StartupTaskId = "LabTetherAgentStartup";
    private const int AppModelErrorNoPackage = 15700;
    private readonly bool _supportsOsIntegration;

    public LoginItemManager(Settings.AppStateLocation? location = null) =>
        _supportsOsIntegration = (location ?? Settings.AppStateLocation.Installed).SupportsOsIntegration;

    /// <summary>
    /// True only when Windows will start this installed copy at login.
    /// </summary>
    public bool IsEnabled()
    {
        if (!_supportsOsIntegration) return false;
#if WINDOWS
        try
        {
            return IsPackaged() ? IsStartupTaskEnabled() : IsRegistryRunEnabled();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            System.Diagnostics.Trace.TraceWarning($"Start at Login state unavailable: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Enable or disable Start at Login. Returns false when Windows did not
    /// accept the requested state.
    /// </summary>
    public bool SetEnabled(bool enabled)
    {
        if (!_supportsOsIntegration) return !enabled;
#if WINDOWS
        try
        {
            if (IsPackaged())
                return SetStartupTaskEnabled(enabled);
            return SetRegistryRunEnabled(enabled) && IsRegistryRunEnabled() == enabled;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            System.Diagnostics.Trace.TraceWarning($"Start at Login could not be changed: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

#if WINDOWS
    private static bool IsRecoverable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException;

    /// <summary>
    /// Package identity, not an environment variable, decides whether the
    /// MSIX StartupTask exists. Unpackaged processes get APPMODEL_ERROR_NO_PACKAGE.
    /// </summary>
    private static bool IsPackaged()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    /// <summary>
    /// The quoted path of the running executable. An entry left by an older
    /// copy elsewhere (for example a previously extracted ZIP) does not count
    /// as enabled, so turning the setting on repairs it.
    /// </summary>
    private static string? CurrentRunCommand()
    {
        var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        return string.IsNullOrWhiteSpace(exePath) ? null : $"\"{Path.GetFullPath(exePath)}\"";
    }

    private static bool IsRegistryRunEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryRunKey);
        var expected = CurrentRunCommand();
        return expected != null &&
            key?.GetValue(RunValueName) is string value &&
            string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetRegistryRunEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryRunKey, writable: true);
        if (key == null) return false;

        if (enabled)
        {
            var command = CurrentRunCommand();
            if (command == null) return false;
            key.SetValue(RunValueName, command, Microsoft.Win32.RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        return true;
    }

    // StartupTask calls complete on a worker thread so a settings window on
    // the UI thread cannot deadlock while waiting for them.
    private static Windows.ApplicationModel.StartupTask GetStartupTask() =>
        Task.Run(() => Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId).AsTask())
            .GetAwaiter().GetResult();

    private static bool IsStartupTaskEnabled() =>
        GetStartupTask().State is Windows.ApplicationModel.StartupTaskState.Enabled
            or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;

    private static bool SetStartupTaskEnabled(bool enabled)
    {
        var task = GetStartupTask();
        if (!enabled)
        {
            // Policy-enabled tasks cannot be turned off by the app.
            if (task.State == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy) return false;
            task.Disable();
            return !IsStartupTaskEnabled();
        }

        // DisabledByUser/DisabledByPolicy stay off until the user or policy
        // changes them in Windows Settings; report that honestly.
        var state = Task.Run(() => task.RequestEnableAsync().AsTask()).GetAwaiter().GetResult();
        return state is Windows.ApplicationModel.StartupTaskState.Enabled
            or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    }
#endif
}
