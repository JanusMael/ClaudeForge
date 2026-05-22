using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// coverage for the SDK's trim-safe JSON conversion
/// helper (<see cref="JsonConversion"/>).
/// </summary>
[TestClass]
public sealed class JsonConversionTests
{
    // ── ConvertToJsonNode ──────────────────────────────────────────────────

    [TestMethod]
    public void ConvertToJsonNode_Null_ReturnsNull()
    {
        Assert.IsNull(JsonConversion.ConvertToJsonNode<string?>(null));
    }

    [TestMethod]
    public void ConvertToJsonNode_JsonNode_DeepClones()
    {
        JsonObject original = new() { ["k"] = "v" };
        JsonNode? converted = JsonConversion.ConvertToJsonNode<JsonNode>(original);
        Assert.IsNotNull(converted);
        Assert.AreNotSame(original, converted, "Pre-built JsonNodes must be deep-cloned to avoid alias mutation.");
        Assert.AreEqual("v", converted!["k"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("hello")]
    public void ConvertToJsonNode_String_RoundTrips(string value)
    {
        JsonNode? node = JsonConversion.ConvertToJsonNode(value);
        Assert.IsNotNull(node);
        Assert.AreEqual(value, node!.GetValue<string>());
    }

    [TestMethod]
    public void ConvertToJsonNode_Bool_RoundTrips()
    {
        JsonNode? node = JsonConversion.ConvertToJsonNode(true);
        Assert.IsTrue(node!.GetValue<bool>());
    }

    [TestMethod]
    public void ConvertToJsonNode_AllNumericPrimitives_RoundTrip()
    {
        // Lock the contract: each numeric primitive supported by SetValue<T>
        // must produce a non-null JsonNode whose .GetValue<T>() returns the
        // original value.
        Assert.AreEqual(42, JsonConversion.ConvertToJsonNode(42)!.GetValue<int>());
        Assert.AreEqual(123L, JsonConversion.ConvertToJsonNode(123L)!.GetValue<long>());
        Assert.AreEqual(3.14, JsonConversion.ConvertToJsonNode(3.14)!.GetValue<double>());
        Assert.AreEqual(2.5f, JsonConversion.ConvertToJsonNode(2.5f)!.GetValue<float>());
        Assert.AreEqual(7.5m, JsonConversion.ConvertToJsonNode(7.5m)!.GetValue<decimal>());
    }

    [TestMethod]
    public void ConvertToJsonNode_UnsupportedType_Throws()
    {
        // The safety net — we explicitly reject types that would otherwise
        // require reflection-based serialisation, which breaks under
        // PublishTrimmed=true. Locking the message shape so callers can
        // surface a useful hint via the exception text.
        NotSupportedException ex = Assert.ThrowsException<NotSupportedException>(() =>
            JsonConversion.ConvertToJsonNode(new DateTime(2026, 4, 29)));
        StringAssert.Contains(ex.Message, "JSON primitive");
        StringAssert.Contains(ex.Message, "JsonNode");
    }

    // ── ConvertFromJsonNode ────────────────────────────────────────────────

    [TestMethod]
    public void ConvertFromJsonNode_Null_ReturnsDefault()
    {
        Assert.IsNull(JsonConversion.ConvertFromJsonNode<string>(null));
        Assert.AreEqual(0, JsonConversion.ConvertFromJsonNode<int>(null));
        Assert.IsNull(JsonConversion.ConvertFromJsonNode<int?>(null));
    }

    [TestMethod]
    public void ConvertFromJsonNode_JsonNodePassthrough_ReturnsSameNode()
    {
        JsonNode node = new JsonObject { ["k"] = "v" };
        JsonNode? got = JsonConversion.ConvertFromJsonNode<JsonNode>(node);
        Assert.AreSame(node, got, "JsonNode passthrough must return the same instance (no copy).");
    }

    [TestMethod]
    public void ConvertFromJsonNode_JsonObjectPassthrough_TypedReturn()
    {
        JsonNode node = new JsonObject { ["k"] = "v" };
        JsonObject? got = JsonConversion.ConvertFromJsonNode<JsonObject>(node);
        Assert.IsNotNull(got);
        Assert.AreSame(node, got);
    }

    [TestMethod]
    public void ConvertFromJsonNode_JsonArrayPassthrough_TypedReturn()
    {
        JsonNode node = new JsonArray { 1, 2, 3 };
        JsonArray? got = JsonConversion.ConvertFromJsonNode<JsonArray>(node);
        Assert.IsNotNull(got);
        Assert.AreEqual(3, got!.Count);
    }

    [TestMethod]
    public void ConvertFromJsonNode_WrongJsonShape_ReturnsDefault()
    {
        // Asking for a JsonObject when the node is a JsonValue → default.
        JsonNode value = JsonValue.Create("a-string")!;
        Assert.IsNull(JsonConversion.ConvertFromJsonNode<JsonObject>(value));
        Assert.IsNull(JsonConversion.ConvertFromJsonNode<JsonArray>(value));
    }

    [TestMethod]
    public void ConvertFromJsonNode_AllPrimitives_RoundTrip()
    {
        Assert.AreEqual("hi", JsonConversion.ConvertFromJsonNode<string>(JsonValue.Create("hi")));
        Assert.IsTrue(JsonConversion.ConvertFromJsonNode<bool>(JsonValue.Create(true)));
        Assert.AreEqual(42, JsonConversion.ConvertFromJsonNode<int>(JsonValue.Create(42)));
        Assert.AreEqual(123L, JsonConversion.ConvertFromJsonNode<long>(JsonValue.Create(123L)));
        Assert.AreEqual(3.14, JsonConversion.ConvertFromJsonNode<double>(JsonValue.Create(3.14)));
        Assert.AreEqual(2.5f, JsonConversion.ConvertFromJsonNode<float>(JsonValue.Create(2.5f)));
        Assert.AreEqual(7.5m, JsonConversion.ConvertFromJsonNode<decimal>(JsonValue.Create(7.5m)));
    }

    [TestMethod]
    public void ConvertFromJsonNode_NullablePrimitives_RoundTrip()
    {
        // Lock the Nullable<T> overloads — these were the most likely
        // uncovered branches per the COVERAGE-B3 report. Each must
        // resolve via the same JsonValue.TryGetValue<T> path as the
        // non-nullable variants but box once for the (T)(object) cast.
        bool? nb = JsonConversion.ConvertFromJsonNode<bool?>(JsonValue.Create(true));
        int? ni = JsonConversion.ConvertFromJsonNode<int?>(JsonValue.Create(7));
        long? nl = JsonConversion.ConvertFromJsonNode<long?>(JsonValue.Create(99L));
        double? nd = JsonConversion.ConvertFromJsonNode<double?>(JsonValue.Create(1.5));
        float? nf = JsonConversion.ConvertFromJsonNode<float?>(JsonValue.Create(0.25f));
        decimal? nm = JsonConversion.ConvertFromJsonNode<decimal?>(JsonValue.Create(1.0m));

        Assert.IsTrue(nb);
        Assert.AreEqual(7, ni);
        Assert.AreEqual(99L, nl);
        Assert.AreEqual(1.5, nd);
        Assert.AreEqual(0.25f, nf);
        Assert.AreEqual(1.0m, nm);
    }

    [TestMethod]
    public void ConvertFromJsonNode_TypeNotInTable_ReturnsDefault()
    {
        // System.Guid is not supported — must fall through to default(T)
        // rather than throwing. This is the asymmetric design choice:
        // ConvertTo throws (eager rejection on the write side), Convert
        // From silently returns default (defensive on the read side, where
        // a returned null lets the caller fall back gracefully).
        JsonNode v = JsonValue.Create("not-a-guid")!;
        Assert.AreEqual(Guid.Empty, JsonConversion.ConvertFromJsonNode<Guid>(v));
    }
}