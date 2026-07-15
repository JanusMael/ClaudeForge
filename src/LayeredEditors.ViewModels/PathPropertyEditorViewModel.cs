namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Editor for file/directory path properties. Shows a text box plus a Browse button
/// when a <see cref="EditorContext.BrowsePath"/> or <see cref="EditorContext.BrowseFile"/>
/// callback is supplied.
/// </summary>
public partial class PathPropertyEditorViewModel : PropertyEditorViewModel
{
    private readonly Func<Task<string?>>? _browseDialog;

    public PathPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope,
                                       Func<Task<string?>>? browseDialog = null)
        : base(schema, editingScope)
    {
        _browseDialog = browseDialog;
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

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_browseDialog == null)
        {
            return;
        }

        string? result = await _browseDialog();
        if (result != null)
        {
            Value = result;
        }
    }

    protected override void OnResetToInherited()
    {
        Value = null;
    }
}