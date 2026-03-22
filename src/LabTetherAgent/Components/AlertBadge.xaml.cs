using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LabTetherAgent.Components;

public sealed partial class AlertBadge : UserControl
{
    public static readonly DependencyProperty AlertNameProperty =
        DependencyProperty.Register(nameof(AlertName), typeof(string), typeof(AlertBadge),
            new PropertyMetadata("", OnNameChanged));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(string), typeof(AlertBadge),
            new PropertyMetadata("warning", OnSeverityChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(AlertBadge),
            new PropertyMetadata("", OnMessageChanged));

    public string AlertName { get => (string)GetValue(AlertNameProperty); set => SetValue(AlertNameProperty, value); }
    public string Severity { get => (string)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public AlertBadge() { this.InitializeComponent(); }

    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AlertBadge)d).NameText.Text = (string)e.NewValue;
    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AlertBadge)d).MessageText.Text = (string)e.NewValue;

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AlertBadge)d;
        var severity = ((string)e.NewValue).ToLowerInvariant();
        control.SeverityText.Text = severity.ToUpperInvariant();
        control.SeverityBadge.Background = new SolidColorBrush(severity switch
        {
            "critical" => Colors.Red,
            "warning" => Colors.Orange,
            _ => Colors.DodgerBlue
        });
    }
}
