using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Services;

namespace LabTetherAgent.Views.Onboarding;

public sealed partial class OnboardingWindow : Window
{
    public OnboardingViewModel ViewModel { get; }
    public event Action? OnCompleted;

    public OnboardingWindow(AppState appState)
    {
        this.InitializeComponent();
        SystemBackdrop = new MicaBackdrop();

        ViewModel = new OnboardingViewModel(
            appState.Settings,
            appState.CredentialStore,
            appState.ConnectionTester);

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

    private static SolidColorBrush StepIndicatorBrush(int step, int currentStep)
    {
        return step <= currentStep
            ? (SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (SolidColorBrush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"];
    }

    private static Visibility IsLastStep(int step) =>
        step == 3 ? Visibility.Visible : Visibility.Collapsed;

    private static Visibility IsNotLastStep(int step) =>
        step < 3 ? Visibility.Visible : Visibility.Collapsed;
}
