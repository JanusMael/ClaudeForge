using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.OpenCode.Avalonia.Editing;
using Bennewitz.Ninja.OpenCode.Sdk.Tooling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tooling;

/// <summary>
/// One entry of a <c>formatter</c> object: a formatter name and the overrides applied to it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b><c>disabled</c> is a THREE-state control, and a plain checkbox here would be a silent
/// rewrite.</b> Absent and <c>false</c> have the same effect, so a two-state box that omits the key
/// when unticked would delete an explicit <c>"disabled": false</c> from the user's file the first
/// time the page is saved. The indeterminate state is the honest representation of "the key is not
/// there" — the same distinction the whole editor's mode selector exists to preserve, one level
/// down.
/// </para>
/// <para>
/// No reorder buttons. This is a map, and unlike a permission map its key order carries no meaning
/// whatsoever — file order is preserved on write so that a diff stays readable, which is a reason
/// to leave order alone rather than a reason to offer arranging it.
/// </para>
/// </remarks>
public sealed partial class OpenCodeFormatterRowViewModel(
    string name,
    Action<OpenCodeFormatterRowViewModel> onRemove) : ObservableObject
{
    private object? _raw;
    private bool _isOpaque;
    private IReadOnlyList<KeyValuePair<string, object?>> _extras = [];

    /// <summary>The formatter name, i.e. the map key.</summary>
    [ObservableProperty] private string _name = name;

    /// <summary>
    /// Whether this formatter is off: <see langword="true"/>, <see langword="false"/>, or
    /// <see langword="null"/> for "the key is absent".
    /// </summary>
    [ObservableProperty] private bool? _disabled;

    /// <summary>True when the entry could not be read and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isOpaqueEntry;

    /// <summary>How many unsurfaced fields this entry carries.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtras))]
    private int _extraCount;

    /// <summary>True when this entry has editable fields.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>True when the entry carries fields this editor does not surface.</summary>
    /// <remarks>
    /// Worth showing rather than hiding: the schema forbids additional properties here, so every
    /// one of these is either a config from a newer OpenCode or a typo — and both are things the
    /// user wants to know they still have.
    /// </remarks>
    public bool HasExtras => ExtraCount > 0;

    /// <summary>The command and its arguments. Position is meaning — this is argv.</summary>
    public OpenCodeStringListViewModel Command { get; } = new();

    /// <summary>Environment variables. Written under <c>environment</c>.</summary>
    public OpenCodePairListViewModel Environment { get; } = new();

    /// <summary>File extensions this formatter claims. Order carries no meaning here.</summary>
    public OpenCodeStringListViewModel Extensions { get; } = new();

    /// <summary>Remove this entry.</summary>
    [RelayCommand]
    private void Remove() => onRemove(this);

    /// <summary>Populate from a parsed entry.</summary>
    internal void Load(OpenCodeFormatterEntry entry)
    {
        _raw = entry.Raw;
        _isOpaque = entry.IsOpaque;
        _extras = entry.Extras;

        Name = entry.Name;
        IsOpaqueEntry = entry.IsOpaque;
        Disabled = entry.Disabled;
        ExtraCount = entry.Extras.Count;

        Command.Reset(entry.Command);
        Environment.Reset(entry.Environment);
        Extensions.Reset(entry.Extensions);
    }

    /// <summary>The model form of this entry.</summary>
    internal OpenCodeFormatterEntry ToEntry() =>
        _isOpaque
            ? new OpenCodeFormatterEntry
            {
                Name = Name.Trim(),
                IsOpaque = true,
                Raw = _raw,
            }
            : new OpenCodeFormatterEntry
            {
                Name = Name.Trim(),
                Disabled = Disabled,
                Command = Command.ToValues(),
                Environment = Environment.ToPairs(),
                Extensions = Extensions.ToValues(),
                Extras = _extras,
            };
}

