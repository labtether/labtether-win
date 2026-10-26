using System.ComponentModel;
using System.Diagnostics;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Process;

public sealed record AgentProcessExit(long Generation, int ExitCode, bool UserInitiated);

/// <summary>Owns each child, its log readers, and asynchronous stop references.</summary>
public partial class AgentProcess : IDisposable
{
    // AppState shares this gate only for owner state changes. Process I/O and
    // all event subscribers run after releasing it.
    internal object LifecycleGate { get; } = new();
    private Child? _child;
    private long _generation;
    private readonly CrashRestartCoordinator _crashCoordinator = new();
    private readonly Func<ProcessStartInfo, System.Diagnostics.Process> _makeProcess;
    private readonly Action<Action> _dispatchExit;
    private readonly Func<System.Diagnostics.Process, TimeSpan, Task<bool>> _waitForExit;
    private bool _disposed;
    private readonly AgentChildOwnership? _ownership;

    public AgentProcess() : this(null, null, null) { }

    public AgentProcess(AppStateLocation location) : this(null, null, null) =>
        _ownership = new AgentChildOwnership(location);

    internal AgentProcess(AppStateLocation location, Func<ProcessStartInfo, System.Diagnostics.Process> makeProcess)
        : this(makeProcess, null, null) => _ownership = new AgentChildOwnership(location);

    internal AgentProcess(
        Func<ProcessStartInfo, System.Diagnostics.Process>? makeProcess,
        Action<Action>? dispatchExit = null,
        Func<System.Diagnostics.Process, TimeSpan, Task<bool>>? waitForExit = null)
    {
        _makeProcess = makeProcess ?? (info => new System.Diagnostics.Process { StartInfo = info });
        _dispatchExit = dispatchExit ?? (action => action());
        _waitForExit = waitForExit ?? WaitForExitAsync;
    }

    public AgentLogReader LogReader { get; } = new();
    public bool IsRunning { get { lock (LifecycleGate) return IsAlive(_child); } }
    public bool IsStarting { get; private set; }
    public bool NeedsRestart { get; set; }
    public bool LastExitWasUserInitiated { get; private set; }
    public long Generation { get { lock (LifecycleGate) return _generation; } }
    public CrashRestartCoordinator CrashCoordinator => _crashCoordinator;
    public event Action? OnStarted;
    public event Action<int>? OnExited;
    public event Action<long>? OnOwnedStarted;
    public event Action<AgentProcessExit>? OnOwnedExited;
    public event Action<string>? OnError;

    public void Start(string binaryPath, Dictionary<string, string> environment)
    {
        RefreshExitedChild();
        PrepareStart(binaryPath, environment)?.Invoke();
    }

    // HasExited may synchronously invoke Exited in .NET. Never query it under
    // LifecycleGate, including through a caller's reentrant monitor.
    internal void RefreshExitedChild()
    {
        Child? child;
        lock (LifecycleGate) { child = _child is { Started: true } ? RetainCurrentLocked() : null; }
        if (child == null) return;
        try { if (child.Process.HasExited) HandleExit(child); }
        finally { Release(child); }
    }

    internal Action? PrepareStart(string binaryPath, Dictionary<string, string> environment, long? expectedGeneration = null)
    {
        if (!File.Exists(binaryPath))
        {
            return () => ReportError($"Agent binary not found: {binaryPath}");
        }
        var child = new Child(_makeProcess(CreateStartInfo(binaryPath, environment)));
        Child? retired = null;
        bool rejected;
        lock (LifecycleGate)
        {
            rejected = _disposed || IsStarting || IsAlive(_child) ||
                (expectedGeneration.HasValue && expectedGeneration != _generation);
            if (!rejected)
            {
                retired = RetainCurrentLocked();
                if (retired != null) RetireLocked(retired);
                child.Generation = ++_generation;
                child.References++; // Current owner, separate from Start.
                _child = child;
                IsStarting = true;
                LastExitWasUserInitiated = false;
                NeedsRestart = false;
            }
        }
        if (rejected) { Release(child); return null; }
        return () => StartPreparedChild(child, retired);
    }

    private void StartPreparedChild(Child child, Child? retired)
    {
        if (retired != null) { retired.Logs.Cancel(); Release(retired); }
        bool cancelled;
        lock (LifecycleGate)
        {
            cancelled = child.UserStop || _disposed;
            if (cancelled && ReferenceEquals(_child, child)) { IsStarting = false; RetireLocked(child); }
        }
        if (cancelled)
        {
            child.StartCompleted.TrySetResult(false);
            Release(child);
            return;
        }
        child.ExitHandler = (_, _) => HandleExit(child);
        child.Process.Exited += child.ExitHandler;
        try
        {
            if (!child.Process.Start()) throw new InvalidOperationException("agent process did not start");
            _ = LogReader.ReadAsync(child.Process.StandardOutput, child.Logs.Token);
            _ = LogReader.ReadAsync(child.Process.StandardError, child.Logs.Token);
            child.Ownership = _ownership?.Remember(child.Process);
            var processId = child.Process.Id;
            bool publish;
            lock (LifecycleGate)
            {
                child.ProcessId = processId;
                child.Started = true;
                publish = ReferenceEquals(_child, child) && !child.UserStop && !_disposed;
                if (ReferenceEquals(_child, child)) IsStarting = false;
                if (publish) _crashCoordinator.RecordStart();
            }
            child.StartCompleted.TrySetResult(true);
            // Enable notifications last. Even an immediate exit has an owner.
            child.Process.EnableRaisingEvents = true;
            if (publish)
            {
                LogReader.AppendRaw($"Agent started (PID {child.Process.Id})");
                OnOwnedStarted?.Invoke(child.Generation);
                OnStarted?.Invoke();
            }
        }
        catch (Exception ex)
        {
            bool current;
            lock (LifecycleGate)
            {
                current = ReferenceEquals(_child, child);
                if (current) { IsStarting = false; RetireLocked(child); }
            }
            child.StartCompleted.TrySetResult(false);
            TryKill(child);
            ForgetExitedChild(child);
            if (current) ReportError($"Failed to start agent: {ex.Message}");
        }
        finally { Release(child); }
    }

