using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Block-scalar coverage for <see cref="YamlFrontMatter"/> — the <c>&gt;</c>
/// (folded) and <c>|</c> (literal) forms, with their chomping indicators.
///
/// <para>
/// Every skill shipped by Claude Code writes its <c>description</c> as a folded
/// block scalar, because the descriptions are long enough that a one-line plain
/// scalar would be unreadable:
/// </para>
/// <code>
/// description: &gt;-
///   Fetch, vet, and act on review feedback left on a pull request.
///   Use this WHENEVER the developer says there is review feedback.
/// </code>
/// <para>
/// The parser previously treated the <c>&gt;-</c> header as the value itself, so
/// every consumer of <c>FindScalar("description")</c> — the Agents &amp; Skills
/// list subtitle, the read-only detail pane, and the editor field — displayed the
/// literal text "&gt;-" instead of the prose.
/// </para>
/// </summary>
/// <remarks>
/// ⓘ <b>Ported from <c>main</c> on 2026-09-16</b>, where this coverage was written independently
/// of the branch's own block-scalar suite. Both are kept: 10 of these failed against the branch's
/// parser and 3 of the branch's failed against main's, so neither side was a superset and the
/// implementation here is the union. See <c>YamlFrontMatterBlockScalarTests</c> for the sibling.
/// </remarks>
public sealed class YamlFrontMatterRealFileBlockScalarTests
{
    /// <summary>
    /// A realistic skill file: folded description, strip chomping, two source
    /// lines that fold into one logical line.
    /// </summary>
    private const string FoldedSkill =
        "---\n" +
        "name: address-pr-feedback\n" +
        "description: >-\n" +
        "  Fetch, vet, and act on review feedback left on a pull request.\n" +
        "  Use this WHENEVER the developer says there is review feedback.\n" +
        "---\n" +
        "\n" +
        "# Address PR feedback\n";

    // ── Parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void FoldedBlockScalar_YieldsTheProse_NotTheHeaderToken()
    {
        FrontMatter fm = YamlFrontMatter.Parse(FoldedSkill);

        MessageAssert.Equal(
            "Fetch, vet, and act on review feedback left on a pull request. "
            + "Use this WHENEVER the developer says there is review feedback.",
            fm.FindScalar("description"),
            "A folded block scalar must parse to its folded prose. Returning the "
            + "'>-' header token is what put the literal '>-' in the skills list.");
    }

    [Fact]
    public void FoldedBlockScalar_IsNotMistakenForTheHeaderToken()
    {
        FrontMatter fm = YamlFrontMatter.Parse(FoldedSkill);

        MessageAssert.NotEqual(">-", fm.FindScalar("description"),
            "The block-scalar header must never surface as the value.");
    }

    [Fact]
    public void LiteralBlockScalar_PreservesInteriorNewlines()
    {
        string input =
            "---\n" +
            "name: t\n" +
            "description: |-\n" +
            "  line one\n" +
            "  line two\n" +
            "---\n";

        MessageAssert.Equal("line one\nline two", YamlFrontMatter.Parse(input).FindScalar("description"),
            "A literal ('|') block keeps its newlines; only the folded ('>') form joins lines.");
    }

    [Fact]
    public void FoldedBlockScalar_BlankLineBecomesAParagraphBreak()
    {
        string input =
            "---\n" +
            "description: >-\n" +
            "  para one\n" +
            "\n" +
            "  para two\n" +
            "---\n";

        MessageAssert.Equal("para one\npara two", YamlFrontMatter.Parse(input).FindScalar("description"),
            "In a folded block a blank line folds to a single newline, separating paragraphs.");
    }

    [Fact]
    public void ClipChomping_KeepsExactlyOneTrailingNewline()
    {
        string input =
            "---\n" +
            "description: |\n" +
            "  body\n" +
            "---\n";

        MessageAssert.Equal("body\n", YamlFrontMatter.Parse(input).FindScalar("description"),
            "Default (clip) chomping keeps a single trailing newline; '-' would strip it.");
    }

    [Fact]
    public void BlockScalar_DoesNotSwallowTheNextKey()
    {
        string input =
            "---\n" +
            "description: >-\n" +
            "  the description\n" +
            "model: sonnet\n" +
            "---\n";

        FrontMatter fm = YamlFrontMatter.Parse(input);

        MessageAssert.Equal("the description", fm.FindScalar("description"),
            "The block ends at the first line that is not more-indented than the block.");
        MessageAssert.Equal("sonnet", fm.FindScalar("model"),
            "A sibling key after the block must still parse as its own field.");
    }

    // ── Round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void UnmodifiedBlockScalar_RoundTripsByteForByte()
    {
        FrontMatter fm = YamlFrontMatter.Parse(FoldedSkill);

        MessageAssert.Equal(FoldedSkill, YamlFrontMatter.Compose(fm),
            "An untouched block-scalar field keeps its RawText, so the file is unchanged on save.");
    }

