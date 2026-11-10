using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
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
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnboardingViewModel.HasTrustPrompt) && ViewModel.HasTrustPrompt)
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FocusTrustPanel);
        };
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

    // Connect turns disabled while the panel asks, and focus fell back to the
    // Hub URL field. Focus the panel's Cancel instead: a keyboard user starts
    // at the question, and an Enter pressed twice cancels instead of trusting
    // a certificate nobody compared. The title is read out as it appears.
    private void FocusTrustPanel()
    {
        if (!ViewModel.HasTrustPrompt) return;
        CancelTrustButton.StartBringIntoView();
        CancelTrustButton.Focus(FocusState.Programmatic);
        (FrameworkElementAutomationPeer.FromElement(TrustHubTitle) ??
         FrameworkElementAutomationPeer.CreatePeerForElement(TrustHubTitle))?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnViewLogs(object sender, RoutedEventArgs e) =>
        new LogViewer.LogViewerWindow(_appState).Activate();
}
