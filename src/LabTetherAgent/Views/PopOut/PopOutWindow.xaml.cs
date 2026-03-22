using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.PopOut;

public sealed partial class PopOutWindow : Window
{
    public PopOutViewModel ViewModel { get; }

    public PopOutWindow(AppState appState)
    {
        this.InitializeComponent();
        SystemBackdrop = new MicaBackdrop();

        ViewModel = new PopOutViewModel(appState.ApiClient);
        ViewModel.HubUrl = appState.Settings.HubUrl;

        // Always on top
        // Note: WinUI 3 doesn't have a direct Topmost property.
        // Use AppWindow.SetPresenter or interop to set HWND_TOPMOST.
        // This will be finalized on Windows.

        Activated += (_, _) => ViewModel.OnWindowOpened();
        Closed += (_, _) => ViewModel.OnWindowClosed();
    }
}
