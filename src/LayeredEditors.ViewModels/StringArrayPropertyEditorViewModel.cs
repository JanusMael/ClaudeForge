using System.Collections.ObjectModel;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

/// <summary>
/// Editor for string-array properties — shows a list with Add/Remove controls.
/// Mixed-type arrays: non-string elements are coerced to their string representation.
/// </summary>
public partial class StringArrayPropertyEditorViewModel : PropertyEditorViewModel
{
    public StringArrayPropertyEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Items = [];
        Items.CollectionChanged += (_, _) =>
        {
            // Routed through the shared TrackValueSet helper.
            TrackValueSet(Items.Count > 0);
            OnPropertyChanged(nameof(IsValueSet));
        };
    }

    public ObservableCollection<string> Items { get; }

    public bool IsValueSet => Items.Count > 0;

    /// <summary>
    /// Watermark for the "add item" text box — shows the first example value from the schema
    /// if any examples are defined, otherwise falls back to a generic "Add item…" prompt.
    /// </summary>
    public string AddItemWatermark =>
        Schema.Examples.Count > 0
            ? $"e.g. {Schema.Examples[0]}"
            : "Add item…";

    [ObservableProperty] private string _newItemText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        string text = NewItemText.Trim();
        if (string.IsNullOrEmpty(text) || Items.Contains(text))
        {
            return;
        }

        Items.Add(text);
        NewItemText = string.Empty;
    }

    private bool CanAddItem()
    {
        return !string.IsNullOrWhiteSpace(NewItemText);
    }

    [RelayCommand]
    private void RemoveItem(string item)
    {
        Items.Remove(item);
    }

    partial void OnNewItemTextChanged(string value)
    {
        AddItemCommand.NotifyCanExecuteChanged();
    }

    public override object? ToValue()
    {
        if (Items.Count == 0)
        {
            return null;
        }

        return Items.Select(s => (object?)s).ToList();
    }

    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        EditingScope = editingScope;
        EffectiveScope = value.EffectiveScope;
        IsOverridden = value.IsOverridden;

        Items.Clear();
        object? scopeValue = value.GetValueAt(editingScope);
        if (scopeValue is IReadOnlyList<object?> list)
        {
            foreach (object? item in list)
            {
                string? s = item as string ?? item?.ToString();
                if (s != null)
                {
                    Items.Add(s);
                }
            }
        }

        IsModified = Items.Count > 0;
        UpdateOtherScopesWithData(value, editingScope);
        UpdateInheritedDisplay(value, editingScope);
    }

    protected override void OnResetToInherited()
    {
        Items.Clear();
    }
}