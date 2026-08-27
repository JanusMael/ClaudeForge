using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Turns JSON into value currency and value currency into canonical text, so a round trip can be
/// asserted as one string comparison.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the same helper in <c>OpenCode.Avalonia.Tests</c>, which extracted it first. Every codec
/// test file in this project routes through this copy; the private duplicates the four older ones
/// carried are gone.
/// </para>
/// <para>
/// <see cref="Render"/> preserves key order, which is the point: a comparison that sorted keys
/// would pass while a codec silently reshuffled a user's file.
/// </para>
/// </remarks>
internal static class CurrencyText
{
    /// <summary>Parse JSON into the value-currency graph, with insertion-ordered maps.</summary>
    internal static object? Parse(string json) => FromNode(JsonNode.Parse(json));

    private static object? FromNode(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject obj => Map(obj.Select(
                p => new KeyValuePair<string, object?>(p.Key, FromNode(p.Value)))),
            JsonArray arr => arr.Select(FromNode).ToList(),
            JsonValue v when v.TryGetValue(out bool b) => b,
            JsonValue v when v.TryGetValue(out long l) => l,
            JsonValue v when v.TryGetValue(out double d) => d,
            JsonValue v when v.TryGetValue(out string? s) => s,
            var other => other.ToString(),
        };
    }

    /// <summary>Build an insertion-ordered map.</summary>
    internal static OrderedPropertyMap Map(IEnumerable<KeyValuePair<string, object?>> entries)
    {
        OrderedPropertyMap map = new();
        foreach ((string key, object? value) in entries)
        {
            map.Set(key, value);
        }

        return map;
    }

    /// <summary>Render a currency graph as canonical, order-preserving text.</summary>
    internal static string Render(object? value)
    {
        StringBuilder sb = new();
        Write(value, sb);
        return sb.ToString();
    }

    private static void Write(object? v, StringBuilder sb)
    {
        switch (v)
        {
            case null:
                sb.Append("null");
                break;
            case IReadOnlyDictionary<string, object?> map:
                sb.Append('{');
                bool first = true;
                foreach ((string k, object? mv) in map)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append(k).Append(':');
                    Write(mv, sb);
                }

                sb.Append('}');
                break;
            case IReadOnlyList<object?> list:
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    Write(list[i], sb);
                }

                sb.Append(']');
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            default:
                sb.Append(v);
                break;
        }
    }
}
