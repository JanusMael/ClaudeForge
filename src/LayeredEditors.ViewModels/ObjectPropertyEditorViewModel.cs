using System.ComponentModel;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Editor for object-type properties. Renders child property editors recursively.
/// Requires a workspace reference to load child values independently.
/// </summary>
/// <remarks>
/// <para>
/// Child editors are created by the factory and passed in at construction time.
/// This VM subscribes to each child's <see cref="PropertyEditorViewModel.IsModified"/>
/// change and propagates it upward so that the hosting group editor hears every
/// nested edit — not just top-level ones.
/// </para>
/// <para>
/// The propagation always force-fires <c>PropertyChanged("IsModified")</c> even when
/// the parent's own <c>IsModified</c> flag does not change (e.g. a second child is
/// modified while the first was already modified). Without the force-fire,
/// CommunityToolkit.Mvvm's <c>[ObservableProperty]</c> setter would suppress the event
/// and the group editor would never write the updated value to the workspace.
/// </para>
/// </remarks>
public partial class ObjectPropertyEditorViewModel : PropertyEditorViewModel
{
    private readonly IEditorWorkspace? _workspace;

    public ObjectPropertyEditorViewModel(
        IEditorSchema schema,
        IEditorScope editingScope,
        IReadOnlyList<PropertyEditorViewModel> children,
        IEditorWorkspace? workspace = null)
        : base(schema, editingScope)
    {
        Children = children;
        _workspace = workspace;

        // Subscribe to children so nested edits bubble up to the group editor.
        // Children have the same lifetime as this VM (created and discarded together
        // during RebuildEditors), so no explicit unsubscription is needed.
        foreach (PropertyEditorViewModel child in Children)
        {
            child.PropertyChanged += OnChildPropertyChanged;
        }
    }

    public IReadOnlyList<PropertyEditorViewModel> Children { get; }

    [ObservableProperty] private bool _isExpanded = true;

    public override object? ToValue()
    {
        Dictionary<string, object?> dict = new(StringComparer.Ordinal);
        foreach (PropertyEditorViewModel child in Children)
        {
            object? val = child.ToValue();
            if (val != null)
            {
                dict[child.Schema.Name] = val;
            }
        }

        return dict.Count > 0 ? (IReadOnlyDictionary<string, object?>)dict : null;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        if (_workspace is not null)
        {
            foreach (PropertyEditorViewModel child in Children)
            {
                IEditorValue childValue = _workspace.GetValue(child.Path);
                child.LoadFromValue(childValue, editingScope);
            }
        }

        // IsModified is true when:
        //   (a) any child reports a value at this scope (covers the common case), OR
        //   (b) the object itself has an explicit value at this scope with no children
        //       (edge case for schema-less or empty-children objects).
        // We cannot rely on children alone when Children is empty; in that case
        // the only signal is whether the object key exists at the editing scope.
        IsModified = Children.Any(c => c.IsModified) || value.IsDefinedAt(editingScope);
    }

    protected override void OnResetToInherited()
    {
        foreach (PropertyEditorViewModel child in Children)
        {
            child.ResetToInheritedCommand.Execute(null);
        }
    }

    // -----------------------------------------------------------------------
    // Child change propagation
    // -----------------------------------------------------------------------

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IsModified))
        {
            return;
        }

        bool wasModified = IsModified;
        IsModified = Children.Any(c => c.IsModified);

        // Force-fire even when IsModified stays true so the hosting group editor
        // always re-invokes ToValue() and writes the complete updated object.
        if (wasModified == IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
    }
}