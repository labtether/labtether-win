using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.Onboarding;

public sealed partial class OnboardingWindow : Window
{
    public OnboardingViewModel ViewModel { get; }
    public event Action? OnCompleted;

    public OnboardingWindow(AppState appState)
    {
        this.InitializeComponent();
        AppWindow.Resize(new SizeInt32(560, 460));
        SystemBackdrop = new MicaBackdrop();

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

        // Navigate to first step
        NavigateToStep(1);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.CurrentStep))
                NavigateToStep(ViewModel.CurrentStep);
        };
        Closed += (_, _) => ViewModel.CancelConnectionAttempt();
    }

    private void NavigateToStep(int step)
    {
        switch (step)
        {
            case 1:
                StepFrame.Navigate(typeof(HubUrlPage), ViewModel);
                break;
            case 2:
                StepFrame.Navigate(typeof(TokenPage), ViewModel);
                break;
            case 3:
                StepFrame.Navigate(typeof(ConnectingPage), ViewModel);
                break;
        }
    }

    private SolidColorBrush StepIndicatorBrush(int step, int currentStep)
    {
        return step <= currentStep
            ? (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (SolidColorBrush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"];
    }

    private Visibility IsLastStep(int step) =>
        step == 3 ? Visibility.Visible : Visibility.Collapsed;

    private Visibility IsNotLastStep(int step) =>
        step < 3 ? Visibility.Visible : Visibility.Collapsed;
}
