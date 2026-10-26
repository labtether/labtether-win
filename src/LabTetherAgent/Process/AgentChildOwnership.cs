using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Process;

/// <summary>Recovery authority for one state's exact child, never a binary-wide sweep.</summary>
internal sealed class AgentChildOwnership(AppStateLocation location)
{
    internal sealed record Record(string Owner, int Pid, long StartedUtcTicks,
        string Executable, int ParentPid, long ParentStartedUtcTicks);

    private readonly object _gate = new();
    private string RecordPath => location.FilePath("child-process.json");

    internal Record Remember(System.Diagnostics.Process child)
    {
        using var parent = System.Diagnostics.Process.GetCurrentProcess();
        var record = new Record(location.Identity, child.Id, child.StartTime.ToUniversalTime().Ticks,
            ExecutablePath(child),
            parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        location.EnsureDirectory();
        lock (_gate) SecureFile.WriteAllText(RecordPath, JsonSerializer.Serialize(record));
        return record;
    }

    internal void Forget(Record? expected)
    {
        lock (_gate)
        {
            if (expected != null && Read() == expected)
                SecureFile.DeleteIfExists(RecordPath);
        }
    }

    internal void Recover(string binaryPath)
    {
        Record? record;
        lock (_gate) record = Read();
        if (record == null) return;
        if (record.ParentPid != Environment.ProcessId && IsSameLiveProcess(record.ParentPid, record.ParentStartedUtcTicks))
            throw new InvalidOperationException("This state still has a live application owner.");
        System.Diagnostics.Process child;
        try { child = System.Diagnostics.Process.GetProcessById(record.Pid); }
        catch (ArgumentException) { Forget(record); return; }
        using (child)
        {
            // Retain this exact OS handle through validation, stop and wait;
            // PID reuse can never turn a stale record into a kill authority.
            _ = child.SafeHandle;
            if (child.HasExited || child.StartTime.ToUniversalTime().Ticks != record.StartedUtcTicks)
            {
                Forget(record);
                return;
            }
            // Dead/reused lifetimes above cannot own a child at any executable path.
            // Only a live matching lifetime needs the current binary fence.
            if (!string.Equals(record.Executable, Path.GetFullPath(binaryPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recorded child belongs to a different executable.");
            if (!string.Equals(ExecutablePath(child), record.Executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recorded child identity differs.");
            child.Kill(entireProcessTree: true);
            if (!child.WaitForExit(5000))
                throw new IOException("The state-owned child has not stopped.");
        }
        Forget(record);
    }

    private static string ExecutablePath(System.Diagnostics.Process process)
    {
        // MainModule can be null before the child's loader initializes. Query
        // the kernel's image identity using the same retained process lifetime.
        var path = new StringBuilder(32768);
        var length = (uint)path.Capacity;
        if (!QueryFullProcessImageName(process.SafeHandle, 0, path, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Child executable identity is unavailable.");
        if (length == 0 || length >= path.Capacity || !Path.IsPathFullyQualified(path.ToString()))
            throw new IOException("Child executable identity is invalid.");
        return Path.GetFullPath(path.ToString());
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode,
        SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
        StringBuilder executable, ref uint size);

    private Record? Read()
    {
        var path = RecordPath;
        if (!File.Exists(path)) return null;
        if (!SecureFile.IsPrivateRegularFile(path) || new FileInfo(path).Length > 4096)
            throw new IOException("Invalid child ownership record.");
        Record? record;
        try { record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path)); }
        catch (JsonException) { throw new IOException("Invalid child ownership record."); }
        if (record == null || record.Owner != location.Identity || record.Pid <= 0 ||
            record.Pid == Environment.ProcessId || record.ParentPid <= 0 || record.StartedUtcTicks <= 0 ||
            record.ParentStartedUtcTicks <= 0 || string.IsNullOrWhiteSpace(record.Executable) ||
            !Path.IsPathFullyQualified(record.Executable))
            throw new IOException("Child ownership record differs from this state.");
        return record;
    }

    private static bool IsSameLiveProcess(int pid, long started)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            _ = process.SafeHandle;
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
        }
        catch (ArgumentException) { return false; }
        // Access denied is deliberately not an absent-owner claim.
    }
}
