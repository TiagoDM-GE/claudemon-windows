using ClaudemonWin.Core;

namespace ClaudemonWin;

/// <summary>
/// Base for the panel's custom-drawn rows. Everything is laid out in 96-DPI
/// "logical" pixels and scaled with <see cref="Px"/> so the panel is crisp at any DPI.
/// </summary>
internal abstract class SurfaceControl : Control
{
    private Palette _palette = Palette.Dark;
    protected bool Hovering { get; private set; }
    protected Point MousePos { get; private set; }

    protected SurfaceControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Margin = Padding.Empty;
    }

    /// <summary>Whether the parent layout should show this row (independent of the form's own visibility).</summary>
    public bool Wanted { get; set; } = true;

    public Palette Palette
    {
        get => _palette;
        set { _palette = value; Invalidate(); }
    }

    protected float S => DeviceDpi / 96f;
    protected int Px(float logical) => (int)MathF.Round(logical * S);

    /// <summary>Height in logical pixels for the given logical width.</summary>
    public abstract int MeasureHeight(int logicalWidth);

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Palette.Background);

    protected override void OnMouseEnter(EventArgs e) { Hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovering = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        MousePos = e.Location;
        Cursor = IsClickable(e.Location) ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnMouseMove(e);
    }

    protected virtual bool IsClickable(Point p) => false;
}

/// <summary>Panel header: title + refresh and settings buttons.</summary>
internal sealed class HeaderView : SurfaceControl
{
    public event EventHandler? RefreshClicked;
    public event EventHandler? SettingsClicked;

    private bool _refreshing;
    public bool Refreshing { get => _refreshing; set { _refreshing = value; Invalidate(); } }
    private bool _settingsOpen;
    public bool SettingsOpen { get => _settingsOpen; set { _settingsOpen = value; Invalidate(); } }

    public override int MeasureHeight(int logicalWidth) => 48;

    private Rectangle SettingsRect => new(Width - Px(16 + 32), Px(8), Px(32), Px(32));
    private Rectangle RefreshRect => new(Width - Px(16 + 32 + 4 + 32), Px(8), Px(32), Px(32));

