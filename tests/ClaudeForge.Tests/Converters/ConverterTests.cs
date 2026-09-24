using System.Globalization;
using Avalonia.Controls;
using Bennewitz.Ninja.ClaudeForge.Converters;
using Bennewitz.Ninja.ScopedEditors.Abstractions;
using Bennewitz.Ninja.ScopedEditors.AvaloniaUI.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Converters;

// ─────────────────────────────────────────────────────────────────────────────
// BytesToHumanReadableConverter
// ─────────────────────────────────────────────────────────────────────────────

public class BytesToHumanReadableConverterTests
{
    private static string Format(long bytes)
    {
        return BytesToHumanReadableConverter.Format(bytes);
    }

    private static object Convert(object? value)
    {
        return new BytesToHumanReadableConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    // ── BytesToHumanReadableConverter.Format ──────────────────────────────────

    [Fact]
    public void Format_Zero_ReturnsZeroBytes()
    {
        Assert.Equal("0 B", Format(0));
    }

    [Fact]
    public void Format_512_ReturnsBytesString()
    {
        Assert.Equal("512 B", Format(512));
    }

    [Fact]
    public void Format_1023_ReturnsBytesString()
    {
        Assert.Equal("1023 B", Format(1023));
    }

    [Fact]
    public void Format_1024_ReturnsOneKB()
    {
        Assert.Equal("1.0 KB", Format(1024));
    }

    [Fact]
    public void Format_1536_Returns1Point5KB()
    {
        Assert.Equal("1.5 KB", Format(1536));
    }

    [Fact]
    public void Format_1MB_Returns1MB()
    {
        long oneMb = 1024L * 1024;
        Assert.Equal("1.0 MB", Format(oneMb));
    }

    [Fact]
    public void Format_1GB_Returns1GB()
    {
        long oneGb = 1024L * 1024 * 1024;
        Assert.Equal("1.00 GB", Format(oneGb));
    }

    // ── IValueConverter.Convert ───────────────────────────────────────────────

