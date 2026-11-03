using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty IsProblemProperty =
        DependencyProperty.Register(nameof(IsProblem), typeof(bool), typeof(StatusIndicator),
            new PropertyMetadata(false, (d, _) => ((StatusIndicator)d).ApplyState()));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("", (d, _) => ((StatusIndicator)d).ApplyState()));

    public bool IsProblem { get => (bool)GetValue(IsProblemProperty); set => SetValue(IsProblemProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    public StatusIndicator()
    {
        this.InitializeComponent();
        Loaded += (_, _) => ApplyState();
    }

    private void ApplyState()
    {
        StatusLabel.Text = StatusText;
        AutomationProperties.SetName(this, StatusText);
        VisualStateManager.GoToState(this, IsProblem ? "Problem" : "Normal", false);
    }
}
