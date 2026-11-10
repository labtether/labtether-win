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

        ViewModel = new SettingsViewModel(appState.Settings, appState.CredentialStore)
        {
            CaFileNotice = appState.CaFileNotice,
        };
        _ = RefreshCaFileNoticeAsync(appState);
        ViewModel.OnRestartRequired += async () =>
        {
            await appState.RestartAgentAsync();
        };
        // Cancel and Save end the dialog, as the footer buttons promise.
        ViewModel.OnCancelled += Close;
        ViewModel.OnSaved += Close;
    }

    // A CA file an earlier version saved unchecked: ask the Hub now and show
    // the answer beside the field.
    private async Task RefreshCaFileNoticeAsync(AppState appState)
    {
        await appState.RecheckCaFileAsync();
        // The notice is about the saved file, not one typed meanwhile.
        if (ViewModel.TlsCaFile == appState.Settings.TlsCaFile)
            ViewModel.CaFileNotice = appState.CaFileNotice;
    }
}
