using System.Diagnostics;
using System.ComponentModel;

namespace LabTetherAgent.Process;

public partial class AgentProcess
{
    internal static ProcessStartInfo CreateStartInfo(
        string binaryPath,
        IReadOnlyDictionary<string, string> environment,
        ProcessStartInfo? inheritedStartInfo = null
    )
    {
        var startInfo = inheritedStartInfo ?? new ProcessStartInfo();
        startInfo.FileName = binaryPath;
        // A child launched by the tray app is already an interactive process.
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? ".";

        // ProcessStartInfo inherits the tray process environment by default.
        // Strip standalone agent settings before applying this app's settings:
        // inherited local-bind, no-auth, or TLS-skip flags must not control
        // the bundled child or expose its localhost API.
        foreach (var key in startInfo.Environment.Keys.ToArray())
        {
            if (key.StartsWith("LABTETHER_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("AGENT_", StringComparison.OrdinalIgnoreCase))
                startInfo.Environment.Remove(key);
        }

        foreach (var (key, value) in environment)
            startInfo.Environment[key] = value;

        // A future caller may pass both options. Explicit CA trust must still
        // win at the final launch boundary, as it does in the settings UI.
        if (startInfo.Environment.TryGetValue("LABTETHER_TLS_CA_FILE", out var caFile) &&
            !string.IsNullOrWhiteSpace(caFile))
            startInfo.Environment["LABTETHER_TLS_SKIP_VERIFY"] = "false";

        // Final launch-boundary enforcement: a bundled child is owned by this
        // native app and must neither outlive it nor independently replace its
        // signed/attested executable. Override even stale or hostile settings
        // passed by a future call site.
        startInfo.Environment["LABTETHER_PARENT_PID"] = Environment.ProcessId.ToString();
        startInfo.Environment["LABTETHER_AUTO_UPDATE"] = "false";

        return startInfo;
    }

    /// <summary>Recover only the exact child recorded by this state owner.</summary>
    public void KillOrphanedAgents(string binaryPath) => _ownership?.Recover(binaryPath);

    /// <summary>
    /// Send a graceful shutdown signal to the process.
    /// On Windows: GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT).
    /// On other platforms: sends SIGTERM equivalent.
    /// </summary>
    private static bool SendGracefulShutdown(int processId)
    {
#if WINDOWS
        // P/Invoke to kernel32.dll GenerateConsoleCtrlEvent
        return NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CTRL_BREAK_EVENT, (uint)processId);
#else
        // On non-Windows (dev builds), just return false to trigger Kill() fallback
        return false;
#endif
    }

    private static async Task<bool> WaitForExitAsync(System.Diagnostics.Process process, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

}

#if WINDOWS
/// <summary>
/// P/Invoke declarations for Windows process control.
/// </summary>
internal static partial class NativeMethods
{
    internal const uint CTRL_BREAK_EVENT = 1;

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
}
#endif
