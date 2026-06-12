using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Bennewitz.Ninja.ClaudeForge.Core.Platform;

/// <summary>
/// Cross-platform resolution of all Claude config file paths.
/// </summary>
public static class PlatformPaths
{
    // Test-only overrides are backed by AsyncLocal, NOT a plain static, so they
    // isolate per logical async flow. Under MSTest parallelization each test runs
    // in its own execution-context flow, so one test's sandbox override never
    // leaks into a concurrently-running test (the contamination that made the
    // suite unsafe to parallelize). In production the value is never set, so the
    // AsyncLocal default (null) is returned and the real OS path is used.
    private static readonly AsyncLocal<string?> s_testUserProfileOverride = new();
    private static readonly AsyncLocal<string?> s_testAppBaseDirOverride = new();

    /// <summary>
    /// Test-only override for the user-profile root. When non-null, every path derived
    /// from the user profile (<see cref="ClaudeHome"/>, <see cref="ClaudeJsonPath"/>,
    /// <see cref="DesktopConfigPath"/>, etc.) resolves against this instead of the OS
    /// user profile — lets tests point the engine at a sandboxed scratch dir without
    /// touching real Claude data. Left <c>null</c> in production. Backed by
    /// <see cref="AsyncLocal{T}"/> so concurrent (parallelized) tests stay isolated.
    /// </summary>
    public static string? TestUserProfileOverride
    {
        get => s_testUserProfileOverride.Value;
        set => s_testUserProfileOverride.Value = value;
    }

    /// <summary>
    /// User-profile root directory (<c>%USERPROFILE%</c> on Windows,
    /// <c>$HOME</c> on macOS / Linux). Honors <see cref="TestUserProfileOverride"/>
    /// when set. Public so callers that need to format display-friendly
    /// <c>~/...</c> paths (e.g. the SaveChangesDialog destination-path
    /// resolver) can detect when an absolute path falls under the home
    /// directory without reaching for raw <see cref="Environment"/> calls
    /// that would bypass the test override.
    /// </summary>
    public static string UserProfile =>
        TestUserProfileOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>~/.claude/</summary>
    public static string ClaudeHome => Path.Combine(UserProfile, ".claude");

    /// <summary>~/.claude/settings.json — User scope for Claude Code settings.</summary>
    public static string UserSettingsPath =>
        Path.Combine(ClaudeHome, "settings.json");

    /// <summary>~/.claude/mcp.json — User-level MCP server overrides.</summary>
    public static string UserMcpPath =>
        Path.Combine(ClaudeHome, "mcp.json");

    /// <summary>~/.claude/managed-settings.json — Managed (enterprise/MDM) policy file.</summary>
    public static string ManagedSettingsPath =>
        Path.Combine(ClaudeHome, "managed-settings.json");

    /// <summary>~/.claude/managed-settings.d/ — Drop-in managed settings directory.</summary>
    public static string ManagedSettingsDropInDir =>
        Path.Combine(ClaudeHome, "managed-settings.d");

    /// <summary>~/.claude/profiles/ — Named profile directories.</summary>
    public static string ProfilesDirectory =>
        Path.Combine(ClaudeHome, "profiles");

    /// <summary>~/.claude/cache/schemas/ — Disk cache for fetched JSON schemas.</summary>
    public static string SchemaCacheDirectory =>
        Path.Combine(ClaudeHome, "cache", "schemas");

    /// <summary>
    /// ~/claude-backups/ — Default location for backup <c>.zip</c> archives written
    /// by the Backup / Restore feature.
    /// <para>
    /// Stored <em>next to</em> <see cref="ClaudeHome"/> rather than inside it so that
    /// a catastrophic loss of <c>~/.claude/</c> (accidental deletion, failed uninstall,
    /// etc.) does not simultaneously destroy the backups. The engine also explicitly
    /// skips any <c>backups/</c> subdirectory it encounters inside ClaudeHome, so even
    /// if the user points the directory picker back into <c>~/.claude/</c>, the output
    /// folder is never recursively included in subsequent archives.
    /// </para>
    /// </summary>
    public static string DefaultBackupDirectory =>
        Path.Combine(UserProfile, "claude-backups");

