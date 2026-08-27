using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Editing;
using Bennewitz.Ninja.OpenCode.Sdk.Tooling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tooling;

/// <summary>
/// Editor for OpenCode's <c>formatter</c> setting: a four-state mode over a map of formatter name →
/// overrides.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic editor.</b> To the schema this is an <c>anyOf</c> over a boolean and an
/// object whose values are themselves objects — a shape the generic dispatch cannot classify, so the
/// whole key renders as raw JSON. Draft 9 of the plan missed the key entirely for that reason: it
/// looked like it already had an editor.
/// </para>
/// <para>
/// Every field here is optional and <c>additionalProperties</c> is <c>false</c>, so an empty entry
/// is legal and is written as <c>{}</c> — the same call the agent editor made, for the same reason:
/// the user added the row.
/// </para>
/// </remarks>
public sealed partial class OpenCodeFormatterEditorViewModel : OpenCodeToolingEditorViewModel
{
    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeFormatterEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Entries = [];
        Entries.CollectionChanged += OnEntriesChanged;
    }

    /// <summary>Formatter entries, in file order.</summary>
    public ObservableCollection<OpenCodeFormatterRowViewModel> Entries { get; }

    /// <summary>Name for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    private string _newEntryName = string.Empty;

    /// <summary>How many entries are held verbatim because they were not objects.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpaqueEntries))]
    private int _opaqueEntryCount;

    /// <summary>True when at least one entry is held verbatim.</summary>
    public bool HasOpaqueEntries => OpaqueEntryCount > 0;

    /// <summary>How many entries carry fields this editor does not surface.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntriesWithExtras))]
    private int _entriesWithExtrasCount;

    /// <summary>True when at least one entry carries unsurfaced fields.</summary>
    public bool HasEntriesWithExtras => EntriesWithExtrasCount > 0;

    /// <summary>Add an entry from <see cref="NewEntryName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        string name = NewEntryName.Trim();
        if (name.Length == 0
            || Entries.Any(e => string.Equals(e.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        Entries.Add(NewEntry(name));
        NewEntryName = string.Empty;

        // Adding an override is only meaningful in the configured mode, and a user who adds one
        // from any other mode plainly means to configure. Better than silently accepting a row
        // that ToValue() would then not write.
        SelectedMode = OptionFor(OpenCodeToolingMode.Configured);
    }

    private bool CanAddEntry() => !string.IsNullOrWhiteSpace(NewEntryName);

    private OpenCodeFormatterRowViewModel NewEntry(string name) =>
        new(name, onRemove: row => Entries.Remove(row));

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the SDK codec so the written shape stays adjacent to the reader. ⚠ Returns
    /// value-currency types only — never a <c>JsonNode</c>, which the currency conversion has no
    /// case for and would stringify, landing the whole block in the file as one quoted string.
    /// </remarks>
    public override object? ToValue()
    {
        if (TryWriteModeOnly(out object? modeOnly))
        {
            return modeOnly;
        }

        return OpenCodeToolingCodec.WriteFormatter(new OpenCodeFormatterConfig
        {
            Mode = OpenCodeToolingMode.Configured,
            Entries = [.. Entries.Select(e => e.ToEntry())],
        });
    }

    /// <inheritdoc />
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ArgumentNullException.ThrowIfNull(value);

        OpenCodeFormatterConfig config = OpenCodeToolingCodec.ReadFormatter(
            value.GetValueAt(editingScope), value.IsDefinedAt(editingScope));

        LoadModeState(value, editingScope, config.Mode, config.Raw, () => LoadEntries(config));
        RefreshDerived();
    }

    /// <remarks>
    /// ⚠⚠ <b>The collection handler is detached for the whole rebuild, and that is what makes the
    /// explicit subscribe below load-bearing rather than decorative.</b> Found by canary: with the
    /// handler still attached, <c>Entries.Add</c> subscribed each row on the way in <i>and</i> the
    /// loop below subscribed it again — every row carried two handlers, so <c>MarkModified</c> fired
    /// twice per keystroke, and deleting the loop entirely changed nothing and broke no test.
    /// Detaching first means exactly one subscription per row.
    /// </remarks>
    private void LoadEntries(OpenCodeFormatterConfig config)
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        try
        {
            foreach (OpenCodeFormatterRowViewModel row in Entries)
            {
                UnsubscribeEntry(row);
            }

            Entries.Clear();
            foreach (OpenCodeFormatterEntry entry in config.Entries)
            {
                OpenCodeFormatterRowViewModel row = NewEntry(entry.Name);
                row.Load(entry);
                Entries.Add(row);
            }
        }
        finally
        {
            Entries.CollectionChanged += OnEntriesChanged;
        }

        // ⚠ Each row's nested collections were filled by Load() BEFORE it was added, so
        // SubscribeEntry's inner loops over the rows already present are the only thing that hooks
        // them. Subscribing just future additions leaves every loaded row silent and Save disabled
        // — the documented trap, reached here by the same route as in the MCP editor.
        foreach (OpenCodeFormatterRowViewModel row in Entries)
        {
            SubscribeEntry(row);
        }
    }

    /// <inheritdoc />
    protected override void OnResetToInherited() => ResetToLastLoaded(ClearEntries);

    private void ClearEntries()
    {
        foreach (OpenCodeFormatterRowViewModel row in Entries)
        {
            UnsubscribeEntry(row);
        }

        Entries.Clear();
    }

    /// <inheritdoc />
    protected override void RefreshDerived()
    {
        EntryCount = Entries.Count;
        OpaqueEntryCount = Entries.Count(e => e.IsOpaqueEntry);
        EntriesWithExtrasCount = Entries.Count(e => e.HasExtras);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeFormatterRowViewModel row in e.OldItems)
            {
                UnsubscribeEntry(row);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeFormatterRowViewModel row in e.NewItems)
            {
                SubscribeEntry(row);
            }
        }

        MarkModified();
    }

    /// <remarks>
    /// ⚠ <b>Three levels, and the third is the one that gets forgotten:</b> the entry's own
    /// properties, its three nested collections, and the rows <i>already inside</i> those
    /// collections.
    /// </remarks>
    private void SubscribeEntry(OpenCodeFormatterRowViewModel row)
    {
        row.PropertyChanged += OnEntryPropertyChanged;

        row.Command.Rows.CollectionChanged += OnNestedCollectionChanged;
        row.Environment.Rows.CollectionChanged += OnNestedCollectionChanged;
        row.Extensions.Rows.CollectionChanged += OnNestedCollectionChanged;

        row.Command.PropertyChanged += OnNestedListPropertyChanged;
        row.Environment.PropertyChanged += OnNestedListPropertyChanged;
        row.Extensions.PropertyChanged += OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel item in row.Command.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel item in row.Environment.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodeStringRowViewModel item in row.Extensions.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }
    }

    /// <remarks>
    /// Mirrors <see cref="SubscribeEntry"/> exactly. Asymmetry means a reload accumulates handlers
    /// and <c>MarkModified</c> fires N times per keystroke, which is invisible until it is a
    /// performance bug.
    /// </remarks>
    private void UnsubscribeEntry(OpenCodeFormatterRowViewModel row)
    {
        row.PropertyChanged -= OnEntryPropertyChanged;

        row.Command.Rows.CollectionChanged -= OnNestedCollectionChanged;
        row.Environment.Rows.CollectionChanged -= OnNestedCollectionChanged;
        row.Extensions.Rows.CollectionChanged -= OnNestedCollectionChanged;

        row.Command.PropertyChanged -= OnNestedListPropertyChanged;
        row.Environment.PropertyChanged -= OnNestedListPropertyChanged;
        row.Extensions.PropertyChanged -= OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel item in row.Command.Rows)
        {
            item.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel item in row.Environment.Rows)
        {
            item.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodeStringRowViewModel item in row.Extensions.Rows)
        {
            item.PropertyChanged -= OnNestedRowPropertyChanged;
        }
    }

    private void OnNestedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object item in e.OldItems)
            {
                Detach(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object item in e.NewItems)
            {
                Attach(item);
            }
        }

        // A row added through the UI arrives with no owner, and every row's reorder CanExecute asks
        // its owner for its index, so ownership is re-stamped across the board after any structural
        // change rather than only on the list that raised this.
        foreach (OpenCodeFormatterRowViewModel row in Entries)
        {
            row.Command.Adopt();
            row.Environment.Adopt();
            row.Extensions.Adopt();
        }

        MarkModified();
    }

    private void Attach(object item)
    {
        switch (item)
        {
            case OpenCodeStringRowViewModel s:
                s.PropertyChanged += OnNestedRowPropertyChanged;
                break;
            case OpenCodePairRowViewModel p:
                p.PropertyChanged += OnNestedRowPropertyChanged;
                break;
            default:
                break;
        }
    }

    private void Detach(object item)
    {
        switch (item)
        {
            case OpenCodeStringRowViewModel s:
                s.PropertyChanged -= OnNestedRowPropertyChanged;
                break;
            case OpenCodePairRowViewModel p:
                p.PropertyChanged -= OnNestedRowPropertyChanged;
                break;
            default:
                break;
        }
    }

    /// <remarks>
    /// ⚠ The filtered names are recomputed by <see cref="RefreshDerived"/>, which
    /// <c>MarkModified</c> calls — so without the filter one keystroke becomes
    /// mark → recount → row changed → mark.
    /// </remarks>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeFormatterRowViewModel.IsEditable)
            or nameof(OpenCodeFormatterRowViewModel.IsOpaqueEntry)
            or nameof(OpenCodeFormatterRowViewModel.HasExtras)
            or nameof(OpenCodeFormatterRowViewModel.ExtraCount))
        {
            return;
        }

        MarkModified();
    }

    /// <remarks>The add boxes only — a keystroke there is not yet an edit to the config.</remarks>
    private void OnNestedListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeStringListViewModel.NewValue)
            or nameof(OpenCodePairListViewModel.NewKey))
        {
            return;
        }

        MarkModified();
    }

    private void OnNestedRowPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        MarkModified();
}
