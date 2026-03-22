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
    [STAThread]
    static void Main(string[] args)
    {
        // Single-instance check must happen before any WinUI initialization
        if (!App.EnsureSingleInstance())
            return;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
