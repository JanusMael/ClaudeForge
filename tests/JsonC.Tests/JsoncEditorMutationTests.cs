using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.JsonC.Tests;

/// <summary>
/// Insertion and removal — the cases where the editor has to synthesize text rather
/// than swap a span, and therefore the cases where it can produce invalid JSON.
/// Every test here re-parses the result and asserts it is still clean.
/// </summary>
public sealed class JsoncEditorMutationTests
{
    private static void AssertStillValid(string text)
    {
        JsoncDocument document = JsoncDocument.Parse(text);
        Assert.True(
            document.IsEditable,
            $"Edit produced text that no longer parses: {string.Join("; ", document.Errors)}\n---\n{text}\n---");
    }

    [Fact]
    public void AddMember_AppendsAfterTheLastMember_WithMatchingIndent()
    {
        const string before = """
                              {
                                "model": "sonnet"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "effortLevel", JsonValue.Create("high"));

        Assert.Equal(
            """
            {
              "model": "sonnet",
              "effortLevel": "high"
            }
            """,
            after);
        AssertStillValid(after);
    }

    [Fact]
    public void AddMember_ToAnEmptyObject_OpensItOntoItsOwnLines()
    {
        const string before = "{}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        Assert.Contains("\"model\"", after);
        Assert.Equal("{" + Environment.NewLine + "  \"model\": \"opus\"" + Environment.NewLine + "}", after);
    }

    [Fact]
    public void AddMember_KeepsATrailingCommentAtTheEndOfTheObject()
    {
        const string before = """
                              {
                                "model": "sonnet"
                                // a note after the last member
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "effortLevel", JsonValue.Create("high"));

        AssertStillValid(after);
        Assert.Contains("// a note after the last member", after);
        Assert.Contains("\"effortLevel\": \"high\"", after);
    }

    [Fact]
    public void AddNestedPath_CreatesTheMissingIntermediateObjects()
    {
        const string before = """
                              {
                                "model": "sonnet"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "permissions.defaultMode",
                                            JsonValue.Create("acceptEdits"));

        AssertStillValid(after);
        Assert.Equal(
            """
            {
              "model": "sonnet",
              "permissions": {
                "defaultMode": "acceptEdits"
              }
            }
            """,
            after);
    }

    [Fact]
    public void AddIntoAnExistingNestedObject_AppendsThereNotAtTheRoot()
    {
        const string before = """
                              {
                                "permissions": {
                                  "defaultMode": "ask"
                                }
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "permissions.allow", new JsonArray());

        AssertStillValid(after);
        Assert.Equal(
            """
            {
              "permissions": {
                "defaultMode": "ask",
                "allow": []
              }
            }
            """,
            after);
    }

    [Fact]
    public void SetValue_OnAnEmptyDocument_CreatesTheRootObject()
    {
        string after = JsoncEditor.SetValue(string.Empty, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        Assert.Contains("\"model\": \"opus\"", after);
    }

    [Fact]
    public void SetValue_OnACommentsOnlyDocument_KeepsTheComments()
    {
        const string before = "// my hand-written header\n";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        Assert.StartsWith("// my hand-written header", after);
        Assert.Contains("\"model\": \"opus\"", after);
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_FirstMember_LeavesValidJsonAndNoLeadingComma()
    {
        const string before = """
                              {
                                "a": 1,
                                "b": 2,
                                "c": 3
                              }
                              """;

        string after = JsoncEditor.Remove(before, "a");

        AssertStillValid(after);
        Assert.Equal(
            """
            {
              "b": 2,
              "c": 3
            }
            """,
            after);
    }

    [Fact]
    public void Remove_MiddleMember()
    {
        const string before = """
                              {
                                "a": 1,
                                "b": 2,
                                "c": 3
                              }
                              """;

        string after = JsoncEditor.Remove(before, "b");

        AssertStillValid(after);
        Assert.Equal(
            """
            {
              "a": 1,
              "c": 3
            }
            """,
            after);
    }

    [Fact]
    public void Remove_LastMember_TakesThePrecedingCommaWithIt()
    {
        const string before = """
                              {
                                "a": 1,
                                "b": 2,
                                "c": 3
                              }
                              """;

        string after = JsoncEditor.Remove(before, "c");

        AssertStillValid(after);
        Assert.Equal(
            """
            {
              "a": 1,
              "b": 2
            }
            """,
            after);
    }

    [Fact]
    public void Remove_OnlyMember_LeavesAnEmptyObject()
    {
        const string before = """
                              {
                                "a": 1
                              }
                              """;

        string after = JsoncEditor.Remove(before, "a");

        AssertStillValid(after);
        Assert.Equal("{\n}", after.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Remove_NestedMember_LeavesSiblingsAndCommentsIntact()
    {
        const string before = """
                              {
                                "permissions": {
                                  // keep
                                  "defaultMode": "ask",
                                  "allow": []
                                }
                              }
                              """;

        string after = JsoncEditor.Remove(before, "permissions.allow");

        AssertStillValid(after);
        Assert.Contains("// keep", after);
        Assert.Equal(
            """
            {
              "permissions": {
                // keep
                "defaultMode": "ask"
              }
            }
            """,
            after);
    }

    [Fact]
    public void Remove_AbsentPath_IsANoOp_ReturningTheOriginalBytes()
    {
        const string before = """
                              {
                                // untouched
                                "a": 1
                              }
                              """;

        Assert.Equal(before, JsoncEditor.Remove(before, "nope"));
        Assert.Equal(before, JsoncEditor.Remove(before, "a.b.c"));
    }
}
