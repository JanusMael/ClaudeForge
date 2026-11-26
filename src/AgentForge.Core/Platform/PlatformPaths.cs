namespace Bennewitz.Ninja.AgentForge.Core.Platform;

/// <summary>
/// Cross-platform resolution of all Claude config file paths.
/// </summary>
/// <remarks>
/// ⓘ <b>Install detection left in plans/00008 step 10</b>: finding the Claude Code binary and
/// the Claude Desktop install is <c>ClaudePaths</c>, in <c>ClaudeForge.Sdk.Claude</c>. The Claude
/// config paths below leave in step 11 with <c>ProfileEngine</c> and <c>ConfigFileDiscoverer</c>,
/// their last neutral readers.
/// </remarks>
public static class PlatformPaths
{
    // Test-only overrides are backed by AsyncLocal, NOT a plain static, so they
    // isolate per logical async flow. AgentForge.Sdk.Tests runs method-level
    // PARALLEL ([assembly: Parallelize]), and 12+ of its fixtures set this override;
    // AsyncLocal keeps each concurrently-running test's sandbox from leaking into
    // another (a plain static races there — ~48 failures/run). In production the
    // value is never set, so the AsyncLocal default (null) is returned and the real
    // OS path is used.
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

    /// <summary>
    /// The user config directory — <c>$CLAUDE_CONFIG_DIR</c> when set, otherwise
    /// <c>~/.claude/</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>A METHOD rather than a property, and the parameter is REQUIRED on purpose.</b>
    /// Every path below derives from this one, so a call site that kept reading a parameterless
    /// property would silently resolve the <i>old</i> tree for any user who set the variable —
    /// no error, no failing test, just an editor reading a different directory than the agent.
    /// Requiring the argument turns all 104 call sites into compile errors, which is the only
    /// mechanism that finds them all. An optional parameter would have found none of them.
    /// </para>
    /// <para>
    /// ⚠ <b>Precedence is fixed by <c>plans/00002</c> step 3 and the order is load-bearing:</b>
    /// <see cref="TestUserProfileOverride"/> first, then
    /// <see cref="ClaudeEnvironment.ResolvedConfigDir"/>, then the default under
    /// <see cref="UserProfile"/>. The test override wins because it is how the suite sandboxes
    /// itself; a real <c>CLAUDE_CONFIG_DIR</c> on the developer's machine must not be able to
    /// pull a sandboxed test back out into their own config.
    /// </para>
    /// <para>
    /// ⛔ <b>This does NOT move managed policy.</b> See <see cref="ManagedSettingsRoot"/>: policy
    /// lives outside the config directory precisely so a user-settable variable cannot escape it.
    /// </para>
    /// </remarks>
    public static string ClaudeHome(ClaudeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        return TestUserProfileOverride is { } sandbox
            ? Path.Combine(sandbox, ".claude")
            : env.ResolvedConfigDir ?? Path.Combine(UserProfile, ".claude");
    }

