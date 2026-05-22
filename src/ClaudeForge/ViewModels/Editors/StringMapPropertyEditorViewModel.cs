using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Editor for object-typed schema nodes whose <c>additionalProperties</c> is
/// <c>{type: "string"}</c> — i.e. a free-form string→string map. Renders as a
/// row table where each row is a <see cref="StringMapEntryViewModel"/>:
/// an optional-suggestion <see cref="Avalonia.Controls.AutoCompleteBox"/> for
/// the key, a plain <see cref="Avalonia.Controls.TextBox"/> for the value, and
/// a remove button. A "+ Add" row at the bottom appends new entries.
/// </summary>
/// <remarks>
/// <para>
/// Used by <c>modelOverrides</c> on the Code Settings page; the factory injects
/// the same model-id list that the standalone <c>model</c> editor offers
/// (sonnet / opus / haiku / claude-sonnet-4-5 / claude-opus-4-5) as
/// <see cref="KeySuggestions"/>, so the user picks a known model on the LEFT
/// and types the provider-specific override on the RIGHT — the same
/// "string in, string out" experience as the simple <c>model</c> field, just
/// with the additional structural row this property's schema requires.
/// </para>
/// <para>
/// Tolerates pre-existing bad on-disk data (e.g. a bare string written by the
/// older silent-corruption fallback): when the loaded scope value is not a
/// JsonObject, the rows simply start empty. The user can save to replace the
/// bad data with a well-formed object, or click Reset to clear the property
/// outright.
/// </para>
/// </remarks>
public partial class StringMapPropertyEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;

    // Reset semantic consistency.  See McpServerListEditorViewModel
    // for rationale.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    public StringMapPropertyEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IReadOnlyList<string>? keySuggestions = null)
        : base(schema, editingScope)
    {
        KeySuggestions = keySuggestions ?? [];
        Items = [];
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    /// <inheritdoc/>
    protected override bool IsLoading => _isLoading;

    /// <summary>
    /// Optional autocomplete suggestions for the key column. Populated by the
    /// factory at construction time from sibling schema knowledge (e.g. for
    /// <c>modelOverrides</c>, the list mirrors the <c>model</c> property's
    /// <c>examples</c>). Empty when no suggestions apply — the AutoCompleteBox
    /// degrades to a plain free-text input.
    /// </summary>
    public IReadOnlyList<string> KeySuggestions { get; }

    /// <summary>The current entry rows. Bound to an ItemsControl in the View.</summary>
    public ObservableCollection<StringMapEntryViewModel> Items { get; }

    /// <summary>Watermark for the new-key input.</summary>
    public string NewKeyWatermark => "model id";

    /// <summary>Watermark for the new-value input.</summary>
    public string NewValueWatermark => "override value";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private string _newKeyText = string.Empty;

    [ObservableProperty] private string _newValueText = string.Empty;

    /// <summary>
    /// Adds a new <see cref="StringMapEntryViewModel"/> from the new-key /
    /// new-value inputs. Disabled until the new-key textbox has a non-blank
    /// value (the value is allowed to be empty — that mirrors the on-disk
    /// shape where the schema permits any string, including empty).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        string key = NewKeyText.Trim();
        if (key.Length == 0)
        {
            return;
        }

        // Replace an existing entry with the same key rather than producing
        // duplicates that would silently lose one of them when ToJsonValue
        // builds the JsonObject.
        StringMapEntryViewModel? existing = Items.FirstOrDefault(e => e.Key == key);
        if (existing is not null)
        {
            existing.Value = NewValueText;
        }
        else
        {
            Items.Add(new StringMapEntryViewModel(key, NewValueText));
        }

        NewKeyText = string.Empty;
        NewValueText = string.Empty;
    }

    private bool CanAddEntry()
    {
        return !string.IsNullOrWhiteSpace(NewKeyText);
    }

    /// <summary>Remove the entry from <see cref="Items"/>.</summary>
    [RelayCommand]
    private void RemoveEntry(StringMapEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        Items.Remove(entry);
    }

    // -----------------------------------------------------------------------
    // Workspace round-trip
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public override JsonNode? ToJsonValue()
    {
        if (Items.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (StringMapEntryViewModel entry in Items)
        {
            // Skip blank keys; they would round-trip to `"":""` which is
            // never what the user intended and would re-trigger the schema
            // banner on next reload.
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            obj[entry.Key] = JsonValue.Create(entry.Value ?? string.Empty);
        }

        return obj.Count > 0 ? obj : null;
    }

    /// <inheritdoc/>
    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            // Detach existing items before clearing so their PropertyChanged
            // handlers don't fire spurious modified events during the rebuild.
            foreach (StringMapEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();

            JsonNode? scopeValue = layered.GetValueAt(editingScope);
            if (scopeValue is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> kvp in obj)
                {
                    string value = kvp.Value is JsonValue jv && jv.TryGetValue(out string? s)
                        ? s
                        // Tolerate non-string values — coerce to string so the
                        // user can edit and rewrite them rather than crashing
                        // on bad inputs.
                        : kvp.Value?.ToJsonString() ?? string.Empty;
                    StringMapEntryViewModel entry = new(kvp.Key, value);
                    entry.PropertyChanged += OnEntryChanged;
                    Items.Add(entry);
                }

                IsModified = obj.Count > 0;
            }
            else
            {
                // Bad on-disk data (bare string from the old corrupting
                // fallback, or unexpected shape). Start empty; the user can
                // save to replace, or reset to clear.
                IsModified = false;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <inheritdoc/>
    protected override void OnResetToInherited()
    {
        // Reset semantic consistency: prefer reload from the
        // cached snapshot so unsaved edits revert to the at-load entries
        // rather than wiping everything.  See McpServerListEditorViewModel.
        if (_lastLayered is not null)
        {
            NewKeyText = string.Empty;
            NewValueText = string.Empty;
            LoadFromLayered(_lastLayered, _lastScope);
            return;
        }

        // Fallback path — no snapshot.  Suppress MarkModified while we tear
        // down the rows — Items.Clear() would otherwise re-flag IsModified=true
        // through OnItemsCollectionChanged and undo the base class's
        // IsModified=false that fired just before us.
        _isLoading = true;
        try
        {
            foreach (StringMapEntryViewModel item in Items)
            {
                item.PropertyChanged -= OnEntryChanged;
            }

            Items.Clear();
            NewKeyText = string.Empty;
            NewValueText = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // -----------------------------------------------------------------------
    // Change propagation
    // -----------------------------------------------------------------------

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (StringMapEntryViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnEntryChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (StringMapEntryViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnEntryChanged;
            }
        }

        // A row was added or removed — propagate as a modification through
        // the standard MarkModified helper so the live-write loop runs.
        MarkModified();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StringMapEntryViewModel.Key)
            or nameof(StringMapEntryViewModel.Value))
        {
            MarkModified();
        }
    }
}

/// <summary>One row in a <see cref="StringMapPropertyEditorViewModel"/>'s table.</summary>
public partial class StringMapEntryViewModel : ObservableObject
{
    public StringMapEntryViewModel(string key, string value)
    {
        _key = key;
        _value = value;
    }

    [ObservableProperty] private string _key;

    [ObservableProperty] private string _value;
}