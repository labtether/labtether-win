using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using WinRT.Interop;

namespace LabTetherAgent.Views.LogViewer;

public sealed partial class LogViewerWindow : Window
{
    private readonly AppState _appState;
    public LogViewerViewModel ViewModel { get; }

    public LogViewerWindow(AppState appState)
    {
        _appState = appState;
        this.InitializeComponent();
        AppWindow.Resize(new SizeInt32(700, 500));
        SystemBackdrop = new MicaBackdrop();
        ViewModel = new LogViewerViewModel(appState.AgentProcess.LogReader);
        ViewModel.OnNewLine += OnNewLogLine;
        Closed += OnClosed;
    }

    private void OnNewLogLine()
    {
        if (!ViewModel.AutoScroll || ViewModel.FilteredLines.Count == 0)
            return;

        LogList.ScrollIntoView(ViewModel.FilteredLines[^1]);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ViewModel.OnNewLine -= OnNewLogLine;
        ViewModel.Dispose();
        Closed -= OnClosed;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        ExportStatus.IsOpen = false;
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"labtether-logs-{DateTimeOffset.Now:yyyyMMdd-HHmmss}",
            };
            picker.FileTypeChoices.Add("Text file", [".txt"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            await Windows.Storage.FileIO.WriteTextAsync(
                file,
                ViewModel.BuildExportContent(_appState.Settings));
            ExportStatus.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            ExportStatus.Title = "Logs exported";
            ExportStatus.Message = "The text file was saved successfully.";
            ExportStatus.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Log export failed: {ex.GetType().Name}: {ex.Message}");
            ExportStatus.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            ExportStatus.Title = "Log export failed";
            ExportStatus.Message = "Choose another location and try again.";
            ExportStatus.IsOpen = true;
        }
    }
}
