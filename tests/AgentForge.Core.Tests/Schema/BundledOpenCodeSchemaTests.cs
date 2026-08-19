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
/// <c>opencode-config.json</c> is upstream's file with four external
/// <c>models.dev</c> <c>$ref</c>s removed. Any future refresh — by hand or by the tooling
/// Phase 13 adds — re-downloads a file that has them, so the strip has to be re-applied
/// every time. A refresh that forgets is not a subtle regression: see
/// <see cref="BundledConfigSchema_HasNoExternalRef"/> for what it costs.
/// </para>
/// </remarks>
[TestClass]
public class BundledOpenCodeSchemaTests
{
    private const string ConfigSchema = "opencode-config.json";
    private const string TuiSchema = "opencode-tui.json";

    private static string ReadBundled(string fileName)
    {
        byte[]? bytes = BundledResource.TryRead("Schemas", fileName);
        Assert.IsNotNull(bytes,
            $"'{fileName}' is not embedded. Assets/Schemas/**/*.json is globbed into the "
            + "assembly, so this means the file is missing from the repo, not that the "
            + "csproj needs an entry.");
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// The whole reason the bundled copy diverges from upstream. Upstream types four
    /// <c>model</c> properties with <c>"$ref": "https://models.dev/model-schema.json#/$defs/Model"</c>
    /// alongside a plain <c>"type": "string"</c>.
    /// <para>
    /// Leaving it in makes evaluation throw a ref-resolution failure through
    /// <c>ValidateWorkspaceAsync</c> → <c>SaveAsync</c> for <b>any</b> config that sets a
    /// model, and the restore path's evaluate guard catches <c>JsonException</c>,
    /// <c>InvalidOperationException</c> and <c>ArgumentException</c> — not that. Resolving
    /// it instead would impose a 6,688-entry allow-list that rejects custom models.
    /// Stripping the keyword leaves <c>"type": "string"</c>, which is the behaviour wanted.
    /// </para>
    /// </summary>
    [TestMethod]
    public void BundledConfigSchema_HasNoExternalRef()
    {
        string json = ReadBundled(ConfigSchema);

        Assert.IsFalse(
            json.Contains("models.dev", StringComparison.OrdinalIgnoreCase),
            "The bundled OpenCode schema still references models.dev. A refresh has "
            + "re-introduced the external $ref that must be stripped: saving any config "
            + "that sets `model` will throw on schema evaluation.");

        Assert.IsFalse(
            json.Contains("\"$ref\": \"http", StringComparison.OrdinalIgnoreCase),
            "A bundled schema must resolve offline. An http(s) $ref makes evaluation "
            + "depend on the network at save time.");
    }

    /// <summary>
    /// The strip must remove only the keyword. If it took the enclosing property with it,
    /// the four model fields would become untyped — every value would validate, and the
    /// editor would offer no type information at all.
    /// </summary>
    [TestMethod]
    public void StrippingTheRefLeftTheModelPropertiesTyped()
    {
        JsonNode? root = JsonNode.Parse(ReadBundled(ConfigSchema));
        JsonNode? defs = root?["$defs"];
        Assert.IsNotNull(defs, "$defs is missing — the schema shape changed upstream.");

        foreach ((string defName, string propertyName) in new[]
                 {
                     ("Config", "model"),
                     ("Config", "small_model"),
                     ("AgentConfig", "model"),
                 })
        {
            JsonNode? property = defs[defName]?["properties"]?[propertyName];
            Assert.IsNotNull(property, $"$defs.{defName}.properties.{propertyName} is gone.");
            Assert.AreEqual(
                "string",
                property["type"]?.GetValue<string>(),
                $"$defs.{defName}.properties.{propertyName} lost its type. The strip is "
                + "supposed to remove the $ref keyword and nothing else.");
        }
    }

    /// <summary>
    /// Both files have to survive <see cref="SchemaRegistry.ParseSchema"/>. A bundled file
    /// that parses as JSON but not as a schema fails late and quietly: the restore path
    /// catches the parse failure per-file and simply skips that schema, so validation
    /// silently stops happening rather than reporting anything.
    /// </summary>
    [TestMethod]
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
    /// The two shapes the tree builder has to cope with, pinned here because they are the
    /// reason the builder needs a root-<c>$ref</c> fallback at all: the config schema puts
    /// everything behind a root <c>$ref</c> with no <c>properties</c> keyword, while the TUI
    /// schema is an ordinary object with <c>properties</c> and no <c>$ref</c>.
    /// </summary>
    [TestMethod]
    public void TheTwoSchemasHaveTheRootShapesTheTreeBuilderMustHandle()
    {
        JsonNode? config = JsonNode.Parse(ReadBundled(ConfigSchema));
        Assert.IsNull(config?["properties"],
            "opencode-config.json is expected to have NO root `properties` — everything "
            + "hangs off a root $ref. If upstream added one, the root-$ref fallback is no "
            + "longer exercised by the real schema.");
        Assert.AreEqual("#/$defs/Config", config?["$ref"]?.GetValue<string>());

        JsonNode? tui = JsonNode.Parse(ReadBundled(TuiSchema));
        Assert.IsNotNull(tui?["properties"],
            "opencode-tui.json is expected to be an ordinary object schema.");
        Assert.IsNull(tui?["$ref"],
            "opencode-tui.json has no root $ref, which is why it is the control case.");
    }
}
