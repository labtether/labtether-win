using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using LabTetherAgent.State;
using LabTetherAgent.Views.TrayIcon;

namespace LabTetherAgent.App;

/// <summary>
/// Application entry point. Handles single-instance enforcement,
/// tray icon setup, and agent lifecycle coordination.
/// </summary>
public partial class App : Application, IXamlMetadataProvider
{
    internal const string GeneratedXamlMetadataProviderTypeName =
        "LabTetherAgent.LabTetherAgent_XamlTypeInfo.XamlMetaDataProvider";

    private TrayIconManager? _trayIconManager;
    private AppState? _appState;
    private ToastNotificationService? _notifications;
    private Action<string>? _updateAvailableHandler;
    private Action<bool>? _notificationConnectionHandler;
    private Action<AgentStatus>? _notificationStatusHandler;
    private Action<string>? _notificationAgentErrorHandler;
    private ConnectionToastGate? _connectionToasts;
    private DispatcherQueueTimer? _updateCheckTimer;
    private IXamlMetadataProvider? _xamlMetadataProvider;
    private Action? _redirectedActivationHandler;
    private static event Action? RedirectedActivation;

    // The WinUI C# compiler generates its Application metadata-provider
    // implementation only for RootNamespace.App. This application lives in
    // the nested LabTetherAgent.App namespace, so delegate explicitly to the
    // generated provider. Without this bridge, framework control resources
    // fail during startup because WinUI cannot resolve their type metadata.
    private IXamlMetadataProvider XamlMetadataProvider =>
        _xamlMetadataProvider ??= CreateGeneratedXamlMetadataProvider();

    internal static IXamlMetadataProvider CreateGeneratedXamlMetadataProvider()
    {
        var providerType = typeof(App).Assembly.GetType(
            GeneratedXamlMetadataProviderTypeName,
            throwOnError: true)!;
        if (!typeof(IXamlMetadataProvider).IsAssignableFrom(providerType))
        {
            throw new InvalidOperationException(
                $"Generated XAML metadata provider '{providerType.FullName}' does not implement {nameof(IXamlMetadataProvider)}.");
        }

        return (IXamlMetadataProvider)(Activator.CreateInstance(providerType) ??
            throw new InvalidOperationException(
                $"Unable to create generated XAML metadata provider '{providerType.FullName}'."));
    }

    public App()
    {
        this.InitializeComponent();
        UnhandledException += (_, args) => GlobalExceptionHandlers.HandleXamlException(args);
    }

    public IXamlType GetXamlType(Type type) => XamlMetadataProvider.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => XamlMetadataProvider.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => XamlMetadataProvider.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Application.Resources is not available in the constructor. Install
        // the token control brushes before any window exists (and in the
        // probe, so a token or resource fault fails the release gate).
        LabTetherAgent.Theme.ControlBrushes.Install(Resources);

        if (Program.IsWinUiRuntimeProbe)
        {
            // Exercise both App.xaml and a project-owned compiled XAML
            // component. A missing application PRI fails here before the
            // release gate can accept a broken unpackaged payload.
            _ = new global::LabTetherAgent.Components.SectionHeader();
            Application.Current.Exit();
            return;
        }

        // Initialize global state
        _appState = AppState.Initialize(Program.StateLocation);
#if DEBUG
        if (Program.SnapshotDirectory is { } snapshotDirectory)
        {
            _ = global::LabTetherAgent.Views.Snapshot.SnapshotRunner.RunAsync(_appState, snapshotDirectory);
            return;
        }
#endif

        if (_appState.Settings.Location.SupportsOsIntegration)
        {
            RestartRegistration.Register();
            _appState.ApplyInstallDefaults(InstallDefaults.ReadMarker());
        }

