using ClaudemonWin.Core;

namespace ClaudemonWin.Core.Tests;

public class StateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    private static UsageReport Report(int pct, DateTimeOffset at) =>
        new([new UsageMetric(UsageKind.Session, "Current session", pct, at.AddHours(3), "UTC")], at);

    [Fact]
    public void StartsLoading()
    {
        var sm = new UsageStateMachine();
        Assert.Equal(UsageStatus.Loading, sm.Status);
        Assert.False(sm.HasData);
    }

    [Fact]
    public void SuccessLoadsAndClearsErrors()
    {
        var sm = new UsageStateMachine();
        sm.ApplyFailure(new UsageFetchException(FetchErrorKind.TimedOut, "Claude CLI timed out"));
        sm.ApplySuccess(Report(10, T0));
        Assert.Equal(UsageStatus.Loaded, sm.Status);
        Assert.Null(sm.ErrorMessage);
        Assert.Equal(DiagnosticKind.None, sm.Diagnostic);
        Assert.Equal(T0, sm.LastUpdated);
    }

    [Fact]
    public void StaleRenderKeepsLastGoodCalmly()
    {
        var sm = new UsageStateMachine();
        sm.ApplySuccess(Report(10, T0));
        sm.ApplyStaleRender(T0.AddMinutes(1));
        Assert.Equal(UsageStatus.StaleRender, sm.Status);
        Assert.Equal(10, sm.LastGood!.Session!.Percent);
        Assert.Null(sm.ErrorMessage);
    }

    [Fact]
    public void FailureWithDataIsStaleError()
    {
        var sm = new UsageStateMachine();
        sm.ApplySuccess(Report(10, T0));
        sm.ApplyFailure(new UsageFetchException(FetchErrorKind.TimedOut, "Claude CLI timed out"));
        Assert.Equal(UsageStatus.StaleError, sm.Status);
        Assert.NotNull(sm.LastGood);
    }

    [Fact]
    public void ClassifiesDiagnostics()
    {
        Assert.Equal(DiagnosticKind.NotInstalled,
            UsageStateMachine.Classify(new UsageFetchException(FetchErrorKind.ClaudeNotFound, "x")).Kind);
        Assert.Equal(DiagnosticKind.NotSignedIn,
            UsageStateMachine.Classify(new UsageFetchException(FetchErrorKind.NotSignedIn, "x")).Kind);
        Assert.Equal(DiagnosticKind.NotSignedIn,
            UsageStateMachine.Classify(new UsageParseException(UsageParseErrorKind.CliError, "Please run /login")).Kind);
        Assert.Equal(DiagnosticKind.Other,
            UsageStateMachine.Classify(new UsageFetchException(FetchErrorKind.TimedOut, "t")).Kind);
    }

    [Fact]
    public void ColdMissesEscalateOnlyAfterCountAndWallClock()
    {
        var sm = new UsageStateMachine();
        sm.MarkStarted(T0);
        for (int i = 0; i < 10; i++) sm.ApplyStaleRender(T0.AddSeconds(10 * i)); // 10 misses within 90s
        Assert.Equal(UsageStatus.Loading, sm.Status);

        sm.ApplyStaleRender(T0.AddSeconds(130));
        Assert.Equal(UsageStatus.Error, sm.Status);
        Assert.Equal(DiagnosticKind.Other, sm.Diagnostic);

        sm.ApplySuccess(Report(5, T0.AddSeconds(140)));
        Assert.Equal(UsageStatus.Loaded, sm.Status);
    }

    [Fact]
    public void AcceleratedFollowUpsAreCapped()
    {
        var sm = new UsageStateMachine();
        sm.ApplySuccess(Report(10, T0));
        Assert.True(sm.ApplyStaleRender(T0));   // 1st miss → follow-up
        Assert.True(sm.ApplyStaleRender(T0));   // 2nd → one more
        Assert.False(sm.ApplyStaleRender(T0));  // cap reached
        Assert.True(sm.ApplyStaleRender(T0));   // new cycle
        sm.ApplySuccess(Report(10, T0));
        Assert.True(sm.ApplyStaleRender(T0));   // a hit resets the counter
    }

    [Fact]
    public void PrimeOnlyWhileLoading()
    {
        var sm = new UsageStateMachine();
        Assert.True(sm.Prime(Report(42, T0.AddHours(-1))));
        Assert.Equal(UsageStatus.StaleRender, sm.Status);
        Assert.Equal(T0.AddHours(-1), sm.LastUpdated);
        Assert.False(sm.Prime(Report(1, T0)));
        Assert.Equal(42, sm.LastGood!.Session!.Percent);
    }

    [Fact]
    public void DataOldAfterTenMinutes()
    {
        var sm = new UsageStateMachine();
        sm.ApplySuccess(Report(10, T0));
        Assert.False(sm.IsDataOld(T0.AddMinutes(9)));
        Assert.True(sm.IsDataOld(T0.AddMinutes(11)));
    }
}
