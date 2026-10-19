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
/// ⛔ <b>Since 2026-10-19 no bundled schema uses a root <c>$ref</c>, so the fallback is
/// guarded by a SYNTHETIC schema, not a real one.</b> <c>opencode-config.json</c> was the
/// only real instance; upstream then inlined its root (48 <c>properties</c>, and
/// <c>$defs/Config</c> removed). Updating that test's count alone would have left it green
/// while it exercised nothing of the fallback — a name promising coverage it no longer had.
/// <see cref="ARootRefWithoutProperties_IsFollowed"/> is now the fallback's only positive
/// guard, and it was canaried: disabling the fallback reddens it.
/// </para>
/// <para>
/// Both real schemas are still asserted, now as ordinary object schemas: <c>tui.json</c>
/// remains the control, and the config schema is a regression count of what the user must
/// be able to edit.
/// </para>
/// </remarks>
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
        MessageAssert.NotNull(bytes, $"'{fileName}' is not embedded.");
        return SchemaTreeBuilder.BuildTopLevel(ParseNode(Encoding.UTF8.GetString(bytes))).ToList();
    }

    /// <summary>
    /// The fallback's positive case, on a schema written to need it: the whole object hangs off
    /// a root <c>$ref</c> and the root declares no <c>properties</c>. The tree must be the
    /// target's properties — exactly those, so a fallback that resolved some other subschema
    /// cannot pass on arity alone.
    /// </summary>
    [Fact]
    public void ARootRefWithoutProperties_IsFollowed()
    {
        JsonSchemaNode root = ParseNode(
            """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$ref": "#/$defs/Config",
              "$defs": {
                "Config": {
                  "type": "object",
                  "properties": {
                    "model": { "type": "string" },
                    "permission": { "type": "object" },
                    "provider": { "type": "object" }
                  }
                },
                "Decoy": {
                  "type": "object",
                  "properties": { "notTheTarget": { "type": "string" } }
                }
              }
            }
            """);

        List<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root).ToList();

        MessageAssert.NotEqual(0, nodes.Count,
            "A root $ref with no `properties` produced an empty tree — the exact silent failure "
            + "the fallback exists to prevent: the editor renders a page with nothing on it.");

        MessageAssert.SameElements(
            new[] { "model", "permission", "provider" },
            nodes.Select(n => n.Name).Order(StringComparer.Ordinal).ToArray(),
            "The fallback must surface the $ref TARGET's properties, and only those.");
    }

    /// <summary>
    /// The real config schema, as a regression count. 48 is not a magic number: it is every
    /// root property of <c>opencode-config.json</c>, which is what the user must be able to
    /// edit. Any change means upstream altered the schema and the count should be updated
    /// deliberately.
    /// </summary>
    /// <remarks>
    /// ⓘ <b>Corrected 2026-10-19.</b> This was <c>OpenCodeConfig_RootRefIsFollowed_SoTheTreeIsNotEmpty</c>,
    /// asserting 36 properties reached through the root-<c>$ref</c> fallback (spike S4). Upstream
    /// inlined the root, so the real schema no longer reaches the fallback at all; the name is
    /// changed because keeping it would claim coverage this test no longer gives. The
    /// fallback's guard is <see cref="ARootRefWithoutProperties_IsFollowed"/>.
    /// </remarks>
    [Fact]
    public void OpenCodeConfig_EveryRootPropertyIsInTheTree()
    {
        List<SchemaNode> nodes = TopLevel("opencode-config.json");

        MessageAssert.Equal(48, nodes.Count,
            "Expected the 48 root properties of opencode-config.json (measured 2026-10-19, when "
            + "upstream inlined its root). A different count means upstream changed the schema; "
            + "confirm the new shape and update this number deliberately.");

        // Spot-check identity, not just arity: a builder that read the wrong subschema could
        // still yield 48 of something else.
        foreach (string expected in new[] { "model", "permission", "provider", "agent" })
        {
            Assert.True(
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
    [Fact]
    public void OpenCodeTui_OrdinaryObjectSchema_IsUnaffectedByTheFallback()
    {
        List<SchemaNode> nodes = TopLevel("opencode-tui.json");

        MessageAssert.Equal(13, nodes.Count,
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
    [Fact]
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

        MessageAssert.SameElements(
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
    [Fact]
    public void ARefCycle_IsRejectedWhenTheSchemaIsBuilt_NotWhenItIsWalked()
    {
        JsonSchemaException ex = Assert.Throws<JsonSchemaException>(() => ParseNode(
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

        MessageAssert.Contains("Cycle", ex.Message,
            "Expected the schema library's own cycle detection to reject this.");
    }
}
