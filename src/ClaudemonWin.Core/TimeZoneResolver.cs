namespace ClaudemonWin.Core;

/// <summary>
/// Resolves the IANA zone names the CLI prints ("Europe/Lisbon") on any OS.
/// .NET 6+ accepts IANA ids on Windows when ICU is present (Windows 10 1703+);
/// the explicit IANA→Windows conversion is a belt-and-braces fallback.
/// </summary>
public static class TimeZoneResolver
{
    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();

        if (TryFind(id) is { } direct) return direct;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TryFind(windowsId) is { } viaWindows)
            return viaWindows;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) && TryFind(ianaId) is { } viaIana)
            return viaIana;

        return null;
    }

    public static TimeZoneInfo FindOrLocal(string? id) => Find(id) ?? TimeZoneInfo.Local;

    private static TimeZoneInfo? TryFind(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}
