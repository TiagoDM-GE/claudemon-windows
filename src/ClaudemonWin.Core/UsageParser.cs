using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudemonWin.Core;

public enum UsageParseErrorKind
{
    /// <summary>No JSON / no <c>.result</c> / empty body — a real failure.</summary>
    EmptyResult,
    /// <summary>
    /// A real body without the "Current session/week" lines. NOT a failure: the
    /// CLI refreshes those lines on its own ~1 minute cadence and returns a fast
    /// local-only body in between. Keep showing last-good data.
    /// </summary>
    NoMetricsFound,
    /// <summary>The CLI reported an error inside the JSON envelope (<c>is_error: true</c>).</summary>
    CliError,
}

public sealed class UsageParseException(UsageParseErrorKind kind, string message) : Exception(message)
{
    public UsageParseErrorKind Kind { get; } = kind;
    public bool IsStaleRender => Kind == UsageParseErrorKind.NoMetricsFound;
}

/// <summary>
/// Tolerant parser for the text inside <c>claude -p "/usage" --output-format json</c>.
/// Expected lines (order and wording tolerant):
/// <code>
/// Current session: 28% used · resets Jun 24 at 2:49am (Europe/Istanbul)
/// Current week (all models): 29% used · resets Jun 26 at 9:59am (Europe/Istanbul)
/// Current week (Fable): 2% used · resets Jun 26 at 10am (Europe/Istanbul)
/// </code>
/// Also accepts the multi-line TUI layout ("Current session" / "28% used" /
/// "Resets 3pm (Europe/Lisbon)"), times without a date, and 24h clocks.
/// </summary>
public static partial class UsageParser
{
    private const int MaxContinuationLines = 2;

    /// <summary>Parse the outer JSON envelope, extract <c>.result</c>, then parse the metrics.</summary>
    public static UsageReport Parse(string stdout, DateTimeOffset now)
    {
        var (result, isError) = ExtractResult(stdout);
        if (result is null)
            throw new UsageParseException(UsageParseErrorKind.EmptyResult, "The usage output was empty.");

        try
        {
            return ParseResultText(result, now);
        }
        catch (UsageParseException ex) when (ex.IsStaleRender && (isError || LooksLikeLoginPrompt(result)))
        {
            // No metrics AND the CLI flagged an error → surface it (e.g. "Invalid API key · Please run /login").
            throw new UsageParseException(UsageParseErrorKind.CliError, result.Trim());
        }
    }

    /// <summary>Parse the human-readable <c>.result</c> text directly.</summary>
    public static UsageReport ParseResultText(string resultText, DateTimeOffset now)
    {
        var trimmed = resultText?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new UsageParseException(UsageParseErrorKind.EmptyResult, "The usage output was empty.");

        var lines = trimmed.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToArray();
        var metrics = new List<UsageMetric>();

        for (int i = 0; i < lines.Length; i++)
        {
            var header = lines[i];
            var kind = Classify(header);
            if (kind is null) continue;
            if (metrics.Any(m => m.Kind == kind)) continue; // first match wins

            // Single-line format has everything on the header. The TUI layout
            // splits it over the next lines — glue up to two of them on.
            var block = header;
            for (int j = 1; j <= MaxContinuationLines && i + j < lines.Length; j++)
            {
                if (PercentRegex().IsMatch(block) && ResetWordRegex().IsMatch(block)) break;
                var next = lines[i + j];
                if (next.Length == 0 || Classify(next) is not null || IsHeader(next)) break;
                block += " " + next;
            }

            if (ParseMetric(header, block, kind.Value, now) is { } metric)
                metrics.Add(metric);
        }

        if (metrics.Count == 0)
            throw new UsageParseException(UsageParseErrorKind.NoMetricsFound, "No new usage data this cycle.");

        metrics.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return new UsageReport(metrics, now);
    }

    // ---------------------------------------------------------------- envelope

    private static (string? Result, bool IsError) ExtractResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return (null, false);

        if (TryReadEnvelope(stdout.Trim(), out var r, out var e)) return (r, e);

        // Tolerate warnings printed before/after the JSON: try each line that
        // looks like an object, last one first (the result is printed last).
        foreach (var line in stdout.Split('\n').Select(l => l.Trim()).Reverse())
        {
            if (line.StartsWith('{') && TryReadEnvelope(line, out r, out e)) return (r, e);
        }
        // ...or a multi-line object starting at the first '{'.
        int brace = stdout.IndexOf('{');
        if (brace > 0 && TryReadEnvelope(stdout[brace..].Trim(), out r, out e)) return (r, e);

