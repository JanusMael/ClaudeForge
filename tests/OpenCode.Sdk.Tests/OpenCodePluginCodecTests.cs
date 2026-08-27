using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk.Plugins;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>plugin</c> array and the TUI's <c>plugin_enabled</c> map.
/// </summary>
/// <remarks>
/// The property doing the most work here is that <c>"foo"</c> and <c>["foo", {}]</c> stay apart:
/// the schema's tuple arm is strict (<c>prefixItems</c>, <c>minItems</c> and <c>maxItems</c> both
/// 2), so an empty options object is a deliberate statement and collapsing it would rewrite the
/// user's file for no reason.
/// </remarks>
[TestClass]
public sealed class OpenCodePluginCodecTests
{
    private static void AssertListRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(OpenCodePluginCodec.WriteList(
                OpenCodePluginCodec.ReadList(CurrencyText.Parse(json)))),
            because);

    // ── The two arms ─────────────────────────────────────────────────────────

    [TestMethod]
    public void BothArmsRoundTrip()
    {
        AssertListRoundTrips(
            """["bare-one","bare-two"]""",
            "Bare specifiers must stay bare.");

        AssertListRoundTrips(
            """[["with-opts",{"a":1,"b":"two"}]]""",
            "And the tuple form must keep its options.");

        AssertListRoundTrips(
            """["bare",["with-opts",{"x":true}],"another"]""",
            "Mixed arms in one array, in order.");
    }

    /// <remarks>
    /// ⭐ The distinction the whole codec exists for. Collapsing <c>["foo", {}]</c> to <c>"foo"</c>
    /// looks like tidying and is a silent edit to a different arm of the union.
    /// </remarks>
    [TestMethod]
    public void AnEmptyOptionsObjectIsNotCollapsedToTheBareForm()
    {
        AssertListRoundTrips(
            """[["foo",{}]]""",
            "An explicitly empty options object is a deliberate statement, not noise.");

        OpenCodePluginEntry entry =
            OpenCodePluginCodec.ReadEntry(CurrencyText.Parse("""["foo",{}]"""));
        Assert.IsTrue(entry.HasOptions);
        Assert.AreEqual("foo", entry.Name);

        OpenCodePluginEntry bare = OpenCodePluginCodec.ReadEntry("foo");
        Assert.IsFalse(bare.HasOptions, "And the bare form must not acquire options.");
    }

    [TestMethod]
    public void ElementOrderIsPreserved()
    {
        object? written = OpenCodePluginCodec.WriteList(OpenCodePluginCodec.ReadList(
            CurrencyText.Parse("""["zeta","alpha","middle"]""")));

        CollectionAssert.AreEqual(
            new object?[] { "zeta", "alpha", "middle" },
            ((IReadOnlyList<object?>)written!).ToArray());
    }

    // ── The strict tuple, and what falls outside it ──────────────────────────

    /// <remarks>
    /// ⚠ The tuple arm is strict, so "nearly right" is common — and a plugin silently dropped is a
    /// plugin the user believes is loaded.
    /// </remarks>
    [TestMethod]
    public void ElementsThatMatchNeitherArmAreHeldVerbatim()
    {
        foreach (string json in new[]
                 {
                     """[["only-one-item"]]""",
                     """[["name","not-an-object"]]""",
                     """[["name",{},"extra"]]""",
                     """[42]""",
                     """[null]""",
                     """[{"not":"a tuple"}]""",
                 })
        {
            AssertListRoundTrips(json, $"{json} matches neither arm and must survive unchanged.");
        }
    }

    [TestMethod]
    public void AnUnreadableElementDoesNotStopItsNeighboursBeingRead()
    {
        IReadOnlyList<OpenCodePluginEntry> entries = OpenCodePluginCodec.ReadList(
            CurrencyText.Parse("""[["broken"],"good",["opts",{"a":1}]]"""));

        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries[0].IsOpaque);
        Assert.AreEqual("good", entries[1].Name);
        Assert.IsTrue(entries[2].HasOptions);
    }

    // ── Removal ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void WriteList_IsNullWhenThereAreNoEntries()
    {
        Assert.IsNull(
            OpenCodePluginCodec.WriteList([]),
            "An empty array would persist a plugin key that loads nothing.");
    }

    [TestMethod]
    public void WriteList_SkipsABlankSpecifier()
    {
        Assert.IsNull(OpenCodePluginCodec.WriteList([new OpenCodePluginEntry { Name = "  " }]));
    }

    [TestMethod]
    public void ReadList_TreatsANonArrayValueAsNothingToEdit()
    {
        Assert.AreEqual(0, OpenCodePluginCodec.ReadList(null).Count);
        Assert.AreEqual(0, OpenCodePluginCodec.ReadList("nonsense").Count);
    }

    // ── plugin_enabled ───────────────────────────────────────────────────────

    [TestMethod]
    public void TheEnabledMapRoundTripsAndKeepsKeyOrder()
    {
        object? value = CurrencyText.Parse("""{"zeta":true,"alpha":false}""");
        object? written = OpenCodePluginCodec.WriteEnabledMap(
            OpenCodePluginCodec.ReadEnabledMap(value));

        Assert.AreEqual(CurrencyText.Render(value), CurrencyText.Render(written));
        Assert.IsInstanceOfType<OrderedPropertyMap>(written, "the toggle map");
    }

    /// <remarks>
    /// ⚠ Filtering the map down to its readable entries looks harmless on a read and deletes the
    /// rest on the next write — which is what an earlier draft of this codec did.
    /// </remarks>
    [TestMethod]
    public void ANonBooleanToggleValueIsPreservedRatherThanDropped()
    {
        object? value = CurrencyText.Parse(
            """{"good":true,"weird":"yes","alsoWeird":{"nested":1}}""");

        IReadOnlyList<OpenCodePluginToggle> toggles = OpenCodePluginCodec.ReadEnabledMap(value);

        Assert.AreEqual(3, toggles.Count);
        Assert.IsFalse(toggles[0].IsOpaque);
        Assert.IsTrue(toggles[1].IsOpaque);
        Assert.IsTrue(toggles[2].IsOpaque);

        Assert.AreEqual(
            CurrencyText.Render(value),
            CurrencyText.Render(OpenCodePluginCodec.WriteEnabledMap(toggles)),
            "Including the entries this build cannot toggle.");
    }

    [TestMethod]
    public void WriteEnabledMap_IsNullWhenEmpty()
    {
        Assert.IsNull(OpenCodePluginCodec.WriteEnabledMap([]));
        Assert.IsNull(OpenCodePluginCodec.WriteEnabledMap([
            new OpenCodePluginToggle { Name = "   ", Enabled = true },
        ]));
    }

    [TestMethod]
    public void ReadEnabledMap_TreatsANonObjectValueAsNothingToEdit()
    {
        Assert.AreEqual(0, OpenCodePluginCodec.ReadEnabledMap(null).Count);
        Assert.AreEqual(0, OpenCodePluginCodec.ReadEnabledMap("nonsense").Count);
    }

    /// <remarks>
    /// The two products declare the identical <c>plugin</c> shape, which is what lets one editor
    /// serve both. Read from the bundled schemas rather than restated, so a refresh that diverges
    /// them fails here instead of silently giving the TUI an editor built for the config file.
    /// </remarks>
    [TestMethod]
    public void BothProductsDeclareTheSamePluginShape()
    {
        string config = ReadSchemaFragment("opencode-config.json", isConfig: true);
        string tui = ReadSchemaFragment("opencode-tui.json", isConfig: false);

        Assert.AreEqual(
            config,
            tui,
            "Config.plugin and the TUI's plugin diverged. One editor is registered for both by "
            + "property name, so a divergence means one of them is now being edited by an editor "
            + "built for the other shape.");
    }

    private static string ReadSchemaFragment(string fileName, bool isConfig)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", fileName);
            if (File.Exists(candidate))
            {
                JsonNode root = JsonNode.Parse(File.ReadAllText(candidate))!;
                JsonNode container = isConfig
                    ? root["$defs"]!["Config"]!
                    : root;
                return container["properties"]!["plugin"]!.ToJsonString();
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException($"Could not locate '{fileName}'.");
    }
}
