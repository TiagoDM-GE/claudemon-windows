using ClaudemonWin.Core;

namespace ClaudemonWin;

/// <summary>
/// The flyout shown when the tray icon is clicked. Borderless, top-most, hides
/// when it loses focus — like the Windows 11 quick-settings flyouts.
/// </summary>
internal sealed class PanelForm : Form
{
    public const int LogicalWidth = 360;

    private readonly HeaderView _header = new();
    private readonly MessageView _message = new();
    private readonly MetricView[] _metrics = [new(), new(), new()];
    private readonly SectionView _settingsCaption = new() { Caption = Strings.Settings };
    private readonly ChoiceRow _trayStyle = new() { Caption = Strings.TrayIcon, Options = [Strings.StyleRing, Strings.StylePercent, Strings.StyleBars] };
    private readonly ChoiceRow _theme = new() { Caption = Strings.ThemeLabel, Options = [Strings.ThemeSystem, Strings.ThemeLight, Strings.ThemeDark] };
    private readonly ChoiceRow _barsSource = new() { Caption = Strings.BarsSecond, Indented = true };
    private readonly ToggleRow _floating = new() { Caption = Strings.Floating };
    private readonly ToggleRow _precise = new() { Caption = Strings.PreciseBars };
    private readonly ToggleRow _startup = new() { Caption = Strings.StartWithWindows };
    private readonly ToggleRow _alerts = new() { Caption = Strings.Alerts };
    private readonly ToggleRow _alertQuarter = new() { Caption = Strings.AlertQuarter, Indented = true };
    private readonly ToggleRow _alertLow = new() { Caption = Strings.AlertLow, Indented = true };
    private readonly ToggleRow _alertDepleted = new() { Caption = Strings.AlertDepleted, Indented = true };
    private readonly ToggleRow _alertReset = new() { Caption = Strings.AlertReset, Indented = true };
    private readonly FooterView _footer = new();

    private readonly AppSettings _settings;
    private bool _settingsOpen;
    private Palette _palette = Theme.Current;
    private Action? _messageAction;
    private bool _hasMessage = true;

    /// <summary>Time the panel last auto-hid, so a click on the tray icon that caused it doesn't reopen it.</summary>
    public DateTime LastHiddenAt { get; private set; } = DateTime.MinValue;

    public event EventHandler? RefreshRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler? SettingsChanged;