    /// <summary>
    /// Claude Desktop log directory (platform-specific).
    /// Windows: <c>%LOCALAPPDATA%\Claude\logs</c>
    /// macOS:   <c>~/Library/Logs/Claude</c>
    /// Linux:   (none — standard Claude Desktop has no persistent log dir on Linux)
    /// </summary>
    public static string? DesktopLogsPath
    {
        get
        {
            // Use PlatformInfo.Current rather than RuntimeInformation directly so
            // the --windows / --macos / --linux debug flags can emulate a different
            // platform's path layout for UI testing without rebooting into that OS.
            // The host's Environment.SpecialFolder lookups still resolve against
            // the real OS — emulation only flips the branch selected here.
            if (PlatformInfo.Current.IsWindows)
            {
                string local = TestUserProfileOverride != null
                    ? Path.Combine(TestUserProfileOverride, "AppData", "Local")
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Claude", "logs");
            }

            if (PlatformInfo.Current.IsMacOS)
            {
                return Path.Combine(UserProfile, "Library", "Logs", "Claude");
            }

            return null;
        }
    }

    /// <summary>
    /// Test-only override for the application base directory used by
    /// <see cref="AppLogsDirectory"/>. When non-null, <c>AppLogsDirectory</c>
    /// resolves to <c>&lt;TestAppBaseDirOverride&gt;/logs</c> instead of the
    /// directory that contains the running executable. Left <c>null</c> in production.
    /// Backed by <see cref="AsyncLocal{T}"/> (see <see cref="TestUserProfileOverride"/>)
    /// so concurrent (parallelized) tests stay isolated.
    /// </summary>
    internal static string? TestAppBaseDirOverride
    {
        get => s_testAppBaseDirOverride.Value;
        set => s_testAppBaseDirOverride.Value = value;
    }

    /// <summary>
    /// Directory for ClaudeForge's own rolling log files (written by the Serilog
    /// pipeline in <c>Program.Main</c>). Always resolves to a <c>logs/</c> subdirectory
    /// next to the running executable so log files are immediately visible beside the
    /// application without hunting through OS-specific user-data directories.
    /// Distinct from <see cref="DesktopLogsPath"/>, which points at Anthropic's
    /// Claude Desktop app logs. Directory creation is the caller's responsibility.
    /// </summary>
    public static string AppLogsDirectory
    {
        get
        {
            string baseDir = TestAppBaseDirOverride
                             ?? Path.GetDirectoryName(Environment.ProcessPath)
                             ?? AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs");
        }
    }

    /// <summary>~/.claude.json — Claude Code global config file (home dir, not inside .claude/).</summary>
    public static string ClaudeJsonPath => Path.Combine(UserProfile, ".claude.json");

    /// <summary>~/.claude/.credentials.json — Claude Code credentials (Windows/Linux).
    /// On macOS this file does not exist; credentials live in Keychain.</summary>
    public static string CredentialsPath =>
        Path.Combine(ClaudeHome, ".credentials.json");

    /// <summary>
    /// Short platform identifier written into backup manifests:
    /// <c>"windows"</c>, <c>"macos"</c>, or <c>"linux"</c>. Delegates to
    /// <see cref="PlatformInfo.Current"/> so the <c>--windows</c> /
    /// <c>--macos</c> / <c>--linux</c> debug flags can produce manifests
    /// tagged with the emulated platform for testing.
    /// </summary>
    public static string PlatformId => PlatformInfo.Current.PlatformId;

    /// <summary>
    /// Parent directory that contains the Desktop config file and (when profiles are used)
    /// the <c>profiles/</c> subdirectory.
    /// Windows: %APPDATA%\Claude\
    /// macOS:   ~/Library/Application Support/Claude/
    /// Linux:   ~/.config/Claude/
    /// </summary>
    public static string DesktopConfigDir => Path.GetDirectoryName(DesktopConfigPath)!;

    /// <summary>Named Desktop profile directories, stored alongside the Desktop config.</summary>
    public static string DesktopProfilesDirectory => Path.Combine(DesktopConfigDir, "profiles");

