using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Agents;

/// <summary>
/// Reads and writes the <c>agent</c> map in the editor library's value currency.
/// </summary>
/// <remarks>
/// <para>
/// Read and write live in one file, as in <c>OpenCodeMcpCodec</c>, so the two halves of one format
/// cannot drift apart.
/// </para>
/// <para>
/// ⚠ <b>Absent and default are different, and this is where that is enforced.</b> An agent entry is
/// usually a partial override of a built-in, so a field the user never set must not appear in the
/// written value — writing <c>"temperature": 0</c> for an untouched box would override the model's
/// own default with a real setting. Every writer here is conditional on the value being present.
/// </para>
/// <para>
/// ⚠ <b>No <c>JsonNode</c> in any signature</b>: the currency conversion has no case for one, so a
/// <c>JsonNode</c> handed back to the editor library is stringified — an entire agent definition
/// would land in the user's config as a single quoted string.
/// </para>
/// </remarks>
public static class OpenCodeAgentCodec
{
    /// <summary>Fields <see cref="OpenCodeAgentConfig"/> surfaces, in schema declaration order.</summary>
    private static readonly string[] SurfacedKeys =
    [
        "model", "variant", "temperature", "top_p", "prompt", "tools", "disable", "description",
        "mode", "hidden", "options", "color", "steps", "maxSteps", "permission",
    ];

    private static readonly HashSet<string> Surfaced = new(SurfacedKeys, StringComparer.Ordinal);

    /// <summary>
    /// Parse the whole <c>agent</c> map, preserving entry order.
    /// </summary>
    /// <remarks>
    /// ⚠ Order comes from the map passed in, which this type cannot verify:
    /// <see cref="Dictionary{TKey,TValue}"/> promises no enumeration order. Agent order carries no
    /// meaning, but reshuffling a user's file turns a one-line change into an unreviewable diff.
    /// </remarks>
    public static IReadOnlyList<KeyValuePair<string, OpenCodeAgentConfig>> ReadMap(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return [];
        }

        List<KeyValuePair<string, OpenCodeAgentConfig>> agents = [];
        foreach ((string name, object? entry) in map)
        {
            agents.Add(new KeyValuePair<string, OpenCodeAgentConfig>(name, ReadAgent(entry)));
        }

