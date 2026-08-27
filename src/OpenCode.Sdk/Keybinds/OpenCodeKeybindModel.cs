namespace Bennewitz.Ninja.OpenCode.Sdk.Keybinds;

/// <summary>
/// Which arm of a single <c>keybinds.&lt;action&gt;</c> value the file states.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The plan describes this union as
/// <c>false | "none" | string | {name,ctrl,shift,meta,super,hyper} | …</c>, and the two shapes
/// hidden behind that <c>…</c> are the two that decide the model.</b> Measured against the bundled
/// <c>opencode-tui.json</c>, each action's value is a FOUR-arm <c>anyOf</c>:
/// </para>
/// <list type="number">
///   <item><c>{"type":"boolean","enum":[false]}</c> — the literal <c>false</c> and nothing else.
///   <b>The literal <c>true</c> is not admitted</b>, which is why <see cref="Disabled"/> has no
///   enabled counterpart.</item>
///   <item><c>{"type":"string","enum":["none"]}</c> — the literal <c>"none"</c>.</item>
///   <item>An <b>inner <c>anyOf</c> of three</b>: a bare <c>string</c>, a key object, or an
///   <i>event</i> object (<c>key</c> required, plus <c>event</c> / <c>preventDefault</c> /
///   <c>fallthrough</c>). See <see cref="OpenCodeKeyBinding"/>.</item>
///   <item>An <b><c>array</c></b> of that same inner union — several bindings for one action.</item>
/// </list>
/// <para>
/// ⚠⚠ <b><see cref="Bound"/> and <see cref="Sequence"/> must stay separate, because <c>"x"</c> and
/// <c>["x"]</c> are different files.</b> A one-element array is arm 4, not arm 3, and collapsing it
/// looks like tidying while silently moving the value to the other arm of a union. That is the same
/// trap <c>plugin[]</c> hit with <c>"foo"</c> versus <c>["foo", {}]</c> — the fourth time this phase
/// has met it.
/// </para>
/// <para>
/// ⚠ <see cref="Disabled"/> and <see cref="None"/> are also kept apart. They plainly have the same
/// <i>effect</i> — the action is unbound — but they are not the same <i>text</i>, and this phase's
/// rule is that an editor never rewrites one spelling of a value into another. Same call as
/// <c>formatter</c>'s <c>false</c>-versus-absent and <c>mcp</c>'s three-state <c>oauth</c>.
/// </para>
/// <para>
/// <see cref="NotSet"/> is the zero value deliberately: a row the user has not touched must mean
/// "no opinion" and write nothing.
/// </para>
/// </remarks>
public enum OpenCodeKeybindMode
{
    /// <summary>The action is absent from the file. Writes nothing.</summary>
    NotSet,

    /// <summary>The literal <c>false</c>.</summary>
    Disabled,

    /// <summary>The literal string <c>"none"</c>.</summary>
    None,

    /// <summary>Exactly one binding, written as the binding itself rather than as an array.</summary>
    Bound,

    /// <summary>An array of bindings, written as an array even when it holds one.</summary>
    Sequence,

    /// <summary>The value matched no arm and is held verbatim rather than interpreted.</summary>
    /// <remarks>
    /// ⚠ An explicit JSON <c>null</c> lands here and cannot be written back: the editor library's
    /// value currency uses <see langword="null"/> to mean "remove this key". Schema-invalid either
    /// way. Same limitation, stated the same way, as the <c>formatter</c> / <c>lsp</c> and
    /// <c>autoupdate</c> modes.
    /// </remarks>
    Unrecognised,
}

/// <summary>
/// A key named structurally: the key's own name plus the modifiers held with it.
/// </summary>
/// <remarks>
/// <para>
/// The schema's object form is <c>{name, ctrl, shift, meta, super, hyper}</c> with <c>name</c>
/// <b>required</b> and <c>additionalProperties: false</c>. Every modifier is an ordinary optional
/// boolean, so <see langword="null"/> means the key is absent and <c>false</c> means it is present
/// and off — two different files, so two different states, as everywhere else in this phase.
/// </para>
/// <para>
/// ⭐ <b>This is the form the capture control writes, and the reason is that the schema does not
/// define the other one.</b> Arm 3a is a bare <c>string</c> with <b>no <c>pattern</c></b>, so every
/// string validates and the chord grammar lives entirely in OpenCode's parser. Turning a captured
/// <c>Ctrl</c>+<c>X</c> into text would mean choosing between <c>"ctrl+x"</c>, <c>"C-x"</c> and
/// <c>"ctrl-x"</c> on no evidence — inventing a grammar and writing the guess into the user's file.
/// This object form is specified exactly, so it is what capture produces; a string a user typed is
/// preserved as the string they typed.
/// </para>
/// </remarks>
public sealed record OpenCodeKeySpec
{
    /// <summary>The key's name. Required by the schema.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether Control is held, or <see langword="null"/> when unstated.</summary>
    public bool? Ctrl { get; init; }

