using System.Collections.ObjectModel;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.OpenCode.Sdk.Plugins;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Plugins;

/// <summary>
/// One element of a <c>plugin</c> array.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>The options object is edited as raw JSON, and that is the honest control for it.</b> The
/// schema types the tuple's second element as <c>object</c> with no declared properties — there is
/// no shape to render fields for. A text box that says "JSON" is truthful; a pretend form would
/// invent structure the schema does not have.
/// </para>
/// <para>
/// ⚠ <b>Toggling options off keeps the text.</b> Flipping an entry between <c>"foo"</c> and
/// <c>["foo", {…}]</c> must not destroy the options the user typed — the same
/// preserve-the-other-arm rule the MCP and permission editors follow, and the third place in this
/// phase it has mattered.
/// </para>
/// </remarks>
public sealed partial class OpenCodePluginRowViewModel : ObservableObject
{
    private object? _lastGoodOptions;
    private object? _raw;
    private bool _isOpaque;

    /// <summary>The plugin specifier, verbatim.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>True when this element writes the two-element tuple form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    private bool _hasOptions;

    /// <summary>The options object as JSON text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionsError))]
    private string _optionsText = string.Empty;

    /// <summary>True when the options editor should be shown.</summary>
    public bool ShowOptions => HasOptions && !IsOpaqueEntry;

    /// <summary>True when the element matched neither arm and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    private bool _isOpaqueEntry;

    /// <summary>True when this element has editable fields.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>
    /// True when <see cref="OptionsText"/> is not valid JSON for an object.
    /// </summary>
    /// <remarks>
    /// ⚠ While set, <see cref="ToEntry"/> keeps the LAST GOOD options rather than dropping them.
    /// Half-typed JSON is the normal state of a JSON text box, and treating every intermediate
    /// keystroke as "no options" would delete the user's configuration on a live-write host before
    /// they finished typing it.
    /// </remarks>
    public bool HasOptionsError => HasOptions && !TryParseOptions(OptionsText, out _);

    /// <summary>The list holding this element, or null while detached.</summary>
    internal OpenCodePluginListViewModel? Owner { get; set; }

    /// <summary>Remove this element.</summary>
    [RelayCommand]
    private void Remove() => Owner?.Remove(this);

    /// <summary>Move this element one position earlier.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Owner?.Move(this, -1);

    /// <summary>Move this element one position later.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Owner?.Move(this, +1);

    private bool CanMoveUp() => Owner is { } o && o.Rows.IndexOf(this) > 0;

    private bool CanMoveDown() =>
        Owner is { } o && o.Rows.IndexOf(this) is var i && i >= 0 && i < o.Rows.Count - 1;

    /// <summary>Re-evaluate the reorder commands after the list changed shape around this row.</summary>
    internal void RefreshMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Populate from a parsed element.</summary>
    internal void Load(OpenCodePluginEntry entry)
    {
        _raw = entry.Raw;
        _isOpaque = entry.IsOpaque;
        _lastGoodOptions = entry.Options;

        IsOpaqueEntry = entry.IsOpaque;
        Name = entry.Name ?? string.Empty;
        HasOptions = entry.HasOptions;
        OptionsText = entry.Options is null ? string.Empty : Format(entry.Options);
    }

    /// <summary>The model form of this element.</summary>
    internal OpenCodePluginEntry ToEntry()
    {
        if (_isOpaque)
        {
            return new OpenCodePluginEntry { IsOpaque = true, Raw = _raw };
        }

        if (!HasOptions)
        {
            return new OpenCodePluginEntry { Name = Name };
        }

        if (TryParseOptions(OptionsText, out object? parsed))
        {
            _lastGoodOptions = parsed;
            return new OpenCodePluginEntry { Name = Name, Options = parsed };
        }

        // Unparseable mid-edit: keep whatever last parsed, so the tuple form survives typing. An
        // empty object is the floor — dropping to the bare-string form would silently change which
        // arm of the union the file uses.
        return new OpenCodePluginEntry
        {
            Name = Name,
            Options = _lastGoodOptions ?? new OrderedPropertyMap(),
        };
    }

    /// <summary>
    /// Parse JSON text into a value-currency object. Blank counts as an empty object.
    /// </summary>
    /// <remarks>
    /// Blank is valid on purpose: the tuple form's second element may legitimately be <c>{}</c>, and
    /// requiring the user to type braces to express "options, but none yet" would be busywork.
    /// </remarks>
    internal static bool TryParseOptions(string text, out object? options)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            options = new OrderedPropertyMap();
            return true;
        }

        try
        {
            object? currency = JsonCurrency.FromJsonNode(System.Text.Json.Nodes.JsonNode.Parse(text));

            // Must be an OBJECT: the schema's prefixItems types this element as `object`, so an
            // array or a scalar here would produce a config OpenCode rejects.
            if (currency is IReadOnlyDictionary<string, object?>)
            {
                options = currency;
                return true;
            }
        }
        catch (JsonException)
        {
            // Falls through to the failure result — a JSON text box is unparseable most of the time
            // it is being used, so this is an expected state rather than an error to log.
        }

        options = null;
        return false;
    }

    private static string Format(object? options) =>
        JsonCurrency.ToJsonNode(options)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? string.Empty;
}

