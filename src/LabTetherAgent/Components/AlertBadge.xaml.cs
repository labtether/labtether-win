using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class AlertBadge : UserControl
{
    public static readonly DependencyProperty AlertNameProperty =
        DependencyProperty.Register(nameof(AlertName), typeof(string), typeof(AlertBadge),
            new PropertyMetadata("", (d, _) => ((AlertBadge)d).Apply()));

    public static readonly DependencyProperty IsCriticalProperty =
        DependencyProperty.Register(nameof(IsCritical), typeof(bool), typeof(AlertBadge),
            new PropertyMetadata(false, (d, _) => ((AlertBadge)d).Apply()));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(AlertBadge),
            new PropertyMetadata("", (d, _) => ((AlertBadge)d).Apply()));

    public string AlertName { get => (string)GetValue(AlertNameProperty); set => SetValue(AlertNameProperty, value); }
    public bool IsCritical { get => (bool)GetValue(IsCriticalProperty); set => SetValue(IsCriticalProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public AlertBadge()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    private void Apply()
    {
        NameText.Text = AlertName;
        MessageText.Text = Message;
        MessageText.Visibility = string.IsNullOrEmpty(Message) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(SeverityDot, IsCritical ? "Critical" : "Warning");
        AutomationProperties.SetName(this, $"{(IsCritical ? "Critical" : "Warning")}: {AlertName}");
        VisualStateManager.GoToState(this, IsCritical ? "Critical" : "Warning", false);
    }
}