    /// <summary>Whether Shift is held, or <see langword="null"/> when unstated.</summary>
    public bool? Shift { get; init; }

    /// <summary>Whether Meta is held, or <see langword="null"/> when unstated.</summary>
    public bool? Meta { get; init; }

    /// <summary>Whether Super is held, or <see langword="null"/> when unstated.</summary>
    public bool? Super { get; init; }

    /// <summary>Whether Hyper is held, or <see langword="null"/> when unstated.</summary>
    public bool? Hyper { get; init; }

    /// <summary>Fields this model does not surface, kept so a round trip does not delete them.</summary>
    /// <remarks>
    /// The key object sets <c>additionalProperties: false</c>, so anything here is a violation —
    /// which is why it is preserved rather than stripped. A config written by a newer OpenCode is
    /// the ordinary way to meet one.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>True when the required <c>name</c> is missing or blank.</summary>
    /// <remarks>
    /// Reported, never invented. Inventing a name would claim a key the user never pressed; writing
    /// <c>""</c> would claim the key is named the empty string. Both leave the file invalid and only
    /// one lies — the same call <c>command{}</c>'s required <c>template</c> forced.
    /// </remarks>
    public bool IsIncomplete => string.IsNullOrWhiteSpace(Name);
}

/// <summary>
/// Which of the inner union's three arms a single binding states.
/// </summary>
public enum OpenCodeBindingForm
{
    /// <summary>A bare string, held exactly as written.</summary>
    Chord,

    /// <summary>The key object — <c>{name, ctrl, …}</c>.</summary>
    Key,

    /// <summary>
    /// The event object — <c>key</c> (itself a string or a key object) plus <c>event</c>,
    /// <c>preventDefault</c> and <c>fallthrough</c>.
    /// </summary>
    Event,

    /// <summary>The binding matched no arm and is held verbatim.</summary>
    Opaque,
}

/// <summary>
/// One binding: a key, optionally wrapped in the event object that adds delivery options.
/// </summary>
/// <remarks>
/// <para>
/// The inner union's three arms collapse to <b>a key reference plus an optional event wrapper</b>,
/// because arm 3c's <c>key</c> property is itself <c>string | key-object</c> — the same choice arms
/// 3a and 3b offer one level up. So <see cref="Chord"/> and <see cref="Key"/> carry the key directly
/// and <see cref="OpenCodeBindingForm.Event"/> carries the same key inside a wrapper.
/// </para>
/// <para>
/// ⛔ <b>The event object does NOT set <c>additionalProperties: false</c>, unlike the key object
/// beside it.</b> Measured, not assumed. So an unrecognised field there is <i>legal</i>, which makes
/// preserving it correctness rather than courtesy — exactly the distinction <c>AgentConfig</c> drew
/// against <c>McpConfig</c> in 9a-5.
/// </para>
/// <para>
/// ⚠ <c>key</c> is <c>required</c> on the event object, the third such field found in Phase 9 after
/// a command's <c>template</c> and an <c>lsp</c> entry's <c>command</c>. It is reported through
/// <see cref="IsIncomplete"/> and never invented.
/// </para>
/// </remarks>
public sealed record OpenCodeKeyBinding
{
    /// <summary>Which arm this binding states.</summary>
    public OpenCodeBindingForm Form { get; init; } = OpenCodeBindingForm.Chord;

    /// <summary>
    /// The binding as the user typed it, when <see cref="Form"/> is
    /// <see cref="OpenCodeBindingForm.Chord"/>, or the event wrapper's string <c>key</c>.
    /// </summary>
    /// <remarks>
    /// Held verbatim. The string arm declares no <c>pattern</c>, so this SDK has no grammar to
    /// validate against and any "correction" would be a guess about OpenCode's parser.
    /// </remarks>
    public string Chord { get; init; } = string.Empty;

    /// <summary>
    /// The structured key, when the key is stated as an object rather than a string.
    /// </summary>
    public OpenCodeKeySpec? Key { get; init; }

    /// <summary>
    /// The <c>event</c> value — <c>"press"</c> or <c>"release"</c> — or <see langword="null"/> when
    /// unstated.
    /// </summary>
    /// <remarks>
    /// Kept as the raw string rather than an enum so a value outside the schema's two literals
    /// survives a round trip instead of being folded onto whichever one an enum defaults to.
    /// </remarks>
    public string? Event { get; init; }

    /// <summary>The <c>preventDefault</c> flag, or <see langword="null"/> when unstated.</summary>
    public bool? PreventDefault { get; init; }

    /// <summary>The <c>fallthrough</c> flag, or <see langword="null"/> when unstated.</summary>
    public bool? Fallthrough { get; init; }