/// <summary>
/// One entry of an <c>lsp</c> object: a language-server name and its configuration.
/// </summary>
/// <remarks>
/// <para>
/// ⚠⚠ <b>This entry can be made invalid by one obvious click, and the editor says so rather than
/// repairing it.</b> The schema's two arms are <c>{ "disabled": true }</c> exactly, or an object
/// with a <b>required</b> <c>command</c>. So unticking "disabled" on a disable-only server leaves
/// <c>{ "disabled": false }</c>, which matches neither — and neither does an entry carrying only
/// <c>extensions</c>. <see cref="NeedsCommand"/> surfaces that state. Inventing a command would be
/// a claim about the user's machine; deleting what they typed would be a claim about their intent.
/// </para>
/// <para>
/// ⚠ <b>The environment key here is <c>env</c>, not <c>environment</c>.</b> A formatter entry uses
/// the other spelling and both objects forbid additional properties, so the two are separate
/// fields all the way down to the codec.
/// </para>
/// </remarks>
public sealed partial class OpenCodeLspRowViewModel(
    string name,
    Action<OpenCodeLspRowViewModel> onRemove) : ObservableObject
{
    private object? _raw;
    private bool _isOpaque;
    private object? _lastGoodInitialization;
    private IReadOnlyList<KeyValuePair<string, object?>> _extras = [];

    /// <summary>The server name, i.e. the map key.</summary>
    [ObservableProperty] private string _name = name;

    /// <summary>
    /// Whether this server is off: <see langword="true"/>, <see langword="false"/>, or
    /// <see langword="null"/> for "the key is absent".
    /// </summary>
    /// <remarks>
    /// ⚠ Three-state for the same reason as the formatter row's, plus one specific to this shape:
    /// the indeterminate state is the only way out of the invalid <c>{ "disabled": false }</c>
    /// entry that unticking a two-state box would strand the user in.
    /// </remarks>
    [ObservableProperty] private bool? _disabled;

    /// <summary>True when the entry could not be read and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isOpaqueEntry;

    /// <summary>How many unsurfaced fields this entry carries.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtras))]
    private int _extraCount;

    /// <summary>True when the entry writes an <c>initialization</c> object.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInitialization))]
    [NotifyPropertyChangedFor(nameof(HasInitializationError))]
    private bool _hasInitialization;

    /// <summary>The <c>initialization</c> object as JSON text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInitializationError))]
    private string _initializationText = string.Empty;

    /// <summary>True when this entry has editable fields.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>True when the entry carries fields this editor does not surface.</summary>
    public bool HasExtras => ExtraCount > 0;

    /// <summary>True when the initialization editor should be shown.</summary>
    public bool ShowInitialization => HasInitialization && !IsOpaqueEntry;

    /// <summary>True when <see cref="InitializationText"/> is not valid JSON for an object.</summary>
    /// <remarks>
    /// ⚠ While set, <see cref="ToEntry"/> keeps the LAST GOOD value rather than dropping it. A JSON
    /// text box is unparseable most of the time it is in use, and treating every intermediate
    /// keystroke as "no initialization" would delete the user's configuration on a live-write host
    /// before they finished typing it. Same rule as the plugin editor's options box.
    /// </remarks>
    public bool HasInitializationError =>
        HasInitialization && !TryParseInitialization(InitializationText, out _);

    /// <summary>
    /// True when the entry has content but matches neither schema arm, because it states no command
    /// and is not the bare disable-only form.
    /// </summary>
    /// <remarks>
    /// ⭐ Derived from the model's own verdict rather than recomputed here, so the warning the user
    /// sees and the value that gets written cannot disagree. Recomputed on demand — the editor
    /// calls <see cref="RefreshDerived"/> after any change, because this depends on three nested
    /// collections whose counts nothing on this object notifies about.
    /// </remarks>
    public bool NeedsCommand => ToEntry().IsIncomplete;

    /// <summary>The command and its arguments. Position is meaning — this is argv.</summary>
    public OpenCodeStringListViewModel Command { get; } = new();

    /// <summary>File extensions this server claims. Order carries no meaning here.</summary>
    public OpenCodeStringListViewModel Extensions { get; } = new();

    /// <summary>Environment variables. Written under <c>env</c>.</summary>
    public OpenCodePairListViewModel Env { get; } = new();

    /// <summary>Remove this entry.</summary>
    [RelayCommand]
    private void Remove() => onRemove(this);

    /// <summary>Re-raise the properties computed from the nested collections.</summary>
    internal void RefreshDerived()
    {
        OnPropertyChanged(nameof(NeedsCommand));
        OnPropertyChanged(nameof(HasInitializationError));
    }

    /// <summary>Populate from a parsed entry.</summary>
    internal void Load(OpenCodeLspEntry entry)
    {
        _raw = entry.Raw;
        _isOpaque = entry.IsOpaque;
        _extras = entry.Extras;
        _lastGoodInitialization = entry.Initialization;

        Name = entry.Name;
        IsOpaqueEntry = entry.IsOpaque;
        Disabled = entry.Disabled;
        ExtraCount = entry.Extras.Count;

        Command.Reset(entry.Command);
        Extensions.Reset(entry.Extensions);
        Env.Reset(entry.Env);

        HasInitialization = entry.Initialization is not null;
        InitializationText = entry.Initialization is null ? string.Empty : Format(entry.Initialization);
    }

    /// <summary>The model form of this entry.</summary>
    internal OpenCodeLspEntry ToEntry()
    {
        if (_isOpaque)
        {
            return new OpenCodeLspEntry { Name = Name.Trim(), IsOpaque = true, Raw = _raw };
        }

        return new OpenCodeLspEntry
        {
            Name = Name.Trim(),
            Disabled = Disabled,
            Command = Command.ToValues(),
            Extensions = Extensions.ToValues(),
            Env = Env.ToPairs(),
            Initialization = ResolveInitialization(),
            Extras = _extras,
        };
    }

    /// <remarks>
    /// ⚠ Toggling <see cref="HasInitialization"/> off keeps the text, so toggling it back restores
    /// what the user wrote instead of an empty object — the preserve-the-other-arm rule applied to
    /// an optional field.
    /// </remarks>
    private object? ResolveInitialization()
    {
        if (!HasInitialization)
        {
            return null;
        }

        if (TryParseInitialization(InitializationText, out object? parsed))
        {
            _lastGoodInitialization = parsed;
            return parsed;
        }

        return _lastGoodInitialization ?? new OrderedPropertyMap();
    }

    /// <summary>
    /// Parse JSON text into a value-currency object. Blank counts as an empty object.
    /// </summary>
    /// <remarks>
    /// Blank is valid on purpose: <c>"initialization": {}</c> is a legitimate value, and requiring
    /// the user to type braces to express "initialization, but nothing in it yet" would be busywork.
    /// </remarks>
    internal static bool TryParseInitialization(string text, out object? initialization)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            initialization = new OrderedPropertyMap();
            return true;
        }

        try
        {
            object? currency = JsonCurrency.FromJsonNode(System.Text.Json.Nodes.JsonNode.Parse(text));

            // Must be an OBJECT: the schema types `initialization` as `object`, so an array or a
            // scalar here would produce a config OpenCode rejects.
            if (currency is IReadOnlyDictionary<string, object?>)
            {
                initialization = currency;
                return true;
            }
        }
        catch (JsonException)
        {
            // Expected while typing, not an error to log.
        }

        initialization = null;
        return false;
    }

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? string.Empty;
}
