using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// The precedence layers Claude Code reads artifacts from.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Precedence orders a chain for display; it is not a measured statement about which
/// definition Claude executes.</b> Where two layers hold the same settings file the order is
/// documented behaviour — managed policy over project over user — and stating it any other way
/// would misreport whose value is live. Where two layers hold an agent or skill of the same name,
/// what Claude actually runs has not been measured here, and the resolver deliberately does not
/// merge or discard: consumers list the whole chain. Do not upgrade that into an override claim
/// without evidence.
/// </para>
/// <para>
/// ⭐ One definition, two source lists. The Memory inventory and the Agents &amp; Skills editor
/// both draw from Claude's layers, and a second copy of these numbers is exactly how the two pages
/// would come to disagree about which scope a file belongs to.
/// </para>
/// </remarks>
internal static class ClaudeScopes
{
    /// <summary>Files under the user's own <c>~/.claude/</c>.</summary>
    internal static readonly ArtifactScope User = new("user", "User", 10);

    /// <summary>Files under the open project.</summary>
    internal static readonly ArtifactScope Project = new("project", "Project", 20);

    /// <summary>
    /// Administrator policy — the layer Claude reads over the user's own settings.
    /// </summary>
    internal static readonly ArtifactScope Managed = new("managed", "Managed policy", 30);

    /// <summary>
    /// Another tool's files, sitting beside Claude's. Lowest, and they never actually compete:
    /// their names are tool-qualified precisely so a sibling's <c>AGENTS.md</c> is not mistaken
    /// for a shadowed copy of Claude's.
    /// </summary>
    internal static readonly ArtifactScope CrossTool = new("cross-tool", "Other tools", 0);

    /// <summary>
    /// One installed plugin, identified by its path-derived name.
    /// </summary>
    /// <param name="label">
    /// The plugin's display name, e.g. <c>everything-claude-code</c>. It becomes the scope's
    /// <see cref="ArtifactScope.DisplayName"/>, which is what the editor shows per row so two
    /// same-named artifacts from different plugins are distinguishable at a glance.
    /// </param>
    /// <remarks>
    /// ⭐ <b>A plugin is a SCOPE, not a source.</b> There are as many of these as the user has
    /// plugins installed, discovered during the walk rather than declared — which is the reason
    /// <see cref="IArtifactSource"/> carries no scope of its own. Precedence 0 puts them below the
    /// user's and the project's own files; see the ordering caveat on this class.
    /// </remarks>
    internal static ArtifactScope ForPlugin(string label)
    {
        return new ArtifactScope($"plugin:{label}", label, 0);
    }
}
