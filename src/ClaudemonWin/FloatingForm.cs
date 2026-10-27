using ClaudemonWin.Core;

namespace ClaudemonWin;

/// <summary>
/// Always-on-top desktop window. Full view: session ring + both weekly bars +
/// reset footer. Compact view: just the session ring. Double-click toggles,
/// drag anywhere to move, right-click for options.
/// </summary>
internal sealed class FloatingForm : Form
{
    private static readonly Size FullLogical = new(300, 132);
    private static readonly Size CompactLogical = new(84, 84);

    private readonly AppSettings _settings;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _compactItem = new(Strings.MenuCompact);
    private UsageReport? _report;
    private bool _stale;
    private bool _error;
    private Palette _palette = Theme.Current;

    private Point _dragStart;
    private Point _formStart;
    private bool _dragging;

    public event EventHandler? RefreshRequested;
    public event EventHandler? HideRequested;

    public FloatingForm(AppSettings settings)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Opacity = 0.97;
        Text = "Claudemon";

        _compactItem.Click += (_, _) => ToggleCompact();
        _menu.Items.Add(_compactItem);
        _menu.Items.Add(Strings.MenuRefresh, null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.MenuHide, null, (_, _) => HideRequested?.Invoke(this, EventArgs.Empty));
        ContextMenuStrip = _menu;

        ApplySize();
        PlaceInitially();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.TryRoundCorners(Handle);
        ApplySize(); // DeviceDpi is only reliable once the handle exists on its monitor
    }

    /// <summary>Alt+F4 hides the floating window through the normal setting instead of disposing it.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnFormClosing(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplySize();
    }

    public void UpdateFrom(UsageController controller)
    {
        _report = controller.Machine.LastGood;
        _stale = controller.Machine.IsDataOld(DateTimeOffset.Now);
        _error = controller.Machine.Status is UsageStatus.Error or UsageStatus.StaleError;
        _palette = Theme.Current;
        _compactItem.Checked = _settings.FloatingCompact;
        Invalidate();
    }

    private float S => DeviceDpi / 96f;
    private int Px(float v) => (int)MathF.Round(v * S);

    private void ApplySize()
    {
        var logical = _settings.FloatingCompact ? CompactLogical : FullLogical;
        ClientSize = new Size(Px(logical.Width), Px(logical.Height));
        KeepOnScreen();
        Invalidate();
    }

    private void ToggleCompact()
    {
        // Keep the top-right corner fixed so the window doesn't jump away from where it sits.
        int right = Right;
        _settings.FloatingCompact = !_settings.FloatingCompact;
        _compactItem.Checked = _settings.FloatingCompact;
        ApplySize();
        Left = right - Width;
        KeepOnScreen();
        SavePosition();
    }

    private void PlaceInitially()
    {
        if (_settings.FloatingX is int x && _settings.FloatingY is int y)
        {
            Location = new Point(x, y);
        }
        else
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(wa.Right - Width - Px(24), wa.Top + Px(24));
        }
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        var wa = Screen.FromRectangle(Bounds).WorkingArea;
        int x = Math.Clamp(Left, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        int y = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        Location = new Point(x, y);
    }

    private void SavePosition()
    {
        _settings.FloatingX = Left;
        _settings.FloatingY = Top;
        _settings.Save();
    }

    // ------------------------------------------------------------- mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = Cursor.Position;
        _formStart = Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = Cursor.Position;
        Location = new Point(_formStart.X + p.X - _dragStart.X, _formStart.Y + p.Y - _dragStart.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        KeepOnScreen();
        SavePosition();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) ToggleCompact();
    }

    // ------------------------------------------------------------- painting

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(_palette.Background);
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.HighQuality(g);
        var session = _report?.Session;

        if (_settings.FloatingCompact)
        {
            DrawSessionRing(g, new Rectangle(Px(10), Px(10), Width - Px(20), Height - Px(20)), session, compact: true);
            return;
        }

        // Left: session ring.
        var ringRect = new Rectangle(Px(14), Px(14), Px(84), Px(84));
        DrawSessionRing(g, ringRect, session, compact: false);
        Theme.DrawText(g, session is null ? "" : Strings.ShortLabel(session), Theme.Ui(11.5f * S), _palette.TextMuted,
            new Rectangle(ringRect.X, ringRect.Bottom + Px(4), ringRect.Width, Px(18)), TextFormatFlags.HorizontalCenter);

        // Right: weekly bars.
        int left = ringRect.Right + Px(18);
        int right = Width - Px(16);
        int y = Px(18);
        foreach (var m in new[] { _report?.WeekAll, _report?.WeekModel })
        {
            if (m is null) continue;
            var pct = $"{m.Percent}%";
            var pctFont = Theme.UiSemibold(13 * S);
            int pctW = TextRenderer.MeasureText(g, pct, pctFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            Theme.DrawText(g, Strings.ShortLabel(m), Theme.Ui(12 * S), _palette.Text,
                new Rectangle(left, y, right - left - pctW - Px(6), Px(18)));
            Theme.DrawText(g, pct, pctFont, _palette.ForPercent(m.Percent),
                new Rectangle(right - pctW, y, pctW, Px(18)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.DrawBar(g, new RectangleF(left, y + Px(22), right - left, Px(6)), m.Percent,
                _palette.ForPercent(m.Percent), _palette.Track, _settings.PreciseBars);
            y += Px(40);
        }

        // Footer: session countdown + freshness.
        var footer = session?.ResetDate is { } reset
            ? Strings.ResetsInShort(Strings.Countdown(UsageFormatting.Countdown(reset, DateTimeOffset.Now)))
            : (_report is null ? Strings.Waiting : "");
        if (_stale) footer += Strings.Stale;
        Theme.DrawText(g, footer, Theme.Ui(11 * S), _error ? _palette.Critical : _palette.TextMuted,
            new Rectangle(left, Height - Px(30), right - left, Px(18)));
    }

    private void DrawSessionRing(Graphics g, Rectangle r, UsageMetric? session, bool compact)
    {
        int pct = session?.Percent ?? 0;
        var color = session is null ? _palette.Track : _palette.ForPercent(pct);
        Theme.DrawRing(g, r, Px(compact ? 7 : 9), pct, color, _palette.Track);

        var text = session is null ? "–" : $"{pct}%";
        Theme.DrawText(g, text, Theme.UiSemibold((compact ? 16 : 20) * S), _palette.Text, r,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (_error)
        {
            using var dot = new SolidBrush(_palette.Critical);
            int d = Px(8);
            g.FillEllipse(dot, r.Right - d, r.Top, d, d);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}
