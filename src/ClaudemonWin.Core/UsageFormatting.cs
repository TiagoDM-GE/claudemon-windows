using System.Globalization;

namespace ClaudemonWin.Core;

public static class UsageFormatting
{
    /// <summary>
    /// Compact countdown: "now", "42m", "3h 12m" or, for weekly windows, "4d 6h".
    /// Returns null when there is no reset date.
    /// </summary>
    public static string? Countdown(DateTimeOffset? resetDate, DateTimeOffset now)
    {
        if (resetDate is null) return null;
        var span = resetDate.Value - now;
        if (span <= TimeSpan.Zero) return "now";

        int totalMinutes = (int)span.TotalMinutes;
        int days = totalMinutes / (60 * 24);
        int hours = totalMinutes / 60 % 24;
        int minutes = totalMinutes % 60;

        if (days > 0) return $"{days}d {hours}h";
        if (totalMinutes >= 60) return $"{totalMinutes / 60}h {minutes}m";
        return $"{minutes}m";
    }

    /// <summary>Absolute reset time in the metric's own zone, e.g. "Oct 27, 5:00 PM".</summary>
    public static string? AbsoluteReset(DateTimeOffset? resetDate, string? timeZoneId, CultureInfo? culture = null)
    {
        if (resetDate is null) return null;
        culture ??= CultureInfo.CurrentCulture;
        var tz = TimeZoneResolver.FindOrLocal(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(resetDate.Value, tz);
        // Short month + day + short time in the user's culture (24h in pt-PT, 12h in en-US).
        var pattern = culture.DateTimeFormat.MonthDayPattern.Contains("MMMM")
            ? culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM")
            : culture.DateTimeFormat.MonthDayPattern;
        var text = local.ToString(pattern, culture) + ", " + local.ToString(culture.DateTimeFormat.ShortTimePattern, culture);
        // ICU emits narrow/no-break spaces ("2:49 AM"); normalise for GDI fonts and tooltips.
        return text.Replace(' ', ' ').Replace(' ', ' ');
    }

    /// <summary>"HH:mm" in the local zone, used for "updated 14:32".</summary>
    public static string ShortClock(DateTimeOffset value) =>
        value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
