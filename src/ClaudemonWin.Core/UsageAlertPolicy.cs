namespace ClaudemonWin.Core;

/// <summary>Alert points, expressed as REMAINING quota percentage.</summary>
public enum UsageAlertThreshold
{
    Quarter = 25,  // 25% left
    Low = 5,       // 5% left
    Depleted = 0,  // used up
}

/// <summary>
/// Pure policy deciding when a quota crosses an alert point and when a used
/// window has reset. No UI, no I/O — the app layer posts the notifications.
/// </summary>
public static class UsageAlertPolicy
{
    public static readonly UsageAlertThreshold[] AllThresholds =
        [UsageAlertThreshold.Quarter, UsageAlertThreshold.Low, UsageAlertThreshold.Depleted];

    public sealed record Decision(UsageAlertThreshold? Fire, string Signature, IReadOnlySet<int> Fired);

    /// <summary>
    /// Decide which (if any) threshold should fire. A new window signature re-arms
    /// everything. Only the most severe newly-crossed threshold fires, so a big
    /// jump between polls yields one accurate alert instead of several.
    /// </summary>
    public static Decision Decide(
        int percentUsed,
        string windowSignature,
        string? priorSignature,
        IReadOnlySet<int> firedThresholds,
        IReadOnlySet<UsageAlertThreshold> enabledThresholds)
    {
        var fired = priorSignature == windowSignature ? new HashSet<int>(firedThresholds) : new HashSet<int>();
        int remaining = Math.Max(0, 100 - percentUsed);

        UsageAlertThreshold? mostSevere = null;
        foreach (var threshold in AllThresholds)
        {
            if (!enabledThresholds.Contains(threshold)) continue;
            if (remaining > (int)threshold) continue;
            if (!fired.Add((int)threshold)) continue;
            if (mostSevere is null || (int)threshold < (int)mostSevere) mostSevere = threshold;
        }

        return new Decision(mostSevere, windowSignature, fired);
    }

    /// <summary>
    /// De-dup window id derived from the reset date. Bucketed to the nearest hour
    /// (the CLI jitters "9:59am" ↔ "10am"), and inherits the prior signature on a
    /// transient parse miss so one bad poll can't re-arm and re-fire everything.
    /// </summary>
    public static string WindowSignature(DateTimeOffset? resetDate, string? prior)
    {
        if (resetDate is null) return prior ?? "none";
        long hourBucket = (long)Math.Round(resetDate.Value.ToUnixTimeSeconds() / 3600.0, MidpointRounding.AwayFromZero);
        return $"h{hourBucket}";
    }

    // ------------------------------------------------------------ reset alerts

    /// <summary>
    /// Per-metric in-memory state for the "limit reset" alert. Never persisted:
    /// only a rollover witnessed while the app runs is announced.
    /// </summary>
    public sealed record ResetWindowState(DateTimeOffset? PendingResetDate = null, bool DidUse = false);

    public sealed record ResetDecision(bool FireReset, ResetWindowState State);

    /// <summary>
    /// 1. If a previously-seen future reset has passed, the window rolled over:
    ///    fire iff that window had any usage, then clear.
    /// 2. Arm/refresh the pending window from the latest future reset date.
    /// 3. Remember any usage in the current window.
    /// </summary>
    public static ResetDecision DecideReset(DateTimeOffset now, DateTimeOffset? resetDate, int percentUsed, ResetWindowState prior)
    {
        var state = prior;
        bool fire = false;

        if (state.PendingResetDate is { } pending && now >= pending)
        {
            fire = state.DidUse;
            state = new ResetWindowState(null, false);
        }

        if (resetDate is { } r && r > now)
            state = state with { PendingResetDate = r };

        if (percentUsed > 0)
            state = state with { DidUse = true };

        return new ResetDecision(fire, state);
    }
}

/// <summary>A notification the app should show.</summary>
public sealed record AlertEvent(UsageMetric Metric, UsageAlertThreshold? Threshold, bool IsReset);

/// <summary>Persisted per-metric threshold state (survives restarts so relaunching doesn't re-alert).</summary>
public sealed class ThresholdWindowState
{
    public string Signature { get; set; } = "none";
    public HashSet<int> Fired { get; set; } = [];
}

/// <summary>
/// Stateful wrapper around <see cref="UsageAlertPolicy"/>: feed it every fresh
/// report and it returns the alerts to post.
/// </summary>
public sealed class AlertTracker
{
    private readonly Dictionary<UsageKind, UsageAlertPolicy.ResetWindowState> _resetState = [];

    public AlertTracker(Dictionary<UsageKind, ThresholdWindowState>? persisted = null)
    {
        ThresholdState = persisted ?? [];
    }

    /// <summary>Threshold state to persist between runs.</summary>
    public Dictionary<UsageKind, ThresholdWindowState> ThresholdState { get; }

    public IReadOnlyList<AlertEvent> Evaluate(
        UsageReport report,
        IReadOnlySet<UsageAlertThreshold> enabledThresholds,
        bool resetAlertsEnabled)
    {
        var events = new List<AlertEvent>();

        foreach (var metric in report.Metrics)
        {
            ThresholdState.TryGetValue(metric.Kind, out var prior);
            var signature = UsageAlertPolicy.WindowSignature(metric.ResetDate, prior?.Signature);
            var decision = UsageAlertPolicy.Decide(
                metric.Percent,
                signature,
                prior?.Signature,
                prior?.Fired ?? (IReadOnlySet<int>)new HashSet<int>(),
                enabledThresholds);

            if (decision.Fire is { } threshold)
                events.Add(new AlertEvent(metric, threshold, false));

            ThresholdState[metric.Kind] = new ThresholdWindowState
            {
                Signature = decision.Signature,
                Fired = new HashSet<int>(decision.Fired),
            };

            // Track resets every poll (even with the toggle off) but only report when enabled.
            _resetState.TryGetValue(metric.Kind, out var resetPrior);
            var reset = UsageAlertPolicy.DecideReset(report.CapturedAt, metric.ResetDate, metric.Percent,
                resetPrior ?? new UsageAlertPolicy.ResetWindowState());
            _resetState[metric.Kind] = reset.State;
            if (resetAlertsEnabled && reset.FireReset)
                events.Add(new AlertEvent(metric, null, true));
        }

        return events;
    }
}
