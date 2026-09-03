using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;

/// <summary>
/// One selectable value of an action's mode control, with the text that says what it writes.
/// </summary>
/// <param name="Mode">The mode this option selects.</param>
/// <param name="Label">Short text for the picker.</param>
/// <param name="Description">What the file will contain, stated as the JSON it produces.</param>
public sealed record OpenCodeKeybindModeOption(
    OpenCodeKeybindMode Mode,
    string Label,
    string Description);

/// <summary>
/// One row of the keybinds editor: an action, and what it is bound to.
/// </summary>
/// <remarks>
/// <para>
/// One of these exists for each of the schema's 184 actions whether the file mentions it or not, plus
/// one for each action name the file states that the schema does not declare. A row the user has not
/// touched is <see cref="OpenCodeKeybindMode.NotSet"/> and writes nothing — without that, opening the
/// page and saving would produce a 184-key diff.
/// </para>
/// <para>
/// ⚠ <b>The label is the schema's own description, and there is no default to show beside it.</b>
/// Checked, not assumed: all 184 actions carry a description and <b>none</b> declares a
/// <c>default</c>, so this editor cannot say what OpenCode does when an action is unset — and it does
/// not guess. <c>OpenCodeKeybindSchemaDriftTests.NoAction_DeclaresADefault</c> keeps that true.
/// </para>
/// <para>
/// ⚠ <b>No <c>CollectionChanged</c> handler and no <c>PropertyChanged</c> subscription on a binding
/// row.</b> A binding reports through the <c>Owner</c> reference it was constructed with, and that is
/// the only mechanism — so this VM needs neither an attach/detach pair nor a filter naming the
/// derived properties. Both alternatives were tried and measured: a collection handler is the trap
/// 9a-8's canary found in three editors, and a <c>PropertyChanged</c> subscription beside the
/// callback made one keystroke report <b>twice</b>. See
/// <see cref="OpenCodeKeybindEditorViewModel"/>'s remarks for the numbers.
/// </para>
/// </remarks>
public sealed partial class OpenCodeKeybindActionViewModel : ObservableObject, IKeyBindingOwner
{
    private readonly Action _onChanged;
    private object? _rawValue;
    private bool _isLoading;

