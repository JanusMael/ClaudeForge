using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.AgentForge.Jsonc.Tests;

/// <summary>
/// The reason this library exists: a save must change the bytes the user changed and
/// nothing else. Each test here names a specific thing today's re-serializing writer
/// destroys.
/// </summary>
[TestClass]
public sealed class JsoncEditorPreservationTests
{
    [TestMethod]
    public void SetExistingValue_ChangesOnlyThatValuesSpan()
    {
        const string before = """
                              {
                                "model": "sonnet",
                                "effortLevel": "high"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.AreEqual(
            """
            {
              "model": "opus",
              "effortLevel": "high"
            }
            """,
            after);
    }

    [TestMethod]
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

        StringAssert.Contains(after, "// why we pin the model");
        StringAssert.Contains(after, "// inline note");
        Assert.AreEqual(
            """
            {
              // why we pin the model
              "model": "opus", // inline note
              "effortLevel": "high"
            }
            """,
            after);
    }

    [TestMethod]
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

        StringAssert.Contains(after, "/* a block");
        StringAssert.Contains(after, "spanning lines */");
        Assert.AreEqual(before.Replace("\"high\"", "\"low\""), after,
                        "Only the edited value's span should differ.");
    }

    [TestMethod]
    public void TabIndentation_IsNotConvertedToSpaces()
    {
        string before = "{\n\t\"model\": \"sonnet\",\n\t\"nested\": {\n\t\t\"a\": 1\n\t}\n}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.IsFalse(after.Contains("  \"", StringComparison.Ordinal),
                       "A tab-indented document must not gain space indentation.");
        Assert.AreEqual(before.Replace("sonnet", "opus"), after);
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
    [TestMethod]
    public void InsertedMultiLineValue_UsesTheDocumentsTabsAndCrlf()
    {
        const string before = "{\r\n\t\"model\": \"sonnet\"\r\n}";

        JsonObject value = new() { ["defaultMode"] = JsonValue.Create("ask") };
        string after = JsoncEditor.SetValue(before, "permissions", value);

        Assert.AreEqual(
            "{\r\n\t\"model\": \"sonnet\",\r\n\t\"permissions\": {\r\n\t\t\"defaultMode\": \"ask\"\r\n\t}\r\n}",
            after);

        Assert.IsFalse(after.Contains("  ", StringComparison.Ordinal),
                       "No space indentation should appear in a tab-indented document.");
        Assert.AreEqual(
            after.Split("\r\n").Length - 1,
            after.Count(c => c == '\n'),
            "Every LF should be part of a CRLF pair; a bare LF means the inserted text "
            + "used the wrong line ending.");
    }

    [TestMethod]
    public void CrlfLineEndings_Survive()
    {
        const string before = "{\r\n  \"model\": \"sonnet\",\r\n  \"effortLevel\": \"high\"\r\n}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        Assert.AreEqual(before.Replace("sonnet", "opus"), after);
        Assert.IsFalse(
            after.Replace("\r\n", string.Empty).Contains('\n', StringComparison.Ordinal),
            "No bare LF should appear in a CRLF document.");
    }

    [TestMethod]
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

        Assert.IsTrue(
            after.IndexOf("zebra", StringComparison.Ordinal)
            < after.IndexOf("alpha", StringComparison.Ordinal),
            "Source key order must survive; a re-serializing writer is what loses it.");
        Assert.AreEqual(before.Replace(": 2", ": 99"), after);
    }

    [TestMethod]
    public void SetValue_ToTheSameValue_IsAByteIdenticalNoOp()
    {
        const string before = """
                              {
                                // keep me
                                "model": "sonnet"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("sonnet"));

        Assert.AreEqual(before, after,
                        "Re-writing the identical value should reproduce the file byte for byte.");
    }

    [TestMethod]
    public void ReplacingAScalarWithAnObject_IndentsToTheDocumentsStyle()
    {
        string before = "{\n\t\"permissions\": null\n}";

        JsonObject value = new()
        {
            ["defaultMode"] = JsonValue.Create("acceptEdits"),
            ["allow"] = new JsonArray { JsonValue.Create("Bash(git status)") },
        };

        string after = JsoncEditor.SetValue(before, "permissions", value);

        Assert.AreEqual(
            "{\n\t\"permissions\": {\n\t\t\"defaultMode\": \"acceptEdits\",\n\t\t\"allow\": [\n\t\t\t\"Bash(git status)\"\n\t\t]\n\t}\n}",
            after);
    }

    [TestMethod]
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

        Assert.AreEqual(before.Replace("\"ask\"", "\"acceptEdits\""), after);
        StringAssert.Contains(after, "// preserve this");
    }

    [TestMethod]
    public void TrailingCommaDocument_IsEditable_NotRejected()
    {
        const string before = """
                              {
                                "model": "sonnet",
                              }
                              """;

        JsoncDocument document = JsoncDocument.Parse(before);
        Assert.IsTrue(document.IsEditable,
                      "JSONC in the wild has trailing commas; rejecting them would route the "
                      + "caller onto a lossy fallback for something every JSONC parser accepts. "
                      + $"Errors: {string.Join("; ", document.Errors)}");

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));
        Assert.AreEqual(before.Replace("sonnet", "opus"), after);
    }

    [TestMethod]
    public void DuplicateKeys_TheLastOneIsEdited_MatchingReaderSemantics()
    {
        const string before = """
                              {
                                "model": "first",
                                "model": "second"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("edited"));

        Assert.AreEqual(
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
