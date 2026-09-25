using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Diagnostics;

/// <summary>
/// Tests for <see cref="JsonFormatting"/> — the SDK-side pretty-printer
/// + cap helpers that back the App's tooltip converter and any future
/// MCP / CLI consumer that wants the same shape-and-bound treatment for
/// long JSON blobs.  Migrated from the App-side
/// <c>LongValueTooltipConverterTests</c> when the helpers moved to the SDK.
/// </summary>
public sealed class JsonFormattingTests
{
    // ── LooksLikeJson ─────────────────────────────────────────────────

    [Theory]
    [InlineData("{\"a\":1}", true)]
    [InlineData("[1,2,3]", true)]
    [InlineData("  { \"x\": 1 }  ", true)] // surrounding whitespace is trimmed
    [InlineData("", false)]
    [InlineData("hello", false)]
    [InlineData("{\"a\":1", false)] // unmatched braces
    [InlineData("[1,2,3", false)]
    public void LooksLikeJson_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, JsonFormatting.LooksLikeJson(input));
    }

    [Fact]
    public void LooksLikeJson_NullInput_ReturnsFalse()
    {
        Assert.False(JsonFormatting.LooksLikeJson(null));
    }

    // ── TryPrettyPrint ────────────────────────────────────────────────

    [Fact]
    public void TryPrettyPrint_ValidObject_AddsIndentation()
    {
        string? pretty = JsonFormatting.TryPrettyPrint("{\"firstName\":\"Alice\",\"age\":30}");
        Assert.NotNull(pretty);
        OrdinalAssert.Contains("\n", pretty);
        OrdinalAssert.Contains("\"firstName\"", pretty);
    }

    [Fact]
    public void TryPrettyPrint_ValidArray_AddsIndentation()
    {
        string? pretty = JsonFormatting.TryPrettyPrint("[1,2,3,4,5]");
        Assert.NotNull(pretty);
        OrdinalAssert.Contains("\n", pretty);
    }

    [Fact]
    public void TryPrettyPrint_InvalidJson_ReturnsNull()
    {
        Assert.Null(JsonFormatting.TryPrettyPrint("{ this is not valid json }"));
    }

    [Fact]
    public void TryPrettyPrint_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(JsonFormatting.TryPrettyPrint(null));
        Assert.Null(JsonFormatting.TryPrettyPrint(string.Empty));
    }

    // ── Cap ───────────────────────────────────────────────────────────

    [Fact]
    public void Cap_WithinLimits_ReturnsInputUnchanged()
    {
        string input = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}"));
        Assert.Equal(input, JsonFormatting.Cap(input));
    }

    [Fact]
    public void Cap_ExceedsLineLimit_AppendsTruncationFooter()
    {
        string input = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}"));
        string result = JsonFormatting.Cap(input);
        OrdinalAssert.Contains("line 1", result);
        OrdinalAssert.Contains("line 30", result);
        Assert.False(result.Contains("line 31"),
            "Lines beyond the cap must be omitted.");
        OrdinalAssert.Contains(JsonFormatting.TruncationFooter, result);
    }

    [Fact]
    public void Cap_ExceedsCharLimit_AppendsTruncationFooter()
    {
        string input = new('a', 5_000);
        string result = JsonFormatting.Cap(input);
        Assert.True(result.Length < 5_000 + 200,
            $"Result must be capped well below the input length; got {result.Length}.");
        OrdinalAssert.Contains(JsonFormatting.TruncationFooter, result);
    }

    [Fact]
    public void Cap_CustomLimits_AreRespected()
    {
        string input = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line {i}"));
        string result = JsonFormatting.Cap(input, maxLines: 5, maxChars: 10_000);
        OrdinalAssert.Contains("line 5", result);
        OrdinalAssert.DoesNotContain("line 6", result);
        OrdinalAssert.Contains(JsonFormatting.TruncationFooter, result);
    }

    [Fact]
    public void TruncationFooter_PointsAtCopyEscapeHatch()
    {
        // Locks the canonical wording so a future reword doesn't silently
        // break consumer messages that reference the same hatch ("right-click → Copy").
        OrdinalAssert.Contains("truncated", JsonFormatting.TruncationFooter);
        OrdinalAssert.Contains("right-click", JsonFormatting.TruncationFooter);
    }
}