        if (_appState.Settings.Location.SupportsOsIntegration)
            _notifications = new ToastNotificationService(_appState.AgentProcess.LogReader.AppendRaw);
        if (_notifications?.TryInitialize() == true)
        {
            _updateAvailableHandler = _notifications.NotifyUpdateAvailable;
            _appState.UpdateChecker.OnUpdateAvailable += _updateAvailableHandler;
            _notificationConnectionHandler = OnNotificationConnectionStateChanged;
            _notificationStatusHandler = OnNotificationStatusUpdated;
            _notificationAgentErrorHandler = _notifications.NotifyAgentError;
            _appState.ApiClient.OnConnectionStateChanged += _notificationConnectionHandler;
            _appState.ApiClient.OnStatusUpdated += _notificationStatusHandler;
            _appState.AgentProcess.OnError += _notificationAgentErrorHandler;
            _ = _appState.UpdateChecker.CheckIfDueAsync();
            // The tray app runs for weeks; look again while it runs. The
            // checker itself asks GitHub at most every 12 hours.
            var updateChecker = _appState.UpdateChecker;
            _updateCheckTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _updateCheckTimer.Interval = TimeSpan.FromHours(1);
            _updateCheckTimer.Tick += (_, _) => _ = updateChecker.CheckIfDueAsync();
            _updateCheckTimer.Start();
        }

        // Set up the system tray icon (no main window)
        _trayIconManager = new TrayIconManager(_appState);
        _trayIconManager.Initialize();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _redirectedActivationHandler = () =>
            dispatcher.TryEnqueue(() => _trayIconManager?.ShowFlyout());
        RedirectedActivation += _redirectedActivationHandler;
        GlobalExceptionHandlers.MarkTrayReady();

        // If enrolled, start the agent automatically
        if (!_appState.ShouldShowOnboarding)
        {
            // Starts the agent; first checks a CA file an earlier version saved unchecked.
            _ = _appState.StartAgentAfterCaFileCheckAsync();
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
    public static bool EnsureSingleInstance(AppStateLocation? location = null)
    {
        var instance = AppInstance.FindOrRegisterForKey((location ?? AppStateLocation.Installed).ActivationKey);

        if (!instance.IsCurrent)
        {
            // Another instance is running — redirect activation to it and exit
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
            return false;
        }

        // Register for future activation redirects. A start by the installer
        // (or by Windows after an upgrade) finds the app already running and
        // must not open the flyout.
        instance.Activated += (_, activatedArgs) =>
        {
            if (!IsInstallerStart(activatedArgs))
                RedirectedActivation?.Invoke();
        };

        return true;
    }

    /// <summary>The argument the MSI and the upgrade restart pass when they start the app.</summary>
    internal const string InstallerStartArgument = "--from-installer";

    private static bool IsInstallerStart(AppActivationArguments activatedArgs) =>
        activatedArgs.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch &&
        (launch.Arguments ?? string.Empty).Contains(InstallerStartArgument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Clean shutdown — stop agent, remove tray icon.
    /// </summary>
    public async Task ShutdownAsync()
    {
        // Detach notifications before StopAgentAsync invalidates the last
        // connected status. An intentional Quit must not emit a misleading
        // "Offline since …" toast on the way out.
        _updateCheckTimer?.Stop();
        if (_appState != null && _updateAvailableHandler != null)
            _appState.UpdateChecker.OnUpdateAvailable -= _updateAvailableHandler;
        if (_appState != null && _notificationConnectionHandler != null)
            _appState.ApiClient.OnConnectionStateChanged -= _notificationConnectionHandler;
        if (_appState != null && _notificationStatusHandler != null)
            _appState.ApiClient.OnStatusUpdated -= _notificationStatusHandler;
        if (_appState != null && _notificationAgentErrorHandler != null)
            _appState.AgentProcess.OnError -= _notificationAgentErrorHandler;
        _notifications?.Cleanup();

        if (_appState != null)
            await _appState.StopAgentAsync();

        if (_redirectedActivationHandler != null)
            RedirectedActivation -= _redirectedActivationHandler;

        _trayIconManager?.Dispose();
        _appState?.Dispose();
    }

    private void OnNotificationConnectionStateChanged(bool connected)
    {
        if (_notifications is not { } notifications)
            return;
        _connectionToasts ??= new ConnectionToastGate(
            () => notifications.NotifyDisconnected(_appState?.ApiClient.LatestStatus),
            notifications.NotifyReconnected);
        _connectionToasts.OnConnectionChanged(connected);
    }

    private void OnNotificationStatusUpdated(AgentStatus status)
    {
        if (_notifications == null)
            return;

        foreach (var alert in status.FiringAlerts)
            _notifications.NotifyAlert(alert);
    }
}
