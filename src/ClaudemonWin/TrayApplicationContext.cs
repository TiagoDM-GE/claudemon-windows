using ClaudemonWin.Core;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace ClaudemonWin;

/// <summary>
/// The app: tray icon + flyout panel + optional floating window, wired to one
/// <see cref="UsageController"/> that is the single source of truth.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UsageController _controller;
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _floatingItem = new(Strings.MenuFloating);
    private readonly ToolStripMenuItem _startupItem = new(Strings.MenuStartup);
    private readonly PanelForm _panel;
    private readonly Timer _repaintTimer = new() { Interval = 30_000 }; // keeps countdowns ticking
    private readonly Timer _resumeTimer = new() { Interval = 8_000 };
    private readonly AlertTracker _alerts;
    private readonly SynchronizationContext _ui;
    private FloatingForm? _floating;
    private TrayVisual? _lastVisual;

    public TrayApplicationContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Theme.Mode = _settings.ThemeMode;
        StartupManager.RepairPathIfNeeded();

        var fetcher = new UsageFetcher(ClaudeLocator.CreateDefault(), AppPaths.WorkDir);
        _controller = new UsageController(fetcher);
        _alerts = new AlertTracker(JsonStore.Read<Dictionary<UsageKind, ThresholdWindowState>>(AppPaths.AlertStateFile));

        _panel = new PanelForm(_settings);
        _panel.RefreshRequested += (_, _) => _controller.Refresh();
        _panel.QuitRequested += (_, _) => Quit();
        _panel.SettingsChanged += (_, _) => ApplySettings();

        BuildMenu();
        _tray.ContextMenuStrip = _menu;
        _tray.Text = Strings.AppTitle;
        _tray.MouseClick += OnTrayClick;
        _tray.Icon = TrayIconRenderer.Render(CurrentVisual());
        _tray.Visible = true;

        _controller.Changed += (_, _) => RefreshUi();
        _controller.FreshReport += (_, report) => EvaluateAlerts(report);
        _repaintTimer.Tick += (_, _) => RefreshUi();
        _resumeTimer.Tick += (_, _) =>
        {
            _resumeTimer.Stop();
            _controller.Start();
        };
        _repaintTimer.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplySettings();
        _controller.Start();

        if (!_settings.FirstRunHintShown)
        {
            _settings.FirstRunHintShown = true;
            _settings.Save();
            _tray.ShowBalloonTip(8000, Strings.FirstRunTitle, Strings.FirstRunBody, ToolTipIcon.Info);
        }
        Log.Info($"Started. claude = {_controller.ClaudePath ?? "(not found)"}");
    }

    // ---------------------------------------------------------------- tray

    private void BuildMenu()
    {
        _menu.Items.Add(Strings.MenuOpen, null, (_, _) => _panel.ShowNearTray());
        _menu.Items.Add(Strings.MenuRefresh, null, (_, _) => _controller.Refresh());
        _menu.Items.Add(new ToolStripSeparator());
        _floatingItem.Click += (_, _) =>
        {
            _settings.FloatingEnabled = !_settings.FloatingEnabled;
            _settings.Save();
            ApplySettings();
        };
        _startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(!StartupManager.IsEnabled);
            ApplySettings();
        };
        _menu.Items.Add(_floatingItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(Strings.MenuDataFolder, null, (_, _) => AppPaths.OpenDataFolder());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Strings.MenuQuit, null, (_, _) => Quit());
        _menu.Opening += (_, _) =>
        {
            _floatingItem.Checked = _settings.FloatingEnabled;
            _startupItem.Checked = StartupManager.IsEnabled;
        };
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_panel.Visible)
        {
            _panel.HidePanel();
            return;
        }
        // The click that deactivated (and hid) the panel must not immediately reopen it.
        if ((DateTime.UtcNow - _panel.LastHiddenAt).TotalMilliseconds < 350) return;
        _panel.UpdateFrom(_controller);
        _panel.ShowNearTray();
    }

    private TrayVisual CurrentVisual()
    {
        var sm = _controller?.Machine;
        var report = sm?.LastGood;
        return new TrayVisual(
            _settings.TrayStyle,
            report?.Session?.Percent,
            report?.Get(_settings.BarsWeekSource)?.Percent,
            HasError: sm?.Status is UsageStatus.Error or UsageStatus.StaleError,
            IsStale: sm?.IsDataOld(DateTimeOffset.Now) ?? false,
            Size: NativeMethods.CurrentSmallIconSize(),
            TaskbarLight: Theme.TaskbarIsLight);
    }

    private void UpdateTrayIcon(bool force = false)
    {
        var visual = CurrentVisual();
        if (!force && _lastVisual == visual) return;
        _lastVisual = visual;

        var old = _tray.Icon;
        _tray.Icon = TrayIconRenderer.Render(visual);
        old?.Dispose();
    }

    private string TooltipText()
    {
        var report = _controller.Machine.LastGood;
        if (report is null)
        {
            return _controller.Machine.Diagnostic switch
            {
                DiagnosticKind.NotInstalled => Strings.NotInstalledTitle,
                DiagnosticKind.NotSignedIn => Strings.NotSignedInTitle,
                DiagnosticKind.Other => Strings.ErrorTitle,
                _ => Strings.Waiting,
            };
        }

        var parts = new List<string>();
        foreach (var m in report.Metrics)
        {
            var line = $"{Strings.ShortLabel(m)} {m.Percent}%";
            var cd = UsageFormatting.Countdown(m.ResetDate, DateTimeOffset.Now);
            if (m.Kind == UsageKind.Session && cd is not null) line += $" ({Strings.Countdown(cd)})";
            parts.Add(line);
        }
        var text = "Claude · " + string.Join("\n", parts);
        return text.Length <= 127 ? text : text[..127]; // NotifyIcon.Text hard limit
    }

    // ------------------------------------------------------------------ ui

    private void RefreshUi()
    {
        UpdateTrayIcon();
        _tray.Text = TooltipText();
        if (_panel.Visible) _panel.UpdateFrom(_controller);
        _floating?.UpdateFrom(_controller);
    }

    private void ApplySettings()
    {
        if (_settings.FloatingEnabled && _floating is null)
        {
            _floating = new FloatingForm(_settings);
            _floating.RefreshRequested += (_, _) => _controller.Refresh();
            _floating.HideRequested += (_, _) =>
            {
                _settings.FloatingEnabled = false;
                _settings.Save();
                // Defer: this can run inside the form's own menu click or FormClosing.
                _ui.Post(_ => ApplySettings(), null);
            };
            _floating.UpdateFrom(_controller);
            _floating.Show();
        }
        else if (!_settings.FloatingEnabled && _floating is not null)
        {
            var old = _floating;
            _floating = null;
            old.Dispose(); // Dispose (not Close) so OnFormClosing's hide-instead-of-close doesn't interfere
        }

        Theme.Mode = _settings.ThemeMode;
        _panel.ApplyPalette();
        UpdateTrayIcon(force: true);
        RefreshUi();
    }

    private void EvaluateAlerts(UsageReport report)
    {
        var events = _alerts.Evaluate(report,
            _settings.AlertsEnabled ? _settings.EnabledThresholds() : new HashSet<UsageAlertThreshold>(),
            _settings.AlertsEnabled && _settings.AlertReset);
        JsonStore.Write(AppPaths.AlertStateFile, _alerts.ThresholdState);
        if (events.Count == 0) return;

        // Balloon tips show as Windows notifications; merge several into one so none is lost.
        var title = events.Count == 1 ? Strings.MetricLabel(events[0].Metric) : "Claudemon";
        var body = string.Join("\n", events.Select(e =>
        {
            var text = e.IsReset ? Strings.ResetBody : Strings.AlertBody(e.Threshold!.Value);
            return events.Count == 1 ? text : $"{Strings.ShortLabel(e.Metric)}: {text}";
        }));
        bool severe = events.Any(e => e.Threshold is UsageAlertThreshold.Low or UsageAlertThreshold.Depleted);
        _tray.ShowBalloonTip(10_000, title, body, severe ? ToolTipIcon.Warning : ToolTipIcon.Info);
        Log.Info($"Alert: {title} — {body.Replace('\n', ' ')}");
    }

    // --------------------------------------------------------- system events

    // SystemEvents may raise on a background thread: always hop to the UI thread.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => _ui.Post(_ =>
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _resumeTimer.Stop();
            Log.Info("Suspend: pausing polling");
            _controller.Stop();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            Log.Info("Resume: restarting polling shortly");
            _resumeTimer.Stop();
            _resumeTimer.Start(); // give Wi-Fi/VPN a few seconds to come back first
        }
    }, null);

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => _ui.Post(_ =>
    {
        // Light/dark switches arrive as General.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            _panel.ApplyPalette();
            UpdateTrayIcon(force: true);
            _floating?.UpdateFrom(_controller);
        }
    }, null);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _ui.Post(_ => UpdateTrayIcon(force: true), null);

    // ------------------------------------------------------------- shutdown

    private void Quit()
    {
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _repaintTimer.Dispose();
            _resumeTimer.Dispose();
            _controller.Dispose();
            _floating?.Dispose();
            _panel.Dispose();
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
