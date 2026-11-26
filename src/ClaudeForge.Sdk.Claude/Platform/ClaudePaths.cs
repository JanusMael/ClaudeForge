using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Platform;

/// <summary>
/// Where Claude Code and Claude Desktop are installed on this machine: the Claude half of what
/// was one <see cref="PlatformPaths"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⓘ <b>plans/00008 step 10 split this out</b>, so the neutral <c>PlatformPaths</c> in
/// <c>AgentForge.Core</c> no longer probes for Claude's binaries or install folders. The config
/// path members (<see cref="PlatformPaths.ClaudeHome"/> and the rest) stay there until step 11,
/// because <c>ProfileEngine</c> and <c>ConfigFileDiscoverer</c> still read them from Core and leave
/// with them.
/// </para>
/// <para>
/// ⚠ <b>Paths still resolve through <see cref="PlatformPaths"/></b>, never through
/// <see cref="Environment"/> directly, so the <see cref="AsyncLocal{T}"/> test override
/// (<see cref="PlatformPaths.TestUserProfileOverride"/>) sandboxes these probes as it did before
/// the split.
/// </para>
/// </remarks>
public static class ClaudePaths
{

    private static readonly AsyncLocal<bool> s_testSuppressClaudeCodeBinaryProbe = new();

    /// <summary>
    /// Test-only switch that makes <see cref="TryFindClaudeCodeBinary"/> and
    /// <see cref="IsClaudeCodeOnPath"/> report "not found" without probing.
    /// The PATH probe reads the real process environment and the system-wide
    /// entries in <c>CanonicalClaudeCodeCandidates</c> are absolute paths, so
    /// neither is redirected by <see cref="PlatformPaths.TestUserProfileOverride"/> — a
    /// developer machine with the CLI installed would otherwise leak into any
    /// test of the "Claude Code not detected" state. Checked ahead of the
    /// process-lifetime caches, so enabling it never records a stale miss and
    /// <see cref="InvalidatePathCache"/> is not needed afterwards. Left
    /// <see langword="false"/> in production. Backed by <see cref="AsyncLocal{T}"/>
    /// (see <see cref="PlatformPaths.TestUserProfileOverride"/>) so concurrent (parallelized)
    /// tests stay isolated.
    /// </summary>
    internal static bool TestSuppressClaudeCodeBinaryProbe
    {
        get => s_testSuppressClaudeCodeBinaryProbe.Value;
        set => s_testSuppressClaudeCodeBinaryProbe.Value = value;
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
    /// Tests that need the "not installed" state on a machine with the CLI
    /// present set <see cref="TestSuppressClaudeCodeBinaryProbe"/> instead.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The cache is keyed on nothing, including not on <paramref name="env"/>.</b> That is
    /// unchanged behaviour, not a regression the parameter introduced: it was already blind to the
    /// <see cref="AsyncLocal{T}"/> sandbox override, which moves the same candidate paths. It is
    /// sound because a process resolves exactly one environment at composition and never a second.
    /// A caller that genuinely needs a different one must call <see cref="InvalidatePathCache"/>,
    /// exactly as the About page's "Add to PATH" command already does.
    /// </remarks>
    public static ClaudeCodeLocation? TryFindClaudeCodeBinary(ClaudeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (TestSuppressClaudeCodeBinaryProbe)
        {
            return null;
        }

        // Volatile.Read pairs with the Volatile.Write in InvalidatePathCache.
        // _claudeCodeLocationCacheValid is the gate; the value field is read
        // only when the gate is true.
        if (_claudeCodeLocationCacheValid)
        {
            return _claudeCodeLocationCache;
        }

        ClaudeCodeLocation? result = TryFindClaudeCodeBinaryUncached(env);
        _claudeCodeLocationCache = result;
        _claudeCodeLocationCacheValid = true;
        return result;
    }

    private static volatile bool _claudeCodeLocationCacheValid;
    private static ClaudeCodeLocation? _claudeCodeLocationCache;

    private static ClaudeCodeLocation? TryFindClaudeCodeBinaryUncached(ClaudeEnvironment env)
    {
        string? pathHit = FindFirstOnPath("claude");
        if (pathHit != null)
        {
            return new ClaudeCodeLocation(pathHit, IsOnPath: true);
        }

        foreach (string candidate in CanonicalClaudeCodeCandidates(env))
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
    private static IEnumerable<string> CanonicalClaudeCodeCandidates(ClaudeEnvironment env)
    {
        string localDir = Path.Combine(PlatformPaths.ClaudeHome(env), "local");
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(localDir, "claude.exe");
            yield return Path.Combine(localDir, "claude.cmd");

            // npm-global default prefix. This is the common Windows-ARM64 case:
            // `npm install -g @anthropic-ai/claude-code` drops claude.cmd here
            // but %APPDATA%\npm is not on PATH until the user adds it.
            string appData = PlatformPaths.TestUserProfileOverride != null
                ? Path.Combine(PlatformPaths.TestUserProfileOverride, "AppData", "Roaming")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "npm", "claude.cmd");
            yield return Path.Combine(appData, "npm", "claude.ps1");
            yield return Path.Combine(appData, "npm", "claude");

            // Future-proofing for a standalone installer (e.g.
            // %LOCALAPPDATA%\Programs\claude\claude.exe — mirrors how VS Code,
            // GitHub Desktop, and similar tools install per-user on Windows).
            string localAppData = PlatformPaths.TestUserProfileOverride != null
                ? Path.Combine(PlatformPaths.TestUserProfileOverride, "AppData", "Local")
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Programs", "claude", "claude.exe");
        }
        else
        {
            // Self-contained installer / local npm prefix.
            yield return Path.Combine(localDir, "claude");
            yield return Path.Combine(PlatformPaths.UserProfile, ".local", "bin", "claude"); // `curl | bash` installer default
            yield return Path.Combine(PlatformPaths.UserProfile, ".npm-global", "bin", "claude");

            // Volta-managed npm binaries.
            yield return Path.Combine(PlatformPaths.UserProfile, ".volta", "bin", "claude");

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
    public static bool IsClaudeCodeOnPath =>
        !TestSuppressClaudeCodeBinaryProbe && FindFirstOnPath("claude") != null;

    /// <summary>
    /// Returns true when the Claude Code CLI appears to be installed on this machine.
    /// Detection order:
    /// 1. Anywhere on the current PATH (via <see cref="TryFindClaudeCodeBinary"/>).
    /// 2. At any canonical disk location known to <see cref="TryFindClaudeCodeBinary"/>
    ///    — this catches the "installed but PATH not updated" case, including the
    ///    Windows-ARM64-npm-global scenario.
    /// 3. <c>settings.json</c> exists in the resolved home (CLI was run at least once from
    ///    somewhere we no longer recognise — belt-and-braces fallback).
    /// </summary>
    /// <remarks>
    /// ⓘ <b><see cref="IsClaudeCodeOnPath"/> deliberately takes no environment</b>, because it asks
    /// only about <c>PATH</c>. This one consults the resolved home for its third signal, so it does.
    /// </remarks>
    public static bool IsClaudeCodeInstalled(ClaudeEnvironment env) =>
        TryFindClaudeCodeBinary(env) is not null ||
        File.Exists(PlatformPaths.UserSettingsPath(env));

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
            if (File.Exists(PlatformPaths.DesktopConfigPath))
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
            else if (PlatformInfo.Current.IsMacOS && Directory.Exists("/Applications/Claude.app"))
            {
                // Standard macOS app bundle location.
                return true;
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
            foreach (string candidate in extensions.Select(ext => Path.Combine(dir, exe + ext)))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
