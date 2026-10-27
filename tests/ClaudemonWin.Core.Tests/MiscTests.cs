using System.Globalization;
using ClaudemonWin.Core;

namespace ClaudemonWin.Core.Tests;

public class LocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claudemon-locator-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_root, "profile");
    private string AppData => Path.Combine(_root, "appdata");
    private string Local => Path.Combine(_root, "local");

    private void Touch(params string[] parts)
    {
        var path = Path.Combine(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    private ClaudeLocator Make(string? path = null) => new(Profile, AppData, Local, path);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ReturnsNullWhenNothingInstalled() => Assert.Null(Make().Locate());

    [Fact]
    public void FindsNpmShim()
    {
        Touch(AppData, "npm", "claude.cmd");
        Assert.Equal(Path.Combine(AppData, "npm", "claude.cmd"), Make().Locate());
    }

    [Fact]
    public void NativeInstallerWinsOverNpm()
    {
        Touch(AppData, "npm", "claude.cmd");
        Touch(Profile, ".local", "bin", "claude.exe");
        Assert.Equal(Path.Combine(Profile, ".local", "bin", "claude.exe"), Make().Locate());
    }

    [Fact]
    public void ExeWinsOverCmdInSameFolder()
    {
        Touch(Local, "pnpm", "claude.cmd");
        Touch(Local, "pnpm", "claude.exe");
        Assert.Equal(Path.Combine(Local, "pnpm", "claude.exe"), Make().Locate());
    }

    [Fact]
    public void IgnoresExtensionlessShellShim()
    {
        Touch(AppData, "npm", "claude"); // the sh script npm also drops
        Assert.Null(Make().Locate());
    }

    [Fact]
    public void FindsNewestNvmVersion()
    {
        Touch(AppData, "nvm", "v18.20.0", "claude.cmd");
        Touch(AppData, "nvm", "v22.11.0", "claude.cmd");
        Assert.Equal(Path.Combine(AppData, "nvm", "v22.11.0", "claude.cmd"), Make().Locate());
    }

    [Fact]
    public void FallsBackToPath()
    {
        var custom = Path.Combine(_root, "tools");
        Touch(custom, "claude.exe");
        Assert.Equal(Path.Combine(custom, "claude.exe"), Make($"C:\\nope;\"{custom}\";").Locate());
    }
}

public class FetcherTests
{
    [Fact]
    public void ExeIsLaunchedDirectlyWithArgumentList()
    {
        var psi = UsageFetcher.BuildStartInfo(@"C:\Users\me\.local\bin\claude.exe", @"C:\work");
        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe", psi.FileName);
        Assert.Equal(new[] { "-p", "/usage", "--output-format", "json" }, psi.ArgumentList.ToArray());
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardInput);
        Assert.Equal(@"C:\work", psi.WorkingDirectory);
    }

    [Fact]
    public void CmdShimGoesThroughCmdExeWithSafeQuoting()
    {
        var psi = UsageFetcher.BuildStartInfo(@"C:\Users\Jo Doe\AppData\Roaming\npm\claude.cmd", @"C:\work", comSpec: @"C:\Windows\system32\cmd.exe");
        Assert.Equal(@"C:\Windows\system32\cmd.exe", psi.FileName);
        Assert.Equal("/d /s /c \"\"C:\\Users\\Jo Doe\\AppData\\Roaming\\npm\\claude.cmd\" -p /usage --output-format json\"", psi.Arguments);
    }

    [Fact]
    public void EnvironmentGetsIdentityAndQuietFlags()
    {
        var env = new Dictionary<string, string?> { ["USERNAME"] = "", ["PATH"] = "x" };
        UsageFetcher.ApplyEnvironment(env);
        Assert.False(string.IsNullOrEmpty(env["USERNAME"]));
        Assert.False(string.IsNullOrEmpty(env["USERPROFILE"]));
        Assert.Equal("1", env["DISABLE_AUTOUPDATER"]);
        Assert.Equal("x", env["PATH"]);
    }

    [Fact]
    public async Task MissingClaudeIsReportedAsNotInstalled()
    {
        var empty = Path.Combine(Path.GetTempPath(), "claudemon-empty-" + Guid.NewGuid().ToString("N"));
        var fetcher = new UsageFetcher(new ClaudeLocator(empty, empty, empty, null), Path.GetTempPath());
        var ex = await Assert.ThrowsAsync<UsageFetchException>(() => fetcher.FetchUsageJsonAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(FetchErrorKind.ClaudeNotFound, ex.Kind);
    }
}

public class FormattingAndDiagnosticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CountdownFormats()
    {
        Assert.Null(UsageFormatting.Countdown(null, Now));
        Assert.Equal("now", UsageFormatting.Countdown(Now.AddSeconds(-5), Now));
        Assert.Equal("42m", UsageFormatting.Countdown(Now.AddMinutes(42.5), Now));
        Assert.Equal("3h 12m", UsageFormatting.Countdown(Now.AddMinutes(192), Now));
        Assert.Equal("4d 6h", UsageFormatting.Countdown(Now.AddHours(102).AddMinutes(30), Now));
    }

    [Fact]
    public void AbsoluteResetUsesMetricZone()
    {
        var instant = new DateTimeOffset(2026, 6, 23, 23, 49, 0, TimeSpan.Zero);
        var text = UsageFormatting.AbsoluteReset(instant, "Europe/Istanbul", CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("Jun 24, 2:49 AM", text);
    }

    [Fact]
    public void BandsMatchOriginal()
    {
        Assert.Equal(UsageBand.Ok, UsageBands.For(59));
        Assert.Equal(UsageBand.Warning, UsageBands.For(60));
        Assert.Equal(UsageBand.Warning, UsageBands.For(85));
        Assert.Equal(UsageBand.Critical, UsageBands.For(86));
    }

    [Fact]
    public void AuthHeuristic()
    {
        Assert.True(ClaudeDiagnostics.LooksLikeAuthFailure("Invalid API key · Please run /login"));
        Assert.True(ClaudeDiagnostics.LooksLikeAuthFailure("OAuth token has expired"));
        Assert.False(ClaudeDiagnostics.LooksLikeAuthFailure("Error: ENOENT logging failed"));
        Assert.False(ClaudeDiagnostics.LooksLikeAuthFailure(null));
    }

    [Fact]
    public void ReportRoundTripsThroughJsonStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "claudemon-cache-" + Guid.NewGuid().ToString("N") + ".json");
        var report = new UsageReport(
            [new UsageMetric(UsageKind.WeekModel, "Current week (Fable)", 12, Now.AddDays(2), "Europe/Lisbon")], Now);
        Assert.True(JsonStore.Write(path, report));
        var back = JsonStore.Read<UsageReport>(path);
        File.Delete(path);

        Assert.NotNull(back);
        Assert.Equal(report.Metrics.Single(), back!.Metrics.Single());
        Assert.Equal(Now, back.CapturedAt);
        Assert.Equal("Fable", back.WeekModel!.ModelName);
    }

    [Fact]
    public void AlertStateRoundTripsThroughJsonStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "claudemon-alerts-" + Guid.NewGuid().ToString("N") + ".json");
        var state = new Dictionary<UsageKind, ThresholdWindowState>
        {
            [UsageKind.WeekModel] = new() { Signature = "h123", Fired = [25, 5] },
        };
        Assert.True(JsonStore.Write(path, state));
        var back = JsonStore.Read<Dictionary<UsageKind, ThresholdWindowState>>(path);
        File.Delete(path);

        Assert.NotNull(back);
        Assert.Equal("h123", back![UsageKind.WeekModel].Signature);
        Assert.True(back[UsageKind.WeekModel].Fired.SetEquals([25, 5]));
    }
}
