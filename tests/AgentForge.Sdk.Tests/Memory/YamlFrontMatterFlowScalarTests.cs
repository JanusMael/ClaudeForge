using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Coverage for the two YAML shapes that carry a value across several lines
/// without a <c>&gt;</c> / <c>|</c> indicator: the multi-line <b>flow scalar</b>,
/// and the <b>nested mapping</b> that looks just like one.
///
/// <para>
/// A flow scalar can open on the key line or on the line below it.  Anthropic's
/// own <c>math-olympiad</c> plugin skill uses the second form:
/// </para>
/// <code>
/// description:
///   "Solve competition math problems (IMO, Putnam, USAMO, AIME) with adversarial
///   verification that catches the errors self-verification misses."
/// </code>
/// <para>
/// Nothing follows the colon, so the parser used to read the value as empty and
/// drop the prose into unparsed filler — the skill showed "(no description)".
/// </para>
///
/// <para>
/// A nested mapping is indented the same way but is a structure, not prose.  The
/// typed surface deliberately does not model it, and the contract is that it
/// round-trips verbatim and stays invisible to <see cref="FrontMatter.FindScalar"/>
/// / <see cref="FrontMatter.WithScalar"/>.  It used to be flattened into phantom
/// top-level fields, so editing one re-rendered it at column 0 and silently
/// lifted it out of its parent.
/// </para>
/// </summary>
/// <remarks>
/// ⓘ <b>Ported from <c>main</c> on 2026-09-16</b>, where this coverage was written independently
/// of the branch's own block-scalar suite. Both are kept: 10 of these failed against the branch's
/// parser and 3 of the branch's failed against main's, so neither side was a superset and the
/// implementation here is the union. See <c>YamlFrontMatterBlockScalarTests</c> for the sibling.
/// </remarks>
[TestClass]
public sealed class YamlFrontMatterFlowScalarTests
{
    /// <summary>The math-olympiad shape: quoted value entirely on the following lines.</summary>
    private const string ValueOnNextLines =
        "---\n" +
        "name: math-olympiad\n" +
        "description:\n" +
        "  \"Solve competition math problems (IMO, Putnam, USAMO, AIME) with adversarial\n" +
        "  verification that catches the errors self-verification misses.\"\n" +
        "version: 0.1.0\n" +
        "---\n" +
        "\n" +
        "Body.\n";

    /// <summary>A real memory note: <c>metadata</c> is a mapping, not a scalar.</summary>
    private const string NestedMapping =
        "---\n" +
        "name: note\n" +
        "description: A one-liner.\n" +
        "metadata:\n" +
        "  node_type: memory\n" +
        "  type: project\n" +
        "---\n" +
        "\n" +
        "Body.\n";

    private const string ExpectedProse =
        "Solve competition math problems (IMO, Putnam, USAMO, AIME) with adversarial "
        + "verification that catches the errors self-verification misses.";

    // ── Multi-line flow scalars ──────────────────────────────────────────

    [TestMethod]
    public void QuotedScalar_StartingOnTheNextLine_YieldsTheWholeProse()
    {
        FrontMatter fm = YamlFrontMatter.Parse(ValueOnNextLines);

        Assert.AreEqual(
            ExpectedProse,
            fm.FindScalar("description"),
            "Nothing follows the colon, so the value lives entirely on the indented lines below it.");
    }

