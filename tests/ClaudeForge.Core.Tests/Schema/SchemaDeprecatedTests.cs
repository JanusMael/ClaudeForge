using System.Reflection;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// properties whose description begins with
/// "DEPRECATED" (or which carry the JSON-Schema Draft-2019-09
/// <c>deprecated</c> keyword) must surface <see cref="SchemaNode.IsDeprecated"/>
/// = <c>true</c>. The host uses this flag to hide obsolete settings from the
/// editor list unless a value is already set at some scope (so users can
/// still remove legacy values).
/// </summary>
[TestClass]
public sealed class SchemaDeprecatedTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        Assembly assembly = typeof(SchemaRegistry).Assembly;
        const string resourceName = ResourceHelper.ResourcePrefix + ".Core.Assets.Schemas.claude-code-settings.json";

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        Assert.IsNotNull(stream, $"Embedded resource '{resourceName}' must exist.");
        using StreamReader reader = new(stream!);
        string json = reader.ReadToEnd();

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    [TestMethod]
    public void IncludeCoAuthoredBy_IsFlagged_Deprecated_ByDescriptionHeuristic()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? node = top.FirstOrDefault(n => n.Name == "includeCoAuthoredBy");
        Assert.IsNotNull(node, "includeCoAuthoredBy property must exist at top level of the schema");

        Assert.IsTrue(node!.IsDeprecated,
            "The description begins with 'DEPRECATED' so the heuristic should mark IsDeprecated=true.");
    }

    [TestMethod]
    public void NonDeprecatedSibling_Is_NotFlagged()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        // `model` has no DEPRECATED prefix and no deprecated keyword.
        SchemaNode? node = top.FirstOrDefault(n => n.Name == "model");
        Assert.IsNotNull(node);
        Assert.IsFalse(node!.IsDeprecated,
            "Non-deprecated properties must not carry the IsDeprecated flag.");
    }

    [TestMethod]
    public void ExplicitDeprecatedKeyword_Wins_OverDescriptionHeuristic()
    {
        // A tiny synthetic schema proves the JSON-Schema keyword is honored
        // independently of the description prefix.
        const string schemaJson = """
                                  {
                                    "type": "object",
                                    "properties": {
                                      "legacyField": {
                                        "type": "string",
                                        "deprecated": true,
                                        "description": "No prefix here."
                                      }
                                    }
                                  }
                                  """;

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(schemaJson, opts);
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(schema.Root!);

        SchemaNode legacy = top.Single(n => n.Name == "legacyField");
        Assert.IsTrue(legacy.IsDeprecated,
            "deprecated:true keyword must set IsDeprecated=true regardless of description.");
    }

    // ── IsUndocumented ────────────────────────────────────────────────────────

    [TestMethod]
    public void UndocumentedPrefix_SetsFlag_AndStripsPrefix()
    {
        const string schemaJson = """
                                  {
                                    "type": "object",
                                    "properties": {
                                      "hiddenProp": {
                                        "type": "string",
                                        "description": "UNDOCUMENTED. Internal property for testing."
                                      }
                                    }
                                  }
                                  """;

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(schemaJson, opts);
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(schema.Root!);

        SchemaNode node = top.Single(n => n.Name == "hiddenProp");
        Assert.IsTrue(node.IsUndocumented,
            "Description starting with 'UNDOCUMENTED' must set IsUndocumented=true.");
        Assert.IsFalse(node.Description?.StartsWith("UNDOCUMENTED", StringComparison.OrdinalIgnoreCase) ?? false,
            "The 'UNDOCUMENTED' prefix must be stripped from the Description.");
        Assert.IsTrue(node.Description?.Contains("Internal property") ?? false,
            "The rest of the description must be preserved.");
    }

    [TestMethod]
    public void NonUndocumented_NotFlagged()
    {
        const string schemaJson = """
                                  {
                                    "type": "object",
                                    "properties": {
                                      "normalProp": { "type": "string", "description": "Normal documented property." }
                                    }
                                  }
                                  """;

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(schemaJson, opts);
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(schema.Root!);

        Assert.IsFalse(top.Single().IsUndocumented);
    }

    [TestMethod]
    public void MidDescriptionUndocumented_SetsFlag_AndStripsMarker()
    {
        // When "UNDOCUMENTED:" appears mid-description (not at the start) the flag
        // must still be set and only the marker word stripped — the rest of the text
        // (including the sentence that follows) must be preserved.
        const string schemaJson = """
                                  {
                                    "type": "object",
                                    "properties": {
                                      "modeEnum": {
                                        "type": "string",
                                        "description": "Mode options.\n\"fast\": quick mode.\nUNDOCUMENTED. \"turbo\": experimental hidden mode."
                                      }
                                    }
                                  }
                                  """;

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(schemaJson, opts);
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(schema.Root!);

        SchemaNode node = top.Single(n => n.Name == "modeEnum");
        Assert.IsTrue(node.IsUndocumented,
            "UNDOCUMENTED mid-description must set IsUndocumented=true.");
        Assert.IsFalse(
            node.Description?.Contains("UNDOCUMENTED", StringComparison.OrdinalIgnoreCase) ?? false,
            "The UNDOCUMENTED marker must be stripped from Description.");
        Assert.IsTrue(node.Description?.Contains("\"turbo\"") ?? false,
            "The text that followed the UNDOCUMENTED marker must be preserved.");
        Assert.IsTrue(node.Description?.Contains("\"fast\"") ?? false,
            "Earlier parts of the description (before the marker) must be preserved.");
        Assert.IsTrue(node.Description?.Contains("experimental hidden mode") ?? false,
            "The sentence after the marker must be preserved in full.");
    }

    // ── SuggestedEnvVars ──────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_FindsClaudeAndAnthropicTokens()
    {
        // Simulates the real `env` property description.
        const string desc = "Configure environment variables for the session.\n" +
                            "CLAUDE_CODE_PLUGIN_GIT_TIMEOUT_MS controls the timeout.\n" +
                            "ANTHROPIC_API_KEY is the API key.";

        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(desc);

        CollectionAssert.Contains(result.ToList(), "CLAUDE_CODE_PLUGIN_GIT_TIMEOUT_MS");
        CollectionAssert.Contains(result.ToList(), "ANTHROPIC_API_KEY");
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_ReturnsEmpty_WhenNoEnvVarPhrase()
    {
        // Without "environment variable" in the text, no suggestions should be returned.
        const string desc = "CLAUDE_CODE_TIMEOUT_MS controls something.";
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(desc);
        Assert.AreEqual(0, result.Count,
            "Tokens should only be extracted when description mentions 'environment variable'.");
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_ReturnsEmpty_ForGenericTokens()
    {
        // Tokens named without an explicit "=" assignment and without ANTHROPIC_/CLAUDE
        // are not returned — prevents surfacing every generic constant like NODE_ENV.
        const string desc = "Set environment variables like PATH, HOME, NODE_ENV.";
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(desc);
        Assert.AreEqual(0, result.Count,
            "Generic tokens without ANTHROPIC_/CLAUDE or assignment syntax should not be suggested.");
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_ExtractsFromAssignmentSyntax_WithoutEnvVarPhrase()
    {
        // Variables documented as "Set NAME=value" (e.g. from Claude Code's updater channel
        // description: "Set DISABLE_AUTOUPDATER=1 to disable updates entirely.") must be
        // extracted even when the description does not contain "environment variable".
        const string desc = "Set DISABLE_AUTOUPDATER=1 to disable updates entirely.";
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(desc);
        Assert.AreEqual(1, result.Count,
            "Assignment-syntax env var should be extracted.");
        Assert.AreEqual("DISABLE_AUTOUPDATER", result[0]);
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_ExtractsMultipleAssignmentVars()
    {
        // Multiple "NAME=value" assignments in one description should all be extracted.
        const string desc = "Set MAX_THINKING_TOKENS=8000 or API_TIMEOUT_MS=30000 to tune behavior.";
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(desc);
        Assert.IsTrue(result.Contains("MAX_THINKING_TOKENS"), "MAX_THINKING_TOKENS should be extracted.");
        Assert.IsTrue(result.Contains("API_TIMEOUT_MS"), "API_TIMEOUT_MS should be extracted.");
    }

    [TestMethod]
    public void CollectSuggestedEnvVars_Deduplicates_AcrossNodes()
    {
        const string schemaJson = """
                                  {
                                    "type": "object",
                                    "properties": {
                                      "propA": {
                                        "type": "string",
                                        "description": "Set environment variables like CLAUDE_CODE_X and ANTHROPIC_Y."
                                      },
                                      "propB": {
                                        "type": "string",
                                        "description": "Also set environment variable CLAUDE_CODE_X for the timeout."
                                      }
                                    }
                                  }
                                  """;

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(schemaJson, opts);
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(schema.Root!);
        IReadOnlyList<string> all = SchemaTreeBuilder.CollectSuggestedEnvVars(top);

        // CLAUDE_CODE_X appears in both nodes — should only be in the result once.
        Assert.AreEqual(1, all.Count(v => v == "CLAUDE_CODE_X"),
            "Duplicate suggestions across nodes must be de-duplicated.");
        Assert.IsTrue(all.Contains("ANTHROPIC_Y"));
    }

    // Note: a bundled-schema smoke test for IsUndocumented was removed because all
    // UNDOCUMENTED descriptions in claude-code-settings.json appear inside specialized
    // nodes (hooks, permissions) that are classified as SchemaValueType.Complex and
    // whose children are intentionally not walked by BuildTopLevel.  The synthetic
    // tests above fully cover the detection and stripping logic.
}