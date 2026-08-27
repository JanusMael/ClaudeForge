namespace Bennewitz.Ninja.OpenCode.Sdk.Tooling;

/// <summary>
/// Which arm of a <c>formatter</c> / <c>lsp</c> value the file states.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>These are four different claims, and folding any two of them rewrites the user's file.</b>
/// The schema description reads "Omit or set to false to disable, true to enable built-ins, or an
/// object to enable built-ins with overrides" — so <c>false</c> and absent have the same
/// <i>effect</i>, and an empty object has the same effect as <c>true</c>. Neither pair has the same
/// <i>text</i>, and an editor that normalises one into the other turns opening a settings page into
/// an unrequested diff. Same trap as <c>mcp</c>'s three-state <c>oauth</c>.
/// </para>
/// <para>
/// <see cref="NotSet"/> is the zero value deliberately. An editor that has not loaded anything yet
/// must mean "no opinion" and write nothing; making <see cref="Unrecognised"/> the default would
/// have a freshly-constructed editor claim the file holds something it cannot read.
/// </para>
/// </remarks>
public enum OpenCodeToolingMode
{
    /// <summary>The key is absent. Writes nothing.</summary>
    NotSet,

    /// <summary>The literal <c>false</c> — the whole subsystem is off.</summary>
    Disabled,

    /// <summary>The literal <c>true</c> — built-ins on, no per-language overrides.</summary>
    BuiltIns,

    /// <summary>An object — built-ins on, with per-language overrides.</summary>
    Configured,

    /// <summary>
    /// The value matched neither arm and is held verbatim rather than interpreted.
    /// </summary>
    /// <remarks>
    /// ⚠ An explicit JSON <c>null</c> lands here and <b>cannot</b> be written back: the editor
    /// library's value currency uses <see langword="null"/> to mean "remove this key", so there is
    /// no way to express "the key is present and null". That value is schema-invalid either way —
    /// <c>anyOf: [boolean, object]</c> admits neither — so nothing valid is lost, but the collapse
    /// is real and is stated rather than hidden.
    /// </remarks>
    Unrecognised,
}

/// <summary>
/// One entry of a <c>formatter</c> object: a formatter name and the overrides applied to it.
/// </summary>
/// <remarks>
/// <para>
/// The schema gives this one shape — <c>disabled</c> · <c>command</c> · <c>environment</c> ·
/// <c>extensions</c>, with <c>additionalProperties: false</c> and <b>nothing required</b>. So an
/// empty entry is legal, and is written as <c>{}</c> for the same reason an empty agent entry is:
/// the user added the row, no field is required, and withholding it would lose a row they can see.
/// </para>
/// <para>
/// ⚠ <b>The environment key here is <c>environment</c>; the matching key on an
/// <see cref="OpenCodeLspEntry"/> is <c>env</c>.</b> Both objects set
/// <c>additionalProperties: false</c>, so writing one name into the other's entry produces a config
/// OpenCode rejects. They are deliberately separate fields rather than one shared helper, and
/// <c>OpenCodeToolingCodecTests</c> pins both names against the schema.
/// </para>
/// </remarks>
public sealed record OpenCodeFormatterEntry
{
    /// <summary>The formatter name, i.e. the map key, verbatim.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this formatter is off, or <see langword="null"/> when unstated.</summary>
    public bool? Disabled { get; init; }

    /// <summary>The command and its arguments. Position is meaning — this is argv.</summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    /// <summary>Environment variables, in file order. Written under <c>environment</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment { get; init; } = [];

    /// <summary>File extensions this formatter claims.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>
    /// Fields this model does not surface, kept so a round trip does not delete them.
    /// </summary>
    /// <remarks>
    /// The schema says <c>additionalProperties: false</c>, so every entry here is a violation —
    /// which is exactly why they are preserved. A config written by a newer OpenCode is the normal
    /// way to meet one, and an editor that strips what it has not heard of makes upgrading lossy.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>The entry exactly as read, when it was not an object at all.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the entry could not be read and is held verbatim.</summary>
    /// <remarks>
    /// An explicit flag rather than <c>Raw is not null</c>, which cannot tell a JSON <c>null</c>
    /// entry from a readable one — the derivation that made an agent entry round-trip as an
    /// invented empty object in 9a-5.
    /// </remarks>
    public bool IsOpaque { get; init; }
}

