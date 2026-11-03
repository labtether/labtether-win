using Microsoft.UI.Xaml;

namespace LabTetherAgent.Views.Shared;

/// <summary>Small x:Bind helpers for views.</summary>
public static class Ui
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static string OnOff(bool value) => value ? "On" : "Off";
}
