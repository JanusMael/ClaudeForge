using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;

/// <summary>
/// One selectable binding form, with the text that says what it writes.
/// </summary>
/// <param name="Form">The form this option selects.</param>
/// <param name="Label">Short text for the picker.</param>
/// <param name="Description">What the file will contain, stated as the JSON it produces.</param>
public sealed record OpenCodeBindingFormOption(
    OpenCodeBindingForm Form,
    string Label,
    string Description);

/// <summary>
/// One binding of one action: a key, in whichever of the three forms the file states.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The capture control fills in the KEY form, and the reason is that the schema does not define
/// the other one.</b> The chord arm is a bare <c>string</c> with no <c>pattern</c>, so the grammar
/// lives entirely in OpenCode's parser: turning a captured <c>Ctrl</c>+<c>X</c> into text would mean
/// choosing between <c>"ctrl+x"</c>, <c>"C-x"</c> and <c>"ctrl-x"</c> on no evidence and writing the
/// guess into the user's file. <c>OpenCodeKeybindSchemaDriftTests.TheChordArm_IsAnUnconstrainedString</c>
/// pins the missing pattern, so if a refresh ever adds one the failing test is the invitation to
/// generate text for real. A chord a user typed is of course kept as the text they typed.
/// </para>
/// <para>
/// ⚠ <b>Every modifier is <see cref="bool"/>?, rendered as a three-state box.</b> A modifier stated
/// as <c>false</c> and one left out are different files, so a two-state box would delete an explicit
/// <c>"ctrl": false</c> on the first save. Same reason <c>disabled</c> is three-state in the
/// <c>lsp</c> editor, one level further in.
/// </para>
/// <para>
/// ⚠ Row commands live here and forward to <see cref="Owner"/>. Reaching a list-level command
/// through <c>{Binding $parent[ItemsControl].DataContext.…}</c> trips <b>IL2026</b>, which is a build
/// error in this repo — so a row owns its own commands and the code-behind stays empty. Their
/// <c>CanExecute</c> depends on this row's <i>index</i>, which nothing notifies about, so the owner
/// must call <see cref="RefreshMoveCommands"/> on every row after any structural change.
/// </para>
/// <para>
/// ⚠⚠ <b>The owner is told about a change through exactly ONE mechanism: the explicit
/// <see cref="Notify"/> call in each value-bearing field's <c>OnXChanged</c> hook.</b> This VM does
/// not <i>also</i> route <c>PropertyChanged</c> to the owner, and the difference is not academic — it
/// was measured. With both mechanisms live, one keystroke reported <b>twice</b> and one form switch
/// reported <b>six times</b>, because every <c>[NotifyPropertyChangedFor]</c> target is itself a
/// <c>PropertyChanged</c> the owner then reacted to. An exclusion filter cannot fix that: it has to
/// name every derived property, and forgetting one is silent. An include-list sitting beside each
/// field cannot drift, and a field whose hook is missing fails loudly — Save never enables.
/// </para>
/// </remarks>
public sealed partial class OpenCodeKeyBindingViewModel : ObservableObject
{
    private readonly object? _rawValue;
    private readonly IReadOnlyList<KeyValuePair<string, object?>> _extras;

    /// <summary>Set while one gesture writes several fields, so it is reported once.</summary>
    private bool _inGesture;

    /// <summary>Creates a row for <paramref name="binding"/>.</summary>
    /// <param name="binding">The binding as the codec read it.</param>
    /// <param name="owner">The action this binding belongs to.</param>
    public OpenCodeKeyBindingViewModel(OpenCodeKeyBinding binding, IKeyBindingOwner owner)
    {
        ArgumentNullException.ThrowIfNull(binding);

        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _rawValue = binding.Raw;
        _extras = binding.Extras;

        _isOpaque = binding.IsOpaque;
        _rawText = binding.IsOpaque ? Format(binding.Raw) : string.Empty;

        // An opaque binding leaves the picker on the chord option, which the view never shows: while
        // IsOpaque the row is replaced by the verbatim value and the replace command.
        _selectedForm = OptionFor(
            binding.IsOpaque ? OpenCodeBindingForm.Chord : binding.Form);

        _chord = binding.Chord;

        if (binding.Key is { } key)
        {
            _usesStructuredKey = true;
            _keyName = key.Name;
            _ctrl = key.Ctrl;
            _shift = key.Shift;
            _meta = key.Meta;
            _super = key.Super;
            _hyper = key.Hyper;
            _keyExtras = key.Extras;
        }
        else
        {
            _keyExtras = [];
        }

        _selectedEvent = binding.Event ?? UnsetEvent;
        _preventDefault = binding.PreventDefault;
        _fallthrough = binding.Fallthrough;
    }

