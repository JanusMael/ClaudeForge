using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;

/// <summary>One search hit for the global property search.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(
        NavigationNodeViewModel node,
        string sectionTitle,
        string groupTitle,
        string propertyDisplayName,
        string propertyKey,
        string snippet,
        string fullDescription)
    {
        Node = node;
        SectionTitle = sectionTitle;
        GroupTitle = groupTitle;
        PropertyDisplayName = propertyDisplayName;
        PropertyKey = propertyKey;
        Snippet = snippet;
        FullDescription = fullDescription;
    }

    public NavigationNodeViewModel Node { get; }

    /// <summary>Top-level nav section — one hosted product's header node.</summary>
    public string SectionTitle { get; }

    public string GroupTitle { get; }
    public string PropertyDisplayName { get; }

    /// <summary>JSON key / path used to filter the editor to this exact property after navigation.</summary>
    public string PropertyKey { get; }

    /// <summary>Truncated, query-centred excerpt shown in the popup row.</summary>
    public string Snippet { get; }

    /// <summary>Complete description string (kept for internal use; not shown in the search popup tooltip).</summary>
    public string FullDescription { get; }

    /// <summary>
    /// <c>true</c> for hand-crafted rows that are not derived from the schema — the
    /// product's <see cref="SyntheticSearchEntry"/> list, which maps things like a CLI
    /// flag or a page card onto a config property. Consumers use this flag to activate
    /// additional contextual UI once the row is clicked.
    /// </summary>
    public bool IsSynthetic { get; init; }

    /// <summary>
    /// How much the knob behind this hit matters, and whether it is set to the unsafe value right
    /// now — as assessed by the editor that renders it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Not classified here.</b> The value comes from
    /// <see cref="IDangerAnnotatedEditor.AssessDanger(string)"/> on the target editor, so it is
    /// the same assessment the settings row shows. See that interface for why search must not
    /// call the classifier itself.
    /// </para>
    /// <para>
    /// Defaults to <see cref="DangerAssessment.Unremarkable"/>, which renders as no dot — the
    /// right answer for a product with no danger table, a page whose editor cannot be asked, and
    /// a synthetic row that points at a page rather than a property.
    /// </para>
    /// </remarks>
    public DangerAssessment Danger { get; init; } = DangerAssessment.Unremarkable;

    /// <summary>
    /// Whether to render a severity dot on this row at all — true only when the product actually
    /// said something about this path.
    /// </summary>
    /// <remarks>
    /// Named to match <see cref="PropertyEditorViewModel.HasDangerSeverity"/> on purpose: the two
    /// apps' search templates and the settings wrapper then bind identical names, so the dot
    /// cannot be wired one way in one place and another way elsewhere.
    /// </remarks>
    public bool HasDangerSeverity => Danger.Explanation is not null;

    /// <summary>
    /// What a screen reader announces for this row's severity dot — the tier and the consequence.
    /// </summary>
    /// <remarks>
    /// ⛔ A coloured glyph conveys nothing without this, and on a search row it is the only place
    /// the severity appears at all: the row has no banner to fall back on.
    /// </remarks>
    public string DangerAccessibleText =>
        Danger.Explanation is null ? string.Empty : $"{Danger.Severity}: {Danger.Explanation}";

    /// <summary>
    /// Breadcrumb path shown as the secondary line of the tooltip — the hosted
    /// product's section, then the page within it (e.g. "Widget Forge › General").
    /// </summary>
    public string NavigationContext => $"{SectionTitle} › {GroupTitle}";

    /// <summary>
    /// Full tooltip text shown on the search popup button.  Combines the property description
    /// (when available) with the breadcrumb navigation context on a separate line so the user
    /// can read what the property does <em>and</em> where they will land after clicking.
    /// </summary>
    public string TooltipText => string.IsNullOrWhiteSpace(FullDescription)
        ? NavigationContext
        : $"{FullDescription}\n\n{NavigationContext}";

    /// <summary>
    /// What a screen reader should announce for the whole row: which knob, where it lives, and —
    /// when the product said something — how much it matters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>An <c>ItemsSource</c>-generated <c>ListBoxItem</c> takes its name from the ITEM, not
    /// from the <c>ItemTemplate</c>, and falls back to <see cref="object.ToString"/>.</b> Measured
    /// via UIA: without this, every row of OpenCodeForge's search results announced
    /// <c>Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search.SearchResultViewModel</c> — the type
    /// name, five times over. This is the same defect already fixed for both of the repo's
    /// <c>ItemsSource</c>-bound TabControls, on a third container type.
    /// </para>
    /// <para>
    /// ⚠ The severity rides along because the row's dot carries it on an inner
    /// <c>TextBlock</c>'s <c>HelpText</c>, and a reader announcing the CONTAINER does not
    /// necessarily read a child's help text. The whole point of the dot is lost if the one
    /// announcement a keyboard user hears omits it. ClaudeForge's rows are Buttons with an
    /// explicit name and so never fell back here, which is exactly why this went unnoticed in one
    /// app while being broken in the other.
    /// </para>
    /// </remarks>
    public string AccessibleName => HasDangerSeverity
        ? $"{PropertyDisplayName}, {NavigationContext}. {DangerAccessibleText}"
        : $"{PropertyDisplayName}, {NavigationContext}";

    /// <inheritdoc cref="AccessibleName"/>
    public override string ToString() => AccessibleName;
}
