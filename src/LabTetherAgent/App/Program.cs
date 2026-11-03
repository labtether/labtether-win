using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using LabTetherAgent.Settings;

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

    /// <summary>Debug builds: write window snapshots to this folder and exit.</summary>
    internal static string? SnapshotDirectory { get; private set; }
    internal static AppStateLocation StateLocation { get; private set; } = AppStateLocation.Installed;

    [STAThread]
    static void Main(string[] args)
    {
        // Scheduled tasks, shortcuts, and management tools may launch the
        // unpackaged EXE with System32 (or another unrelated directory) as
        // the process working directory. Windows App SDK resource discovery
        // must start from the published application directory; normalize this
        // before AppInstance or any WinUI/COM initialization occurs.
        NormalizeWorkingDirectory();
        GlobalExceptionHandlers.Install();

        IsWinUiRuntimeProbe = HasWinUiRuntimeProbeArgument(args);
#if DEBUG
        var snapshot = Array.FindIndex(args, arg => string.Equals(arg, "--render-snapshots", StringComparison.OrdinalIgnoreCase));
        if (snapshot >= 0 && snapshot + 1 < args.Length)
        {
            SnapshotDirectory = Path.GetFullPath(args[snapshot + 1]);
            args = args.Where((_, i) => i != snapshot && i != snapshot + 1).ToArray();
        }
#endif
        StateLocation = AppStateLocation.FromArguments(args);

        // Single-instance check must happen before any WinUI initialization
        // Use global:: to disambiguate from the LabTetherAgent.App namespace
        if (!IsWinUiRuntimeProbe && SnapshotDirectory == null &&
            !global::LabTetherAgent.App.App.EnsureSingleInstance(StateLocation))
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
