using Bennewitz.Ninja.ClaudeForge.Adapters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

/// <summary>
/// Direct tests for <see cref="JsonCurrency"/>
/// </summary>
/// <remarks>
/// The existing <c>ClaudeValueAdapter</c> tests cover the same behaviour
/// indirectly because <c>Normalise</c>/<c>Coerce</c> now delegate here;
/// these tests target the public surface directly so future consumers
/// (post-step-5 SettingsGroupEditorViewModel, MCP tools that deal in
/// currency, etc.) have a clear contract to rely on.
/// </remarks>
[TestClass]
public sealed class JsonCurrencyTests
{
    // ── ToJsonNode (currency → JsonNode) ──────────────────────────────

    [TestMethod]
    public void ToJsonNode_Null_ReturnsNull()
    {
        Assert.IsNull(JsonCurrency.ToJsonNode(null));
    }

    [TestMethod]
    [DataRow(true, "true")]
    [DataRow(false, "false")]
    public void ToJsonNode_Bool_ProducesJsonBoolean(bool input, string expectedJson)
    {
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.IsNotNull(node);
        Assert.AreEqual(expectedJson, node.ToJsonString());
    }

    [TestMethod]
    public void ToJsonNode_Long_ProducesJsonNumber()
    {
        JsonNode? node = JsonCurrency.ToJsonNode(42L);
        Assert.IsNotNull(node);
        Assert.AreEqual("42", node.ToJsonString());
    }

    [TestMethod]
    public void ToJsonNode_Int_WidensToLong()
    {
        JsonNode? node = JsonCurrency.ToJsonNode(42);
        Assert.IsNotNull(node);
        Assert.AreEqual("42", node.ToJsonString());
    }

    [TestMethod]
    public void ToJsonNode_String_ProducesJsonString()
    {
        JsonNode? node = JsonCurrency.ToJsonNode("hello");
        Assert.IsNotNull(node);
        Assert.AreEqual("\"hello\"", node.ToJsonString());
    }

    [TestMethod]
    public void ToJsonNode_List_ProducesJsonArray()
    {
        IReadOnlyList<object?> input = (IReadOnlyList<object?>)["a", 1L, true, null];
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.IsNotNull(node);
        Assert.IsInstanceOfType<JsonArray>(node);
        Assert.AreEqual("[\"a\",1,true,null]", node.ToJsonString());
    }

    [TestMethod]
    public void ToJsonNode_Dict_ProducesJsonObject()
    {
        IReadOnlyDictionary<string, object?> input = (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?> { ["a"] = 1L, ["b"] = "x" };
        JsonNode? node = JsonCurrency.ToJsonNode(input);
        Assert.IsNotNull(node);
        Assert.IsInstanceOfType<JsonObject>(node);
        string jstr = node.ToJsonString();
        StringAssert.Contains(jstr, "\"a\":1");
        StringAssert.Contains(jstr, "\"b\":\"x\"");
    }

    [TestMethod]
    public void ToJsonNode_NestedShapes_RoundTripStructure()
    {
        IReadOnlyDictionary<string, object?> nested = (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["inner"] = (IReadOnlyList<object?>)[1L, 2L, 3L],
        };
        JsonNode? node = JsonCurrency.ToJsonNode(nested);
        Assert.IsNotNull(node);
        Assert.AreEqual("{\"inner\":[1,2,3]}", node.ToJsonString());
    }

    // ── FromJsonNode (JsonNode → currency) ────────────────────────────

    [TestMethod]
    public void FromJsonNode_Null_ReturnsNull()
    {
        Assert.IsNull(JsonCurrency.FromJsonNode(null));
    }

    [TestMethod]
    public void FromJsonNode_JsonBool_ReturnsBool()
    {
        Assert.IsTrue((bool?)JsonCurrency.FromJsonNode(JsonValue.Create(true)));
        Assert.IsFalse((bool?)JsonCurrency.FromJsonNode(JsonValue.Create(false)));
    }

    [TestMethod]
    public void FromJsonNode_JsonInteger_ReturnsLong()
    {
        object? result = JsonCurrency.FromJsonNode(JsonValue.Create(42));
        Assert.AreEqual(42L, result);
        Assert.IsInstanceOfType<long>(result);
    }

    [TestMethod]
    public void FromJsonNode_JsonString_ReturnsString()
    {
        Assert.AreEqual("hello", JsonCurrency.FromJsonNode(JsonValue.Create("hello")));
    }

    [TestMethod]
    public void FromJsonNode_JsonArray_ReturnsListOfCurrency()
    {
        JsonArray arr = new("a", 1, true);
        IReadOnlyList<object?>? result = JsonCurrency.FromJsonNode(arr) as IReadOnlyList<object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("a", result[0]);
        Assert.AreEqual(1L, result[1]); // widened to long
        Assert.IsTrue((bool?)result[2]);
    }

    [TestMethod]
    public void FromJsonNode_JsonObject_ReturnsDictOfCurrency()
    {
        JsonObject obj = new() { ["k"] = "v", ["n"] = 7 };
        IReadOnlyDictionary<string, object?>? result = JsonCurrency.FromJsonNode(obj) as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual("v", result["k"]);
        Assert.AreEqual(7L, result["n"]);
    }

    // ── Round-trip ────────────────────────────────────────────────────

    [TestMethod]
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
        Assert.IsNotNull(asJson);
        IReadOnlyDictionary<string, object?>? roundTripped = JsonCurrency.FromJsonNode(asJson) as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual("x", roundTripped["s"]);
        Assert.AreEqual(42L, roundTripped["n"]);
        Assert.IsTrue((bool?)roundTripped["b"]);
        IReadOnlyList<object?>? arr = roundTripped["arr"] as IReadOnlyList<object?>;
        Assert.IsNotNull(arr);
        Assert.AreEqual(3, arr.Count);
    }
}