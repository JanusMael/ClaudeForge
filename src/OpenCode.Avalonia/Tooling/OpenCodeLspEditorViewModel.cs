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
/// Editor for OpenCode's <c>lsp</c> setting: a four-state mode over a map of server name →
/// configuration.
/// </summary>
/// <remarks>
/// <para>
/// Shares the mode control with the <c>formatter</c> editor and nothing else. ⛔ <b>The plan
/// describes both keys as the same per-language shape; the schema disagrees three ways</b> — an
/// <c>lsp</c> entry is a two-arm union, its full arm <b>requires</b> <c>command</c>, its
/// environment key is <c>env</c> rather than <c>environment</c>, and it carries an extra untyped
/// <c>initialization</c> object.
/// </para>
/// <para>
/// ⚠⚠ <b><see cref="IncompleteEntryCount"/> is the point of this editor existing.</b> The entry a
/// user is most likely to want — "turn off the built-in server for this language" — is expressible
/// only as the exact literal <c>{ "disabled": true }</c>; the moment anything else is added, or the
/// toggle is moved to <c>false</c>, the entry matches neither arm and OpenCode rejects the file.
/// A generic object editor renders all of those identically and says nothing. This one counts them
/// and names them.
/// </para>
/// </remarks>
public sealed partial class OpenCodeLspEditorViewModel : OpenCodeToolingEditorViewModel
{
    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeLspEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Entries = [];
        Entries.CollectionChanged += OnEntriesChanged;
    }

    /// <summary>Server entries, in file order.</summary>
    public ObservableCollection<OpenCodeLspRowViewModel> Entries { get; }

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

    /// <summary>
    /// How many entries have content but match neither schema arm.
    /// </summary>
    /// <remarks>
    /// ⚠ Recomputed on every change, not on load only. A count computed once goes stale the moment
    /// the user unticks a toggle — which is precisely the edit that creates one of these.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncompleteEntries))]
    private int _incompleteEntryCount;

    /// <summary>True when at least one entry matches neither arm.</summary>
    public bool HasIncompleteEntries => IncompleteEntryCount > 0;

    /// <summary>How many entries have unparseable initialization JSON.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInitializationErrors))]
    private int _initializationErrorCount;

    /// <summary>True when at least one entry's initialization JSON is invalid.</summary>
    public bool HasInitializationErrors => InitializationErrorCount > 0;

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
        SelectedMode = OptionFor(OpenCodeToolingMode.Configured);
    }

    private bool CanAddEntry() => !string.IsNullOrWhiteSpace(NewEntryName);

    private OpenCodeLspRowViewModel NewEntry(string name) =>
        new(name, onRemove: row => Entries.Remove(row));

    /// <inheritdoc />
    public override object? ToValue()
    {
        if (TryWriteModeOnly(out object? modeOnly))
        {
            return modeOnly;
        }

        return OpenCodeToolingCodec.WriteLsp(new OpenCodeLspConfig
        {
            Mode = OpenCodeToolingMode.Configured,
            Entries = [.. Entries.Select(e => e.ToEntry())],
        });
    }

    /// <inheritdoc />
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ArgumentNullException.ThrowIfNull(value);

        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            value.GetValueAt(editingScope), value.IsDefinedAt(editingScope));

        LoadModeState(value, editingScope, config.Mode, config.Raw, () => LoadEntries(config));
        RefreshDerived();
    }

    /// <remarks>
    /// ⚠⚠ <b>The collection handler is detached for the whole rebuild, and that is what makes the
    /// explicit subscribe below load-bearing rather than decorative.</b> Found by canary: with the
    /// handler still attached, <c>Entries.Add</c> subscribed each row on the way in <i>and</i> the
    /// loop below subscribed it again — every row carried two handlers, so <c>MarkModified</c> fired
    /// twice per keystroke, and deleting the loop entirely changed nothing and broke no test. Both
    /// halves looked correct in isolation. Detaching first means exactly one subscription per row,
    /// and it no longer matters whether <c>CollectionChanged</c> happens to fire during a load.
    /// </remarks>
    private void LoadEntries(OpenCodeLspConfig config)
    {
        Entries.CollectionChanged -= OnEntriesChanged;
        try
        {
            foreach (OpenCodeLspRowViewModel row in Entries)
            {
                UnsubscribeEntry(row);
            }

            Entries.Clear();
            foreach (OpenCodeLspEntry entry in config.Entries)
            {
                OpenCodeLspRowViewModel row = NewEntry(entry.Name);
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
        // them. Subscribing just future additions leaves every loaded row silent and Save disabled.
        foreach (OpenCodeLspRowViewModel row in Entries)
        {
            SubscribeEntry(row);
        }
    }

    /// <inheritdoc />
    protected override void OnResetToInherited() => ResetToLastLoaded(ClearEntries);

    private void ClearEntries()
    {
        foreach (OpenCodeLspRowViewModel row in Entries)
        {
            UnsubscribeEntry(row);
        }

        Entries.Clear();
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⭐ Each row's own <c>NeedsCommand</c> is refreshed here too, so the per-row warning and the
    /// banner count are computed at the same instant from the same source — a row saying it is fine
    /// while the banner counts it would be worse than either warning alone.
    /// </remarks>
    protected override void RefreshDerived()
    {
        foreach (OpenCodeLspRowViewModel row in Entries)
        {
            row.RefreshDerived();
        }

        EntryCount = Entries.Count;
        OpaqueEntryCount = Entries.Count(e => e.IsOpaqueEntry);
        EntriesWithExtrasCount = Entries.Count(e => e.HasExtras);
        IncompleteEntryCount = Entries.Count(e => e.NeedsCommand);
        InitializationErrorCount = Entries.Count(e => e.HasInitializationError);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodeLspRowViewModel row in e.OldItems)
            {
                UnsubscribeEntry(row);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodeLspRowViewModel row in e.NewItems)
            {
                SubscribeEntry(row);
            }
        }

        MarkModified();
    }

    private void SubscribeEntry(OpenCodeLspRowViewModel row)
    {
        row.PropertyChanged += OnEntryPropertyChanged;

        row.Command.Rows.CollectionChanged += OnNestedCollectionChanged;
        row.Extensions.Rows.CollectionChanged += OnNestedCollectionChanged;
        row.Env.Rows.CollectionChanged += OnNestedCollectionChanged;

        row.Command.PropertyChanged += OnNestedListPropertyChanged;
        row.Extensions.PropertyChanged += OnNestedListPropertyChanged;
        row.Env.PropertyChanged += OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel item in row.Command.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodeStringRowViewModel item in row.Extensions.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel item in row.Env.Rows)
        {
            item.PropertyChanged += OnNestedRowPropertyChanged;
        }
    }

    private void UnsubscribeEntry(OpenCodeLspRowViewModel row)
    {
        row.PropertyChanged -= OnEntryPropertyChanged;

        row.Command.Rows.CollectionChanged -= OnNestedCollectionChanged;
        row.Extensions.Rows.CollectionChanged -= OnNestedCollectionChanged;
        row.Env.Rows.CollectionChanged -= OnNestedCollectionChanged;

        row.Command.PropertyChanged -= OnNestedListPropertyChanged;
        row.Extensions.PropertyChanged -= OnNestedListPropertyChanged;
        row.Env.PropertyChanged -= OnNestedListPropertyChanged;

        foreach (OpenCodeStringRowViewModel item in row.Command.Rows)
        {
            item.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodeStringRowViewModel item in row.Extensions.Rows)
        {
            item.PropertyChanged -= OnNestedRowPropertyChanged;
        }

        foreach (OpenCodePairRowViewModel item in row.Env.Rows)
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

        foreach (OpenCodeLspRowViewModel row in Entries)
        {
            row.Command.Adopt();
            row.Extensions.Adopt();
            row.Env.Adopt();
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
    /// ⚠⚠ <b>This filter is load-bearing, not cosmetic.</b> Every name here is recomputed by
    /// <see cref="RefreshDerived"/>, which <c>MarkModified</c> calls — and
    /// <c>RefreshDerived</c> re-raises <c>NeedsCommand</c> on every row, so without the filter one
    /// keystroke becomes mark → refresh → row changed → mark, unbounded. The permission grid's
    /// equivalent filter, removed as a canary, stack-overflowed and aborted the test host.
    /// </remarks>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodeLspRowViewModel.IsEditable)
            or nameof(OpenCodeLspRowViewModel.IsOpaqueEntry)
            or nameof(OpenCodeLspRowViewModel.HasExtras)
            or nameof(OpenCodeLspRowViewModel.ExtraCount)
            or nameof(OpenCodeLspRowViewModel.NeedsCommand)
            or nameof(OpenCodeLspRowViewModel.ShowInitialization)
            or nameof(OpenCodeLspRowViewModel.HasInitializationError))
        {
            return;
        }

        MarkModified();
    }

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
