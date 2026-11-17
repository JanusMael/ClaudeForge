using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Json.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Schema;

/// <summary>
/// <c>theme</c> is an <c>anyOf</c> of two string variants — a fixed enum
/// (auto/dark/light/…) OR a <c>"custom:&lt;slug&gt;"</c> pattern string. It must classify
/// as a <b>free-form</b> <see cref="Enum"/> so the UI renders the enum values as
/// suggestions in an AutoCompleteBox that still lets the user type a <c>custom:</c>
/// reference — NOT fall through to the raw-JSON box (which produced the confusing
/// "Value matches none of the 2 permitted variants" error when a user pasted an object).
/// </summary>
/// <remarks>
/// Loads the embedded bundled schema directly (see
/// <see cref="OutputStylePropertyPromotionTests"/>) so a stale developer disk cache
/// can't mask a regression in the repository schema.
/// </remarks>
public sealed class ThemePropertyPromotionTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        byte[]? bytes = BundledProduct.ClaudeFiles.TryRead("claude-code-settings.json");
        MessageAssert.NotNull(bytes, "Claude's bundled files must carry claude-code-settings.json.");
        string json = System.Text.Encoding.UTF8.GetString(bytes!);

        BuildOptions opts = new() { SchemaRegistry = new SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    [Fact]
    public void Theme_Promotes_ToFreeFormEnum_WithEnumSuggestions()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? theme = top.FirstOrDefault(n => n.Name == "theme");
        MessageAssert.NotNull(theme, "theme property must exist at top level of schema");

        MessageAssert.Equal(SchemaValueType.Enum, theme!.ValueType,
            "anyOf of string variants (enum | pattern) must promote to Enum, not the raw-JSON fallback.");

        // The fixed-enum branch seeds the suggestions.
        Assert.Contains("dark", theme.EnumValues.ToArray());
        Assert.Contains("light", theme.EnumValues.ToArray());
        Assert.Contains("auto", theme.EnumValues.ToArray());

        // A non-enum string branch (the "custom:<slug>" pattern) means values beyond the
        // list are permitted — surfaced to the enum editor as a non-empty Examples, which
        // is its "allow free-form typing" signal (AutoCompleteBox, not a strict ComboBox).
        Assert.True(theme.Examples.Count > 0,
            "The custom: pattern branch must mark theme free-form (non-empty Examples).");
    }
}
