using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using LabTetherAgent.App;
using LabTetherAgent.Services;
using WinRT.Interop;

namespace LabTetherAgent.Views.About;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly AppState _appState;
    private readonly nint _ownerWindowHandle;
    private bool _exportInProgress;

    public AboutDialog(AppState appState, nint ownerWindowHandle)
    {
        this.InitializeComponent();
        _appState = appState;
        _ownerWindowHandle = ownerWindowHandle;

        // Populate version info
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText.Text = assemblyVersion != null
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "0.0.0";
        AgentVersionText.Text = ReadAgentVersion();
        HubUrlText.Text = appState.Settings.HubUrl;
        FingerprintText.Text = ""; // filled from /agent/info
        VersionText.Text = $"Version {AppVersionText.Text}";

        // Load fingerprint from API
        _ = LoadFingerprintAsync();
    }

    private async Task LoadFingerprintAsync()
    {
        var info = await _appState.ApiClient.FetchInfoAsync();
        FingerprintText.Text = string.IsNullOrWhiteSpace(info?.Fingerprint) ? "Not available" : info!.Fingerprint;
    }

    // Handle "Export Diagnostics" button
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Don't close dialog yet
        if (_exportInProgress)
            return;

        var deferral = args.GetDeferral();
        _exportInProgress = true;
        IsPrimaryButtonEnabled = false;
        ShowExportStatus(null, problem: false);
        try
        {
            var exported = await ExportDiagnosticsAsync();
            if (exported)
                ShowExportStatus("Diagnostics saved", problem: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Diagnostics export failed: {ex.GetType().Name}: {ex.Message}");
            ShowExportStatus("Could not save diagnostics. Choose another folder.", problem: true);
        }
        finally
        {
            _exportInProgress = false;
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }

    private void ShowExportStatus(string? text, bool problem)
    {
        ExportStatus.Text = problem ? "" : text ?? "";
        ExportError.Text = problem ? text ?? "" : "";
        ExportStatus.Visibility = !problem && text != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ExportError.Visibility = problem ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async Task<bool> ExportDiagnosticsAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = DiagnosticsExport.CreateSuggestedFileName(DateTimeOffset.Now),
        };
        picker.FileTypeChoices.Add("ZIP archive", [".zip"]);

        // WinUI 3 requires initializing the picker with the owning window handle.
        // Use the exact flyout HWND. GetActiveWindow() can return a transient
        // shell/dialog window in a tray-only process and make picker ownership
        // nondeterministic.
        if (_ownerWindowHandle == 0)
            throw new InvalidOperationException("The diagnostics picker has no owner window.");
        InitializeWithWindow.Initialize(picker, _ownerWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return false; // User cancelled

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
            return true;
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
