using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;

/// <summary>
/// Editor for the TUI's <c>keybinds</c>: 184 actions, each a four-arm union nested three deep.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic editor.</b> Spike S6 measured it: all 184 children classify as
/// <c>Complex</c> with no children and no enum, so the generic dispatch lands every one of them on
/// the raw-JSON fallback — <b>184 JSON text boxes</b>, which is 86% of the TUI schema's node tree and
/// 99% of the file. This editor is the reason that page is usable at all.
/// </para>
/// <para>
/// ⭐ <b>Search is the primary control, not a convenience.</b> Nobody scrolls 184 rows to find one
/// action, so the filter matches the action name, the schema's description, and the current binding's
/// summary — the last of those is what answers "what is Ctrl+K bound to?", which is the question the
/// list form otherwise cannot answer.
/// </para>
/// <para>
/// ⭐ <b>Clash detection is the thing only a cross-row view can do</b>, and it is the same division of
/// labour the permission grid draws for shadowed rules: a row cannot see its neighbours, so the
/// editor computes it and pushes the result down. ⚠ It reports that two actions name the same key and
/// says <b>nothing about which one wins</b> — the schema states no precedence, and inventing one
/// would be the same overreach as claiming plugin array order is load order.
/// </para>
/// <para>
/// ⚠ <b>No <c>CollectionChanged</c> handler anywhere</b>, and no <c>PropertyChanged</c> subscription
/// on a row either. A row reports through the <c>onChanged</c> callback it is constructed with, and
/// that is the <b>only</b> mechanism. Both halves of that matter, and both were measured:
/// </para>
/// <list type="bullet">
///   <item>
///     A <c>CollectionChanged</c> handler is the trap 9a-8's canary found in three editors — the
///     <c>_isLoading</c> guard suppresses <c>MarkModified</c> but not the subscription, so a handler
///     left attached during a rebuild hooks every row twice.
///   </item>
///   <item>
///     ⚠⚠ A <c>PropertyChanged</c> subscription <i>alongside</i> the callback is the same defect by
///     another route, and this editor shipped with it until the exact-count guard caught it: one
///     keystroke raised <c>IsModified</c> <b>twice</b> and one mode change raised it <b>seven
///     times</b>, because every <c>[NotifyPropertyChangedFor]</c> target is itself a
///     <c>PropertyChanged</c>. Each of those runs a full <see cref="RefreshDerived"/> over every row
///     plus an <see cref="ApplyFilter"/> that resets the collection a virtualizing list is bound to —
///     so the visible list was rebuilt seven times for one click. An exclusion filter cannot fix it:
///     it has to name every derived property and forgetting one is silent.
///   </item>
/// </list>
/// <para>
/// The guard is <c>ReloadingDoesNotAccumulateRowSubscriptions</c> and the assertion is an exact
/// <c>== 1</c>. A bounded <c>1..4</c> assertion tolerates precisely the doubling that was there.
/// </para>
/// </remarks>
public sealed partial class OpenCodeKeybindEditorViewModel : PropertyEditorViewModel
{
    private readonly Dictionary<string, OpenCodeKeybindActionViewModel> _byAction =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Where each action stood in the file, so a save does not reorder what was already there.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>184 keys is why this exists.</b> The <c>mcp</c> and <c>formatter</c> codecs write an
    /// entry's keys in schema order because six keys carry no meaning and the reshuffle is trivial.
    /// Reordering up to 184 turns a one-line change into a diff nobody can review, so actions the
    /// file already stated keep their place and newly-set ones are appended in schema order.
    /// </remarks>
    private readonly Dictionary<string, int> _fileOrder = new(StringComparer.Ordinal);

