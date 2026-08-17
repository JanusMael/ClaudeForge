using System.Text;

namespace Bennewitz.Ninja.AgentForge.Jsonc.Tests;

/// <summary>
/// Scanner contracts. The load-bearing one is <see cref="Tokens_AreGaplessAndCoverTheWholeInput"/>:
/// every edit this library makes is a span replacement, so a scanner that loses or
/// double-counts a character would silently misplace edits.
/// </summary>
[TestClass]
public sealed class JsoncScannerTests
{
    /// <summary>
    /// A deliberately nasty corpus: comments in odd places, escapes, tabs, CRLF,
    /// trailing commas, unterminated constructs, stray characters.
    /// </summary>
    internal static readonly string[] Corpus =
    [
        "",
        "   ",
        "\n\r\n\t ",
        "{}",
        "{ }",
        "[]",
        "null",
        "true",
        "false",
        "0",
        "-0",
        "12",
        "-3.5",
        "1e10",
        "1E+10",
        "-2.5e-3",
        "\"\"",
        "\"plain\"",
        "\"esc \\\" quote\"",
        "\"esc \\\\ backslash\"",
        "\"unicode \\u00e9\"",
        "\"tab\\tinside\"",
        "// just a comment",
        "/* just a block */",
        "{ \"a\": 1 }",
        "{\n  \"a\": 1\n}",
        "{\r\n  \"a\": 1\r\n}",
        "{\n\t\"a\": 1\n}",
        "{ // trailing line comment\n  \"a\": 1\n}",
        "{\n  // leading comment\n  \"a\": 1\n}",
        "{\n  \"a\": 1, // after value\n  \"b\": 2\n}",
        "{\n  /* block */ \"a\": 1\n}",
        "{ \"a\": 1, }",
        "[ 1, 2, 3, ]",
        "{ \"a\": { \"b\": { \"c\": [1, 2] } } }",
        "{ \"a\": [ { \"b\": 1 } ] }",
        "{\n\n  \"a\": 1\n\n}",
        // malformed
        "{",
        "}",
        "{ \"a\" }",
        "{ \"a\": }",
        "{ \"a\": 1",
        "\"unterminated",
        "/* unterminated",
        "{ \"a\": 01 }",
        "{ \"a\": 1abc }",
        "{ \"a\": nul }",
        "@",
        "{ \"a\": 1 } trailing",
        "\"line\nbreak\"",
        "\"trailing backslash\\",
    ];

    [TestMethod]
    public void Tokens_AreGaplessAndCoverTheWholeInput()
    {
        foreach (string input in Corpus)
        {
            IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan(input);

            int cursor = 0;
            StringBuilder rebuilt = new();

            foreach (JsoncToken token in tokens)
            {
                Assert.AreEqual(
                    cursor, token.Start,
                    $"Gap or overlap in tokens for input {Describe(input)}. "
                    + $"Expected the next token to start at {cursor}, got {token.Start}.");
                Assert.IsTrue(
                    token.Length > 0,
                    $"Zero-length token {token.Kind} for input {Describe(input)}; a zero-length "
                    + "token means the scanner did not advance and would loop.");

                rebuilt.Append(input, token.Start, token.Length);
                cursor = token.End;
            }

            Assert.AreEqual(
                input.Length, cursor,
                $"Tokens stop short of the end for input {Describe(input)}.");
            Assert.AreEqual(
                input, rebuilt.ToString(),
                $"Concatenated tokens do not reproduce input {Describe(input)}.");
        }
    }

    [TestMethod]
    public void Scan_NeverThrows_OnAnyCorpusInput()
    {
        foreach (string input in Corpus)
        {
            // The whole reason this matters: the loader being replaced turned a parse
            // throw into an empty document and then overwrote the user's file with it.
            _ = JsoncScanner.Scan(input);
        }
    }

    [TestMethod]
    public void LineComment_StopsBeforeTheLineBreak()
    {
        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan("// note\n{}");

        Assert.AreEqual(JsoncTokenKind.LineComment, tokens[0].Kind);
        Assert.AreEqual("// note", new string(tokens[0].Text("// note\n{}")));
        Assert.AreEqual(JsoncTokenKind.Whitespace, tokens[1].Kind,
                        "The line break belongs to the following whitespace token, not the comment.");
    }

    [TestMethod]
    public void BlockComment_Unterminated_IsInvalid_NotSilentlyAccepted()
    {
        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan("/* open");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(JsoncTokenKind.Invalid, tokens[0].Kind,
                        "An unterminated block comment must be Invalid — appending to such a "
                        + "document would place text inside the comment.");
    }

    [TestMethod]
    public void EscapedQuote_DoesNotTerminateTheString()
    {
        const string input = "\"a\\\"b\"";
        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan(input);

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(JsoncTokenKind.String, tokens[0].Kind);
        Assert.AreEqual(input.Length, tokens[0].Length);
    }

    [TestMethod]
    public void RawNewlineInString_IsInvalid_AndStopsAtTheBreak()
    {
        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan("\"a\nb\"");

        Assert.AreEqual(JsoncTokenKind.Invalid, tokens[0].Kind);
        Assert.AreEqual(2, tokens[0].Length,
                        "Should stop at the newline rather than swallowing the rest of the file.");
    }

    [TestMethod]
    public void LeadingZero_IsInvalid_ButBareZeroIsFine()
    {
        Assert.AreEqual(JsoncTokenKind.Invalid, JsoncScanner.Scan("01")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.Number, JsoncScanner.Scan("0")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.Number, JsoncScanner.Scan("-0")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.Number, JsoncScanner.Scan("0.5")[0].Kind);
    }

    [TestMethod]
    public void NumberWithTrailingJunk_IsOneInvalidToken_NotACascade()
    {
        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan("1abc");

        Assert.AreEqual(1, tokens.Count, "Should consume the malformed run as a single token.");
        Assert.AreEqual(JsoncTokenKind.Invalid, tokens[0].Kind);
    }

    [TestMethod]
    public void UnknownBareWord_IsInvalid()
    {
        Assert.AreEqual(JsoncTokenKind.Invalid, JsoncScanner.Scan("nul")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.Null, JsoncScanner.Scan("null")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.True, JsoncScanner.Scan("true")[0].Kind);
        Assert.AreEqual(JsoncTokenKind.False, JsoncScanner.Scan("false")[0].Kind);
    }

    internal static string Describe(string input) =>
        "\"" + input.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
}
