using System.Linq;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the narrow-spec YAML front-matter parser + composer contract
/// (see <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c> §7).  The round-trip
/// fidelity guarantees here are load-bearing for the editor groups (#2,
/// #3): a user who hand-writes an agent / skill / command file and then
/// edits one field through ClaudeForge must not see their other fields,
/// comments, or unknown keys reformatted or dropped.
/// </summary>
[TestClass]
public sealed class YamlFrontMatterTests
{
    [TestMethod]
    public void Parse_RoundTripsThroughCompose_ByteForByte_KnownKeys()
    {
        string input =
            "---\n" +
            "name: code-reviewer\n" +
            "description: Expert review specialist\n" +
            "tools: Read, Grep, Bash\n" +
            "model: sonnet\n" +
            "---\n" +
            "\n" +
            "You are an expert code reviewer.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.IsTrue(fm.Present, "A well-formed --- block must parse as present.");
        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "Parse → Compose of an unmodified file must be byte-identical (every field keeps its RawText).");
    }

    [TestMethod]
    public void Parse_RoundTripsThroughCompose_ByteForByte_CrlfLineEndings()
    {
        string input =
            "---\r\n" +
            "name: foo\r\n" +
            "---\r\n" +
            "\r\n" +
            "Body.\r\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.IsTrue(fm.Present);
        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "CRLF line endings must survive the round trip (body is rejoined with original '\\r').");
    }

    [TestMethod]
    public void Parse_NoFrontMatter_ReturnsNotPresentPlusFullBody()
    {
        string input = "# Just a CLAUDE.md\n\nNo front-matter here.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.IsFalse(fm.Present, "A file with no leading --- must report Present=false.");
        Assert.AreEqual(input, fm.Body, "The whole text becomes the body when there's no front-matter.");
        Assert.AreEqual(0, fm.Nodes.Count);
        Assert.AreEqual(input, YamlFrontMatter.Compose(fm), "Compose of a front-matter-less doc returns the body unchanged.");
    }

    [TestMethod]
    public void Parse_UnterminatedFrontMatter_TreatedAsNoFrontMatter()
    {
        // Opening --- but no closing --- → almost certainly not real
        // front-matter; treat the whole thing as body rather than
        // swallowing the file into an unterminated block.
        string input = "---\nname: foo\nbody with no close\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.IsFalse(fm.Present);
        Assert.AreEqual(input, fm.Body);
    }

    [TestMethod]
    public void Parse_UnknownKey_PreservedThroughEditOfAnotherKey()
    {
        string input =
            "---\n" +
            "name: foo\n" +
            "x-custom-extension: keep-me-verbatim\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        // The unknown key is just an ordinary field — present and readable.
        Assert.AreEqual("keep-me-verbatim", fm.FindScalar("x-custom-extension"));

        // Edit a DIFFERENT (known) key, then compose: the unknown key must
        // survive byte-for-byte.
        FrontMatter edited = fm.WithScalar("name", "bar");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "x-custom-extension: keep-me-verbatim",
            "Editing one key must not drop or reformat an un-modelled sibling key.");
        StringAssert.Contains(composed, "name: bar", "The edited key must re-render with its new value.");
    }

    [TestMethod]
    public void Parse_CommentInFrontMatter_PreservedOnCompose()
    {
        string input =
            "---\n" +
            "# this comment documents the name below\n" +
            "name: foo\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "Comment lines inside the front-matter must round-trip verbatim and in place.");
    }

    [TestMethod]
    public void Parse_QuotedStringWithColon_DoesNotSplitIncorrectly()
    {
        string input =
            "---\n" +
            "description: \"Foo: bar baz\"\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual("Foo: bar baz", fm.FindScalar("description"),
            "Only the FIRST colon delimits key:value; quotes are stripped from the value, " +
            "so an embedded colon stays in the scalar.");
    }

    [TestMethod]
    public void Parse_InlineListAndBlockList_ProduceEquivalentTypedShape()
    {
        FrontMatter inline = YamlFrontMatter.Parse(
            "---\ntools: [Read, Grep, Bash]\n---\n\nBody.\n");
        FrontMatter block = YamlFrontMatter.Parse(
            "---\ntools:\n  - Read\n  - Grep\n  - Bash\n---\n\nBody.\n");

        string[] expected = ["Read", "Grep", "Bash"];

        CollectionAssert.AreEqual(expected, inline.FindList("tools")!.ToArray(),
            "Inline list [a, b, c] must parse to the same typed shape as a block list.");
        CollectionAssert.AreEqual(expected, block.FindList("tools")!.ToArray(),
            "Block list (- a / - b / - c) must parse to the same typed shape as an inline list.");
    }

    [TestMethod]
    public void Parse_EmptyInlineList_ProducesEmptyList()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\ntools: []\n---\n\nBody.\n");

        IReadOnlyList<string>? tools = fm.FindList("tools");
        Assert.IsNotNull(tools);
        Assert.AreEqual(0, tools!.Count);
    }

    [TestMethod]
    public void Compose_PreservesOriginalKeyOrder()
    {
        string input =
            "---\n" +
            "model: sonnet\n" +
            "name: foo\n" +
            "description: bar\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        string[] order = fm.Fields.Select(f => f.Key).ToArray();
        CollectionAssert.AreEqual(new[] { "model", "name", "description" }, order,
            "Field order must match source order, not an alphabetised / canonical order.");
        Assert.AreEqual(input, YamlFrontMatter.Compose(fm));
    }

    [TestMethod]
    public void WithScalar_NewKey_AppendsAfterExistingFields()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("model", "sonnet");

        string[] order = edited.Fields.Select(f => f.Key).ToArray();
        CollectionAssert.AreEqual(new[] { "name", "description", "model" }, order,
            "A newly-added key appends after the last existing field, minimising the diff.");
        StringAssert.Contains(YamlFrontMatter.Compose(edited), "model: sonnet");
    }

    [TestMethod]
    public void WithScalar_ExistingKey_ReplacesInPlace()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("name", "renamed");

        string[] order = edited.Fields.Select(f => f.Key).ToArray();
        CollectionAssert.AreEqual(new[] { "name", "description" }, order,
            "Replacing an existing key must keep its position, not move it to the end.");
        Assert.AreEqual("renamed", edited.FindScalar("name"));
    }

    [TestMethod]
    public void EditedScalarWithColon_ReRendersQuoted()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        // Set a value containing a colon — Compose must quote it so the
        // re-parsed file doesn't split at the wrong colon.
        FrontMatter edited = fm.WithScalar("description", "Foo: bar");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "description: \"Foo: bar\"",
            "A re-rendered scalar containing a colon must be double-quoted.");

        // Re-parse to confirm the quoting actually round-trips the value.
        FrontMatter reparsed = YamlFrontMatter.Parse(composed);
        Assert.AreEqual("Foo: bar", reparsed.FindScalar("description"));
    }

    [TestMethod]
    public void Without_RemovesKey()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.Without("description");

        Assert.IsNull(edited.FindScalar("description"), "Removed key must no longer be found.");
        CollectionAssert.AreEqual(new[] { "name" }, edited.Fields.Select(f => f.Key).ToArray());
    }

    [TestMethod]
    public void EditedListField_ReRendersAsBlockList()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        FrontMatter edited = fm.WithList("tools", ["Read", "Grep"]);
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "tools:\n  - Read\n  - Grep",
            "A canonically re-rendered list field uses block-list syntax.");

        // And it must re-parse back to the same typed shape.
        FrontMatter reparsed = YamlFrontMatter.Parse(composed);
        CollectionAssert.AreEqual(new[] { "Read", "Grep" }, reparsed.FindList("tools")!.ToArray());
    }

    [TestMethod]
    public void EmptyFrontMatterBlock_ParsesPresentWithNoFields()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\n---\n\nBody.\n");

        Assert.IsTrue(fm.Present, "An empty --- / --- block is still 'present' (just field-less).");
        Assert.AreEqual(0, fm.Fields.Count());
        Assert.AreEqual("\nBody.\n", fm.Body);
    }

    [TestMethod]
    public void EditedCommaScalar_NotOverQuoted_MatchesClaudeCodeNativeForm()
    {
        // A mid-string comma is legal in a YAML plain scalar in block context,
        // so an edited comma-separated tools value must NOT be quoted — it
        // should render in Claude Code's native `tools: Read, Grep, Bash` form.
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");
        FrontMatter edited = fm.WithScalar("tools", "Read, Grep, Bash");

        string composed = YamlFrontMatter.Compose(edited);
        StringAssert.Contains(composed, "tools: Read, Grep, Bash",
            "A comma-separated scalar must render unquoted (commas are legal in block-context plain scalars).");
        Assert.IsFalse(composed.Contains("\"Read, Grep, Bash\""),
            "The comma-scalar must NOT be double-quoted.");
    }

    [TestMethod]
    public void EditedScalarStartingWithIndicator_IsQuoted()
    {
        // Leading '[' would otherwise be read as an inline-list opener.
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");
        FrontMatter edited = fm.WithScalar("description", "[bracketed] value");

        string composed = YamlFrontMatter.Compose(edited);
        StringAssert.Contains(composed, "description: \"[bracketed] value\"",
            "A scalar starting with a YAML indicator char must be quoted.");
    }
}
