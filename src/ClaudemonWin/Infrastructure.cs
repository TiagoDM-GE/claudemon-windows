using System.Diagnostics;
using System.Runtime.InteropServices;
using ClaudemonWin.Core;
using Microsoft.Win32;

namespace ClaudemonWin;

/// <summary>Where the app keeps its files (all per-user, no admin rights needed).</summary>
internal static class AppPaths
{
    public const string AppName = "ClaudemonWin";

    public static string RoamingDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LocalDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public static string AlertStateFile => Path.Combine(LocalDir, "alert-state.json");
    public static string CacheFile => Path.Combine(LocalDir, "usage-cache.json");
    public static string LogFile => Path.Combine(LocalDir, "claudemon.log");

    /// <summary>Neutral working dir for the CLI so it never scans the user's projects.</summary>
    public static string WorkDir => Path.Combine(LocalDir, "work");

    public static void OpenDataFolder()
    {
        Directory.CreateDirectory(LocalDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LocalDir}\"") { UseShellExecute = true });
    }
}

/// <summary>Minimal rolling text log, handy because the app has no console.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LocalDir);
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(file.FullName, file.FullName + ".1", overwrite: true);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}

internal enum TrayStyle { Ring, Percent, Bars }

/// <summary>Panel/floating-window theme: follow Windows, or force light/dark.</summary>
internal enum ThemeMode { System, Light, Dark }

/// <summary>User settings, persisted as JSON in %APPDATA%\ClaudemonWin.</summary>
internal sealed class AppSettings
{
    public TrayStyle TrayStyle { get; set; } = TrayStyle.Ring;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public UsageKind BarsWeekSource { get; set; } = UsageKind.WeekAll;
    public bool FloatingEnabled { get; set; }
    public bool FloatingCompact { get; set; }
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }
    public bool PreciseBars { get; set; }
    public bool AlertsEnabled { get; set; } = true;
    public bool AlertQuarter { get; set; } = true;
    public bool AlertLow { get; set; } = true;
    public bool AlertDepleted { get; set; } = true;
    public bool AlertReset { get; set; } = true;
    public bool FirstRunHintShown { get; set; }

    public static AppSettings Load() => JsonStore.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();

    public void Save()
    {
        if (!JsonStore.Write(AppPaths.SettingsFile, this)) Log.Error("Couldn't save settings");
    }

    public IReadOnlySet<UsageAlertThreshold> EnabledThresholds()
    {
        var set = new HashSet<UsageAlertThreshold>();
        if (AlertQuarter) set.Add(UsageAlertThreshold.Quarter);
        if (AlertLow) set.Add(UsageAlertThreshold.Low);
        if (AlertDepleted) set.Add(UsageAlertThreshold.Depleted);
        return set;
    }
}

/// <summary>"Start with Windows" via the per-user Run key (no admin, no scheduled task).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager → Startup "Disabled" lives here (first byte odd = disabled).
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(AppPaths.AppName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(AppPaths.AppName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(AppPaths.AppName, Command);
                // Clear a "Disabled" flag set from Task Manager so the toggle really takes effect.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(AppPaths.AppName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(AppPaths.AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't update Run key", ex);
        }
    }

    /// <summary>If the exe moved since autostart was enabled, point the Run key at the new path.</summary>
    public static void RepairPathIfNeeded()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(AppPaths.AppName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(AppPaths.AppName, Command);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't repair Run key", ex);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    /// <summary>Windows 11 rounded corners for borderless windows (no-op on Windows 10).</summary>
    public static void TryRoundCorners(IntPtr hwnd, bool small = false)
    {
        try
        {
            int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const int SM_CXSMICON = 49;

    /// <summary>
    /// Small-icon size at the primary monitor's CURRENT scale. SystemInformation.SmallIconSize
    /// is frozen at process start, which blurs the tray icon after a live scale change.
    /// </summary>
    public static int CurrentSmallIconSize()
    {
        try
        {
            var monitor = MonitorFromPoint(Point.Empty, MONITOR_DEFAULTTOPRIMARY);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0)
            {
                int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
                if (size > 0) return size;
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return SystemInformation.SmallIconSize.Width;
    }

    public const int CS_DROPSHADOW = 0x00020000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
}
