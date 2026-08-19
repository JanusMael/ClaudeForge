using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// How OpenCode combines values that several config layers define at the same path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-key, not one array rule.</b> Spike S1 asked "union or replace?" and found it a
/// false binary: measured against three real layers, <c>instructions</c> and <c>plugin</c>
/// union while <c>disabled_providers</c>, <c>enabled_providers</c>, <c>skills.paths</c>,
/// <c>skills.urls</c> and <c>experimental.primary_tools</c> replace outright. Both possible
/// simplifications lose data in opposite directions, and neither loses it loudly: union
/// everything and a provider the user disabled comes back to life; replace everything and
/// the global <c>AGENTS.md</c> silently drops out of <c>instructions</c>.
/// </para>
/// <para>
/// <b>Union order is not cosmetic here.</b> OpenCode evaluates the <b>last</b> matching
/// permission rule, so a union assembled from the wrong end of the ladder inverts the user's
/// intent without any file being edited. S1 measured lowest-priority-first —
/// <c>["global-x.md", "proj-a.md", "proj-b.md", "inline-z.md"]</c> — which is the opposite of
/// Claude Code's order.
/// </para>
/// <para>
/// <b>Value shape is deliberately ignored.</b> <see cref="UnionsAt"/> receives
/// <c>everyValueIsArray</c> and does not consult it. Claude Code infers union-ness from the
/// values for paths its schema does not declare; OpenCode must not, because replacing is its
/// default for arrays it has not listed. Inferring here would quietly union
/// <c>disabled_providers</c> — an array by shape, replace by measurement — and resurrect
/// providers the user turned off.
/// </para>
/// <para>
/// Objects deep-merge and scalars are won by the highest-priority layer. Neither appears
/// here because both are universal, and the merge engine owns them.
/// </para>
/// </remarks>
public sealed class OpenCodeMergePolicy : IMergePolicy
{
    /// <summary>The shared instance. The policy is immutable and carries no state.</summary>
    public static OpenCodeMergePolicy Instance { get; } = new();

    /// <summary>
    /// The only two paths that union, measured in S1. Everything else replaces.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a deny-list on purpose. A new OpenCode array key added
    /// upstream then defaults to <b>replace</b>, which is OpenCode's own default for arrays
    /// it has not declared — so the failure mode of falling behind upstream is "a value the
    /// user set is overridden by a higher layer", not "a value the user removed comes back".
    /// </remarks>
    private static readonly HashSet<string> UnionPaths =
        new(StringComparer.Ordinal)
        {
            // Instruction files accumulate across layers: the global AGENTS.md and the
            // project's are both meant to apply.
            "instructions",

            // Plugins likewise, with auto-discovered entries appended last.
            "plugin",
        };

    private OpenCodeMergePolicy()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="everyValueIsArray"/> is accepted and ignored — see the class remarks
    /// for why inferring from value shape is the specific mistake this product must not make.
    /// </remarks>
    public bool UnionsAt(string path, bool everyValueIsArray)
    {
        ArgumentNullException.ThrowIfNull(path);
        _ = everyValueIsArray;
        return UnionPaths.Contains(path);
    }

    /// <inheritdoc/>
    public MergeUnionOrder UnionOrder => MergeUnionOrder.LowestPriorityFirst;
}
