using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tooling;

/// <summary>
/// Reads and writes the <c>formatter</c> and <c>lsp</c> values in the editor library's value
/// currency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both halves of both formats live in this one file</b>, and the two formats share it, because
/// the part they have in common is the part that would drift: the four-state mode
/// (<c>absent</c> / <c>false</c> / <c>true</c> / object) is identical for both keys, and two
/// implementations of it would eventually disagree about which states may be folded. Everything
/// below the mode is deliberately <i>not</i> shared — see <see cref="OpenCodeLspEntry"/>.
/// </para>
/// <para>
/// ⛔ <b>The plan describes both keys as one shape, "a per-language map of
/// <c>disabled</c> · <c>command[]</c> · <c>environment</c> · <c>extensions</c>". Measured against
/// the bundled schema, that is right for <c>formatter</c> and wrong for <c>lsp</c> three ways:</b>
/// an <c>lsp</c> entry is a two-arm union whose full arm <b>requires</b> <c>command</c>; its
/// environment key is <c>env</c>, not <c>environment</c>; and it carries an extra untyped
/// <c>initialization</c> object. Both entry objects set <c>additionalProperties: false</c>, so
/// borrowing either name for the other key produces a config OpenCode rejects.
/// </para>
/// <para>
/// ⚠ <b>Keys inside one entry are written in schema order, so the first save can reorder a
/// hand-written entry.</b> Accepted deliberately, on the same grounds as the <c>mcp</c> codec: an
/// entry has at most six keys and their order carries no meaning. Collections <i>within</i> an
/// entry keep file order — argv because position is meaning, environment maps because a reshuffle
/// turns a one-line change into an unreviewable diff.
/// </para>
/// </remarks>
public static class OpenCodeToolingCodec
{
    /// <summary>Parse a <c>formatter</c> value.</summary>
    /// <param name="value">The value at the editing scope, in the library's currency.</param>
    /// <param name="isDefined">
    /// Whether the editing scope defines the key at all. Required because
    /// <c>IEditorValue.GetValueAt</c> returns <see langword="null"/> for both "absent" and
    /// "explicitly null", and those are different modes.
    /// </param>
    public static OpenCodeFormatterConfig ReadFormatter(object? value, bool isDefined)
    {
        (OpenCodeToolingMode mode, object? raw) = ClassifyMode(value, isDefined);
        if (mode != OpenCodeToolingMode.Configured)
        {
            return new OpenCodeFormatterConfig { Mode = mode, Raw = raw };
        }

        List<OpenCodeFormatterEntry> entries = [];
        foreach ((string name, object? entry) in (IReadOnlyDictionary<string, object?>)value!)
        {
            entries.Add(ReadFormatterEntry(name, entry));
        }

        return new OpenCodeFormatterConfig { Mode = mode, Entries = entries };
    }

    /// <summary>Parse an <c>lsp</c> value.</summary>
    /// <param name="value">The value at the editing scope, in the library's currency.</param>
    /// <param name="isDefined">Whether the editing scope defines the key at all.</param>
    public static OpenCodeLspConfig ReadLsp(object? value, bool isDefined)
    {
        (OpenCodeToolingMode mode, object? raw) = ClassifyMode(value, isDefined);
        if (mode != OpenCodeToolingMode.Configured)
        {
            return new OpenCodeLspConfig { Mode = mode, Raw = raw };
        }

        List<OpenCodeLspEntry> entries = [];
        foreach ((string name, object? entry) in (IReadOnlyDictionary<string, object?>)value!)
        {
            entries.Add(ReadLspEntry(name, entry));
        }

        return new OpenCodeLspConfig { Mode = mode, Entries = entries };
    }

    /// <summary>
    /// Serialise a <c>formatter</c> value, or <see langword="null"/> when the key should be removed.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><see cref="OpenCodeToolingMode.Configured"/> with no entries writes <c>{}</c>, not
    /// <see langword="null"/>.</b> That is the deliberate exception to this phase's
    /// "empty means remove the key" rule, and it is not a corner case: an absent key means the
    /// subsystem is off, and <c>{}</c> means built-ins are on with no overrides yet. Collapsing the
    /// empty object to a removal would undo the only thing the user's mode choice did.
    /// </remarks>
    public static object? WriteFormatter(OpenCodeFormatterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Mode != OpenCodeToolingMode.Configured)
        {
            return WriteMode(config.Mode, config.Raw);
        }

        OrderedPropertyMap map = new();
        foreach (OpenCodeFormatterEntry entry in config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            map.Set(entry.Name, WriteFormatterEntry(entry));
        }

