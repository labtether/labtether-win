using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LabTetherAgent.Components;

public sealed partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(StatusIndicator),
            new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(StatusIndicator),
            new PropertyMetadata("Disconnected", OnTextChanged));

    public bool IsConnected { get => (bool)GetValue(IsConnectedProperty); set => SetValue(IsConnectedProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    public StatusIndicator() { this.InitializeComponent(); }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (StatusIndicator)d;
        var connected = (bool)e.NewValue;
        control.StatusDot.Fill = new SolidColorBrush(connected
            ? Colors.LimeGreen
            : Colors.Gray);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusIndicator)d).StatusLabel.Text = (string)e.NewValue;
}