    protected override bool IsClickable(Point p) => SettingsRect.Contains(p) || RefreshRect.Contains(p);

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (RefreshRect.Contains(e.Location)) RefreshClicked?.Invoke(this, EventArgs.Empty);
        else if (SettingsRect.Contains(e.Location)) SettingsClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);

        // Brand dot + title.
        using (var dot = new SolidBrush(Palette.Accent))
            g.FillEllipse(dot, Px(16), Px(19), Px(10), Px(10));
        Theme.DrawText(g, Strings.AppTitle, Theme.UiSemibold(15 * S), Palette.Text,
            new Rectangle(Px(34), 0, RefreshRect.Left - Px(40), Height));

        DrawIconButton(g, RefreshRect, Theme.GlyphRefresh, Refreshing ? Palette.TextMuted : Palette.Text, false);
        DrawIconButton(g, SettingsRect, Theme.GlyphSettings, Palette.Text, SettingsOpen);
    }

    private void DrawIconButton(Graphics g, Rectangle r, string glyph, Color color, bool active)
    {
        bool hover = Hovering && r.Contains(MousePos);
        if (hover || active)
        {
            using var path = Theme.RoundedRect(r, Px(6));
            using var b = new SolidBrush(Palette.Hover);
            g.FillPath(b, path);
        }
        Theme.DrawText(g, glyph, Theme.Icons(14 * S), color, r,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Onboarding / error card with an optional action link.</summary>
internal sealed class MessageView : SurfaceControl
{
    public event EventHandler? LinkClicked;

    private string _title = "";
    public string Title { get => _title; set { _title = value; Invalidate(); } }
    private string _body = "";
    public string Body { get => _body; set { _body = value; Invalidate(); } }
    private string? _link;
    public string? Link { get => _link; set { _link = value; Invalidate(); } }
    private bool _isWarning;
    public bool IsWarning { get => _isWarning; set { _isWarning = value; Invalidate(); } }

    private const int Pad = 12;
    private Rectangle _linkRect;

    public override int MeasureHeight(int logicalWidth)
    {
        int textWidth = logicalWidth - 32 - 2 * Pad - 28;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        // Measure at 96 DPI with logical-size fonts.
        int title = TextRenderer.MeasureText(Title, Theme.UiSemibold(13), new Size(textWidth, int.MaxValue), flags).Height;
        int body = string.IsNullOrEmpty(Body) ? 0
            : TextRenderer.MeasureText(Body, Theme.Ui(12), new Size(textWidth, int.MaxValue), flags).Height + 4;
        int link = Link is null ? 0 : 22;
        return 8 + Pad + title + body + link + Pad + 4;
    }

    protected override bool IsClickable(Point p) => Link is not null && _linkRect.Contains(p);

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && IsClickable(e.Location)) LinkClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var card = new Rectangle(Px(16), Px(4), Width - Px(32), Height - Px(8));
        var tint = IsWarning ? Palette.Critical : Palette.Accent;

        using (var path = Theme.RoundedRect(card, Px(8)))
        {
            using var fill = new SolidBrush(Color.FromArgb(Palette.IsDark ? 38 : 22, tint));
            using var border = new Pen(Color.FromArgb(90, tint), Math.Max(1, S));
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        int x = card.X + Px(Pad);
        int y = card.Y + Px(Pad);
        Theme.DrawText(g, IsWarning ? Theme.GlyphWarning : Theme.GlyphInfo, Theme.Icons(14 * S), tint,
            new Rectangle(x, y, Px(20), Px(18)), TextFormatFlags.Left | TextFormatFlags.Top);

        int textX = x + Px(28);
        int textW = card.Right - Px(Pad) - textX;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Top;

        var titleFont = Theme.UiSemibold(13 * S);
        int titleH = TextRenderer.MeasureText(g, Title, titleFont, new Size(textW, int.MaxValue), flags).Height;
        TextRenderer.DrawText(g, Title, titleFont, new Rectangle(textX, y, textW, titleH), Palette.Text, flags);
        y += titleH;

        if (!string.IsNullOrEmpty(Body))
        {
            y += Px(4);
            var bodyFont = Theme.Ui(12 * S);
            int bodyH = TextRenderer.MeasureText(g, Body, bodyFont, new Size(textW, int.MaxValue), flags).Height;
            TextRenderer.DrawText(g, Body, bodyFont, new Rectangle(textX, y, textW, bodyH), Palette.TextMuted, flags);
            y += bodyH;
        }

        if (Link is not null)
        {
            var linkFont = Theme.Ui(12 * S, IsClickable(MousePos) && Hovering ? FontStyle.Underline : FontStyle.Regular);
            var size = TextRenderer.MeasureText(g, Link, linkFont, Size.Empty, TextFormatFlags.NoPadding);
            _linkRect = new Rectangle(textX, y + Px(6), size.Width, size.Height);
            TextRenderer.DrawText(g, Link, linkFont, _linkRect, Palette.Accent, TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>One limit: label, percent, bar and "resets in" line.</summary>
internal sealed class MetricView : SurfaceControl
{
    private UsageMetric? _metric;
    public UsageMetric? Metric { get => _metric; set { _metric = value; Invalidate(); } }
    private bool _precise;
    public bool Precise { get => _precise; set { _precise = value; Invalidate(); } }
    private bool _dimmed;
    public bool Dimmed { get => _dimmed; set { _dimmed = value; Invalidate(); } }

    public override int MeasureHeight(int logicalWidth) => 66;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Metric is not { } m) return;
        var g = e.Graphics;
        Theme.HighQuality(g);

        int left = Px(16), right = Width - Px(16);
        var color = Palette.ForPercent(m.Percent);
        var text = Dimmed ? Palette.TextMuted : Palette.Text;

        var pctText = $"{m.Percent}%";
        var pctFont = Theme.UiSemibold(15 * S);
        int pctW = TextRenderer.MeasureText(g, pctText, pctFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        Theme.DrawText(g, pctText, pctFont, Dimmed ? Palette.TextMuted : color,
            new Rectangle(right - pctW, Px(6), pctW, Px(22)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        Theme.DrawText(g, Strings.MetricLabel(m), Theme.Ui(13 * S), text,
            new Rectangle(left, Px(6), right - left - pctW - Px(8), Px(22)));

        var bar = new RectangleF(left, Px(32), right - left, Px(8));
        Theme.DrawBar(g, bar, m.Percent, Dimmed ? Palette.TextMuted : color, Palette.Track, Precise);

        var countdown = UsageFormatting.Countdown(m.ResetDate, DateTimeOffset.Now);
        if (countdown is not null)
        {
            var line = Strings.ResetsIn(Strings.Countdown(countdown), UsageFormatting.AbsoluteReset(m.ResetDate, m.TimeZoneId));
            Theme.DrawText(g, line, Theme.Ui(11.5f * S), Palette.TextMuted,
                new Rectangle(left, Px(44), right - left, Px(18)));
        }
    }
}

/// <summary>Small section caption.</summary>
internal sealed class SectionView : SurfaceControl
{
    private string _caption = "";
    public string Caption { get => _caption; set { _caption = value; Invalidate(); } }

    public override int MeasureHeight(int logicalWidth) => 34;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var pen = new Pen(Palette.Border, Math.Max(1, S)))
            g.DrawLine(pen, Px(16), Px(6), Width - Px(16), Px(6));
        Theme.DrawText(g, Caption.ToUpperInvariant(), Theme.UiSemibold(11 * S), Palette.TextMuted,
            new Rectangle(Px(16), Px(12), Width - Px(32), Px(20)));
    }
}

/// <summary>Label + Windows 11 style toggle switch.</summary>
internal sealed class ToggleRow : SurfaceControl
{
    public event EventHandler? CheckedChanged;

    private string _caption = "";
    public string Caption { get => _caption; set { _caption = value; Invalidate(); } }
    public bool Indented { get; set; }
    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Set without raising <see cref="CheckedChanged"/> (for syncing from settings).</summary>
    public void SetSilently(bool value)
    {
        var handler = CheckedChanged;
        CheckedChanged = null;
        Checked = value;
        CheckedChanged = handler;
    }

    public override int MeasureHeight(int logicalWidth) => 34;

    protected override bool IsClickable(Point p) => true;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        if (Hovering)
        {
            using var hover = new SolidBrush(Palette.Hover);
            using var path = Theme.RoundedRect(new Rectangle(Px(8), Px(1), Width - Px(16), Height - Px(2)), Px(6));
            g.FillPath(hover, path);
        }

        int left = Px(Indented ? 32 : 16);
        var sw = new RectangleF(Width - Px(16 + 40), (Height - Px(20)) / 2f, Px(40), Px(20));
        Theme.DrawText(g, Caption, Theme.Ui(13 * S), Indented ? Palette.TextMuted : Palette.Text,
            new Rectangle(left, 0, (int)sw.Left - left - Px(8), Height));

        using var track = Theme.RoundedRect(sw, sw.Height / 2);
        if (Checked)
        {
            using var on = new SolidBrush(Palette.Accent);
            g.FillPath(on, track);
        }
        else
        {
            using var off = new Pen(Palette.TextMuted, Math.Max(1, S));
            g.DrawPath(off, track);
        }

        float knob = Px(Checked ? 12 : 10);
        float kx = Checked ? sw.Right - Px(5) - knob : sw.Left + Px(5);
        using var knobBrush = new SolidBrush(Checked ? Color.White : Palette.TextMuted);
        g.FillEllipse(knobBrush, kx, sw.Top + (sw.Height - knob) / 2, knob, knob);
    }
}

/// <summary>Label + segmented choice (e.g. tray icon style).</summary>
internal sealed class ChoiceRow : SurfaceControl
{
    public event EventHandler? SelectedChanged;

    private string _caption = "";
    public string Caption { get => _caption; set { _caption = value; Invalidate(); } }
    private string[] _options = [];
    public string[] Options { get => _options; set { _options = value; Invalidate(); } }
    public bool Indented { get; set; }
    private int _selectedIndex;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSilently(int index)
    {
        var handler = SelectedChanged;
        SelectedChanged = null;
        SelectedIndex = index;
        SelectedChanged = handler;
    }

    public override int MeasureHeight(int logicalWidth) => 38;

    private Rectangle[] SegmentRects(Graphics? g = null)
    {
        var font = Theme.Ui(12 * S);
        var widths = Options.Select(o => TextRenderer.MeasureText(o, font, Size.Empty, TextFormatFlags.NoPadding).Width + Px(20)).ToArray();
        int x = Width - Px(16) - widths.Sum();
        var rects = new Rectangle[Options.Length];
        for (int i = 0; i < Options.Length; i++)
        {
            rects[i] = new Rectangle(x, (Height - Px(26)) / 2, widths[i], Px(26));
            x += widths[i];
        }
        return rects;
    }

    protected override bool IsClickable(Point p) => SegmentRects().Any(r => r.Contains(p));

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var rects = SegmentRects();
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].Contains(e.Location)) SelectedIndex = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var rects = SegmentRects(g);
        if (rects.Length == 0) return;

        int left = Px(Indented ? 32 : 16);
        Theme.DrawText(g, Caption, Theme.Ui(13 * S), Indented ? Palette.TextMuted : Palette.Text,
            new Rectangle(left, 0, rects[0].Left - left - Px(8), Height));

        var all = Rectangle.Union(rects[0], rects[^1]);
        using (var outline = Theme.RoundedRect(all, Px(6)))
        using (var pen = new Pen(Palette.Border, Math.Max(1, S)))
        using (var bg = new SolidBrush(Palette.Surface))
        {
            g.FillPath(bg, outline);
            g.DrawPath(pen, outline);
        }

        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            bool selected = i == SelectedIndex;
            if (selected)
            {
                var inner = Rectangle.Inflate(r, -Px(2), -Px(2));
                using var path = Theme.RoundedRect(inner, Px(5));
                using var fill = new SolidBrush(Palette.Accent);
                g.FillPath(fill, path);
            }
            else if (Hovering && r.Contains(MousePos))
            {
                var inner = Rectangle.Inflate(r, -Px(2), -Px(2));
                using var path = Theme.RoundedRect(inner, Px(5));
                using var fill = new SolidBrush(Palette.Hover);
                g.FillPath(fill, path);
            }
            Theme.DrawText(g, Options[i], Theme.Ui(12 * S), selected ? Color.White : Palette.Text, r,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>"updated 14:32" + quit link.</summary>
internal sealed class FooterView : SurfaceControl
{
    public event EventHandler? QuitClicked;

    private string _status = "";
    public string Status { get => _status; set { _status = value; Invalidate(); } }

    public override int MeasureHeight(int logicalWidth) => 40;

    private Rectangle QuitRect
    {
        get
        {
            var size = TextRenderer.MeasureText(Strings.MenuQuit, Theme.Ui(12 * S), Size.Empty, TextFormatFlags.NoPadding);
            return new Rectangle(Width - Px(16) - size.Width - Px(12), Px(8), size.Width + Px(12), Px(26));
        }
    }

    protected override bool IsClickable(Point p) => QuitRect.Contains(p);

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && QuitRect.Contains(e.Location)) QuitClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        using (var pen = new Pen(Palette.Border, Math.Max(1, S)))
            g.DrawLine(pen, 0, 0, Width, 0);

        var quit = QuitRect;
        Theme.DrawText(g, Status, Theme.Ui(11.5f * S), Palette.TextMuted,
            new Rectangle(Px(16), 0, quit.Left - Px(24), Height));

        if (Hovering && quit.Contains(MousePos))
        {
            using var path = Theme.RoundedRect(quit, Px(6));
            using var b = new SolidBrush(Palette.Hover);
            g.FillPath(b, path);
        }
        Theme.DrawText(g, Strings.MenuQuit, Theme.Ui(12 * S), Palette.Text, quit,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
