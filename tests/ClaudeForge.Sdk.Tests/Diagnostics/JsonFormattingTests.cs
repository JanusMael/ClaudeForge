using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Diagnostics;

/// <summary>
/// Tests for <see cref="JsonFormatting"/> — the SDK-side pretty-printer
/// + cap helpers that back the App's tooltip converter and any future
/// MCP / CLI consumer that wants the same shape-and-bound treatment for
/// long JSON blobs.  Migrated from the App-side
/// <c>LongValueTooltipConverterTests</c> when the helpers moved to the SDK.
/// </summary>
[TestClass]
public sealed class JsonFormattingTests
{
    // ── LooksLikeJson ─────────────────────────────────────────────────

    [TestMethod]
    [DataRow("{\"a\":1}", true)]
    [DataRow("[1,2,3]", true)]
    [DataRow("  { \"x\": 1 }  ", true)] // surrounding whitespace is trimmed
    [DataRow("", false)]
    [DataRow("hello", false)]
    [DataRow("{\"a\":1", false)] // unmatched braces
    [DataRow("[1,2,3", false)]
    public void LooksLikeJson_ReturnsExpected(string input, bool expected)
    {
        Assert.AreEqual(expected, JsonFormatting.LooksLikeJson(input));
    }

    [TestMethod]
    public void LooksLikeJson_NullInput_ReturnsFalse()
    {
        Assert.IsFalse(JsonFormatting.LooksLikeJson(null));
    }

    // ── TryPrettyPrint ────────────────────────────────────────────────

    [TestMethod]
    public void TryPrettyPrint_ValidObject_AddsIndentation()
    {
        string? pretty = JsonFormatting.TryPrettyPrint("{\"firstName\":\"Alice\",\"age\":30}");
        Assert.IsNotNull(pretty);
        StringAssert.Contains(pretty, "\n");
        StringAssert.Contains(pretty, "\"firstName\"");
    }

    [TestMethod]
    public void TryPrettyPrint_ValidArray_AddsIndentation()
    {
        string? pretty = JsonFormatting.TryPrettyPrint("[1,2,3,4,5]");
        Assert.IsNotNull(pretty);
        StringAssert.Contains(pretty, "\n");
    }

    [TestMethod]
    public void TryPrettyPrint_InvalidJson_ReturnsNull()
    {
        Assert.IsNull(JsonFormatting.TryPrettyPrint("{ this is not valid json }"));
    }

    [TestMethod]
    public void TryPrettyPrint_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(JsonFormatting.TryPrettyPrint(null));
        Assert.IsNull(JsonFormatting.TryPrettyPrint(string.Empty));
    }

    // ── Cap ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Cap_WithinLimits_ReturnsInputUnchanged()
    {
        string input = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}"));
        Assert.AreEqual(input, JsonFormatting.Cap(input));
    }

    [TestMethod]
    public void Cap_ExceedsLineLimit_AppendsTruncationFooter()
    {
        string input = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line {i}"));
        string result = JsonFormatting.Cap(input);
        StringAssert.Contains(result, "line 1");
        StringAssert.Contains(result, "line 30");
        Assert.IsFalse(result.Contains("line 31"),
            "Lines beyond the cap must be omitted.");
        StringAssert.Contains(result, JsonFormatting.TruncationFooter);
    }

    [TestMethod]
    public void Cap_ExceedsCharLimit_AppendsTruncationFooter()
    {
        string input = new('a', 5_000);
        string result = JsonFormatting.Cap(input);
        Assert.IsTrue(result.Length < 5_000 + 200,
            $"Result must be capped well below the input length; got {result.Length}.");
        StringAssert.Contains(result, JsonFormatting.TruncationFooter);
    }

    [TestMethod]
    public void Cap_CustomLimits_AreRespected()
    {
        string input = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line {i}"));
        string result = JsonFormatting.Cap(input, maxLines: 5, maxChars: 10_000);
        StringAssert.Contains(result, "line 5");
        Assert.IsFalse(result.Contains("line 6"));
        StringAssert.Contains(result, JsonFormatting.TruncationFooter);
    }

    [TestMethod]
    public void TruncationFooter_PointsAtCopyEscapeHatch()
    {
        // Locks the canonical wording so a future reword doesn't silently
        // break consumer messages that reference the same hatch ("right-click → Copy").
        StringAssert.Contains(JsonFormatting.TruncationFooter, "truncated");
        StringAssert.Contains(JsonFormatting.TruncationFooter, "right-click");
    }
}