using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.JsonC.Tests;

/// <summary>
/// The reason this library exists: a save must change the bytes the user changed and
/// nothing else. Each test here names a specific thing today's re-serializing writer
/// destroys.
/// </summary>
public sealed class JsoncEditorPreservationTests
{
    [Fact]
    public void SetExistingValue_ChangesOnlyThatValuesSpan()
    {
        const string before = """
                              {
                                "model": "sonnet",
                                "effortLevel": "high"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.Equal(
            """
            {
              "model": "opus",
              "effortLevel": "high"
            }
            """,
            after);
    }

    [Fact]
    public void LineComments_Survive_IncludingOnesAttachedToTheEditedMember()
    {
        const string before = """
                              {
                                // why we pin the model
                                "model": "sonnet", // inline note
                                "effortLevel": "high"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.Contains("// why we pin the model", after);
        Assert.Contains("// inline note", after);
        Assert.Equal(
            """
            {
              // why we pin the model
              "model": "opus", // inline note
              "effortLevel": "high"
            }
            """,
            after);
    }

    [Fact]
    public void BlockComments_AndBlankLines_Survive()
    {
        const string before = """
                              {
                                /* a block
                                   spanning lines */

                                "model": "sonnet",

                                "effortLevel": "high"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "effortLevel", JsonValue.Create("low"));

        Assert.Contains("/* a block", after);
        Assert.Contains("spanning lines */", after);
        MessageAssert.Equal(before.Replace("\"high\"", "\"low\""), after,
                        "Only the edited value's span should differ.");
    }

    [Fact]
    public void TabIndentation_IsNotConvertedToSpaces()
    {
        string before = "{\n\t\"model\": \"sonnet\",\n\t\"nested\": {\n\t\t\"a\": 1\n\t}\n}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.False(after.Contains("  \"", StringComparison.Ordinal),
                       "A tab-indented document must not gain space indentation.");
        Assert.Equal(before.Replace("sonnet", "opus"), after);
    }

    /// <summary>
    /// Tabs and CRLF together, on an <i>inserted multi-line</i> value — the only shape
    /// where the writer has to choose an indent unit and a line ending rather than reuse
    /// what is already on the line.
    /// </summary>
    /// <remarks>
    /// Added after canarying: disabling style detection entirely left
    /// <see cref="TabIndentation_IsNotConvertedToSpaces"/> and
    /// <see cref="CrlfLineEndings_Survive"/> both passing, because replacing one scalar
    /// with another never consults the style. Two tests whose names promised more than
    /// they checked. This is the one that actually fails if detection breaks, and it is
    /// the realistic case — a user with a tab-indented CRLF config gaining
    /// space-indented LF islands wherever the tool inserted something.
    /// </remarks>
    [Fact]
    public void InsertedMultiLineValue_UsesTheDocumentsTabsAndCrlf()
    {
        const string before = "{\r\n\t\"model\": \"sonnet\"\r\n}";

        JsonObject value = new() { ["defaultMode"] = JsonValue.Create("ask") };
        string after = JsoncEditor.SetValue(before, "permissions", value);

        Assert.Equal(
            "{\r\n\t\"model\": \"sonnet\",\r\n\t\"permissions\": {\r\n\t\t\"defaultMode\": \"ask\"\r\n\t}\r\n}",
            after);

        Assert.False(after.Contains("  ", StringComparison.Ordinal),
                       "No space indentation should appear in a tab-indented document.");
        MessageAssert.Equal(
            after.Split("\r\n").Length - 1,
            after.Count(c => c == '\n'),
            "Every LF should be part of a CRLF pair; a bare LF means the inserted text "
            + "used the wrong line ending.");
    }

    [Fact]
    public void CrlfLineEndings_Survive()
    {
        const string before = "{\r\n  \"model\": \"sonnet\",\r\n  \"effortLevel\": \"high\"\r\n}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.Equal(before.Replace("sonnet", "opus"), after);
        Assert.False(
            after.Replace("\r\n", string.Empty).Contains('\n', StringComparison.Ordinal),
            "No bare LF should appear in a CRLF document.");
    }

    [Fact]
    public void KeyOrder_IsNeverNormalized()
    {
        const string before = """
                              {
                                "zebra": 1,
                                "alpha": 2,
                                "middle": 3
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "alpha", JsonValue.Create(99));

        Assert.True(
            after.IndexOf("zebra", StringComparison.Ordinal)
            < after.IndexOf("alpha", StringComparison.Ordinal),
            "Source key order must survive; a re-serializing writer is what loses it.");
        Assert.Equal(before.Replace(": 2", ": 99"), after);
    }

    [Fact]
    public void SetValue_ToTheSameValue_IsAByteIdenticalNoOp()
    {
        const string before = """
                              {
                                // keep me
                                "model": "sonnet"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("sonnet"));

        MessageAssert.Equal(before, after,
                        "Re-writing the identical value should reproduce the file byte for byte.");
    }

    [Fact]
    public void ReplacingAScalarWithAnObject_IndentsToTheDocumentsStyle()
    {
        string before = "{\n\t\"permissions\": null\n}";

        JsonObject value = new()
        {
            ["defaultMode"] = JsonValue.Create("acceptEdits"),
            ["allow"] = new JsonArray { JsonValue.Create("Bash(git status)") },
        };

        string after = JsoncEditor.SetValue(before, "permissions", value);

        Assert.Equal(
            "{\n\t\"permissions\": {\n\t\t\"defaultMode\": \"acceptEdits\",\n\t\t\"allow\": [\n\t\t\t\"Bash(git status)\"\n\t\t]\n\t}\n}",
            after);
    }

    [Fact]
    public void NestedValue_IsReachedByDottedPath_AndSiblingsAreUntouched()
    {
        const string before = """
                              {
                                "permissions": {
                                  // preserve this
                                  "defaultMode": "ask",
                                  "allow": []
                                }
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "permissions.defaultMode",
                                            JsonValue.Create("acceptEdits"));

        Assert.Equal(before.Replace("\"ask\"", "\"acceptEdits\""), after);
        Assert.Contains("// preserve this", after);
    }

    [Fact]
    public void TrailingCommaDocument_IsEditable_NotRejected()
    {
        const string before = """
                              {
                                "model": "sonnet",
                              }
                              """;

        JsoncDocument document = JsoncDocument.Parse(before);
        Assert.True(document.IsEditable,
                      "JSONC in the wild has trailing commas; rejecting them would route the "
                      + "caller onto a lossy fallback for something every JSONC parser accepts. "
                      + $"Errors: {string.Join("; ", document.Errors)}");

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));
        Assert.Equal(before.Replace("sonnet", "opus"), after);
    }

    [Fact]
    public void DuplicateKeys_TheLastOneIsEdited_MatchingReaderSemantics()
    {
        const string before = """
                              {
                                "model": "first",
                                "model": "second"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("edited"));

        MessageAssert.Equal(
            """
            {
              "model": "first",
              "model": "edited"
            }
            """,
            after,
            "System.Text.Json's object model keeps the last duplicate, so that is the one a "
            + "reader sees and therefore the one an edit must target.");
    }
}