    public void ReportError(string message)
    {
        LogReader.AppendRaw(message);
        OnError?.Invoke(message);
    }

    public Task StopAsync(TimeSpan? timeout = null) => PrepareStop(timeout)();

    internal Func<Task> PrepareStop(TimeSpan? timeout = null)
    {
        Child? child;
        lock (LifecycleGate)
        {
            child = RetainCurrentLocked();
            if (child != null) child.UserStop = true;
        }
        return child == null ? (() => Task.CompletedTask) : (() => StopChildAsync(child, timeout ?? TimeSpan.FromSeconds(10)));
    }

    private async Task StopChildAsync(Child child, TimeSpan timeout)
    {
        try
        {
            if (!await child.StartCompleted.Task) return;
            if (child.Process.HasExited) return;
            LogReader.AppendRaw("Stopping agent...");
            if (!SendGracefulShutdown(child.Process.Id)) TryKill(child);
            if (!await _waitForExit(child.Process, timeout))
            {
                LogReader.AppendRaw("Graceful shutdown timed out, forcing kill.");
                TryKill(child);
                await _waitForExit(child.Process, TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            LogReader.AppendRaw($"Agent stop completed with process error: {ex.Message}");
        }
        finally
        {
            // This reference keeps A's resources alive through the await. It
            // never looks up B's log source or process when A completes.
            child.Logs.Cancel();
            ForgetExitedChild(child);
            Release(child);
        }
    }

    public async Task RestartAsync(string binaryPath, Dictionary<string, string> environment)
    {
        var generation = Generation;
        await StopAsync();
        await Task.Delay(500);
        PrepareStart(binaryPath, environment, generation)?.Invoke();
    }

    private void HandleExit(Child child)
    {
        lock (LifecycleGate)
        {
            if (!ReferenceEquals(_child, child)) return;
            child.References++; // Retain the OS handle before querying it.
        }
        var exitCode = TryGetExitCode(child.Process);
        ForgetExitedChild(child);
        AgentProcessExit exited;
        bool current;
        lock (LifecycleGate)
        {
            current = ReferenceEquals(_child, child);
            exited = new AgentProcessExit(child.Generation, exitCode, child.UserStop);
            if (current)
            {
                LastExitWasUserInitiated = child.UserStop;
                IsStarting = false;
                RetireLocked(child);
                if (!exited.UserInitiated && exited.ExitCode != 0) _crashCoordinator.CheckStability();
            }
        }
        if (!current) { Release(child); return; }
        _dispatchExit(() =>
        {
            try
            {
                child.Logs.Cancel();
                LogReader.AppendRaw($"Agent exited (code {exited.ExitCode})");
                OnOwnedExited?.Invoke(exited);
                OnExited?.Invoke(exited.ExitCode);
            }
            finally { Release(child); }
        });
    }

    private void ForgetExitedChild(Child child)
    {
        try
        {
            if (child.Process.HasExited) _ownership?.Forget(child.Ownership);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            LogReader.AppendRaw("Could not clear the retired child ownership record; recovery will recheck its identity.");
        }
    }

    private Child? RetainCurrentLocked()
    {
        if (_child != null) _child.References++;
        return _child;
    }

    private void RetireLocked(Child child)
    {
        if (!ReferenceEquals(_child, child)) return;
        _child = null;
        child.References--;
    }

    private void Release(Child child)
    {
        bool dispose;
        lock (LifecycleGate) { dispose = --child.References == 0; }
        if (!dispose) return;
        child.Process.Exited -= child.ExitHandler;
        child.Logs.Cancel();
        child.Logs.Dispose();
        child.Process.Dispose();
    }

    private static bool IsAlive(Child? child) => child is { Started: true };

    private static int TryGetExitCode(System.Diagnostics.Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private void TryKill(Child child)
    {
        try { if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        {
            LogReader.AppendRaw($"Failed to stop owned agent process: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Child? child;
        lock (LifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            child = RetainCurrentLocked();
            if (child != null) { child.UserStop = true; RetireLocked(child); }
        }
        if (child != null) _ = StopChildAsync(child, TimeSpan.FromSeconds(5));
    }

    private sealed class Child(System.Diagnostics.Process process)
    {
        internal readonly System.Diagnostics.Process Process = process;
        internal readonly CancellationTokenSource Logs = new();
        internal readonly TaskCompletionSource<bool> StartCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal EventHandler? ExitHandler;
        internal AgentChildOwnership.Record? Ownership;
        internal long Generation;
        internal int References = 1;
        internal int ProcessId;
        internal bool Started;
        internal bool UserStop;
    }
}