        return map;
    }

    /// <summary>
    /// Serialise an <c>lsp</c> value, or <see langword="null"/> when the key should be removed.
    /// </summary>
    /// <remarks>
    /// An entry stating nothing at all is skipped — unlike a formatter entry, <c>{}</c> matches
    /// neither <c>lsp</c> arm, so writing one would persist a schema violation created by a
    /// half-finished click. Any entry with content is written, including one that matches neither
    /// arm: withholding what the user typed is the worse failure, and
    /// <see cref="OpenCodeLspEntry.IsIncomplete"/> is how the editor tells them.
    /// </remarks>
    public static object? WriteLsp(OpenCodeLspConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Mode != OpenCodeToolingMode.Configured)
        {
            return WriteMode(config.Mode, config.Raw);
        }

        OrderedPropertyMap map = new();
        foreach (OpenCodeLspEntry entry in config.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.IsEmpty)
            {
                continue;
            }

            map.Set(entry.Name, WriteLspEntry(entry));
        }

        return map;
    }

    /// <summary>
    /// Classify the four non-object-content states. Shared so the two keys cannot disagree about
    /// which of <c>absent</c> / <c>false</c> / <c>true</c> / object they are looking at.
    /// </summary>
    private static (OpenCodeToolingMode Mode, object? Raw) ClassifyMode(object? value, bool isDefined)
    {
        if (!isDefined)
        {
            return (OpenCodeToolingMode.NotSet, null);
        }

        return value switch
        {
            bool flag => (flag ? OpenCodeToolingMode.BuiltIns : OpenCodeToolingMode.Disabled, null),
            IReadOnlyDictionary<string, object?> => (OpenCodeToolingMode.Configured, null),
            _ => (OpenCodeToolingMode.Unrecognised, value),
        };
    }

    /// <summary>Serialise every mode except <see cref="OpenCodeToolingMode.Configured"/>.</summary>
    private static object? WriteMode(OpenCodeToolingMode mode, object? raw) => mode switch
    {
        OpenCodeToolingMode.Disabled => false,
        OpenCodeToolingMode.BuiltIns => true,
        OpenCodeToolingMode.Unrecognised => raw,
        // NotSet, and Configured never reaches here.
        _ => null,
    };

    private static OpenCodeFormatterEntry ReadFormatterEntry(string name, object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeFormatterEntry { Name = name, IsOpaque = true, Raw = value };
        }

        bool? disabled = null;
        IReadOnlyList<string> command = [];
        IReadOnlyList<KeyValuePair<string, string>> environment = [];
        IReadOnlyList<string> extensions = [];
        List<KeyValuePair<string, object?>> extras = [];

        foreach ((string key, object? item) in map)
        {
            // A surfaced key whose value is the WRONG SHAPE goes to Extras rather than being read
            // as absent. Without this a non-boolean `disabled` or a mixed-type `command` reads as
            // null and then vanishes on save — and each is already schema-invalid, i.e. exactly
            // when the user needs to see it. One uniform rule, learned in 9a-5.
            switch (key)
            {
                case "disabled" when item is bool flag:
                    disabled = flag;
                    break;
                case "command" when TryReadStringList(item, out IReadOnlyList<string> argv):
                    command = argv;
                    break;
                case "environment" when TryReadStringMap(
                    item, out IReadOnlyList<KeyValuePair<string, string>> pairs):
                    environment = pairs;
                    break;
                case "extensions" when TryReadStringList(item, out IReadOnlyList<string> exts):
                    extensions = exts;
                    break;
                default:
                    extras.Add(new KeyValuePair<string, object?>(key, item));
                    break;
            }
        }

        return new OpenCodeFormatterEntry
        {
            Name = name,
            Disabled = disabled,
            Command = command,
            Environment = environment,
            Extensions = extensions,
            Extras = extras,
        };
    }

    private static OpenCodeLspEntry ReadLspEntry(string name, object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeLspEntry { Name = name, IsOpaque = true, Raw = value };
        }

        bool? disabled = null;
        IReadOnlyList<string> command = [];
        IReadOnlyList<string> extensions = [];
        IReadOnlyList<KeyValuePair<string, string>> env = [];
        object? initialization = null;
        List<KeyValuePair<string, object?>> extras = [];

        foreach ((string key, object? item) in map)
        {
            switch (key)
            {
                case "disabled" when item is bool flag:
                    disabled = flag;
                    break;
                case "command" when TryReadStringList(item, out IReadOnlyList<string> argv):
                    command = argv;
                    break;
                case "extensions" when TryReadStringList(item, out IReadOnlyList<string> exts):
                    extensions = exts;
                    break;
                // `env`, NOT `environment`. The formatter entry's key is the other one, and both
                // objects forbid additional properties.
                case "env" when TryReadStringMap(
                    item, out IReadOnlyList<KeyValuePair<string, string>> pairs):
                    env = pairs;
                    break;
                case "initialization" when item is IReadOnlyDictionary<string, object?>:
                    initialization = item;
                    break;
                default:
                    extras.Add(new KeyValuePair<string, object?>(key, item));
                    break;
            }
        }

        return new OpenCodeLspEntry
        {
            Name = name,
            Disabled = disabled,
            Command = command,
            Extensions = extensions,
            Env = env,
            Initialization = initialization,
            Extras = extras,
        };
    }

    private static object? WriteFormatterEntry(OpenCodeFormatterEntry entry)
    {
        if (entry.IsOpaque)
        {
            return entry.Raw;
        }

        OrderedPropertyMap map = new();

        if (entry.Disabled is bool disabled)
        {
            map.Set("disabled", disabled);
        }

        if (entry.Command.Count > 0)
        {
            map.Set("command", ToCurrencyList(entry.Command));
        }

        if (entry.Environment.Count > 0)
        {
            map.Set("environment", ToCurrencyMap(entry.Environment));
        }

        if (entry.Extensions.Count > 0)
        {
            map.Set("extensions", ToCurrencyList(entry.Extensions));
        }

        foreach ((string key, object? value) in entry.Extras)
        {
            map.Set(key, value);
        }

        // An empty entry writes `{}`. Legal — the schema requires nothing here — and the same call
        // the agent editor made: the user added the row, so it is theirs to see.
        return map;
    }

    private static object? WriteLspEntry(OpenCodeLspEntry entry)
    {
        if (entry.IsOpaque)
        {
            return entry.Raw;
        }

        // The full arm's declared order. The disable-only arm falls out of it: with no command and
        // no companions, this emits exactly `{ "disabled": true }`.
        OrderedPropertyMap map = new();

        if (entry.Command.Count > 0)
        {
            map.Set("command", ToCurrencyList(entry.Command));
        }

        if (entry.Extensions.Count > 0)
        {
            map.Set("extensions", ToCurrencyList(entry.Extensions));
        }

        if (entry.Disabled is bool disabled)
        {
            map.Set("disabled", disabled);
        }

        if (entry.Env.Count > 0)
        {
            map.Set("env", ToCurrencyMap(entry.Env));
        }

        if (entry.Initialization is not null)
        {
            map.Set("initialization", entry.Initialization);
        }

        foreach ((string key, object? value) in entry.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }

    /// <summary>
    /// Read an array of strings, failing when any item is not a string.
    /// </summary>
    /// <remarks>
    /// All-or-nothing on purpose. A mixed array cannot be modelled as <c>string[]</c>, and keeping
    /// only the string items would delete the rest on the next write — so the whole value goes to
    /// <c>Extras</c> instead, where it survives.
    /// </remarks>
    private static bool TryReadStringList(object? value, out IReadOnlyList<string> items)
    {
        items = [];
        if (value is not IReadOnlyList<object?> list)
        {
            return false;
        }

        List<string> read = [];
        foreach (object? item in list)
        {
            if (item is not string text)
            {
                return false;
            }

            read.Add(text);
        }

        items = read;
        return true;
    }

    /// <summary>Read a map of string values, failing when any value is not a string.</summary>
    private static bool TryReadStringMap(
        object? value,
        out IReadOnlyList<KeyValuePair<string, string>> pairs)
    {
        pairs = [];
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return false;
        }

        List<KeyValuePair<string, string>> read = [];
        foreach ((string key, object? item) in map)
        {
            if (item is not string text)
            {
                return false;
            }

            read.Add(new KeyValuePair<string, string>(key, text));
        }

        pairs = read;
        return true;
    }

    private static IReadOnlyList<object?> ToCurrencyList(IReadOnlyList<string> items) =>
        [.. items.Cast<object?>()];

    /// <remarks>
    /// An <see cref="OrderedPropertyMap"/> rather than a plain dictionary at <i>this</i> level too:
    /// a canary in 9a-2 proved that swapping only the inner map leaves every behavioural order test
    /// green, so the guarantee has to be made at every level to hold anywhere.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> ToCurrencyMap(
        IReadOnlyList<KeyValuePair<string, string>> pairs)
    {
        OrderedPropertyMap map = new();
        foreach ((string key, string value) in pairs)
        {
            map.Set(key, value);
        }

        return map;
    }
}
