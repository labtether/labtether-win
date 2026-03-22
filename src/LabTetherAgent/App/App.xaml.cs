using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using LabTetherAgent.Views.TrayIcon;

namespace LabTetherAgent.App;

/// <summary>
/// Application entry point. Handles single-instance enforcement,
/// tray icon setup, and agent lifecycle coordination.
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private AppState? _appState;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize global state
        _appState = AppState.Initialize();

        // Set up the system tray icon (no main window)
        _trayIconManager = new TrayIconManager(_appState);
        _trayIconManager.Initialize();

        // If enrolled, start the agent automatically
        if (!_appState.ShouldShowOnboarding)
        {
            _appState.StartAgent();
        }
        else
        {
            // Show onboarding wizard
            _trayIconManager.ShowOnboarding();
        }
    }

    /// <summary>
    /// Single-instance enforcement. Call this from Program.cs Main() before
    /// starting the Application.
    /// </summary>
    public static bool EnsureSingleInstance()
    {
        var instance = AppInstance.FindOrRegisterForKey("LabTetherAgent");

        if (!instance.IsCurrent)
        {
            // Another instance is running — redirect activation to it and exit
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
            return false;
        }

        // Register for future activation redirects
        instance.Activated += (_, activatedArgs) =>
        {
            // Bring existing tray icon to focus when another instance tries to launch
            // This will be handled by TrayIconManager showing the flyout
        };

        return true;
    }

    /// <summary>
    /// Clean shutdown — stop agent, remove tray icon.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_appState != null)
            await _appState.StopAgentAsync();

        _trayIconManager?.Dispose();
        _appState?.Dispose();
    }
}
