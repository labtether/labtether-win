using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LabTetherAgent.Theme;

/// <summary>
/// Dresses the stock WinUI controls in the generated 2.0 tokens. Rose goes only
/// to AccentButtonStyle, which each window uses for its one main button.
/// Switches, checks, radio buttons, selection marks and the focused text box
/// edge use the text colour instead of the system accent.
/// The brushes are built per theme from LabTetherTheme.xaml, so no token value
/// is repeated here. High contrast keeps the stock WinUI values.
/// </summary>
internal static class ControlBrushes
{
    private static readonly string[] Themes = ["Light", "Dark"];

    private static readonly (string Control, string Token)[] Map =
    [
        ("TextFillColorPrimaryBrush", "LTTextColor"),
        ("TextFillColorSecondaryBrush", "LTText2Color"),
        ("TextFillColorTertiaryBrush", "LTText3Color"),

        ("AccentButtonBackground", "LTRoseColor"),
        ("AccentButtonBackgroundPointerOver", "LTRoseHoverColor"),
        ("AccentButtonBackgroundPressed", "LTRosePressColor"),
        ("AccentButtonForeground", "LTAccentContrastColor"),
        ("AccentButtonForegroundPointerOver", "LTAccentContrastColor"),
        ("AccentButtonForegroundPressed", "LTAccentContrastColor"),

        ("ButtonBackground", "LTPanelColor"),
        ("ButtonBackgroundPointerOver", "LTCtlHoverColor"),
        ("ButtonBackgroundPressed", "LTCtlPressColor"),
        ("ButtonForeground", "LTTextColor"),
        ("ButtonForegroundPointerOver", "LTTextColor"),
        ("ButtonForegroundPressed", "LTTextColor"),
        ("ButtonBorderBrush", "LTFieldLineColor"),
        ("ButtonBorderBrushPointerOver", "LTFieldLineColor"),
        ("ButtonBorderBrushPressed", "LTFieldLineColor"),

        ("ToggleSwitchFillOn", "LTTextColor"),
        ("ToggleSwitchFillOnPointerOver", "LTText2Color"),
        ("ToggleSwitchFillOnPressed", "LTText2Color"),
        ("ToggleSwitchStrokeOn", "LTTextColor"),
        ("ToggleSwitchStrokeOnPointerOver", "LTText2Color"),
        ("ToggleSwitchStrokeOnPressed", "LTText2Color"),
        ("ToggleSwitchKnobFillOn", "LTPanelColor"),
        ("ToggleSwitchKnobFillOnPointerOver", "LTPanelColor"),
        ("ToggleSwitchKnobFillOnPressed", "LTPanelColor"),
        ("ToggleSwitchStrokeOff", "LTText2Color"),
        ("ToggleSwitchStrokeOffPointerOver", "LTTextColor"),
        ("ToggleSwitchStrokeOffPressed", "LTTextColor"),
        ("ToggleSwitchKnobFillOff", "LTText2Color"),
        ("ToggleSwitchKnobFillOffPointerOver", "LTTextColor"),
        ("ToggleSwitchKnobFillOffPressed", "LTTextColor"),
        ("ToggleSwitchFillOffPointerOver", "LTHoverColor"),
        ("ToggleSwitchFillOffPressed", "LTSelColor"),

        ("CheckBoxCheckBackgroundFillChecked", "LTTextColor"),
        ("CheckBoxCheckBackgroundFillCheckedPointerOver", "LTText2Color"),
        ("CheckBoxCheckBackgroundFillCheckedPressed", "LTText2Color"),
        ("CheckBoxCheckBackgroundStrokeChecked", "LTTextColor"),
        ("CheckBoxCheckBackgroundStrokeCheckedPointerOver", "LTText2Color"),
        ("CheckBoxCheckBackgroundStrokeCheckedPressed", "LTText2Color"),
        ("CheckBoxCheckGlyphForegroundChecked", "LTPanelColor"),
        ("CheckBoxCheckGlyphForegroundCheckedPointerOver", "LTPanelColor"),
        ("CheckBoxCheckGlyphForegroundCheckedPressed", "LTPanelColor"),
        ("CheckBoxCheckBackgroundStrokeUnchecked", "LTText3Color"),

        ("RadioButtonOuterEllipseCheckedFill", "LTTextColor"),
        ("RadioButtonOuterEllipseCheckedFillPointerOver", "LTText2Color"),
        ("RadioButtonOuterEllipseCheckedFillPressed", "LTText2Color"),
        ("RadioButtonOuterEllipseCheckedStroke", "LTTextColor"),
        ("RadioButtonOuterEllipseCheckedStrokePointerOver", "LTText2Color"),
        ("RadioButtonOuterEllipseCheckedStrokePressed", "LTText2Color"),
        ("RadioButtonCheckGlyphFill", "LTPanelColor"),
        ("RadioButtonCheckGlyphFillPointerOver", "LTPanelColor"),
        ("RadioButtonCheckGlyphFillPressed", "LTPanelColor"),
        ("RadioButtonOuterEllipseStroke", "LTText3Color"),

        ("TextControlBackground", "LTPanelColor"),
        ("TextControlBackgroundPointerOver", "LTPanelColor"),
        ("TextControlBackgroundFocused", "LTPanelColor"),
        ("TextControlPlaceholderForeground", "LTText3Color"),
        ("TextControlPlaceholderForegroundPointerOver", "LTText3Color"),
        ("TextControlPlaceholderForegroundFocused", "LTText3Color"),

        ("ComboBoxBackground", "LTPanelColor"),
        ("ComboBoxBackgroundPointerOver", "LTCtlHoverColor"),
        ("ComboBoxBackgroundPressed", "LTCtlPressColor"),
        ("ComboBoxBorderBrush", "LTFieldLineColor"),
        ("ComboBoxBorderBrushPointerOver", "LTFieldLineColor"),
        ("ComboBoxItemPillFillBrush", "LTTextColor"),

        ("ListViewItemSelectionIndicatorBrush", "LTTextColor"),
        ("ListViewItemSelectionIndicatorPointerOverBrush", "LTTextColor"),
        ("ListViewItemSelectionIndicatorPressedBrush", "LTTextColor"),

        ("HyperlinkButtonForeground", "LTTextColor"),
        ("HyperlinkButtonForegroundPointerOver", "LTText2Color"),
        ("HyperlinkButtonForegroundPressed", "LTText3Color"),

        ("ExpanderHeaderBackground", "LTPanelColor"),
        ("ExpanderHeaderBorderBrush", "LTLineColor"),
        ("ExpanderContentBackground", "LTPanelColor"),
        ("ExpanderContentBorderBrush", "LTLineColor"),

        ("ContentDialogBackground", "LTCanvasColor"),
        ("ContentDialogTopOverlay", "LTPanelColor"),
        ("ContentDialogBorderBrush", "LTLineColor"),
    ];

