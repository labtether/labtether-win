using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.TrayIcon;

public sealed partial class FlyoutWindow : Window
{
    public FlyoutViewModel ViewModel { get; }

    public FlyoutWindow(AppState appState)
    {
        this.InitializeComponent();

        // Mica backdrop (falls back to solid color on Win10)
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        ViewModel = new FlyoutViewModel(appState.ApiClient);
        ViewModel.HubUrl = appState.Settings.HubUrl;

        // Visibility-aware polling
        Activated += (_, _) => ViewModel.OnFlyoutOpened();
        Closed += (_, _) => ViewModel.OnFlyoutClosed();
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var win = new LogViewer.LogViewerWindow(AppState.Shared);
        win.Activate();
    }

    private void OnPopOut(object sender, RoutedEventArgs e)
    {
        var win = new PopOut.PopOutWindow(AppState.Shared);
        win.Activate();
    }
}
