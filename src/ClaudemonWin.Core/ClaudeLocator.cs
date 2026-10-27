namespace ClaudemonWin.Core;

/// <summary>
/// Finds the <c>claude</c> executable on Windows across the common install layouts.
/// Pure filesystem checks (no processes) so it is unit-testable with a temp tree.
/// Order: well-known locations → version-manager folders (nvm-windows, fnm) → PATH.
/// Within a location, <c>.exe</c> wins over <c>.cmd</c> (a .cmd needs cmd.exe).
/// </summary>
public sealed class ClaudeLocator
{
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat"];

    private readonly string _userProfile;
    private readonly string _appData;
    private readonly string _localAppData;
    private readonly string? _pathEnv;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, IEnumerable<string>> _listDirectories;

    public ClaudeLocator(
        string userProfile,
        string appData,
        string localAppData,
        string? pathEnv,
        Func<string, bool>? fileExists = null,
        Func<string, IEnumerable<string>>? listDirectories = null)
    {
        _userProfile = userProfile;
        _appData = appData;
        _localAppData = localAppData;
        _pathEnv = pathEnv;
        _fileExists = fileExists ?? File.Exists;
        _listDirectories = listDirectories ?? SafeListDirectories;
    }

    public static ClaudeLocator CreateDefault() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("PATH"));

    /// <summary>Well-known install directories, in priority order.</summary>
    public IReadOnlyList<string> CandidateDirectories =>
    [
        Path.Combine(_userProfile, ".local", "bin"),                 // official native installer
        Path.Combine(_userProfile, ".claude", "local"),              // legacy local installer
        Path.Combine(_appData, "npm"),                               // npm install -g
        Path.Combine(_localAppData, "Microsoft", "WinGet", "Links"), // winget
        Path.Combine(_userProfile, "scoop", "shims"),                // scoop
        Path.Combine(_localAppData, "pnpm"),                         // pnpm
        Path.Combine(_localAppData, "Volta", "bin"),                 // volta
        Path.Combine(_userProfile, ".bun", "bin"),                   // bun
    ];

    /// <summary>Resolve the full path to claude, or null if not found.</summary>
    public string? Locate()
    {
        foreach (var dir in CandidateDirectories)
            if (FindIn(dir) is { } hit) return hit;

        if (FindInVersionManagers() is { } vm) return vm;

        return FindOnPath();
    }

    public string? FindOnPath()
    {
        if (string.IsNullOrWhiteSpace(_pathEnv)) return null;
        foreach (var raw in _pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dir = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
            if (FindIn(dir) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>nvm-windows: %APPDATA%\nvm\vX.Y.Z\claude.cmd; fnm: …\node-versions\vX\installation\claude.cmd.</summary>
    public string? FindInVersionManagers()
    {
        var nvmRoots = new[] { Environment.GetEnvironmentVariable("NVM_HOME"), Path.Combine(_appData, "nvm") };
        foreach (var root in nvmRoots.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            foreach (var versionDir in _listDirectories(root!).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                if (FindIn(versionDir) is { } hit) return hit;
        }

        var fnmRoots = new[]
        {
            Environment.GetEnvironmentVariable("FNM_DIR"),
            Path.Combine(_appData, "fnm"),
            Path.Combine(_localAppData, "fnm"),
        };
        foreach (var root in fnmRoots.Where(r => !string.IsNullOrEmpty(r)).Distinct())
        {
            var versions = Path.Combine(root!, "node-versions");
            foreach (var versionDir in _listDirectories(versions).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                if (FindIn(Path.Combine(versionDir, "installation")) is { } hit) return hit;
        }
        return null;
    }

    private string? FindIn(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        foreach (var ext in Extensions)
        {
            var candidate = Path.Combine(directory, "claude" + ext);
            if (_fileExists(candidate)) return candidate;
        }
        return null;
    }

    private static IEnumerable<string> SafeListDirectories(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetDirectories(path) : []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
