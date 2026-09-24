using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// Direct tests for <see cref="JsonCurrency"/>
/// </summary>
/// <remarks>
/// The existing <c>LayeredValueAdapter</c> tests cover the same behaviour
/// indirectly because <c>Normalise</c>/<c>Coerce</c> now delegate here;
/// these tests target the public surface directly so future consumers
/// (post-step-5 SettingsGroupEditorViewModel, MCP tools that deal in
/// currency, etc.) have a clear contract to rely on.
/// </remarks>
public sealed class JsonCurrencyTests
{
    // ── ToJsonNode (currency → JsonNode) ──────────────────────────────

    [Fact]
    public void ToJsonNode_Null_ReturnsNull()
    {
        Assert.Null(JsonCurrency.ToJsonNode(null));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ToJsonNode_Bool_ProducesJsonBoolean(bool input, string expectedJson)
    {
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.NotNull(node);
        Assert.Equal(expectedJson, node.ToJsonString());
    }

    [Fact]
    public void ToJsonNode_Long_ProducesJsonNumber()
    {
        JsonNode? node = JsonCurrency.ToJsonNode(42L);
        Assert.NotNull(node);
        Assert.Equal("42", node.ToJsonString());
    }

    [Fact]
    public void ToJsonNode_Int_WidensToLong()
    {
        JsonNode? node = JsonCurrency.ToJsonNode(42);
        Assert.NotNull(node);
        Assert.Equal("42", node.ToJsonString());
    }

    [Fact]
    public void ToJsonNode_String_ProducesJsonString()
    {
        JsonNode? node = JsonCurrency.ToJsonNode("hello");
        Assert.NotNull(node);
        Assert.Equal("\"hello\"", node.ToJsonString());
    }

    [Fact]
    public void ToJsonNode_List_ProducesJsonArray()
    {
        IReadOnlyList<object?> input = (IReadOnlyList<object?>)["a", 1L, true, null];
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.NotNull(node);
        Assert.IsAssignableFrom<JsonArray>(node);
        Assert.Equal("[\"a\",1,true,null]", node.ToJsonString());
    }

    [Fact]
    public void ToJsonNode_Dict_ProducesJsonObject()
    {
        IReadOnlyDictionary<string, object?> input = (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["a"] = 1L, ["b"] = "x" };
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.NotNull(node);
        Assert.IsAssignableFrom<JsonObject>(node);
        string jstr = node.ToJsonString();
        OrdinalAssert.Contains("\"a\":1", jstr);
        OrdinalAssert.Contains("\"b\":\"x\"", jstr);
    }

    [Fact]
    public void ToJsonNode_NestedShapes_RoundTripStructure()
    {
        IReadOnlyDictionary<string, object?> nested = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["inner"] = (IReadOnlyList<object?>)[1L, 2L, 3L],
        };
        JsonNode? node = JsonCurrency.ToJsonNode(nested);
        Assert.NotNull(node);
        Assert.Equal("{\"inner\":[1,2,3]}", node.ToJsonString());
    }

    // ── FromJsonNode (JsonNode → currency) ────────────────────────────

    [Fact]
    public void FromJsonNode_Null_ReturnsNull()
    {
        Assert.Null(JsonCurrency.FromJsonNode(null));
    }

    [Fact]
    public void FromJsonNode_JsonBool_ReturnsBool()
    {
        Assert.True((bool?)JsonCurrency.FromJsonNode(JsonValue.Create(true)));
        Assert.False((bool?)JsonCurrency.FromJsonNode(JsonValue.Create(false)));
    }

    [Fact]
    public void FromJsonNode_JsonInteger_ReturnsLong()
    {
        object? result = JsonCurrency.FromJsonNode(JsonValue.Create(42));
        Assert.Equal(42L, result);
        Assert.IsAssignableFrom<long>(result);
    }

    [Fact]
    public void FromJsonNode_JsonString_ReturnsString()
    {
        Assert.Equal("hello", JsonCurrency.FromJsonNode(JsonValue.Create("hello")));
    }

    [Fact]
    public void FromJsonNode_JsonArray_ReturnsListOfCurrency()
    {
        JsonArray arr = new("a", 1, true);
        IReadOnlyList<object?>? result = JsonCurrency.FromJsonNode(arr) as IReadOnlyList<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal(1L, result[1]); // widened to long
        Assert.True((bool?)result[2]);
    }

    [Fact]
    public void FromJsonNode_JsonObject_ReturnsDictOfCurrency()
    {
        JsonObject obj = new() { ["k"] = "v", ["n"] = 7 };
        IReadOnlyDictionary<string, object?>? result = JsonCurrency.FromJsonNode(obj) as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(result);
        Assert.Equal("v", result["k"]);
        Assert.Equal(7L, result["n"]);
    }

    // ── Round-trip ────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_NestedStructure_PreservesShape()
    {
        IReadOnlyDictionary<string, object?> original = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["s"] = "x",
            ["n"] = 42L,
            ["b"] = true,
            ["arr"] = (IReadOnlyList<object?>)["a", 1L, false],
        };

        JsonNode? asJson = JsonCurrency.ToJsonNode(original);
        Assert.NotNull(asJson);
        IReadOnlyDictionary<string, object?>? roundTripped = JsonCurrency.FromJsonNode(asJson) as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(roundTripped);
        Assert.Equal("x", roundTripped["s"]);
        Assert.Equal(42L, roundTripped["n"]);
        Assert.True((bool?)roundTripped["b"]);
        IReadOnlyList<object?>? arr = roundTripped["arr"] as IReadOnlyList<object?>;
        Assert.NotNull(arr);
        Assert.Equal(3, arr.Count);
    }
}