namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>Editor for string properties with a fixed set of allowed values (schema enum).</summary>
public partial class EnumPropertyEditorViewModel : PropertyEditorViewModel
{
    public EnumPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        EnumOptions = schema.EnumValues ?? [];
        // When the schema has an "examples" keyword the Enum was promoted from a free-form
        // string — users should be able to type custom values in addition to picking one.
        // A strict enum (from JSON-Schema "enum") has no Examples and is rendered as a
        // ComboBox with a visible dropdown chevron so the dropdown affordance is obvious.
        AllowsFreeForm = schema.Examples.Count > 0;
    }

    /// <summary>The allowed values from the schema's enum definition.</summary>
    public IReadOnlyList<string> EnumOptions { get; }

    /// <summary>
    /// <c>true</c> for enums promoted from the schema <c>examples</c> keyword —
    /// the editor should allow the user to type arbitrary values.
    /// <c>false</c> for strict enums (from the schema <c>enum</c> keyword) — the
    /// editor should restrict to the options list.
    /// </summary>
    public bool AllowsFreeForm { get; }

    /// <summary>Inverse of <see cref="AllowsFreeForm"/> — convenience for XAML visibility bindings.</summary>
    public bool IsStrictEnum => !AllowsFreeForm;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValueSet))]
    private string? _selectedValue;

    public bool IsValueSet => SelectedValue != null;

    partial void OnSelectedValueChanged(string? value)
    {
        TrackValueSet(value != null);
    }

    public override object? ToValue()
    {
        return SelectedValue;
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        object? scopeValue = value.GetValueAt(editingScope);
        SelectedValue = scopeValue as string;
        IsModified = SelectedValue != null;
        UpdateOtherScopesWithData(value, editingScope);
        UpdateInheritedDisplay(value, editingScope);
    }

    protected override void OnResetToInherited()
    {
        SelectedValue = null;
    }
}