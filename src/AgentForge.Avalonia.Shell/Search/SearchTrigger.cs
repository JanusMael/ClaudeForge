namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>
/// The condition under which a product's <see cref="SyntheticSearchEntry"/>
/// surfaces for a query.
///
/// <para>
/// Declarative rather than a predicate delegate on purpose. Matching a typed
/// query against a phrase list is the <em>shell's</em> algorithm — every product
/// wants the same three flavours, and the differences between them are subtle
/// enough that hand-written predicates get them wrong. (They already did once:
/// the bidirectional <see cref="Phrases"/> rule is why a bare "bypass" query
/// used to surface the opposite-intent "disable bypass" row alongside the one
/// the user meant.) The product supplies the words; the shell owns the walk.
/// </para>
/// <para>
/// A trigger with no rules at all matches nothing. That is deliberate: an entry
/// nobody can reach is a visible bug, whereas an entry everybody reaches would
/// pin a row to the top of every search.
/// </para>
/// </summary>
public sealed record SearchTrigger
{
    /// <summary>
    /// Bidirectional substring match: the query matches when it <em>contains</em>
    /// one of these phrases, <em>or</em> one of these phrases contains the query.
    /// The second direction is what lets partial typing land early — "san"
    /// reaches a "sandbox" phrase before the user finishes the word.
    /// </summary>
    public IReadOnlyList<string> Phrases { get; init; } = [];

    /// <summary>
    /// The query matches when it is a <em>prefix</em> of one of these terms.
    /// Strictly narrower than <see cref="Phrases"/> — use it for a single long
    /// identifier the user types from the front (a CLI flag, a config key), where
    /// matching an interior fragment would fire on unrelated queries.
    /// </summary>
    public IReadOnlyList<string> PrefixOf { get; init; } = [];

    /// <summary>
    /// The query matches when it <em>contains</em> one of these terms. The
    /// one-directional half of <see cref="Phrases"/>: use it when a shorter query
    /// should <em>not</em> match, e.g. "pass" must not reach a "bypass" entry.
    /// </summary>
    public IReadOnlyList<string> Mentions { get; init; } = [];

    /// <summary>
    /// Veto list. A query containing any of these never matches, whatever the
    /// rules above say — the escape hatch for an opposite-intent query whose
    /// words overlap ("disable bypass" versus "bypass").
    /// </summary>
    public IReadOnlyList<string> Excluding { get; init; } = [];

    /// <summary>
    /// Shortest query allowed to match at all, guarding against a single
    /// keystroke pinning a row before the user has expressed an intent.
    /// </summary>
    public int MinQueryLength { get; init; } = 2;

    /// <summary>
    /// Evaluate this trigger against an already-normalised query — lower-cased
    /// and trimmed by <c>SearchViewModel</c>, so every rule here is a plain
    /// ordinal comparison and normalisation cannot drift between rule kinds.
    /// </summary>
    public bool Matches(string normalizedQuery)
    {
        ArgumentNullException.ThrowIfNull(normalizedQuery);

        if (normalizedQuery.Length < MinQueryLength)
        {
            return false;
        }

        foreach (string veto in Excluding)
        {
            if (normalizedQuery.Contains(veto, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (string phrase in Phrases)
        {
            if (normalizedQuery.Contains(phrase, StringComparison.Ordinal) ||
                phrase.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (string term in PrefixOf)
        {
            if (term.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (string term in Mentions)
        {
            if (normalizedQuery.Contains(term, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
