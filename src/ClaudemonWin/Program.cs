namespace ClaudemonWin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance per user session.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ClaudemonWin.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        // High-DPI mode (PerMonitorV2) comes from the csproj via ApplicationConfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
