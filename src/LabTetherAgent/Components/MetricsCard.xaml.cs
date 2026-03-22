using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class MetricsCard : UserControl
{
    public static readonly DependencyProperty CpuPercentProperty =
        DependencyProperty.Register(nameof(CpuPercent), typeof(double), typeof(MetricsCard),
            new PropertyMetadata(0.0, OnCpuChanged));

    public static readonly DependencyProperty MemoryTextProperty =
        DependencyProperty.Register(nameof(MemoryText), typeof(string), typeof(MetricsCard),
            new PropertyMetadata("--", OnMemoryChanged));

    public static readonly DependencyProperty DiskPercentProperty =
        DependencyProperty.Register(nameof(DiskPercent), typeof(double), typeof(MetricsCard),
            new PropertyMetadata(0.0, OnDiskChanged));

    public double CpuPercent { get => (double)GetValue(CpuPercentProperty); set => SetValue(CpuPercentProperty, value); }
    public string MemoryText { get => (string)GetValue(MemoryTextProperty); set => SetValue(MemoryTextProperty, value); }
    public double DiskPercent { get => (double)GetValue(DiskPercentProperty); set => SetValue(DiskPercentProperty, value); }

    public MetricsCard() { this.InitializeComponent(); }

    private static void OnCpuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetricsCard)d).CpuText.Text = $"{(double)e.NewValue:F0}%";
    private static void OnMemoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetricsCard)d).MemText.Text = (string)e.NewValue;
    private static void OnDiskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MetricsCard)d).DiskText.Text = $"{(double)e.NewValue:F0}%";
}
