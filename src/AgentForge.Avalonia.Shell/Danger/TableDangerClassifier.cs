using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;

/// <summary>
/// One product's statement about one settings path.
/// </summary>
/// <remarks>
/// Rule and matcher live in one file on purpose: the matcher's specificity and inheritance rules
/// are the only thing that gives these fields meaning, and a rule authored against different
/// semantics than the matcher applies is a silent mis-classification.
/// </remarks>
public sealed record DangerRule
{
    /// <summary>The tier this path deserves regardless of its current value.</summary>
    public required AppSeverity Tier { get; init; }

    /// <summary>
    /// One sentence naming the consequence, shown to the user. Write the outcome, not the
    /// mechanism — "auto-approves every tool" rather than "sets permission to allow".
    /// </summary>
    public required string Why { get; init; }

    /// <summary>
    /// True when the value actually held is the unsafe one.
    /// <para>
    /// ⚠ Only ever invoked on an <b>exact</b> path match — see
    /// <see cref="TableDangerClassifier"/>. A predicate written for
    /// <c>attachment</c> must never be handed <c>attachment.image.maxWidth</c>'s number.
    /// </para>
    /// <para>
    /// ⚠ Values arrive in the editor value currency (<see cref="IEditorValue"/>): integers are
    /// <see cref="long"/>, never <see cref="int"/>, and objects are
    /// <c>IReadOnlyDictionary&lt;string, object?&gt;</c>, never <c>JsonNode</c>. A rule that
    /// pattern-matches the wrong type never fires and reports safe.
    /// </para>
    /// </summary>
    public Func<object?, bool>? Unsafe { get; init; }

    /// <summary>
    /// Given a scope id, true when this path is worse at that scope than its
    /// <see cref="Tier"/> says.
    /// </summary>
    /// <remarks>
    /// The canonical case is a secret: an API key in a user-global file is a local secret, while
    /// the identical key in a project file is committed to git and published to everyone with
    /// repo access. Receives <see langword="null"/> when the caller has no scope in hand, and
    /// must answer <see langword="false"/> then — an unknown scope is not evidence of danger.
    /// </remarks>
    public Func<string?, bool>? EscalatesAt { get; init; }

    /// <summary>Tier used when <see cref="EscalatesAt"/> fires.</summary>
    public AppSeverity EscalatedTier { get; init; } = AppSeverity.Critical;
}

/// <summary>
/// The neutral matcher that turns a product's <see cref="DangerRule"/> table into an
/// <see cref="IDangerClassifier"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mechanism here, data in the product — the same split as
/// <see cref="Navigation.SchemaPageLayout"/>, whose tables live in each app's adapters folder.
/// Danger policy is a statement about an agent's threat model and belongs to whoever ships the
/// agent; matching a dotted path against a pattern is not.
/// </para>
///
/// <para><b>Patterns.</b> Dotted, with <c>*</c> matching exactly one segment:
/// <c>permission.bash</c>, <c>provider.*.options.apiKey</c>, <c>agent.*.permission.*</c>.</para>
///
/// <para><b>Specificity.</b> The winner is the matching pattern with the most segments; ties go
/// to the one with the fewest wildcards. So <c>permission.bash</c> beats <c>permission.*</c>,
/// which beats <c>permission</c>.</para>
///
/// <para>
/// ⭐ <b>Tier is inherited by descendants; the value predicate is NOT.</b> A path with no rule of
/// its own takes the tier and explanation of its nearest ancestor pattern, which is what makes a
/// per-top-level-key table cover a whole nested schema — and is why the coverage guard over
/// top-level keys is meaningful. But <see cref="DangerRule.Unsafe"/> and
/// <see cref="DangerRule.EscalatesAt"/> run <b>only on an exact match</b>, because a predicate
/// written for an object cannot be handed one of that object's leaf values. An inherited
/// assessment therefore always reports <c>IsDangerNow: false</c>: it says "this area deserves
/// attention", never "this specific value is wrong".
/// </para>
/// </remarks>
public sealed class TableDangerClassifier : IDangerClassifier
{
    private readonly (string Pattern, string[] Segments, int Wildcards, DangerRule Rule)[] _rules;

    /// <param name="rules">
    /// Pattern → rule. Ordinal keys; duplicate patterns are the caller's bug and
    /// <see cref="Dictionary{TKey, TValue}"/> already rejects them at construction.
    /// </param>
    public TableDangerClassifier(IReadOnlyDictionary<string, DangerRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules = [.. rules.Select(kv =>
        {
            string[] segments = kv.Key.Split('.');
            return (kv.Key, segments, segments.Count(s => s == "*"), kv.Value);
        })];
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ClassifiedPaths => [.. _rules.Select(r => r.Pattern)];

    /// <inheritdoc/>
    public DangerAssessment Classify(string path, IEditorScope? scope, object? currentValue)
    {
        if (string.IsNullOrEmpty(path))
        {
            return DangerAssessment.Unremarkable;
        }

        string[] actual = path.Split('.');

        (string Pattern, string[] Segments, int Wildcards, DangerRule Rule)? best = null;

        foreach (var candidate in _rules)
        {
            if (!Matches(candidate.Segments, actual))
            {
                continue;
            }

            // Most segments wins; fewest wildcards breaks the tie.
            if (best is null
                || candidate.Segments.Length > best.Value.Segments.Length
                || (candidate.Segments.Length == best.Value.Segments.Length
                    && candidate.Wildcards < best.Value.Wildcards))
            {
                best = candidate;
            }
        }

        if (best is null)
        {
            return DangerAssessment.Unremarkable;
        }

        DangerRule rule = best.Value.Rule;
        bool exact = best.Value.Segments.Length == actual.Length;

        // Inherited (ancestor) matches carry the tier only — see the class remarks.
        if (!exact)
        {
            return new DangerAssessment(rule.Tier, false, rule.Why);
        }

        AppSeverity severity = rule.EscalatesAt?.Invoke(scope?.Id) == true
            ? rule.EscalatedTier
            : rule.Tier;

        return new DangerAssessment(severity, rule.Unsafe?.Invoke(currentValue) ?? false, rule.Why);
    }

    /// <summary>
    /// True when <paramref name="pattern"/> matches <paramref name="actual"/> or one of its
    /// ancestors — i.e. the pattern is no longer than the path and every segment agrees.
    /// </summary>
    private static bool Matches(string[] pattern, string[] actual)
    {
        if (pattern.Length > actual.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != "*" && !string.Equals(pattern[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
