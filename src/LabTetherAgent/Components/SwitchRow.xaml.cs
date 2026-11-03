using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class SwitchRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SwitchRow),
            new PropertyMetadata("", (d, _) => ((SwitchRow)d).Apply()));

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(SwitchRow),
            new PropertyMetadata(false, (d, _) => ((SwitchRow)d).Apply()));

    public static readonly DependencyProperty AutomationIdProperty =
        DependencyProperty.Register(nameof(AutomationId), typeof(string), typeof(SwitchRow),
            new PropertyMetadata("", (d, _) => ((SwitchRow)d).Apply()));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public string AutomationId { get => (string)GetValue(AutomationIdProperty); set => SetValue(AutomationIdProperty, value); }

    public SwitchRow()
    {
        this.InitializeComponent();
        IsEnabledChanged += (_, _) => Apply();
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (IsOn != Switch.IsOn)
            IsOn = Switch.IsOn;
    }

    private void Apply()
    {
        LabelText.Text = Label;
        StateText.Text = IsOn ? "On" : "Off";
        // A row Windows will not let the user change reads as unavailable.
        LabelText.Opacity = StateText.Opacity = IsEnabled ? 1.0 : 0.55;
        if (Switch.IsOn != IsOn)
            Switch.IsOn = IsOn;
        // The switch carries the row's label, so screen readers and UI
        // Automation find it by name ("Let the Hub change settings").
        AutomationProperties.SetName(Switch, Label);
        AutomationProperties.SetAutomationId(Switch, AutomationId);
    }
}
