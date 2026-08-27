using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Tooling;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>formatter</c> and <c>lsp</c> values: a four-state mode over a per-language map.
/// </summary>
/// <remarks>
/// <para>
/// The property doing the most work here is that <b>the four modes stay apart</b>. Absent and
/// <c>false</c> have the same effect; <c>{}</c> and <c>true</c> have the same effect. Neither pair
/// has the same text, so a codec that normalises either one rewrites a file it was only asked to
/// read.
/// </para>
/// <para>
/// The second is that <b><c>formatter</c> and <c>lsp</c> entries are different shapes.</b> The plan
/// describes them as one; the schema does not. The drift guards at the bottom of this file read the
/// bundled schema directly, so a Phase 13 refresh that changes either shape fails here rather than
/// silently handing one key an editor built for the other.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeToolingCodecTests
{
    // ── Mode: the four states, and the two folds that must not happen ──────────

    [TestMethod]
    public void AnAbsentKey_IsNotSet_AndWritesNothing()
    {
        OpenCodeFormatterConfig formatter = OpenCodeToolingCodec.ReadFormatter(null, isDefined: false);
        OpenCodeLspConfig lsp = OpenCodeToolingCodec.ReadLsp(null, isDefined: false);

        Assert.AreEqual(OpenCodeToolingMode.NotSet, formatter.Mode);
        Assert.AreEqual(OpenCodeToolingMode.NotSet, lsp.Mode);
        Assert.IsNull(OpenCodeToolingCodec.WriteFormatter(formatter));
        Assert.IsNull(OpenCodeToolingCodec.WriteLsp(lsp));
    }

    [TestMethod]
    public void TheLiteralFalse_IsDisabled_AndRoundTrips()
    {
        OpenCodeFormatterConfig formatter = OpenCodeToolingCodec.ReadFormatter(false, isDefined: true);
        OpenCodeLspConfig lsp = OpenCodeToolingCodec.ReadLsp(false, isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.Disabled, formatter.Mode);
        Assert.AreEqual(OpenCodeToolingMode.Disabled, lsp.Mode);
        Assert.AreEqual(false, OpenCodeToolingCodec.WriteFormatter(formatter));
        Assert.AreEqual(false, OpenCodeToolingCodec.WriteLsp(lsp));
    }

    [TestMethod]
    public void TheLiteralTrue_IsBuiltIns_AndRoundTrips()
    {
        OpenCodeFormatterConfig formatter = OpenCodeToolingCodec.ReadFormatter(true, isDefined: true);
        OpenCodeLspConfig lsp = OpenCodeToolingCodec.ReadLsp(true, isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.BuiltIns, formatter.Mode);
        Assert.AreEqual(OpenCodeToolingMode.BuiltIns, lsp.Mode);
        Assert.AreEqual(true, OpenCodeToolingCodec.WriteFormatter(formatter));
        Assert.AreEqual(true, OpenCodeToolingCodec.WriteLsp(lsp));
    }

    /// <remarks>
    /// ⚠ The first of the two folds this codec must never perform. An empty object and
    /// <c>true</c> have the same effect — built-ins on, no overrides — so the temptation is to
    /// normalise one into the other. They are different text, and the user wrote one of them.
    /// </remarks>
    [TestMethod]
    public void AnEmptyObject_IsConfigured_AndStaysAnEmptyObject()
    {
        OpenCodeFormatterConfig formatter =
            OpenCodeToolingCodec.ReadFormatter(CurrencyText.Parse("{}"), isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.Configured, formatter.Mode);
        Assert.AreEqual(0, formatter.Entries.Count);

        object? written = OpenCodeToolingCodec.WriteFormatter(formatter);
        Assert.IsNotNull(
            written,
            "An empty configured object collapsed to a key removal. Absent means the subsystem is "
            + "off and {} means built-ins are on, so this undoes the only thing the mode did.");
        Assert.AreNotEqual(
            true,
            written,
            "An empty configured object was normalised to `true`. Same effect, different text — "
            + "which makes opening the page an unrequested diff.");
        Assert.AreEqual("{}", CurrencyText.Render(written));
    }

    /// <remarks>
    /// ⚠ The second fold. The schema's own description says omitting the key and setting it to
    /// <c>false</c> both disable the subsystem, which is exactly why a codec is tempted to treat
    /// them as one state.
    /// </remarks>
    [TestMethod]
    public void AbsentAndFalse_AreDifferentModes()
    {
        OpenCodeFormatterConfig absent = OpenCodeToolingCodec.ReadFormatter(null, isDefined: false);
        OpenCodeFormatterConfig off = OpenCodeToolingCodec.ReadFormatter(false, isDefined: true);

        Assert.AreNotEqual(absent.Mode, off.Mode);
        Assert.IsNull(OpenCodeToolingCodec.WriteFormatter(absent));
        Assert.AreEqual(false, OpenCodeToolingCodec.WriteFormatter(off));
    }

    [TestMethod]
    public void AValueMatchingNeitherArm_IsHeldVerbatim()
    {
        OpenCodeFormatterConfig formatter =
            OpenCodeToolingCodec.ReadFormatter("prettier", isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.Unrecognised, formatter.Mode);
        Assert.AreEqual("prettier", formatter.Raw);
        Assert.AreEqual("prettier", OpenCodeToolingCodec.WriteFormatter(formatter));

        OpenCodeLspConfig lsp = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("[1,2]"), isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.Unrecognised, lsp.Mode);
        Assert.AreEqual("[1,2]", CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(lsp)));
    }

    /// <remarks>
    /// ⚠ Pins a real limitation rather than a behaviour: the value currency uses
    /// <see langword="null"/> to mean "remove the key", so <c>"formatter": null</c> cannot survive
    /// a round trip. It is schema-invalid either way — <c>anyOf: [boolean, object]</c> admits
    /// neither — so nothing valid is lost, but the collapse is real and belongs in a test rather
    /// than in a hope.
    /// </remarks>
    [TestMethod]
    public void AnExplicitNull_ReadsAsUnrecognised_AndCannotBeWrittenBack()
    {
        OpenCodeFormatterConfig formatter = OpenCodeToolingCodec.ReadFormatter(null, isDefined: true);

        Assert.AreEqual(OpenCodeToolingMode.Unrecognised, formatter.Mode);
        Assert.IsNull(OpenCodeToolingCodec.WriteFormatter(formatter));
    }

    /// <remarks>
    /// The preserve-the-other-arm rule at model level: the entries ride alongside the mode rather
    /// than inside it, so a user who flips to "built-ins only" and back has not lost their
    /// overrides. Fourth place in this phase it has mattered.
    /// </remarks>
    [TestMethod]
    public void ChangingModeAwayFromConfigured_KeepsTheEntriesForTheWayBack()
    {
        OpenCodeFormatterConfig loaded = OpenCodeToolingCodec.ReadFormatter(
            CurrencyText.Parse("""{"prettier":{"command":["prettier","--write"]}}"""),
            isDefined: true);

        OpenCodeFormatterConfig switched = loaded with { Mode = OpenCodeToolingMode.BuiltIns };

        Assert.AreEqual(true, OpenCodeToolingCodec.WriteFormatter(switched));
        Assert.AreEqual(1, switched.Entries.Count, "The overrides were dropped by the mode switch.");

        OpenCodeFormatterConfig back = switched with { Mode = OpenCodeToolingMode.Configured };
        Assert.AreEqual(
            """{prettier:{command:[prettier,--write]}}""",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(back)));
    }

    // ── formatter entries ──────────────────────────────────────────────────────

    [TestMethod]
    public void AFullFormatterEntry_RoundTripsWithEveryField()
    {
        const string json = """
            {
              "prettier": {
                "disabled": false,
                "command": ["prettier", "--write", "$FILE"],
                "environment": { "NODE_ENV": "production", "A": "b" },
                "extensions": [".ts", ".tsx"]
              }
            }
            """;

        OpenCodeFormatterConfig config =
            OpenCodeToolingCodec.ReadFormatter(CurrencyText.Parse(json), isDefined: true);

        OpenCodeFormatterEntry entry = config.Entries.Single();
        Assert.AreEqual("prettier", entry.Name);
        Assert.AreEqual(false, entry.Disabled);
        CollectionAssert.AreEqual(
            new[] { "prettier", "--write", "$FILE" }, entry.Command.ToArray());
        CollectionAssert.AreEqual(new[] { ".ts", ".tsx" }, entry.Extensions.ToArray());
        Assert.AreEqual(0, entry.Extras.Count);

        Assert.AreEqual(
            "{prettier:{disabled:false,command:[prettier,--write,$FILE],"
            + "environment:{NODE_ENV:production,A:b},extensions:[.ts,.tsx]}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)));
    }

    /// <remarks>
    /// ⛔ <b>The name guard.</b> A formatter entry's environment key is <c>environment</c>; an
    /// <c>lsp</c> entry's is <c>env</c>. Both entry objects set <c>additionalProperties: false</c>,
    /// so borrowing one name for the other produces a config OpenCode rejects — and the plan
    /// describes both keys with the same field list, which is how that mistake gets made.
    /// </remarks>
    [TestMethod]
    public void AFormatterEntryUsesEnvironment_AndDoesNotAnswerToEnv()
    {
        OpenCodeFormatterConfig config = OpenCodeToolingCodec.ReadFormatter(
            CurrencyText.Parse("""{"gofmt":{"env":{"GOFLAGS":"-mod=mod"}}}"""),
            isDefined: true);

        OpenCodeFormatterEntry entry = config.Entries.Single();
        Assert.AreEqual(
            0,
            entry.Environment.Count,
            "`env` was read as a formatter entry's environment map. That key belongs to lsp; "
            + "reading it here would move a user's value onto a key the schema forbids.");
        Assert.AreEqual("env", entry.Extras.Single().Key);

        // Preserved, not silently renamed: the value survives the round trip under its own key.
        Assert.AreEqual(
            "{gofmt:{env:{GOFLAGS:-mod=mod}}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)));
    }

    /// <remarks>
    /// One uniform rule, learned in 9a-5: a surfaced key whose value is the wrong shape goes to
    /// <c>Extras</c>. Without it each of these reads as absent and then vanishes on save — and each
    /// is already schema-invalid, i.e. exactly when the user needs to see it.
    /// </remarks>
    [TestMethod]
    public void SurfacedKeysWithTheWrongShape_GoToExtras_AndSurviveTheRoundTrip()
    {
        const string json = """
            {
              "odd": {
                "disabled": "yes",
                "command": ["ok", 7],
                "environment": { "N": 1 },
                "extensions": "not-an-array"
              }
            }
            """;

        OpenCodeFormatterConfig config =
            OpenCodeToolingCodec.ReadFormatter(CurrencyText.Parse(json), isDefined: true);

        OpenCodeFormatterEntry entry = config.Entries.Single();
        Assert.IsNull(entry.Disabled);
        Assert.AreEqual(0, entry.Command.Count);
        Assert.AreEqual(0, entry.Environment.Count);
        Assert.AreEqual(0, entry.Extensions.Count);
        Assert.AreEqual(4, entry.Extras.Count);

        Assert.AreEqual(
            "{odd:{disabled:yes,command:[ok,7],environment:{N:1},extensions:not-an-array}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)));
    }

    [TestMethod]
    public void AnEmptyFormatterEntry_WritesAnEmptyObject()
    {
        OpenCodeFormatterConfig config = new()
        {
            Mode = OpenCodeToolingMode.Configured,
            Entries = [new OpenCodeFormatterEntry { Name = "prettier" }],
        };

        Assert.AreEqual(
            "{prettier:{}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)),
            "A formatter entry requires nothing, so an empty one is legal and the user added it "
            + "deliberately. Contrast the lsp case, where {} matches no arm.");
    }

    /// <remarks>
    /// Per-entry granularity, 9a-3's pattern: one unreadable entry must not freeze its neighbours.
    /// Whole-value echo would make a single newer-OpenCode entry lock the rest of the map.
    /// </remarks>
    [TestMethod]
    public void ANonObjectFormatterEntry_IsHeldVerbatim_AndItsNeighboursStayEditable()
    {
        OpenCodeFormatterConfig config = OpenCodeToolingCodec.ReadFormatter(
            CurrencyText.Parse("""{"weird":"prettier","gofmt":{"disabled":true}}"""),
            isDefined: true);

        Assert.IsTrue(config.Entries[0].IsOpaque);
        Assert.IsFalse(config.Entries[1].IsOpaque);
        Assert.AreEqual(true, config.Entries[1].Disabled);

        Assert.AreEqual(
            "{weird:prettier,gofmt:{disabled:true}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)));
    }

    [TestMethod]
    public void EntryOrderAndEnvironmentOrder_AreBothPreserved()
    {
        const string json = """
            {
              "zeta":  { "environment": { "Z": "1", "A": "2", "M": "3" } },
              "alpha": { "environment": { "Q": "4" } }
            }
            """;

        OpenCodeFormatterConfig config =
            OpenCodeToolingCodec.ReadFormatter(CurrencyText.Parse(json), isDefined: true);

        Assert.AreEqual(
            "{zeta:{environment:{Z:1,A:2,M:3}},alpha:{environment:{Q:4}}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)),
            "Something sorted. Rewriting a user's config with keys reshuffled turns a one-line "
            + "change into an unreviewable diff.");
    }

    [TestMethod]
    public void ABlankEntryName_IsSkipped()
    {
        OpenCodeFormatterConfig config = new()
        {
            Mode = OpenCodeToolingMode.Configured,
            Entries =
            [
                new OpenCodeFormatterEntry { Name = "   " },
                new OpenCodeFormatterEntry { Name = "gofmt", Disabled = true },
            ],
        };

        Assert.AreEqual(
            "{gofmt:{disabled:true}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteFormatter(config)));
    }

    /// <remarks>
    /// ⭐ Asserts the TYPE at every level, not the behaviour. A 9a-2 canary proved that swapping
    /// only the inner map leaves every behavioural order test green, so no behavioural assertion
    /// can hold this guarantee.
    /// </remarks>
    [TestMethod]
    public void EveryMapLevel_IsAnOrderedPropertyMap()
    {
        OpenCodeFormatterConfig config = OpenCodeToolingCodec.ReadFormatter(
            CurrencyText.Parse("""{"p":{"environment":{"A":"b"}}}"""), isDefined: true);

        object? written = OpenCodeToolingCodec.WriteFormatter(config);

        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "The outer map lost its ordering.");
        object? entry = ((OrderedPropertyMap)written!)["p"];
        Assert.IsInstanceOfType<OrderedPropertyMap>(entry, "An entry map lost its ordering.");
        Assert.IsInstanceOfType<OrderedPropertyMap>(
            ((OrderedPropertyMap)entry!)["environment"],
            "The environment map lost its ordering — the level a canary proved no behavioural "
            + "test covers.");
    }

    // ── lsp entries: the two arms ──────────────────────────────────────────────

    [TestMethod]
    public void ADisableOnlyLspEntry_RoundTripsAsExactlyDisabledTrue()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"gopls":{"disabled":true}}"""), isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        Assert.IsTrue(entry.IsDisableOnly);
        Assert.IsFalse(entry.IsIncomplete);
        Assert.IsFalse(entry.IsEmpty);

        Assert.AreEqual(
            "{gopls:{disabled:true}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    [TestMethod]
    public void AFullLspEntry_RoundTripsWithEveryField()
    {
        const string json = """
            {
              "pyright": {
                "command": ["pyright-langserver", "--stdio"],
                "extensions": [".py", ".pyi"],
                "disabled": false,
                "env": { "PYTHONPATH": "/src", "B": "c" },
                "initialization": { "settings": { "strict": true } }
              }
            }
            """;

        OpenCodeLspConfig config =
            OpenCodeToolingCodec.ReadLsp(CurrencyText.Parse(json), isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        CollectionAssert.AreEqual(
            new[] { "pyright-langserver", "--stdio" }, entry.Command.ToArray());
        Assert.AreEqual(false, entry.Disabled);
        Assert.IsNotNull(entry.Initialization);
        Assert.IsFalse(entry.IsIncomplete);
        Assert.AreEqual(0, entry.Extras.Count);

        Assert.AreEqual(
            "{pyright:{command:[pyright-langserver,--stdio],extensions:[.py,.pyi],disabled:false,"
            + "env:{PYTHONPATH:/src,B:c},initialization:{settings:{strict:true}}}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    /// <remarks>
    /// ⛔ The mirror of the formatter name guard, and the direction that actually bites: an
    /// <c>lsp</c> entry says <c>env</c>.
    /// </remarks>
    [TestMethod]
    public void AnLspEntryUsesEnv_AndDoesNotAnswerToEnvironment()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse(
                """{"gopls":{"command":["gopls"],"environment":{"GOFLAGS":"-mod=mod"}}}"""),
            isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        Assert.AreEqual(
            0,
            entry.Env.Count,
            "`environment` was read as an lsp entry's env map. That key belongs to formatter.");
        Assert.AreEqual("environment", entry.Extras.Single().Key);

        Assert.AreEqual(
            "{gopls:{command:[gopls],environment:{GOFLAGS:-mod=mod}}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    /// <remarks>
    /// ⚠⚠ <b>The state one obvious click produces.</b> Unticking "disabled" on a disable-only
    /// entry leaves <c>{ "disabled": false }</c>, which matches neither arm: the first requires the
    /// literal <c>true</c>, the second requires a command. It is reported, not repaired — inventing
    /// a command would be a claim about the user's machine.
    /// </remarks>
    [TestMethod]
    public void DisabledFalseWithNoCommand_IsIncomplete_AndIsStillWritten()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"gopls":{"disabled":false}}"""), isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        Assert.IsTrue(
            entry.IsIncomplete,
            "The entry matches neither arm and nothing said so, so the editor has nothing to warn "
            + "with and the user learns from a rejected config.");
        Assert.IsFalse(entry.IsDisableOnly);

        Assert.AreEqual(
            "{gopls:{disabled:false}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)),
            "What the user typed was withheld, which is the worse failure of the two.");
    }

    [TestMethod]
    public void ExtensionsWithoutACommand_IsIncomplete()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"gopls":{"extensions":[".go"]}}"""), isDefined: true);

        Assert.IsTrue(config.Entries.Single().IsIncomplete);
    }

    /// <remarks>
    /// The disable-only arm sets <c>additionalProperties: false</c>, so <c>disabled: true</c> plus
    /// any companion field matches neither arm either. Easy to miss, because the entry looks like
    /// the common disable case with one harmless extra.
    /// </remarks>
    [TestMethod]
    public void DisabledTrueWithACompanionField_IsIncomplete()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"gopls":{"disabled":true,"extensions":[".go"]}}"""),
            isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        Assert.IsFalse(entry.IsDisableOnly);
        Assert.IsTrue(entry.IsIncomplete);
    }

    /// <remarks>
    /// Deliberately unlike the formatter case above: <c>{}</c> matches no <c>lsp</c> arm, so
    /// writing one would persist a schema violation produced by a half-finished click. The command
    /// editor made the same call for the same reason.
    /// </remarks>
    [TestMethod]
    public void AnEmptyLspEntry_IsNotWritten()
    {
        OpenCodeLspConfig config = new()
        {
            Mode = OpenCodeToolingMode.Configured,
            Entries =
            [
                new OpenCodeLspEntry { Name = "gopls" },
                new OpenCodeLspEntry { Name = "pyright", Disabled = true },
            ],
        };

        Assert.IsTrue(config.Entries[0].IsEmpty);
        Assert.AreEqual(
            "{pyright:{disabled:true}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    [TestMethod]
    public void ANonObjectInitialization_GoesToExtras()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"gopls":{"command":["gopls"],"initialization":"nope"}}"""),
            isDefined: true);

        OpenCodeLspEntry entry = config.Entries.Single();
        Assert.IsNull(entry.Initialization);
        Assert.AreEqual("initialization", entry.Extras.Single().Key);
        Assert.AreEqual(
            "{gopls:{command:[gopls],initialization:nope}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    [TestMethod]
    public void LspArgvOrder_IsPreserved()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"x":{"command":["a","-y","b"]}}"""), isDefined: true);

        Assert.AreEqual(
            "{x:{command:[a,-y,b]}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)),
            "This is argv: [a,-y,b] is not [-y,a,b].");
    }

    [TestMethod]
    public void ANonObjectLspEntry_IsHeldVerbatim()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"weird":42,"gopls":{"disabled":true}}"""), isDefined: true);

        Assert.IsTrue(config.Entries[0].IsOpaque);
        Assert.IsFalse(config.Entries[0].IsEmpty, "An opaque entry must never be mistaken for an "
            + "empty one — WriteLsp skips empties, so that would delete it.");
        Assert.AreEqual(
            "{weird:42,gopls:{disabled:true}}",
            CurrencyText.Render(OpenCodeToolingCodec.WriteLsp(config)));
    }

    [TestMethod]
    public void EveryLspMapLevel_IsAnOrderedPropertyMap()
    {
        OpenCodeLspConfig config = OpenCodeToolingCodec.ReadLsp(
            CurrencyText.Parse("""{"g":{"command":["g"],"env":{"A":"b"}}}"""), isDefined: true);

        object? written = OpenCodeToolingCodec.WriteLsp(config);

        Assert.IsInstanceOfType<OrderedPropertyMap>(written);
        object? entry = ((OrderedPropertyMap)written!)["g"];
        Assert.IsInstanceOfType<OrderedPropertyMap>(entry);
        Assert.IsInstanceOfType<OrderedPropertyMap>(((OrderedPropertyMap)entry!)["env"]);
    }

    // ── Schema drift: the shapes this codec was built against ──────────────────

    private static JsonDocument Schema()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", "opencode-config.json");
            if (File.Exists(candidate))
            {
                return JsonDocument.Parse(File.ReadAllText(candidate));
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the bundled OpenCode schema from '{AppContext.BaseDirectory}'.");
    }

    private static JsonElement TopLevel(JsonDocument doc, string name) =>
        doc.RootElement.GetProperty("$defs").GetProperty("Config").GetProperty("properties")
            .GetProperty(name);

    /// <summary>
    /// The <c>lsp</c> entry arm that declares a <c>command</c> property.
    /// </summary>
    /// <remarks>
    /// Selected by what it declares rather than by position, so a reordered union does not read as
    /// a shape change. <b>Declaring</b> <c>command</c> and <b>requiring</b> it are different
    /// claims, so the tests asserting <c>required</c> against this arm are not tautologies.
    /// </remarks>
    private static JsonElement LspFullArm(JsonDocument doc) => LspArm(doc, declaresCommand: true);

    /// <summary>The <c>lsp</c> entry arm that does not declare a <c>command</c> property.</summary>
    private static JsonElement LspDisableOnlyArm(JsonDocument doc) =>
        LspArm(doc, declaresCommand: false);

    private static JsonElement LspArm(JsonDocument doc, bool declaresCommand)
    {
        JsonElement arms = TopLevel(doc, "lsp").GetProperty("anyOf")[1]
            .GetProperty("additionalProperties").GetProperty("anyOf");

        foreach (JsonElement arm in arms.EnumerateArray())
        {
            if (arm.GetProperty("properties").TryGetProperty("command", out _) == declaresCommand)
            {
                return arm;
            }
        }

        throw new InvalidOperationException(
            $"No lsp entry arm {(declaresCommand ? "declares" : "omits")} `command`. The union's "
            + "shape changed, so these tests are misreading the schema rather than reporting drift.");
    }

    [TestMethod]
    public void BothKeys_AreStillBooleanOrObject()
    {
        using JsonDocument doc = Schema();

        foreach (string name in new[] { "formatter", "lsp" })
        {
            JsonElement arms = TopLevel(doc, name).GetProperty("anyOf");
            Assert.AreEqual(
                2,
                arms.GetArrayLength(),
                $"`{name}` no longer has exactly two arms, so the four-state mode this codec "
                + "implements is reading a shape that changed underneath it.");
            Assert.AreEqual("boolean", arms[0].GetProperty("type").GetString());
            Assert.AreEqual("object", arms[1].GetProperty("type").GetString());
        }
    }

    /// <remarks>
    /// ⛔ The drift guard for the plan's error. If a refresh ever unifies these two key names, the
    /// codec's two separate fields become pointless — but until then, sharing them is a bug.
    /// </remarks>
    [TestMethod]
    public void FormatterEntriesSayEnvironment_AndLspEntriesSayEnv()
    {
        using JsonDocument doc = Schema();

        JsonElement formatterEntry =
            TopLevel(doc, "formatter").GetProperty("anyOf")[1].GetProperty("additionalProperties");
        JsonElement formatterProps = formatterEntry.GetProperty("properties");

        Assert.IsTrue(
            formatterProps.TryGetProperty("environment", out _),
            "A formatter entry stopped declaring `environment`.");
        Assert.IsFalse(
            formatterProps.TryGetProperty("env", out _),
            "A formatter entry now declares `env` too. The codec routes that key to Extras, so it "
            + "would preserve rather than surface a field the schema recognises.");
        Assert.IsFalse(
            formatterEntry.GetProperty("additionalProperties").GetBoolean(),
            "A formatter entry stopped forbidding additional properties, which is the reason "
            + "borrowing the other key's name is a rejection rather than a curiosity.");

        JsonElement lspFull = LspFullArm(doc);
        JsonElement lspProps = lspFull.GetProperty("properties");

        Assert.IsTrue(
            lspProps.TryGetProperty("env", out _), "An lsp entry stopped declaring `env`.");
        Assert.IsFalse(
            lspProps.TryGetProperty("environment", out _),
            "An lsp entry now declares `environment` too.");
    }

    /// <remarks>
    /// The three-way difference the plan missed entirely. Each assertion here is a behaviour the
    /// codec relies on: two arms, a literal-<c>true</c>-only disable arm, and a required command.
    /// </remarks>
    [TestMethod]
    public void AnLspEntry_IsTwoArms_WithALiteralTrueDisableArmAndARequiredCommand()
    {
        using JsonDocument doc = Schema();

        JsonElement entry =
            TopLevel(doc, "lsp").GetProperty("anyOf")[1].GetProperty("additionalProperties");

        Assert.IsTrue(
            entry.TryGetProperty("anyOf", out JsonElement arms),
            "An lsp entry is no longer a union. This codec classifies each entry into one of two "
            + "arms and reports the entries that match neither; a single shape would make every "
            + "IsIncomplete verdict wrong.");
        Assert.AreEqual(2, arms.GetArrayLength());

        JsonElement disableOnly = LspDisableOnlyArm(doc);
        Assert.AreEqual(
            "disabled", disableOnly.GetProperty("required")[0].GetString());
        Assert.AreEqual(
            1,
            disableOnly.GetProperty("properties").GetProperty("disabled")
                .GetProperty("enum").GetArrayLength(),
            "The disable-only arm's `disabled` stopped being a single-value enum.");
        Assert.IsTrue(
            disableOnly.GetProperty("properties").GetProperty("disabled")
                .GetProperty("enum")[0].GetBoolean(),
            "The disable-only arm now permits `false`, which would make "
            + "{ \"disabled\": false } valid — and IsIncomplete reports it as invalid.");
        Assert.IsFalse(disableOnly.GetProperty("additionalProperties").GetBoolean());

        JsonElement full = LspFullArm(doc);
        Assert.AreEqual(
            "command",
            full.GetProperty("required")[0].GetString(),
            "`command` stopped being required by the lsp full arm, so every IsIncomplete warning "
            + "this editor shows is now a false alarm.");
        Assert.IsTrue(
            full.GetProperty("properties").TryGetProperty("initialization", out JsonElement init),
            "An lsp entry stopped declaring `initialization`.");
        Assert.IsFalse(
            init.TryGetProperty("properties", out _),
            "`initialization` gained declared properties, so the editor's raw-JSON box is no "
            + "longer the honest control for it — there is now a shape to render fields for.");
    }

    [TestMethod]
    public void AFormatterEntry_RequiresNothing()
    {
        using JsonDocument doc = Schema();

        JsonElement entry =
            TopLevel(doc, "formatter").GetProperty("anyOf")[1].GetProperty("additionalProperties");

        Assert.IsFalse(
            entry.TryGetProperty("required", out _),
            "A formatter entry gained a required field. An empty one is written as {} on the "
            + "strength of nothing being required, which would now be a schema violation.");
    }

    /// <remarks>
    /// ⚠⚠ <b>The name collision.</b> Specialised editors are registered by property NAME,
    /// path-insensitively — deliberately, because that is what lets an agent's nested
    /// <c>permission</c> reuse the permission grid. But <c>PermissionConfig</c>'s object arm also
    /// has a property called <c>lsp</c>: the permission action for the <c>lsp</c> <i>tool</i>,
    /// which is a rule config and nothing like a map of language servers. This asserts the two stay
    /// different, so if a refresh ever makes them look alike, the collision is reported here rather
    /// than discovered as a permission node rendered by a server editor.
    /// </remarks>
    [TestMethod]
    public void PermissionsInnerLsp_IsARuleConfig_NotAServerMap()
    {
        using JsonDocument doc = Schema();

        JsonElement permissionObjectArm = doc.RootElement
            .GetProperty("$defs").GetProperty("PermissionConfig")
            .GetProperty("anyOf")[1];

        Assert.IsTrue(
            permissionObjectArm.GetProperty("properties").TryGetProperty(
                "lsp", out JsonElement permissionLsp),
            "PermissionConfig stopped declaring an `lsp` tool. Harmless in itself — but this test "
            + "is the record of why the name collision is safe, so it must not silently stop "
            + "checking anything.");

        Assert.AreEqual(
            "#/$defs/PermissionRuleConfig",
            permissionLsp.GetProperty("$ref").GetString(),
            "The `lsp` inside a permission map is no longer a plain rule-config $ref. Editors are "
            + "registered by property name, so a shape change here is how a permission entry ends "
            + "up in front of the language-server editor.");

        Assert.IsFalse(
            TopLevel(doc, "lsp").TryGetProperty("$ref", out _),
            "Config.lsp became a $ref. If it ever becomes the same $ref as the permission tool, "
            + "one name would mean two shapes and name-based registration would have to go.");
    }
}
