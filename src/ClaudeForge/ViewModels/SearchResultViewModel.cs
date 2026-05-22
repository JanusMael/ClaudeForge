namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

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

    /// <summary>Top-level nav section (e.g. "Claude Code" or "Claude Desktop").</summary>
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
    /// <c>true</c> for hand-crafted results that are not derived from the schema — e.g. the
    /// synthetic <c>--dangerouslySkipPermissions</c> entry that maps a CLI flag to a config
    /// property.  Consumers can use this flag to activate additional contextual UI.
    /// </summary>
    public bool IsSynthetic { get; init; }

    /// <summary>
    /// Breadcrumb path shown as the secondary line of the tooltip (e.g. "Claude Code › General").
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
}