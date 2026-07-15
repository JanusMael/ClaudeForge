using System.Text;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// the <c>model</c> property in the bundled Claude Code schema must
/// carry a <c>default</c> and <c>examples</c> so <see cref="Core.Schema.SchemaTreeBuilder"/> promotes
/// it to <see cref="Core.Schema.SchemaValueType.Enum"/>. Enum promotion is what causes the UI to
/// render it as an AutoCompleteBox with dropdown suggestions and an
/// "(inherits: sonnet)" watermark, instead of a plain "(not set)" TextBox.
/// </summary>
/// <remarks>
/// Loads the embedded bundled schema resource directly rather than going through
/// <see cref="Core.Schema.SchemaRegistry"/>. <see cref="Core.Schema.SchemaRegistry"/> prefers the on-disk
/// cache at <c>~/.claude/cache/schemas/</c>, which on a developer machine is stale
/// until refreshed from the network — so a test routed through it would assert
/// against a user's cached copy rather than the repository schema we just edited.
/// </remarks>
[TestClass]
public sealed class ModelPropertyPromotionTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        // load via TryReadBundledBytesMerged so the
        // claude-code-settings.overlay.json sibling is applied at the same
        // layer as production.  The base schema's `model` block was stripped
        // back to upstream-only form (just type + description) on the same
        // date; the overlay now carries `default`, `examples`, and the
        // enriched description.  Production goes through the same merge
        // step via SchemaRegistry.GetSchemaAsync's bundled-resource branch.
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged("claude-code-settings.json");
        Assert.IsNotNull(bytes, "Bundled schema resource must exist.");
        string json = Encoding.UTF8.GetString(bytes!);

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    [TestMethod]
    public void Model_Promotes_ToEnum_WithExamplesAndDefault()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? model = top.FirstOrDefault(n => n.Name == "model");
        Assert.IsNotNull(model, "model property must exist at top level of schema");

        Assert.AreEqual(SchemaValueType.Enum, model!.ValueType,
            "string + examples must promote to Enum so the UI shows an AutoCompleteBox.");

        Assert.AreEqual("sonnet", model.DefaultValue,
            "Default of 'sonnet' is required for the '(inherits: sonnet)' watermark.");

        Assert.IsTrue(model.EnumValues.Count >= 4,
            $"Examples should provide at least four suggestions; got {model.EnumValues.Count}.");
        CollectionAssert.Contains(model.EnumValues.ToArray(), "sonnet");
        CollectionAssert.Contains(model.EnumValues.ToArray(), "opus");
        CollectionAssert.Contains(model.EnumValues.ToArray(), "haiku");
    }

    [TestMethod]
    public void Model_CarriesEnumValueDescriptions_FromDescriptionsResource()
    {
        // The descriptions resource (Assets/Descriptions/claude-code-settings.enumdescriptions.json)
        // must surface on the model node so the picker can render per-item tooltips. Loaded via
        // SchemaRegistry.GetEnumDescriptions (System.Text.Json, not JsonSchema.Net) and threaded
        // into BuildTopLevel.
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> descriptions =
            SchemaRegistry.GetEnumDescriptions("claude-code-settings.json");

        IReadOnlyList<SchemaNode> top =
            SchemaTreeBuilder.BuildTopLevel(root, knownPaths: null, flagAllAsNew: false, descriptions);
        SchemaNode model = top.First(n => n.Name == "model");

        Assert.IsTrue(model.EnumValueDescriptions.Count >= 6,
            $"Expected per-value descriptions from the descriptions resource; got {model.EnumValueDescriptions.Count}.");

        // Every promoted picker value should have a tooltip (no item left bare).
        foreach (string value in model.EnumValues)
        {
            Assert.IsTrue(model.EnumValueDescriptions.ContainsKey(value),
                $"Model picker value '{value}' has no tooltip description.");
        }

        Assert.IsTrue(model.EnumValueDescriptions.TryGetValue("opusplan", out string? plan)
                      && plan.Contains("plan", StringComparison.OrdinalIgnoreCase),
            "opusplan should be described as the plan/execute hybrid.");
    }
}