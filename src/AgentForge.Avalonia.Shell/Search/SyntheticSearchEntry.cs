using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>
/// A hand-written search row a product pins for queries the schema alone would
/// never match — a CLI flag that has a config-file equivalent, an environment
/// variable, a curated page card, the symptom phrasing of a known gotcha.
///
/// <para>
/// This is the product's half of the search seam. Everything here is data or a
/// tree lookup; none of it is an algorithm. <c>SearchViewModel</c> walks the
/// list, asks each <see cref="Trigger"/> whether the query matches, resolves
/// <see cref="FindTarget"/> against the live navigation tree, and emits a row —
/// the same shape as the <c>IMergePolicy</c> and <c>ScopeLadder</c> seams, where
/// the product states its rules and the neutral side applies them.
/// </para>
/// </summary>
public sealed record SyntheticSearchEntry
{
    /// <summary>
    /// Identity within one product's entry list, used by <see cref="Suppresses"/>.
    /// Deliberately separate from <see cref="PropertyKey"/>, which is a payload
    /// and is legitimately empty for a row that means "open this page, no
    /// particular property".
    /// </summary>
    public required string Id { get; init; }

    /// <summary>When this row surfaces.</summary>
    public required SearchTrigger Trigger { get; init; }

    /// <summary>
    /// Locate the node this row navigates to, given the live navigation tree.
    /// Returning <see langword="null"/> drops the row silently — the correct
    /// behaviour for a page this install does not have, and the reason the
    /// lookup is a delegate rather than a stored node: the tree is rebuilt by
    /// every workspace reload.
    /// </summary>
    public required Func<IEnumerable<NavigationNodeViewModel>, NavigationNodeViewModel?> FindTarget { get; init; }

    /// <summary>Top-level nav section the row is filed under in the results list.</summary>
    public required string SectionTitle { get; init; }

    /// <summary>Page / group shown as the second breadcrumb segment.</summary>
    public required string GroupTitle { get; init; }

    /// <summary>The row's headline text.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Carried through to <see cref="SearchResultViewModel.PropertyKey"/>, where
    /// the host interprets it after navigation — a JSON path to filter the editor
    /// to, a card id to highlight, or empty for "just open the page".
    /// </summary>
    public string PropertyKey { get; init; } = string.Empty;

    /// <summary>Excerpt shown under the headline in the results popup.</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>Full description, shown in the row's tooltip.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Ids of other entries whose rows this one displaces when it fires. For a
    /// pair of opposite-intent entries whose words overlap, the veto list on
    /// <see cref="SearchTrigger.Excluding"/> keeps the broad one out of the
    /// narrow query; this keeps the narrow one out of the broad query.
    /// <para>
    /// Applied only when this entry actually produced a row, so an entry whose
    /// target page is absent suppresses nothing.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Suppresses { get; init; } = [];
}
