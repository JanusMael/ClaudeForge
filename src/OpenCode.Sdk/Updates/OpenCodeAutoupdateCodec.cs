namespace Bennewitz.Ninja.OpenCode.Sdk.Updates;

/// <summary>
/// What an <c>autoupdate</c> value says.
/// </summary>
/// <remarks>
/// <para>
/// The schema is <c>anyOf: [boolean, {"type": "string", "enum": ["notify"]}]</c> — "Set to true to
/// auto-update, false to disable, or 'notify' to show update notifications". ✅ <b>The plan's
/// description of this key was right</b>, which is worth saying out loud after six of the previous
/// eight slices found it wrong.
/// </para>
/// <para>
/// ⭐ <b>This is the only <c>boolean | scalar</c> union in either bundled schema</b> — surveyed, not
/// assumed: of 20 unions in the config schema and 924 in the TUI schema, this is the one whose arms
/// are a boolean and a string. So there is no family here to generalise for, and one editor
/// registered for one property name is the whole job.
/// </para>
/// <para>
/// ⚠ Three values plus absent is four states, and the generic dispatch renders the lot as a single
/// free-text box — confirmed by reading the running app's automation tree, where <c>autoupdate</c>
/// appeared as <c>Edit|autoupdate</c>. A text box invites <c>"true"</c>, <c>"yes"</c> and
/// <c>"Notify"</c>, none of which this schema admits.
/// </para>
/// </remarks>
public enum OpenCodeAutoupdateMode
{
    /// <summary>The key is absent. Writes nothing.</summary>
    NotSet,

    /// <summary>The literal <c>false</c> — never update.</summary>
    Disabled,

    /// <summary>The literal <c>true</c> — update automatically.</summary>
    Automatic,

    /// <summary>The literal string <c>"notify"</c> — show a notification, do not update.</summary>
    Notify,

    /// <summary>The value matched no arm and is held verbatim rather than interpreted.</summary>
    /// <remarks>
    /// ⚠ An explicit JSON <c>null</c> lands here and cannot be written back: the value currency uses
    /// <see langword="null"/> to mean "remove this key". That value is schema-invalid anyway. Same
    /// limitation, stated the same way, as the <c>formatter</c> / <c>lsp</c> mode.
    /// </remarks>
    Unrecognised,
}

/// <summary>
/// A whole <c>autoupdate</c> value: which arm the file states, plus the verbatim text when none.
/// </summary>
public sealed record OpenCodeAutoupdateConfig
{
    /// <summary>Which arm the file states.</summary>
    public OpenCodeAutoupdateMode Mode { get; init; } = OpenCodeAutoupdateMode.NotSet;

    /// <summary>
    /// The value exactly as read, when <see cref="Mode"/> is
    /// <see cref="OpenCodeAutoupdateMode.Unrecognised"/>.
    /// </summary>
    public object? Raw { get; init; }
}

/// <summary>
/// Reads and writes <c>autoupdate</c> in the editor library's value currency.
/// </summary>
/// <remarks>
/// Read and write live together for the reason every codec in this phase does: they are the pair
/// that drifts, and a reader that accepts a spelling the writer never produces is how a config
/// quietly changes on save.
/// </remarks>
public static class OpenCodeAutoupdateCodec
{
    /// <summary>The one string the schema's enum arm admits.</summary>
    public const string NotifyLiteral = "notify";

    /// <summary>Parse an <c>autoupdate</c> value.</summary>
    /// <param name="value">The value at the editing scope, in the library's currency.</param>
    /// <param name="isDefined">
    /// Whether the editing scope defines the key at all. Required because
    /// <c>IEditorValue.GetValueAt</c> returns <see langword="null"/> for both "absent" and
    /// "explicitly null", and those are different states.
    /// </param>
    /// <remarks>
    /// ⚠⚠ <b>The string arm is matched with <see cref="StringComparison.Ordinal"/>, so
    /// <c>"Notify"</c> is NOT read as <c>"notify"</c>.</b> The schema's enum is exact, and accepting
    /// the variant would mean silently rewriting a user's typo into a different value on the next
    /// save. Holding it verbatim shows them the problem instead of hiding the fix — the same call
    /// this phase made for every other near-miss it found.
    /// </remarks>
    public static OpenCodeAutoupdateConfig Read(object? value, bool isDefined)
    {
        if (!isDefined)
        {
            return new OpenCodeAutoupdateConfig { Mode = OpenCodeAutoupdateMode.NotSet };
        }

        return value switch
        {
            bool flag => new OpenCodeAutoupdateConfig
            {
                Mode = flag ? OpenCodeAutoupdateMode.Automatic : OpenCodeAutoupdateMode.Disabled,
            },
            string text when string.Equals(text, NotifyLiteral, StringComparison.Ordinal) =>
                new OpenCodeAutoupdateConfig { Mode = OpenCodeAutoupdateMode.Notify },
            _ => new OpenCodeAutoupdateConfig
            {
                Mode = OpenCodeAutoupdateMode.Unrecognised,
                Raw = value,
            },
        };
    }

    /// <summary>
    /// Serialise an <c>autoupdate</c> value, or <see langword="null"/> when the key should be
    /// removed.
    /// </summary>
    public static object? Write(OpenCodeAutoupdateConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.Mode switch
        {
            OpenCodeAutoupdateMode.Disabled => false,
            OpenCodeAutoupdateMode.Automatic => true,
            OpenCodeAutoupdateMode.Notify => NotifyLiteral,
            OpenCodeAutoupdateMode.Unrecognised => config.Raw,
            // NotSet.
            _ => null,
        };
    }
}
