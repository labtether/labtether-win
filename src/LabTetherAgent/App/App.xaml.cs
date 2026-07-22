using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using LabTetherAgent.Services;
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
    private bool? _lastNotifiedConnectionState;
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
    }

    public IXamlType GetXamlType(Type type) => XamlMetadataProvider.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => XamlMetadataProvider.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => XamlMetadataProvider.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
        _appState = AppState.Initialize();

        _notifications = new ToastNotificationService();
        if (_notifications.TryInitialize())
        {
            _updateAvailableHandler = _notifications.NotifyUpdateAvailable;
            _appState.UpdateChecker.OnUpdateAvailable += _updateAvailableHandler;
            _notificationConnectionHandler = OnNotificationConnectionStateChanged;
            _notificationStatusHandler = OnNotificationStatusUpdated;
            _appState.ApiClient.OnConnectionStateChanged += _notificationConnectionHandler;
            _appState.ApiClient.OnStatusUpdated += _notificationStatusHandler;
            _ = _appState.UpdateChecker.CheckIfDueAsync();
        }

        // Set up the system tray icon (no main window)
        _trayIconManager = new TrayIconManager(_appState);
        _trayIconManager.Initialize();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _redirectedActivationHandler = () =>
            dispatcher.TryEnqueue(() => _trayIconManager?.ShowFlyout());
        RedirectedActivation += _redirectedActivationHandler;

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
            RedirectedActivation?.Invoke();
        };

        return true;
    }

    /// <summary>
    /// Clean shutdown — stop agent, remove tray icon.
    /// </summary>
    public async Task ShutdownAsync()
    {
        // Detach notifications before StopAgentAsync invalidates the last
        // connected status. An intentional Quit must not emit a misleading
        // "Hub Connection Lost — Retrying" toast on the way out.
        if (_appState != null && _updateAvailableHandler != null)
            _appState.UpdateChecker.OnUpdateAvailable -= _updateAvailableHandler;
        if (_appState != null && _notificationConnectionHandler != null)
            _appState.ApiClient.OnConnectionStateChanged -= _notificationConnectionHandler;
        if (_appState != null && _notificationStatusHandler != null)
            _appState.ApiClient.OnStatusUpdated -= _notificationStatusHandler;
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
        var previous = _lastNotifiedConnectionState;
        _lastNotifiedConnectionState = connected;
        if (previous == null || previous == connected || _notifications == null)
            return;

        if (connected)
            _notifications.NotifyReconnected();
        else
            _notifications.NotifyDisconnected();
    }

    private void OnNotificationStatusUpdated(AgentStatus status)
    {
        if (_notifications == null)
            return;

        foreach (var alert in status.FiringAlerts)
            _notifications.NotifyAlert(alert.Name, alert.Severity, alert.Message);
    }
}
