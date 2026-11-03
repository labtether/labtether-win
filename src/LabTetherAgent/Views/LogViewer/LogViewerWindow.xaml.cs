using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.Shared;
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
        WindowChrome.Apply(this, Root, TitleBar);
        WindowChrome.SizeContent(this, Root, 760, 540);
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
        ShowExportStatus("", problem: false);
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
            ShowExportStatus($"Saved {file.Name}", problem: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Log export failed: {ex.GetType().Name}: {ex.Message}");
            ShowExportStatus("Could not save the logs. Choose another folder.", problem: true);
        }
    }

    private void ShowExportStatus(string text, bool problem)
    {
        ExportStatus.Text = problem ? "" : text;
        ExportError.Text = problem ? text : "";
    }
}
