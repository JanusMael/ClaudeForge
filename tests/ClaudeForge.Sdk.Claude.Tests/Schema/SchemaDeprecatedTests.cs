using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Schema;

/// <summary>
/// Claude Code's bundled schema marks <c>includeCoAuthoredBy</c> deprecated through its
/// description, and the tree builder must surface that as <see cref="SchemaNode.IsDeprecated"/>.
/// </summary>
/// <remarks>
/// The heuristic itself is tested on synthetic schemas in <c>AgentForge.Core.Tests</c>, under the
/// same class name; these two tests check it against Claude's real data.
/// </remarks>
public sealed class SchemaDeprecatedTests
{
    private static JsonSchemaNode LoadBundledClaudeCodeRoot()
    {
        byte[]? bytes = BundledProduct.ClaudeFiles.TryRead("claude-code-settings.json");
        MessageAssert.NotNull(bytes, "Claude's bundled files must carry claude-code-settings.json.");

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(Encoding.UTF8.GetString(bytes!), opts);
        return schema.Root!;
    }

    [Fact]
    public void IncludeCoAuthoredBy_IsFlagged_Deprecated_ByDescriptionHeuristic()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        SchemaNode? node = top.FirstOrDefault(n => n.Name == "includeCoAuthoredBy");
        MessageAssert.NotNull(node, "includeCoAuthoredBy property must exist at top level of the schema");

        Assert.True(node!.IsDeprecated,
            "The description begins with 'DEPRECATED' so the heuristic should mark IsDeprecated=true.");
    }

    [Fact]
    public void NonDeprecatedSibling_Is_NotFlagged()
    {
        JsonSchemaNode root = LoadBundledClaudeCodeRoot();
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);

        // `model` has no DEPRECATED prefix and no deprecated keyword.
        SchemaNode? node = top.FirstOrDefault(n => n.Name == "model");
        Assert.NotNull(node);
        Assert.False(node!.IsDeprecated,
            "Non-deprecated properties must not carry the IsDeprecated flag.");
    }
}