    private bool _isLoading;
    private bool _wasDefined;
    private object? _rawValue;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeKeybindEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        BuildRowsFromSchema(schema);
        ApplyFilter();
        RefreshDerived();
    }

    /// <summary>Every action, in schema order, followed by any the file added.</summary>
    public ObservableCollection<OpenCodeKeybindActionViewModel> Actions { get; } = [];

    /// <summary>The actions the current filter admits.</summary>
    /// <remarks>
    /// A separate collection rather than a predicate on <see cref="Actions"/>, because the view binds
    /// a virtualizing list to it and re-filtering must be one collection reset rather than 184
    /// visibility toggles.
    /// </remarks>
    public ObservableCollection<OpenCodeKeybindActionViewModel> FilteredActions { get; } = [];

    /// <summary>The groups on offer, with the all-groups placeholder first.</summary>
    public ObservableCollection<string> Groups { get; } = [];

    /// <summary>The placeholder the group picker shows for "every group".</summary>
    public static string AllGroups => Strings.KeybindAllGroups;

    /// <summary>Free text matched against the action, its description and its current binding.</summary>
    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>The selected group, or <see cref="AllGroups"/>.</summary>
    [ObservableProperty] private string _selectedGroup = Strings.KeybindAllGroups;

    /// <summary>True to hide every action this file says nothing about.</summary>
    /// <remarks>
    /// ⭐ The filter people actually want first: 184 rows of "not set" is what the page looks like on
    /// a fresh config, and "show me what I have changed" is the question that makes it navigable.
    /// </remarks>
    [ObservableProperty] private bool _onlyShowSet;

    /// <summary>How many actions this editor will write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSet))]
    private int _setCount;

    /// <summary>How many actions name no key, so the schema rejects them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomplete))]
    private int _incompleteCount;

    /// <summary>How many actions the schema does not declare.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnknown))]
    private int _unknownCount;

    /// <summary>How many actions share a key with another action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private int _conflictCount;

    /// <summary>How many actions the filter is currently hiding.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHidden))]
    private int _hiddenCount;

    /// <summary>True when the whole value was present but unreadable.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceUnrecognisedCommand))]
    private bool _isUnrecognised;

    /// <summary>The unreadable value as JSON, so it can be read and copied.</summary>
    [ObservableProperty] private string _unrecognisedText = string.Empty;

    /// <summary>True when at least one action is set.</summary>
    public bool HasSet => SetCount > 0;

    /// <summary>True when at least one action names no key.</summary>
    public bool HasIncomplete => IncompleteCount > 0;

    /// <summary>True when at least one action is not declared by the schema.</summary>
    public bool HasUnknown => UnknownCount > 0;

    /// <summary>True when at least two actions share a key.</summary>
    public bool HasConflicts => ConflictCount > 0;

    /// <summary>True when the filter is hiding something.</summary>
    public bool HasHidden => HiddenCount > 0;

    /// <inheritdoc />
    /// <remarks>
    /// ⚠ <b>Returns <see langword="null"/> only when the key was absent AND nothing is set.</b> An
    /// empty <c>keybinds: {}</c> that the user has not added to writes <c>{}</c> back, because that is
    /// the file they opened — the same distinction the <c>formatter</c> codec draws between an absent
    /// key and an empty object.
    /// </remarks>
    public override object? ToValue()
    {
        if (IsUnrecognised)
        {
            return _rawValue;
        }

        List<OpenCodeKeybindEntry> entries = [];
        foreach (OpenCodeKeybindActionViewModel row in Ordered())
        {
            entries.Add(new OpenCodeKeybindEntry(row.Action, row.ToValue(), row.IsKnown));
        }

        bool anySet = entries.Exists(e => e.Value.Mode != OpenCodeKeybindMode.NotSet);
        if (!anySet && !_wasDefined)
        {
            return null;
        }

        return OpenCodeKeybindCodec.Write(new OpenCodeKeybindConfig
        {
            IsDefined = true,
            Entries = entries,
        });
    }

    /// <inheritdoc />
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ArgumentNullException.ThrowIfNull(value);

        _isLoading = true;
        try
        {
            _lastValue = value;
            _lastScope = editingScope;

            EditingScope = editingScope;
            EffectiveScope = value.EffectiveScope;
            IsOverridden = value.IsOverridden;

            OpenCodeKeybindConfig config = OpenCodeKeybindCodec.Read(
                value.GetValueAt(editingScope),
                value.IsDefinedAt(editingScope),
                new HashSet<string>(_byAction.Keys, StringComparer.Ordinal));

            _wasDefined = config.IsDefined;
            IsUnrecognised = config.IsOpaque;
            _rawValue = config.IsOpaque ? config.Raw : null;
            UnrecognisedText = config.IsOpaque ? Format(config.Raw) : string.Empty;

            ResetRows();
            _fileOrder.Clear();

            int position = 0;
            foreach (OpenCodeKeybindEntry entry in config.Entries)
            {
                _fileOrder[entry.Action] = position++;

                if (!_byAction.TryGetValue(entry.Action, out OpenCodeKeybindActionViewModel? row))
                {
                    // An action the schema does not declare. `keybinds` sets
                    // additionalProperties: false, so this is a violation — which is exactly why it
                    // gets a row instead of being dropped. A config from a newer OpenCode that has
                    // since added the action is the ordinary way to meet one.
                    //
                    // ⚠ The row is NOT marked unknown here. `Load` below assigns `IsKnown` from the
                    // codec's own determination two statements later, so an assignment here would
                    // be dead code that reads as the mechanism — a canary proved it: flipping it to
                    // `true` changed no test. The codec is the single source of that fact.
                    row = NewRow(entry.Action, Strings.KeybindUnknownAction, GroupOf(entry.Action));
                    _byAction[entry.Action] = row;
                    Actions.Add(row);
                }

                row.Load(entry.Value, entry.IsKnown);
            }

            RebuildGroups();
            ApplyFilter();
            RefreshDerived();

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <inheritdoc />
    protected override void OnResetToInherited()
    {
        if (_lastValue is { } value && _lastScope is { } scope)
        {
            LoadFromValue(value, scope);
            IsModified = false;
            return;
        }

        _isLoading = true;
        try
        {
            IsUnrecognised = false;
            _rawValue = null;
            UnrecognisedText = string.Empty;
            _wasDefined = false;
            _fileOrder.Clear();
            ResetRows();
            RebuildGroups();
            ApplyFilter();
            RefreshDerived();
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Leave the held state, discarding the unreadable value for an editable page.</summary>
    [RelayCommand(CanExecute = nameof(IsUnrecognised))]
    private void ReplaceUnrecognised()
    {
        IsUnrecognised = false;
        _rawValue = null;
        UnrecognisedText = string.Empty;
        _wasDefined = true;
        MarkModified();
    }

    /// <summary>Clear the search box, the group picker and the only-set filter.</summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        SelectedGroup = AllGroups;
        OnlyShowSet = false;
    }

    /// <summary>Build one row per action the schema declares, in schema order.</summary>
    /// <remarks>
    /// ⚠ <b>Rows are created ONCE, here, and reloaded in place.</b> 184 allocations per load would be
    /// affordable, but rebuilding would also discard the user's filter state and every row's
    /// expansion — and a rebuild is the situation in which the double-subscription defect arises.
    /// Creating once means each row is subscribed exactly once for the editor's whole life.
    /// </remarks>
    private void BuildRowsFromSchema(IEditorSchema schema)
    {
        foreach (IEditorSchema action in schema.Properties)
        {
            if (_byAction.ContainsKey(action.Name))
            {
                continue;
            }

            OpenCodeKeybindActionViewModel row = NewRow(
                action.Name,
                // The description IS the label. There is no default to show beside it — checked, not
                // assumed: none of the 184 actions declares one.
                action.Description ?? action.Title ?? string.Empty,
                GroupOf(action.Name));

            _byAction[action.Name] = row;
            Actions.Add(row);
        }

        RebuildGroups();
    }

    private OpenCodeKeybindActionViewModel NewRow(string action, string label, string group) =>
        new(action, label, group, isKnown: true, OnRowChanged);

    /// <summary>Return every row to "not set", and drop the rows only a file created.</summary>
    private void ResetRows()
    {
        for (int i = Actions.Count - 1; i >= 0; i--)
        {
            OpenCodeKeybindActionViewModel row = Actions[i];
            if (!row.IsKnown)
            {
                _byAction.Remove(row.Action);
                Actions.RemoveAt(i);
                continue;
            }

            row.Load(new OpenCodeKeybindValue(), isKnown: true);
        }
    }

    /// <summary>The rows in the order they will be written.</summary>
    /// <remarks>
    /// Actions the file already stated keep their place; newly-set ones follow in schema order. The
    /// sort is stable, so the second key is the row's own position rather than a lookup.
    /// </remarks>
    private IEnumerable<OpenCodeKeybindActionViewModel> Ordered() =>
        Actions
            .Select((Row, Index) => (Row, Index))
            .OrderBy(e => _fileOrder.TryGetValue(e.Row.Action, out int at) ? at : int.MaxValue)
            .ThenBy(e => e.Index)
            .Select(e => e.Row);

    private void RebuildGroups()
    {
        List<string> groups = [AllGroups, .. Actions
            .Select(a => a.Group)
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(g => g, StringComparer.Ordinal)];

        if (Groups.SequenceEqual(groups, StringComparer.Ordinal))
        {
            return;
        }

        string wanted = SelectedGroup;
        Groups.Clear();
        foreach (string group in groups)
        {
            Groups.Add(group);
        }

        SelectedGroup = Groups.Contains(wanted, StringComparer.Ordinal) ? wanted : AllGroups;
    }

    /// <summary>
    /// The group an action is filed under, from the first segment of its name.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The 184 names use TWO separators, not one.</b> Measured: 168 are <c>snake_case</c>
    /// (<c>app_exit</c>), 16 are dot-separated (<c>dialog.select.prev</c>,
    /// <c>prompt.autocomplete.hide</c>, <c>permission.prompt.fullscreen</c>), and one — <c>leader</c>
    /// — has neither. Splitting on <c>_</c> alone scatters those 16 into their own single-action
    /// groups; splitting on both yields 34 real groups. A grouping that looked right on the first
    /// dozen names would have been wrong for the last sixteen.
    /// </remarks>
    internal static string GroupOf(string action)
    {
        if (string.IsNullOrEmpty(action))
        {
            return string.Empty;
        }

        int cut = action.IndexOfAny(['_', '.']);
        return cut <= 0 ? action : action[..cut];
    }

    /// <summary>Repopulate <see cref="FilteredActions"/> from the current filter.</summary>
    private void ApplyFilter()
    {
        string needle = FilterText.Trim();
        bool hasNeedle = needle.Length > 0;
        bool byGroup = !string.Equals(SelectedGroup, AllGroups, StringComparison.Ordinal);

        FilteredActions.Clear();
        foreach (OpenCodeKeybindActionViewModel row in Actions)
        {
            if (OnlyShowSet && !row.IsSet)
            {
                continue;
            }

            if (byGroup && !string.Equals(row.Group, SelectedGroup, StringComparison.Ordinal))
            {
                continue;
            }

            // Matched case-insensitively against three fields. The binding summary is the one that
            // earns its keep: it is what answers "what is this key already bound to?", which is the
            // question 184 rows of action names cannot.
            if (hasNeedle
                && !row.Action.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !row.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !row.Summary.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredActions.Add(row);
        }

        HiddenCount = Actions.Count - FilteredActions.Count;
    }

    /// <summary>Recompute the banners and the cross-row clash flags.</summary>
    private void RefreshDerived()
    {
        int set = 0;
        int incomplete = 0;
        int unknown = 0;

        Dictionary<string, List<OpenCodeKeybindActionViewModel>> byKey =
            new(StringComparer.Ordinal);

        foreach (OpenCodeKeybindActionViewModel row in Actions)
        {
            if (row.IsSet)
            {
                set++;
            }

            if (row.IsIncomplete)
            {
                incomplete++;
            }

            if (!row.IsKnown)
            {
                unknown++;
            }

            foreach (string key in row.ComparableKeys())
            {
                if (!byKey.TryGetValue(key, out List<OpenCodeKeybindActionViewModel>? sharing))
                {
                    sharing = [];
                    byKey[key] = sharing;
                }

                // An action may bind the same key twice in one sequence. That is odd but it is not a
                // clash BETWEEN actions, and reporting it as one would name the row against itself.
                if (!sharing.Contains(row))
                {
                    sharing.Add(row);
                }
            }
        }

        Dictionary<OpenCodeKeybindActionViewModel, SortedSet<string>> clashes = [];
        foreach (List<OpenCodeKeybindActionViewModel> sharing in byKey.Values)
        {
            if (sharing.Count < 2)
            {
                continue;
            }

            foreach (OpenCodeKeybindActionViewModel row in sharing)
            {
                if (!clashes.TryGetValue(row, out SortedSet<string>? others))
                {
                    others = new SortedSet<string>(StringComparer.Ordinal);
                    clashes[row] = others;
                }

                foreach (OpenCodeKeybindActionViewModel other in sharing)
                {
                    if (!ReferenceEquals(other, row))
                    {
                        others.Add(other.Action);
                    }
                }
            }
        }

        foreach (OpenCodeKeybindActionViewModel row in Actions)
        {
            row.ConflictWith = clashes.TryGetValue(row, out SortedSet<string>? others)
                ? string.Join(", ", others)
                : string.Empty;
        }

        SetCount = set;
        IncompleteCount = incomplete;
        UnknownCount = unknown;
        ConflictCount = clashes.Count;
    }

    /// <summary>Called by a row after any change the user made to it.</summary>
    private void OnRowChanged()
    {
        if (_isLoading)
        {
            return;
        }

        MarkModified();
    }

    /// <summary>
    /// Force-fire <c>PropertyChanged(IsModified)</c> on every user mutation, even when the flag was
    /// already true from the prior load.
    /// </summary>
    private void MarkModified()
    {
        if (_isLoading)
        {
            return;
        }

        RefreshDerived();

        // The filter reads Summary and IsSet, both of which an edit can change — so a row that no
        // longer matches has to leave the list, or the only-set filter would show rows the user has
        // just unset.
        ApplyFilter();

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedGroupChanged(string value) => ApplyFilter();

    partial void OnOnlyShowSetChanged(bool value) => ApplyFilter();

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? "null";
}
