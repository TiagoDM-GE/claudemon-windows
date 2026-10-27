using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudemonWin.Core;

public enum FetchErrorKind { ClaudeNotFound, NotSignedIn, LaunchFailed, TimedOut, NonZeroExit, EmptyOutput }

public sealed class UsageFetchException(FetchErrorKind kind, string message, int? exitCode = null, string? stderr = null)
    : Exception(message)
{
    public FetchErrorKind Kind { get; } = kind;
    public int? ExitCode { get; } = exitCode;
    public string? Stderr { get; } = stderr;
}

/// <summary>
/// Runs <c>claude -p "/usage" --output-format json</c> without a console window
/// and returns stdout. Async, cancellable, with a hard timeout that kills the
/// whole process tree (important when claude is a .cmd shim behind cmd.exe).
/// </summary>
public sealed class UsageFetcher(ClaudeLocator locator, string workingDirectory)
{
    public static readonly string[] UsageArguments = ["-p", "/usage", "--output-format", "json"];

    private readonly object _gate = new();
    private string? _cachedPath;

    public string? ResolveClaudePath()
    {
        lock (_gate)
        {
            if (_cachedPath is not null && File.Exists(_cachedPath)) return _cachedPath;
            _cachedPath = locator.Locate();
            return _cachedPath;
        }
    }

    /// <summary>Forget the cached path so the next poll re-discovers (e.g. after an nvm switch).</summary>
    public void InvalidateCache()
    {
        lock (_gate) _cachedPath = null;
    }

    /// <summary>
    /// Builds the start info. Pure apart from reading the current environment, so
    /// the command line can be unit-tested.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string claudePath, string workingDirectory, string? comSpec = null)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,          // no console flashing every minute
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,   // closed right away so `-p` never waits on stdin
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = workingDirectory,
        };

        var ext = Path.GetExtension(claudePath);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // Batch shims (npm, pnpm, nvm) can only be run through cmd.exe.
            // /d skips AutoRun, /s + outer quotes keep the inner quoting intact.
            psi.FileName = comSpec ?? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.Arguments = $"/d /s /c \"\"{claudePath}\" {string.Join(' ', UsageArguments)}\"";
        }
        else
        {
            psi.FileName = claudePath;
            foreach (var a in UsageArguments) psi.ArgumentList.Add(a);
        }

        ApplyEnvironment(psi.Environment);
        return psi;
    }

    /// <summary>
    /// Make sure the identity variables claude relies on exist even when we were
    /// started by the Run key / Task Scheduler with a trimmed environment, and keep
    /// the per-minute background calls from triggering self-updates or telemetry.
    /// </summary>
    public static void ApplyEnvironment(IDictionary<string, string?> env)
    {
        static void Ensure(IDictionary<string, string?> e, string key, string value)
        {
            if (!e.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) e[key] = value;
        }

        Ensure(env, "USERPROFILE", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Ensure(env, "USERNAME", Environment.UserName);
        Ensure(env, "APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Ensure(env, "LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        env["DISABLE_AUTOUPDATER"] = "1";
        env["NO_COLOR"] = "1";
    }

    public async Task<string> FetchUsageJsonAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var claudePath = ResolveClaudePath()
            ?? throw new UsageFetchException(FetchErrorKind.ClaudeNotFound, "Claude Code isn't installed");

        Directory.CreateDirectory(workingDirectory);
        using var process = new Process { StartInfo = BuildStartInfo(claudePath, workingDirectory) };

        try
        {
            if (!process.Start())
                throw new UsageFetchException(FetchErrorKind.LaunchFailed, "Couldn't launch claude");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            InvalidateCache(); // stale path (e.g. node version switch) → self-heal next poll
            throw new UsageFetchException(FetchErrorKind.LaunchFailed, $"Couldn't launch claude: {ex.Message}");
        }

        try { process.StandardInput.Close(); } catch (IOException) { /* already gone */ }

        // Drain both pipes concurrently so a full buffer can never block the child.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new UsageFetchException(FetchErrorKind.TimedOut, "Claude CLI timed out");
        }

        // A grandchild that inherited the pipes (cmd.exe chains, hooks, MCP helpers) can keep
        // them open after claude exits — bound the reads by the same timeout.
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new UsageFetchException(FetchErrorKind.TimedOut, "Claude CLI timed out");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            if (ClaudeDiagnostics.LooksLikeAuthFailure(stderr) || ClaudeDiagnostics.LooksLikeAuthFailure(stdout))
                throw new UsageFetchException(FetchErrorKind.NotSignedIn, "Sign in to Claude Code", process.ExitCode, stderr);

            var detail = FirstMeaningfulLine(stderr) ?? FirstMeaningfulLine(stdout);
            throw new UsageFetchException(FetchErrorKind.NonZeroExit,
                $"claude exited ({process.ExitCode})" + (detail is null ? "" : $": {detail}"),
                process.ExitCode, stderr);
        }

        if (string.IsNullOrWhiteSpace(stdout))
            throw new UsageFetchException(FetchErrorKind.EmptyOutput, "Couldn't read usage");

        return stdout;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception) { /* best effort */ }
    }

    private static string? FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null) return null;
        return line.Length > 160 ? line[..160] + "…" : line;
    }
}