    /// <summary>settings.json in the resolved home — User scope for Claude Code settings.</summary>
    public static string UserSettingsPath(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), "settings.json");

    /// <summary>mcp.json in the resolved home — User-level MCP server overrides.</summary>
    public static string UserMcpPath(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), "mcp.json");

    /// <summary>
    /// The per-OS SYSTEM directory Claude Code reads enterprise / MDM policy from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>This is NOT under <see cref="ClaudeHome"/>, and believing it was is the defect this
    /// member exists to fix.</b> Every managed path here resolved to <c>~/.claude/</c>, which
    /// Claude Code never reads. It failed in BOTH directions, silently: a machine with policy
    /// deployed showed <b>no managed layer at all</b>, so the effective view told the user their
    /// own value won where policy actually overrode it; and a file the user placed at
    /// <c>~/.claude/managed-settings.json</c> displayed as <b>enforced</b> while doing nothing.
    /// </para>
    /// <para>
    /// ⛔ <b>A user-settable variable must never move this.</b> <c>CLAUDE_CONFIG_DIR</c> relocates
    /// the config directory; policy lives outside it precisely so exporting a variable cannot
    /// escape policy. See <see cref="ClaudeEnvironment"/>.
    /// </para>
    /// <para>
    /// ⚠ <b>The legacy Windows location is deliberately not read.</b>
    /// <c>C:\ProgramData\ClaudeCode\managed-settings.json</c> is explicitly not a location Claude
    /// Code consults, so reading it would reintroduce the confidently-wrong display in a new place.
    /// </para>
    /// <para>
    /// ⚠ <b>Windows resolves through <c>SpecialFolder.ProgramFiles</c>, not a hardcoded
    /// <c>C:\</c></b>, because the drive and the folder's display name are both installable
    /// choices. The literal is only the fallback for when that lookup returns nothing — which is
    /// exactly what happens when <c>--windows</c> emulates Windows on a non-Windows host, since
    /// emulation flips the branch but the host's <see cref="Environment.SpecialFolder"/> lookups
    /// still answer for the real OS.
    /// </para>
    /// </remarks>
    public static string ManagedSettingsRoot
    {
        get
        {
            if (PlatformInfo.Current.IsWindows)
            {
                string programFiles =
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                return string.IsNullOrEmpty(programFiles)
                    ? Path.Combine("C:", "Program Files", "ClaudeCode")
                    : Path.Combine(programFiles, "ClaudeCode");
            }

            if (PlatformInfo.Current.IsMacOS)
            {
                return Path.Combine("/", "Library", "Application Support", "ClaudeCode");
            }

            // Linux and WSL.
            return Path.Combine("/", "etc", "claude-code");
        }
    }

    /// <summary>
    /// <c>managed-settings.json</c> in the system policy directory — the managed (enterprise/MDM)
    /// policy file. See <see cref="ManagedSettingsRoot"/> for why this is not under the home.
    /// </summary>
    public static string ManagedSettingsPath =>
        Path.Combine(ManagedSettingsRoot, "managed-settings.json");

    /// <summary>
    /// <c>managed-settings.d/</c> in the system policy directory — the drop-in policy directory.
    /// </summary>
    public static string ManagedSettingsDropInDir =>
        Path.Combine(ManagedSettingsRoot, "managed-settings.d");

    /// <summary>
    /// <c>managed-mcp.json</c> in the system policy directory — managed MCP server policy.
    /// </summary>
    /// <remarks>
    /// ⚠ Previously not handled anywhere in the product, in any location. Added here so the path
    /// exists and is named; wiring it into discovery is its own change.
    /// </remarks>
    public static string ManagedMcpPath =>
        Path.Combine(ManagedSettingsRoot, "managed-mcp.json");

    /// <summary><c>profiles/</c> in the resolved home — Named profile directories.</summary>
    public static string ProfilesDirectory(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), "profiles");


    /// <summary>
    /// <c>claude-backups/</c> beside the resolved home — default location for backup
    /// <c>.zip</c> archives written by the Backup / Restore feature.
    /// <para>
    /// Stored <em>next to</em> the home directory rather than inside it so that
    /// a catastrophic loss of the config directory (accidental deletion, failed uninstall,
    /// etc.) does not simultaneously destroy the backups. The engine also explicitly
    /// skips any <c>backups/</c> subdirectory it encounters inside the home, so even
    /// if the user points the directory picker back into it, the output
    /// folder is never recursively included in subsequent archives.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>A SIBLING of the resolved home, computed rather than fixed.</b> When
    /// <c>CLAUDE_CONFIG_DIR</c> is unset this is exactly the old <c>~/claude-backups</c>; when it
    /// is set, the backups follow. A hardcoded path under <see cref="UserProfile"/> would break
    /// the "next to" rationale above the moment the home moved, and would write into the user's
    /// real profile from a session that had relocated everything else out of it.
    /// </para>
    /// <para>
    /// ⚠ The fallback covers a home with no parent (a filesystem root), which
    /// <see cref="Directory.GetParent(string)"/> reports as <see langword="null"/>. It is not
    /// reachable from the default layout, but a hand-set <c>CLAUDE_CONFIG_DIR</c> can produce it
    /// and a null dereference in a path accessor would take down startup.
    /// </para>
    /// </remarks>
    public static string DefaultBackupDirectory(ClaudeEnvironment env)
    {
        string home = ClaudeHome(env);
        string? parent = Directory.GetParent(home)?.FullName;

        return Path.Combine(parent ?? UserProfile, "claude-backups");
    }

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

    /// <summary><c>.credentials.json</c> in the resolved home — Claude Code credentials
    /// (Windows/Linux). On macOS this file does not exist; credentials live in Keychain.</summary>
    public static string CredentialsPath(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), ".credentials.json");

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

    /// <summary><c>CLAUDE.md</c> in the resolved home — global instruction file for Claude Code.</summary>
    public static string ClaudeMdPath(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), "CLAUDE.md");

    /// <summary>
    /// <c>.claudectx-current</c> in the resolved home — plain-text file containing the name of the
    /// active profile (written by both claudectx CLI and ClaudeForge). Absent when
    /// no profile has ever been activated via Apply.
    /// </summary>
    public static string CurrentProfileFilePath(ClaudeEnvironment env) =>
        Path.Combine(ClaudeHome(env), ".claudectx-current");

    /// <summary>Profile settings.json for the given profile name.</summary>
    public static string ProfileSettingsPath(ClaudeEnvironment env, string profileName)
    {
        return Path.Combine(ProfilesDirectory(env), profileName, "settings.json");
    }

    /// <summary>Profile CLAUDE.md for the given profile name.</summary>
    public static string ProfileClaudeMdPath(ClaudeEnvironment env, string profileName)
    {
        return Path.Combine(ProfilesDirectory(env), profileName, "CLAUDE.md");
    }

    /// <summary>Profile mcp.json for the given profile name.</summary>
    public static string ProfileMcpPath(ClaudeEnvironment env, string profileName)
    {
        return Path.Combine(ProfilesDirectory(env), profileName, "mcp.json");
    }

    /// <summary>All profile names (subdirectory names under the resolved home's profiles/).</summary>
    public static IReadOnlyList<string> DiscoverProfiles(ClaudeEnvironment env)
    {
        string profilesDirectory = ProfilesDirectory(env);
        if (!Directory.Exists(profilesDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetDirectories(profilesDirectory)
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