using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.Shared;

namespace LabTetherAgent.Views.Onboarding;

public sealed partial class OnboardingWindow : Window
{
    private readonly AppState _appState;

    public OnboardingViewModel ViewModel { get; }
    public event Action? OnCompleted;

    public OnboardingWindow(AppState appState)
    {
        _appState = appState;
        this.InitializeComponent();
        WindowChrome.Apply(this, Root, TitleBar);
        WindowChrome.SizeContent(this, Root, 660, 640);

        ViewModel = new OnboardingViewModel(
            appState.Settings,
            appState.CredentialStore,
            appState.ConnectionTester,
            appState.ConnectAgentForSetupAsync);

        ViewModel.OnCompleted += () =>
        {
            OnCompleted?.Invoke();
            Close();
        };
        Closed += (_, _) => ViewModel.CancelConnectionAttempt();
        // Advanced opens below the fold of the fixed-size window; scroll to it.
        AdvancedExpander.Expanding += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => AdvancedExpander.StartBringIntoView());
    }

    internal void ExpandAdvanced(bool expand)
    {
        if (!expand) return;
        AdvancedExpander.Loaded += (_, _) =>
        {
            AdvancedExpander.IsExpanded = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => AdvancedExpander.StartBringIntoView());
        };
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnViewLogs(object sender, RoutedEventArgs e) =>
        new LogViewer.LogViewerWindow(_appState).Activate();
}
