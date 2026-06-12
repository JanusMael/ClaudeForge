using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core;
using Bennewitz.Ninja.ClaudeForge.Core.Catalog;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Catalog;

/// <summary>
/// Keeps the model catalog and the bundled JSON schema in lockstep. The catalog
/// is the source of truth for the value lists; this test fails CI if the schema's
/// <c>effortLevel</c> / <c>permissions.defaultMode</c> enums drift away from it
/// (e.g. after a schema refresh adds a mode the catalog doesn't know). Also
/// validates <c>model-catalog.json</c> against its own schema.
/// </summary>
[TestClass]
public sealed class ModelCatalogSchemaParityTests
{
    private static JsonNode LoadMergedSchema()
    {
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged("claude-code-settings.json");
        Assert.IsNotNull(bytes, "Bundled claude-code-settings.json must be present.");
        return JsonNode.Parse(bytes)!;
    }

    private static HashSet<string> EnumAt(JsonNode node)
        => node.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

    [TestMethod]
    public void EffortLevelEnum_MatchesCatalog()
    {
        JsonNode schema = LoadMergedSchema();
        HashSet<string> schemaEnum = EnumAt(schema["properties"]!["effortLevel"]!["enum"]!);
        HashSet<string> catalog = ModelCatalogLoader.Load().EffortLevels.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(schemaEnum.SetEquals(catalog),
            $"effortLevel enum drift. schema=[{string.Join(",", schemaEnum)}] catalog=[{string.Join(",", catalog)}]");
    }

    [TestMethod]
    public void DefaultModeEnum_MatchesCatalog()
    {
        JsonNode schema = LoadMergedSchema();
        HashSet<string> schemaEnum = EnumAt(schema["properties"]!["permissions"]!["properties"]!["defaultMode"]!["enum"]!);
        HashSet<string> catalog = ModelCatalogLoader.Load().DefaultModes.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(schemaEnum.SetEquals(catalog),
            $"permissions.defaultMode enum drift. schema=[{string.Join(",", schemaEnum)}] catalog=[{string.Join(",", catalog)}]");
    }

    [TestMethod]
    public void ModelCatalogJson_ValidatesAgainstItsSchema()
    {
        byte[]? catBytes = BundledResource.TryRead("ModelCatalog", "model-catalog.json");
        byte[]? schBytes = BundledResource.TryRead("ModelCatalog", "model-catalog.schema.json");
        Assert.IsNotNull(catBytes, "model-catalog.json must be embedded.");
        Assert.IsNotNull(schBytes, "model-catalog.schema.json must be embedded.");

        JsonSchema schema = SchemaRegistry.ParseSchema(Encoding.UTF8.GetString(schBytes));
        using JsonDocument doc = JsonDocument.Parse(catBytes);
        EvaluationResults results = schema.Evaluate(
            doc.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.IsTrue(results.IsValid, "model-catalog.json must validate against model-catalog.schema.json.");
    }

    [TestMethod]
    public void EveryAlias_ResolvesToARealModel()
    {
        ModelCatalog c = ModelCatalogLoader.Load();
        foreach (KeyValuePair<string, string> kv in c.Aliases)
        {
            Assert.IsTrue(c.Models.Any(m => m.Id == kv.Value),
                $"Alias '{kv.Key}' points at unknown model id '{kv.Value}'.");
        }
    }

    [TestMethod]
    public void EveryModel_DefaultEffort_IsSupportedOrNull()
    {
        ModelCatalog c = ModelCatalogLoader.Load();
        foreach (ModelInfo m in c.Models)
        {
            if (m.DefaultEffortLevel is null)
            {
                continue;
            }

            CollectionAssert.Contains(
                m.SupportedEffortLevels.ToList(),
                m.DefaultEffortLevel,
                $"Model '{m.Id}' default effort '{m.DefaultEffortLevel}' is not in its supported set.");
        }
    }
}
