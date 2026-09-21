using System.Linq;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the folded / literal block-scalar reader
/// (<c>key: &gt;</c>, <c>&gt;-</c>, <c>&gt;+</c>, <c>|</c>, <c>|-</c>, <c>|+</c>).
///
/// <para>
/// ⛔⛔ <b>The failure this prevents is a silent wrong answer, not a cosmetic one.</b>  Before
/// these tests, <see cref="FrontMatter.FindScalar"/> returned the two-character marker
/// <c>"&gt;-"</c> as the description's value.  Because that is non-empty, every "does this
/// artifact declare a description?" check downstream — most visibly
/// <c>OpenCodeArtifactInventory.Diagnose</c> — passed, so a skill whose description was
/// unreadable was reported to the user as healthy.  A parser that returns junk is survivable;
/// one that returns junk convincingly is not.
/// </para>
///
/// <para>
/// ⚠ <b>Measured, 2026-08-27:</b> 14 of the skills under <c>~/.claude/skills/*/SKILL.md</c> on
/// the machine this was written on declare <c>description</c> as a block scalar — 8 as <c>&gt;</c>
/// and 6 as <c>&gt;-</c>.  This is the ordinary shape of a real skill file, not an exotic one.
/// </para>
///
/// <para>
/// ⭐ <b>The round-trip contract still rules.</b>  <see cref="YamlFrontMatter"/> promises that an
/// unedited field re-composes byte-for-byte from its preserved
/// <see cref="FrontMatterField.RawText"/>.  Reading a block scalar must not cost that, so the
/// header line AND its continuation lines are captured as one field's RawText — which is why the
/// round-trip tests below are as load-bearing as the value tests.
/// </para>
/// </summary>
[TestClass]
public sealed class YamlFrontMatterBlockScalarTests
{
    // ── The headline defect ──────────────────────────────────────────────