    /// <summary>The placeholder the event picker shows for "the key is absent".</summary>
    /// <remarks>
    /// A sentinel string rather than a nullable selection, because an <c>event</c> value outside the
    /// schema's two literals must remain selectable-looking in the picker rather than reading as
    /// unset — the codec keeps such a value, so the editor has to be able to show it.
    /// </remarks>
    public const string UnsetEvent = "";

    /// <summary>The forms a user may choose.</summary>
    /// <remarks>
    /// ⚠ <see cref="OpenCodeBindingForm.Opaque"/> is absent by design. It is a state a value arrives
    /// in, never one a user picks, and its <c>Raw</c> may be <see langword="null"/>, which the value
    /// currency can only express as "remove the key".
    /// </remarks>
    public static IReadOnlyList<OpenCodeBindingFormOption> FormOptions { get; } =
    [
        new(OpenCodeBindingForm.Chord, Strings.KeybindFormChord, Strings.KeybindFormChordHelp),
        new(OpenCodeBindingForm.Key, Strings.KeybindFormKey, Strings.KeybindFormKeyHelp),
        new(OpenCodeBindingForm.Event, Strings.KeybindFormEvent, Strings.KeybindFormEventHelp),
    ];

    /// <summary>The <c>event</c> values on offer, with the unset placeholder first.</summary>
    public static IReadOnlyList<string> EventOptions { get; } =
        [UnsetEvent, .. OpenCodeKeybindCodec.EventLiterals];

    /// <summary>The action this binding belongs to.</summary>
    public IKeyBindingOwner Owner { get; }

    /// <summary>Which form this binding writes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Form))]
    [NotifyPropertyChangedFor(nameof(FormHelp))]
    [NotifyPropertyChangedFor(nameof(IsChord))]
    [NotifyPropertyChangedFor(nameof(ShowsKeyFields))]
    [NotifyPropertyChangedFor(nameof(ShowsEventFields))]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    private OpenCodeBindingFormOption _selectedForm;

    /// <summary>The chord text, when the key is stated as a string.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _chord;

    /// <summary>
    /// True when the key is stated as an object rather than as a string.
    /// </summary>
    /// <remarks>
    /// ⭐ Separate from <see cref="SelectedForm"/> because the <b>event</b> form's <c>key</c> is
    /// itself <c>string | key-object</c> — the same choice the outer union offers. One flag serves
    /// both places, which is what lets one key control render in both.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsKeyFields))]
    [NotifyPropertyChangedFor(nameof(ShowsChordField))]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _usesStructuredKey;

    /// <summary>The key's <c>name</c>. Required by the object form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _keyName = string.Empty;

    /// <summary>Whether Control is held: true, false, or <see langword="null"/> for absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool? _ctrl;

    /// <summary>Whether Shift is held: true, false, or <see langword="null"/> for absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool? _shift;

    /// <summary>Whether Meta is held: true, false, or <see langword="null"/> for absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool? _meta;

    /// <summary>Whether Super is held: true, false, or <see langword="null"/> for absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool? _super;

    /// <summary>Whether Hyper is held: true, false, or <see langword="null"/> for absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool? _hyper;

    /// <summary>The <c>event</c> value, or <see cref="UnsetEvent"/> when the key is absent.</summary>
    [ObservableProperty] private string _selectedEvent = UnsetEvent;

    /// <summary>The <c>preventDefault</c> flag, or <see langword="null"/> for absent.</summary>
    [ObservableProperty] private bool? _preventDefault;

