using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Sdk.Internal;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests;

/// <summary>
/// coverage for the SDK's trim-safe JSON conversion
/// helper (<see cref="JsonConversion"/>).
/// </summary>
public sealed class JsonConversionTests
{
    // ── ConvertToJsonNode ──────────────────────────────────────────────────

    [Fact]
    public void ConvertToJsonNode_Null_ReturnsNull()
    {
        Assert.Null(JsonConversion.ConvertToJsonNode<string?>(null));
    }

    [Fact]
    public void ConvertToJsonNode_JsonNode_DeepClones()
    {
        JsonObject original = new() { ["k"] = "v" };
        JsonNode? converted = JsonConversion.ConvertToJsonNode<JsonNode>(original);
        Assert.NotNull(converted);
        MessageAssert.NotSame(original, converted, "Pre-built JsonNodes must be deep-cloned to avoid alias mutation.");
        Assert.Equal("v", converted!["k"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("hello")]
    public void ConvertToJsonNode_String_RoundTrips(string value)
    {
        JsonNode? node = JsonConversion.ConvertToJsonNode(value);
        Assert.NotNull(node);
        Assert.Equal(value, node!.GetValue<string>());
    }

    [Fact]
    public void ConvertToJsonNode_Bool_RoundTrips()
    {
        JsonNode? node = JsonConversion.ConvertToJsonNode(true);
        Assert.True(node!.GetValue<bool>());
    }

    [Fact]
    public void ConvertToJsonNode_AllNumericPrimitives_RoundTrip()
    {
        // Lock the contract: each numeric primitive supported by SetValue<T>
        // must produce a non-null JsonNode whose .GetValue<T>() returns the
        // original value.
        Assert.Equal(42, JsonConversion.ConvertToJsonNode(42)!.GetValue<int>());
        Assert.Equal(123L, JsonConversion.ConvertToJsonNode(123L)!.GetValue<long>());
        Assert.Equal(3.14, JsonConversion.ConvertToJsonNode(3.14)!.GetValue<double>());
        Assert.Equal(2.5f, JsonConversion.ConvertToJsonNode(2.5f)!.GetValue<float>());
        Assert.Equal(7.5m, JsonConversion.ConvertToJsonNode(7.5m)!.GetValue<decimal>());
    }

    [Fact]
    public void ConvertToJsonNode_UnsupportedType_Throws()
    {
        // The safety net — we explicitly reject types that would otherwise
        // require reflection-based serialisation, which breaks under
        // PublishTrimmed=true. Locking the message shape so callers can
        // surface a useful hint via the exception text.
        NotSupportedException ex = Assert.Throws<NotSupportedException>(() =>
            JsonConversion.ConvertToJsonNode(new DateTime(2026, 4, 29)));
        OrdinalAssert.Contains("JSON primitive", ex.Message);
        OrdinalAssert.Contains("JsonNode", ex.Message);
    }

    // ── ConvertFromJsonNode ────────────────────────────────────────────────

    [Fact]
    public void ConvertFromJsonNode_Null_ReturnsDefault()
    {
        Assert.Null(JsonConversion.ConvertFromJsonNode<string>(null));
        Assert.Equal(0, JsonConversion.ConvertFromJsonNode<int>(null));
        Assert.Null(JsonConversion.ConvertFromJsonNode<int?>(null));
    }

    [Fact]
    public void ConvertFromJsonNode_JsonNodePassthrough_ReturnsSameNode()
    {
        JsonNode node = new JsonObject { ["k"] = "v" };
        JsonNode? got = JsonConversion.ConvertFromJsonNode<JsonNode>(node);
        MessageAssert.Same(node, got, "JsonNode passthrough must return the same instance (no copy).");
    }

    [Fact]
    public void ConvertFromJsonNode_JsonObjectPassthrough_TypedReturn()
    {
        JsonNode node = new JsonObject { ["k"] = "v" };
        JsonObject? got = JsonConversion.ConvertFromJsonNode<JsonObject>(node);
        Assert.NotNull(got);
        Assert.Same(node, got);
    }

    [Fact]
    public void ConvertFromJsonNode_JsonArrayPassthrough_TypedReturn()
    {
        JsonNode node = new JsonArray { 1, 2, 3 };
        JsonArray? got = JsonConversion.ConvertFromJsonNode<JsonArray>(node);
        Assert.NotNull(got);
        Assert.Equal(3, got!.Count);
    }

    [Fact]
    public void ConvertFromJsonNode_WrongJsonShape_ReturnsDefault()
    {
        // Asking for a JsonObject when the node is a JsonValue → default.
        JsonNode value = JsonValue.Create("a-string")!;
        Assert.Null(JsonConversion.ConvertFromJsonNode<JsonObject>(value));
        Assert.Null(JsonConversion.ConvertFromJsonNode<JsonArray>(value));
    }

    [Fact]
    public void ConvertFromJsonNode_AllPrimitives_RoundTrip()
    {
        Assert.Equal("hi", JsonConversion.ConvertFromJsonNode<string>(JsonValue.Create("hi")));
        Assert.True(JsonConversion.ConvertFromJsonNode<bool>(JsonValue.Create(true)));
        Assert.Equal(42, JsonConversion.ConvertFromJsonNode<int>(JsonValue.Create(42)));
        Assert.Equal(123L, JsonConversion.ConvertFromJsonNode<long>(JsonValue.Create(123L)));
        Assert.Equal(3.14, JsonConversion.ConvertFromJsonNode<double>(JsonValue.Create(3.14)));
        Assert.Equal(2.5f, JsonConversion.ConvertFromJsonNode<float>(JsonValue.Create(2.5f)));
        Assert.Equal(7.5m, JsonConversion.ConvertFromJsonNode<decimal>(JsonValue.Create(7.5m)));
    }

    [Fact]
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

        Assert.True(nb);
        Assert.Equal(7, ni);
        Assert.Equal(99L, nl);
        Assert.Equal(1.5, nd);
        Assert.Equal(0.25f, nf);
        Assert.Equal(1.0m, nm);
    }

    [Fact]
    public void ConvertFromJsonNode_TypeNotInTable_ReturnsDefault()
    {
        // System.Guid is not supported — must fall through to default(T)
        // rather than throwing. This is the asymmetric design choice:
        // ConvertTo throws (eager rejection on the write side), Convert
        // From silently returns default (defensive on the read side, where
        // a returned null lets the caller fall back gracefully).
        JsonNode v = JsonValue.Create("not-a-guid")!;
        Assert.Equal(Guid.Empty, JsonConversion.ConvertFromJsonNode<Guid>(v));
    }
}