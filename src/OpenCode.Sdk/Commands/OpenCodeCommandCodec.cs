using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Commands;

/// <summary>
/// One entry of the <c>command</c> map: a named slash-command definition.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b><see cref="Template"/> is the only <c>required</c> field anywhere in this phase.</b> The
/// schema declares <c>required: ["template"]</c>, so an entry without one is invalid — and unlike
/// every optional field, "absent" here is not a legitimate state to write. The codec still refuses
/// to invent one: a blank template is omitted and reported, because writing <c>"template": ""</c>
/// would be a claim that the command's body is empty rather than missing.
/// </para>
/// <para>
/// ⚠ <b><c>additionalProperties: false</c></b>, unlike <c>AgentConfig</c>. Unknown fields are
/// therefore schema violations rather than legal extensions — which is exactly why
/// <see cref="Extras"/> still exists. A config written by a newer OpenCode is the normal way to
/// meet one, and stripping it on save is worse than round-tripping something this build cannot use.
/// </para>
/// </remarks>
public sealed record OpenCodeCommandConfig
{
    /// <summary>The command body. Required by the schema.</summary>
    /// <remarks>
    /// Supports <c>$ARGUMENTS</c>, positional <c>$1</c>…<c>$n</c>, <c>@file</c> references, and
    /// <c>!`cmd`</c> shell interpolation — see <see cref="UsesShellInterpolation"/>.
    /// </remarks>
    public string? Template { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>The agent this command runs as.</summary>
    public string? Agent { get; init; }

    /// <summary>Model id override.</summary>
    public string? Model { get; init; }

    /// <summary>Model variant override.</summary>
    public string? Variant { get; init; }

    /// <summary>Whether the command runs as a subtask.</summary>
    public bool? Subtask { get; init; }

    /// <summary>Fields this model does not surface, preserved in the order they arrived.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>The entry exactly as read, when it was not an object.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the entry was not an object, so <see cref="Raw"/> is written back.</summary>
    /// <remarks>
    /// An explicit flag rather than <c>Raw is not null</c>, which cannot tell a JSON <c>null</c>
    /// entry from an object one — the same derivation that made an agent entry round-trip as an
    /// invented empty object.
    /// </remarks>
    public bool IsOpaque { get; init; }

    /// <summary>
    /// True when <see cref="Template"/> is missing or blank, so the entry violates the schema.
    /// </summary>
    public bool IsTemplateMissing =>
        !IsOpaque && string.IsNullOrWhiteSpace(Template);

    /// <summary>
    /// True when the template contains <c>!`…`</c>, which runs a shell command when the command is
    /// invoked.
    /// </summary>
    /// <remarks>
    /// ⚠ Worth surfacing rather than leaving buried in a text box. A command template is not inert
    /// data: this syntax executes whatever it wraps, with the user's privileges, every time the
    /// command runs — and a template arriving from a shared or copied config is exactly where
    /// nobody looks. This editor only <b>detects and reports</b> it; nothing here runs anything.
    /// </remarks>
    public bool UsesShellInterpolation => ContainsShellInterpolation(Template);

    /// <summary>
    /// Whether <paramref name="template"/> contains the <c>!`…`</c> shell-interpolation form.
    /// </summary>
    /// <remarks>
    /// Deliberately a plain scan for <c>!`</c> followed by a closing backtick, not an attempt to
    /// parse the template language. Over-precision here would be a liability: the point is to
    /// flag "this may run a shell command, go and read it", and a detector that tried to be clever
    /// about escaping would miss cases while looking authoritative.
    /// </remarks>
    public static bool ContainsShellInterpolation(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return false;
        }

        int marker = template.IndexOf("!`", StringComparison.Ordinal);
        return marker >= 0
               && template.IndexOf('`', marker + 2) > marker + 1;
    }
}

/// <summary>
/// Reads and writes the <c>command</c> map in the editor library's value currency.
/// </summary>
/// <remarks>
/// Read and write live in one file, as in the MCP and agent codecs, so the two halves of one format
/// cannot drift. No <c>JsonNode</c> in any signature: the currency conversion has no case for one,
/// so a <c>JsonNode</c> handed back to the editor library is stringified.
/// </remarks>
public static class OpenCodeCommandCodec
{
    /// <summary>Fields surfaced, in schema declaration order.</summary>
    private static readonly string[] SurfacedKeys =
        ["template", "description", "agent", "model", "variant", "subtask"];

