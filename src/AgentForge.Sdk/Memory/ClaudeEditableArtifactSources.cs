using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// One artifact source plus the editing scope its entries belong to.
/// </summary>
/// <param name="Source">The source itself.</param>
/// <param name="Scope">
/// Which <see cref="EditableMemoryScope"/> the entries land in — and therefore whether the editor
/// offers to save them.
/// </param>
/// <remarks>
/// ⚠ <b>Not recoverable from the entry, and for the opposite reason to
/// <see cref="CategorisedArtifactSource"/>.</b> There the tag was coarser than the entry's kind;
/// here it is coarser than the entry's <see cref="ArtifactScope"/> — the plugin source yields one
/// scope per installed plugin, and every one of them is
/// <see cref="EditableMemoryScope.Plugin"/>. Reading writability off a scope id would mean parsing
/// a string prefix, which is how a typo becomes an editable plugin file.
/// </remarks>
internal sealed record ScopedArtifactSource(IArtifactSource Source, EditableMemoryScope Scope);

/// <summary>
/// Where Claude Code's three editable artifact kinds live, as an ordered source list.
/// </summary>
/// <remarks>
/// ⭐ <b>Two of the three scopes are literally the same sources the Memory inventory uses.</b>
/// <c>~/.claude/agents</c> is one directory whether the page in front of you lists it or edits it,
/// and the source ids match for that reason. Only the plugin tree is unique to this page, and only
/// because the Memory inventory does not surface plugin-provided artifacts at all.
/// </remarks>
internal static class ClaudeEditableArtifactSources
{
    /// <summary>
    /// Build the source list backing the Agents &amp; Skills editor, in walk order.
    /// </summary>
    /// <param name="paths">Where this profile's Claude files live.</param>
    /// <param name="projectRoot">
    /// The open project's root, or <see langword="null"/> when none is open.
    /// </param>
    internal static IReadOnlyList<ScopedArtifactSource> ForEditor(
        ClaudeArtifactPaths paths, string? projectRoot)
    {
        List<ScopedArtifactSource> sources = [];
        AddClaudeDirectory(sources, paths.ClaudeHome, ClaudeScopes.User, EditableMemoryScope.User);

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            AddClaudeDirectory(
                sources, Path.Combine(projectRoot, ".claude"), ClaudeScopes.Project, EditableMemoryScope.Project);
        }

        // Read-only, and discovered rather than declared — see ClaudePluginArtifactSource.
        sources.Add(new ScopedArtifactSource(
            new ClaudePluginArtifactSource(Path.Combine(paths.ClaudeHome, "plugins")),
            EditableMemoryScope.Plugin));

        return sources;
    }

    /// <summary>
    /// The three editable kinds under one <c>.claude</c> directory.
    /// </summary>
    private static void AddClaudeDirectory(
        List<ScopedArtifactSource> sources,
        string claudeDirectory,
        ArtifactScope scope,
        EditableMemoryScope editableScope)
    {
        // The scope id is the source-id prefix, so a third writable scope cannot be added with a
        // copy-pasted id from the second.
        string prefix = $"claude-{scope.Id}";

        sources.Add(new ScopedArtifactSource(
            ClaudeArtifactSources.Walk(
                new ArtifactSourceIdentity($"{prefix}-agents", ArtifactKind.Agent, scope),
                Path.Combine(claudeDirectory, "agents"), "*.md"),
            editableScope));

        sources.Add(new ScopedArtifactSource(
            ClaudeArtifactSources.Walk(
                new ArtifactSourceIdentity($"{prefix}-commands", ArtifactKind.Command, scope),
                Path.Combine(claudeDirectory, "commands"), "*.md"),
            editableScope));

        sources.Add(new ScopedArtifactSource(
            new SkillDirectoryArtifactSource(
                new ArtifactSourceIdentity($"{prefix}-skills", ArtifactKind.Skill, scope),
                Path.Combine(claudeDirectory, "skills"),
                "SKILL.md"),
            editableScope));
    }
}
