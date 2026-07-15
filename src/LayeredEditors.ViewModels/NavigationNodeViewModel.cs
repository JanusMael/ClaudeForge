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
}