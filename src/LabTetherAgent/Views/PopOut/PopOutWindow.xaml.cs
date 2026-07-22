using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.PopOut;

public sealed partial class PopOutWindow : Window
{
    public PopOutViewModel ViewModel { get; }

    public PopOutWindow(AppState appState)
    {
        this.InitializeComponent();
        AppWindow.Resize(new SizeInt32(300, 200));
        SystemBackdrop = new MicaBackdrop();

        ViewModel = new PopOutViewModel(appState.ApiClient);
        ViewModel.HubUrl = appState.Settings.HubUrl;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;

        Activated += (_, _) => ViewModel.OnWindowOpened();
        Closed += (_, _) => ViewModel.OnWindowClosed();
    }
}