    /// <summary>
    /// ⛔ The regression this whole file exists for.
    /// </summary>
    [TestMethod]
    public void FoldedBlockScalar_ReadsTheFoldedText_NotTheMarker()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "name: address-pr-feedback\n" +
            "description: >-\n" +
            "  Fetch, vet, and act on review feedback\n" +
            "  left on a pull request.\n" +
            "---\n\nBody.\n");

        Assert.AreNotEqual(">-", fm.FindScalar("description"),
            "The block-scalar marker is not the value. Returning it is the silent-wrong-answer "
            + "that made unreadable descriptions look healthy downstream.");
        Assert.AreEqual(
            "Fetch, vet, and act on review feedback left on a pull request.",
            fm.FindScalar("description"),
            "A folded (>) scalar joins its continuation lines with single spaces.");
    }

    /// <summary>
    /// ⛔ The other half of the same defect: the continuation lines were not merely ignored, they
    /// were re-parsed as top-level entries.  A description containing a colon — which the real
    /// <c>address-pr-feedback</c> skill does — became a phantom field whose key was a sentence.
    /// A phantom field is not inert: <see cref="FrontMatter.Without"/> drops every node matching a
    /// key, and <see cref="FrontMatter.Find"/> takes the first match.
    /// </summary>
    [TestMethod]
    public void FoldedBlockScalar_ContinuationLineWithAColon_IsNotReadAsAField()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: >-\n" +
            "  Use this WHENEVER the developer says: address the feedback.\n" +
            "---\n\nBody.\n");

        CollectionAssert.AreEqual(new[] { "description" }, fm.Fields.Select(f => f.Key).ToArray(),
            "A continuation line belongs to the block scalar. Splitting it on its first colon "
            + "invents a field whose key is a sentence fragment.");
        Assert.AreEqual("Use this WHENEVER the developer says: address the feedback.",
            fm.FindScalar("description"));
    }

    /// <summary>
    /// ⛔ A block scalar whose body is empty must read as empty, so the "no description" checks
    /// downstream fire.  Reading it as <c>"&gt;"</c> is precisely what suppressed them.
    /// </summary>
    [TestMethod]
    public void EmptyBlockScalar_ReadsAsEmpty_SoAMissingDescriptionStaysDetectable()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: quiet\ndescription: >-\n---\n\nBody.\n");

        Assert.AreEqual(string.Empty, fm.FindScalar("description"),
            "A header with no continuation lines declares an empty string, not the marker.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(fm.FindScalar("description")),
            "This is the predicate every downstream 'has a description?' check uses.");
    }

    // ── Folding rules ────────────────────────────────────────────────────

    [TestMethod]
    public void FoldedBlockScalar_BlankLineBecomesASingleNewline()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: >-\n" +
            "  First paragraph.\n" +
            "\n" +
            "  Second paragraph.\n" +
            "---\n\nBody.\n");

        Assert.AreEqual("First paragraph.\nSecond paragraph.", fm.FindScalar("description"),
            "In a folded scalar n line breaks collapse to n-1 newlines, so one blank line is one "
            + "paragraph break.");
    }

    /// <summary>
    /// A YAML folding rule that matters for descriptions carrying an indented example: lines
    /// indented deeper than the block's own indent are NOT folded.
    /// </summary>
    [TestMethod]
    public void FoldedBlockScalar_MoreIndentedLinesKeepTheirLineBreaks()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: >-\n" +
            "  Run it like this:\n" +
            "    dotnet test\n" +
            "  and read the output.\n" +
            "---\n\nBody.\n");

        Assert.AreEqual("Run it like this:\n  dotnet test\nand read the output.",
            fm.FindScalar("description"),
            "More-indented lines are verbatim; folding would silently reflow a code sample onto "
            + "one line.");
    }

    [TestMethod]
    public void LiteralBlockScalar_KeepsEveryLineBreak()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: |-\n" +
            "  line one\n" +
            "  line two\n" +
            "---\n\nBody.\n");

        Assert.AreEqual("line one\nline two", fm.FindScalar("description"),
            "A literal (|) scalar preserves newlines; folding it would be the opposite of what "
            + "the author asked for.");
    }

    // ── Chomping ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Clip is the default and the majority form in the measured corpus (8 of 14 skills use a
    /// bare <c>&gt;</c>), so getting its single trailing newline right is not an edge case.
    /// </summary>
    [TestMethod]
    public void Chomping_Clip_KeepsExactlyOneTrailingNewline()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: >\n  text\n\n\n---\n\nBody.\n");

        Assert.AreEqual("text\n", fm.FindScalar("description"),
            "Clip (no indicator) keeps the final line break and drops the rest.");
    }

    [TestMethod]
    public void Chomping_Strip_KeepsNoTrailingNewline()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: >-\n  text\n\n\n---\n\nBody.\n");

        Assert.AreEqual("text", fm.FindScalar("description"),
            "Strip (-) removes every trailing line break.");
    }

    [TestMethod]
    public void Chomping_Keep_KeepsEveryTrailingNewline()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: |+\n  text\n\n\n---\n\nBody.\n");

        Assert.AreEqual("text\n\n\n", fm.FindScalar("description"),
            "Keep (+) preserves all trailing line breaks.");
    }

    // ── Indentation ──────────────────────────────────────────────────────

    /// <summary>
    /// An explicit indentation indicator is the only way to write a block whose first line is
    /// itself indented — auto-detection would swallow that leading space.
    /// </summary>
    [TestMethod]
    public void ExplicitIndentationIndicator_SetsTheContentIndent()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: |2-\n    indented first line\n  flush\n---\n\nBody.\n");

        Assert.AreEqual("  indented first line\nflush", fm.FindScalar("description"),
            "With |2 the content indent is 2, so the first line keeps its two extra spaces.");
    }

    [TestMethod]
    public void BlockScalar_StopsAtTheNextKey_AndDoesNotSwallowIt()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: >-\n" +
            "  the description\n" +
            "model: sonnet\n" +
            "---\n\nBody.\n");

        Assert.AreEqual("the description", fm.FindScalar("description"));
        Assert.AreEqual("sonnet", fm.FindScalar("model"),
            "A dedent to the key's own indent ends the block. Swallowing the next key would lose "
            + "a real field.");
        CollectionAssert.AreEqual(new[] { "description", "model" },
            fm.Fields.Select(f => f.Key).ToArray());
    }

    /// <summary>
    /// ⚠ An indented <c>---</c> is block content, not the closing delimiter.  Reading it as the
    /// close would truncate the front matter and dump the rest of the file into the body.
    /// </summary>
    [TestMethod]
    public void IndentedTripleDashInsideABlockScalar_IsContent_NotTheClosingDelimiter()
    {
        string input =
            "---\n" +
            "description: |-\n" +
            "  above\n" +
            "  ---\n" +
            "  below\n" +
            "model: sonnet\n" +
            "---\n\nBody.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual("above\n---\nbelow", fm.FindScalar("description"));
        Assert.AreEqual("sonnet", fm.FindScalar("model"),
            "The real closing delimiter is the one at the block's own indent level.");
        Assert.AreEqual("\nBody.\n", fm.Body);
    }

    /// <summary>
    /// A plain scalar cannot begin with <c>&gt;</c> or <c>|</c> in YAML, so anything that is not a
    /// well-formed header falls back to the old plain-scalar reading rather than guessing.
    /// </summary>
    [TestMethod]
    public void MalformedHeader_FallsBackToPlainScalar()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: >nonsense\n---\n\nBody.\n");

        Assert.AreEqual(">nonsense", fm.FindScalar("description"),
            "Only a real header (indicator, optional indent digit, optional chomping) opens a "
            + "block. Everything else keeps its previous meaning.");
    }

    [TestMethod]
    public void BlockScalarHeader_ToleratesATrailingComment()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: >- # why it is folded\n  the text\n---\n\nBody.\n");

        Assert.AreEqual("the text", fm.FindScalar("description"),
            "A comment after the header is not part of the value.");
    }

    // ── Round-trip: the contract this fix must not cost ──────────────────

    [TestMethod]
    public void BlockScalar_RoundTripsThroughCompose_ByteForByte()
    {
        string input =
            "---\n" +
            "name: address-pr-feedback\n" +
            "description: >-\n" +
            "  Fetch, vet, and act on review feedback\n" +
            "  left on a pull request.\n" +
            "model: sonnet\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "An unedited block scalar must re-compose byte-for-byte — the header line and its "
            + "continuation lines are one field's RawText.");
    }

    /// <summary>
    /// ⛔ CRLF is not hypothetical: the measured skill corpus contains CRLF files.  A multi-line
    /// RawText is joined internally with '\n', so Compose must re-apply the file's own ending or
    /// every save of a CRLF skill silently rewrites its block scalar to LF.
    /// </summary>
    [TestMethod]
    public void BlockScalar_RoundTripsThroughCompose_ByteForByte_CrlfLineEndings()
    {
        string input =
            "---\r\n" +
            "description: >-\r\n" +
            "  first line\r\n" +
            "  second line\r\n" +
            "---\r\n" +
            "\r\n" +
            "Body.\r\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual("first line second line", fm.FindScalar("description"),
            "The '\\r' must be stripped before folding, or every folded line ends in a stray CR.");
        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "A CRLF file's multi-line field must re-compose with CRLF throughout, not just on its "
            + "first line.");
    }

    /// <summary>
    /// The same multi-line-RawText hazard, on the construct that already had one: a block list.
    /// </summary>
    [TestMethod]
    public void BlockList_RoundTripsThroughCompose_ByteForByte_CrlfLineEndings()
    {
        string input =
            "---\r\n" +
            "tools:\r\n" +
            "  - Read\r\n" +
            "  - Grep\r\n" +
            "---\r\n" +
            "\r\n" +
            "Body.\r\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        Assert.AreEqual(input, YamlFrontMatter.Compose(fm),
            "A CRLF block list must keep CRLF on its item lines too.");
    }

    // ── Writing back what we can now read ────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>The fix creates this hazard, so it must close it.</b>  Reading a literal block makes
    /// <see cref="FrontMatter.FindScalar"/> able to return a value containing newlines for the
    /// first time — and the editor writes <c>description</c> back on <i>every</i> save, edited or
    /// not.  Rendering such a value as a plain <c>key: value</c> line would emit a raw newline
    /// mid-scalar and corrupt the file.
    /// </summary>
    [TestMethod]
    public void EditedMultiLineScalar_ReRendersAsALiteralBlock_AndReparsesToTheSameValue()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("description", "line one\nline two");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "description: |-\n  line one\n  line two",
            "A multi-line scalar re-renders as a literal block, which is the only shape that can "
            + "carry it.");
        Assert.AreEqual("line one\nline two",
            YamlFrontMatter.Parse(composed).FindScalar("description"),
            "And it must read back identically — write and read are one contract.");
    }

    /// <summary>
    /// Auto-detected indentation would eat a leading space, so a value whose first line starts
    /// with one needs the explicit indicator.
    /// </summary>
    [TestMethod]
    public void EditedMultiLineScalar_WithALeadingSpace_UsesTheExplicitIndentIndicator()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n");

        FrontMatter edited = fm.WithScalar("description", "  indented\nflush");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "description: |2-",
            "Without an explicit indent indicator the leading spaces are indistinguishable from "
            + "the block's own indentation.");
        Assert.AreEqual("  indented\nflush",
            YamlFrontMatter.Parse(composed).FindScalar("description"));
    }

    [TestMethod]
    public void EditedMultiLineScalar_WithTrailingNewlines_ChoosesTheChompingIndicatorThatKeepsThem()
    {
        FrontMatter one = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n")
                                         .WithScalar("description", "a\nb\n");
        FrontMatter many = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n")
                                          .WithScalar("description", "a\nb\n\n");

        Assert.AreEqual("a\nb\n",
            YamlFrontMatter.Parse(YamlFrontMatter.Compose(one)).FindScalar("description"),
            "One trailing newline is clip.");
        Assert.AreEqual("a\nb\n\n",
            YamlFrontMatter.Parse(YamlFrontMatter.Compose(many)).FindScalar("description"),
            "Two or more trailing newlines need keep (+).");
    }

    /// <summary>
    /// The single-line path must be untouched by all of the above.
    /// </summary>
    [TestMethod]
    public void EditedSingleLineScalar_StillRendersAsAPlainKeyValueLine()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: foo\n---\n\nBody.\n")
                                        .WithScalar("description", "an ordinary description");

        StringAssert.Contains(YamlFrontMatter.Compose(fm), "description: an ordinary description",
            "Introducing block-scalar rendering must not reshape ordinary single-line values.");
    }
}
