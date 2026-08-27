using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Tooling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tooling;

/// <summary>
/// One selectable value of the mode control, with the text that explains what it writes.
/// </summary>
/// <param name="Mode">The mode this option selects.</param>
/// <param name="Label">Short text for the picker.</param>
/// <param name="Description">
/// What the file will contain, stated in terms of the JSON — because the entire point of this
/// control is that four states with two effects are still four different files.
/// </param>
public sealed record OpenCodeToolingModeOption(
    OpenCodeToolingMode Mode,
    string Label,
    string Description);

/// <summary>
/// The half of the <c>formatter</c> and <c>lsp</c> editors that is genuinely the same: a
/// four-state mode over a per-language map.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Shared because the mode is the part that would drift.</b> Both keys declare exactly
/// <c>anyOf: [boolean, object]</c> and both carry the same description — "Omit or set to false to
/// disable, true to enable built-ins, or an object to enable built-ins with overrides". Two
/// implementations of that would eventually disagree about which of the four states may be folded
/// into which, and folding any of them rewrites a file the editor was only asked to open. What is
/// <b>not</b> shared is everything below the mode: a formatter entry is one object shape, an
/// <c>lsp</c> entry is a two-arm union with a required <c>command</c> and a different environment
/// key.
/// </para>
/// <para>
/// ⚠ <b><see cref="OpenCodeToolingMode.Unrecognised"/> is not selectable.</b> It is a state a value
/// arrives in, never one a user picks — offering it would invite turning an editable value into an
/// opaque blob, and its <c>Raw</c> may be <see langword="null"/>, which the value currency cannot
/// express as anything but "remove the key". While a value is unrecognised the editor shows it
/// verbatim and offers <see cref="ReplaceUnrecognisedCommand"/>, so leaving that state is an
/// explicit act rather than a side effect of the picker having to show something.
/// </para>
/// </remarks>
public abstract partial class OpenCodeToolingEditorViewModel : PropertyEditorViewModel
{
    private object? _rawValue;

