using System.Linq;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the narrow-spec YAML front-matter parser + composer contract
/// (see <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c> §7).  The round-trip
/// fidelity guarantees here are load-bearing for the editor groups (#2,
/// #3): a user who hand-writes an agent / skill / command file and then
/// edits one field through ClaudeForge must not see their other fields,
/// comments, or unknown keys reformatted or dropped.
/// </summary>
public sealed class YamlFrontMatterTests
{
    [Fact]
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

        Assert.True(fm.Present, "A well-formed --- block must parse as present.");
        MessageAssert.Equal(input, YamlFrontMatter.Compose(fm),
            "Parse → Compose of an unmodified file must be byte-identical (every field keeps its RawText).");
    }

    [Fact]
    public void Parse_RoundTripsThroughCompose_ByteForByte_CrlfLineEndings()
    {
        string input =
            "---\r\n" +
            "name: foo\r\n" +
            "---\r\n" +
            "\r\n" +
            "Body.\r\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.True(fm.Present);
        MessageAssert.Equal(input, YamlFrontMatter.Compose(fm),
            "CRLF line endings must survive the round trip (body is rejoined with original '\\r').");
    }

    [Fact]
    public void Parse_NoFrontMatter_ReturnsNotPresentPlusFullBody()
    {
        string input = "# Just a CLAUDE.md\n\nNo front-matter here.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.False(fm.Present, "A file with no leading --- must report Present=false.");
        MessageAssert.Equal(input, fm.Body, "The whole text becomes the body when there's no front-matter.");
        Assert.Empty(fm.Nodes);
        MessageAssert.Equal(input, YamlFrontMatter.Compose(fm), "Compose of a front-matter-less doc returns the body unchanged.");
    }

    [Fact]
    public void Parse_UnterminatedFrontMatter_TreatedAsNoFrontMatter()
    {
        // Opening --- but no closing --- → almost certainly not real
        // front-matter; treat the whole thing as body rather than
        // swallowing the file into an unterminated block.
        string input = "---\nname: foo\nbody with no close\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.False(fm.Present);
        Assert.Equal(input, fm.Body);
    }

    [Fact]
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
        Assert.Equal("keep-me-verbatim", fm.FindScalar("x-custom-extension"));

        // Edit a DIFFERENT (known) key, then compose: the unknown key must
        // survive byte-for-byte.
        FrontMatter edited = fm.WithScalar("name", "bar");
        string composed = YamlFrontMatter.Compose(edited);

        MessageAssert.Contains("x-custom-extension: keep-me-verbatim", composed,
            "Editing one key must not drop or reformat an un-modelled sibling key.");
        MessageAssert.Contains("name: bar", composed, "The edited key must re-render with its new value.");
    }

    [Fact]
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

        MessageAssert.Equal(input, YamlFrontMatter.Compose(fm),
            "Comment lines inside the front-matter must round-trip verbatim and in place.");
    }

    [Fact]
    public void Parse_QuotedStringWithColon_DoesNotSplitIncorrectly()
    {
        string input =
            "---\n" +
            "description: \"Foo: bar baz\"\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        MessageAssert.Equal("Foo: bar baz", fm.FindScalar("description"),
            "Only the FIRST colon delimits key:value; quotes are stripped from the value, " +
            "so an embedded colon stays in the scalar.");
    }

    [Fact]
    public void Parse_InlineListAndBlockList_ProduceEquivalentTypedShape()
    {
        FrontMatter inline = YamlFrontMatter.Parse(
            "---\ntools: [Read, Grep, Bash]\n---\n\nBody.\n");
        FrontMatter block = YamlFrontMatter.Parse(
            "---\ntools:\n  - Read\n  - Grep\n  - Bash\n---\n\nBody.\n");

        string[] expected = ["Read", "Grep", "Bash"];

        MessageAssert.SequenceEqual(expected, inline.FindList("tools")!.ToArray(),
            "Inline list [a, b, c] must parse to the same typed shape as a block list.");
        MessageAssert.SequenceEqual(expected, block.FindList("tools")!.ToArray(),
            "Block list (- a / - b / - c) must parse to the same typed shape as an inline list.");
    }

    [Fact]
    public void Parse_EmptyInlineList_ProducesEmptyList()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\ntools: []\n---\n\nBody.\n");

        IReadOnlyList<string>? tools = fm.FindList("tools");
        Assert.NotNull(tools);
        Assert.Empty(tools!);
    }

    [Fact]
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
        MessageAssert.SequenceEqual(new[] { "model", "name", "description" }, order,
            "Field order must match source order, not an alphabetised / canonical order.");
        Assert.Equal(input, YamlFrontMatter.Compose(fm));
    }

    [Fact]
    public void WithScalar_NewKey_AppendsAfterExistingFields()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("model", "sonnet");

        string[] order = edited.Fields.Select(f => f.Key).ToArray();
        MessageAssert.SequenceEqual(new[] { "name", "description", "model" }, order,
            "A newly-added key appends after the last existing field, minimising the diff.");
        OrdinalAssert.Contains("model: sonnet", YamlFrontMatter.Compose(edited));
    }

    [Fact]
    public void WithScalar_ExistingKey_ReplacesInPlace()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("name", "renamed");

        string[] order = edited.Fields.Select(f => f.Key).ToArray();
        MessageAssert.SequenceEqual(new[] { "name", "description" }, order,
            "Replacing an existing key must keep its position, not move it to the end.");
        Assert.Equal("renamed", edited.FindScalar("name"));
    }

    [Fact]
    public void EditedScalarWithColon_ReRendersQuoted()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        // Set a value containing a colon — Compose must quote it so the
        // re-parsed file doesn't split at the wrong colon.
        FrontMatter edited = fm.WithScalar("description", "Foo: bar");
        string composed = YamlFrontMatter.Compose(edited);

        MessageAssert.Contains("description: \"Foo: bar\"", composed,
            "A re-rendered scalar containing a colon must be double-quoted.");

        // Re-parse to confirm the quoting actually round-trips the value.
        FrontMatter reparsed = YamlFrontMatter.Parse(composed);
        Assert.Equal("Foo: bar", reparsed.FindScalar("description"));
    }

    [Fact]
    public void Without_RemovesKey()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ndescription: bar\n---\n\nBody.\n");

        FrontMatter edited = fm.Without("description");

        MessageAssert.Null(edited.FindScalar("description"), "Removed key must no longer be found.");
        Assert.Equal(new[] { "name" }, edited.Fields.Select(f => f.Key).ToArray());
    }

    [Fact]
    public void EditedListField_ReRendersAsBlockList()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        FrontMatter edited = fm.WithList("tools", ["Read", "Grep"]);
        string composed = YamlFrontMatter.Compose(edited);

        MessageAssert.Contains("tools:\n  - Read\n  - Grep", composed,
            "A canonically re-rendered list field uses block-list syntax.");

        // And it must re-parse back to the same typed shape.
        FrontMatter reparsed = YamlFrontMatter.Parse(composed);
        Assert.Equal(new[] { "Read", "Grep" }, reparsed.FindList("tools")!.ToArray());
    }

    [Fact]
    public void EmptyFrontMatterBlock_ParsesPresentWithNoFields()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\n---\n\nBody.\n");

        Assert.True(fm.Present, "An empty --- / --- block is still 'present' (just field-less).");
        Assert.Empty(fm.Fields);
        Assert.Equal("\nBody.\n", fm.Body);
    }

    [Fact]
    public void EditedCommaScalar_NotOverQuoted_MatchesClaudeCodeNativeForm()
    {
        // A mid-string comma is legal in a YAML plain scalar in block context,
        // so an edited comma-separated tools value must NOT be quoted — it
        // should render in Claude Code's native `tools: Read, Grep, Bash` form.
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");
        FrontMatter edited = fm.WithScalar("tools", "Read, Grep, Bash");

        string composed = YamlFrontMatter.Compose(edited);
        MessageAssert.Contains("tools: Read, Grep, Bash", composed,
            "A comma-separated scalar must render unquoted (commas are legal in block-context plain scalars).");
        Assert.False(composed.Contains("\"Read, Grep, Bash\""),
            "The comma-scalar must NOT be double-quoted.");
    }

    [Fact]
    public void EditedScalarStartingWithIndicator_IsQuoted()
    {
        // Leading '[' would otherwise be read as an inline-list opener.
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");
        FrontMatter edited = fm.WithScalar("description", "[bracketed] value");

        string composed = YamlFrontMatter.Compose(edited);
        MessageAssert.Contains("description: \"[bracketed] value\"", composed,
            "A scalar starting with a YAML indicator char must be quoted.");
    }
}
