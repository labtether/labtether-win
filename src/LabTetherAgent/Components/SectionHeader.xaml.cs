using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader),
            new PropertyMetadata("", OnTitleChanged));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public SectionHeader() { this.InitializeComponent(); }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SectionHeader)d).TitleText.Text = ((string)e.NewValue).ToUpperInvariant();
}
