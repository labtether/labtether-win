#if WINDOWS
using System.Runtime.InteropServices;

namespace LabTetherAgent.Process;

/// <summary>
/// Sends CTRL_BREAK to the agent so it stops by itself. The Go agent reads
/// CTRL_BREAK as os.Interrupt and runs its own shutdown. It runs in a hidden
/// console of its own and this app has none, so the event can only be sent
/// from inside that console: join it, send, leave.
///
/// While this app is in the agent's console the break reaches it as well.
/// What keeps the app alive for that whole window:
/// - The handler is added after AttachConsole. Added before it, the break
///   still reached the default handler and ended the test host with
///   0xC000013A. Handlers run newest first; this one answers the break, so
///   the default handler never runs.
/// - Nothing sends an event between AttachConsole and SetConsoleCtrlHandler:
///   this app is the only sender and it holds <see cref="Gate"/>.
/// - Windows runs a handler on a new thread. The app leaves the console only
///   after that thread has reported the break, or after two seconds. If the
///   break has not arrived by then the handler stays registered, so a late
///   one is still ignored. Without a console no further event can arrive.
/// - An exception on the way out still leaves the console (finally). If the
///   app process itself ends while joined, Windows detaches it; the agent
///   keeps its console and later ends itself when its parent is gone.
/// - One gate for the whole process: console calls act on the process, so a
///   second stop (another generation, or Quit during a restart) waits.
/// </summary>
internal static unsafe partial class AgentConsoleBreak
{
    private static readonly object Gate = new();
    private static readonly ManualResetEventSlim BreakReachedApp = new();

    /// <summary>
    /// False when the break could not be sent, for example because the agent
    /// has already exited; the caller then kills it. The caller holds the
    /// process handle, so the ID cannot belong to another process yet.
    /// </summary>
    internal static bool Send(int processId)
    {
        lock (Gate)
        {
            // Joining a console replaces unset standard handles; put them back.
            var handles = new[] { STD_INPUT_HANDLE, STD_OUTPUT_HANDLE, STD_ERROR_HANDLE }
                .Select(kind => (kind, handle: GetStdHandle(kind))).ToArray();
            if (!AttachConsole((uint)processId))
                return false;
            var reached = false;
            try
            {
                if (!SetConsoleCtrlHandler(&IgnoreBreak, true))
                    return false;
                BreakReachedApp.Reset();
                // Group 0: every process in the agent's console, which is the
                // agent, any console children it has and, for now, this app.
                if (!GenerateConsoleCtrlEvent(CTRL_BREAK_EVENT, 0))
                    return false;
                reached = BreakReachedApp.Wait(TimeSpan.FromSeconds(2));
                return true;
            }
            finally
            {
                FreeConsole();
                foreach (var (kind, handle) in handles)
                    SetStdHandle(kind, handle);
                // The break has been handled and no console is joined, so no
                // event can reach the handler now. Keep the list from growing.
                if (reached)
                    SetConsoleCtrlHandler(&IgnoreBreak, false);
            }
        }
    }

    // Answers the break for this app. Anything else falls through to the
    // other handlers, as before this app joined a console.
    [UnmanagedCallersOnly]
    private static int IgnoreBreak(uint controlType)
    {
        if (controlType is not (CTRL_C_EVENT or CTRL_BREAK_EVENT))
            return 0;
        BreakReachedApp.Set();
        return 1;
    }

    private const uint CTRL_C_EVENT = 0;
    private const uint CTRL_BREAK_EVENT = 1;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(
        delegate* unmanaged<uint, int> handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int nStdHandle, nint hHandle);
}
#endif