    /// <summary>
    /// The destructive case. Before block scalars were understood, the header line
    /// parsed as the field and each continuation line became a standalone node. On
    /// edit the field re-rendered as a quoted one-liner while those orphaned lines
    /// were still emitted after it — appending the old prose to the new value.
    /// </summary>
    [Fact]
    public void EditingABlockScalar_DoesNotStrandTheOldContinuationLines()
    {
        FrontMatter edited = YamlFrontMatter.Parse(FoldedSkill).WithScalar("description", "A new description.");
        string composed = YamlFrontMatter.Compose(edited);

        MessageAssert.Contains("A new description.", composed,
            "The edited value must reach the file.");
        Assert.False(composed.Contains("Fetch, vet, and act", StringComparison.Ordinal),
            "The replaced prose must not survive as orphaned lines below the new value — "
            + "that silently appends the old description to the new one.");
        Assert.False(composed.Contains("\">-\"", StringComparison.Ordinal),
            "The header token must never be written back as a quoted scalar value.");
    }

    [Fact]
    public void EditedBlockScalar_ReRendersAsABlockScalar_KeepingTheOriginalStyle()
    {
        FrontMatter edited = YamlFrontMatter.Parse(FoldedSkill).WithScalar("description", "A new description.");
        string composed = YamlFrontMatter.Compose(edited);

        MessageAssert.Contains("description: >-", composed,
            "A field that arrived as a folded block should be written back as one, so an "
            + "edit through the GUI does not reformat the file into a long single line.");
    }

    [Fact]
    public void EditedBlockScalar_ReParsesToTheEditedValue()
    {
        FrontMatter edited = YamlFrontMatter.Parse(FoldedSkill).WithScalar("description", "A new description.");

        MessageAssert.Equal("A new description.",
            YamlFrontMatter.Parse(YamlFrontMatter.Compose(edited)).FindScalar("description"),
            "Compose → Parse must return exactly what was set: the edit has to survive a save/load cycle.");
    }

    [Fact]
    public void MultiLineValueSetOnAPlainField_RendersAsALiteralBlock()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: t\n---\n")
                                        .WithScalar("notes", "first\nsecond");
        string composed = YamlFrontMatter.Compose(fm);

        MessageAssert.Equal("first\nsecond", YamlFrontMatter.Parse(composed).FindScalar("notes"),
            "A newline-bearing value cannot be a plain scalar; it must round-trip through a block.");
    }

    // ── Real-world shape ─────────────────────────────────────────────────

    /// <summary>
    /// The verbatim front-matter of a shipped skill. Worth pinning as a fixture
    /// because the prose contains every character that would force quoting in a
    /// plain scalar — a colon-space, a <c>#</c>, double quotes, an apostrophe, an
    /// em dash and an ellipsis — which is precisely why these are block scalars.
    /// </summary>
    private const string RealSkill =
        """
        ---
        name: address-pr-feedback
        description: >-
          Fetch, vet, and act on review feedback left on a pull request — from AI reviewers (codex, Copilot,
          greptile, …) or humans. Use this WHENEVER the developer says there's review feedback on a PR: "address
          the PR feedback", "codex left comments on #1234", "handle the Copilot review", "respond to the reviewers",
          "there are unresolved threads on my PR", or similar — even if they don't name a specific reviewer. Pulls
          the unresolved threads, vets each against the actual code, fixes the real issues, and responds/resolves.
        ---

        # Address PR feedback

        """;

    [Fact]
    public void RealSkillFile_FoldsToTheProseShownOnGitHub()
    {
        string? description = YamlFrontMatter.Parse(RealSkill).FindScalar("description");

        MessageAssert.StartsWith("Fetch, vet, and act on review feedback left on a pull request —", description,
            "The folded value begins at the prose, never at the '>-' header.");
        MessageAssert.Contains("on a PR: \"address the PR feedback\"", description,
            "Folding joins the source lines with a single space, so a sentence split across "
            + "two lines reads continuously — and a colon-space inside the prose is harmless here.");
        Assert.False(description!.Contains('\n'),
            "This block has no blank lines, so it folds to exactly one logical line.");
    }

    [Fact]
    public void RealSkillFile_RoundTripsByteForByte()
    {
        MessageAssert.Equal(RealSkill, YamlFrontMatter.Compose(YamlFrontMatter.Parse(RealSkill)),
            "Opening a skill in the editor and saving without edits must not touch the file.");
    }

    [Fact]
    public void RealSkillFile_EditedDescription_StaysAFoldedBlockAndDropsTheOldProse()
    {
        string composed = YamlFrontMatter.Compose(
            YamlFrontMatter.Parse(RealSkill).WithScalar("description", "A concise replacement description."));

        MessageAssert.Contains("description: >-", composed,
            "The folded style is preserved, so the file's shape does not churn on edit.");
        MessageAssert.Contains("  A concise replacement description.", composed,
            "The new prose is indented as block content.");
        Assert.False(composed.Contains("greptile", StringComparison.Ordinal),
            "None of the replaced prose may survive underneath the new value.");
        MessageAssert.Equal("A concise replacement description.",
            YamlFrontMatter.Parse(composed).FindScalar("description"),
            "And the edit survives a save/load cycle intact.");
    }

    // ── Typed projection ─────────────────────────────────────────────────

    [Fact]
    public void SkillFrontMatter_SurfacesTheFoldedDescription()
    {
        SkillFrontMatter skill = SkillFrontMatter.From(YamlFrontMatter.Parse(FoldedSkill));

        Assert.Equal("address-pr-feedback", skill.Name);
        MessageAssert.StartsWith("Fetch, vet, and act", skill.Description,
            "The typed projection reads through FindScalar, so it inherits the block-scalar fix — "
            + "this is what the skills list and the detail pane bind to.");
    }
}
