using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class UsageRow : UserControl
{
    /// <summary>Usage at or above this percent turns the bar and number amber.</summary>
    public const double HighThreshold = 85;

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(UsageRow),
            new PropertyMetadata("", (d, _) => ((UsageRow)d).Apply()));

    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(nameof(Percent), typeof(double), typeof(UsageRow),
            new PropertyMetadata(0.0, (d, _) => ((UsageRow)d).Apply()));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public UsageRow()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    private void Apply()
    {
        var percent = double.IsFinite(Percent) ? Math.Clamp(Percent, 0, 100) : 0;
        var shown = Math.Round(percent).ToString(CultureInfo.CurrentCulture);
        LabelText.Text = Label;
        ValueText.Text = shown;
        UsedColumn.Width = new GridLength(percent, GridUnitType.Star);
        FreeColumn.Width = new GridLength(100 - percent, GridUnitType.Star);
        AutomationProperties.SetName(this, $"{Label} {shown}%");
        VisualStateManager.GoToState(this, percent >= HighThreshold ? "High" : "Normal", false);
    }
}