    [Fact]
    public void Convert_NullInput_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Convert(null));
    }

    [Fact]
    public void Convert_NegativeLong_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Convert(-1L));
    }

    [Fact]
    public void Convert_LongValue_CallsFormat()
    {
        // A long value of 2048 should produce "2.0 KB" just like Format(2048) does.
        Assert.Equal(Format(2048), Convert(2048L));
    }

    [Fact]
    public void Convert_IntValue_IsAccepted()
    {
        Assert.Equal(Format(512), Convert(512));
    }

    [Fact]
    public void Convert_DoubleValue_IsAccepted()
    {
        Assert.Equal(Format(1024), Convert(1024.0));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ScopeToTooltipConverter
// ─────────────────────────────────────────────────────────────────────────────

public class ScopeToTooltipConverterTests
{
    private static object? Convert(object? value)
    {
        return new ScopeToTooltipConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    // Simple IEditorScope stub used to exercise the IEditorScope branch.
    private sealed class StubScope(string id) : IEditorScope
    {
        public int Priority => 0;
        public string Id => id;
        public string DisplayName => id;
        public bool IsReadOnly => false;
    }

    [Fact]
    public void Convert_ManagedScope_ContainsManagedText()
    {
        string? result = Convert(new StubScope("managed")) as string;
        Assert.NotNull(result);
        OrdinalAssert.Contains("managed", result);
    }

    [Fact]
    public void Convert_UserScope_ContainsUserText()
    {
        string? result = Convert(new StubScope("user")) as string;
        Assert.NotNull(result);
        OrdinalAssert.Contains("user", result);
    }

    [Fact]
    public void Convert_ProjectScope_ContainsProjectText()
    {
        string? result = Convert(new StubScope("project")) as string;
        Assert.NotNull(result);
        OrdinalAssert.Contains("project", result);
    }

    [Fact]
    public void Convert_LocalScope_ContainsLocalText()
    {
        string? result = Convert(new StubScope("local")) as string;
        Assert.NotNull(result);
        OrdinalAssert.Contains("local", result);
    }

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        Assert.Null(Convert(null));
    }

    [Fact]
    public void Convert_ConfigScopeEnum_Works()
    {
        // ConfigScope.User should produce the same non-empty tooltip as the
        // IEditorScope("user") path does.
        string? viaEnum = Convert(ConfigScope.User) as string;
        string? viaStub = Convert(new StubScope("user")) as string;
        Assert.NotNull(viaEnum);
        Assert.Equal(viaStub, viaEnum);
    }

    [Fact]
    public void Convert_AllConfigScopeValues_ReturnNonNullStrings()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            string? result = Convert(scope) as string;
            Assert.False(string.IsNullOrWhiteSpace(result),
                $"ConfigScope.{scope} must produce a non-empty tooltip.");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// LongValueTooltipConverter
// ─────────────────────────────────────────────────────────────────────────────

public class LongValueTooltipConverterTests
{
    private static object? Convert(object? value)
    {
        return new LongValueTooltipConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Convert_NullInput_ReturnsNull()
    {
        Assert.Null(Convert(null));
    }

    [Fact]
    public void Convert_ShortString_ReturnsNull()
    {
        // 50 chars or fewer, no newline — no tooltip needed.
        string shortString = new('a', 50);
        Assert.Null(Convert(shortString));
    }

    [Fact]
    public void Convert_LongString_ReturnsString()
    {
        // More than 50 chars, no newlines, not valid JSON — returned as-is.
        string longString = new('a', 51);
        Assert.Equal(longString, Convert(longString));
    }

    [Fact]
    public void Convert_StringWithNewline_ReturnsString()
    {
        // Short but contains a newline — must produce a tooltip.
        const string withNewline = "short\nvalue";
        Assert.Equal(withNewline, Convert(withNewline));
    }

    /// <summary>
    /// valid JSON content is now returned as a monospace
    /// <see cref="Avalonia.Controls.TextBlock"/> rather than a plain
    /// string, so the tooltip popup renders indented JSON in a font where
    /// brackets / keys align.  Helper to read the body text uniformly
    /// across both return shapes for the test assertions.
    /// </summary>
    private static string? GetTooltipText(object? converted)
    {
        return converted switch
        {
            string s => s,
            TextBlock tb => tb.Text,
            null => null,
            var _ => converted.ToString(),
        };
    }

    [Fact]
    public void Convert_ValidJsonObject_ReturnsPrettyPrintedMonospace()
    {
        // The string must exceed the 50-char threshold so the converter does not
        // return null early. Using a realistic JSON object with several properties.
        const string compact = "{\"firstName\":\"Alice\",\"lastName\":\"Smith\",\"age\":30,\"x\":1}";
        object? converted = Convert(compact);
        MessageAssert.IsAssignableFrom<TextBlock>(converted,
            "Valid JSON should produce a monospace-styled TextBlock so the popup renders code-like.");
        TextBlock tb = (TextBlock)converted!;
        // FontFamily.Name returns the first family in the fallback chain
        // (Avalonia exposes only the primary family on .Name; the rest of
        // the chain lives in FamilyNames).  Asserting the primary is
        // "Consolas" matches our requested
        // "Consolas,Menlo,monospace" without depending on internal
        // representation of the fallback list.
        MessageAssert.Equal("Consolas", tb.FontFamily.Name,
            "Primary font family should be Consolas (with Menlo / monospace as fallbacks).");
        string result = tb.Text!;
        OrdinalAssert.Contains("\n", result);
        OrdinalAssert.Contains("\"firstName\"", result);
    }

    [Fact]
    public void Convert_ValidJsonArray_ReturnsPrettyPrintedMonospace()
    {
        const string compact = "[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20]";
        string? result = GetTooltipText(Convert(compact));
        Assert.NotNull(result);
        OrdinalAssert.Contains("\n", result);
    }

    [Fact]
    public void Convert_StringThatLooksLikeJsonButIsnt_ReturnsRaw()
    {
        // Starts with '{' and ends with '}' but is not valid JSON.
        const string invalid = "{this is not json at all, really long string padding padding padding}";
        // Invalid JSON falls through to the plain-string path — no
        // monospace wrap because the content isn't structured.
        string? result = Convert(invalid) as string;
        Assert.Equal(invalid, result);
    }

    // ── Cap behaviour: bounds the rendered tooltip so Avalonia's positioner
    //    doesn't ping-pong above/below the cursor while trying to fit a
    //    too-tall popup on screen (observed as a flicker by the user). ──

    [Fact]
    public void Cap_LineCountAtThreshold_NotTruncated()
    {
        // 30 lines is the cap; exactly 30 must be returned untouched.
        string input = string.Join('\n',
            Enumerable.Range(1, 30).Select(i => $"line {i}"));
        string? result = Convert(input) as string;
        Assert.Equal(input, result);
        Assert.False(result!.Contains("truncated"),
            "30-line input must not be flagged as truncated.");
    }

    [Fact]
    public void Cap_LineCountAboveThreshold_IsTruncatedWithFooter()
    {
        // 100 lines → cap at 30 + truncation footer that points the user
        // at the right-click → Copy escape hatch.
        string input = string.Join('\n',
            Enumerable.Range(1, 100).Select(i => $"line {i}"));
        string? result = Convert(input) as string;
        Assert.NotNull(result);
        OrdinalAssert.Contains("line 1", result);
        OrdinalAssert.Contains("line 30", result);
        Assert.False(result.Contains("line 31"),
            "Lines beyond the cap must be omitted.");
        OrdinalAssert.Contains("truncated", result);
        OrdinalAssert.Contains("right-click", result);
    }

    [Fact]
    public void Cap_CharCountAboveThreshold_IsTruncatedWithFooter()
    {
        // One huge line — char cap kicks in even though line count is 1.
        string input = new('a', 5_000);
        string? result = Convert(input) as string;
        Assert.NotNull(result);
        Assert.True(result.Length < 5_000 + 200,
            $"Result must be capped well below the input length; got {result.Length}.");
        OrdinalAssert.Contains("truncated", result);
    }

    [Fact]
    public void Cap_LargeJson_IsTruncatedAfterPrettyPrint()
    {
        // Build a JSON array with enough elements that the pretty-printed
        // form exceeds the 30-line cap.
        string arr = string.Join(",",
            Enumerable.Range(1, 100).Select(i => $"{i}"));
        string input = $"[{arr}]";
        string? result = GetTooltipText(Convert(input));
        Assert.NotNull(result);
        OrdinalAssert.Contains("truncated", result);
        OrdinalAssert.Contains("right-click", result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EnvVarTooltipConverter
// ─────────────────────────────────────────────────────────────────────────────

public class EnvVarTooltipConverterTests
{
    private static object? Convert(object? value)
    {
        return new EnvVarTooltipConverter()
            .Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Convert_KnownKey_ReturnsDescription()
    {
        string? result = Convert("ANTHROPIC_API_KEY") as string;
        Assert.False(string.IsNullOrWhiteSpace(result),
            "ANTHROPIC_API_KEY must map to a non-empty description.");
    }

    [Fact]
    public void Convert_CaseInsensitive()
    {
        string? upper = Convert("ANTHROPIC_API_KEY") as string;
        string? lower = Convert("anthropic_api_key") as string;
        Assert.NotNull(upper);
        MessageAssert.Equal(upper, lower,
            "Lookup must be case-insensitive.");
    }

    [Fact]
    public void Convert_UnknownKey_ReturnsFallbackName()
    {
        // Unknown variables fall back to the name itself so the Name column
        // always shows something useful on hover.
        Assert.Equal("COMPLETELY_UNKNOWN_VAR_XYZ_12345", Convert("COMPLETELY_UNKNOWN_VAR_XYZ_12345"));
    }

    [Fact]
    public void Convert_NullInput_ReturnsNull()
    {
        Assert.Null(Convert(null));
    }

    [Fact]
    public void Convert_NonStringInput_ReturnsNull()
    {
        Assert.Null(Convert(42));
    }
}