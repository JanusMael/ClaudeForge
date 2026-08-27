using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Agents;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>agent</c> map: fifteen optional fields, where "absent" and "default" are different
/// claims.
/// </summary>
/// <remarks>
/// An agent entry is usually a <b>partial override</b> of one of the seven built-ins, so the
/// property that matters most is that a field the user never set stays absent. Writing
/// <c>"temperature": 0</c> for an untouched box does not preserve their config — it overrides the
/// model's own default with a real setting.
/// </remarks>
[TestClass]
public sealed class OpenCodeAgentCodecTests
{
    private static object? RoundTrip(string json) =>
        OpenCodeAgentCodec.WriteMap(OpenCodeAgentCodec.ReadMap(CurrencyText.Parse(json)));

    private static void AssertRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(RoundTrip(json)),
            because);

    // ── Round trips ──────────────────────────────────────────────────────────

    [TestMethod]
    public void AFullyPopulatedAgent_RoundTripsEveryField()
    {
        AssertRoundTrips(
            """
            {"build":{"model":"anthropic/claude","variant":"fast","temperature":0.3,"top_p":0.9,
            "prompt":"You build things.","tools":{"bash":true,"edit":false},"disable":false,
            "description":"Builder","mode":"primary","hidden":false,
            "options":{"anything":[1,2,{"deep":true}]},"color":"#ff8800","steps":12,"maxSteps":40,
            "permission":{"bash":{"git *":"allow"}}}}
            """,
            "All fifteen fields are surfaced, so nothing here should need the opaque path.");
    }

    [TestMethod]
    public void APartialOverride_StaysPartial()
    {
        AssertRoundTrips(
            """{"build":{"model":"anthropic/claude"}}""",
            "An override of one field must not acquire the other fourteen. Every absent key stays "
            + "absent, or the agent stops inheriting everything the user left alone.");
    }

    [TestMethod]
    public void AnEmptyAgentEntry_RoundTripsAsEmpty()
    {
        AssertRoundTrips(
            """{"build":{}}""",
            "An empty object is a legal entry meaning 'no overrides'; it must not become null and "
            + "disappear.");
    }

    [TestMethod]
    public void AgentOrderIsPreserved()
    {
        object? written = RoundTrip(
            """{"zeta":{"model":"m"},"alpha":{"model":"n"},"build":{"model":"o"}}""");

        CollectionAssert.AreEqual(
            new[] { "zeta", "alpha", "build" },
            ((IReadOnlyDictionary<string, object?>)written!).Keys.ToArray(),
            "Agent order carries no meaning, but alphabetising a user's config turns a one-line "
            + "change into a whole-file diff.");
    }

    [TestMethod]
    public void EveryEmittedMapIsAnOrderedMap_AtEveryLevel()
    {
        object? written = RoundTrip(
            """{"build":{"tools":{"bash":true},"permission":{"bash":"ask"}}}""");

        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "the agent map");

        var agent = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)written!)["build"]!;
        Assert.IsInstanceOfType<OrderedPropertyMap>(agent, "one agent's fields");
        Assert.IsInstanceOfType<OrderedPropertyMap>(agent["tools"], "the tools map");
    }

    // ── Absent vs default ────────────────────────────────────────────────────

    [TestMethod]
    public void UnsetNumbersAndBooleansAreOmitted_NotWrittenAsZeroOrFalse()
    {
        object? written = OpenCodeAgentCodec.WriteAgent(new OpenCodeAgentConfig
        {
            Model = "m",
        });

        var agent = (IReadOnlyDictionary<string, object?>)written!;

        foreach (string key in new[]
                 {
                     "temperature", "top_p", "steps", "maxSteps", "disable", "hidden", "mode",
                     "prompt", "variant", "description", "color", "tools", "options", "permission",
                 })
        {
            Assert.IsFalse(
                agent.ContainsKey(key),
                $"'{key}' was written for an agent that only set a model. A default written as a "
                + "value overrides whatever the agent would otherwise inherit.");
        }
    }

    [TestMethod]
    public void AClearedTextBoxIsNotWrittenAsAnEmptyString()
    {
        object? written = OpenCodeAgentCodec.WriteAgent(new OpenCodeAgentConfig { Model = "   " });

        Assert.IsFalse(
            ((IReadOnlyDictionary<string, object?>)written!).ContainsKey("model"),
            "An empty model is a setting, not the absence of one — it would override the inherited "
            + "model with nothing.");
    }

    [TestMethod]
    public void ATemperatureOfZero_IsWritten()
    {
        object? written = OpenCodeAgentCodec.WriteAgent(
            new OpenCodeAgentConfig { Temperature = 0d });

        var agent = (IReadOnlyDictionary<string, object?>)written!;

        Assert.IsTrue(
            agent.ContainsKey("temperature"),
            "Zero is a deliberate and meaningful temperature. Omitting it because it looks like a "
            + "default is the mirror-image bug of writing defaults.");
        Assert.AreEqual(0d, agent["temperature"]);
    }

    /// <remarks>
    /// <c>1</c> and <c>1.0</c> are both legal JSON for the same number, and a currency conversion
    /// may hand over either. Reading only one silently blanks the user's setting.
    /// </remarks>
    [TestMethod]
    public void AnIntegralTemperature_IsStillRead()
    {
        OpenCodeAgentConfig agent = OpenCodeAgentCodec.ReadAgent(CurrencyText.Map([
            new KeyValuePair<string, object?>("temperature", 1L),
            new KeyValuePair<string, object?>("steps", 5.0),
        ]));

        Assert.AreEqual(1d, agent.Temperature);
        Assert.AreEqual(5L, agent.Steps);
    }

    // ── ⭐ Wrong-shaped values are preserved, not deleted ────────────────────

    /// <remarks>
    /// ⭐ Each of these is invalid per the schema, which is exactly when the user needs to see it
    /// rather than have the editor quietly delete it on the next save. Before the uniform
    /// wrong-shape rule, every one of them read as null and then vanished.
    /// </remarks>
    [TestMethod]
    public void AWrongShapedSurfacedField_IsPreservedRatherThanDropped()
    {
        AssertRoundTrips(
            """{"build":{"mode":"supervisor"}}""",
            "An unrecognised mode is not one of the three the enum allows, so it cannot be read — "
            + "but deleting it loses the only evidence of the mistake.");

        AssertRoundTrips(
            """{"build":{"temperature":"warm"}}""",
            "A temperature written as a string.");

        AssertRoundTrips(
            """{"build":{"tools":{"bash":"yes"}}}""",
            "A tools map whose value is not boolean.");

        AssertRoundTrips(
            """{"build":{"disable":"true"}}""",
            "A boolean written as a string.");
    }

    [TestMethod]
    public void AWrongShapedFieldDoesNotStopTheRestOfTheAgentBeingRead()
    {
        OpenCodeAgentConfig agent = OpenCodeAgentCodec.ReadAgent(
            CurrencyText.Parse("""{"model":"m","mode":"supervisor","steps":7}"""));

        Assert.AreEqual("m", agent.Model, "The readable fields are still read.");
        Assert.AreEqual(7L, agent.Steps);
        Assert.AreEqual(OpenCodeAgentMode.Unset, agent.Mode);
        Assert.IsTrue(
            agent.Extras.Any(e => e.Key == "mode"),
            "And the unreadable one is held as an extra.");
    }

    [TestMethod]
    public void FieldsTheModelDoesNotSurface_Survive()
    {
        AssertRoundTrips(
            """{"build":{"model":"m","futureField":{"nested":[1,2]}}}""",
            "The schema does NOT set additionalProperties:false on AgentConfig, so unknown fields "
            + "are legal rather than merely tolerated.");
    }

    [TestMethod]
    public void AnEntryThatIsNotAnObject_IsHeldVerbatim()
    {
        AssertRoundTrips(
            """{"weird":"just a string","alsoWeird":[1,2],"nope":null}""",
            "Nothing about these is editable, and nothing about them should be destroyed.");
    }

    [TestMethod]
    public void OptionsAndPermissionArePassedThroughUntouched()
    {
        AssertRoundTrips(
            """
            {"build":{"options":{"provider":{"deep":{"deeper":[1,{"x":null}]}}},
            "permission":{"bash":{"npm *":"deny","*":"ask"}}}}
            """,
            "Both are opaque here on purpose: options has no declared shape, and permission is the "
            + "same definition the permission grid already owns. Re-parsing either would duplicate "
            + "a model and let the copies drift.");
    }

    // ── Removal, and the built-in list ───────────────────────────────────────

    [TestMethod]
    public void WriteMap_IsNullWhenThereAreNoAgents()
    {
        Assert.IsNull(
            OpenCodeAgentCodec.WriteMap([]),
            "An empty object would persist an agent key that configures nothing.");
    }

    [TestMethod]
    public void WriteMap_SkipsABlankAgentName()
    {
        Assert.IsNull(OpenCodeAgentCodec.WriteMap([
            new KeyValuePair<string, OpenCodeAgentConfig>("  ", new OpenCodeAgentConfig()),
        ]));
    }

    [TestMethod]
    public void ReadMap_TreatsANonObjectValueAsNothingToEdit()
    {
        Assert.AreEqual(0, OpenCodeAgentCodec.ReadMap(null).Count);
        Assert.AreEqual(0, OpenCodeAgentCodec.ReadMap("nonsense").Count);
    }

    [TestMethod]
    public void ModeToWire_RefusesUnset()
    {
        Assert.AreEqual("subagent", OpenCodeAgentCodec.ModeToWire(OpenCodeAgentMode.Subagent));
        Assert.AreEqual("primary", OpenCodeAgentCodec.ModeToWire(OpenCodeAgentMode.Primary));
        Assert.AreEqual("all", OpenCodeAgentCodec.ModeToWire(OpenCodeAgentMode.All));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => OpenCodeAgentCodec.ModeToWire(OpenCodeAgentMode.Unset),
            "Unset means the key is absent. Writing a mode for it sets something the user did not.");
    }

    /// <remarks>
    /// Cross-checked against the schema rather than restated, because the seven are the names an
    /// override changes the tool's own behaviour through — the distinction the editor surfaces.
    /// </remarks>
    [TestMethod]
    public void BuiltInAgents_MatchTheSchemasNamedProperties()
    {
        string path = SchemaFile();
        using System.Text.Json.JsonDocument doc =
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        List<string> named =
        [
            .. doc.RootElement
                .GetProperty("$defs").GetProperty("Config").GetProperty("properties")
                .GetProperty("agent").GetProperty("properties")
                .EnumerateObject()
                .Select(p => p.Name)
                .Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(
            named.ToArray(),
            OpenCodeBuiltInAgents.Names.Order(StringComparer.Ordinal).ToArray(),
            "The built-in agent list drifted from the schema. A name missing here is presented as a "
            + "user-defined agent when overriding it actually changes how OpenCode behaves.");
    }

    private static string SchemaFile()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", "opencode-config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate the bundled OpenCode schema.");
    }
}
