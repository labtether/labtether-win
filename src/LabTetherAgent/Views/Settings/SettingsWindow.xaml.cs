using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(AppState appState)
    {
        this.InitializeComponent();
        AppWindow.Resize(new SizeInt32(500, 640));
        SystemBackdrop = new MicaBackdrop();

        ViewModel = new SettingsViewModel(appState.Settings, appState.CredentialStore);
        ViewModel.OnRestartRequired += async () =>
        {
            await appState.RestartAgentAsync();
        };
    }
}
