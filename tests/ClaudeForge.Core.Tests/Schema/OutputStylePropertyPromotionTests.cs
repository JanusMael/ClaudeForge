using System.Reflection;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Json.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// <c>outputStyle</c> must promote to
/// <see cref="Enum"/> so the UI renders it as an AutoCompleteBox
/// with suggestions ("default", "Explanatory", "Learning"). The underlying promotion
/// is driven by the <c>examples</c> array in the bundled schema; this test guards
/// against a future schema edit that drops those examples (which would silently
/// regress the editor back to a plain TextBox).
/// </summary>
/// <remarks>
/// Loads the embedded bundled schema directly rather than going through
/// <see cref="Json.Schema.SchemaRegistry"/> — the disk cache at <c>~/.claude/cache/schemas/</c>
/// is stale on developer machines and would mask a regression in the repository
/// schema until the cache was manually refreshed.
/// </remarks>
[TestClass]
public sealed class OutputStylePropertyPromotionTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        Assembly assembly = typeof(Core.Schema.SchemaRegistry).Assembly;
        const string resourceName = ResourceHelper.ResourcePrefix + ".Core.Assets.Schemas.claude-code-settings.json";

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        Assert.IsNotNull(stream, $"Embedded resource '{resourceName}' must exist.");
        using StreamReader reader = new(stream!);
        string json = reader.ReadToEnd();

        BuildOptions opts = new() { SchemaRegistry = new SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    [TestMethod]
    public void OutputStyle_Promotes_ToEnum_WithExamples()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? outputStyle = top.FirstOrDefault(n => n.Name == "outputStyle");
        Assert.IsNotNull(outputStyle, "outputStyle property must exist at top level of schema");

        Assert.AreEqual(SchemaValueType.Enum, outputStyle!.ValueType,
            "string + examples must promote to Enum so the UI shows an AutoCompleteBox.");

        Assert.IsTrue(outputStyle.EnumValues.Count >= 3,
            $"Examples should provide at least three suggestions; got {outputStyle.EnumValues.Count}.");
        CollectionAssert.Contains(outputStyle.EnumValues.ToArray(), "default");
        CollectionAssert.Contains(outputStyle.EnumValues.ToArray(), "Explanatory");
        CollectionAssert.Contains(outputStyle.EnumValues.ToArray(), "Learning");
    }
}