/// <summary>
/// A <c>plugin</c> array: an ordered, editable list of plugin specifiers.
/// </summary>
/// <remarks>
/// Order is preserved as written. Reordering is offered because this is a list a user may want to
/// arrange — <b>not</b> because the schema assigns array position any meaning, which it does not
/// state either way.
/// </remarks>
public sealed partial class OpenCodePluginListViewModel : ObservableObject
{
    /// <summary>The elements, in file order.</summary>
    public ObservableCollection<OpenCodePluginRowViewModel> Rows { get; } = [];

    /// <summary>Specifier for the add box. Transient.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = string.Empty;

    /// <summary>Append an element from <see cref="NewName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        string name = NewName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        Rows.Add(new OpenCodePluginRowViewModel { Name = name });
        NewName = string.Empty;
        Adopt();
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewName);

    /// <summary>Remove <paramref name="row"/>.</summary>
    internal void Remove(OpenCodePluginRowViewModel row) => Rows.Remove(row);

    /// <summary>Move <paramref name="row"/> by <paramref name="offset"/>, clamped to the list.</summary>
    internal void Move(OpenCodePluginRowViewModel row, int offset)
    {
        int index = Rows.IndexOf(row);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
    }

    /// <summary>Replace every row, for the load path.</summary>
    internal void Reset(IEnumerable<OpenCodePluginEntry> entries)
    {
        Rows.Clear();
        foreach (OpenCodePluginEntry entry in entries)
        {
            OpenCodePluginRowViewModel row = new();
            row.Load(entry);
            Rows.Add(row);
        }

        Adopt();
    }

    /// <summary>Stamp ownership on every row and refresh their reorder commands.</summary>
    /// <remarks>
    /// Re-stamped on every structural change rather than only on insert: a row's reorder
    /// <c>CanExecute</c> asks the owner for its index, so a row added before this list existed
    /// would otherwise keep disabled buttons for the editor's whole life.
    /// </remarks>
    internal void Adopt()
    {
        foreach (OpenCodePluginRowViewModel row in Rows)
        {
            row.Owner = this;
            row.RefreshMoveCommands();
        }
    }

    /// <summary>The elements as the model sees them.</summary>
    internal IReadOnlyList<OpenCodePluginEntry> ToEntries() => [.. Rows.Select(r => r.ToEntry())];
}

/// <summary>
/// One entry of the TUI's <c>plugin_enabled</c> map.
/// </summary>
public sealed partial class OpenCodePluginToggleViewModel : ObservableObject
{
    private object? _raw;

    /// <summary>The plugin name, verbatim.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Whether the plugin is enabled.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>True when the value was not a boolean and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isOpaqueEntry;

    /// <summary>True when the toggle is editable.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>The list holding this toggle, or null while detached.</summary>
    internal OpenCodePluginToggleListViewModel? Owner { get; set; }

    /// <summary>Remove this toggle.</summary>
    [RelayCommand]
    private void Remove() => Owner?.Remove(this);

    /// <summary>Populate from a parsed entry.</summary>
    internal void Load(OpenCodePluginToggle toggle)
    {
        _raw = toggle.Raw;
        Name = toggle.Name;
        IsOpaqueEntry = toggle.IsOpaque;
        Enabled = toggle.Enabled ?? false;
    }

    /// <summary>The model form of this toggle.</summary>
    internal OpenCodePluginToggle ToToggle() =>
        IsOpaqueEntry
            ? new OpenCodePluginToggle { Name = Name, Raw = _raw }
            : new OpenCodePluginToggle { Name = Name, Enabled = Enabled };
}

/// <summary>
/// The TUI's <c>plugin_enabled</c> map.
/// </summary>
public sealed partial class OpenCodePluginToggleListViewModel : ObservableObject
{
    /// <summary>The toggles, in file order.</summary>
    public ObservableCollection<OpenCodePluginToggleViewModel> Rows { get; } = [];

    /// <summary>Name for the add box. Transient.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _newName = string.Empty;

    /// <summary>Add a toggle from <see cref="NewName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        string name = NewName.Trim();
        if (name.Length == 0 || Rows.Any(r => string.Equals(r.Name, name, StringComparison.Ordinal)))
        {
            return;
        }

        OpenCodePluginToggleViewModel row = new() { Name = name, Enabled = true, Owner = this };
        Rows.Add(row);
        NewName = string.Empty;
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewName);

    /// <summary>Remove <paramref name="row"/>.</summary>
    internal void Remove(OpenCodePluginToggleViewModel row) => Rows.Remove(row);

    /// <summary>Replace every row, for the load path.</summary>
    internal void Reset(IEnumerable<OpenCodePluginToggle> toggles)
    {
        Rows.Clear();
        foreach (OpenCodePluginToggle toggle in toggles)
        {
            OpenCodePluginToggleViewModel row = new();
            row.Load(toggle);
            row.Owner = this;
            Rows.Add(row);
        }
    }

    /// <summary>The toggles as the model sees them.</summary>
    internal IReadOnlyList<OpenCodePluginToggle> ToToggles() => [.. Rows.Select(r => r.ToToggle())];
}
