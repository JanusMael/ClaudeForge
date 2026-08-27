using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// What it MEANS that an artifact was declared more than once.
/// </summary>
/// <remarks>
/// ⛔⛔ <b>"Shadowed" is a structural fact and a per-kind claim, and conflating the two states
/// something false.</b> <see cref="ResolvedArtifact.IsShadowed"/> only says more than one source
/// declared the name. What the tool then does with the losers is <b>opposite</b> for OpenCode's two
/// families, and both were measured against v1.17.9.
/// </remarks>
public enum OpenCodeChainSemantics
{
    /// <summary>
    /// One declaration wins outright and the rest are never loaded.
    /// </summary>
    /// <remarks>
    /// Measured for skills: the same skill name in the project and the global directory resolved to
    /// <b>one</b> entry — the project copy — with the global one not surfaced by
    /// <c>opencode debug skill</c> at all. Calling the loser "overridden" is accurate here.
    /// </remarks>
    SingleWinner,

    /// <summary>
    /// Every declaration contributes; later layers overwrite earlier ones <b>per field</b>.
    /// </summary>
    /// <remarks>
    /// Measured for agents and commands: the same name in the project and the global directory
    /// produced one entry whose <c>description</c> came from the global file while a field only the
    /// project file set <b>survived alongside it</b>. So the lower-precedence declarations are
    /// <b>live</b>, and describing them as shadowed or overridden would tell the user their file is
    /// doing nothing when several of its fields are in force.
    /// </remarks>
    DeepMerge,
}

/// <summary>
/// Per-kind rules for reading a resolved chain, so no consumer has to guess.
/// </summary>
/// <remarks>
/// ⚠ <b>This exists because the honest answer differs per kind and the wrong answer is invisible.</b>
/// A page that prints "2 copies overridden" under a merging agent is not slightly off — it tells the
/// user to delete a file that is contributing settings.
/// </remarks>
public static class OpenCodeArtifactSemantics
{
    /// <summary>
    /// How to read a chain of the given kind.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Only the two measured kinds are <see cref="OpenCodeChainSemantics.DeepMerge"/>.</b>
    /// Everything else defaults to <see cref="OpenCodeChainSemantics.SingleWinner"/> rather than
    /// being assumed into the merging bucket — an unmeasured kind gets the conservative reading,
    /// and the way to move one is to measure it, not to widen this predicate.
    /// </remarks>
    public static OpenCodeChainSemantics For(ArtifactKind kind)
        => OpenCodeArtifactScopes.MergesWithGlobalLast(kind)
            ? OpenCodeChainSemantics.DeepMerge
            : OpenCodeChainSemantics.SingleWinner;

    /// <summary>
    /// Whether a declaration lives in a directory belonging to a different tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Badge these, because editing one is editing another tool's installation.</b> A user
    /// deleting what looks like a duplicate skill from OpenCode's list would be removing it from
    /// Claude Code. On the machine this was measured on, 118 of 124 resolved skills came from
    /// <c>~/.claude/skills</c> — the overlap is the normal case, not an edge one.
    /// </para>
    /// <para>
    /// ⚠ <b>Takes the whole entry because the SCOPE cannot answer it.</b> The user-level roots are
    /// their own scopes, but a project's <c>.claude/skills/</c> sits in the <i>project</i> scope —
    /// the very same scope as <c>.opencode/skills/</c> beside it — so scope id alone would badge
    /// only two of the three and quietly miss the one inside the user's own repository.
    /// </para>
    /// </remarks>
    public static bool IsCrossTool(ArtifactRef entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Scope.Id.StartsWith(OpenCodeArtifactSources.ExternalScopePrefix, StringComparison.Ordinal)
            || entry.SourceId.EndsWith(OpenCodeArtifactSources.ClaudeSkillsSourceSuffix, StringComparison.Ordinal);
    }
}
