using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class MetricsCard : UserControl
{
    public static readonly DependencyProperty CpuPercentProperty =
        DependencyProperty.Register(nameof(CpuPercent), typeof(double), typeof(MetricsCard),
            new PropertyMetadata(0.0, (d, e) => ((MetricsCard)d).CpuRow.Percent = (double)e.NewValue));

    public static readonly DependencyProperty MemoryPercentProperty =
        DependencyProperty.Register(nameof(MemoryPercent), typeof(double), typeof(MetricsCard),
            new PropertyMetadata(0.0, (d, e) => ((MetricsCard)d).MemoryRow.Percent = (double)e.NewValue));

    public static readonly DependencyProperty DiskPercentProperty =
        DependencyProperty.Register(nameof(DiskPercent), typeof(double), typeof(MetricsCard),
            new PropertyMetadata(0.0, (d, e) => ((MetricsCard)d).DiskRow.Percent = (double)e.NewValue));

    public double CpuPercent { get => (double)GetValue(CpuPercentProperty); set => SetValue(CpuPercentProperty, value); }
    public double MemoryPercent { get => (double)GetValue(MemoryPercentProperty); set => SetValue(MemoryPercentProperty, value); }
    public double DiskPercent { get => (double)GetValue(DiskPercentProperty); set => SetValue(DiskPercentProperty, value); }

    public MetricsCard() { this.InitializeComponent(); }
}
