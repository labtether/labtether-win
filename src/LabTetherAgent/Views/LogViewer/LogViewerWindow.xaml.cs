using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.LogViewer;

public sealed partial class LogViewerWindow : Window
{
    public LogViewerViewModel ViewModel { get; }

    public LogViewerWindow(AppState appState)
    {
        this.InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ViewModel = new LogViewerViewModel(appState.AgentProcess.LogReader);
    }
}
