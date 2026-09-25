using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.JsonHelpers;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.JsonHelpers;

/// <summary>
/// Locks the contract of <see cref="JsonNodeExtensions.AsStringOrNull"/>: returns the
/// string contents only for JSON-string nodes; never throws on type mismatches.
/// </summary>
public class JsonNodeExtensionsTests
{
    [Fact]
    public void AsStringOrNull_OnNullNode_ReturnsNull()
    {
        JsonNode? node = null;
        Assert.Null(node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnStringValue_ReturnsString()
    {
        JsonNode node = JsonValue.Create("hello")!;
        Assert.Equal("hello", node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnEmptyString_ReturnsEmptyString()
    {
        JsonNode node = JsonValue.Create("")!;
        Assert.Equal("", node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnNumber_ReturnsNullDoesNotThrow()
    {
        // The naive ?.GetValue<string>() pattern throws InvalidOperationException here.
        JsonNode node = JsonValue.Create(42)!;
        Assert.Null(node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnBoolean_ReturnsNullDoesNotThrow()
    {
        JsonNode node = JsonValue.Create(true)!;
        Assert.Null(node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnObject_ReturnsNullDoesNotThrow()
    {
        JsonNode node = new JsonObject { ["nested"] = "value" };
        Assert.Null(node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnArray_ReturnsNullDoesNotThrow()
    {
        JsonNode node = new JsonArray("a", "b");
        Assert.Null(node.AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnMissingKey_ReturnsNull()
    {
        // Hand-edited config with a missing optional field — the indexer returns null,
        // the extension method tolerates that, and the caller falls through to its
        // default (mirrors the real call sites in HookEntry/McpServerEntry/etc.).
        JsonObject obj = new() { ["other"] = "x" };
        Assert.Null(obj["missing"].AsStringOrNull());
    }

    [Fact]
    public void AsStringOrNull_OnPresentTypeMismatchedKey_ReturnsNull()
    {
        // The bug scenario: user wrote {"matcher": 42}. Pre-fix this threw and crashed
        // the editor on load. Post-fix the field is silently treated as absent.
        JsonObject obj = new() { ["matcher"] = 42 };
        Assert.Null(obj["matcher"].AsStringOrNull());
    }
}