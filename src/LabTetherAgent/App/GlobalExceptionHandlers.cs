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
    /// A failed UI callback must not end the tray app and orphan the running
    /// agent. Log it and keep the dispatcher alive.
    /// </summary>
    public static void HandleXamlException(Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Log("Unhandled UI exception", args.Exception, args.Message);
        args.Handled = true;
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
