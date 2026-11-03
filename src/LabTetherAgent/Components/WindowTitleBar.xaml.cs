using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LabTetherAgent.Components;

public sealed partial class WindowTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(WindowTitleBar),
            new PropertyMetadata("", (d, e) => ((WindowTitleBar)d).TitleText.Text = (string)e.NewValue));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public WindowTitleBar() { this.InitializeComponent(); }
}
