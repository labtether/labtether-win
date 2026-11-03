#if DEBUG
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LabTetherAgent.Presentation;
using LabTetherAgent.Views.TrayIcon;

namespace LabTetherAgent.Views.Snapshot;

/// <summary>
/// Debug builds only: the tray glyph in every state at 16, 24 and 32 px on a
/// light and a dark taskbar, at 1x and enlarged 4x, for design review.
/// </summary>
internal static class TraySnapshot
{
    // Approximate Windows 11 taskbar colours (not app tokens), as in the artboard.
    private static readonly Color LightTaskbar = Color.FromArgb(238, 238, 241);
    private static readonly Color DarkTaskbar = Color.FromArgb(32, 32, 36);
    private static readonly TrayGlyphState[] States =
        [TrayGlyphState.Ok, TrayGlyphState.Warning, TrayGlyphState.Critical, TrayGlyphState.Offline];
    private static readonly int[] Sizes = [16, 24, 32];

    public static void Write(string directory, List<string> log)
    {
        foreach (var light in new[] { true, false })
        {
            var name = light ? "win-tray-light" : "win-tray-dark";
            using var strip = Strip(light);
            strip.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
            using var large = new Bitmap(strip.Width * 4, strip.Height * 4, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(large))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(strip, 0, 0, large.Width, large.Height);
            }
            large.Save(Path.Combine(directory, name + "-4x.png"), ImageFormat.Png);
            log.Add($"{name}: {strip.Width}x{strip.Height} (ok, warning, critical, offline at 16, 24, 32 px)");
        }
    }

    private static Bitmap Strip(bool light)
    {
        const int cell = 40;
        var palette = TaskbarEnvironment.Palette(light);
        var strip = new Bitmap(cell * States.Length * Sizes.Length, cell, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(strip);
        graphics.Clear(light ? LightTaskbar : DarkTaskbar);
        var x = 0;
        foreach (var state in States)
        {
            foreach (var size in Sizes)
            {
                using var glyph = TrayGlyph.Render(state, palette, size);
                graphics.DrawImage(glyph, x + (cell - size) / 2, (cell - size) / 2, size, size);
                x += cell;
            }
        }
        return strip;
    }
}
#endif
