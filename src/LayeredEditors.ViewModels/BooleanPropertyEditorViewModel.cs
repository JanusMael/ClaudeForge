namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Editor for boolean properties. Tri-state: <c>null</c> = unset (inherits),
/// <c>true</c>, <c>false</c>.
/// </summary>
public partial class BooleanPropertyEditorViewModel : PropertyEditorViewModel
{
    public BooleanPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
    }

    /// <summary><c>null</c> means "not set at this scope" — inherits from a lower-priority scope.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValueSet))]
    private bool? _value;

    public bool IsValueSet => Value.HasValue;

    partial void OnValueChanged(bool? value)
    {
        TrackValueSet(value.HasValue);
    }

    public override object? ToValue()
    {
        return Value.HasValue ? Value.Value : null;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        object? scopeValue = value.GetValueAt(editingScope);
        if (scopeValue is bool b)
        {
            Value = b;
            IsModified = true;
        }
        else
        {
            Value = null;
            IsModified = false;
        }

        // populate the inheritance affordances so the wrapper
        // renders "Defined in scopes:" + "Currently effective from {scope}"
        // the same way compound editors already do.
        UpdateOtherScopesWithData(value, editingScope);
        UpdateInheritedDisplay(value, editingScope);
    }

    protected override void OnResetToInherited()
    {
        Value = null;
    }
}