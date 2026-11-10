using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Guards the two bundled OpenCode schemas, and in particular the one place where the
/// bundled copy deliberately differs from upstream.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the enforcement mechanism for a manual step.</b> The bundled
/// <c>opencode-config.json</c> is upstream's file with its external <c>models.dev</c>
/// <c>$ref</c>s removed — three since 2026-10-19, four before (see <see cref="ModelRefSites"/>).
/// Any future refresh — by hand or by the tooling
/// Phase 13 adds — re-downloads a file that has them, so the strip has to be re-applied
/// every time. A refresh that forgets is not a subtle regression: see
/// <see cref="BundledConfigSchema_HasNoExternalRef"/> for what it costs.
/// </para>
/// </remarks>
public class BundledOpenCodeSchemaTests
{
    private const string ConfigSchema = "opencode-config.json";
    private const string TuiSchema = "opencode-tui.json";

    private static string ReadBundled(string fileName)
    {
        byte[]? bytes = BundledResource.TryRead("Schemas", fileName);
        MessageAssert.NotNull(bytes,
            $"'{fileName}' is not embedded. Assets/Schemas/**/*.json is globbed into the "
            + "assembly, so this means the file is missing from the repo, not that the "
            + "csproj needs an entry.");
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// The whole reason the bundled copy diverges from upstream. Upstream types its
    /// <c>model</c>-valued properties with <c>"$ref": "https://models.dev/model-schema.json#/$defs/Model"</c>
    /// alongside a plain <c>"type": "string"</c> — three of them since 2026-10-19.
    /// <para>
    /// Leaving it in makes evaluation throw a ref-resolution failure through
    /// <c>ValidateWorkspaceAsync</c> → <c>SaveAsync</c> for <b>any</b> config that sets a
    /// model, and the restore path's evaluate guard catches <c>JsonException</c>,
    /// <c>InvalidOperationException</c> and <c>ArgumentException</c> — not that. Resolving
    /// it instead would impose a 6,688-entry allow-list that rejects custom models.
    /// Stripping the keyword leaves <c>"type": "string"</c>, which is the behaviour wanted.
    /// </para>
    /// </summary>
    [Fact]
    public void BundledConfigSchema_HasNoExternalRef()
    {
        string json = ReadBundled(ConfigSchema);

        Assert.False(
            json.Contains("models.dev", StringComparison.OrdinalIgnoreCase),
            "The bundled OpenCode schema still references models.dev. A refresh has "
            + "re-introduced the external $ref that must be stripped: saving any config "
            + "that sets `model` will throw on schema evaluation.");

        Assert.False(
            json.Contains("\"$ref\": \"http", StringComparison.OrdinalIgnoreCase),
            "A bundled schema must resolve offline. An http(s) $ref makes evaluation "
            + "depend on the network at save time.");
    }

    /// <summary>
    /// Every site upstream puts an external <c>$ref</c> on, named as a JSON pointer.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>There are FOUR, and one of them is nested two levels deeper than the other three.</b>
    /// Measured 2026-09-08 by walking the live upstream file for <c>$ref</c> values containing
    /// <c>models.dev</c>. The <c>command</c> one does not fit a <c>(def, property)</c> pair, which
    /// is exactly why an earlier revision of this test covered only three of the four: the shape
    /// of the fixture decided the coverage. Pointers instead, so depth is not a filter.
    /// <para>
    /// Every site carries a <c>type</c> sibling upstream, which is what makes stripping the
    /// keyword alone a safe mechanical edit.
    /// </para>
    /// <para>
    /// ⓘ <b>Re-measured 2026-10-19, the same way, against the live upstream file: there are now
    /// THREE.</b> Upstream inlined the schema root and removed <c>$defs/Config</c>, so the two
    /// <c>Config</c> sites moved to the root's <c>properties</c>, and the top-level <c>model</c>
    /// lost its <c>$ref</c> entirely: it is now an <c>anyOf</c> of a <c>provider/model</c> string
    /// and a <c>{providerID, model, variant}</c> object, pinned in
    /// <see cref="TheTwoSchemasHaveTheRootShapesTheTreeBuilderMustHandle"/>. Each of the three
    /// still has its <c>"type": "string"</c> sibling. The old pointers were all
    /// <c>/$defs/Config/...</c> for three of the four, and this test refused to pass over them
    /// once they stopped resolving — which is how the change was found.
    /// </para>
    /// </remarks>
    private static readonly string[] ModelRefSites =
    [
        "/properties/small_model",
        "/$defs/AgentConfig/properties/model",
        "/properties/command/additionalProperties/properties/model",
    ];

    /// <summary>Resolve a slash-delimited JSON pointer, or <see langword="null"/>.</summary>
    private static JsonNode? Resolve(JsonNode? root, string pointer)
    {
        JsonNode? node = root;
        foreach (string segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node?[segment];
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>
    /// The strip must remove only the keyword. If it took the enclosing property with it,
    /// the model fields would become untyped — every value would validate, and the
    /// editor would offer no type information at all.
    /// </summary>
    [Fact]
    public void StrippingTheRefLeftTheModelPropertiesTyped()
    {
        JsonNode? root = JsonNode.Parse(ReadBundled(ConfigSchema));
        MessageAssert.NotNull(root?["$defs"], "$defs is missing — the schema shape changed upstream.");

        foreach (string pointer in ModelRefSites)
        {
            JsonNode? property = Resolve(root, pointer);

            // Premise before claim: a pointer that stopped resolving would otherwise let this
            // loop pass over a site it never looked at.
            MessageAssert.NotNull(property,
                $"'{pointer}' no longer resolves. Upstream moved or removed it, so the strip "
                + "this test guards may now be aimed at nothing — re-measure the ref sites "
                + "against the live upstream file before editing the list.");

            MessageAssert.Equal(
                "string",
                property["type"]?.GetValue<string>(),
                $"'{pointer}' lost its type. The strip is supposed to remove the $ref keyword "
                + "and nothing else.");

            MessageAssert.Null(property["$ref"],
                $"'{pointer}' still carries a $ref. The strip missed this site — and it is the "
                + "deeply-nested one if this is the `command` pointer.");
        }
    }

    /// <summary>
    /// Both files have to survive <see cref="SchemaRegistry.ParseSchema"/>. A bundled file
    /// that parses as JSON but not as a schema fails late and quietly: the restore path
    /// catches the parse failure per-file and simply skips that schema, so validation
    /// silently stops happening rather than reporting anything.
    /// </summary>
    [Fact]
    public void BothBundledSchemas_ParseAsJsonSchema()
    {
        foreach (string fileName in new[] { ConfigSchema, TuiSchema })
        {
            string json = ReadBundled(fileName);
            try
            {
                _ = SchemaRegistry.ParseSchema(json);
            }
            catch (Exception ex)
            {
                Assert.Fail($"'{fileName}' did not parse as a JSON Schema: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The root shapes the two real schemas have, pinned so the next upstream redesign fails
    /// here, by name, rather than somewhere downstream.
    /// </summary>
    /// <remarks>
    /// ⓘ <b>Corrected 2026-10-19.</b> This pinned the config schema as having NO root
    /// <c>properties</c> and a root <c>$ref</c> to <c>#/$defs/Config</c>, and its message
    /// predicted the day that stopped: <i>"If upstream added one, the root-$ref fallback is no
    /// longer exercised by the real schema."</i> Upstream did — the root is inlined and
    /// <c>$defs/Config</c> is gone — so both real schemas are now ordinary object schemas, and
    /// the fallback is guarded synthetically by
    /// <c>RootRefSchemaTreeTests.ARootRefWithoutProperties_IsFollowed</c>.
    /// <para>
    /// The top-level <c>model</c> is pinned as well, because it changed TYPE, not just place: a
    /// <c>provider/model</c> string or a <c>{providerID, model, variant}</c> object. That is why
    /// it left <see cref="ModelRefSites"/>, and it is the one change an editor has to handle
    /// rather than merely re-find.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoSchemasHaveTheRootShapesTheTreeBuilderMustHandle()
    {
        JsonNode? config = JsonNode.Parse(ReadBundled(ConfigSchema));
        MessageAssert.NotNull(config?["properties"],
            "opencode-config.json is expected to declare its properties at the root (since "
            + "2026-10-19). If they have moved behind a $ref again, the real schema reaches the "
            + "root-$ref fallback once more.");
        MessageAssert.Null(config?["$ref"],
            "opencode-config.json is expected to have no root $ref (since 2026-10-19).");

        JsonNode? model = config?["properties"]?["model"];
        MessageAssert.NotNull(model, "The root `model` property is missing.");
        JsonArray? anyOf = model["anyOf"] as JsonArray;
        MessageAssert.NotNull(anyOf,
            "`model` is expected to be an anyOf of a string and an object (since 2026-10-19). "
            + "If it is a plain string again, it may be back in ModelRefSites' list.");
        string[] branchTypes = anyOf.Select(b => b?["type"]?.GetValue<string>() ?? "").Order(StringComparer.Ordinal).ToArray();
        MessageAssert.SameElements(new[] { "object", "string" }, branchTypes,
            "`model` is expected to accept a provider/model string OR a {providerID, model, "
            + "variant} object.");

        JsonNode? tui = JsonNode.Parse(ReadBundled(TuiSchema));
        MessageAssert.NotNull(tui?["properties"],
            "opencode-tui.json is expected to be an ordinary object schema.");
        MessageAssert.Null(tui?["$ref"],
            "opencode-tui.json has no root $ref, which is why it is the control case.");
    }
}
