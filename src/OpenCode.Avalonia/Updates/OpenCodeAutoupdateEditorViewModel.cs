using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Updates;

/// <summary>
/// One selectable <c>autoupdate</c> value, with the text that says what it writes.
/// </summary>
/// <param name="Mode">The mode this option selects.</param>
/// <param name="Label">Short text for the picker.</param>
/// <param name="Description">What the file will contain, stated as the JSON it produces.</param>
public sealed record OpenCodeAutoupdateOption(
    OpenCodeAutoupdateMode Mode,
    string Label,
    string Description);

/// <summary>
/// Editor for <c>autoupdate</c>: <c>true</c> | <c>false</c> | <c>"notify"</c>, plus absent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the generic editor.</b> The generic dispatch cannot type a union of a boolean and a
/// string literal, so it falls back to a free-text box — verified by reading the running app's
/// automation tree, where this property appeared as <c>Edit|autoupdate</c>. A text box for a
/// three-value enum invites <c>"yes"</c>, <c>"True"</c> and <c>"Notify"</c>, none of which the
/// schema admits, and none of which the box would object to.
/// </para>
/// <para>
/// ⚠ <b>Deliberately NOT built on <c>OpenCodeToolingEditorViewModel</c></b>, despite both being a
/// four-state mode with an opaque arm. That base carries an entry list — <c>IsConfigured</c>,
/// <c>EntryCount</c>, the abstract <c>RefreshDerived</c> — and none of it means anything for a
/// scalar. Sharing it would mean inheriting three members that must be permanently pinned to
/// "empty", which is a worse lie than a little repetition. The <i>idioms</i> are shared instead: an
/// option record carrying its own help text, and an unrecognised state that is shown rather than
/// selectable.
/// </para>
/// <para>
/// ⚠ <see cref="OpenCodeAutoupdateMode.Unrecognised"/> is not in <see cref="Options"/>. It is a
/// state a value arrives in, never one a user picks, and its <c>Raw</c> may be
/// <see langword="null"/>, which the value currency can only express as "remove the key".
/// </para>
/// </remarks>
public sealed partial class OpenCodeAutoupdateEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private object? _rawValue;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodeAutoupdateEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
    }

    /// <summary>The values a user may choose, ordered least to most automatic.</summary>
    /// <remarks>
    /// Ordered by how much the tool does on its own rather than by schema arm, because that is the
    /// axis the user is moving along. <see cref="OpenCodeAutoupdateMode.Unrecognised"/> is absent by
    /// design.
    /// </remarks>
    public static IReadOnlyList<OpenCodeAutoupdateOption> Options { get; } =
    [
        new(OpenCodeAutoupdateMode.NotSet, Strings.AutoupdateNotSet, Strings.AutoupdateNotSetHelp),
        new(
            OpenCodeAutoupdateMode.Disabled,
            Strings.AutoupdateDisabled,
            Strings.AutoupdateDisabledHelp),
        new(OpenCodeAutoupdateMode.Notify, Strings.AutoupdateNotify, Strings.AutoupdateNotifyHelp),
        new(
            OpenCodeAutoupdateMode.Automatic,
            Strings.AutoupdateAutomatic,
            Strings.AutoupdateAutomaticHelp),
    ];

    /// <summary>The selected option.</summary>
    /// <remarks>
    /// ⚠ Seeded by a FIELD initialiser rather than assigned in the constructor: the generated setter
    /// raises <c>OnSelectedOptionChanged</c>, which marks the editor modified, and doing that from a
    /// constructor would leave a freshly-built editor claiming an edit the user never made. The same
    /// trap the tooling editors hit.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(ModeHelp))]
    private OpenCodeAutoupdateOption _selectedOption = Options[0];

    /// <summary>True when the loaded value matched no arm and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceUnrecognisedCommand))]
    private bool _isUnrecognised;

    /// <summary>
    /// The unreadable value as JSON, so it can be read and copied before being replaced.
    /// </summary>
    [ObservableProperty] private string _unrecognisedText = string.Empty;

    /// <summary>The mode this editor will write.</summary>
    public OpenCodeAutoupdateMode Mode =>
        IsUnrecognised ? OpenCodeAutoupdateMode.Unrecognised : SelectedOption.Mode;

    /// <summary>What the selected option writes.</summary>
    public string ModeHelp => SelectedOption.Description;

    /// <inheritdoc />
    public override object? ToValue() =>
        IsUnrecognised
            ? _rawValue
            : OpenCodeAutoupdateCodec.Write(
                new OpenCodeAutoupdateConfig { Mode = SelectedOption.Mode });

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

            OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(
                value.GetValueAt(editingScope), value.IsDefinedAt(editingScope));

            IsUnrecognised = config.Mode == OpenCodeAutoupdateMode.Unrecognised;
            _rawValue = IsUnrecognised ? config.Raw : null;
            UnrecognisedText = IsUnrecognised ? Format(config.Raw) : string.Empty;

            // An unrecognised value leaves the picker on the not-set option, which the view never
            // shows: while IsUnrecognised the picker is replaced by the held value and the replace
            // command. Mode reports Unrecognised regardless, so nothing reads the placeholder as a
            // claim about the file.
            SelectedOption = OptionFor(
                IsUnrecognised ? OpenCodeAutoupdateMode.NotSet : config.Mode);

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
            SelectedOption = Options[0];
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Leave the unrecognised state, discarding the held value in favour of a selectable one.
    /// </summary>
    /// <remarks>
    /// A command rather than a side effect of touching the picker: replacing a value the editor
    /// could not read is destructive, and the user should have had to mean it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsUnrecognised))]
    private void ReplaceUnrecognised()
    {
        IsUnrecognised = false;
        _rawValue = null;
        UnrecognisedText = string.Empty;
        SelectedOption = OptionFor(OpenCodeAutoupdateMode.Notify);
        MarkModified();
    }

    private static OpenCodeAutoupdateOption OptionFor(OpenCodeAutoupdateMode mode) =>
        Options.FirstOrDefault(o => o.Mode == mode) ?? Options[0];

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

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    /// <remarks>
    /// The picker is the one control here, and it is also what moves the value between "key absent"
    /// and "key present" — so it must mark the edit even when the previous state was already
    /// modified, or the host never sees the new value.
    /// </remarks>
    partial void OnSelectedOptionChanged(OpenCodeAutoupdateOption value) => MarkModified();

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? "null";
}
