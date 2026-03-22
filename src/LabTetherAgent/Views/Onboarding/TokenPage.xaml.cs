using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.Onboarding;

public sealed partial class TokenPage : Page
{
    public OnboardingViewModel? ViewModel { get; private set; }

    public TokenPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = e.Parameter as OnboardingViewModel;
        base.OnNavigatedTo(e);
    }
}
