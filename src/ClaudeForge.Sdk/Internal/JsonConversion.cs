using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

/// <summary>
/// Trim-safe conversion between SDK consumer types and the underlying
/// <see cref="JsonNode"/> shape that <see cref="Bennewitz.Ninja.ClaudeForge.Core.Settings.SettingsWorkspace"/>
/// stores.
/// </summary>
/// <remarks>
/// <para>
/// The SDK's generic escape hatch (<c>SetValue&lt;T&gt;</c> / <c>GetEffective&lt;T&gt;</c>)
/// only supports the JSON-currency primitives plus <see cref="JsonNode"/>
/// passthrough. <see cref="System.Text.Json.JsonSerializer"/> with reflection-based
/// serialisation is intentionally avoided here so the SDK remains trim-safe
/// when consumed from a <c>PublishTrimmed=true</c> host (the existing GUI uses
/// trimmed Release builds).
/// </para>
/// <para>
/// Consumers who need to round-trip arbitrary <c>T</c> values should either
/// pass a pre-built <see cref="JsonNode"/> / <see cref="JsonObject"/>
/// / <see cref="JsonArray"/>, or use the strongly-typed accessors
/// (<see cref="IClaudeConfigClient.Permissions"/> et al.) which know the
/// shape of the values they expose.
/// </para>
/// </remarks>
internal static class JsonConversion
{
    /// <summary>
    /// Convert an SDK consumer value to the <see cref="JsonNode"/> shape
    /// <see cref="Bennewitz.Ninja.ClaudeForge.Core.Settings.SettingsWorkspace.SetValue"/> requires.
    /// </summary>
    public static JsonNode? ConvertToJsonNode<T>(T value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonNode node)
        {
            return node.DeepClone();
        }

        return value switch
        {
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            float f => JsonValue.Create(f),
            decimal m => JsonValue.Create(m),
            var _ => throw new NotSupportedException(
                $"SetValue<{typeof(T).Name}> only supports JSON primitive values (string / bool / number) " +
                "and JsonNode / JsonObject / JsonArray. Use the strongly-typed accessors for compound objects, " +
                "or pass a pre-built JsonNode."),
        };
    }

    /// <summary>
    /// Convert a <see cref="JsonNode"/> read from the workspace to the SDK
    /// consumer's requested type. Returns <c>default(T)</c> when the node is
    /// <see langword="null"/> or when conversion is not possible.
    /// </summary>
    public static T? ConvertFromJsonNode<T>(JsonNode? node)
    {
        if (node is null)
        {
            return default;
        }

        // JsonNode passthrough — the consumer asked for the raw shape.
        if (typeof(T) == typeof(JsonNode))
        {
            return (T)(object)node;
        }

        if (typeof(T) == typeof(JsonObject))
        {
            return node is JsonObject jo ? (T)(object)jo : default;
        }

        if (typeof(T) == typeof(JsonArray))
        {
            return node is JsonArray ja ? (T)(object)ja : default;
        }

        if (typeof(T) == typeof(JsonValue))
        {
            return node is JsonValue jv ? (T)(object)jv : default;
        }

        if (node is JsonValue jval)
        {
            // Reference primitives.
            if (typeof(T) == typeof(string) && jval.TryGetValue(out string? s))
            {
                return (T)(object)s;
            }

            // Value primitives — including their Nullable<T> variants. The
            // pattern below handles both because JsonValue.TryGetValue<int>
            // returns the raw int, which we then box once for the (T)(object)
            // cast that works for both `int` and `int?`.
            if ((typeof(T) == typeof(bool) || typeof(T) == typeof(bool?)) && jval.TryGetValue(out bool b))
            {
                return (T)(object)b;
            }

            if ((typeof(T) == typeof(int) || typeof(T) == typeof(int?)) && jval.TryGetValue(out int i))
            {
                return (T)(object)i;
            }

            if ((typeof(T) == typeof(long) || typeof(T) == typeof(long?)) && jval.TryGetValue(out long l))
            {
                return (T)(object)l;
            }

            if ((typeof(T) == typeof(double) || typeof(T) == typeof(double?)) && jval.TryGetValue(out double d))
            {
                return (T)(object)d;
            }

            if ((typeof(T) == typeof(float) || typeof(T) == typeof(float?)) && jval.TryGetValue(out float f))
            {
                return (T)(object)f;
            }

            if ((typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?)) && jval.TryGetValue(out decimal m))
            {
                return (T)(object)m;
            }
        }

        return default;
    }
}