    /// <summary>
    /// Plain-text file containing the name of the currently-active Desktop profile.
    /// Absent or empty means no Desktop profile is active (the live config is used directly).
    /// </summary>
    public static string DesktopCurrentProfileFilePath => Path.Combine(DesktopConfigDir, ".desktop-current");

    /// <summary>The <c>claude_desktop_config.json</c> snapshot inside a named Desktop profile.</summary>
    public static string DesktopProfileConfigPath(string profileName)
    {
        return Path.Combine(DesktopProfilesDirectory, profileName, "claude_desktop_config.json");
    }

    /// <summary>
    /// Claude Desktop config file path, platform-specific.
    /// Windows: %APPDATA%\Claude\claude_desktop_config.json
    /// macOS:   ~/Library/Application Support/Claude/claude_desktop_config.json
    /// Linux:   ~/.config/Claude/claude_desktop_config.json
    /// </summary>
    public static string DesktopConfigPath
    {
        get
        {
            // Use PlatformInfo.Current rather than RuntimeInformation directly so
            // the --windows / --macos / --linux debug flags can emulate a different
            // platform's path layout for UI testing.
            if (PlatformInfo.Current.IsWindows)
            {
                // Windows: APPDATA lives alongside UserProfile. When the tests override
                // the profile root, fall back to <UserProfile>\AppData\Roaming so the
                // entire sandbox stays inside the override.
                string appData = TestUserProfileOverride != null
                    ? Path.Combine(TestUserProfileOverride, "AppData", "Roaming")
                    : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Claude", "claude_desktop_config.json");
            }

            if (PlatformInfo.Current.IsMacOS)
            {
                return Path.Combine(UserProfile, "Library", "Application Support", "Claude",
                    "claude_desktop_config.json");
            }

            // Linux
            string xdgConfig = TestUserProfileOverride is null
                ? (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                   ?? Path.Combine(UserProfile, ".config"))
                : Path.Combine(UserProfile, ".config");
            return Path.Combine(xdgConfig, "Claude", "claude_desktop_config.json");
        }
    }

    /// <summary>.claude/settings.json in the given project root (Project scope).</summary>
    public static string ProjectSettingsPath(string projectRoot)
    {
        return Path.Combine(projectRoot, ".claude", "settings.json");
    }

    /// <summary>.claude/settings.local.json in the given project root (Local scope).</summary>
    public static string LocalSettingsPath(string projectRoot)
    {
        return Path.Combine(projectRoot, ".claude", "settings.local.json");
    }

    /// <summary>.mcp.json in the given project root (project-level MCP).</summary>
    public static string ProjectMcpPath(string projectRoot)
    {
        return Path.Combine(projectRoot, ".mcp.json");
    }

    /// <summary>~/.claude/CLAUDE.md — global instruction file for Claude Code.</summary>
    public static string ClaudeMdPath =>
        Path.Combine(ClaudeHome, "CLAUDE.md");

    /// <summary>
    /// ~/.claude/.claudectx-current — plain-text file containing the name of the
    /// active profile (written by both claudectx CLI and ClaudeForge). Absent when
    /// no profile has ever been activated via Apply.
    /// </summary>
    public static string CurrentProfileFilePath =>
        Path.Combine(ClaudeHome, ".claudectx-current");

    /// <summary>Profile settings.json for the given profile name.</summary>
    public static string ProfileSettingsPath(string profileName)
    {
        return Path.Combine(ProfilesDirectory, profileName, "settings.json");
    }

    /// <summary>Profile CLAUDE.md for the given profile name.</summary>
    public static string ProfileClaudeMdPath(string profileName)
    {
        return Path.Combine(ProfilesDirectory, profileName, "CLAUDE.md");
    }

    /// <summary>Profile mcp.json for the given profile name.</summary>
    public static string ProfileMcpPath(string profileName)
    {
        return Path.Combine(ProfilesDirectory, profileName, "mcp.json");
    }