    private static readonly HashSet<string> Surfaced = new(SurfacedKeys, StringComparer.Ordinal);

    /// <summary>Parse the whole <c>command</c> map, preserving entry order.</summary>
    /// <remarks>
    /// ⚠ Order comes from the map passed in, which this type cannot verify. Command order carries
    /// no meaning, but reshuffling a user's file turns a one-line change into an unreadable diff.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, OpenCodeCommandConfig>> ReadMap(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return [];
        }

        List<KeyValuePair<string, OpenCodeCommandConfig>> commands = [];
        foreach ((string name, object? entry) in map)
        {
            commands.Add(new KeyValuePair<string, OpenCodeCommandConfig>(name, ReadCommand(entry)));
        }

        return commands;
    }

    /// <summary>Parse one command entry.</summary>
    public static OpenCodeCommandConfig ReadCommand(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return new OpenCodeCommandConfig { IsOpaque = true, Raw = value };
        }

        // Same uniform rule as the agent codec: a surfaced key whose value is the wrong shape goes
        // to extras rather than being read, so it survives instead of reading as null and then
        // vanishing on save. A `subtask` written as the string "true" is the likely case.
        List<KeyValuePair<string, object?>> extras = [];
        foreach ((string key, object? entry) in map)
        {
            if (!Surfaced.Contains(key) || !FitsSurfacedShape(key, entry))
            {
                extras.Add(new KeyValuePair<string, object?>(key, entry));
            }
        }

        return new OpenCodeCommandConfig
        {
            Template = Str(map, "template"),
            Description = Str(map, "description"),
            Agent = Str(map, "agent"),
            Model = Str(map, "model"),
            Variant = Str(map, "variant"),
            Subtask = Bool(map, "subtask"),
            Extras = extras,
        };
    }

    /// <summary>Serialise the whole map, or <see langword="null"/> when there is nothing to write.</summary>
    public static object? WriteMap(IReadOnlyList<KeyValuePair<string, OpenCodeCommandConfig>> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        OrderedPropertyMap map = new();
        foreach ((string name, OpenCodeCommandConfig command) in commands)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            object? written = WriteCommand(command);

            // ⚠ An entry with NOTHING in it is skipped, unlike an empty agent entry — because an
            // empty agent is legal (no required fields) and an empty command is not. A user who
            // types a name and clicks Add has a row to fill in, not a command; writing `{}` for it
            // would put a schema-invalid entry in their config, and on a live-write host that
            // happens the instant they click. Any single field present is enough to write it, so
            // nothing they actually typed is withheld.
            if (written is IReadOnlyDictionary<string, object?> { Count: 0 })
            {
                continue;
            }

            map.Set(name, written);
        }

        return map.Count == 0 ? null : map;
    }

    /// <summary>Serialise one command entry.</summary>
    /// <remarks>
    /// ⚠ A blank <see cref="OpenCodeCommandConfig.Template"/> is omitted rather than written as an
    /// empty string. Both leave the entry schema-invalid, but only one of them lies about what the
    /// command does — and <see cref="OpenCodeCommandConfig.IsTemplateMissing"/> is what tells the
    /// user, rather than a silently repaired file.
    /// </remarks>
    public static object? WriteCommand(OpenCodeCommandConfig command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.IsOpaque)
        {
            return command.Raw;
        }

        OrderedPropertyMap map = new();
        SetText(map, "template", command.Template);
        SetText(map, "description", command.Description);
        SetText(map, "agent", command.Agent);
        SetText(map, "model", command.Model);
        SetText(map, "variant", command.Variant);

        if (command.Subtask is { } subtask)
        {
            map.Set("subtask", subtask);
        }

        foreach ((string key, object? value) in command.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }

    private static bool FitsSurfacedShape(string key, object? value)
    {
        return key switch
        {
            "template" or "description" or "agent" or "model" or "variant" => value is string,
            "subtask" => value is bool,
            var _ => false,
        };
    }

    private static string? Str(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) ? v as string : null;

    private static bool? Bool(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) && v is bool b ? b : null;

    /// <remarks>
    /// ⚠ Whitespace counts as absent for every field EXCEPT that this is also how a cleared box is
    /// distinguished from an untouched one — persisting <c>"model": ""</c> would override an
    /// inherited model with an empty one, which is a setting rather than the absence of one.
    /// </remarks>
    private static void SetText(OrderedPropertyMap map, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            map.Set(key, value);
        }
    }
}