    /// <summary>
    /// Fields of the event object this model does not surface, kept so a round trip preserves them.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>The binding exactly as read, when it matched no arm.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the binding could not be read and is held verbatim.</summary>
    /// <remarks>
    /// An explicit flag rather than <c>Raw is not null</c>. That derivation cannot tell a JSON
    /// <c>null</c> from a readable value, and it is what made an agent entry round-trip as an
    /// invented empty object in 9a-5.
    /// </remarks>
    public bool IsOpaque { get; init; }

    /// <summary>
    /// True when the binding states a shape the schema rejects, because the key it names is missing.
    /// </summary>
    /// <remarks>
    /// Reachable by one obvious click — clearing the text of a chord, or clearing the <c>name</c> of
    /// a structured key. Reported so the reason the config is rejected shows up where it was created,
    /// and never repaired: the same rule the <c>lsp</c> editor follows for an entry that matches
    /// neither arm.
    /// </remarks>
    public bool IsIncomplete => Form switch
    {
        OpenCodeBindingForm.Opaque => false,
        OpenCodeBindingForm.Key => Key is null || Key.IsIncomplete,
        // The event wrapper needs its `key`, whichever of the two forms states it.
        OpenCodeBindingForm.Event => Key is null
            ? string.IsNullOrWhiteSpace(Chord)
            : Key.IsIncomplete,
        _ => string.IsNullOrWhiteSpace(Chord),
    };
}

/// <summary>
/// A whole <c>keybinds.&lt;action&gt;</c> value: which arm the file states, plus its bindings.
/// </summary>
/// <remarks>
/// <see cref="Bindings"/> is carried independently of <see cref="Mode"/> so that switching to
/// <see cref="OpenCodeKeybindMode.Disabled"/> and back does not destroy what was bound — the
/// preserve-the-other-arm rule this phase has needed for permission (global ↔ per-tool), MCP
/// (local ↔ remote), plugin (bare ↔ tuple), formatter/lsp (mode ↔ entries), and here.
/// </remarks>
public sealed record OpenCodeKeybindValue
{
    /// <summary>Which arm the file states.</summary>
    public OpenCodeKeybindMode Mode { get; init; } = OpenCodeKeybindMode.NotSet;

    /// <summary>
    /// The bindings, in file order. Exactly one for <see cref="OpenCodeKeybindMode.Bound"/>; any
    /// number, including none, for <see cref="OpenCodeKeybindMode.Sequence"/>.
    /// </summary>
    public IReadOnlyList<OpenCodeKeyBinding> Bindings { get; init; } = [];

    /// <summary>
    /// The value exactly as read, when <see cref="Mode"/> is
    /// <see cref="OpenCodeKeybindMode.Unrecognised"/>.
    /// </summary>
    public object? Raw { get; init; }
}

/// <summary>
/// One action of the <c>keybinds</c> object: its name and the value stated for it.
/// </summary>
/// <param name="Action">The action name, i.e. the map key, verbatim.</param>
/// <param name="Value">What the file says this action is bound to.</param>
/// <param name="IsKnown">
/// Whether <paramref name="Action"/> is one of the actions the schema declares.
/// </param>
/// <remarks>
/// ⚠ <b><c>keybinds</c> sets <c>additionalProperties: false</c>, so an unknown action name is a
/// schema violation — which is exactly why it is preserved.</b> The ordinary way to meet one is a
/// config written by a newer OpenCode that has since added the action, and an editor that silently
/// drops what it has not heard of makes upgrading lossy. <paramref name="IsKnown"/> lets the editor
/// say so without deciding on the user's behalf.
/// </remarks>
public sealed record OpenCodeKeybindEntry(
    string Action,
    OpenCodeKeybindValue Value,
    bool IsKnown = true);

/// <summary>
/// A whole <c>keybinds</c> value: the actions the file states, in file order.
/// </summary>
/// <remarks>
/// Only actions the file actually mentions appear here. The editor pairs this with the schema's own
/// list of 184 declared actions to show rows for the rest — the schema states <b>no <c>default</c>
/// for any action</b> (checked: 184 descriptions, zero defaults), so an unmentioned action is shown
/// as "not set" and never as a guess at what OpenCode does on its own.
/// </remarks>
public sealed record OpenCodeKeybindConfig
{
    /// <summary>True when the key is present in the file at all.</summary>
    /// <remarks>
    /// An empty <c>keybinds: {}</c> and an absent <c>keybinds</c> are different files. Neither
    /// changes any binding, so the distinction is only about not rewriting what the user wrote.
    /// </remarks>
    public bool IsDefined { get; init; }

    /// <summary>The value exactly as read, when it was present but not an object.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the value was present but could not be read as an object.</summary>
    public bool IsOpaque { get; init; }

    /// <summary>The actions the file states, in file order.</summary>
    public IReadOnlyList<OpenCodeKeybindEntry> Entries { get; init; } = [];
}