    public PanelForm(AppSettings settings)
    {
        _settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None; // we scale ourselves (see SurfaceControl.Px)
        Text = Strings.AppTitle;

        Controls.AddRange([_header, _message, .. _metrics, _settingsCaption, _theme, _trayStyle, _barsSource,
            _floating, _precise, _startup, _alerts, _alertQuarter, _alertLow, _alertDepleted, _alertReset, _footer]);

        _header.RefreshClicked += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _header.SettingsClicked += (_, _) =>
        {
            _settingsOpen = !_settingsOpen;
            Relayout();
        };
        _footer.QuitClicked += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _message.LinkClicked += (_, _) => _messageAction?.Invoke();

        WireSettings();
        ApplyPalette();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW; // keep out of Alt+Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.TryRoundCorners(Handle);
        Relayout(); // DeviceDpi is only reliable once the handle exists on its monitor
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible) HidePanel();
    }

    /// <summary>Alt+F4 must hide, never dispose — the panel is reused for the app's lifetime.</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HidePanel();
        }
        base.OnFormClosing(e);
    }

    public void HidePanel()
    {
        LastHiddenAt = DateTime.UtcNow;
        Hide();
        _settingsOpen = false; // reopen on the usage view
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) HidePanel();
        else if (e.KeyCode == Keys.F5) RefreshRequested?.Invoke(this, EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(_palette.Background);
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ----------------------------------------------------------------- update

    public void ApplyPalette()
    {
        _palette = Theme.Current;
        BackColor = _palette.Background;
        foreach (Control c in Controls)
            if (c is SurfaceControl s) s.Palette = _palette;
        Invalidate(true);
    }

    /// <summary>Re-reads the controller's state and repaints.</summary>
    public void UpdateFrom(UsageController controller)
    {
        var sm = controller.Machine;
        var now = DateTimeOffset.Now;
        _header.Refreshing = controller.IsRefreshing;

        // Metrics (keep last-good values visible even when there's an error).
        var metrics = sm.LastGood?.Metrics ?? [];
        bool dim = sm.Status == UsageStatus.StaleError;
        for (int i = 0; i < _metrics.Length; i++)
        {
            _metrics[i].Metric = i < metrics.Count ? metrics[i] : null;
            _metrics[i].Precise = _settings.PreciseBars;
            _metrics[i].Dimmed = dim;
        }

        // Second-bar picker follows the live model name.
        var model = sm.LastGood?.WeekModel?.ModelName ?? UsageKind.WeekModel.ShortName();
        _barsSource.Options = [Strings.AllModels, model];

        // Onboarding / error card.
        _messageAction = null;
        _message.Link = null;
        _hasMessage = true;
        switch (sm.Diagnostic)
        {
            case DiagnosticKind.NotInstalled:
                _message.IsWarning = false;
                _message.Title = Strings.NotInstalledTitle;
                _message.Body = Strings.NotInstalledBody;
                _message.Link = Strings.NotInstalledLink;
                _messageAction = () => OpenUrl("https://claude.ai/download");
                break;
            case DiagnosticKind.NotSignedIn:
                _message.IsWarning = false;
                _message.Title = Strings.NotSignedInTitle;
                _message.Body = Strings.NotSignedInBody;
                _message.Link = Strings.NotSignedInLink;
                _messageAction = () => OpenTerminalWithClaude(controller.ClaudePath);
                break;
            case DiagnosticKind.Other:
                _message.IsWarning = true;
                _message.Title = sm.HasData ? Strings.StaleErrorTitle : Strings.ErrorTitle;
                _message.Body = sm.ErrorMessage ?? "";
                break;
            default:
                if (!sm.HasData)
                {
                    _message.IsWarning = false;
                    _message.Title = Strings.Waiting;
                    _message.Body = "";
                }
                else
                {
                    _hasMessage = false;
                }
                break;
        }

        // Footer.
        string status = controller.IsRefreshing && !sm.HasData ? Strings.Refreshing : "";
        if (sm.LastUpdated is { } updated)
        {
            status = Strings.Updated(UsageFormatting.ShortClock(updated));
            if (sm.IsDataOld(now)) status += Strings.Stale;
            if (controller.IsRefreshing) status += " · " + Strings.Refreshing;
        }
        _footer.Status = status;

        SyncSettingsControls();
        Relayout();
    }

    private void SyncSettingsControls()
    {
        _theme.SetSilently((int)_settings.ThemeMode);
        _trayStyle.SetSilently((int)_settings.TrayStyle);
        _barsSource.SetSilently(_settings.BarsWeekSource == UsageKind.WeekModel ? 1 : 0);
        _floating.SetSilently(_settings.FloatingEnabled);
        _precise.SetSilently(_settings.PreciseBars);
        _startup.SetSilently(StartupManager.IsEnabled);
        _alerts.SetSilently(_settings.AlertsEnabled);
        _alertQuarter.SetSilently(_settings.AlertQuarter);
        _alertLow.SetSilently(_settings.AlertLow);
        _alertDepleted.SetSilently(_settings.AlertDepleted);
        _alertReset.SetSilently(_settings.AlertReset);
    }

    private void WireSettings()
    {
        void Changed()
        {
            _settings.Save();
            Relayout();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        _theme.SelectedChanged += (_, _) =>
        {
            _settings.ThemeMode = (ThemeMode)_theme.SelectedIndex;
            Theme.Mode = _settings.ThemeMode;
            ApplyPalette(); // repaint the open panel right away
            Changed();
        };
        _trayStyle.SelectedChanged += (_, _) => { _settings.TrayStyle = (TrayStyle)_trayStyle.SelectedIndex; Changed(); };
        _barsSource.SelectedChanged += (_, _) =>
        {
            _settings.BarsWeekSource = _barsSource.SelectedIndex == 1 ? UsageKind.WeekModel : UsageKind.WeekAll;
            Changed();
        };
        _floating.CheckedChanged += (_, _) => { _settings.FloatingEnabled = _floating.Checked; Changed(); };
        _precise.CheckedChanged += (_, _) => { _settings.PreciseBars = _precise.Checked; Changed(); };
        _startup.CheckedChanged += (_, _) => { StartupManager.SetEnabled(_startup.Checked); Changed(); };
        _alerts.CheckedChanged += (_, _) => { _settings.AlertsEnabled = _alerts.Checked; Changed(); };
        _alertQuarter.CheckedChanged += (_, _) => { _settings.AlertQuarter = _alertQuarter.Checked; Changed(); };
        _alertLow.CheckedChanged += (_, _) => { _settings.AlertLow = _alertLow.Checked; Changed(); };
        _alertDepleted.CheckedChanged += (_, _) => { _settings.AlertDepleted = _alertDepleted.Checked; Changed(); };
        _alertReset.CheckedChanged += (_, _) => { _settings.AlertReset = _alertReset.Checked; Changed(); };
    }

    /// <summary>Stack the wanted rows top-to-bottom and size the window to fit.</summary>
    /// <remarks>
    /// Uses <see cref="SurfaceControl.Wanted"/> rather than <c>Visible</c>: a child's
    /// <c>Visible</c> reads false while the form itself is hidden, which would
    /// collapse the layout before the first Show().
    /// </remarks>
    private void Relayout()
    {
        _header.SettingsOpen = _settingsOpen;
        bool open = _settingsOpen;
        // The gear swaps the usage view for the settings view, which keeps the
        // flyout short enough for small / high-scale screens.
        foreach (var m in _metrics) m.Wanted = !open && m.Metric is not null;
        _message.Wanted = !open && _hasMessage;
        _settingsCaption.Wanted = _theme.Wanted = _trayStyle.Wanted = _floating.Wanted = _precise.Wanted =
            _startup.Wanted = _alerts.Wanted = open;
        _barsSource.Wanted = open && _settings.TrayStyle == TrayStyle.Bars;
        _alertQuarter.Wanted = _alertLow.Wanted = _alertDepleted.Wanted = _alertReset.Wanted =
            open && _settings.AlertsEnabled;

        float scale = DeviceDpi / 96f;
        int width = (int)MathF.Round(LogicalWidth * scale);
        var lastMetric = _metrics.LastOrDefault(m => m.Wanted);
        int y = 0;
        SuspendLayout();
        foreach (Control c in Controls)
        {
            if (c is not SurfaceControl s) continue;
            s.Visible = s.Wanted;
            if (!s.Wanted) continue;
            int h = (int)MathF.Round(s.MeasureHeight(LogicalWidth) * scale);
            s.Bounds = new Rectangle(0, y, width, h);
            y += h;
            if (ReferenceEquals(s, lastMetric)) y += (int)(6 * scale);
        }
        ResumeLayout(false);

        var oldBottom = Bottom;
        ClientSize = new Size(width, Math.Max(y, 1));
        if (Visible)
        {
            // Grow upwards when anchored above the taskbar, but never off the top of the screen.
            var wa = Screen.FromControl(this).WorkingArea;
            int top = _anchoredBottom ? oldBottom - Height : Top;
            Top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - Height));
        }
        Invalidate(true);
    }

    // --------------------------------------------------------------- showing

    private bool _anchoredBottom = true;

    /// <summary>Position next to the tray (taskbar can be on any edge) and show.</summary>
    public void ShowNearTray()
    {
        ApplyPalette();
        Relayout();

        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;
        var b = screen.Bounds;
        int gap = (int)(12 * DeviceDpi / 96f);

        // Clamp helper that never throws when the panel is larger than the area.
        static int Fit(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);

        int x, y;
        _anchoredBottom = true;
        if (wa.Top > b.Top) // taskbar on top
        {
            x = Fit(cursor.X - Width / 2, wa.Left + gap, wa.Right - Width - gap);
            y = wa.Top + gap;
            _anchoredBottom = false;
        }
        else if (wa.Left > b.Left) // left
        {
            x = wa.Left + gap;
            y = Fit(cursor.Y - Height / 2, wa.Top + gap, wa.Bottom - Height - gap);
        }
        else if (wa.Right < b.Right) // right
        {
            x = wa.Right - Width - gap;
            y = Fit(cursor.Y - Height / 2, wa.Top + gap, wa.Bottom - Height - gap);
        }
        else // bottom (default) or auto-hide
        {
            x = Fit(cursor.X - Width / 2, wa.Left + gap, wa.Right - Width - gap);
            y = Math.Max(wa.Top, wa.Bottom - Height - gap);
        }

        Location = new Point(x, y);
        Show();
        Activate();
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Couldn't open URL", ex); }
    }

    private static void OpenTerminalWithClaude(string? claudePath)
    {
        try
        {
            var target = string.IsNullOrEmpty(claudePath) ? "claude" : $"\"{claudePath}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/k {target}")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
        }
        catch (Exception ex) { Log.Error("Couldn't open terminal", ex); }
    }
}
