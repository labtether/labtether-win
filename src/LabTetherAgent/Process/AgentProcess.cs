using System.Diagnostics;
using System.ComponentModel;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Process;

/// <summary>
/// Manages the Go labtether-agent.exe binary lifecycle: start, stop, restart.
/// Mirrors mac-agent/Sources/LabTetherAgent/Process/AgentProcess.swift.
/// </summary>
public class AgentProcess : IDisposable
{
    private System.Diagnostics.Process? _process;
    private CancellationTokenSource? _logCts;
    private readonly CrashRestartCoordinator _crashCoordinator = new();
    private bool _userInitiatedStop;
    private bool _disposed;

    public AgentLogReader LogReader { get; } = new();

    public bool IsRunning
    {
        get
        {
            var process = _process;
            if (process == null)
                return false;

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
    public bool IsStarting { get; private set; }
    public bool NeedsRestart { get; set; }
    public bool LastExitWasUserInitiated { get; private set; }

    public event Action? OnStarted;
    public event Action<int>? OnExited; // exit code
    public event Action<string>? OnError;

    /// <summary>
    /// Start the Go agent process with the given environment variables.
    /// </summary>
    public void Start(string binaryPath, Dictionary<string, string> environment)
    {
        if (IsRunning)
        {
            LogReader.AppendRaw("Agent is already running.");
            return;
        }

        if (!File.Exists(binaryPath))
        {
            OnError?.Invoke($"Agent binary not found: {binaryPath}");
            return;
        }

        IsStarting = true;
        _userInitiatedStop = false;
        LastExitWasUserInitiated = false;
        NeedsRestart = false;

        System.Diagnostics.Process? process = null;
        CancellationTokenSource? logCts = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--console", // force interactive mode
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? ".",
            };

            // Set environment variables
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;

            process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;

            logCts = new CancellationTokenSource();
            if (!process.Start())
                throw new InvalidOperationException("agent process did not start");

            // Start reading stdout and stderr
            _process = process;
            _logCts = logCts;
            _ = LogReader.ReadAsync(process.StandardOutput, logCts.Token);
            _ = LogReader.ReadAsync(process.StandardError, logCts.Token);

            _crashCoordinator.RecordStart();
            IsStarting = false;

            LogReader.AppendRaw($"Agent started (PID {process.Id})");
            OnStarted?.Invoke();
        }
        catch (Exception ex)
        {
            IsStarting = false;
            CleanupFailedStart(process, logCts);
            OnError?.Invoke($"Failed to start agent: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop the agent process gracefully.
    /// On Windows, sends CTRL_BREAK_EVENT via P/Invoke with fallback to Kill().
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        var process = _process;
        if (process == null || process.HasExited)
            return;

        _userInitiatedStop = true;
        timeout ??= TimeSpan.FromSeconds(10);

        LogReader.AppendRaw("Stopping agent...");

        try
        {
            // Try graceful shutdown first
            if (!SendGracefulShutdown(process.Id))
            {
                // Fallback: kill directly
                process.Kill();
                await WaitForExitAsync(process, timeout.Value);
            }
            else
            {
                // Wait for graceful exit
                var exited = await WaitForExitAsync(process, timeout.Value);
                if (!exited)
                {
                    LogReader.AppendRaw("Graceful shutdown timed out, forcing kill.");
                    process.Kill();
                    await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (Win32Exception ex)
        {
            LogReader.AppendRaw($"Failed to stop agent process: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            LogReader.AppendRaw($"Failed to stop agent process: {ex.Message}");
        }

        _logCts?.Cancel();
    }

    /// <summary>
    /// Stop and restart the agent process.
    /// </summary>
    public async Task RestartAsync(string binaryPath, Dictionary<string, string> environment)
    {
        await StopAsync();
        await Task.Delay(500); // brief pause between stop and start
        Start(binaryPath, environment);
    }

    /// <summary>
    /// Kill any orphaned agent processes from previous app runs.
    /// </summary>
    public void KillOrphanedAgents(string binaryPath)
    {
        var binaryName = Path.GetFileNameWithoutExtension(binaryPath);
        var expectedPath = NormalizeExecutablePath(binaryPath);
        try
        {
            var orphans = System.Diagnostics.Process.GetProcessesByName(binaryName);
            foreach (var orphan in orphans)
            {
                try
                {
                    if (orphan.Id == _process?.Id) continue; // skip our own
                    if (orphan.Id == Environment.ProcessId) continue;
                    if (!ProcessMatchesExecutable(orphan, expectedPath)) continue;

                    orphan.Kill();
                    LogReader.AppendRaw($"Killed orphaned agent process (PID {orphan.Id})");
                }
                catch (InvalidOperationException ex)
                {
                    LogReader.AppendRaw($"Skipped orphaned agent process (PID {orphan.Id}): {ex.Message}");
                }
                catch (Win32Exception ex)
                {
                    LogReader.AppendRaw($"Failed to kill orphaned agent process (PID {orphan.Id}): {ex.Message}");
                }
                catch (NotSupportedException ex)
                {
                    LogReader.AppendRaw($"Failed to kill orphaned agent process (PID {orphan.Id}): {ex.Message}");
                }
                finally
                {
                    orphan.Dispose();
                }
            }
        }
        catch (Win32Exception ex)
        {
            LogReader.AppendRaw($"Failed to enumerate orphaned agent processes: {ex.Message}");
        }
        catch (PlatformNotSupportedException ex)
        {
            LogReader.AppendRaw($"Failed to enumerate orphaned agent processes: {ex.Message}");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitedProcess = sender as System.Diagnostics.Process;
        if (exitedProcess != null && !ReferenceEquals(_process, exitedProcess))
        {
            exitedProcess.Exited -= OnProcessExited;
            exitedProcess.Dispose();
            return;
        }

        var exitCode = TryGetExitCode(exitedProcess ?? _process);
        var userInitiated = _userInitiatedStop;
        LastExitWasUserInitiated = userInitiated;
        LogReader.AppendRaw($"Agent exited (code {exitCode})");
        IsStarting = false;
        _logCts?.Cancel();
        if (exitedProcess != null)
        {
            exitedProcess.Exited -= OnProcessExited;
            _process = null;
        }

        if (!userInitiated && exitCode != 0)
        {
            _crashCoordinator.CheckStability();
        }

        OnExited?.Invoke(exitCode);

        if (!userInitiated && exitCode != 0)
        {
            // Unexpected crash — schedule restart with backoff
            LogReader.AppendRaw("Crash detected; restart scheduling delegated to app state.");
            // The caller (AppState) is responsible for actually restarting
            // after the delay, since it has the binary path and environment.
        }
    }

    private static int TryGetExitCode(System.Diagnostics.Process? process)
    {
        if (process == null)
            return -1;

        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string NormalizeExecutablePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool ProcessMatchesExecutable(System.Diagnostics.Process process, string expectedPath)
    {
        try
        {
            var actual = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(actual))
                return false;

            return string.Equals(NormalizeExecutablePath(actual), expectedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private void CleanupFailedStart(System.Diagnostics.Process? process, CancellationTokenSource? logCts)
    {
        logCts?.Cancel();
        logCts?.Dispose();
        if (ReferenceEquals(_logCts, logCts))
            _logCts = null;

        process ??= _process;
        if (process == null)
            return;

        try
        {
            process.Exited -= OnProcessExited;
        }
        catch (InvalidOperationException)
        {
            // Process object was never associated with an OS process.
        }
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Process object was never associated with an OS process.
        }
        catch (Win32Exception ex)
        {
            LogReader.AppendRaw($"Failed to kill partially started agent process: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            LogReader.AppendRaw($"Failed to kill partially started agent process: {ex.Message}");
        }
        process.Dispose();
        if (ReferenceEquals(_process, process))
            _process = null;
    }

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

    /// <summary>
    /// Get the crash restart coordinator (for AppState to use).
    /// </summary>
    public CrashRestartCoordinator CrashCoordinator => _crashCoordinator;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logCts?.Cancel();
        _logCts?.Dispose();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Exited -= OnProcessExited;
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
            catch (Win32Exception ex)
            {
                LogReader.AppendRaw($"Failed to kill agent process during dispose: {ex.Message}");
            }
            catch (NotSupportedException ex)
            {
                LogReader.AppendRaw($"Failed to kill agent process during dispose: {ex.Message}");
            }
        }
        _process?.Dispose();
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
