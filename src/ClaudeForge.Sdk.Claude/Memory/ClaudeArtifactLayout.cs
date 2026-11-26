using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Memory;

/// <summary>
/// Claude Code's artifact layout: its inventory and editor sources, its seven footprint
/// categories, and the single <c>"home"</c> root they resolve against.
/// </summary>
/// <param name="paths">
/// The paths to read, resolved on every use. See <see cref="IArtifactLayout"/> for why this is a
/// factory and not an instance.
/// </param>
/// <remarks>
/// ⭐ <b>Two ways to build one.</b> <see cref="For"/> follows the process profile, which is what the
/// client and the editor pages use; <see cref="Over"/> pins one set of paths, which is what a test
/// of a particular root wants.
/// </remarks>
public sealed class ClaudeArtifactLayout(Func<ClaudeArtifactPaths> paths) : IArtifactLayout
{
    private readonly Func<ClaudeArtifactPaths> _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    /// <summary>
    /// The layout for the current process, re-resolving <see cref="PlatformPaths.UserProfile"/> and
    /// <paramref name="env"/> on every read.
    /// </summary>
    public static ClaudeArtifactLayout For(ClaudeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        return new(() => ClaudeArtifactPaths.DefaultFor(env));
    }

    /// <summary>The layout over one fixed set of paths.</summary>
    public static ClaudeArtifactLayout Over(ClaudeArtifactPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new(() => paths);
    }

    /// <summary>The paths this read resolves to.</summary>
    public ClaudeArtifactPaths Paths => _paths();

    /// <inheritdoc/>
    public IReadOnlyList<CategorisedArtifactSource> InventorySources(string? projectRoot) =>
        ClaudeArtifactSources.ForInventory(Paths, projectRoot);

    /// <inheritdoc/>
    public IReadOnlyList<ScopedArtifactSource> EditorSources(string? projectRoot) =>
        ClaudeEditableArtifactSources.ForEditor(Paths, projectRoot);

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ <b><see cref="FootprintCatalog.Default"/> is Claude's catalog, and it is still in the
    /// neutral SDK.</b> It moves here with the <see cref="FootprintCategory"/> statics in
    /// <c>plans/00008</c> step 13; until then this layout is the one place that names it.
    /// </remarks>
    public FootprintCatalog FootprintCatalog => FootprintCatalog.Default;

    /// <inheritdoc/>
    public FootprintRoots FootprintRoots => FootprintRoots.Single(FootprintRoots.Home, Paths.ClaudeHome);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>~/.claude/projects</c>: one directory per project, named by Claude's path mangling.
    /// </remarks>
    public string? ProjectTranscriptsDirectory => Path.Combine(Paths.ClaudeHome, "projects");
}
