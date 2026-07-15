using System.Reflection;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Json.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

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
[TestClass]
public sealed class ThemePropertyPromotionTests
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
    public void Theme_Promotes_ToFreeFormEnum_WithEnumSuggestions()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? theme = top.FirstOrDefault(n => n.Name == "theme");
        Assert.IsNotNull(theme, "theme property must exist at top level of schema");

        Assert.AreEqual(SchemaValueType.Enum, theme!.ValueType,
            "anyOf of string variants (enum | pattern) must promote to Enum, not the raw-JSON fallback.");

        // The fixed-enum branch seeds the suggestions.
        CollectionAssert.Contains(theme.EnumValues.ToArray(), "dark");
        CollectionAssert.Contains(theme.EnumValues.ToArray(), "light");
        CollectionAssert.Contains(theme.EnumValues.ToArray(), "auto");

        // A non-enum string branch (the "custom:<slug>" pattern) means values beyond the
        // list are permitted — surfaced to the enum editor as a non-empty Examples, which
        // is its "allow free-form typing" signal (AutoCompleteBox, not a strict ComboBox).
        Assert.IsTrue(theme.Examples.Count > 0,
            "The custom: pattern branch must mark theme free-form (non-empty Examples).");
    }
}