    /// <summary>
    /// Result of <see cref="TryFindClaudeCodeBinary"/>. <see cref="IsOnPath"/>
    /// distinguishes the "binary reachable via bare-name PATH lookup" case from
    /// the "binary exists on disk at a canonical install location but the
    /// user's PATH was never updated" case — the latter is the
    /// Windows-ARM64-with-npm-global scenario where
    /// <c>%APPDATA%\npm\claude.cmd</c> is present but <c>%APPDATA%\npm</c> is
    /// not on PATH out of the box.
    /// </summary>
    public sealed record ClaudeCodeLocation(string BinaryPath, bool IsOnPath);

    /// <summary>
    /// Probes PATH first, then an ordered, platform-specific list of canonical
    /// install locations for the Claude Code CLI. Returns <see langword="null"/>
    /// if nothing is found.
    ///
    /// <para>
    /// Probed locations:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>claude</c> (any extension on Windows) anywhere on PATH.</item>
    ///   <item>All platforms: <c>~/.claude/local/claude(.exe|.cmd)</c> — the
    ///         self-contained install directory used by recent installers.</item>
    ///   <item>Windows: <c>%APPDATA%\npm\claude.cmd</c> (+ .ps1, no-ext
    ///         variants) — npm's default global prefix, which is frequently
    ///         missing from PATH on freshly-installed machines.</item>
    ///   <item>Windows: <c>%LOCALAPPDATA%\Programs\claude\claude.exe</c> —
    ///         future-proofing for a native standalone installer.</item>
    ///   <item>Unix: <c>~/.local/bin/claude</c>, <c>~/.npm-global/bin/claude</c>.</item>
    ///   <item>macOS: <c>/opt/homebrew/bin/claude</c> (Apple Silicon).</item>
    ///   <item>macOS + Linux: <c>/usr/local/bin/claude</c>.</item>
    /// </list>
    /// <para>
    /// Result is cached for the lifetime of the process — install state
    /// rarely changes mid-session, and the call is invoked several times per
    /// profile switch (About page, version probe, install banner). Callers
    /// that mutate the in-process PATH (the About page's "Add to PATH"
    /// command) must invoke <see cref="InvalidatePathCache"/> to clear it.
    /// </para>
    /// </summary>
    public static ClaudeCodeLocation? TryFindClaudeCodeBinary()
    {
        // Volatile.Read pairs with the Volatile.Write in InvalidatePathCache.
        // _claudeCodeLocationCacheValid is the gate; the value field is read
        // only when the gate is true.
        if (_claudeCodeLocationCacheValid)
        {
            return _claudeCodeLocationCache;
        }

        ClaudeCodeLocation? result = TryFindClaudeCodeBinaryUncached();
        _claudeCodeLocationCache = result;
        _claudeCodeLocationCacheValid = true;
        return result;
    }

    private static volatile bool _claudeCodeLocationCacheValid;
    private static ClaudeCodeLocation? _claudeCodeLocationCache;

    private static ClaudeCodeLocation? TryFindClaudeCodeBinaryUncached()
    {
        string? pathHit = FindFirstOnPath("claude");
        if (pathHit != null)
        {
            return new ClaudeCodeLocation(pathHit, IsOnPath: true);
        }

        foreach (string candidate in CanonicalClaudeCodeCandidates())
        {
            if (File.Exists(candidate))
            {
                return new ClaudeCodeLocation(candidate, IsOnPath: false);
            }
        }

        return null;
    }

    /// <summary>
    /// Ordered set of absolute candidate paths for the Claude Code binary on
    /// the current platform. Order matters: earlier entries win when multiple
    /// installs coexist. Self-contained <c>~/.claude/local/</c> comes first
    /// because it is the most specific signal of an intentional install.
    /// </summary>
    private static IEnumerable<string> CanonicalClaudeCodeCandidates()
    {
        string localDir = Path.Combine(ClaudeHome, "local");
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(localDir, "claude.exe");
            yield return Path.Combine(localDir, "claude.cmd");

            // npm-global default prefix. This is the common Windows-ARM64 case:
            // `npm install -g @anthropic-ai/claude-code` drops claude.cmd here
            // but %APPDATA%\npm is not on PATH until the user adds it.
            string appData = TestUserProfileOverride != null
                ? Path.Combine(TestUserProfileOverride, "AppData", "Roaming")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "npm", "claude.cmd");
            yield return Path.Combine(appData, "npm", "claude.ps1");
            yield return Path.Combine(appData, "npm", "claude");

