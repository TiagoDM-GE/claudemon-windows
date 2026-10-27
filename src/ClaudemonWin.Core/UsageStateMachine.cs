namespace ClaudemonWin.Core;

public enum UsageStatus
{
    /// <summary>First load in progress, no data yet.</summary>
    Loading,
    /// <summary>Fresh data this tick.</summary>
    Loaded,
    /// <summary>Last-good kept; the CLI returned no new limit lines this cycle (NOT an error).</summary>
    StaleRender,
    /// <summary>Last-good kept; a real failure happened.</summary>
    StaleError,
    /// <summary>No data and a real error.</summary>
    Error,
}

public enum DiagnosticKind { None, NotInstalled, NotSignedIn, Other }

/// <summary>
/// UI-agnostic state machine behind the tray app (port of Claudemon's UsageStore
/// logic). The app owns the timers and calls these transitions; every rule about
/// what counts as an error, when to keep last-good data and when to poll sooner
/// lives here so it can be unit-tested.
/// </summary>
public sealed class UsageStateMachine
{
    public const int MaxColdNoMetrics = 5;
    public static readonly TimeSpan MinColdEscalationDelay = TimeSpan.FromSeconds(120);
    public const int MaxAcceleratedPolls = 2;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private int _consecutiveColdNoMetrics;
    private int _acceleratedPollsRemaining;
    private DateTimeOffset? _startedAt;

    public UsageStatus Status { get; private set; } = UsageStatus.Loading;
    public UsageReport? LastGood { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DiagnosticKind Diagnostic { get; private set; } = DiagnosticKind.None;
    /// <summary>Capture time of the data currently shown.</summary>
    public DateTimeOffset? LastUpdated { get; private set; }

    public bool HasData => LastGood is not null;

    public void MarkStarted(DateTimeOffset now) => _startedAt ??= now;

    /// <summary>Seed last-good data from disk on launch (only while still loading).</summary>
    public bool Prime(UsageReport cached)
    {
        if (Status != UsageStatus.Loading || LastGood is not null) return false;
        LastGood = cached;
        Status = UsageStatus.StaleRender;
        LastUpdated = cached.CapturedAt; // honest capture time, not "now"
        return true;
    }

    public void ApplySuccess(UsageReport report)
    {
        _acceleratedPollsRemaining = 0;
        _consecutiveColdNoMetrics = 0;
        LastGood = report;
        Status = UsageStatus.Loaded;
        ErrorMessage = null;
        Diagnostic = DiagnosticKind.None;
        LastUpdated = report.CapturedAt;
    }

    /// <summary>
    /// The CLI answered but without limit lines. Keeps last-good data calmly.
    /// Returns true when the caller should schedule one sooner follow-up poll.
    /// </summary>
    public bool ApplyStaleRender(DateTimeOffset now)
    {
        if (LastGood is not null)
        {
            Diagnostic = DiagnosticKind.None;
            ErrorMessage = null;
            _consecutiveColdNoMetrics = 0;
            Status = UsageStatus.StaleRender;
        }
        else
        {
            // Cold start: stay calm, but don't hang on "waiting…" forever.
            _consecutiveColdNoMetrics++;
            var elapsed = _startedAt is { } s ? now - s : TimeSpan.Zero;
            if (_consecutiveColdNoMetrics >= MaxColdNoMetrics && elapsed >= MinColdEscalationDelay)
            {
                Diagnostic = DiagnosticKind.Other;
                ErrorMessage = "Couldn't read usage. Try Refresh, or restart the app.";
                Status = UsageStatus.Error;
            }
            else
            {
                Diagnostic = DiagnosticKind.None;
                ErrorMessage = null;
                Status = UsageStatus.Loading;
            }
        }

        return NextAcceleratedFollowUp();
    }

    public void ApplyFailure(Exception error)
    {
        _consecutiveColdNoMetrics = 0;
        var (kind, message) = Classify(error);
        Diagnostic = kind;
        ErrorMessage = message;
        Status = LastGood is not null ? UsageStatus.StaleError : UsageStatus.Error;
    }

    /// <summary>Call when polling stops (sleep / quit) so counters don't leak across cycles.</summary>
    public void ResetCounters()
    {
        _acceleratedPollsRemaining = 0;
        _consecutiveColdNoMetrics = 0;
    }

    public static (DiagnosticKind Kind, string Message) Classify(Exception error) => error switch
    {
        UsageFetchException { Kind: FetchErrorKind.ClaudeNotFound } f => (DiagnosticKind.NotInstalled, f.Message),
        UsageFetchException { Kind: FetchErrorKind.NotSignedIn } f => (DiagnosticKind.NotSignedIn, f.Message),
        UsageParseException { Kind: UsageParseErrorKind.CliError } p when ClaudeDiagnostics.LooksLikeAuthFailure(p.Message)
            => (DiagnosticKind.NotSignedIn, p.Message),
        _ => (DiagnosticKind.Other, error.Message),
    };

    /// <summary>"updated 14:32" plus "· stale" when older than 10 minutes. Never alarming.</summary>
    public bool IsDataOld(DateTimeOffset now) => LastUpdated is { } u && now - u > StaleAfter;

    // After a miss, poll once sooner (~28s) to catch the CLI's next refresh,
    // capped to a couple in a row; a hit resets the counter.
    private bool NextAcceleratedFollowUp()
    {
        if (_acceleratedPollsRemaining == 0)
        {
            _acceleratedPollsRemaining = MaxAcceleratedPolls;
            return true;
        }
        _acceleratedPollsRemaining--;
        return _acceleratedPollsRemaining > 0;
    }
}
