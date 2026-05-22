using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Bridges between <see cref="JsonNode"/> and the value-currency contract
/// the reusable editor library speaks:
/// <c>null | bool | string | long | double |
/// IReadOnlyList&lt;object?&gt; | IReadOnlyDictionary&lt;string, object?&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// extracted from <c>ClaudeValueAdapter.Coerce</c> /
/// <c>ClaudeValueAdapter.Normalise</c> so consumers that aren't building an
/// <see cref="LayeredEditors.Abstractions.IEditorValue"/> wrapper can still
/// reach the same conversions.  In particular, when steps 2–6 of the
/// leaf-editor migration delete the App-side bridge editors,
/// <see cref="Bennewitz.Ninja.ClaudeForge.ViewModels.SettingsGroupEditorViewModel"/>'s
/// live-write loop will go from <c>editor.ToJsonValue()</c> (App-bridge,
/// returns <c>JsonNode?</c> directly) to
/// <c>JsonCurrency.ToJsonNode(editor.ToValue())</c> (library, returns
/// currency that this helper converts).
/// </para>
/// <para>
/// <b>Naming:</b> "currency" is the term the library docs already use
/// for the canonical primitive set the editor-library leaf VMs return
/// from <c>ToValue()</c>.  <see cref="ToJsonNode"/> takes currency and
/// produces a <see cref="JsonNode"/>; <see cref="FromJsonNode"/> takes a
/// <see cref="JsonNode"/> and produces currency.  The <c>ClaudeValueAdapter</c>
/// delegates to these for its internal <c>Coerce</c>/<c>Normalise</c>
/// helpers so behaviour stays identical.
/// </para>
/// </remarks>
public static class JsonCurrency
{
    /// <summary>
    /// Convert a value-currency object graph to a <see cref="JsonNode"/>
    /// suitable for <c>SettingsWorkspace.SetValue</c>.  Best-effort for
    /// non-currency types (stringifies via <see cref="object.ToString"/>).
    /// </summary>
    public static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            bool bv => JsonValue.Create(bv),
            long lv => JsonValue.Create(lv),
            int iv => JsonValue.Create((long)iv),
            double dv => JsonValue.Create(dv),
            float fv => JsonValue.Create((double)fv),
            string sv => JsonValue.Create(sv),
            IReadOnlyList<object?> list => CoerceArray(list),
            IReadOnlyDictionary<string, object?> map => CoerceObject(map),
            var _ => JsonValue.Create(value.ToString()),
        };
    }

    /// <summary>
    /// Convert a <see cref="JsonNode"/> into the value-currency object graph.
    /// Raw <see cref="JsonNode"/> instances never escape this method.
    /// </summary>
    public static object? FromJsonNode(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node switch
        {
            JsonValue jv => FromScalar(jv),
            JsonArray ja => FromArray(ja),
            JsonObject jo => FromObject(jo),
            var _ => node.ToString(), // fallback — should not occur
        };
    }

    // ── ToJsonNode helpers ─────────────────────────────────────────────

    private static JsonArray CoerceArray(IReadOnlyList<object?> list)
    {
        JsonArray arr = new();
        foreach (object? item in list)
        {
            arr.Add(ToJsonNode(item));
        }

        return arr;
    }

    private static JsonObject CoerceObject(IReadOnlyDictionary<string, object?> map)
    {
        JsonObject obj = new();
        foreach ((string key, object? value) in map)
        {
            obj[key] = ToJsonNode(value);
        }

        return obj;
    }

    // ── FromJsonNode helpers ───────────────────────────────────────────

    private static object? FromScalar(JsonValue jv)
    {
        // Try currency types — exact match first, then widen numeric CLR types
        if (jv.TryGetValue(out bool b))
        {
            return b;
        }

        if (jv.TryGetValue(out long l))
        {
            return l;
        }

        if (jv.TryGetValue(out int i))
        {
            return (long)i;
        }

        if (jv.TryGetValue(out short sh))
        {
            return (long)sh;
        }

        if (jv.TryGetValue(out byte by))
        {
            return (long)by;
        }

        if (jv.TryGetValue(out double d))
        {
            return d;
        }

        if (jv.TryGetValue(out float f))
        {
            return (double)f;
        }

        if (jv.TryGetValue(out string? s))
        {
            return s;
        }

        // Fallback: use JsonElement for values that came from JSON parsing
        try
        {
            JsonElement elem = jv.GetValue<JsonElement>();
            return elem.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when elem.TryGetInt64(out long li) => li,
                JsonValueKind.Number when elem.TryGetDouble(out double di) => di,
                JsonValueKind.String => elem.GetString(),
                JsonValueKind.Null => null,
                var _ => elem.ToString(),
            };
        }
        catch (InvalidOperationException)
        {
            return jv.ToString();
        }
    }

    private static IReadOnlyList<object?> FromArray(JsonArray ja)
    {
        return ja.Select(n => FromJsonNode(n)).ToList();
    }

    private static IReadOnlyDictionary<string, object?> FromObject(JsonObject jo)
    {
        Dictionary<string, object?> dict = new(StringComparer.Ordinal);
        foreach ((string key, JsonNode? value) in jo)
        {
            dict[key] = FromJsonNode(value);
        }

        return dict;
    }
}