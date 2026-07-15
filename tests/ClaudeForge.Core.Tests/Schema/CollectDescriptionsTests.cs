using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Locks <see cref="SchemaTreeBuilder.CollectDescriptions"/>: it maps each
/// help-bearing node's dot-path to its description (title as fallback), recurses into
/// object properties and array item schemas, and omits nodes with no help text.
/// </summary>
[TestClass]
public sealed class CollectDescriptionsTests
{
    [TestMethod]
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

        Assert.AreEqual("Permission settings.", map["permissions"]);
        Assert.AreEqual("The default permission mode.", map["permissions.defaultMode"]);
        Assert.AreEqual("Allowed rules", map["permissions.allow"], "Title should be used when there is no description.");
        Assert.IsFalse(map.ContainsKey("permissions.deny"), "Nodes with no help text are omitted.");
    }

    [TestMethod]
    public void CollectDescriptions_RecursesArrayItemSchema()
    {
        SchemaNode item = new("servers[]", "item") { Description = "One server entry." };
        SchemaNode array = new("servers", "servers") { ItemsSchema = item };

        IReadOnlyDictionary<string, string> map = SchemaTreeBuilder.CollectDescriptions([array]);

        Assert.AreEqual("One server entry.", map["servers[]"]);
    }
}
