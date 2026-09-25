using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.JsonC.Tests;

/// <summary>
/// The refusal contract, and the reason it is the most important thing in this library.
/// </summary>
/// <remarks>
/// The writer this replaces did the opposite of refusing:
/// <c>ConfigFileLoader.LoadAsync</c> caught the parse exception, substituted an
/// <b>empty</b> <c>JsonObject</c>, and the next save then serialized that empty document
/// over the user's file. A single stray character in a config file was therefore enough
/// to lose it. These tests exist so that path cannot come back.
/// </remarks>
public sealed class JsoncEditorSafetyTests
{
    private static readonly string[] Unparseable =
    [
        "{ \"a\": 1",              // unterminated object
        "{ \"a\" }",               // missing colon and value
        "{ \"a\": }",              // missing value
        "{ \"a\": 01 }",           // leading zero
        "{ \"a\": nul }",          // bad literal
        "{ \"a\": 1 } trailing",   // content after the root
        "/* unterminated",         // unterminated block comment
        "{ \"unterminated: 1 }",   // unterminated key string
        "@",                       // stray character
    ];

    [Fact]
    public void UnparseableDocuments_AreNotEditable()
    {
        foreach (string text in Unparseable)
        {
            JsoncDocument document = JsoncDocument.Parse(text);
            Assert.False(
                document.IsEditable,
                $"{JsoncScannerTests.Describe(text)} parsed without complaint. Editing a "
                + "document we misread is how config gets corrupted.");
            Assert.True(
                document.Errors.Count > 0,
                $"{JsoncScannerTests.Describe(text)} is not editable but reported no reason why; "
                + "the caller needs something to log.");
        }
    }

    [Fact]
    public void SetValue_OnAnUnparseableDocument_Throws_RatherThanCorrupting()
    {
        foreach (string text in Unparseable)
        {
            InvalidOperationException ex = MessageAssert.Throws<InvalidOperationException>(
                () => JsoncEditor.SetValue(text, "a", JsonValue.Create(1)),
                $"Expected a refusal for {JsoncScannerTests.Describe(text)}.");

            MessageAssert.Contains(
                "Refusing to edit", ex.Message,
                "The message should say plainly that the edit was refused, so a caller "
                + "logging it can tell this from an incidental failure.");
        }
    }

    [Fact]
    public void Remove_OnAnUnparseableDocument_Throws()
    {
        foreach (string text in Unparseable)
        {
            MessageAssert.Throws<InvalidOperationException>(
                () => JsoncEditor.Remove(text, "a"),
                $"Expected a refusal for {JsoncScannerTests.Describe(text)}.");
        }
    }

    [Fact]
    public void EmptyAndCommentOnlyDocuments_AreEditable_BecauseTheyAreUnderstood()
    {
        // The distinction that matters: "we could not parse this" is not the same as
        // "there was nothing to parse". Refusing the latter would make it impossible to
        // write a config file that does not exist yet.
        foreach (string text in new[] { "", "   ", "\n\n", "// note", "/* note */" })
        {
            JsoncDocument document = JsoncDocument.Parse(text);
            Assert.True(
                document.IsEditable,
                $"{JsoncScannerTests.Describe(text)} should be editable. "
                + $"Errors: {string.Join("; ", document.Errors)}");
            MessageAssert.Null(document.Root, "There is no value in this document to find.");
        }
    }

    [Fact]
    public void WellFormedCorpusEntries_AreAllEditable()
    {
        string[] wellFormed =
        [
            "{}",
            "{ \"a\": 1 }",
            "{\n  \"a\": 1\n}",
            "{\r\n  \"a\": 1\r\n}",
            "{ \"a\": 1, }",
            "{ \"a\": { \"b\": { \"c\": [1, 2] } } }",
            "{ // c\n  \"a\": 1\n}",
            "[1, 2, 3]",
            "\"just a string\"",
            "42",
            "null",
        ];

        foreach (string text in wellFormed)
        {
            JsoncDocument document = JsoncDocument.Parse(text);
            Assert.True(
                document.IsEditable,
                $"{JsoncScannerTests.Describe(text)} should parse cleanly. "
                + $"Errors: {string.Join("; ", document.Errors)}");
        }
    }

    [Fact]
    public void SetValue_OnANonObjectRoot_Throws_RatherThanGuessing()
    {
        // "[1,2]" is valid JSONC but has nowhere to put a named member. Silently
        // replacing the array would destroy data; the caller must decide.
        Assert.Throws<InvalidOperationException>(
            () => JsoncEditor.SetValue("[1, 2]", "a", JsonValue.Create(1)));
    }
}
