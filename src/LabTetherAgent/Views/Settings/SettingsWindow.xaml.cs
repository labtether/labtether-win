using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(AppState appState)
    {
        this.InitializeComponent();
        SystemBackdrop = new MicaBackdrop();

        ViewModel = new SettingsViewModel(appState.Settings, appState.CredentialStore);
        ViewModel.OnRestartRequired += async () =>
        {
            await appState.RestartAgentAsync();
        };
    }
}
