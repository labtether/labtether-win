using Microsoft.UI.Xaml.Controls;
using LabTetherAgent.App;
using LabTetherAgent.Services;

namespace LabTetherAgent.Views.About;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly AppState _appState;

    public AboutDialog(AppState appState)
    {
        this.InitializeComponent();
        _appState = appState;

        // Populate version info
        AppVersionText.Text = "0.1.0"; // TODO: read from assembly
        AgentVersionText.Text = ReadAgentVersion();
        HubUrlText.Text = appState.Settings.HubUrl;
        FingerprintText.Text = "—"; // populated from /agent/info
        VersionText.Text = $"v{AppVersionText.Text} (agent v{AgentVersionText.Text})";

        // Load fingerprint from API
        _ = LoadFingerprintAsync();
    }

    private async Task LoadFingerprintAsync()
    {
        var info = await _appState.ApiClient.FetchInfoAsync();
        if (info?.Fingerprint != null)
            FingerprintText.Text = info.Fingerprint;
    }

    // Handle "Export Diagnostics" button
    protected void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Don't close dialog yet

        _ = ExportDiagnosticsAsync();
    }

    private async Task ExportDiagnosticsAsync()
    {
        var collector = new DiagnosticsCollector(
            _appState.Settings,
            _appState.AgentProcess.LogReader,
            _appState.ApiClient);

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"labtether-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        await collector.ExportAsync(path);

        // TODO: Use FileSavePicker on Windows for proper UX
    }

    private static string ReadAgentVersion()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(dir, "AGENT_VERSION");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return "unknown";
    }
}
