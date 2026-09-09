using System.Collections.ObjectModel;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// A node in a hierarchical settings navigation tree (sidebar).
/// Leaf nodes reference an editor ViewModel; branch nodes group leaves.
/// </summary>
public partial class NavigationNodeViewModel : ObservableObject
{
    public NavigationNodeViewModel(string title, string? icon = null, string? description = null)
    {
        Title = title;
        Icon = icon;
        Description = description;
        Children = [];
    }

    /// <summary>Display label for this node.</summary>
    public string Title { get; }

    /// <summary>
    /// Stable, culture-invariant identifier for this node — the key used by
    /// deep links (<c>--deep-link</c>) and by persisted UI state.
    /// <para>
    /// Distinct from <see cref="Title"/> on purpose. <see cref="Title"/> is a
    /// display label; the app currently hardcodes English there precisely
    /// because programmatic lookups compare against it, and localizing the nav
    /// tree would break every one of those comparisons. <see cref="NodeId"/> is
    /// the lookup key that survives localization, so new code should match on
    /// it rather than on <see cref="Title"/>.
    /// </para>
    /// <para>
    /// Nullable, and <c>init</c>-only like <see cref="Editor"/> /
    /// <see cref="IsDivider"/> / <see cref="IsTopLevel"/>, so the many existing
    /// <see cref="NavigationNodeViewModel"/> constructions in tests compile
    /// unchanged. Two categories legitimately carry <see langword="null"/>:
    /// divider nodes (several share one placeholder title and none is
    /// selectable) and ad-hoc nodes built by tests.
    /// </para>
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>Optional icon glyph or resource key.</summary>
    public string? Icon { get; }

    /// <summary>
    /// Optional one-sentence description of what this section is for.
    /// Surfaced as a hover tooltip in the navigation tree so a user
    /// scanning the sidebar can read what each section covers without
    /// having to click into it. <c>null</c> falls back to a generic
    /// "Click to open this section." tooltip from <c>Strings.resx</c>
    /// — better than no tooltip at all because it still teaches the
    /// user that the row is interactive.
    /// </summary>
    public string? Description { get; }

    /// <summary>Child nodes for branch nodes; empty for leaf nodes.</summary>
    public ObservableCollection<NavigationNodeViewModel> Children { get; }

    /// <summary>True when this node has no children and owns an <see cref="Editor"/>.</summary>
    public bool IsLeaf => Children.Count == 0;

    /// <summary>The editor ViewModel shown when this leaf is selected. <c>null</c> for branch nodes.</summary>
    public object? Editor { get; init; }

    /// <summary>
    /// when <see langword="true"/>, this node is a visual
    /// separator only (a horizontal-rule between sections in the nav
    /// tree).  The TreeView template renders a thin <see cref="System.Windows"/>-style
    /// rule instead of the standard padded text row, and click handling
    /// is suppressed so selecting the divider doesn't change
    /// <c>SelectedNode</c>.  Set this on construction; together with
    /// the empty <see cref="Editor"/> reference, the AXAML
    /// <c>DataTrigger</c> on <see cref="IsDivider"/> swaps the visual.
    /// </summary>
    public bool IsDivider { get; init; }

    /// <summary>
    /// when <see langword="true"/>, this node is a direct
    /// child of the navigation root (Essentials, the Claude Code /
    /// Claude Desktop section headers, Effective Settings, Profiles,
    /// Backup / Restore, Environment, Memory).  Sub-items (Children of
    /// the section headers) carry <see langword="false"/>.
    /// <para>
    /// Drives the AXAML <c>IsVisible</c> binding on the icon column in
    /// the nav-tree template: top-level rows render their <see cref="Icon"/>;
    /// sub-items hide the icon column entirely so they don't get
    /// pushed-right by an icon-shaped indent on rows whose
    /// <see cref="Icon"/> happens to be empty.  Avalonia collapses
    /// <c>IsVisible=false</c> controls to zero width inside a StackPanel,
    /// so the title aligns flush-left on sub-items.
    /// </para>
    /// <para>
    /// Set on construction.  No production code path mutates the value
    /// after the node is wired into the tree, but it's not enforced via
    /// <c>readonly</c> + ctor parameter so legacy <c>NavigationNodeViewModel</c>
    /// constructions in tests don't have to thread the flag everywhere.
    /// </para>
    /// </summary>
    public bool IsTopLevel { get; init; }

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Optional short annotation rendered after the title — e.g. a schema-provenance badge
    /// reading <c>bundled</c> or <c>fetched 14:32</c>. Empty on rows that have nothing to say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>A plain string, and deliberately not a provenance type.</b> This assembly is the
    /// editor library; it knows nothing about schemas, and it must not learn. Each app formats
    /// its own badge from its own resx and assigns it here, exactly as <see cref="Description"/>
    /// already works — which is what keeps one nav template serving two products.
    /// </para>
    /// <para>
    /// Observable rather than <c>init</c>, because provenance is not final: a "check for schema
    /// updates" action re-reads it mid-session, and a node created before the load completes
    /// would otherwise be stuck with whatever was true at construction.
    /// </para>
    /// </remarks>
    [ObservableProperty] private string? _badge;

    /// <summary>
    /// Hover text for <see cref="Badge"/> — the detail the badge itself is too small to carry.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Description"/> because the row already uses that for "what this
    /// section is for", and a badge's tooltip answers a different question ("which copy of the
    /// schema is this, exactly?"). Overloading one tooltip would mean the badge's detail
    /// replaced the section's purpose, or the other way round.
    /// <para>
    /// ⚠ The template must put this on the badge's own <c>TextBlock</c>: Avalonia tooltips do
    /// not propagate child → parent, so a hover landing on the badge glyphs sees nothing if only
    /// the row's Border carries one. Same reason the icon and title each repeat the row tooltip.
    /// </para>
    /// </remarks>
    [ObservableProperty] private string? _badgeTooltip;
}