using System.Drawing.Drawing2D;
using ClaudemonWin.Core;
using Microsoft.Win32;

namespace ClaudemonWin;

/// <summary>Colours for one theme. Follows the Windows "app mode" (light/dark).</summary>
internal sealed record Palette(
    bool IsDark,
    Color Background,
    Color Surface,
    Color Border,
    Color Text,
    Color TextMuted,
    Color Track,
    Color Accent,
    Color Ok,
    Color Warning,
    Color Critical,
    Color Hover)
{
    public static readonly Palette Dark = new(
        IsDark: true,
        Background: Color.FromArgb(0x20, 0x20, 0x20),
        Surface: Color.FromArgb(0x2B, 0x2B, 0x2B),
        Border: Color.FromArgb(0x3A, 0x3A, 0x3A),
        Text: Color.FromArgb(0xF3, 0xF3, 0xF3),
        TextMuted: Color.FromArgb(0xA8, 0xA8, 0xA8),
        Track: Color.FromArgb(0x3D, 0x3D, 0x3D),
        Accent: Color.FromArgb(0xD9, 0x77, 0x57),
        Ok: Color.FromArgb(0x4C, 0xC2, 0x6A),
        Warning: Color.FromArgb(0xE3, 0xB3, 0x41),
        Critical: Color.FromArgb(0xF2, 0x5F, 0x5C),
        Hover: Color.FromArgb(0x38, 0x38, 0x38));

    public static readonly Palette Light = new(
        IsDark: false,
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Surface: Color.White,
        Border: Color.FromArgb(0xE0, 0xE0, 0xE0),
        Text: Color.FromArgb(0x1B, 0x1B, 0x1B),
        TextMuted: Color.FromArgb(0x5F, 0x5F, 0x5F),
        Track: Color.FromArgb(0xE3, 0xE3, 0xE3),
        Accent: Color.FromArgb(0xC2, 0x5E, 0x3E),
        Ok: Color.FromArgb(0x1E, 0x8E, 0x3E),
        Warning: Color.FromArgb(0xB7, 0x7B, 0x00),
        Critical: Color.FromArgb(0xD1, 0x34, 0x38),
        Hover: Color.FromArgb(0xEC, 0xEC, 0xEC));

    public Color ForPercent(int percentUsed) => UsageBands.For(percentUsed) switch
    {
        UsageBand.Ok => Ok,
        UsageBand.Warning => Warning,
        _ => Critical,
    };
}

internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>User choice from settings; <see cref="ThemeMode.System"/> follows Windows.</summary>
    public static ThemeMode Mode { get; set; } = ThemeMode.System;

    /// <summary>
    /// Palette for the panel and floating window. In System mode it follows
    /// Settings → Personalisation → Colours → "Choose your app mode".
    /// </summary>
    public static Palette Current => Mode switch
    {
        ThemeMode.Light => Palette.Light,
        ThemeMode.Dark => Palette.Dark,
        _ => ReadFlag("AppsUseLightTheme", defaultLight: true) ? Palette.Light : Palette.Dark,
    };

    /// <summary>True when the taskbar is light (Windows mode), so tray glyphs must be dark.</summary>
    public static bool TaskbarIsLight => ReadFlag("SystemUsesLightTheme", defaultLight: false);

    private static bool ReadFlag(string name, bool defaultLight)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int v ? v != 0 : defaultLight;
        }
        catch
        {
            return defaultLight;
        }
    }

    // ------------------------------------------------------------------ fonts

    private static readonly Dictionary<(string, float, FontStyle), Font> FontCache = [];

    /// <summary>Pixel-sized fonts so layout is exact at every DPI (size already scaled).</summary>
    public static Font Ui(float pixelSize, FontStyle style = FontStyle.Regular) => Get("Segoe UI", pixelSize, style);
    public static Font UiSemibold(float pixelSize) => Get("Segoe UI Semibold", pixelSize, FontStyle.Regular);
    public static Font Icons(float pixelSize) => Get(IconFontFamily, pixelSize, FontStyle.Regular);

    // Segoe Fluent Icons ships with Windows 11; MDL2 Assets with Windows 10. Same code points.
    private static readonly string IconFontFamily =
        FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    private static Font Get(string family, float px, FontStyle style)
    {
        px = MathF.Round(px * 2) / 2; // bucket to half pixels
        var key = (family, px, style);
        if (!FontCache.TryGetValue(key, out var font))
        {
            font = new Font(family, px, style, GraphicsUnit.Pixel);
            FontCache[key] = font;
        }
        return font;
    }

    public const string GlyphRefresh = "";
    public const string GlyphSettings = "";
    public const string GlyphWarning = "";
    public const string GlyphInfo = "";
    public const string GlyphClose = "";

    // ---------------------------------------------------------------- drawing

    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Horizontal usage bar. Default fill is rounded with a small minimum so low
    /// values still read as "some usage"; precise mode draws the exact width.
    /// </summary>
    public static void DrawBar(Graphics g, RectangleF track, int percent, Color fill, Color trackColor, bool precise)
    {
        float radius = track.Height / 2;
        using (var trackPath = RoundedRect(track, radius))
        using (var trackBrush = new SolidBrush(trackColor))
            g.FillPath(trackBrush, trackPath);

        if (percent <= 0) return;
        float width = track.Width * Math.Clamp(percent, 0, 100) / 100f;
        using var fillBrush = new SolidBrush(fill);

        if (precise)
        {
            var state = g.Save();
            using (var clip = RoundedRect(track, radius)) g.SetClip(clip);
            g.FillRectangle(fillBrush, track.X, track.Y, width, track.Height);
            g.Restore(state);
        }
        else
        {
            width = Math.Max(width, track.Height); // minimum visible pill
            using var fillPath = RoundedRect(new RectangleF(track.X, track.Y, width, track.Height), radius);
            g.FillPath(fillBrush, fillPath);
        }
    }

    /// <summary>Progress ring starting at 12 o'clock, clockwise.</summary>
    public static void DrawRing(Graphics g, RectangleF bounds, float thickness, int percent, Color fill, Color track)
    {
        var r = RectangleF.Inflate(bounds, -thickness / 2, -thickness / 2);
        using (var trackPen = new Pen(track, thickness))
            g.DrawEllipse(trackPen, r);

        if (percent <= 0) return;
        using var pen = new Pen(fill, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float sweep = Math.Max(4f, 360f * Math.Clamp(percent, 0, 100) / 100f);
        if (sweep >= 360f)
            g.DrawEllipse(pen, r);
        else
            g.DrawArc(pen, r, -90, sweep);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        TextRenderer.DrawText(g, text, font, bounds, color,
            flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
