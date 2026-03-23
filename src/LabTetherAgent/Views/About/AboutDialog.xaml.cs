using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using LabTetherAgent.App;
using LabTetherAgent.Services;
using WinRT.Interop;

namespace LabTetherAgent.Views.About;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly AppState _appState;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetActiveWindow();

    public AboutDialog(AppState appState)
    {
        this.InitializeComponent();
        _appState = appState;

        // Populate version info
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText.Text = assemblyVersion != null
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "0.0.0";
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
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = $"labtether-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add("ZIP Archive", [".zip"]);

        // WinUI 3 requires initializing the picker with the owning window handle.
        // Use GetActiveWindow() since this ContentDialog is always shown within
        // an active window, and the tray-only app has no persistent MainWindow.
        var hwnd = GetActiveWindow();
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return; // User cancelled

        var collector = new DiagnosticsCollector(
            _appState.Settings,
            _appState.AgentProcess.LogReader,
            _appState.ApiClient);

        // Write to a temp file first, then copy to the picker-selected location
        var tempPath = Path.Combine(Path.GetTempPath(), $"labtether-diag-{Guid.NewGuid()}.zip");
        try
        {
            await collector.ExportAsync(tempPath);
            var bytes = await File.ReadAllBytesAsync(tempPath);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
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