    /// <summary>True while the load path is populating, to suppress the modified signal.</summary>
    protected bool IsLoading { get; private set; }

    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    protected OpenCodeToolingEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
    }

    /// <summary>The modes a user may choose, in the order they escalate.</summary>
    /// <remarks>
    /// Ordered "off, more on, most on" rather than by schema arm, because that is the axis the user
    /// is actually moving along. <see cref="OpenCodeToolingMode.Unrecognised"/> is absent by design.
    /// </remarks>
    public static IReadOnlyList<OpenCodeToolingModeOption> ModeOptions { get; } =
    [
        new(OpenCodeToolingMode.NotSet, Strings.ToolingModeNotSet, Strings.ToolingModeNotSetHelp),
        new(OpenCodeToolingMode.Disabled, Strings.ToolingModeDisabled, Strings.ToolingModeDisabledHelp),
        new(OpenCodeToolingMode.BuiltIns, Strings.ToolingModeBuiltIns, Strings.ToolingModeBuiltInsHelp),
        new(
            OpenCodeToolingMode.Configured,
            Strings.ToolingModeConfigured,
            Strings.ToolingModeConfiguredHelp),
    ];

    /// <summary>The selected mode option.</summary>
    /// <remarks>
    /// ⚠ Seeded by a FIELD initialiser, not by an assignment in the constructor. The generated
    /// setter raises <c>OnSelectedModeChanged</c>, which calls <see cref="MarkModified"/> and
    /// through it the abstract <see cref="RefreshDerived"/> — a virtual call that would run against
    /// a derived editor whose own collections do not exist yet, and would mark a freshly-built
    /// editor modified before the user had touched anything.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    [NotifyPropertyChangedFor(nameof(ModeHelp))]
    private OpenCodeToolingModeOption _selectedMode = ModeOptions[0];

    /// <summary>
    /// True when the loaded value matched neither arm and is being held verbatim.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(IsConfigured))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceUnrecognisedCommand))]
    private bool _isUnrecognised;

    /// <summary>The unreadable value rendered as JSON, so it can be read and copied before it is
    /// replaced.</summary>
    [ObservableProperty] private string _unrecognisedText = string.Empty;

    /// <summary>The mode this editor will write.</summary>
    public OpenCodeToolingMode Mode =>
        IsUnrecognised ? OpenCodeToolingMode.Unrecognised : SelectedMode.Mode;

    /// <summary>True when the per-language entry list applies.</summary>
    public bool IsConfigured => Mode == OpenCodeToolingMode.Configured;

    /// <summary>How many per-language entries exist.</summary>
    /// <remarks>
    /// Held here rather than bound to the derived editor's collection because the two collections
    /// hold different row types, and because a view cannot compare a count to zero in a compiled
    /// binding without a converter. Each derived editor assigns it from
    /// <see cref="RefreshDerived"/>.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntries))]
    private int _entryCount;

    /// <summary>True when at least one per-language entry exists.</summary>
    public bool HasEntries => EntryCount > 0;

    /// <summary>What the selected mode writes.</summary>
    public string ModeHelp => SelectedMode.Description;

    /// <summary>
    /// Leave the unrecognised state, discarding the held value in favour of an editable one.
    /// </summary>
    /// <remarks>
    /// A command rather than an implicit consequence of touching the picker: replacing a value the
    /// editor could not read is destructive, and the user should have had to mean it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsUnrecognised))]
    private void ReplaceUnrecognised()
    {
        IsUnrecognised = false;
        _rawValue = null;
        UnrecognisedText = string.Empty;
        SelectedMode = OptionFor(OpenCodeToolingMode.Configured);
        MarkModified();
    }

    /// <summary>The option carrying <paramref name="mode"/>, or the not-set option.</summary>
    protected static OpenCodeToolingModeOption OptionFor(OpenCodeToolingMode mode) =>
        ModeOptions.FirstOrDefault(o => o.Mode == mode) ?? ModeOptions[0];

    /// <summary>
    /// Serialise the modes that carry no entries. Returns <see langword="false"/> when the caller
    /// must serialise its own entry map.
    /// </summary>
    protected bool TryWriteModeOnly(out object? value)
    {
        if (IsUnrecognised)
        {
            value = _rawValue;
            return true;
        }

        switch (SelectedMode.Mode)
        {
            case OpenCodeToolingMode.Disabled:
                value = false;
                return true;
            case OpenCodeToolingMode.BuiltIns:
                value = true;
                return true;
            case OpenCodeToolingMode.Configured:
                value = null;
                return false;
            default:
                value = null;
                return true;
        }
    }

    /// <summary>
    /// Apply the shared scope and mode state from a loaded value, under the loading guard.
    /// </summary>
    /// <param name="value">The layered value.</param>
    /// <param name="editingScope">The scope being edited.</param>
    /// <param name="mode">The mode the codec classified.</param>
    /// <param name="raw">The verbatim value, when <paramref name="mode"/> is unrecognised.</param>
    /// <param name="loadEntries">Populates the derived editor's entry list.</param>
    protected void LoadModeState(
        IEditorValue value,
        IEditorScope editingScope,
        OpenCodeToolingMode mode,
        object? raw,
        Action loadEntries)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(loadEntries);

        IsLoading = true;
        try
        {
            _lastValue = value;
            _lastScope = editingScope;

            EditingScope = editingScope;
            EffectiveScope = value.EffectiveScope;
            IsOverridden = value.IsOverridden;

            IsUnrecognised = mode == OpenCodeToolingMode.Unrecognised;
            _rawValue = IsUnrecognised ? raw : null;
            UnrecognisedText = IsUnrecognised ? Format(raw) : string.Empty;

            // An unrecognised value leaves the picker on the not-set option, which the view never
            // shows: while IsUnrecognised the picker is replaced by the verbatim value and the
            // replace command. Mode reports Unrecognised regardless, so nothing downstream reads
            // the placeholder as a claim about the file.
            SelectedMode = OptionFor(IsUnrecognised ? OpenCodeToolingMode.NotSet : mode);

            loadEntries();

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reload the last loaded value, or clear when there was none.</summary>
    /// <param name="clear">Clears the derived editor's entry list.</param>
    protected void ResetToLastLoaded(Action clear)
    {
        ArgumentNullException.ThrowIfNull(clear);

        if (_lastValue is { } value && _lastScope is { } scope)
        {
            LoadFromValue(value, scope);
            IsModified = false;
            return;
        }

        IsLoading = true;
        try
        {
            clear();
            IsUnrecognised = false;
            _rawValue = null;
            UnrecognisedText = string.Empty;
            SelectedMode = ModeOptions[0];
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Force-fire <c>PropertyChanged(IsModified)</c> on every user mutation, even when the flag was
    /// already true from the prior load.
    /// </summary>
    protected void MarkModified()
    {
        if (IsLoading)
        {
            return;
        }

        RefreshDerived();

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    /// <summary>
    /// Recompute whatever the derived editor shows in its banners. Called from
    /// <see cref="MarkModified"/>.
    /// </summary>
    protected abstract void RefreshDerived();

    /// <remarks>
    /// The picker is a user action, so it marks the edit. It is also the one control that can move
    /// the value between "key absent" and "key present", which is why the editor must not treat an
    /// empty entry list as equivalent to being unset.
    /// </remarks>
    partial void OnSelectedModeChanged(OpenCodeToolingModeOption value) => MarkModified();

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? "null";
}
