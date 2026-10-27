using ClaudemonWin.Core;
using Timer = System.Windows.Forms.Timer;

namespace ClaudemonWin;

/// <summary>
/// Owns polling: one 60s timer, an immediate refresh on start, a sooner one-shot
/// follow-up after a "no new data" poll, and pause/resume around sleep. All state
/// rules live in <see cref="UsageStateMachine"/>; this class only does timing and I/O.
/// Everything here runs on the UI thread (WinForms timers + awaits resume on it).
/// </summary>
internal sealed class UsageController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FollowUpInterval = TimeSpan.FromSeconds(28);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30); // node cold start on Windows can be slow

    private readonly UsageFetcher _fetcher;
    private readonly Timer _pollTimer = new() { Interval = (int)PollInterval.TotalMilliseconds };
    private readonly Timer _followUpTimer = new() { Interval = (int)FollowUpInterval.TotalMilliseconds };
    private CancellationTokenSource? _inflight;
    private bool _running;

    public UsageController(UsageFetcher fetcher)
    {
        _fetcher = fetcher;
        _pollTimer.Tick += (_, _) => Refresh();
        _followUpTimer.Tick += (_, _) =>
        {
            _followUpTimer.Stop();
            Refresh();
        };
    }

    public UsageStateMachine Machine { get; } = new();
    public bool IsRefreshing { get; private set; }

    /// <summary>Raised on the UI thread whenever state or the refreshing flag changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised on the UI thread with each genuinely fresh report (drives alerts).</summary>
    public event EventHandler<UsageReport>? FreshReport;

    public string? ClaudePath => _fetcher.ResolveClaudePath();

    public void Start()
    {
        if (_running) return;
        _running = true;
        Machine.MarkStarted(DateTimeOffset.Now);

        // Seed from the on-disk cache so the UI shows numbers instantly.
        if (JsonStore.Read<UsageReport>(AppPaths.CacheFile) is { Metrics.Count: > 0 } cached && Machine.Prime(cached))
            OnChanged();

        Refresh();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _running = false;
        _pollTimer.Stop();
        _followUpTimer.Stop();
        _inflight?.Cancel();
        _inflight = null;
        Machine.ResetCounters();
        IsRefreshing = false;
        OnChanged();
    }

    public async void Refresh()
    {
        if (IsRefreshing) return; // don't pile up overlapping CLI runs
        IsRefreshing = true;
        OnChanged();

        var cts = new CancellationTokenSource();
        _inflight = cts;
        try
        {
            // Discovery + process I/O + parsing all happen off the UI thread.
            var report = await Task.Run(async () =>
            {
                var json = await _fetcher.FetchUsageJsonAsync(FetchTimeout, cts.Token).ConfigureAwait(false);
                return UsageParser.Parse(json, DateTimeOffset.Now);
            }, cts.Token);

            _followUpTimer.Stop();
            Machine.ApplySuccess(report);
            if (!JsonStore.Write(AppPaths.CacheFile, report)) Log.Error("Couldn't write usage cache");
            FreshReport?.Invoke(this, report);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // stopped (sleep / quit) — nothing to report
        }
        catch (UsageParseException ex) when (ex.IsStaleRender)
        {
            if (Machine.ApplyStaleRender(DateTimeOffset.Now) && _running)
            {
                _followUpTimer.Stop();
                _followUpTimer.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Usage refresh failed", ex);
            Machine.ApplyFailure(ex);
        }
        finally
        {
            // Only the current run clears the flag; a run cancelled by Stop() must not
            // unblock a newer run that started after a resume.
            if (ReferenceEquals(_inflight, cts))
            {
                _inflight = null;
                IsRefreshing = false;
            }
            cts.Dispose();
            OnChanged();
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Stop();
        _pollTimer.Dispose();
        _followUpTimer.Dispose();
    }
}
