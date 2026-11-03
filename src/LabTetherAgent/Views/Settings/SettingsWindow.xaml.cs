using Microsoft.UI.Xaml;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.Shared;

namespace LabTetherAgent.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(AppState appState)
    {
        this.InitializeComponent();
        WindowChrome.Apply(this, Root, TitleBar);
        WindowChrome.SizeContent(this, Root, 660, 640);

        ViewModel = new SettingsViewModel(appState.Settings, appState.CredentialStore);
        ViewModel.OnRestartRequired += async () =>
        {
            await appState.RestartAgentAsync();
        };
    }
}
