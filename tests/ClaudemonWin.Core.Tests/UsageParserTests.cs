using System.Text.Json;
using ClaudemonWin.Core;

namespace ClaudemonWin.Core.Tests;

public class UsageParserTests
{
    // Fixed "now": 2026-06-23 12:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    private const string SampleText =
        "Current session: 28% used · resets Jun 24 at 2:49am (Europe/Istanbul)\n" +
        "Current week (all models): 29% used · resets Jun 26 at 9:59am (Europe/Istanbul)\n" +
        "Current week (Fable): 2% used · resets Jun 26 at 10am (Europe/Istanbul)";

    private static string Envelope(string result, bool isError = false) =>
        JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = isError, result });

    [Fact]
    public void ParsesAllThreeMetrics()
    {
        var report = UsageParser.Parse(Envelope(SampleText), Now);

        Assert.Equal(3, report.Metrics.Count);
        Assert.Equal(28, report.Session!.Percent);
        Assert.Equal(29, report.WeekAll!.Percent);
        Assert.Equal(2, report.WeekModel!.Percent);
        Assert.Equal("Fable", report.WeekModel.ModelName);
        Assert.Equal("Current Week (Fable)", report.WeekModel.DisplayLabel);
        Assert.Equal(Now, report.CapturedAt);
    }

    [Fact]
    public void ResolvesResetInstantInItsOwnTimeZone()
    {
        var report = UsageParser.Parse(Envelope(SampleText), Now);
        // Istanbul is UTC+3 all year → 02:49 local = 23:49 UTC the day before.
        Assert.Equal(new DateTimeOffset(2026, 6, 23, 23, 49, 0, TimeSpan.Zero), report.Session!.ResetDate!.Value.ToUniversalTime());
        Assert.Equal("Europe/Istanbul", report.Session.TimeZoneId);
        // "10am" without minutes.
        Assert.Equal(new DateTimeOffset(2026, 6, 26, 7, 0, 0, TimeSpan.Zero), report.WeekModel!.ResetDate!.Value.ToUniversalTime());
    }

    [Fact]
    public void RollsYearForwardWhenDateAlreadyPassed()
    {
        var lateDecember = new DateTimeOffset(2026, 12, 31, 20, 0, 0, TimeSpan.Zero);
        var report = UsageParser.ParseResultText("Current session: 5% used · resets Jan 1 at 1am (UTC)", lateDecember);
        Assert.Equal(2027, report.Session!.ResetDate!.Value.Year);
    }

    [Fact]
    public void ModelNameIsNotHardcoded()
    {
        var report = UsageParser.ParseResultText(
            "Current week (Sonnet only): 40% used · resets Jun 26 at 10am (Europe/Lisbon)", Now);
        Assert.Equal(UsageKind.WeekModel, report.Metrics.Single().Kind);
        Assert.Equal("Sonnet only", report.WeekModel!.ModelName);
    }

    [Fact]
    public void OrderIsStableRegardlessOfInputOrder()
    {
        var shuffled = string.Join("\n", SampleText.Split('\n').Reverse());
        var report = UsageParser.ParseResultText(shuffled, Now);
        Assert.Equal(new[] { UsageKind.Session, UsageKind.WeekAll, UsageKind.WeekModel }, report.Metrics.Select(m => m.Kind).ToArray());
    }

    [Fact]
    public void ClampsPercentTo100()
    {
        var report = UsageParser.ParseResultText("Current session: 130% used · resets Jun 24 at 2am (UTC)", Now);
        Assert.Equal(100, report.Session!.Percent);
    }

    [Fact]
    public void AcceptsMultiLineTuiLayout()
    {
        const string tui = "Current session\n█████▌ 11% used\nResets 3pm (Europe/Lisbon)\n\n" +
                           "Current week (all models)\n██ 4% used\nResets Oct 30, 9am (Europe/Lisbon)";
        var now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        var report = UsageParser.ParseResultText(tui, now);

        Assert.Equal(11, report.Session!.Percent);
        Assert.Equal("Current session", report.Session.RawLabel);
        // Lisbon is UTC+0 on Oct 27 (after DST ended Oct 25) → 15:00 UTC today.
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero), report.Session.ResetDate!.Value.ToUniversalTime());
        Assert.Equal(4, report.WeekAll!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero), report.WeekAll.ResetDate!.Value.ToUniversalTime());
    }

    [Fact]
    public void TimeOnlyResetThatAlreadyPassedMeansTomorrow()
    {
        var now = new DateTimeOffset(2026, 10, 27, 16, 0, 0, TimeSpan.Zero);
        var report = UsageParser.ParseResultText("Current session: 50% used · resets 3pm (UTC)", now);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero), report.Session!.ResetDate);
    }

    [Fact]
    public void Accepts24HourClock()
    {
        var report = UsageParser.ParseResultText("Current session: 50% used · resets Jun 24 at 14:30 (UTC)", Now);
        Assert.Equal(new DateTimeOffset(2026, 6, 24, 14, 30, 0, TimeSpan.Zero), report.Session!.ResetDate);
    }

    [Fact]
    public void MissingResetClauseStillYieldsPercent()
    {
        var report = UsageParser.ParseResultText("Current session: 7% used", Now);
        Assert.Equal(7, report.Session!.Percent);
        Assert.Null(report.Session.ResetDate);
    }

    [Fact]
    public void UnknownTimeZoneFallsBackButKeepsDate()
    {
        var report = UsageParser.ParseResultText("Current session: 7% used · resets Jun 24 at 2am (Mars/Olympus)", Now);
        Assert.NotNull(report.Session!.ResetDate);
        Assert.Equal("Mars/Olympus", report.Session.TimeZoneId);
    }

    [Fact]
    public void BodyWithoutLimitLinesIsStaleRenderNotError()
    {
        var ex = Assert.Throws<UsageParseException>(() => UsageParser.Parse(Envelope("Total cost: $0.00\nTotal duration: 1s"), Now));
        Assert.Equal(UsageParseErrorKind.NoMetricsFound, ex.Kind);
        Assert.True(ex.IsStaleRender);
    }

    [Fact]
    public void EmptyOrGarbageIsEmptyResult()
    {
        Assert.Equal(UsageParseErrorKind.EmptyResult, Assert.Throws<UsageParseException>(() => UsageParser.Parse("", Now)).Kind);
        Assert.Equal(UsageParseErrorKind.EmptyResult, Assert.Throws<UsageParseException>(() => UsageParser.Parse("not json", Now)).Kind);
        Assert.Equal(UsageParseErrorKind.EmptyResult, Assert.Throws<UsageParseException>(() => UsageParser.Parse(Envelope("   "), Now)).Kind);
    }

    [Fact]
    public void CliErrorEnvelopeIsSurfaced()
    {
        var ex = Assert.Throws<UsageParseException>(() =>
            UsageParser.Parse(Envelope("Invalid API key · Please run /login", isError: true), Now));
        Assert.Equal(UsageParseErrorKind.CliError, ex.Kind);
        Assert.Contains("/login", ex.Message);
    }

    [Fact]
    public void ToleratesNoiseAroundJson()
    {
        var stdout = "Warning: something noisy\r\n" + Envelope(SampleText) + "\r\n";
        var report = UsageParser.Parse(stdout, Now);
        Assert.Equal(3, report.Metrics.Count);
    }

    [Fact]
    public void ToleratesArrayOfEvents()
    {
        var stdout = JsonSerializer.Serialize(new object[]
        {
            new { type = "system", subtype = "init" },
            new { type = "result", is_error = false, result = SampleText },
        });
        Assert.Equal(3, UsageParser.Parse(stdout, Now).Metrics.Count);
    }

    [Fact]
    public void HandlesWindowsLineEndings()
    {
        var report = UsageParser.ParseResultText(SampleText.Replace("\n", "\r\n"), Now);
        Assert.Equal(3, report.Metrics.Count);
    }

    [Fact]
    public void DstGapPicksFirstValidInstant()
    {
        // Europe/Lisbon springs forward 2026-03-29 01:00 → 02:00.
        var now = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
        var (date, _) = UsageParser.ParseReset("resets Mar 29 at 1:30am (Europe/Lisbon)", now);
        Assert.NotNull(date);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), date!.Value.ToUniversalTime());
    }

    [Fact]
    public void InvalidCalendarDateReturnsNull()
    {
        var (date, tz) = UsageParser.ParseReset("resets Feb 30 at 1am (UTC)", Now);
        Assert.Null(date);
        Assert.Null(tz);
    }
}
