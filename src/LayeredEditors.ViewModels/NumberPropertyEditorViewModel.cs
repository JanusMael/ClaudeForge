namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Editor for integer and floating-point number properties, with optional min/max bounds.
/// </summary>
public partial class NumberPropertyEditorViewModel : PropertyEditorViewModel
{
    public NumberPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Minimum = schema.Minimum;
        Maximum = schema.Maximum;
        IsInteger = schema.ValueType == EditorValueType.Integer;
    }

    public double? Minimum { get; }
    public double? Maximum { get; }

    /// <summary>True when the schema describes an integer (controls spinner increment and round-trip).</summary>
    public bool IsInteger { get; }

    /// <summary><c>null</c> = not set at this scope (inherits).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValueSet))]
    private double? _value;

    public bool IsValueSet => Value.HasValue;

    partial void OnValueChanged(double? value)
    {
        TrackValueSet(value.HasValue);
    }

    public override object? ToValue()
    {
        if (!Value.HasValue)
        {
            return null;
        }

        return IsInteger ? (object)(long)Value.Value : Value.Value;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        object? scopeValue = value.GetValueAt(editingScope);
        if (scopeValue is long l)
        {
            Value = l;
            IsModified = true;
        }
        else if (scopeValue is double d)
        {
            Value = d;
            IsModified = true;
        }
        else if (scopeValue is int i)
        {
            Value = i;
            IsModified = true;
        }
        else
        {
            Value = null;
            IsModified = false;
        }

        UpdateOtherScopesWithData(value, editingScope);
        UpdateInheritedDisplay(value, editingScope);
    }

    protected override void OnResetToInherited()
    {
        Value = null;
    }
}