using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Commands;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>command</c> map: six fields, one of them genuinely required.
/// </summary>
/// <remarks>
/// The first shape in this phase with a <c>required</c> field, and the first with
/// <c>additionalProperties: false</c> alongside optional fields — so the two interesting properties
/// are that a missing template is <i>reported</i> rather than invented, and that an unknown field is
/// still preserved even though the schema forbids it.
/// </remarks>
[TestClass]
public sealed class OpenCodeCommandCodecTests
{
    private static void AssertRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(OpenCodeCommandCodec.WriteMap(
                OpenCodeCommandCodec.ReadMap(CurrencyText.Parse(json)))),
            because);

    // ── Round trips ──────────────────────────────────────────────────────────

    [TestMethod]
    public void AFullyPopulatedCommand_RoundTripsEveryField()
    {
        AssertRoundTrips(
            """
            {"review":{"template":"Review $ARGUMENTS","description":"Review code",
            "agent":"build","model":"anthropic/claude","variant":"fast","subtask":true}}
            """,
            "All six fields are surfaced, so nothing should need the opaque path.");
    }

    [TestMethod]
    public void ATemplateOnlyCommand_StaysTemplateOnly()
    {
        AssertRoundTrips(
            """{"review":{"template":"Review this"}}""",
            "Only template is required, so the other five must stay absent rather than "
            + "materialising as empty strings.");
    }

    [TestMethod]
    public void CommandOrderIsPreserved()
    {
        object? written = OpenCodeCommandCodec.WriteMap(OpenCodeCommandCodec.ReadMap(
            CurrencyText.Parse("""{"zeta":{"template":"a"},"alpha":{"template":"b"}}""")));

        CollectionAssert.AreEqual(
            new[] { "zeta", "alpha" },
            ((IReadOnlyDictionary<string, object?>)written!).Keys.ToArray(),
            "Command order carries no meaning, but alphabetising a user's file turns a one-line "
            + "change into a whole-file diff.");
    }

    [TestMethod]
    public void EveryEmittedMapIsAnOrderedMap_AtBothLevels()
    {
        object? written = OpenCodeCommandCodec.WriteMap(OpenCodeCommandCodec.ReadMap(
            CurrencyText.Parse("""{"review":{"template":"a","description":"b"}}""")));

        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "the command map");
        Assert.IsInstanceOfType<OrderedPropertyMap>(
            ((IReadOnlyDictionary<string, object?>)written!)["review"],
            "one command's fields");
    }

    // ── The required field ───────────────────────────────────────────────────

    /// <remarks>
    /// ⚠ Both "omit it" and "write an empty string" leave the entry schema-invalid, but only one of
    /// them lies about what the command does. The flag is what the user acts on.
    /// </remarks>
    [TestMethod]
    public void AMissingTemplate_IsReportedAndNotInvented()
    {
        OpenCodeCommandConfig command = OpenCodeCommandCodec.ReadCommand(
            CurrencyText.Parse("""{"description":"no body"}"""));

        Assert.IsTrue(command.IsTemplateMissing);
        Assert.IsNull(command.Template);

        var written = (IReadOnlyDictionary<string, object?>)
            OpenCodeCommandCodec.WriteCommand(command)!;

        Assert.IsFalse(
            written.ContainsKey("template"),
            "An empty template would claim the command's body is empty rather than missing.");
        StringAssert.Contains(CurrencyText.Render(written), "description");
    }

    [TestMethod]
    public void ABlankTemplate_CountsAsMissing()
    {
        OpenCodeCommandConfig command = new() { Template = "   " };

        Assert.IsTrue(
            command.IsTemplateMissing,
            "Whitespace is not a command body; treating it as present would hide the error.");
    }

    [TestMethod]
    public void AnOpaqueEntry_IsNotReportedAsMissingATemplate()
    {
        OpenCodeCommandConfig command = OpenCodeCommandCodec.ReadCommand("just a string");

        Assert.IsTrue(command.IsOpaque);
        Assert.IsFalse(
            command.IsTemplateMissing,
            "An entry that is not an object cannot be missing a field. Reporting it as invalid "
            + "would send the user looking for a template box that is not there.");
    }

    // ── ⭐ Shell interpolation is surfaced, not buried ───────────────────────

    /// <remarks>
    /// ⭐ A command template is not inert data: <c>!`…`</c> runs a shell command with the user's
    /// privileges every time the command is invoked, and a template copied from a shared config is
    /// exactly where nobody looks. Detected and reported only — nothing here executes anything.
    /// </remarks>
    [TestMethod]
    public void ShellInterpolationIsDetected()
    {
        Assert.IsTrue(OpenCodeCommandConfig.ContainsShellInterpolation("Diff: !`git diff`"));
        Assert.IsTrue(OpenCodeCommandConfig.ContainsShellInterpolation("!`ls`"));

        Assert.IsTrue(
            new OpenCodeCommandConfig { Template = "Check !`whoami` now" }.UsesShellInterpolation);
    }

    [TestMethod]
    public void OrdinaryTemplatesAreNotFlaggedAsShell()
    {
        foreach (string template in new[]
                 {
                     "Review $ARGUMENTS",
                     "Look at @file and $1",
                     "A backtick `code span` is not execution",
                     "An unclosed !`oops",
                     "",
                 })
        {
            Assert.IsFalse(
                OpenCodeCommandConfig.ContainsShellInterpolation(template),
                $"'{template}' is not shell interpolation, and crying wolf trains users to ignore "
                + "the warning that matters.");
        }

        Assert.IsFalse(OpenCodeCommandConfig.ContainsShellInterpolation(null));
    }

    // ── Wrong shapes and unknown fields ──────────────────────────────────────

    /// <remarks>
    /// The schema sets <c>additionalProperties: false</c>, so an unknown field is a violation — and
    /// a config from a newer OpenCode is the normal way to meet one. Stripping it on save is worse
    /// than round-tripping something this build cannot use.
    /// </remarks>
    [TestMethod]
    public void UnknownFieldsAndWrongShapesArePreserved()
    {
        AssertRoundTrips(
            """{"review":{"template":"a","futureField":{"nested":[1]}}}""",
            "An unknown field survives even though the schema forbids it.");

        AssertRoundTrips(
            """{"review":{"template":"a","subtask":"true"}}""",
            "A boolean written as a string cannot be read, and deleting it loses the only evidence "
            + "of the mistake.");

        AssertRoundTrips(
            """{"review":{"template":42}}""",
            "Nor can a numeric template — and that is exactly the entry the user must be able to see.");
    }

    [TestMethod]
    public void AWrongShapedTemplateStillCountsAsMissing()
    {
        OpenCodeCommandConfig command =
            OpenCodeCommandCodec.ReadCommand(CurrencyText.Parse("""{"template":42}"""));

        Assert.IsTrue(
            command.IsTemplateMissing,
            "It is unreadable, so as far as this editor is concerned there is no template — while "
            + "the original value still round-trips through Extras.");
        Assert.IsTrue(command.Extras.Any(e => e.Key == "template"));
    }

    [TestMethod]
    public void AnEntryThatIsNotAnObject_IsHeldVerbatim()
    {
        AssertRoundTrips(
            """{"weird":"a string","alsoWeird":[1,2],"nope":null}""",
            "Nothing about these is editable, and nothing about them should be destroyed.");
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void WriteMap_IsNullWhenThereAreNoCommands()
    {
        Assert.IsNull(
            OpenCodeCommandCodec.WriteMap([]),
            "An empty object would persist a command key that configures nothing.");
    }

    [TestMethod]
    public void WriteMap_SkipsABlankCommandName()
    {
        Assert.IsNull(OpenCodeCommandCodec.WriteMap([
            new KeyValuePair<string, OpenCodeCommandConfig>(
                "  ",
                new OpenCodeCommandConfig { Template = "a" }),
        ]));
    }

    /// <remarks>
    /// ⚠ Deliberately different from an empty AGENT entry, which round-trips as <c>{}</c> because
    /// no agent field is required. An empty command entry cannot be valid, so writing it would put
    /// a schema violation in the user's config from a half-finished click — while any single field
    /// present is enough to write the entry, so nothing they typed is withheld.
    /// </remarks>
    [TestMethod]
    public void AnEntirelyEmptyCommandEntry_IsNotWritten()
    {
        Assert.IsNull(OpenCodeCommandCodec.WriteMap([
            new KeyValuePair<string, OpenCodeCommandConfig>("review", new OpenCodeCommandConfig()),
        ]));

        object? withOneField = OpenCodeCommandCodec.WriteMap([
            new KeyValuePair<string, OpenCodeCommandConfig>(
                "review",
                new OpenCodeCommandConfig { Description = "no body yet" }),
        ]);

        Assert.IsNotNull(
            withOneField,
            "One field is enough — the entry is still invalid, but it holds something the user "
            + "typed and that must not be thrown away.");
    }

    [TestMethod]
    public void ReadMap_TreatsANonObjectValueAsNothingToEdit()
    {
        Assert.AreEqual(0, OpenCodeCommandCodec.ReadMap(null).Count);
        Assert.AreEqual(0, OpenCodeCommandCodec.ReadMap("nonsense").Count);
    }

    [TestMethod]
    public void AClearedTextBoxIsNotWrittenAsAnEmptyString()
    {
        var written = (IReadOnlyDictionary<string, object?>)OpenCodeCommandCodec.WriteCommand(
            new OpenCodeCommandConfig { Template = "a", Model = "   " })!;

        Assert.IsFalse(
            written.ContainsKey("model"),
            "An empty model overrides the inherited one with nothing, which is a setting rather "
            + "than the absence of one.");
    }
}
