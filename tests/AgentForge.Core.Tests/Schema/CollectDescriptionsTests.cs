using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Locks <see cref="SchemaTreeBuilder.CollectDescriptions"/>: it maps each
/// help-bearing node's dot-path to its description (title as fallback), recurses into
/// object properties and array item schemas, and omits nodes with no help text.
/// </summary>
public sealed class CollectDescriptionsTests
{
    [Fact]
    public void CollectDescriptions_MapsPathsToDescription_RecursingProperties()
    {
        SchemaNode child = new("permissions.defaultMode", "defaultMode")
        {
            Description = "The default permission mode.",
        };
        SchemaNode titleOnly = new("permissions.allow", "allow")
        {
            Title = "Allowed rules",
        };
        SchemaNode noHelp = new("permissions.deny", "deny");
        SchemaNode parent = new("permissions", "permissions")
        {
            Description = "Permission settings.",
            Properties = [child, titleOnly, noHelp],
        };

        IReadOnlyDictionary<string, string> map = SchemaTreeBuilder.CollectDescriptions([parent]);

        Assert.Equal("Permission settings.", map["permissions"]);
        Assert.Equal("The default permission mode.", map["permissions.defaultMode"]);
        MessageAssert.Equal("Allowed rules", map["permissions.allow"], "Title should be used when there is no description.");
        Assert.False(map.ContainsKey("permissions.deny"), "Nodes with no help text are omitted.");
    }

    [Fact]
    public void CollectDescriptions_RecursesArrayItemSchema()
    {
        SchemaNode item = new("servers[]", "item") { Description = "One server entry." };
        SchemaNode array = new("servers", "servers") { ItemsSchema = item };

        IReadOnlyDictionary<string, string> map = SchemaTreeBuilder.CollectDescriptions([array]);

        Assert.Equal("One server entry.", map["servers[]"]);
    }
}
