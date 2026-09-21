using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

/// <summary>
/// Discovers all Claude config files at all scope levels.
/// </summary>
public static class ConfigFileDiscoverer
{
    /// <summary>
    /// Returns all scoped Claude Code settings files (Managed through Local).
    /// Files that don't yet exist are included — they can be created on first save.
    /// </summary>
    /// <param name="projectRoot">
    ///   Optional project root directory. When provided, Project and Local scope files are included.
    /// </param>
    /// <param name="profileName">
    ///   Optional named profile. When provided, the profile-specific
    ///   <c>~/.claude/profiles/&lt;name&gt;/settings.json</c> is loaded at User scope
    ///   <em>instead of</em> the global <c>~/.claude/settings.json</c>.
    ///   This mirrors how the Claude Code CLI behaves with <c>--profile &lt;name&gt;</c>.
    /// </param>
    /// <param name="managedRoot">
    ///   The directory managed (enterprise / MDM) policy is read from. Defaults to
    ///   <see cref="PlatformPaths.ManagedSettingsRoot"/>, which is the per-OS system directory.
    ///   <para>
    ///   ⛔ <b>A seam for tests, and it has to exist.</b> The real location is
    ///   <c>C:\Program Files\ClaudeCode\</c> or <c>/etc/claude-code/</c>, both of which need
    ///   elevation to write — so "place a policy file and see it discovered" cannot run on a normal
    ///   CI agent or developer machine. Without an injection point that check would be quietly
    ///   skipped, or quietly run as admin, and neither is evidence.
    ///   </para>
    ///   <para>
    ///   ⚠ <b>A parameter, not ambient state</b>, so it is the same shape as
    ///   <see cref="Platform.ClaudeEnvironment"/> rather than a second mechanism — and a caller
    ///   that forgets it gets the production answer rather than a sandbox.
    ///   </para>
    /// </param>
    public static IReadOnlyList<DiscoveredFile> DiscoverClaudeCodeSettings(
        ClaudeEnvironment env,
        string? projectRoot = null,
        string? profileName = null,
        string? managedRoot = null)
    {
        List<DiscoveredFile> files = [];

        string policyRoot = managedRoot ?? PlatformPaths.ManagedSettingsRoot;
        string managedSettings = Path.Combine(policyRoot, "managed-settings.json");
        string managedDropIn = Path.Combine(policyRoot, "managed-settings.d");

        // Managed scope — read all managed settings files (unaffected by profile)
        if (File.Exists(managedSettings))
        {
            files.Add(Describe(ConfigScope.Managed, ConfigFileType.ClaudeCodeSettings,
                managedSettings, readOnly: true));
        }

        // Managed drop-in directory — skip gracefully if unreadable (e.g. enterprise policy dir).
        if (Directory.Exists(managedDropIn))
        {
            IEnumerable<string> dropIns = [];
            try
            {
                dropIns = Directory.GetFiles(managedDropIn, "*.json").OrderBy(x => x);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Log.Warning(ex, "[Discoverer] Cannot read managed drop-in directory {Dir}",
                    managedDropIn);
            }

            foreach (string f in dropIns)
            {
                files.Add(Describe(ConfigScope.Managed, ConfigFileType.ClaudeCodeSettings, f, readOnly: true));
            }
        }

        // User scope: profile-specific file when a named profile is active,
        // otherwise the global user settings. The two are intentionally mutually
        // exclusive — loading both would create two User-scope entries and cause
        // merge ambiguity in SettingsWorkspace.
        string userPath = string.IsNullOrEmpty(profileName)
            ? PlatformPaths.UserSettingsPath(env)
            : PlatformPaths.ProfileSettingsPath(env, profileName);
        files.Add(Describe(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, userPath, readOnly: false));

        // Project + Local scopes (only when a project root is provided)
        if (projectRoot != null)
        {
            files.Add(Describe(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings,
                PlatformPaths.ProjectSettingsPath(projectRoot), readOnly: false));
            files.Add(Describe(ConfigScope.Local, ConfigFileType.ClaudeCodeSettings,
                PlatformPaths.LocalSettingsPath(projectRoot), readOnly: false));
        }

        return files;
    }

    /// <summary>
    /// Returns the Claude Desktop config file descriptor.
    /// </summary>
    /// <param name="profileName">
    ///   Optional named Desktop profile.  When provided, the profile-specific
    ///   <c>claude_desktop_config.json</c> is loaded instead of the live config.
    ///   Pass <c>null</c> (the default) to load the live Desktop config.
    /// </param>
    public static DiscoveredFile DiscoverDesktopConfig(string? profileName = null)
    {
        string path = string.IsNullOrEmpty(profileName)
            ? PlatformPaths.DesktopConfigPath
            : PlatformPaths.DesktopProfileConfigPath(profileName);
        return Describe(ConfigScope.User, ConfigFileType.ClaudeDesktopConfig, path, readOnly: false);
    }

    /// <summary>
    /// Returns MCP config files for user scope (and project scope if projectRoot provided).
    /// </summary>
    /// <param name="profileName">
    ///   When provided, loads the profile-specific mcp.json instead of the global one.
    /// </param>
    public static IReadOnlyList<DiscoveredFile> DiscoverMcpFiles(
        ClaudeEnvironment env,
        string? projectRoot = null,
        string? profileName = null)
    {
        string userMcpPath = string.IsNullOrEmpty(profileName)
            ? PlatformPaths.UserMcpPath(env)
            : PlatformPaths.ProfileMcpPath(env, profileName);

        List<DiscoveredFile> files =
        [
            Describe(ConfigScope.User, ConfigFileType.McpJson, userMcpPath, readOnly: false),
        ];

        if (projectRoot != null)
        {
            files.Add(Describe(ConfigScope.Project, ConfigFileType.McpJson, PlatformPaths.ProjectMcpPath(projectRoot),
                readOnly: false));
        }

        return files;
    }

    /// <summary>
    /// Returns all profile config files for all discovered profiles.
    /// </summary>
    public static IReadOnlyList<DiscoveredFile> DiscoverProfiles(ClaudeEnvironment env)
    {
        List<DiscoveredFile> files = [];
        foreach (string profile in PlatformPaths.DiscoverProfiles(env))
        {
            files.Add(new DiscoveredFile(
                ConfigScope.User,
                ConfigFileType.ProfileSettings,
                PlatformPaths.ProfileSettingsPath(env, profile),
                File.Exists(PlatformPaths.ProfileSettingsPath(env, profile)),
                IsReadOnly: false,
                ProfileName: profile));

            files.Add(new DiscoveredFile(
                ConfigScope.User,
                ConfigFileType.ProfileMcp,
                PlatformPaths.ProfileMcpPath(env, profile),
                File.Exists(PlatformPaths.ProfileMcpPath(env, profile)),
                IsReadOnly: false,
                ProfileName: profile));
        }

        return files;
    }

    private static DiscoveredFile Describe(ConfigScope scope, ConfigFileType type, string path, bool readOnly)
    {
        bool exists = File.Exists(path);
        bool isReadOnly = readOnly || (exists && new FileInfo(path).IsReadOnly);
        return new DiscoveredFile(scope, type, path, exists, isReadOnly);
    }
}