    /// <summary>Creates a row for one action.</summary>
    /// <param name="action">The action name, i.e. the map key.</param>
    /// <param name="label">The schema's description of the action.</param>
    /// <param name="group">The group this action is filed under.</param>
    /// <param name="isKnown">Whether the schema declares this action.</param>
    /// <param name="onChanged">Called after any change the user made.</param>
    public OpenCodeKeybindActionViewModel(
        string action,
        string label,
        string group,
        bool isKnown,
        Action onChanged)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Label = label ?? string.Empty;
        Group = group ?? string.Empty;
        _isKnown = isKnown;
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        // ⚠ A field initialiser, not an assignment: the generated setter raises
        // OnSelectedModeChanged, which reports a change, and doing that from a constructor would
        // leave a freshly-built row claiming an edit the user never made. The same trap the tooling
        // and autoupdate editors hit.
        _selectedMode = ModeOptions[0];
    }

    /// <summary>The modes a user may choose.</summary>
    /// <remarks>
    /// <para>
    /// Ordered "unset, off, unbound, one key, several keys" — the axis the user is moving along,
    /// rather than the schema's arm order.
    /// </para>
    /// <para>
    /// ⚠ <see cref="OpenCodeKeybindMode.Unrecognised"/> is absent by design. It is a state a value
    /// arrives in, never one a user picks, and its <c>Raw</c> may be <see langword="null"/>, which
    /// the value currency can only express as "remove the key".
    /// </para>
    /// <para>
    /// ⚠ There is no "enabled" option, because the schema's boolean arm is <c>enum: [false]</c> — the
    /// literal <c>true</c> is not admitted anywhere in this union. Offering it would put a value in
    /// the user's file that OpenCode rejects.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<OpenCodeKeybindModeOption> ModeOptions { get; } =
    [
        new(OpenCodeKeybindMode.NotSet, Strings.KeybindModeNotSet, Strings.KeybindModeNotSetHelp),
        new(OpenCodeKeybindMode.Disabled, Strings.KeybindModeDisabled, Strings.KeybindModeDisabledHelp),
        new(OpenCodeKeybindMode.None, Strings.KeybindModeNone, Strings.KeybindModeNoneHelp),
        new(OpenCodeKeybindMode.Bound, Strings.KeybindModeBound, Strings.KeybindModeBoundHelp),
        new(OpenCodeKeybindMode.Sequence, Strings.KeybindModeSequence, Strings.KeybindModeSequenceHelp),
    ];

    /// <summary>The action name, i.e. the key written into the file.</summary>
    public string Action { get; }

    /// <summary>The schema's description of the action.</summary>
    public string Label { get; }

    /// <summary>The group this action is filed under, derived from its name.</summary>
    public string Group { get; }

    /// <summary>The bindings this action states, in file order.</summary>
    public ObservableCollection<OpenCodeKeyBindingViewModel> Bindings { get; } = [];

    /// <summary>Which arm this action writes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(ModeHelp))]
    [NotifyPropertyChangedFor(nameof(IsSet))]
    [NotifyPropertyChangedFor(nameof(ShowsBindings))]
    [NotifyPropertyChangedFor(nameof(IsSequence))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    private OpenCodeKeybindModeOption _selectedMode;

    /// <summary>True when the schema declares this action.</summary>
    [ObservableProperty] private bool _isKnown;

    /// <summary>True when the value matched no arm and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(IsSet))]
    [NotifyPropertyChangedFor(nameof(ShowsBindings))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceUnrecognisedCommand))]
    private bool _isUnrecognised;

    /// <summary>The unreadable value as JSON, so it can be read and copied.</summary>
    [ObservableProperty] private string _unrecognisedText = string.Empty;

    /// <summary>
    /// The other actions bound to the same key as this one, or empty when there is no clash.
    /// </summary>
    /// <remarks>
    /// Set by the editor, which is the only thing that can see across rows — the same division the
    /// permission grid draws for shadowed rules.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    private string _conflictWith = string.Empty;

    /// <summary>True when at least one other action is bound to the same key.</summary>
    public bool HasConflict => !string.IsNullOrEmpty(ConflictWith);

    /// <summary>The mode this row writes.</summary>
    public OpenCodeKeybindMode Mode =>
        IsUnrecognised ? OpenCodeKeybindMode.Unrecognised : SelectedMode.Mode;

    /// <summary>What the selected mode writes.</summary>
    public string ModeHelp => SelectedMode.Description;

    /// <summary>True when this action writes anything at all.</summary>
    public bool IsSet => Mode != OpenCodeKeybindMode.NotSet;

    /// <summary>True when the binding list applies.</summary>
    public bool ShowsBindings =>
        Mode is OpenCodeKeybindMode.Bound or OpenCodeKeybindMode.Sequence;

    /// <summary>True when the list form applies, so add and reorder are offered.</summary>
    public bool IsSequence => Mode == OpenCodeKeybindMode.Sequence;

    /// <summary>
    /// The bindings this row would actually write.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>The single form writes exactly ONE binding, so everything derived from the value has to
    /// agree with that.</b> Extra bindings left over from a previous sequence stay in
    /// <see cref="Bindings"/> — trimming them would destroy them on a mode flip the user may be about
    /// to undo — but they are not written, and reporting a clash or an incomplete key from one would
    /// be warning about text the file will not contain.
    /// </remarks>
    private IEnumerable<OpenCodeKeyBindingViewModel> EffectiveBindings =>
        Mode switch
        {
            OpenCodeKeybindMode.Bound => Bindings.Take(1),
            OpenCodeKeybindMode.Sequence => Bindings,
            _ => [],
        };

    /// <summary>True when any binding names no key, so the schema rejects the value.</summary>
    public bool IsIncomplete => EffectiveBindings.Any(b => b.IsIncomplete);

    /// <summary>
    /// What a screen reader announces for this row of the action list: which action, the keys
    /// bound to it, and whether the value is one the schema will reject.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>An <c>ItemsSource</c>-generated <c>ListBoxItem</c> takes its name from the ITEM and
    /// falls back to <see cref="object.ToString"/>, so without this all ~90 rows announced
    /// <c>Bennewitz.Ninja.OpenCode.Avalonia.Keybinds.OpenCodeKeybindActionViewModel</c>.</b>
    /// Guarded by <c>ItemsSourceBoundListBoxesTests</c>. <see cref="Summary"/> rides along because
    /// the whole purpose of the row is which keys are bound, and the incompleteness because it is
    /// otherwise signalled only by colour.
    /// </remarks>
    public string AccessibleName => IsIncomplete
        ? $"{Label}, {Summary}, incomplete"
        : $"{Label}, {Summary}";

    /// <inheritdoc cref="AccessibleName"/>
    public override string ToString() => AccessibleName;

    /// <summary>This action's value as one line, for the collapsed row and for search.</summary>
    public string Summary => Mode switch
    {
        OpenCodeKeybindMode.NotSet => Strings.KeybindModeNotSet,
        OpenCodeKeybindMode.Disabled => Strings.KeybindModeDisabled,
        OpenCodeKeybindMode.None => Strings.KeybindModeNone,
        OpenCodeKeybindMode.Unrecognised => Strings.KeybindHeldValue,
        _ => EffectiveBindings.Any()
            ? string.Join(
                Strings.KeybindSequenceSeparator,
                EffectiveBindings.Select(b => b.Summary))
            : Strings.KeybindNoKey,
    };

    /// <summary>Populate this row from a value the codec read.</summary>
    /// <remarks>
    /// ⚠ Under its own loading guard as well as the editor's. Assigning
    /// <see cref="SelectedMode"/> runs <c>OnSelectedModeChanged</c>, which seeds an empty binding for
    /// the two bound modes — correct for a user's click and wrong here, where the real bindings are
    /// about to be added. Without the guard the load path would create and discard a row per bound
    /// action.
    /// </remarks>
    public void Load(OpenCodeKeybindValue value, bool isKnown)
    {
        ArgumentNullException.ThrowIfNull(value);

        _isLoading = true;
        try
        {
            IsKnown = isKnown;
            IsUnrecognised = value.Mode == OpenCodeKeybindMode.Unrecognised;
            _rawValue = IsUnrecognised ? value.Raw : null;
            UnrecognisedText = IsUnrecognised ? Format(value.Raw) : string.Empty;

            // No detach loop, because there is nothing subscribed to detach: a binding row reports
            // through the `Owner` reference it was constructed with, so a dropped row is simply
            // unreachable. The stale-handler failure 9a-8 found cannot arise where the link points
            // from the child to the parent rather than the other way about.
            Bindings.Clear();
            foreach (OpenCodeKeyBinding binding in value.Bindings)
            {
                Bindings.Add(new OpenCodeKeyBindingViewModel(binding, this));
            }

            // After the bindings, so the seeding guard has nothing to seed and the derived
            // properties this raises are computed against the real rows.
            SelectedMode = OptionFor(IsUnrecognised ? OpenCodeKeybindMode.NotSet : value.Mode);

            RefreshBindingCommands();
            ConflictWith = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Serialise this row.</summary>
    /// <remarks>
    /// ⭐ <b><see cref="Bindings"/> survives a mode switch</b>, so flipping to
    /// <see cref="OpenCodeKeybindMode.Disabled"/> and back restores what was bound rather than
    /// yielding an empty row. The preserve-the-other-arm rule, which this phase has now needed for
    /// permission, MCP, plugin, formatter/lsp and here.
    /// </remarks>
    public OpenCodeKeybindValue ToValue()
    {
        if (IsUnrecognised)
        {
            return new OpenCodeKeybindValue
            {
                Mode = OpenCodeKeybindMode.Unrecognised,
                Raw = _rawValue,
            };
        }

        return new OpenCodeKeybindValue
        {
            Mode = SelectedMode.Mode,
            Bindings = [.. Bindings.Select(b => b.ToBinding())],
        };
    }

    /// <summary>
    /// The keys this action is bound to, as comparable text, for cross-row clash detection.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>A chord string and a structured key are never reported as the same key, even when they
    /// plainly are.</b> <c>"ctrl+q"</c> and <c>{"name":"q","ctrl":true}</c> almost certainly mean the
    /// same keystroke — but the chord grammar is undefined by the schema, so saying so would mean
    /// inventing the parser this editor deliberately refuses to invent. Like is compared with like:
    /// chord text against chord text, structured key against structured key. A missed clash is a
    /// warning the user does not get; a claimed one that rests on a guessed grammar is the editor
    /// asserting something it cannot know.
    /// </remarks>
    public IEnumerable<string> ComparableKeys()
    {
        foreach (OpenCodeKeyBindingViewModel binding in EffectiveBindings)
        {
            if (binding.IsOpaque || binding.IsIncomplete)
            {
                continue;
            }

            OpenCodeKeyBinding read = binding.ToBinding();

            // The event form's delivery options change WHEN the key fires, not WHICH key it is, so
            // two actions on the same key with different `event` values are still a clash worth
            // showing. Only the key itself is compared.
            if (read.Key is { } key)
            {
                yield return "object:" + string.Join(
                    '|',
                    key.Name,
                    key.Ctrl == true ? "ctrl" : string.Empty,
                    key.Shift == true ? "shift" : string.Empty,
                    key.Meta == true ? "meta" : string.Empty,
                    key.Super == true ? "super" : string.Empty,
                    key.Hyper == true ? "hyper" : string.Empty);
            }
            else if (!string.IsNullOrWhiteSpace(read.Chord))
            {
                yield return "text:" + read.Chord;
            }
        }
    }

    /// <summary>Recompute every binding row's reorder commands.</summary>
    /// <remarks>
    /// Their <c>CanExecute</c> depends on the row's index, which nothing raises a change for, so this
    /// runs after any structural change — the rule the permission grid learned first.
    /// </remarks>
    public void RefreshBindingCommands()
    {
        for (int i = 0; i < Bindings.Count; i++)
        {
            Bindings[i].RefreshMoveCommands(i, Bindings.Count, IsSequence);
        }
    }

    /// <inheritdoc />
    public void OnBindingChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsIncomplete));
        _onChanged();
    }

    /// <inheritdoc />
    public void RemoveBinding(OpenCodeKeyBindingViewModel binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!Bindings.Remove(binding))
        {
            return;
        }

        RefreshBindingCommands();
        OnBindingChanged();
    }

    /// <inheritdoc />
    public void MoveBinding(OpenCodeKeyBindingViewModel binding, int offset)
    {
        ArgumentNullException.ThrowIfNull(binding);

        int index = Bindings.IndexOf(binding);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Bindings.Count)
        {
            return;
        }

        Bindings.Move(index, target);
        RefreshBindingCommands();
        OnBindingChanged();
    }

    /// <summary>Add an empty binding to the sequence.</summary>
    [RelayCommand(CanExecute = nameof(IsSequence))]
    private void AddBinding()
    {
        Bindings.Add(new OpenCodeKeyBindingViewModel(new OpenCodeKeyBinding(), this));
        RefreshBindingCommands();
        OnBindingChanged();
    }

    /// <summary>Leave the held state, discarding the verbatim value for an editable one.</summary>
    [RelayCommand(CanExecute = nameof(IsUnrecognised))]
    private void ReplaceUnrecognised()
    {
        IsUnrecognised = false;
        _rawValue = null;
        UnrecognisedText = string.Empty;
        SelectedMode = OptionFor(OpenCodeKeybindMode.Bound);
        OnBindingChanged();
    }

    private static OpenCodeKeybindModeOption OptionFor(OpenCodeKeybindMode mode) =>
        ModeOptions.FirstOrDefault(o => o.Mode == mode) ?? ModeOptions[0];

    /// <remarks>
    /// ⭐ Choosing <see cref="OpenCodeKeybindMode.Bound"/> or
    /// <see cref="OpenCodeKeybindMode.Sequence"/> seeds one empty binding when there is none, so the
    /// choice produces something to type into. It does NOT clear the bindings when leaving those
    /// modes — that is the preserve-the-other-arm rule, and it is what lets a user flip an action off
    /// and back on without retyping the key.
    /// </remarks>
    partial void OnSelectedModeChanged(OpenCodeKeybindModeOption value)
    {
        // Extra bindings from a previous sequence are kept when moving to the single form — only the
        // first is written, and trimming them here would destroy them on a mode flip the user may be
        // about to undo.
        bool wantsARow = value.Mode is OpenCodeKeybindMode.Bound or OpenCodeKeybindMode.Sequence;
        if (!_isLoading && wantsARow && Bindings.Count == 0)
        {
            Bindings.Add(new OpenCodeKeyBindingViewModel(new OpenCodeKeyBinding(), this));
        }

        RefreshBindingCommands();
        AddBindingCommand.NotifyCanExecuteChanged();
        OnBindingChanged();
    }

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? "null";
}
