namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// The product-specific half of layered-configuration merging: how values found at the
/// same path in several scopes combine into one effective value.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is product-specific, and therefore here.</b> Only two things, both measured:
/// whether a given path <i>unions</i> its contributions or lets the highest-priority scope
/// replace them outright (<see cref="UnionsAt"/>), and which end of the scope ladder a
/// union starts from (<see cref="UnionOrder"/>).
/// </para>
/// <para>
/// <b>What is universal, and therefore not here.</b> Objects deep-merge, resolving each key
/// independently; anything that is neither a unioned path nor an object is won by the
/// highest-priority scope that defines it. Both products behave this way, so the merge
/// engine still owns those rules. A product that deep-merges differently would grow this
/// interface rather than special-case the engine.
/// </para>
/// <para>
/// <b>Why this is not a single "arrays union" flag.</b> Claude Code's rule is global —
/// arrays union, everything else overrides — so a flag would have sufficed for it. Spike S1
/// measured OpenCode and found the question was a false binary: <c>instructions</c> and
/// <c>plugin</c> union, while <c>disabled_providers</c>, <c>enabled_providers</c>,
/// <c>skills.paths</c>, <c>skills.urls</c> and <c>experimental.primary_tools</c> replace.
/// A policy that unions everything silently resurrects providers the user disabled; one
/// that replaces everything silently drops a global <c>AGENTS.md</c> from
/// <c>instructions</c>. Both lose data, which is why the decision is per path.
/// </para>
/// </remarks>
public interface IMergePolicy
{
    /// <summary>
    /// Whether the contributions at <paramref name="path"/> combine by union, rather than
    /// the highest-priority scope replacing the rest.
    /// </summary>
    /// <param name="path">
    /// Dotted path from the document root — <c>"permissions.allow"</c>, <c>"instructions"</c>.
    /// </param>
    /// <param name="everyValueIsArray">
    /// Whether every scope that defines this path holds an array there. Supplied because a
    /// product may want to infer union-ness from the values when its schema does not declare
    /// the path — Claude Code does; OpenCode must not, since replacing is its default for
    /// arrays it has not listed.
    /// </param>
    bool UnionsAt(string path, bool everyValueIsArray);

    /// <summary>
    /// Which end of the scope ladder a union starts from. Load-bearing wherever the
    /// resulting order carries meaning rather than just presenting the same set differently.
    /// </summary>
    MergeUnionOrder UnionOrder { get; }
}

/// <summary>
/// The end of the scope ladder a union starts from — the order contributions are
/// concatenated in, not which scope wins.
/// </summary>
/// <remarks>
/// Claude Code presents the highest-priority scope's entries first. OpenCode appends
/// lowest-first (measured in Spike S1: a global <c>instructions</c> entry precedes the
/// project's). The distinction is not cosmetic for OpenCode — its permission evaluation
/// takes the <b>last</b> matching rule, so ordering a merged map the wrong way round can
/// invert the user's intent with no edit to any file.
/// </remarks>
public enum MergeUnionOrder
{
    /// <summary>Highest-priority scope's contributions first. Claude Code's order.</summary>
    HighestPriorityFirst = 0,

    /// <summary>Lowest-priority scope's contributions first. OpenCode's order (Spike S1).</summary>
    LowestPriorityFirst = 1,
}
