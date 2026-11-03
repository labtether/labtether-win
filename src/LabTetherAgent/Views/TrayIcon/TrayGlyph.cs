using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LabTetherAgent.Presentation;

namespace LabTetherAgent.Views.TrayIcon;

/// <summary>Glyph ink for one taskbar theme: text, muted text, warning and critical.</summary>
internal readonly record struct TrayPalette(Color Ink, Color Ink2, Color Warning, Color Critical);

/// <summary>
/// Draws the monochrome mascot tray glyph with real transparency: the face and
/// the gap around the status dot are cut out, so the taskbar shows through.
/// Sizes below 32 px use the 16-unit drawing; 32 px and up use the 32-unit one.
/// </summary>
internal static class TrayGlyph
{
    public static Bitmap Render(TrayGlyphState state, TrayPalette palette, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var large = size >= 32;
        var unit = large ? 32f : 16f;
        graphics.ScaleTransform(size / unit, size / unit);

        // Offline dims the glyph to half strength and adds the ring.
        var ink = state == TrayGlyphState.Offline ? Color.FromArgb(128, palette.Ink) : palette.Ink;
        if (large) DrawLarge(graphics, ink); else DrawSmall(graphics, ink);

        var dot = large ? (Center: 25f, Gap: 7.5f, Fill: 5.5f, Ring: 4.4f, RingWidth: 2.4f)
                        : (Center: 12.5f, Gap: 4.25f, Fill: 3f, Ring: 2.4f, RingWidth: 1.4f);
        if (state == TrayGlyphState.Ok)
            return bitmap;

        KnockCircle(graphics, dot.Center, dot.Gap);
        if (state == TrayGlyphState.Offline)
        {
            using var ring = new Pen(palette.Ink2, dot.RingWidth);
            graphics.DrawEllipse(ring, dot.Center - dot.Ring, dot.Center - dot.Ring, dot.Ring * 2, dot.Ring * 2);
        }
        else
        {
            using var fill = new SolidBrush(state == TrayGlyphState.Critical ? palette.Critical : palette.Warning);
            graphics.FillEllipse(fill, dot.Center - dot.Fill, dot.Center - dot.Fill, dot.Fill * 2, dot.Fill * 2);
        }

        return bitmap;
    }

    /// <summary>Converts a rendered glyph to a native icon the shell can show.</summary>
    public static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawSmall(Graphics graphics, Color ink)
    {
        using var pen = RoundPen(ink, 1f);
        using var brush = new SolidBrush(ink);
        using (var frame = RoundedRect(1.5f, 1.5f, 13f, 13f, 3f)) graphics.DrawPath(pen, frame);
        using (var screen = RoundedRect(4f, 4f, 8f, 6f, 1.25f)) graphics.FillPath(brush, screen);
        KnockCurves(graphics, 1f, [5.5f, 7.6f, 6.25f, 6.5f, 7f, 7.6f], [9f, 7.6f, 9.75f, 6.5f, 10.5f, 7.6f]);
        graphics.DrawLine(pen, 4.25f, 12f, 7f, 12f);
        graphics.FillEllipse(brush, 10.6f, 11.1f, 1.8f, 1.8f);
    }

    private static void DrawLarge(Graphics graphics, Color ink)
    {
        using var pen = RoundPen(ink, 2f);
        using var brush = new SolidBrush(ink);
        using (var frame = RoundedRect(3f, 3f, 26f, 26f, 6f)) graphics.DrawPath(pen, frame);
        using (var screen = RoundedRect(7f, 7f, 18f, 12f, 2.5f)) graphics.FillPath(brush, screen);
        KnockCurves(graphics, 1.6f, [10.6f, 13f, 12.3f, 10.6f, 14f, 13f], [18f, 13f, 19.7f, 10.6f, 21.4f, 13f]);
        KnockCurves(graphics, 1.4f, [13.8f, 15.9f, 16f, 17.7f, 18.2f, 15.9f]);
        graphics.DrawLine(pen, 8.5f, 24f, 14f, 24f);
        graphics.FillEllipse(brush, 20.75f, 22.25f, 3.5f, 3.5f);
    }

    private static Pen RoundPen(Color color, float width) => new(color, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };

    // Each curve is a quadratic Bezier: start x,y, control x,y, end x,y.
    private static void KnockCurves(Graphics graphics, float width, params float[][] curves)
    {
        var previous = graphics.CompositingMode;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var pen = RoundPen(Color.Transparent, width);
        foreach (var c in curves)
        {
            PointF start = new(c[0], c[1]), control = new(c[2], c[3]), end = new(c[4], c[5]);
            var c1 = new PointF(start.X + 2f / 3f * (control.X - start.X), start.Y + 2f / 3f * (control.Y - start.Y));
            var c2 = new PointF(end.X + 2f / 3f * (control.X - end.X), end.Y + 2f / 3f * (control.Y - end.Y));
            graphics.DrawBezier(pen, start, c1, c2, end);
        }
        graphics.CompositingMode = previous;
    }

    private static void KnockCircle(Graphics graphics, float center, float radius)
    {
        var previous = graphics.CompositingMode;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(Color.Transparent);
        graphics.FillEllipse(clear, center - radius, center - radius, radius * 2, radius * 2);
        graphics.CompositingMode = previous;
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
