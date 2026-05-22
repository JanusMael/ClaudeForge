namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>Editor for general string properties.</summary>
public partial class StringPropertyEditorViewModel : PropertyEditorViewModel
{
    public StringPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValueSet))]
    private string? _value;

    public bool IsValueSet => Value != null;

    partial void OnValueChanged(string? value)
    {
        TrackValueSet(value != null);
    }

    public override object? ToValue()
    {
        return Value;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        object? scopeValue = value.GetValueAt(editingScope);
        Value = scopeValue as string;
        IsModified = Value != null;
        UpdateOtherScopesWithData(value, editingScope);
        UpdateInheritedDisplay(value, editingScope);
    }

    protected override void OnResetToInherited()
    {
        Value = null;
    }
}