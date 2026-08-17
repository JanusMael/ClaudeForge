using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.AgentForge.Jsonc.Tests;

/// <summary>
/// Insertion and removal — the cases where the editor has to synthesize text rather
/// than swap a span, and therefore the cases where it can produce invalid JSON.
/// Every test here re-parses the result and asserts it is still clean.
/// </summary>
[TestClass]
public sealed class JsoncEditorMutationTests
{
    private static void AssertStillValid(string text)
    {
        JsoncDocument document = JsoncDocument.Parse(text);
        Assert.IsTrue(
            document.IsEditable,
            $"Edit produced text that no longer parses: {string.Join("; ", document.Errors)}\n---\n{text}\n---");
    }

    [TestMethod]
    public void AddMember_AppendsAfterTheLastMember_WithMatchingIndent()
    {
        const string before = """
                              {
                                "model": "sonnet"
                              }
                              """;

        string after = JsoncEditor.SetValue(before, "effortLevel", JsonValue.Create("high"));

        Assert.AreEqual(
            """
            {
              "model": "sonnet",
              "effortLevel": "high"
            }
            """,
            after);
        AssertStillValid(after);
    }

    [TestMethod]
    public void AddMember_ToAnEmptyObject_OpensItOntoItsOwnLines()
    {
        const string before = "{}";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        StringAssert.Contains(after, "\"model\"");
        Assert.AreEqual("{" + Environment.NewLine + "  \"model\": \"opus\"" + Environment.NewLine + "}", after);
    }

    [TestMethod]
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
        StringAssert.Contains(after, "// a note after the last member");
        StringAssert.Contains(after, "\"effortLevel\": \"high\"");
    }

    [TestMethod]
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
        Assert.AreEqual(
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

    [TestMethod]
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
        Assert.AreEqual(
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

    [TestMethod]
    public void SetValue_OnAnEmptyDocument_CreatesTheRootObject()
    {
        string after = JsoncEditor.SetValue(string.Empty, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        StringAssert.Contains(after, "\"model\": \"opus\"");
    }

    [TestMethod]
    public void SetValue_OnACommentsOnlyDocument_KeepsTheComments()
    {
        const string before = "// my hand-written header\n";

        string after = JsoncEditor.SetValue(before, "model", JsonValue.Create("opus"));

        AssertStillValid(after);
        StringAssert.StartsWith(after, "// my hand-written header");
        StringAssert.Contains(after, "\"model\": \"opus\"");
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    [TestMethod]
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
        Assert.AreEqual(
            """
            {
              "b": 2,
              "c": 3
            }
            """,
            after);
    }

    [TestMethod]
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
        Assert.AreEqual(
            """
            {
              "a": 1,
              "c": 3
            }
            """,
            after);
    }

    [TestMethod]
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
        Assert.AreEqual(
            """
            {
              "a": 1,
              "b": 2
            }
            """,
            after);
    }

    [TestMethod]
    public void Remove_OnlyMember_LeavesAnEmptyObject()
    {
        const string before = """
                              {
                                "a": 1
                              }
                              """;

        string after = JsoncEditor.Remove(before, "a");

        AssertStillValid(after);
        Assert.AreEqual("{\n}", after.Replace("\r\n", "\n"));
    }

    [TestMethod]
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
        StringAssert.Contains(after, "// keep");
        Assert.AreEqual(
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

    [TestMethod]
    public void Remove_AbsentPath_IsANoOp_ReturningTheOriginalBytes()
    {
        const string before = """
                              {
                                // untouched
                                "a": 1
                              }
                              """;

        Assert.AreEqual(before, JsoncEditor.Remove(before, "nope"));
        Assert.AreEqual(before, JsoncEditor.Remove(before, "a.b.c"));
    }
}
