using LabTetherAgent.Views.TrayIcon;

namespace LabTetherAgent.Tests.Views.TrayIcon;

public class TrayIconManagerTests
{
    [Fact]
    public void Initialize_ExplicitlyRegistersCodeCreatedTrayIcon()
    {
        var source = File.ReadAllText(FindTrayIconManagerSource());

        Assert.Contains(
            "_taskbarIcon.ForceCreate(enablesEfficiencyMode: false);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextMenu_UsesCommandsRequiredByNativePopupMode()
    {
        var source = File.ReadAllText(FindTrayIconManagerSource());
        var buildMenuStart = source.IndexOf(
            "private Microsoft.UI.Xaml.Controls.MenuFlyout BuildContextMenu()",
            StringComparison.Ordinal);
        var openConsoleStart = source.IndexOf(
            "private void OpenConsole()",
            StringComparison.Ordinal);

        Assert.True(buildMenuStart >= 0, "BuildContextMenu source was not found.");
        Assert.True(openConsoleStart > buildMenuStart, "OpenConsole source boundary was not found.");

        var buildMenuSource = source[buildMenuStart..openConsoleStart];
        Assert.DoesNotContain(".Click +=", buildMenuSource, StringComparison.Ordinal);
        Assert.Equal(7, CountOccurrences(buildMenuSource, "Command ="));
    }

    [Fact]
    public void ResolveTrayIconVisual_ReturnsDeterministicConnectedVisual()
    {
        var visual = TrayIconManager.ResolveTrayIconVisual(connected: true);

        Assert.Equal("L", visual.Glyph);
        Assert.Equal((byte)46, visual.Red);
        Assert.Equal((byte)160, visual.Green);
        Assert.Equal((byte)67, visual.Blue);
    }

    [Fact]
    public void ResolveTrayIconVisual_UsesDistinctDisconnectedVisual()
    {
        var connected = TrayIconManager.ResolveTrayIconVisual(connected: true);
        var disconnected = TrayIconManager.ResolveTrayIconVisual(connected: false);

        Assert.Equal("L", disconnected.Glyph);
        Assert.NotEqual(connected, disconnected);
        Assert.Equal((byte)117, disconnected.Red);
        Assert.Equal((byte)117, disconnected.Green);
        Assert.Equal((byte)117, disconnected.Blue);
    }

    [Fact]
    public void CreateTrayIcon_ProducesVisibleNativePixels()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var visual = TrayIconManager.ResolveTrayIconVisual(connected: true);
        using var icon = TrayIconManager.CreateTrayIcon(visual);
        using var bitmap = icon.ToBitmap();

        Assert.Equal(32, bitmap.Width);
        Assert.Equal(32, bitmap.Height);

        var visiblePixels = 0;
        var greenPixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 0)
                    visiblePixels++;
                if (pixel.A > 0 && pixel.G > pixel.R && pixel.G > pixel.B)
                    greenPixels++;
            }
        }

        Assert.True(visiblePixels > 500, $"Only {visiblePixels} tray-icon pixels were visible.");
        Assert.True(greenPixels > 250, $"Only {greenPixels} tray-icon pixels carried the connected state.");
    }

    private static string FindTrayIconManagerSource()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current != null;
             current = current.Parent)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "LabTetherAgent",
                "Views",
                "TrayIcon",
                "TrayIconManager.cs");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Unable to locate TrayIconManager.cs from the test output directory.");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