            // Future-proofing for a standalone installer (e.g.
            // %LOCALAPPDATA%\Programs\claude\claude.exe — mirrors how VS Code,
            // GitHub Desktop, and similar tools install per-user on Windows).
            string localAppData = TestUserProfileOverride != null
                ? Path.Combine(TestUserProfileOverride, "AppData", "Local")
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Programs", "claude", "claude.exe");
        }
        else
        {
            // Self-contained installer / local npm prefix.
            yield return Path.Combine(localDir, "claude");
            yield return Path.Combine(UserProfile, ".local", "bin", "claude"); // `curl | bash` installer default
            yield return Path.Combine(UserProfile, ".npm-global", "bin", "claude");

            // Volta-managed npm binaries.
            yield return Path.Combine(UserProfile, ".volta", "bin", "claude");

            if (OperatingSystem.IsMacOS())
            {
                yield return "/opt/homebrew/bin/claude"; // Apple Silicon Homebrew
                yield return "/usr/local/bin/claude"; // Intel Homebrew / manual install
            }
            else
            {
                // Linux: system-level installs (apt, rpm, snap, manual).
                yield return "/usr/local/bin/claude";
                yield return "/usr/bin/claude";
                yield return "/snap/bin/claude"; // snap install
            }
        }
    }

    /// <summary>
    /// Returns true when the Claude Code CLI is reachable via the current
    /// process's PATH. Distinct from <see cref="IsClaudeCodeInstalled"/>,
    /// which also returns <see langword="true"/> when the binary is present
    /// at a canonical install location but PATH has not been updated.
    /// </summary>
    public static bool IsClaudeCodeOnPath => FindFirstOnPath("claude") != null;

    /// <summary>
    /// Returns true when the Claude Code CLI appears to be installed on this machine.
    /// Detection order:
    /// 1. Anywhere on the current PATH (via <see cref="TryFindClaudeCodeBinary"/>).
    /// 2. At any canonical disk location known to <see cref="TryFindClaudeCodeBinary"/>
    ///    — this catches the "installed but PATH not updated" case, including the
    ///    Windows-ARM64-npm-global scenario.
    /// 3. <c>~/.claude/settings.json</c> exists (CLI was run at least once from
    ///    somewhere we no longer recognise — belt-and-braces fallback).
    /// </summary>
    public static bool IsClaudeCodeInstalled =>
        TryFindClaudeCodeBinary() is not null ||
        File.Exists(UserSettingsPath);

    /// <summary>
    /// Returns true when Claude Desktop appears to be installed on this machine.
    /// Checks for the config file first, then falls back to known <em>application</em>
    /// install directories.
    /// <para>
    /// The previous fallback checked whether the config's parent directory
    /// (<c>%APPDATA%\Claude\</c>) existed, which produced false positives after
    /// uninstall (that directory is typically left behind by the uninstaller) and
    /// after manual config deletion. The application install directories are
    /// authoritative: the installer creates them before first launch and the
    /// uninstaller removes them.
    /// </para>
    /// </summary>
    public static bool IsDesktopInstalled
    {
        get
        {
            // Primary: the config file itself exists (Desktop has been run at least once).
            if (File.Exists(DesktopConfigPath))
            {
                return true;
            }

            // Secondary: check whether the Claude Desktop application is present
            // (installed but never launched). Guard per platform — Desktop only ships
            // for Windows and macOS; Linux has no official Desktop install.
            // Uses PlatformInfo.Current so the --windows / --macos / --linux debug
            // flags participate in emulation (UI-display branch, per PLATFORM.md).
            if (PlatformInfo.Current.IsWindows)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // Squirrel / plain EXE installer writes to %LOCALAPPDATA%\AnthropicClaude\.
                if (Directory.Exists(Path.Combine(local, "AnthropicClaude")))
                {
                    return true;
                }

                // Some builds target %LOCALAPPDATA%\Programs\claude-desktop\.
                if (Directory.Exists(Path.Combine(local, "Programs", "claude-desktop")))
                {
                    return true;
                }

                // MSIX install: per-user package under %LOCALAPPDATA%\Packages\Claude_*\.
                string packages = Path.Combine(local, "Packages");
                if (Directory.Exists(packages) &&
                    Directory.EnumerateDirectories(packages, "Claude_*", SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
            else if (PlatformInfo.Current.IsMacOS)
            {
                // Standard macOS app bundle location.
                if (Directory.Exists("/Applications/Claude.app"))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    // process-lifetime cache for FindFirstOnPath. The bare
    // implementation iterates all PATH dirs × all PATHEXT extensions calling
    // File.Exists — easily 200+ filesystem stats per call on Windows. Rider's
    // monitoring tab flagged it as a hot path on profile switch because
    // BuildNavigationTree → AboutEditorViewModel construction reads
    // IsClaudeCodeOnPath / TryFindClaudeCodeBinary several times.
    //
    // Process-lifetime is the right scope:
    //   • PATH is read from the in-process environment block, which the OS
    //     populates at launch and never refreshes for us. External edits to
    //     HKCU\Environment\Path (e.g., via setx) don't propagate to a running
    //     process — so the cache is correct as long as the process lives.
    //   • Our own "Add to PATH" command writes HKCU\Environment\Path but
    //     intentionally does NOT update the in-process PATH (it asks the
    //     user to relaunch). It still calls InvalidatePathCache() for safety
    //     and for the "already present" branch, where re-probing with the
    //     same env still benefits from a fresh negative cache entry.
    //
    // Sentinel: a missing key means "never probed"; a stored null means
    // "probed and not found". ConcurrentDictionary<string, string?> with
    // GetOrAdd handles both atomically.
    private static readonly ConcurrentDictionary<string, string?> _pathCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discards any cached <see cref="FindFirstOnPath"/> results. Call after
    /// the in-process PATH is mutated (rare — currently only the "Add to
    /// PATH" command on the About page does this). Tests must call this in
    /// <c>TestCleanup</c> after they tweak <c>PATH</c>, or the next test
    /// will see a stale hit/miss.
    /// </summary>
    public static void InvalidatePathCache()
    {
        _pathCache.Clear();
        _claudeCodeLocationCache = null;
        _claudeCodeLocationCacheValid = false;
    }

    /// <summary>
    /// Returns the absolute path of <paramref name="exe"/> if it is found as an
    /// executable on the current PATH, otherwise <see langword="null"/>. On
    /// Windows, all PATHEXT extensions are tried in order (so a
    /// <c>claude.exe</c> wins over a <c>claude.cmd</c> in the same directory).
    /// Results are memoised for the lifetime of the process — see
    /// <see cref="InvalidatePathCache"/>.
    /// </summary>
    private static string? FindFirstOnPath(string exe)
    {
        return _pathCache.GetOrAdd(exe, FindFirstOnPathUncached);
    }

    private static string? FindFirstOnPathUncached(string exe)
    {
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string[] extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""]; // Unix: no extension needed

        foreach (string dir in dirs)
        {
            foreach (string ext in extensions)
            {
                string candidate = Path.Combine(dir, exe + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>All profile names (subdirectory names under ~/.claude/profiles/).</summary>
    public static IReadOnlyList<string> DiscoverProfiles()
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetDirectories(ProfilesDirectory)
                            .Select(Path.GetFileName)
                            .Where(n => n != null)
                            .Cast<string>()
                            .OrderBy(n => n)
                            .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Directory exists but is unreadable (e.g. locked by admin policy).
            return [];
        }
    }

    /// <summary>All Desktop profile names (subdirectory names under the Desktop profiles directory).</summary>
    public static IReadOnlyList<string> DiscoverDesktopProfiles()
    {
        if (!Directory.Exists(DesktopProfilesDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetDirectories(DesktopProfilesDirectory)
                            .Select(Path.GetFileName)
                            .Where(n => n != null)
                            .Cast<string>()
                            .OrderBy(n => n)
                            .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}