using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// Maps OpenCode's config files onto the rungs of <see cref="OpenCodeScopes.Ladder"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three of the five rungs are discovered here, and the other two are deliberately not.</b>
/// Rather than invent them:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Inline</b> (<c>$OPENCODE_CONFIG_CONTENT</c>) is a config with <i>no file behind it</i>,
///   and <see cref="DiscoveredFile"/> is a path plus flags. Representing it needs the loader to
///   accept content that never came from disk, which is a change to the shared load path rather
///   than something this product can decide alone. ⚠ Until then the effective view will
///   disagree with the running agent whenever that variable is set —
///   <see cref="OpenCodeEnvironment.InlineContent"/> is carried so the host can at least say so.
///   </description></item>
///   <item><description>
///   <b>Managed</b> has no measured location for OpenCode. Every spike that touched layering
///   exercised the three below; managed is asserted by the plan and never observed. Guessing a
///   path would produce a scope that silently never populates, which looks identical to a
///   working one on any machine without policy.
///   </description></item>
/// </list>
/// <para>
/// Discovery reports files that do <i>not</i> exist as well as ones that do
/// (<see cref="DiscoveredFile.Exists"/>), because the app needs a canonical path to create
/// when the user edits a scope for the first time.
/// </para>
/// </remarks>
public static class OpenCodeDiscovery
{
    /// <summary>
    /// Discover the main-config files for each populated scope, highest priority first.
    /// </summary>
    /// <param name="projectRoot">
    /// Where the project walk starts. <see langword="null"/> means no project context, which
    /// is not the same as the walk finding nothing — a host with no folder open has no
    /// project scope at all.
    /// </param>
    /// <param name="env">The environment overrides in effect.</param>
    public static IReadOnlyList<DiscoveredFile> DiscoverConfig(string? projectRoot, OpenCodeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        List<DiscoveredFile> files = [];

        // Highest priority first, matching the ladder's own order so the two cannot drift
        // apart silently. Managed and Inline would sit above Project; see the class remarks.

        if (!env.ProjectConfigDisabled && projectRoot is not null)
        {
            string? project = OpenCodePaths.FindProjectConfig(projectRoot);
            if (project is not null)
            {
                files.Add(Config(OpenCodeScopes.Project, project, exists: true));
            }
        }

        if (env.ConfigPath is { } custom)
        {
            files.Add(Config(OpenCodeScopes.Custom, custom, File.Exists(custom)));
        }

        string global = OpenCodePaths.GlobalConfigPath(env);
        files.Add(Config(OpenCodeScopes.Global, global, File.Exists(global)));

        return files;
    }

    /// <summary>
    /// Discover the TUI config. It is global-only — <c>tui.json</c> has no project form and
    /// no environment override of its own.
    /// </summary>
    public static IReadOnlyList<DiscoveredFile> DiscoverTui(OpenCodeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        string path = OpenCodePaths.TuiConfigPath(env);
        return
        [
            new DiscoveredFile(
                ScopeNamed(OpenCodeScopes.Global),
                ConfigFileType.OpenCodeTui,
                path,
                File.Exists(path),
                IsReadOnly: false),
        ];
    }

    private static DiscoveredFile Config(string rungName, string path, bool exists)
    {
        ConfigScope scope = ScopeNamed(rungName);
        return new DiscoveredFile(
            scope,
            ConfigFileType.OpenCodeConfig,
            path,
            exists,
            // Taken from the ladder rather than restated. A second copy of "which scopes are
            // read-only" is exactly how a policy-locked scope becomes editable without any
            // test noticing.
            IsReadOnly: scope.IsReadOnly);
    }

    /// <summary>
    /// The ladder scope with the given rung name. Throws rather than returning a default,
    /// because <c>default(ConfigScope)</c> is Claude's <c>Managed</c> — a silently wrong
    /// answer that would mark an OpenCode file read-only and give it Claude's precedence.
    /// </summary>
    private static ConfigScope ScopeNamed(string rungName)
    {
        foreach (ConfigScope scope in OpenCodeScopes.Ladder.All)
        {
            if (scope.ToString() == rungName)
            {
                return scope;
            }
        }

        throw new InvalidOperationException(
            $"No rung named '{rungName}' on OpenCode's scope ladder.");
    }
}