    /// <summary>Adds the control brushes to the application resources.</summary>
    public static void Install(ResourceDictionary appResources)
    {
        var overrides = new ResourceDictionary();
        foreach (var theme in Themes)
        {
            var target = new ResourceDictionary();
            foreach (var (control, token) in Map)
                target[control] = new SolidColorBrush(ThemeColors.Get(appResources, token, theme));

            // A 2 px bottom edge in the text colour when a text box has focus,
            // drawn like WinUI's own focused edge (one hard stop, no blend).
            target["TextControlBorderBrushFocused"] = FocusedEdge(
                ThemeColors.Get(appResources, "LTTextColor", theme),
                ThemeColors.Get(appResources, "LTFieldLineColor", theme));
            overrides.ThemeDictionaries[theme] = target;
        }

        // An empty HighContrast entry keeps the stock high contrast brushes.
        // Without it a contrast theme fell back to the Light brushes above:
        // white buttons and list boxes, text on backplates, faint switches.
        overrides.ThemeDictionaries["HighContrast"] = new ResourceDictionary();

        appResources.MergedDictionaries.Add(overrides);
    }

    private static LinearGradientBrush FocusedEdge(Color edge, Color field)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 2),
            RelativeTransform = new ScaleTransform { ScaleY = -1, CenterY = 0.5 },
        };
        brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = edge });
        brush.GradientStops.Add(new GradientStop { Offset = 1.0, Color = field });
        return brush;
    }
}
