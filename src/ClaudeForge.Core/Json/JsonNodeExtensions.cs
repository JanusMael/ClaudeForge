using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;

/// <summary>
/// Safe accessors for <see cref="JsonNode"/> that tolerate hand-edited config files
/// where a value might be the wrong JSON kind (e.g. a numeric <c>"matcher": 42</c>
/// where the schema expects a string).
/// </summary>
/// <remarks>
/// <para>
/// The naive pattern <c>node?.GetValue&lt;string&gt;()</c> short-circuits on a missing
/// key but throws <see cref="System.InvalidOperationException"/> on a type mismatch.
/// Editors that load via <c>LoadFromLayered</c> bubble that exception out of their load
/// method and end up half-wired (subscriptions partially attached, lists partially
/// populated). The user sees a crash with no actionable feedback.
/// </para>
/// <para>
/// Use <see cref="AsStringOrNull"/> at every read of a string-typed JSON value that
/// originates from disk — i.e. anywhere user-edited JSON flows into the editor view
/// models.
/// </para>
/// </remarks>
public static class JsonNodeExtensions
{
    /// <summary>
    /// Returns the string contents of <paramref name="node"/> when it is a JSON string;
    /// otherwise returns <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> for: missing nodes, JSON nulls, JSON numbers/bools/objects/arrays,
    /// and any value where <see cref="JsonValue.TryGetValue{T}(out T)"/> reports failure.
    /// Never throws.
    /// </remarks>
    public static string? AsStringOrNull(this JsonNode? node)
    {
        return node is JsonValue jv && jv.TryGetValue(out string? s) ? s : null;
    }
}