        return agents;
    }

    /// <summary>Parse one agent entry.</summary>
    public static OpenCodeAgentConfig ReadAgent(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            // Not an object at all. Held verbatim rather than reshaped into an empty agent, which
            // would silently replace whatever the user actually wrote.
            return new OpenCodeAgentConfig { IsOpaque = true, Raw = value };
        }

        // ⚠ A surfaced key whose value is the WRONG SHAPE is routed to extras rather than read.
        // Without this, every typed reader below would return null for it and the key would then
        // vanish on save: an unrecognised `mode`, a `temperature` written as a string, a `tools`
        // map with a non-boolean value. Each is already invalid per the schema, which is precisely
        // when a user needs to see it rather than have the editor quietly delete it. One uniform
        // rule removes the whole class instead of one case at a time.
        List<KeyValuePair<string, object?>> extras = [];
        foreach ((string key, object? entry) in map)
        {
            if (!Surfaced.Contains(key) || !FitsSurfacedShape(key, entry))
            {
                extras.Add(new KeyValuePair<string, object?>(key, entry));
            }
        }

        return new OpenCodeAgentConfig
        {
            Model = Str(map, "model"),
            Variant = Str(map, "variant"),
            Temperature = Num(map, "temperature"),
            TopP = Num(map, "top_p"),
            Prompt = Str(map, "prompt"),
            Tools = ReadBoolMap(map, "tools"),
            Disable = Bool(map, "disable"),
            Description = Str(map, "description"),
            Mode = ReadMode(Str(map, "mode")),
            Hidden = Bool(map, "hidden"),
            Options = map.TryGetValue("options", out object? options) ? options : null,
            Color = Str(map, "color"),
            Steps = Int(map, "steps"),
            MaxSteps = Int(map, "maxSteps"),
            Permission = map.TryGetValue("permission", out object? permission) ? permission : null,
            Extras = extras,
        };
    }

    /// <summary>
    /// Serialise the whole map, or <see langword="null"/> when there is nothing to write.
    /// </summary>
    public static object? WriteMap(IReadOnlyList<KeyValuePair<string, OpenCodeAgentConfig>> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        OrderedPropertyMap map = new();
        foreach ((string name, OpenCodeAgentConfig agent) in agents)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            map.Set(name, WriteAgent(agent));
        }

        return map.Count == 0 ? null : map;
    }

    /// <summary>Serialise one agent entry.</summary>
    /// <remarks>
    /// ⚠ An entry that was never an object comes back as
    /// <see cref="OpenCodeAgentConfig.Raw"/> — exactly as read.
    /// </remarks>
    public static object? WriteAgent(OpenCodeAgentConfig agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (agent.IsOpaque)
        {
            return agent.Raw;
        }

        OrderedPropertyMap map = new();
        SetText(map, "model", agent.Model);
        SetText(map, "variant", agent.Variant);
        SetNum(map, "temperature", agent.Temperature);
        SetNum(map, "top_p", agent.TopP);
        SetText(map, "prompt", agent.Prompt);

        if (agent.Tools.Count > 0)
        {
            OrderedPropertyMap tools = new();
            foreach ((string tool, bool enabled) in agent.Tools)
            {
                if (!string.IsNullOrWhiteSpace(tool))
                {
                    tools.Set(tool, enabled);
                }
            }

            if (tools.Count > 0)
            {
                map.Set("tools", tools);
            }
        }

        SetBool(map, "disable", agent.Disable);
        SetText(map, "description", agent.Description);

        if (agent.Mode != OpenCodeAgentMode.Unset)
        {
            map.Set("mode", ModeToWire(agent.Mode));
        }

        SetBool(map, "hidden", agent.Hidden);

        if (agent.Options is not null)
        {
            map.Set("options", agent.Options);
        }

        SetText(map, "color", agent.Color);
        SetInt(map, "steps", agent.Steps);
        SetInt(map, "maxSteps", agent.MaxSteps);

        if (agent.Permission is not null)
        {
            map.Set("permission", agent.Permission);
        }

        foreach ((string key, object? value) in agent.Extras)
        {
            map.Set(key, value);
        }

        return map;
    }

    /// <summary>The string OpenCode writes for <paramref name="mode"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is <see cref="OpenCodeAgentMode.Unset"/>, which means the key is
    /// absent — there is no text for it, and inventing one sets something the user did not.
    /// </exception>
    public static string ModeToWire(OpenCodeAgentMode mode)
    {
        return mode switch
        {
            OpenCodeAgentMode.Subagent => "subagent",
            OpenCodeAgentMode.Primary => "primary",
            OpenCodeAgentMode.All => "all",
            var _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unset means the mode key is absent, so it has no written form."),
        };
    }

    /// <summary>
    /// Whether <paramref name="value"/> is the shape this model can read for
    /// <paramref name="key"/>. A "no" sends the key to
    /// <see cref="OpenCodeAgentConfig.Extras"/> so it survives untouched.
    /// </summary>
    private static bool FitsSurfacedShape(string key, object? value)
    {
        return key switch
        {
            "model" or "variant" or "prompt" or "description" or "color" => value is string,
            "temperature" or "top_p" => value is double or long,
            "steps" or "maxSteps" => value is long || (value is double d && d == Math.Floor(d)),
            "disable" or "hidden" => value is bool,
            "mode" => value is string s && s is "subagent" or "primary" or "all",
            // Every value in the map must be boolean. One that is not sends the WHOLE key to
            // extras: surfacing a partial tool map would drop the odd entry on save, and a
            // per-entry passthrough inside a surfaced field is more machinery than a deprecated
            // field deserves.
            "tools" => value is IReadOnlyDictionary<string, object?> tools
                       && tools.Values.All(v => v is bool),
            // Opaque by design — anything at all round-trips.
            "options" or "permission" => true,
            var _ => false,
        };
    }

    private static OpenCodeAgentMode ReadMode(string? text)
    {
        return text switch
        {
            "subagent" => OpenCodeAgentMode.Subagent,
            "primary" => OpenCodeAgentMode.Primary,
            "all" => OpenCodeAgentMode.All,
            var _ => OpenCodeAgentMode.Unset,
        };
    }

    private static string? Str(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) ? v as string : null;

    private static bool? Bool(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v) && v is bool b ? b : null;

    /// <remarks>
    /// A JSON number can arrive as either <see cref="long"/> or <see cref="double"/> depending on
    /// how it was written — <c>1</c> versus <c>1.0</c>. Reading only one of the two silently blanks
    /// the user's temperature.
    /// </remarks>
    private static double? Num(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v)
            ? v switch { double d => d, long l => l, var _ => null }
            : null;

    private static long? Int(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? v)
            ? v switch
            {
                long l => l,
                double d when d == Math.Floor(d) => (long)d,
                var _ => null,
            }
            : null;

    private static IReadOnlyList<KeyValuePair<string, bool>> ReadBoolMap(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? v)
            || v is not IReadOnlyDictionary<string, object?> inner)
        {
            return [];
        }

        List<KeyValuePair<string, bool>> pairs = [];
        foreach ((string k, object? value) in inner)
        {
            if (value is bool b)
            {
                pairs.Add(new KeyValuePair<string, bool>(k, b));
            }
        }

        return pairs;
    }

    /// <remarks>
    /// Whitespace counts as absent. Persisting <c>"model": ""</c> for a cleared box would override
    /// the inherited model with an empty one, which is a setting rather than the absence of one.
    /// </remarks>
    private static void SetText(OrderedPropertyMap map, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            map.Set(key, value);
        }
    }

    private static void SetBool(OrderedPropertyMap map, string key, bool? value)
    {
        if (value is { } b)
        {
            map.Set(key, b);
        }
    }

    private static void SetNum(OrderedPropertyMap map, string key, double? value)
    {
        if (value is { } d)
        {
            map.Set(key, d);
        }
    }

    private static void SetInt(OrderedPropertyMap map, string key, long? value)
    {
        if (value is { } l)
        {
            map.Set(key, l);
        }
    }
}
