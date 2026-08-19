using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Covers <c>SchemaTreeBuilder</c>'s root-<c>$ref</c> fallback: a schema that hangs its
/// whole object off <c>{"$ref": "#/$defs/Config"}</c> with no <c>properties</c> keyword.
/// </summary>
/// <remarks>
/// <para>
/// This failure mode is invisible. Before the fallback, <c>BuildTopLevel</c> returned an
/// empty list for such a schema — no exception, no warning, just an editor page with
/// nothing on it. Nothing in the suite noticed, because every schema the app had ever
/// loaded declared <c>properties</c> at its root.
/// </para>
/// <para>
/// Both real schemas are asserted, not just the interesting one. <c>tui.json</c> is the
/// control: it is an ordinary object schema, so it must produce the same tree with or
/// without the fallback, which is what shows the fallback did not change the normal path.
/// </para>
/// </remarks>
[TestClass]
public class RootRefSchemaTreeTests
{
    /// <summary>
    /// Parse in an isolated <see cref="Json.Schema.SchemaRegistry"/>, the same way
    /// <c>SchemaTreeBuilderTests</c> does, so <c>$defs</c> anchors from one test cannot
    /// collide with another's in the global registry.
    /// </summary>
    private static JsonSchemaNode ParseNode(string json)
    {
        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        return JsonSchema.FromText(json, opts).Root!;
    }

    private static List<SchemaNode> TopLevel(string fileName)
    {
        byte[]? bytes = BundledResource.TryRead("Schemas", fileName);
        Assert.IsNotNull(bytes, $"'{fileName}' is not embedded.");
        return SchemaTreeBuilder.BuildTopLevel(ParseNode(Encoding.UTF8.GetString(bytes))).ToList();
    }

    /// <summary>
    /// The measurement spike S4 recorded, now a regression test. 36 is not a magic number:
    /// it is every property of <c>$defs/Config</c>, which is what the user must be able to
    /// edit. A drop to 0 means the fallback stopped firing; any other change means upstream
    /// altered the schema and the count should be updated deliberately.
    /// </summary>
    [TestMethod]
    public void OpenCodeConfig_RootRefIsFollowed_SoTheTreeIsNotEmpty()
    {
        List<SchemaNode> nodes = TopLevel("opencode-config.json");

        Assert.AreNotEqual(0, nodes.Count,
            "opencode-config.json has no root `properties` — everything hangs off "
            + "\"$ref\": \"#/$defs/Config\". An empty tree here is the exact silent failure "
            + "the fallback exists to prevent: the editor renders a page with nothing on it.");

        Assert.AreEqual(36, nodes.Count,
            "Expected the 36 top-level properties of $defs/Config (spike S4). A different "
            + "count means upstream changed the schema; confirm the new shape and update "
            + "this number deliberately.");

        // Spot-check identity, not just arity: a fallback that resolved the wrong subschema
        // could still yield 36 of something else.
        foreach (string expected in new[] { "model", "permission", "provider", "agent" })
        {
            Assert.IsTrue(
                nodes.Any(n => n.Name == expected),
                $"Expected a top-level '{expected}' node. Got: "
                + string.Join(", ", nodes.Select(n => n.Name).Order(StringComparer.Ordinal)));
        }
    }

    /// <summary>
    /// The control case. <c>tui.json</c> declares <c>properties</c> at its root and has no
    /// <c>$ref</c> anywhere, so it exercises the path that already worked — proving the
    /// fallback is reached only when it should be.
    /// </summary>
    [TestMethod]
    public void OpenCodeTui_OrdinaryObjectSchema_IsUnaffectedByTheFallback()
    {
        List<SchemaNode> nodes = TopLevel("opencode-tui.json");

        Assert.AreEqual(13, nodes.Count,
            "Expected tui.json's 13 top-level properties (spike S4). This schema needs no "
            + "$ref following at all, so a change here means the fallback altered the "
            + "ordinary path.");
    }

    /// <summary>
    /// A schema declaring both <c>properties</c> and <c>$ref</c> must expose only its own
    /// properties. In 2020-12 a <c>$ref</c> beside sibling keywords is an additional
    /// constraint, not a replacement, so merging the target's properties in would invent
    /// fields the author never declared at that level.
    /// </summary>
    [TestMethod]
    public void ARefBesideProperties_IsNotFollowed()
    {
        JsonSchemaNode root = ParseNode(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$ref": "#/$defs/Extra",
              "properties": { "declaredHere": { "type": "string" } },
              "$defs": {
                "Extra": {
                  "type": "object",
                  "properties": { "fromTheRef": { "type": "string" } }
                }
              }
            }
            """);

        List<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root).ToList();

        CollectionAssert.AreEquivalent(
            new[] { "declaredHere" },
            nodes.Select(n => n.Name).ToArray(),
            "Following $ref when `properties` is present would surface 'fromTheRef' as a "
            + "sibling field the schema never declared at this level.");
    }

    /// <summary>
    /// A <c>$ref</c> cycle never reaches the tree builder: JsonSchema.Net detects it while
    /// <i>building</i> the schema and throws, long before anything walks the node graph.
    /// </summary>
    /// <remarks>
    /// Written expecting the builder to bottom out at its depth bound and return nothing;
    /// the schema failed to parse instead. Recorded rather than deleted because it explains
    /// why the depth bound in <c>GetPropertySubschemas</c> looks unreachable: it is
    /// belt-and-braces against a node graph arriving from somewhere other than
    /// <c>JsonSchema.FromText</c>, not the primary defence. If this test ever starts
    /// failing, the library stopped rejecting cycles and that bound became load-bearing.
    /// </remarks>
    [TestMethod]
    public void ARefCycle_IsRejectedWhenTheSchemaIsBuilt_NotWhenItIsWalked()
    {
        JsonSchemaException ex = Assert.ThrowsExactly<JsonSchemaException>(() => ParseNode(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$ref": "#/$defs/A",
              "$defs": {
                "A": { "$ref": "#/$defs/B" },
                "B": { "$ref": "#/$defs/A" }
              }
            }
            """));

        StringAssert.Contains(ex.Message, "Cycle",
            "Expected the schema library's own cycle detection to reject this.");
    }
}
