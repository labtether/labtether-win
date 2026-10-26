using System.Diagnostics;

namespace LabTetherAgent.App;

/// <summary>
/// Last-resort exception logging. Agent lifecycle code reports its own
/// failures; these handlers make anything else visible in the agent log
/// (View Logs and diagnostics export) instead of losing it silently.
/// </summary>
internal static class GlobalExceptionHandlers
{
    private static int _installed;
    private static volatile bool _trayReady;

    /// <summary>
    /// Call once the tray icon is up. Before that, a UI failure means the
    /// app never started and must still end the process instead of leaving
    /// an invisible one behind.
    /// </summary>
    public static void MarkTrayReady() => _trayReady = true;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        // The runtime still terminates after this event; it only records why.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log(args.IsTerminating ? "Fatal unhandled exception" : "Unhandled exception",
                args.ExceptionObject as Exception);

        // Fire-and-forget tasks must not hide their failures.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>
    /// After startup, a failed UI callback must not end the tray app and
    /// orphan the running agent. Log it and keep the dispatcher alive.
    /// </summary>
    public static void HandleXamlException(Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Log(_trayReady ? "Unhandled UI exception" : "Fatal UI exception during startup", args.Exception, args.Message);
        if (_trayReady) args.Handled = true;
    }

    internal static void Log(string summary, Exception? exception, string? fallbackMessage = null)
    {
        var detail = exception?.ToString() ?? fallbackMessage ?? "no exception details";
        try
        {
            Trace.TraceError($"{summary}: {detail}");
            AppState.Current?.AgentProcess.LogReader.AppendRaw($"{summary}: {detail}");
        }
        catch (Exception)
        {
            // Logging is best effort inside a failure handler; never rethrow.
        }
    }
}