    /// <summary>The <c>fallthrough</c> flag, or <see langword="null"/> for absent.</summary>
    [ObservableProperty] private bool? _fallthrough;

    /// <summary>True when the binding matched no arm and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(Form))]
    [NotifyPropertyChangedFor(nameof(IsIncomplete))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceOpaqueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    private bool _isOpaque;

    /// <summary>The unreadable binding as JSON, so it can be read and copied.</summary>
    [ObservableProperty] private string _rawText = string.Empty;

    /// <summary>True when the row can move up in its sequence.</summary>
    [ObservableProperty] private bool _canMoveUp;

    /// <summary>True when the row can move down in its sequence.</summary>
    [ObservableProperty] private bool _canMoveDown;

    /// <summary>True when this row belongs to a sequence, so reordering applies.</summary>
    [ObservableProperty] private bool _isInSequence;

    /// <summary>True while this row is waiting for a keystroke to record.</summary>
    /// <remarks>
    /// ⚠ Capture is an explicit mode with an obvious way out, not an always-on handler. A view that
    /// recorded every keypress would make the search box unusable and would capture the Tab that was
    /// meant to leave the field.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCaptureCommand))]
    private bool _isCapturing;

    private IReadOnlyList<KeyValuePair<string, object?>> _keyExtras;

    /// <summary>The form this binding writes, accounting for the opaque state.</summary>
    public OpenCodeBindingForm Form => IsOpaque ? OpenCodeBindingForm.Opaque : SelectedForm.Form;

    /// <summary>What the selected form writes.</summary>
    public string FormHelp => SelectedForm.Description;

    /// <summary>True when the binding has editable fields.</summary>
    public bool IsEditable => !IsOpaque;

    /// <summary>True when the plain chord form applies.</summary>
    public bool IsChord => SelectedForm.Form == OpenCodeBindingForm.Chord;

    /// <summary>True when the event wrapper's own fields apply.</summary>
    public bool ShowsEventFields => SelectedForm.Form == OpenCodeBindingForm.Event;

    /// <summary>True when the structured key fields apply.</summary>
    /// <remarks>
    /// The key form always shows them; the event form shows them only when its <c>key</c> is stated
    /// as an object.
    /// </remarks>
    public bool ShowsKeyFields => SelectedForm.Form switch
    {
        OpenCodeBindingForm.Key => true,
        OpenCodeBindingForm.Event => UsesStructuredKey,
        _ => false,
    };

    /// <summary>True when a plain text box for the key applies.</summary>
    public bool ShowsChordField => SelectedForm.Form switch
    {
        OpenCodeBindingForm.Chord => true,
        OpenCodeBindingForm.Event => !UsesStructuredKey,
        _ => false,
    };

    /// <summary>True when the binding names no key, so the schema rejects it.</summary>
    /// <remarks>
    /// Reachable by one obvious click — clearing the chord, or clearing the structured key's name.
    /// Reported and never repaired: inventing a name would claim a keystroke the user never made.
    /// </remarks>
    public bool IsIncomplete => ToBinding().IsIncomplete;

    /// <summary>The binding as one line, for the collapsed row and for search.</summary>
    /// <remarks>
    /// ⚠ <b>Built from the structured fields, never parsed back out of a chord string.</b> The chord
    /// grammar is undefined by the schema, so the summary shows a chord verbatim rather than
    /// pretending to understand it.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (IsOpaque)
            {
                return Strings.KeybindHeldValue;
            }

            if (!UsesStructuredKey)
            {
                return string.IsNullOrWhiteSpace(Chord) ? Strings.KeybindNoKey : Chord;
            }

            List<string> parts = [];
            if (Ctrl == true)
            {
                parts.Add(Strings.KeybindCtrl);
            }

            if (Shift == true)
            {
                parts.Add(Strings.KeybindShift);
            }

            if (Meta == true)
            {
                parts.Add(Strings.KeybindMeta);
            }

            if (Super == true)
            {
                parts.Add(Strings.KeybindSuper);
            }

            if (Hyper == true)
            {
                parts.Add(Strings.KeybindHyper);
            }

            parts.Add(string.IsNullOrWhiteSpace(KeyName) ? Strings.KeybindNoKey : KeyName);

            // ⚠ Joined with a display separator that is NOT written to the file. The object form is
            // what gets written; this string is for the eye only, which is why it may use a
            // separator no OpenCode grammar has to accept.
            return string.Join(Strings.KeybindSummarySeparator, parts);
        }
    }

    /// <summary>Serialise this row.</summary>
    public OpenCodeKeyBinding ToBinding()
    {
        if (IsOpaque)
        {
            return new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Opaque,
                IsOpaque = true,
                Raw = _rawValue,
            };
        }

        OpenCodeKeySpec? key = UsesStructuredKey
            ? new OpenCodeKeySpec
            {
                Name = KeyName,
                Ctrl = Ctrl,
                Shift = Shift,
                Meta = Meta,
                Super = Super,
                Hyper = Hyper,
                Extras = _keyExtras,
            }
            : null;

        return SelectedForm.Form switch
        {
            // ⭐ The key form has no wrapper, so it always writes the object — a chord typed while
            // the key form was selected would have nowhere to go, which is why the form picker and
            // UsesStructuredKey are separate: choosing Key sets the latter.
            OpenCodeBindingForm.Key => new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Key,
                Key = key ?? new OpenCodeKeySpec { Name = KeyName },
            },

            OpenCodeBindingForm.Event => new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Event,
                Chord = UsesStructuredKey ? string.Empty : Chord,
                Key = key,
                // The sentinel means "the key is absent", which is not the same as an empty string
                // the user could not have typed — the picker offers no way to write one.
                Event = string.IsNullOrEmpty(SelectedEvent) ? null : SelectedEvent,
                PreventDefault = PreventDefault,
                Fallthrough = Fallthrough,
                Extras = _extras,
            },

            _ => new OpenCodeKeyBinding
            {
                Form = OpenCodeBindingForm.Chord,
                Chord = Chord,
            },
        };
    }

    /// <summary>Recompute the reorder commands from this row's position.</summary>
    /// <remarks>
    /// Their <c>CanExecute</c> depends on the row's index, which nothing raises a change for, so the
    /// owner calls this on every row after any structural change.
    /// </remarks>
    public void RefreshMoveCommands(int index, int count, bool inSequence)
    {
        IsInSequence = inSequence;
        CanMoveUp = inSequence && index > 0;
        CanMoveDown = inSequence && index < count - 1;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Record the keystroke the capture control observed, as the structured key form.
    /// </summary>
    /// <param name="captured">The keystroke, already translated by <see cref="OpenCodeKeyCapture"/>.</param>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Only modifiers that were actually held are written.</b> Writing <c>"ctrl": false</c> for
    /// every unheld modifier would turn one keystroke into five keys of noise, and — worse — it would
    /// claim the user made a decision about Hyper that they did not. Capture produces the minimal
    /// object, and an explicitly-false modifier can still be set by hand through its three-state box.
    /// </para>
    /// <para>
    /// <c>hyper</c> is never captured: no desktop platform reports it as a modifier, so there is
    /// nothing to observe. It stays hand-editable.
    /// </para>
    /// </remarks>
    public void ApplyCapture(CapturedKey captured) => AsOneGesture(() =>
    {
        IsCapturing = false;
        UsesStructuredKey = true;
        KeyName = captured.Name;
        Ctrl = captured.Ctrl ? true : null;
        Shift = captured.Shift ? true : null;
        Meta = captured.Meta ? true : null;
        Super = captured.Super ? true : null;

        // The chord text is deliberately NOT cleared: switching back to the chord form should
        // restore what was there, the same preserve-the-other-arm rule the mode pickers follow.
        if (SelectedForm.Form == OpenCodeBindingForm.Chord)
        {
            SelectedForm = OptionFor(OpenCodeBindingForm.Key);
        }
    });

    /// <summary>Start waiting for a keystroke to record.</summary>
    [RelayCommand(CanExecute = nameof(IsEditable))]
    private void StartCapture() => IsCapturing = true;

    /// <summary>Stop waiting, leaving the binding as it was.</summary>
    [RelayCommand(CanExecute = nameof(IsCapturing))]
    private void CancelCapture() => IsCapturing = false;

    /// <summary>Remove this binding from its sequence.</summary>
    [RelayCommand(CanExecute = nameof(IsInSequence))]
    private void Remove() => Owner.RemoveBinding(this);

    /// <summary>Move this binding one place earlier.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Owner.MoveBinding(this, -1);

    /// <summary>Move this binding one place later.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Owner.MoveBinding(this, 1);

    /// <summary>Leave the held state, discarding the verbatim value for an editable one.</summary>
    /// <remarks>
    /// A command rather than a side effect of touching a field: replacing a value the editor could
    /// not read is destructive, and the user should have had to mean it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(IsOpaque))]
    private void ReplaceOpaque() => AsOneGesture(() =>
    {
        IsOpaque = false;
        RawText = string.Empty;
        _keyExtras = [];
        SelectedForm = OptionFor(OpenCodeBindingForm.Chord);
        Chord = string.Empty;
    });

    /// <summary>Report one change to the owner, unless a larger gesture will report it.</summary>
    private void Notify()
    {
        if (!_inGesture)
        {
            Owner.OnBindingChanged();
        }
    }

    /// <summary>
    /// Run <paramref name="gesture"/> as a single reported change, however many fields it writes.
    /// </summary>
    /// <remarks>
    /// Nesting-safe, because <see cref="ApplyCapture"/> assigns <see cref="SelectedForm"/>, whose own
    /// hook is a gesture too — only the outermost one reports.
    /// </remarks>
    private void AsOneGesture(Action gesture)
    {
        bool outermost = !_inGesture;
        _inGesture = true;
        try
        {
            gesture();
        }
        finally
        {
            _inGesture = !outermost;
        }

        if (outermost)
        {
            Owner.OnBindingChanged();
        }
    }

    private static OpenCodeBindingFormOption OptionFor(OpenCodeBindingForm form) =>
        FormOptions.FirstOrDefault(o => o.Form == form) ?? FormOptions[0];

    /// <remarks>
    /// Choosing the key form implies the structured key; choosing the chord form implies the string.
    /// The event form keeps whichever was in use, because both are legal there.
    /// </remarks>
    partial void OnSelectedFormChanged(OpenCodeBindingFormOption value) => AsOneGesture(() =>
    {
        if (value.Form == OpenCodeBindingForm.Key)
        {
            UsesStructuredKey = true;
        }
        else if (value.Form == OpenCodeBindingForm.Chord)
        {
            UsesStructuredKey = false;
        }
    });

    partial void OnChordChanged(string value) => Notify();

    partial void OnUsesStructuredKeyChanged(bool value) => Notify();

    partial void OnKeyNameChanged(string value) => Notify();

    partial void OnCtrlChanged(bool? value) => Notify();

    partial void OnShiftChanged(bool? value) => Notify();

    partial void OnMetaChanged(bool? value) => Notify();

    partial void OnSuperChanged(bool? value) => Notify();

    partial void OnHyperChanged(bool? value) => Notify();

    partial void OnSelectedEventChanged(string value) => Notify();

    partial void OnPreventDefaultChanged(bool? value) => Notify();

    partial void OnFallthroughChanged(bool? value) => Notify();

    private static string Format(object? value) =>
        JsonCurrency.ToJsonNode(value)?.ToJsonString(
            new JsonSerializerOptions { WriteIndented = true })
        ?? "null";
}

/// <summary>
/// What a binding row needs from the action that owns it.
/// </summary>
/// <remarks>
/// An interface rather than a concrete back-reference so the row can be constructed and tested
/// without an action, and so the row's commands cannot reach anything else on the action.
/// </remarks>
public interface IKeyBindingOwner
{
    /// <summary>Report that a field on one of the bindings changed.</summary>
    void OnBindingChanged();

    /// <summary>Remove <paramref name="binding"/> from the sequence.</summary>
    void RemoveBinding(OpenCodeKeyBindingViewModel binding);

    /// <summary>Move <paramref name="binding"/> by <paramref name="offset"/> places.</summary>
    void MoveBinding(OpenCodeKeyBindingViewModel binding, int offset);
}
