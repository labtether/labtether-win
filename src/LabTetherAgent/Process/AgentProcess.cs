using System.Diagnostics;
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

    public bool IsRunning => _process is { HasExited: false };
    public bool IsStarting { get; private set; }
    public bool NeedsRestart { get; set; }

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
        NeedsRestart = false;

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

            _process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;

            _logCts = new CancellationTokenSource();
            _process.Start();

            // Start reading stdout and stderr
            _ = LogReader.ReadAsync(_process.StandardOutput, _logCts.Token);
            _ = LogReader.ReadAsync(_process.StandardError, _logCts.Token);

            _crashCoordinator.RecordStart();
            IsStarting = false;

            LogReader.AppendRaw($"Agent started (PID {_process.Id})");
            OnStarted?.Invoke();
        }
        catch (Exception ex)
        {
            IsStarting = false;
            OnError?.Invoke($"Failed to start agent: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop the agent process gracefully.
    /// On Windows, sends CTRL_BREAK_EVENT via P/Invoke with fallback to Kill().
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (_process == null || _process.HasExited)
            return;

        _userInitiatedStop = true;
        timeout ??= TimeSpan.FromSeconds(10);

        LogReader.AppendRaw("Stopping agent...");

        try
        {
            // Try graceful shutdown first
            if (!SendGracefulShutdown(_process.Id))
            {
                // Fallback: kill directly
                _process.Kill();
            }
            else
            {
                // Wait for graceful exit
                var exited = await WaitForExitAsync(_process, timeout.Value);
                if (!exited)
                {
                    LogReader.AppendRaw("Graceful shutdown timed out, forcing kill.");
                    _process.Kill();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
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
        try
        {
            var orphans = System.Diagnostics.Process.GetProcessesByName(binaryName);
            foreach (var orphan in orphans)
            {
                if (orphan.Id == _process?.Id) continue; // skip our own
                try
                {
                    orphan.Kill();
                    LogReader.AppendRaw($"Killed orphaned agent process (PID {orphan.Id})");
                }
                catch { }
                finally { orphan.Dispose(); }
            }
        }
        catch { }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _process?.ExitCode ?? -1;
        LogReader.AppendRaw($"Agent exited (code {exitCode})");
        IsStarting = false;

        OnExited?.Invoke(exitCode);

        if (!_userInitiatedStop && exitCode != 0)
        {
            // Unexpected crash — schedule restart with backoff
            _crashCoordinator.CheckStability();
            var delay = _crashCoordinator.NextDelay();
            LogReader.AppendRaw($"Crash detected, restarting in {delay.TotalSeconds:F0}s (attempt {_crashCoordinator.AttemptCount})");
            // The caller (AppState) is responsible for actually restarting
            // after the delay, since it has the binary path and environment.
        }
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
            try { _process.Kill(); } catch { }
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
