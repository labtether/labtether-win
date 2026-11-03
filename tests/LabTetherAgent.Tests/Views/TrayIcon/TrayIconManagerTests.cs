using LabTetherAgent.Presentation;
using LabTetherAgent.State;
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

    private static readonly TrayPalette Palette = new(
        System.Drawing.Color.FromArgb(24, 24, 27),
        System.Drawing.Color.FromArgb(82, 82, 91),
        System.Drawing.Color.FromArgb(194, 122, 14),
        System.Drawing.Color.FromArgb(220, 48, 48));

    // The shell shows these pixels on a light or dark taskbar. The glyph must
    // be visible ink with real transparency (no filled background), at every
    // tray size Windows asks for, and each state must look different.
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    public void CreateTrayIcon_DrawsTransparentGlyphWithDistinctStates(int size)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var ok = Render(TrayGlyphState.Ok, size);
        using var warning = Render(TrayGlyphState.Warning, size);
        using var critical = Render(TrayGlyphState.Critical, size);
        using var offline = Render(TrayGlyphState.Offline, size);

        Assert.Equal(size, ok.Width);
        Assert.Equal(size, ok.Height);
        Assert.Equal(0, ok.GetPixel(0, 0).A);
        Assert.True(CountIn(ok, size, 5, 5, 11, 9, p => p.A < 128) > 0, "The face is not cut out of the screen.");
        Assert.True(Count(ok, p => p.A > 200 && p.R < 60) > size * 2, "The connected glyph has too little ink.");
        Assert.Equal(0, Count(ok, IsWarning));

        Assert.True(Count(warning, IsWarning) > size / 2, "The warning dot is missing.");
        Assert.True(Count(critical, IsCritical) > size / 2, "The critical dot is missing.");
        Assert.Equal(0, Count(critical, IsWarning));

        Assert.Equal(0, Count(offline, IsWarning) + Count(offline, IsCritical));
        Assert.True(Count(offline, p => p.A > 200 && p.R < 60) < Count(ok, p => p.A > 200 && p.R < 60),
            "The offline glyph is not dimmed.");
    }

    [Fact]
    public void TrayStatus_OrdersStatesAndWritesTwoLineTooltips()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            var now = DateTimeOffset.Now;
            var at = new DateTimeOffset(now.Year, now.Month, now.Day, 21, 12, 0, now.Offset);
            var disk = new AgentStatus
            {
                IsConnected = true,
                Alerts = [new AlertSnapshot("Disk is 91% full", "firing", "warning", "218 of 238 GB used")],
            };
            var two = new AgentStatus
            {
                IsConnected = true,
                Alerts = [new("Disk is 91% full", "firing", "warning", null), new("Memory", "firing", "critical", null)],
            };

            Assert.Equal(new TrayStatus(TrayGlyphState.Ok, "LabTether Agent\nConnected"),
                TrayStatus.Resolve(true, new AgentStatus { IsConnected = true }, null, null, now));
            Assert.Equal(new TrayStatus(TrayGlyphState.Warning, "LabTether Agent\nDisk is 91% full"),
                TrayStatus.Resolve(true, disk, null, null, now));
            Assert.Equal(new TrayStatus(TrayGlyphState.Critical, "LabTether Agent\n2 alerts"),
                TrayStatus.Resolve(true, two, null, null, now));
            Assert.Equal(new TrayStatus(TrayGlyphState.Offline, "LabTether Agent\nOffline since 21:12"),
                TrayStatus.Resolve(false, new AgentStatus { DisconnectedAt = at }, null, null, now));
            Assert.Equal(new TrayStatus(TrayGlyphState.Offline, "LabTether Agent\nOffline since 21:12"),
                TrayStatus.Resolve(false, null, at, null, now));
            Assert.Equal(new TrayStatus(TrayGlyphState.Critical, "LabTether Agent\nAgent stopped at 21:12"),
                TrayStatus.Resolve(true, disk, null, at, now));
            Assert.DoesNotContain("\u2014", TrayStatus.Resolve(false, null, null, null, now).Tooltip);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    private static System.Drawing.Bitmap Render(TrayGlyphState state, int size)
    {
        using var icon = TrayIconManager.CreateTrayIcon(state, Palette, size);
        return icon.ToBitmap();
    }

    private static bool IsWarning(System.Drawing.Color p) => p.A > 200 && p.R > 150 && p.G > 90 && p.G < 160 && p.B < 60;

    private static bool IsCritical(System.Drawing.Color p) => p.A > 200 && p.R > 180 && p.G < 80 && p.B < 80;

    // Counts matches inside a box given in the 16-unit drawing's coordinates.
    private static int CountIn(System.Drawing.Bitmap bitmap, int size, int left, int top, int right, int bottom,
        Func<System.Drawing.Color, bool> match)
    {
        var scale = size / 16.0;
        var count = 0;
        for (var y = (int)(top * scale); y < (int)(bottom * scale); y++)
            for (var x = (int)(left * scale); x < (int)(right * scale); x++)
                if (match(bitmap.GetPixel(x, y)))
                    count++;
        return count;
    }

    private static int Count(System.Drawing.Bitmap bitmap, Func<System.Drawing.Color, bool> match)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (match(bitmap.GetPixel(x, y)))
                    count++;
        return count;
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
