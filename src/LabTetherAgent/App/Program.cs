using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LabTetherAgent.App;

/// <summary>
/// Custom entry point for single-instance enforcement.
/// WinUI 3 apps need a custom Main() to handle AppInstance redirection
/// before the XAML framework initializes.
/// </summary>
public static class Program
{
    internal const string WinUiRuntimeProbeArgument = "--winui-runtime-probe";

    internal static bool IsWinUiRuntimeProbe { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        // Scheduled tasks, shortcuts, and management tools may launch the
        // unpackaged EXE with System32 (or another unrelated directory) as
        // the process working directory. Windows App SDK resource discovery
        // must start from the published application directory; normalize this
        // before AppInstance or any WinUI/COM initialization occurs.
        NormalizeWorkingDirectory();

        IsWinUiRuntimeProbe = HasWinUiRuntimeProbeArgument(args);

        // Single-instance check must happen before any WinUI initialization
        // Use global:: to disambiguate from the LabTetherAgent.App namespace
        if (!IsWinUiRuntimeProbe && !global::LabTetherAgent.App.App.EnsureSingleInstance())
            return;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start((p) =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new global::LabTetherAgent.App.App();
        });
    }

    internal static bool HasWinUiRuntimeProbeArgument(IEnumerable<string> args) =>
        args.Any(arg => string.Equals(
            arg,
            WinUiRuntimeProbeArgument,
            StringComparison.OrdinalIgnoreCase));

    internal static string NormalizeWorkingDirectory(
        string? appBaseDirectory = null,
        Action<string>? setCurrentDirectory = null)
    {
        var applicationDirectory = Path.GetFullPath(
            appBaseDirectory ?? AppContext.BaseDirectory);
        (setCurrentDirectory ?? Directory.SetCurrentDirectory)(applicationDirectory);
        return applicationDirectory;
    }
}
