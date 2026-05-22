using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.JsonHelpers;

/// <summary>
/// Locks the contract of <see cref="JsonNodeExtensions.AsStringOrNull"/>: returns the
/// string contents only for JSON-string nodes; never throws on type mismatches.
/// </summary>
[TestClass]
public class JsonNodeExtensionsTests
{
    [TestMethod]
    public void AsStringOrNull_OnNullNode_ReturnsNull()
    {
        JsonNode? node = null;
        Assert.IsNull(node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnStringValue_ReturnsString()
    {
        JsonNode node = JsonValue.Create("hello")!;
        Assert.AreEqual("hello", node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnEmptyString_ReturnsEmptyString()
    {
        JsonNode node = JsonValue.Create("")!;
        Assert.AreEqual("", node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnNumber_ReturnsNullDoesNotThrow()
    {
        // The naive ?.GetValue<string>() pattern throws InvalidOperationException here.
        JsonNode node = JsonValue.Create(42)!;
        Assert.IsNull(node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnBoolean_ReturnsNullDoesNotThrow()
    {
        JsonNode node = JsonValue.Create(true)!;
        Assert.IsNull(node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnObject_ReturnsNullDoesNotThrow()
    {
        JsonNode node = new JsonObject { ["nested"] = "value" };
        Assert.IsNull(node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnArray_ReturnsNullDoesNotThrow()
    {
        JsonNode node = new JsonArray("a", "b");
        Assert.IsNull(node.AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnMissingKey_ReturnsNull()
    {
        // Hand-edited config with a missing optional field — the indexer returns null,
        // the extension method tolerates that, and the caller falls through to its
        // default (mirrors the real call sites in HookEntry/McpServerEntry/etc.).
        JsonObject obj = new() { ["other"] = "x" };
        Assert.IsNull(obj["missing"].AsStringOrNull());
    }

    [TestMethod]
    public void AsStringOrNull_OnPresentTypeMismatchedKey_ReturnsNull()
    {
        // The bug scenario: user wrote {"matcher": 42}. Pre-fix this threw and crashed
        // the editor on load. Post-fix the field is silently treated as absent.
        JsonObject obj = new() { ["matcher"] = 42 };
        Assert.IsNull(obj["matcher"].AsStringOrNull());
    }
}