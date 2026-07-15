using CommunityToolkit.Mvvm.ComponentModel;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// One collapsible category within a large <see cref="ObjectPropertyEditorViewModel"/> —
/// a name-prefix bucket of the object's children (e.g. <c>CLAUDE</c>, <c>OTEL</c>, or the
/// catch-all <c>Other</c>). Rendered as an Expander whose body is a bounded, virtualized
/// list, so a big object (e.g. <c>env</c>'s ~305 vars) shows as a handful of clearly-labelled
/// sections instead of one easy-to-miss toggle, and realizes ZERO child editors until a
/// section is expanded.
/// </summary>
public partial class PropertyCategoryViewModel : ObservableObject
{
    public PropertyCategoryViewModel(string name, IReadOnlyList<LibVm.PropertyEditorViewModel> children)
    {
        Name = name;
        Children = children;
    }

    /// <summary>
    /// Category label — the BARE name prefix (<c>CLAUDE</c>, not <c>CLAUDE_</c>; the trailing
    /// separator is noise in a header), or the catch-alls <c>Other</c> / <c>All</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>Every child editor in this category (realized only while expanded).</summary>
    public IReadOnlyList<LibVm.PropertyEditorViewModel> Children { get; }

    /// <summary>Number of settings in the category — surfaced in the header.</summary>
    public int Count => Children.Count;

    /// <summary>Expander header, e.g. <c>"CLAUDE · 159"</c>.</summary>
    public string Header => $"{Name}  ·  {Count}";

    /// <summary>
    /// Whether this category's editors are shown. Starts collapsed; toggling realizes or
    /// releases the child editors through <see cref="VisibleChildren"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleChildren))]
    private bool _isExpanded;

    /// <summary>
    /// The children the view binds and realizes: all of them when expanded, EMPTY when
    /// collapsed — the lazy gate that keeps a collapsed section at zero realized wrappers.
    /// (An <c>IsVisible</c> binding alone would still realize the whole hidden subtree.)
    /// </summary>
    public IReadOnlyList<LibVm.PropertyEditorViewModel> VisibleChildren =>
        IsExpanded ? Children : [];
}
