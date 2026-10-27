using ClaudemonWin.Core;

namespace ClaudemonWin.Core.Tests;

public class AlertPolicyTests
{
    private static readonly IReadOnlySet<UsageAlertThreshold> All = UsageAlertPolicy.AllThresholds.ToHashSet();
    private static readonly IReadOnlySet<int> None = new HashSet<int>();
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingFiresBelowFirstThreshold()
    {
        var d = UsageAlertPolicy.Decide(70, "h1", null, None, All);
        Assert.Null(d.Fire);
        Assert.Empty(d.Fired);
    }

    [Fact]
    public void FiresQuarterAt75PercentUsed()
    {
        var d = UsageAlertPolicy.Decide(75, "h1", null, None, All);
        Assert.Equal(UsageAlertThreshold.Quarter, d.Fire);
    }

    [Fact]
    public void BigJumpFiresOnlyMostSevereButMarksAllCrossed()
    {
        var d = UsageAlertPolicy.Decide(100, "h1", null, None, All);
        Assert.Equal(UsageAlertThreshold.Depleted, d.Fire);
        Assert.Equal(3, d.Fired.Count);
    }

    [Fact]
    public void DoesNotRefireInSameWindow()
    {
        var first = UsageAlertPolicy.Decide(80, "h1", null, None, All);
        var second = UsageAlertPolicy.Decide(82, "h1", first.Signature, first.Fired, All);
        Assert.Null(second.Fire);
    }

    [Fact]
    public void NewWindowRearms()
    {
        var first = UsageAlertPolicy.Decide(80, "h1", null, None, All);
        var next = UsageAlertPolicy.Decide(80, "h2", first.Signature, first.Fired, All);
        Assert.Equal(UsageAlertThreshold.Quarter, next.Fire);
    }

    [Fact]
    public void DisabledThresholdIsSkipped()
    {
        var onlyLow = new HashSet<UsageAlertThreshold> { UsageAlertThreshold.Low };
        Assert.Null(UsageAlertPolicy.Decide(80, "h1", null, None, onlyLow).Fire);
        Assert.Equal(UsageAlertThreshold.Low, UsageAlertPolicy.Decide(96, "h1", null, None, onlyLow).Fire);
    }

    [Fact]
    public void SignatureAbsorbsMinuteJitter()
    {
        var a = UsageAlertPolicy.WindowSignature(new DateTimeOffset(2026, 6, 26, 9, 59, 0, TimeSpan.Zero), null);
        var b = UsageAlertPolicy.WindowSignature(new DateTimeOffset(2026, 6, 26, 10, 0, 0, TimeSpan.Zero), null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void SignatureInheritsOnParseMiss()
    {
        Assert.Equal("h42", UsageAlertPolicy.WindowSignature(null, "h42"));
        Assert.Equal("none", UsageAlertPolicy.WindowSignature(null, null));
    }

    [Fact]
    public void ResetFiresOnlyForUsedWindowWitnessedRolling()
    {
        var reset = T0.AddHours(1);
        var s1 = UsageAlertPolicy.DecideReset(T0, reset, 30, new());
        Assert.False(s1.FireReset); // arming, not firing

        var s2 = UsageAlertPolicy.DecideReset(reset.AddMinutes(1), reset.AddHours(5), 0, s1.State);
        Assert.True(s2.FireReset);
        Assert.False(s2.State.DidUse);
        Assert.Equal(reset.AddHours(5), s2.State.PendingResetDate);
    }

    [Fact]
    public void UntouchedWindowResetsSilently()
    {
        var reset = T0.AddHours(1);
        var s1 = UsageAlertPolicy.DecideReset(T0, reset, 0, new());
        var s2 = UsageAlertPolicy.DecideReset(reset.AddMinutes(1), reset.AddHours(5), 0, s1.State);
        Assert.False(s2.FireReset);
    }

    [Fact]
    public void TrackerEmitsThresholdAndResetEvents()
    {
        var tracker = new AlertTracker();
        var reset = T0.AddHours(1);
        var r1 = new UsageReport([new UsageMetric(UsageKind.Session, "Current session", 80, reset, "UTC")], T0);
        var events1 = tracker.Evaluate(r1, All, resetAlertsEnabled: true);
        Assert.Single(events1);
        Assert.Equal(UsageAlertThreshold.Quarter, events1[0].Threshold);

        // Same window, no new alert.
        Assert.Empty(tracker.Evaluate(r1 with { CapturedAt = T0.AddMinutes(1) }, All, true));

        // Window rolled → reset alert, and the fresh window re-arms (0% → no threshold).
        var r2 = new UsageReport([new UsageMetric(UsageKind.Session, "Current session", 0, reset.AddHours(5), "UTC")], reset.AddMinutes(2));
        var events2 = tracker.Evaluate(r2, All, true);
        Assert.Single(events2);
        Assert.True(events2[0].IsReset);
    }

    [Fact]
    public void TrackerStateSurvivesRestartViaPersistence()
    {
        var reset = T0.AddHours(1);
        var report = new UsageReport([new UsageMetric(UsageKind.WeekAll, "Current week (all models)", 96, reset, "UTC")], T0);
        var first = new AlertTracker();
        Assert.Single(first.Evaluate(report, All, true));

        var restarted = new AlertTracker(first.ThresholdState);
        Assert.Empty(restarted.Evaluate(report, All, true));
    }
}