/// <summary>
/// One entry of an <c>lsp</c> object: a language-server name and its configuration.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This is a TWO-ARM union, unlike <see cref="OpenCodeFormatterEntry"/>, and the plan
/// describes both as the same per-language shape.</b> Measured against the bundled schema:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Disable-only arm</b> — <c>{ "disabled": true }</c> and nothing else. <c>disabled</c> is
///     <c>required</c> and typed <c>enum: [true]</c>, so the literal <c>false</c> is not permitted
///     here, and <c>additionalProperties: false</c> forbids any companion field.
///   </item>
///   <item>
///     <b>Full arm</b> — <c>command</c> is <b>required</b>, plus optional <c>extensions</c>,
///     <c>disabled</c> (any boolean), <c>env</c> and <c>initialization</c>.
///   </item>
/// </list>
/// <para>
/// ⚠⚠ <b>The consequence is a state a user reaches by one obvious click:</b> unticking "disabled"
/// on a disable-only entry leaves <c>{ "disabled": false }</c>, which matches <b>neither</b> arm —
/// the first needs the literal <c>true</c>, the second needs a command. Same for an entry carrying
/// only <c>extensions</c>. <see cref="IsIncomplete"/> names that state so the editor can report it;
/// it is never silently repaired, because inventing a <c>command</c> would be a claim about the
/// user's machine and deleting their <c>extensions</c> would be a claim about their intent.
/// </para>
/// <para>
/// ⚠ <c>command</c> is the second <c>required</c> field found in Phase 9, after a command
/// template's. Both are reported rather than invented, for the same reason.
/// </para>
/// </remarks>
public sealed record OpenCodeLspEntry
{
    /// <summary>The server name, i.e. the map key, verbatim.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this server is off, or <see langword="null"/> when unstated.</summary>
    public bool? Disabled { get; init; }

    /// <summary>
    /// The command and its arguments. Position is meaning — this is argv. Required by the full arm.
    /// </summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    /// <summary>File extensions this server claims.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Environment variables, in file order. Written under <c>env</c>.</summary>
    /// <remarks>
    /// ⚠ <c>env</c> here, <c>environment</c> on a formatter entry. Not a typo, and not shareable.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, string>> Env { get; init; } = [];

    /// <summary>
    /// The <c>initialization</c> object, passed to the server at startup, or <see langword="null"/>
    /// when the key is absent.
    /// </summary>
    /// <remarks>
    /// Opaque on purpose: the schema types it as <c>object</c> with no declared properties, so
    /// there is no shape to model and nothing this SDK could usefully validate. Same treatment as
    /// a plugin entry's options.
    /// </remarks>
    public object? Initialization { get; init; }

    /// <summary>Fields this model does not surface, kept so a round trip does not delete them.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>The entry exactly as read, when it was not an object at all.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the entry could not be read and is held verbatim.</summary>
    public bool IsOpaque { get; init; }

    /// <summary>True when the entry states nothing at all.</summary>
    /// <remarks>
    /// Written as no entry rather than as <c>{}</c>: unlike a formatter entry, an empty object here
    /// matches neither arm, so persisting one would put a schema violation in the file from a
    /// half-finished click — and on a live-write host that lands immediately. The command editor
    /// made the same call for the same reason.
    /// </remarks>
    public bool IsEmpty =>
        !IsOpaque
        && Disabled is null
        && Command.Count == 0
        && Extensions.Count == 0
        && Env.Count == 0
        && Initialization is null
        && Extras.Count == 0;

    /// <summary>True when the entry writes the disable-only arm, <c>{ "disabled": true }</c>.</summary>
    public bool IsDisableOnly =>
        !IsOpaque
        && Disabled == true
        && Command.Count == 0
        && Extensions.Count == 0
        && Env.Count == 0
        && Initialization is null
        && Extras.Count == 0;

    /// <summary>
    /// True when the entry has content but matches neither arm, because it states no
    /// <see cref="Command"/> and is not the bare disable-only form.
    /// </summary>
    /// <remarks>
    /// Such an entry is still written — withholding what the user typed is the worse failure — and
    /// the editor surfaces it so the reason the config is rejected is visible in the place it was
    /// created rather than in a validator afterwards.
    /// </remarks>
    public bool IsIncomplete => !IsOpaque && !IsEmpty && !IsDisableOnly && Command.Count == 0;
}

/// <summary>
/// A whole <c>formatter</c> value: which arm the file states, plus the entries when it is an object.
/// </summary>
/// <remarks>
/// <see cref="Entries"/> is carried independently of <see cref="Mode"/> so that flipping the mode
/// away from <see cref="OpenCodeToolingMode.Configured"/> and back does not destroy the overrides —
/// the preserve-the-other-arm rule this phase has now needed for permission (global ↔ per-tool),
/// MCP (local ↔ remote), plugin (bare ↔ tuple) and here.
/// </remarks>
public sealed record OpenCodeFormatterConfig
{
    /// <summary>Which arm the file states.</summary>
    public OpenCodeToolingMode Mode { get; init; } = OpenCodeToolingMode.NotSet;

    /// <summary>The value exactly as read, when <see cref="Mode"/> is
    /// <see cref="OpenCodeToolingMode.Unrecognised"/>.</summary>
    public object? Raw { get; init; }

    /// <summary>The per-formatter overrides, in file order.</summary>
    public IReadOnlyList<OpenCodeFormatterEntry> Entries { get; init; } = [];
}

/// <summary>
/// A whole <c>lsp</c> value: which arm the file states, plus the entries when it is an object.
/// </summary>
public sealed record OpenCodeLspConfig
{
    /// <summary>Which arm the file states.</summary>
    public OpenCodeToolingMode Mode { get; init; } = OpenCodeToolingMode.NotSet;

    /// <summary>The value exactly as read, when <see cref="Mode"/> is
    /// <see cref="OpenCodeToolingMode.Unrecognised"/>.</summary>
    public object? Raw { get; init; }

    /// <summary>The per-server configurations, in file order.</summary>
    public IReadOnlyList<OpenCodeLspEntry> Entries { get; init; } = [];
}
