using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ClaudemonWin;

/// <summary>What the tray icon should show. Equal snapshots render identical icons.</summary>
internal readonly record struct TrayVisual(
    TrayStyle Style,
    int? SessionPercent,
    int? WeekPercent,
    bool HasError,
    bool IsStale,
    int Size,
    bool TaskbarLight);

/// <summary>
/// Draws the notification-area icon at the exact small-icon size for the current
/// DPI (16/20/24/32 px). Windows tray icons cannot show text beside them, so the
/// number lives inside the icon (Percent style) or in the tooltip.
/// </summary>
internal static class TrayIconRenderer
{
    public static Icon Render(TrayVisual v)
    {
        int size = Math.Max(16, v.Size);
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var palette = v.TaskbarLight ? Palette.Light : Palette.Dark;
            var glyph = v.TaskbarLight ? Color.FromArgb(0x1B, 0x1B, 0x1B) : Color.FromArgb(0xF3, 0xF3, 0xF3);
            var track = v.TaskbarLight ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(90, 255, 255, 255);
            Color Fill(int pct) => v.IsStale ? Color.FromArgb(160, palette.ForPercent(pct)) : palette.ForPercent(pct);

            switch (v.Style)
            {
                case TrayStyle.Percent:
                    DrawPercent(g, size, v.SessionPercent, glyph, track, Fill);
                    break;
                case TrayStyle.Bars:
                    DrawBars(g, size, v.SessionPercent, v.WeekPercent, track, Fill);
                    break;
                default:
                    DrawRingIcon(g, size, v.SessionPercent, track, Fill);
                    break;
            }

            if (v.HasError)
            {
                float d = size * 0.38f;
                using var badge = new SolidBrush(palette.Critical);
                using var outline = new Pen(v.TaskbarLight ? Color.White : Color.Black, Math.Max(1f, size / 16f));
                var r = new RectangleF(size - d - 0.5f, size - d - 0.5f, d, d);
                g.FillEllipse(badge, r);
                g.DrawEllipse(outline, r);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static void DrawRingIcon(Graphics g, int size, int? pct, Color track, Func<int, Color> fill)
    {
        float thickness = Math.Max(2.5f, size * 0.2f);
        var bounds = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
        Theme.DrawRing(g, bounds, thickness, pct ?? 0, pct is { } p ? fill(p) : track, track);

        if (pct is null)
        {
            // No data yet: small dash in the middle.
            using var b = new SolidBrush(track);
            float w = size * 0.36f, h = Math.Max(1.5f, size * 0.1f);
            g.FillRectangle(b, (size - w) / 2, (size - h) / 2, w, h);
        }
    }

    private static void DrawPercent(Graphics g, int size, int? pct, Color glyph, Color track, Func<int, Color> fill)
    {
        string text = pct is { } p ? (p >= 100 ? "!!" : p.ToString()) : "–";
        float barHeight = Math.Max(2f, MathF.Round(size * 0.14f));

        // Fit the number into the area above the bar.
        var textArea = new RectangleF(0, -size * 0.04f, size, size - barHeight - 1);
        float em = size * (text.Length >= 2 ? 0.78f : 0.9f);
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        path.AddString(text, family, (int)FontStyle.Bold, em, textArea, sf);

        // Shrink if the glyphs overflow the icon width.
        var bounds = path.GetBounds();
        if (bounds.Width > size - 1 && bounds.Width > 0)
        {
            float scale = (size - 1) / bounds.Width;
            using var m = new Matrix();
            m.Translate(size / 2f, textArea.Height / 2f);
            m.Scale(scale, scale);
            m.Translate(-size / 2f, -textArea.Height / 2f);
            path.Transform(m);
        }

        using (var brush = new SolidBrush(pct is { } p2 && p2 >= 86 ? fill(p2) : glyph))
            g.FillPath(brush, path);

        var barRect = new RectangleF(1, size - barHeight - 0.5f, size - 2, barHeight);
        Theme.DrawBar(g, barRect, pct ?? 0, pct is { } p3 ? fill(p3) : track, track, precise: true);
    }

    private static void DrawBars(Graphics g, int size, int? session, int? week, Color track, Func<int, Color> fill)
    {
        float gap = Math.Max(2f, size * 0.12f);
        float width = (size - gap - 2) / 2f;
        DrawVertical(new RectangleF(1, 1, width, size - 2), session);
        DrawVertical(new RectangleF(1 + width + gap, 1, width, size - 2), week);

        void DrawVertical(RectangleF r, int? pct)
        {
            float radius = Math.Min(r.Width / 2, 2.5f);
            using (var trackPath = Theme.RoundedRect(r, radius))
            using (var trackBrush = new SolidBrush(track))
                g.FillPath(trackBrush, trackPath);
            if (pct is not { } p || p <= 0) return;

            float h = Math.Max(r.Width * 0.6f, r.Height * Math.Clamp(p, 0, 100) / 100f);
            var state = g.Save();
            using (var clip = Theme.RoundedRect(r, radius)) g.SetClip(clip);
            using (var brush = new SolidBrush(fill(p)))
                g.FillRectangle(brush, r.X, r.Bottom - h, r.Width, h);
            g.Restore(state);
        }
    }
}
