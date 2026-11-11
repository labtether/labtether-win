using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.Shared;

namespace LabTetherAgent.Views.PopOut;

public sealed partial class PopOutWindow : Window
{
    public PopOutViewModel ViewModel { get; }

    public PopOutWindow(AppState appState)
    {
        this.InitializeComponent();
        ViewModel = new PopOutViewModel(appState.ApiClient) { HubUrl = appState.Settings.IsEnrolled ? appState.Settings.HubUrl : "" };
        // Like the flyout: a stopped agent reads "Stopped" and says why.
        ViewModel.FollowAgentProcess(appState.AgentProcess);
        WindowChrome.Apply(this, Root, TitleBar);
        WindowChrome.SizeContent(this, Root, 320, 204);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;

        Activated += (_, _) => ViewModel.OnWindowOpened();
        Closed += (_, _) => ViewModel.OnWindowClosed();
    }
}
