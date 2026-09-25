using System.Reflection;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Json.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

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
/// <see cref="Core.Schema.SchemaRegistry"/>, keeping this a unit assertion over the
/// repository schema with no cache or global-registration state to manage.
/// <para>
/// Not because a stale disk cache could mask a regression — the registry reads the
/// bundled resource <i>ahead of</i> the disk cache and the network. An earlier
/// version of this remark had that backwards; <c>SchemaLoadPrecedenceTests</c> now
/// guards the ordering behaviourally.
/// </para>
/// </remarks>
public sealed class OutputStylePropertyPromotionTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        Assembly assembly = typeof(Core.Schema.SchemaRegistry).Assembly;
        string resourceName = ResourceHelper.AssetName("Schemas", "claude-code-settings.json");

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        MessageAssert.NotNull(stream, $"Embedded resource '{resourceName}' must exist.");
        using StreamReader reader = new(stream!);
        string json = reader.ReadToEnd();

        BuildOptions opts = new() { SchemaRegistry = new SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    [Fact]
    public void OutputStyle_Promotes_ToEnum_WithExamples()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? outputStyle = top.FirstOrDefault(n => n.Name == "outputStyle");
        MessageAssert.NotNull(outputStyle, "outputStyle property must exist at top level of schema");

        MessageAssert.Equal(SchemaValueType.Enum, outputStyle!.ValueType,
            "string + examples must promote to Enum so the UI shows an AutoCompleteBox.");

        Assert.True(outputStyle.EnumValues.Count >= 3,
            $"Examples should provide at least three suggestions; got {outputStyle.EnumValues.Count}.");
        Assert.Contains("default", outputStyle.EnumValues.ToArray());
        Assert.Contains("Explanatory", outputStyle.EnumValues.ToArray());
        Assert.Contains("Learning", outputStyle.EnumValues.ToArray());
    }
}