        return (null, false);
    }

    private static bool TryReadEnvelope(string json, out string? result, out bool isError)
    {
        result = null;
        isError = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // `--output-format json` may emit either a single result object or an array of events.
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray().Reverse())
                {
                    if (item.ValueKind == JsonValueKind.Object && ReadResultObject(item, out result, out isError))
                        return true;
                }
                return false;
            }
            return root.ValueKind == JsonValueKind.Object && ReadResultObject(root, out result, out isError);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReadResultObject(JsonElement obj, out string? result, out bool isError)
    {
        result = null;
        isError = obj.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True;
        if (obj.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.String)
        {
            result = res.GetString();
            return true;
        }
        return false;
    }

    private static bool LooksLikeLoginPrompt(string text)
    {
        var s = text.ToLowerInvariant();
        return s.Contains("/login") || s.Contains("invalid api key") || s.Contains("not logged in");
    }

    // ---------------------------------------------------------- classification

    private static bool IsHeader(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.StartsWith("current ") || lower.StartsWith("extra usage") || lower.EndsWith(':');
    }

    /// <summary>
    /// Any "week" line that is not "all models" is the per-model limit — the
    /// model name (Sonnet, Fable, …) is read from the text, never hardcoded.
    /// </summary>
    internal static UsageKind? Classify(string line)
    {
        var lower = line.ToLowerInvariant();
        bool usageLine = lower.Contains("used") || lower.Contains("reset");
        bool headerOnly = lower.StartsWith("current session") || lower.StartsWith("current week");
        if (!usageLine && !headerOnly) return null;

        if (lower.Contains("session")) return UsageKind.Session;
        if (lower.Contains("week"))
            return lower.Contains("all models") ? UsageKind.WeekAll : UsageKind.WeekModel;
        return null;
    }

    // ----------------------------------------------------------------- parsing

    private static UsageMetric? ParseMetric(string header, string block, UsageKind kind, DateTimeOffset now)
    {
        var percentMatch = PercentRegex().Match(block);
        if (!percentMatch.Success) return null;
        int percent = Math.Clamp(int.Parse(percentMatch.Groups[1].Value), 0, 100);

        var (resetDate, tzId) = ParseReset(block, now);
        return new UsageMetric(kind, ParseLabel(header, kind), percent, resetDate, tzId);
    }

    private static string ParseLabel(string header, UsageKind kind)
    {
        int colon = header.IndexOf(':');
        if (colon > 0) return header[..colon].Trim();
        // TUI header line ("Current week (Fable)") has no colon and no digits.
        if (!header.Any(char.IsDigit)) return header.Trim();
        return kind.DisplayName();
    }

    /// <summary>"resets Jun 24 at 2:49am (Europe/Istanbul)" → (instant, zone).</summary>
    internal static (DateTimeOffset? Date, string? TimeZoneId) ParseReset(string text, DateTimeOffset now)
    {
        var withDate = ResetWithDateRegex().Match(text);
        if (withDate.Success && MonthNumber(withDate.Groups["month"].Value) is int month)
        {
            var tzId = NullIfEmpty(withDate.Groups["tz"].Value);
            if (!TryHour(withDate, out int hour, out int minute)) return (null, null);
            var tz = TimeZoneResolver.FindOrLocal(tzId);
            int day = int.Parse(withDate.Groups["day"].Value);

            int year = TimeZoneInfo.ConvertTime(now, tz).Year;
            var date = Build(year, month, day, hour, minute, tz);
            if (date is not null && date < now) date = Build(year + 1, month, day, hour, minute, tz) ?? date;
            return date is null ? (null, null) : (date, tzId);
        }

        var timeOnly = ResetTimeOnlyRegex().Match(text);
        if (timeOnly.Success && (timeOnly.Groups["ampm"].Success || timeOnly.Groups["min"].Success))
        {
            var tzId = NullIfEmpty(timeOnly.Groups["tz"].Value);
            if (!TryHour(timeOnly, out int hour, out int minute)) return (null, null);
            var tz = TimeZoneResolver.FindOrLocal(tzId);
            var today = TimeZoneInfo.ConvertTime(now, tz).Date;
            var date = Build(today.Year, today.Month, today.Day, hour, minute, tz);
            if (date is not null && date <= now)
            {
                var tomorrow = today.AddDays(1);
                date = Build(tomorrow.Year, tomorrow.Month, tomorrow.Day, hour, minute, tz);
            }
            return date is null ? (null, null) : (date, tzId);
        }

        return (null, null);
    }

    private static bool TryHour(Match m, out int hour, out int minute)
    {
        hour = int.Parse(m.Groups["hour"].Value);
        minute = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value) : 0;
        var ampm = m.Groups["ampm"].Success ? m.Groups["ampm"].Value.Replace(".", "").ToLowerInvariant() : null;

        if (ampm is not null)
        {
            if (hour is < 1 or > 12) return false;
            if (ampm == "pm" && hour != 12) hour += 12;
            if (ampm == "am" && hour == 12) hour = 0;
        }
        return hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }

    private static DateTimeOffset? Build(int year, int month, int day, int hour, int minute, TimeZoneInfo tz)
    {
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1); // DST gap → first valid instant
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    private static int? MonthNumber(string raw)
    {
        if (raw.Length < 3) return null;
        return raw[..3].ToLowerInvariant() switch
        {
            "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6,
            "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, "dec" => 12,
            _ => null,
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"\bresets?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResetWordRegex();

    [GeneratedRegex(
        @"resets?\s+(?:on\s+)?(?<month>[A-Za-z]{3,})\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s+(?:at\s+)?(?<hour>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ampm>[ap]\.?m\.?)?(?:\s*\((?<tz>[^)]+)\))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex ResetWithDateRegex();

    [GeneratedRegex(
        @"resets?\s+(?:at\s+)?(?<hour>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ampm>[ap]\.?m\.?)?(?:\s*\((?<tz>[^)]+)\))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex ResetTimeOnlyRegex();
}
