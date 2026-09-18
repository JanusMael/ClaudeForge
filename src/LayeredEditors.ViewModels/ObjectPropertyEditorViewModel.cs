using System.ComponentModel;

namespace Bennewitz.Ninja.LayeredEditors.ViewModels;

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
public partial class ObjectPropertyEditorViewModel : PropertyEditorViewModel, IChildEditorHost
{
    private readonly IEditorWorkspace? _workspace;

    /// <summary>
    /// Keys present in this object AT THE EDITING SCOPE that no child models — captured on
    /// load, re-emitted verbatim by <see cref="ToValue"/>. <c>null</c> when the scope holds
    /// nothing this editor cannot account for, which is the ordinary case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ Without this, editing ONE modelled key deletes every unmodelled key in the same
    /// object. <see cref="ToValue"/> rebuilds the object from <see cref="Children"/>, and the
    /// hosting group editor writes that result as a WHOLE-OBJECT replacement at this editor's
    /// path — so a key with no child arrives at the writer as a key the user removed. The
    /// force-fire in <c>OnChildPropertyChanged</c> below exists precisely to make the host
    /// re-invoke <see cref="ToValue"/> and write the complete object, which is what turns a
    /// rebuild into a deletion.
    /// </para>
    /// <para>
    /// An editor may decline to RENDER a key it does not understand. It must not delete what
    /// it chose not to show.
    /// </para>
    /// <para>
    /// ⚠ This is the same defect as the app-side editor's, fixed there first. The two
    /// <c>ObjectPropertyEditorViewModel</c> classes do not derive from one another — see
    /// <see cref="IChildEditorHost"/> — so neither the fix nor its tests carried across.
    /// </para>
    /// <para>
    /// Only the editing scope is captured, because that is the only scope this editor writes;
    /// carrying another scope's keys would promote an inherited value into an explicit
    /// override nobody asked for. Comparison is ORDINAL, matching <see cref="ToValue"/> —
    /// JSON keys are case-sensitive, so a differently-cased near-match is a genuinely
    /// different key and is preserved as one.
    /// </para>
    /// <para>
    /// ⓘ No defensive copy of the values: the currency contract on
    /// <see cref="IEditorValue"/> admits only scalars and read-only collections, none of which
    /// carry the single-parent ownership that forces the app-side editor to clone its
    /// <c>JsonNode</c>s.
    /// </para>
    /// </remarks>
    private Dictionary<string, object?>? _unmodelledAtEditingScope;

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

        // Re-emit what this editor never rendered. A child's own answer always wins: the
        // carried copy is the load-time value, and for a modelled key that is stale by
        // definition once the user has touched it.
        if (_unmodelledAtEditingScope is not null)
        {
            foreach (KeyValuePair<string, object?> kv in _unmodelledAtEditingScope)
            {
                if (!dict.ContainsKey(kv.Key))
                {
                    dict[kv.Key] = kv.Value;
                }
            }
        }

        return dict.Count > 0 ? (IReadOnlyDictionary<string, object?>)dict : null;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        // Before loading anything, remember what this editor is about to ignore.
        _unmodelledAtEditingScope = CaptureUnmodelled(value, editingScope);

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

    /// <summary>
    /// The editing scope's raw object, minus every key some child already models.
    /// </summary>
    private Dictionary<string, object?>? CaptureUnmodelled(IEditorValue value, IEditorScope editingScope)
    {
        if (value.GetValueAt(editingScope) is not IReadOnlyDictionary<string, object?> atScope)
        {
            return null;
        }

        HashSet<string> modelled = new(Children.Select(c => c.Schema.Name), StringComparer.Ordinal);
        Dictionary<string, object?>? kept = null;
        foreach (KeyValuePair<string, object?> kv in atScope)
        {
            if (modelled.Contains(kv.Key))
            {
                continue;
            }

            kept ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            kept[kv.Key] = kv.Value;
        }

        return kept;
    }

    /// <remarks>
    /// ⚠ Resetting DOES drop the carried keys, and that is the intended reading: reset means
    /// "remove this property at this scope", an explicit destructive act on the whole object.
    /// Preserving what the reset was asked to clear would leave a property alive on keys the
    /// user cannot see, after they emptied every field they can.
    /// </remarks>
    protected override void OnResetToInherited()
    {
        foreach (PropertyEditorViewModel child in Children)
        {
            child.ResetToInheritedCommand.Execute(null);
        }

        _unmodelledAtEditingScope = null;
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