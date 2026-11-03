using Microsoft.UI.Xaml;
using Windows.UI;

namespace LabTetherAgent.Theme;

/// <summary>
/// Reads one generated token colour for a named theme ("Light" or "Dark").
/// Code that cannot use {ThemeResource} (tray icon pixels, caption buttons,
/// control brushes) reads the same values XAML uses.
/// </summary>
internal static class ThemeColors
{
    public static Color Get(string token, ElementTheme theme) =>
        Get(Application.Current.Resources, token, theme == ElementTheme.Light ? "Light" : "Dark");

    public static Color Get(ResourceDictionary resources, string token, string theme)
    {
        if (TryFind(resources, token, theme, out var color))
            return color;
        throw new KeyNotFoundException($"The {theme} theme has no colour named {token}.");
    }

    private static bool TryFind(ResourceDictionary dictionary, string token, string theme, out Color color)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(token, out var value)
            && value is Color found)
        {
            color = found;
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryFind(merged, token, theme, out color))
                return true;
        }

        color = default;
        return false;
    }
}
