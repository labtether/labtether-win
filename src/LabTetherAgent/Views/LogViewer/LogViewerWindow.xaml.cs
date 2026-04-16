using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.LogViewer;

public sealed partial class LogViewerWindow : Window
{
    public LogViewerViewModel ViewModel { get; }

    public LogViewerWindow(AppState appState)
    {
        this.InitializeComponent();
        AppWindow.Resize(new SizeInt32(700, 500));
        SystemBackdrop = new MicaBackdrop();
        ViewModel = new LogViewerViewModel(appState.AgentProcess.LogReader);
    }
}