    [TestMethod]
    public void QuotedScalar_OpeningOnTheKeyLine_YieldsTheWholeProse()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: \"Solve competition math problems (IMO, Putnam, USAMO, AIME) with adversarial\n" +
            "  verification that catches the errors self-verification misses.\"\n" +
            "---\n" +
            "\n" +
            "Body.\n");

        Assert.AreEqual(ExpectedProse, fm.FindScalar("description"));
    }

    /// <summary>
    /// The surrounding quotes sit on different lines, so they can only be
    /// stripped from the joined value — never from either line alone.
    /// </summary>
    [TestMethod]
    public void MultiLineQuotedScalar_DoesNotKeepAStrayQuote()
    {
        string? value = YamlFrontMatter.Parse(ValueOnNextLines).FindScalar("description");

        Assert.IsFalse(value!.StartsWith('"'), "A stray opening quote means the value was cut at line one.");
        Assert.IsFalse(value.EndsWith('"'));
    }

    [TestMethod]
    public void PlainMultiLineScalar_FoldsWithSpaces()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "description: this value has\n" +
            "  no quotes at all\n" +
            "---\n" +
            "\n" +
            "Body.\n");

        Assert.AreEqual("this value has no quotes at all", fm.FindScalar("description"));
    }

    [TestMethod]
    public void FlowScalar_DoesNotSwallowTheNextTopLevelKey()
    {
        FrontMatter fm = YamlFrontMatter.Parse(ValueOnNextLines);

        Assert.AreEqual("0.1.0", fm.FindScalar("version"));
        Assert.AreEqual("math-olympiad", fm.FindScalar("name"));
    }

    [TestMethod]
    public void UnmodifiedFlowScalar_RoundTripsByteForByte()
    {
        Assert.AreEqual(
            ValueOnNextLines,
            YamlFrontMatter.Compose(YamlFrontMatter.Parse(ValueOnNextLines)));
    }

    /// <summary>
    /// The value arrived spread over several lines, so an edit keeps it that way
    /// — as a folded block, the shape every other skill uses. Collapsing it into
    /// one very long plain line would churn the file on first edit.
    /// </summary>
    [TestMethod]
    public void EditedFlowScalar_ReRendersAsAFoldedBlock()
    {
        FrontMatter edited = YamlFrontMatter.Parse(ValueOnNextLines)
                                            .WithScalar("description", "Short now.");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "description: >-");
        Assert.IsFalse(
            composed.Contains("Putnam", StringComparison.Ordinal),
            "The superseded continuation lines must not be left stranded in the file.");
        Assert.AreEqual("Short now.", YamlFrontMatter.Parse(composed).FindScalar("description"));
    }

    // ── Nested mappings ──────────────────────────────────────────────────

    [TestMethod]
    public void NestedMapping_KeysAreNotSurfacedAsTopLevelFields()
    {
        FrontMatter fm = YamlFrontMatter.Parse(NestedMapping);

        Assert.IsNull(fm.FindScalar("node_type"), "A nested key must not read as a top-level field.");
        Assert.IsNull(fm.FindScalar("type"));
        Assert.AreEqual("A one-liner.", fm.FindScalar("description"), "Real top-level fields still work.");
    }

    [TestMethod]
    public void NestedMapping_RoundTripsByteForByte()
    {
        Assert.AreEqual(
            NestedMapping,
            YamlFrontMatter.Compose(YamlFrontMatter.Parse(NestedMapping)));
    }

    /// <summary>
    /// The corruption guard: writing a key that only exists inside a mapping must
    /// never re-render that nested line at column 0, which would lift it out of
    /// its parent and silently change the file's meaning.
    /// </summary>
    [TestMethod]
    public void EditingAKeyThatOnlyExistsNested_LeavesTheMappingIntact()
    {
        FrontMatter edited = YamlFrontMatter.Parse(NestedMapping).WithScalar("type", "EDITED");
        string composed = YamlFrontMatter.Compose(edited);

        StringAssert.Contains(composed, "metadata:\n  node_type: memory\n  type: project");
        Assert.IsFalse(
            composed.Contains("\ntype: project", StringComparison.Ordinal),
            "The nested 'type' must not be de-indented out of 'metadata'.");
    }

    [TestMethod]
    public void EditingARealTopLevelField_LeavesTheMappingIntact()
    {
        string composed = YamlFrontMatter.Compose(
            YamlFrontMatter.Parse(NestedMapping).WithScalar("description", "Changed."));

        StringAssert.Contains(composed, "metadata:\n  node_type: memory\n  type: project");
        StringAssert.Contains(composed, "description: Changed.");
    }

    /// <summary>
    /// A blank line ends the indented run. The lines past it are no longer part
    /// of the mapping as far as the parser models it, but they still have to
    /// survive a round-trip rather than being dropped.
    /// </summary>
    [TestMethod]
    public void MappingInterruptedByABlankLine_StillRoundTrips()
    {
        const string text =
            "---\n" +
            "metadata:\n" +
            "  node_type: memory\n" +
            "\n" +
            "  type: project\n" +
            "---\n" +
            "\n" +
            "Body.\n";

        Assert.AreEqual(text, YamlFrontMatter.Compose(YamlFrontMatter.Parse(text)));
        Assert.IsNull(YamlFrontMatter.Parse(text).FindScalar("type"));
    }
}
