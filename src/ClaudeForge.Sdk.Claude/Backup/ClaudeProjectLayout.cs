using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Backup;

/// <summary>
/// Claude Code's <see cref="ProductProjectLayout"/>: what it keeps in a project, the projects its
/// settings name beside the open one, and the projects it has ever opened.
/// </summary>
/// <remarks>
/// <para>
/// ⓘ In <c>AgentForge.Core</c>'s backup and restore engines until plans/00008 step 8, which made
/// all three part of a product's layout. The archive paths they write are unchanged:
/// <c>ClaudeCode/projects/</c> and <c>ClaudeCode/worktrees/</c>.
/// </para>
/// <para>
/// ⛔ <b>The same three answers serve a backup and a restore's authorisation.</b> Restore may write
/// only to a project this machine would itself have captured, and asking one layout both questions
/// is what stops the two lists drifting apart, which is how finding F7 happened.
/// </para>
/// </remarks>
internal static class ClaudeProjectLayout
{
    /// <summary>What a backup captures from each project root, by name.</summary>
    internal static readonly IReadOnlyList<string> Items = [".claude", ".mcp.json", "CLAUDE.md", "CLAUDE.local.md"];

    /// <summary>Claude Code's project layout, with its home resolved from <paramref name="env"/>.</summary>
    internal static ProductProjectLayout For(ClaudeEnvironment env) =>
        new(
            Items,
            RelatedProjects: openRoots => AdditionalDirectoriesResolver.Resolve(SettingsFiles(env, openRoots)),
            KnownProjects: () => KnownProjectsDiscovery.ResolveExisting(PlatformPaths.ClaudeJsonPath));

    /// <summary>
    /// The settings files whose <c>additionalDirectories</c> name related projects: the user's
    /// two, and each open project's two.
    /// </summary>
    internal static IReadOnlyList<string> SettingsFiles(ClaudeEnvironment env, IReadOnlyList<string> openRoots)
    {
        List<string> list =
        [
            PlatformPaths.UserSettingsPath(env),
            Path.Combine(PlatformPaths.ClaudeHome(env), "settings.local.json"),
        ];
        foreach (string p in openRoots)
        {
            list.Add(PlatformPaths.ProjectSettingsPath(p));
            list.Add(PlatformPaths.LocalSettingsPath(p));
        }

        return list;
    }
}
