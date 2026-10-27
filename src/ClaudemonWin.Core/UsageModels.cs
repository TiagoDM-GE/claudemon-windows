using System.Text.Json.Serialization;

namespace ClaudemonWin.Core;

/// <summary>The three limits the Claude Code CLI reports in <c>/usage</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UsageKind>))]
public enum UsageKind
{
    /// <summary>Current session (rolling 5h window).</summary>
    Session,
    /// <summary>Current week, all models.</summary>
    WeekAll,
    /// <summary>
    /// Current week for one specific model. Which model (Sonnet, Fable, ...) is
    /// decided by Anthropic and only appears in <see cref="UsageMetric.RawLabel"/>.
    /// Never hardcode a model name.
    /// </summary>
    WeekModel,
}

public static class UsageKindExtensions
{
    public static string DisplayName(this UsageKind kind) => kind switch
    {
        UsageKind.Session => "Current Session (5h)",
        UsageKind.WeekAll => "Current Week (all models)",
        _ => "Current Week (per model)",
    };

    public static string ShortName(this UsageKind kind) => kind switch
    {
        UsageKind.Session => "Session",
        UsageKind.WeekAll => "Week",
        _ => "Model",
    };
}

/// <summary>A single usage line parsed from <c>/usage</c>.</summary>
/// <param name="Kind">Which limit this is.</param>
/// <param name="RawLabel">Label as it appeared in the CLI text (best effort).</param>
/// <param name="Percent">Percent used, clamped to 0..100.</param>
/// <param name="ResetDate">Absolute reset instant, if it could be computed.</param>
/// <param name="TimeZoneId">IANA zone from the text (e.g. "Europe/Lisbon"), if any.</param>
public sealed record UsageMetric(
    UsageKind Kind,
    string RawLabel,
    int Percent,
    DateTimeOffset? ResetDate,
    string? TimeZoneId)
{
    /// <summary>Remaining quota percentage (100 - used, never negative).</summary>
    [JsonIgnore]
    public int Remaining => Math.Max(0, 100 - Percent);

    /// <summary>
    /// Model name for a <see cref="UsageKind.WeekModel"/> metric, taken from the
    /// parenthesised part of the label ("Current week (Fable)" → "Fable").
    /// </summary>
    [JsonIgnore]
    public string ModelName
    {
        get
        {
            if (Kind != UsageKind.WeekModel) return Kind.ShortName();
            int open = RawLabel.IndexOf('(');
            int close = RawLabel.IndexOf(')');
            if (open < 0 || close < 0 || close <= open) return Kind.ShortName();
            var inner = RawLabel[(open + 1)..close].Trim();
            return inner.Length == 0 ? Kind.ShortName() : inner;
        }
    }

    /// <summary>English display label that follows the live model name.</summary>
    [JsonIgnore]
    public string DisplayLabel =>
        Kind == UsageKind.WeekModel ? $"Current Week ({ModelName})" : Kind.DisplayName();
}

/// <summary>One successful poll of the CLI.</summary>
public sealed record UsageReport(IReadOnlyList<UsageMetric> Metrics, DateTimeOffset CapturedAt)
{
    public UsageMetric? Get(UsageKind kind) => Metrics.FirstOrDefault(m => m.Kind == kind);

    [JsonIgnore] public UsageMetric? Session => Get(UsageKind.Session);
    [JsonIgnore] public UsageMetric? WeekAll => Get(UsageKind.WeekAll);
    [JsonIgnore] public UsageMetric? WeekModel => Get(UsageKind.WeekModel);

    /// <summary>Compact per-kind percent snapshot, handy for change detection.</summary>
    public string Fingerprint() =>
        string.Join("|", Metrics.Select(m => $"{m.Kind}:{m.Percent}:{m.ResetDate?.ToUnixTimeSeconds()}"));
}

/// <summary>Usage-band thresholds shared by every surface (green / yellow / red).</summary>
public enum UsageBand { Ok, Warning, Critical }

public static class UsageBands
{
    public static UsageBand For(int percentUsed) => percentUsed switch
    {
        < 60 => UsageBand.Ok,
        < 86 => UsageBand.Warning,
        _ => UsageBand.Critical,
    };
}
