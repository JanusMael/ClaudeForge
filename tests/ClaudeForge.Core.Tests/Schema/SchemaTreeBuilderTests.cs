using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Json.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

[TestClass]
public class SchemaTreeBuilderTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parse a JSON schema string in isolation (avoids global-registry collisions
    /// between test runs — mirrors the pattern used by SchemaRegistry.ParseSchema).
    /// </summary>
    private static JsonSchemaNode ParseNode(string json)
    {
        BuildOptions opts = new() { SchemaRegistry = new SchemaRegistry() };
        JsonSchema schema = JsonSchema.FromText(json, opts);
        return schema.Root!;
    }

    /// <summary>Wrap a property definition inside a root object schema so BuildTopLevel can extract it.</summary>
    private static JsonSchemaNode WrapProperty(string name, string propertyJson)
    {
        return ParseNode($@"{{""type"":""object"",""properties"":{{""{name}"":{propertyJson}}}}}");
    }

    /// <summary>Build the first (and only) top-level node from a root object schema.</summary>
    private static SchemaNode FirstTopLevel(string name, string propertyJson,
                                            ISet<string>? knownPaths = null)
    {
        JsonSchemaNode root = WrapProperty(name, propertyJson);
        IReadOnlyList<SchemaNode> nodes = knownPaths is null
            ? SchemaTreeBuilder.BuildTopLevel(root)
            : SchemaTreeBuilder.BuildTopLevel(root, knownPaths);
        return nodes[0];
    }

    // -----------------------------------------------------------------------
    // Type mapping
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TypeMapping_String_ReturnsString()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""string""}");
        Assert.AreEqual(SchemaValueType.String, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_Boolean_ReturnsBoolean()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""boolean""}");
        Assert.AreEqual(SchemaValueType.Boolean, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_Integer_ReturnsInteger()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""integer""}");
        Assert.AreEqual(SchemaValueType.Integer, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_Number_ReturnsNumber()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""number""}");
        Assert.AreEqual(SchemaValueType.Number, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_Array_ReturnsArray()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""array""}");
        Assert.AreEqual(SchemaValueType.Array, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_ObjectNoProperties_ReturnsComplex()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""object""}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_ObjectWithProperties_ReturnsObject()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""object"",""properties"":{""child"":{""type"":""string""}}}");
        Assert.AreEqual(SchemaValueType.Object, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_SpecializedName_mcpServers_ReturnsComplex()
    {
        // Even if the schema says string, SpecializedProperties override to Complex.
        SchemaNode node = FirstTopLevel("mcpServers",
            @"{""type"":""object"",""properties"":{""child"":{""type"":""string""}}}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_SpecializedName_hooks_ReturnsComplex()
    {
        SchemaNode node = FirstTopLevel("hooks", @"{""type"":""object"",""properties"":{""child"":{""type"":""string""}}}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    [TestMethod]
    public void TypeMapping_SpecializedName_permissions_ReturnsComplex()
    {
        SchemaNode node = FirstTopLevel("permissions",
            @"{""type"":""object"",""properties"":{""child"":{""type"":""string""}}}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    // -----------------------------------------------------------------------
    // Metadata extraction
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Metadata_TitleAndDescription_Populated()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""string"",""title"":""My Title"",""description"":""My description""}");
        Assert.AreEqual("My Title", node.Title);
        Assert.AreEqual("My description", node.Description);
    }

    [TestMethod]
    public void Metadata_EnumValues_Extracted()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""string"",""enum"":[""alpha"",""beta"",""gamma""]}");
        Assert.AreEqual(SchemaValueType.Enum, node.ValueType);
        CollectionAssert.AreEquivalent(new[] { "alpha", "beta", "gamma" }, node.EnumValues.ToList());
    }

    [TestMethod]
    public void Metadata_ExamplesArray_BecomesEnumValuesWhenTypeIsString()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""string"",""examples"":[""foo"",""bar""]}");
        Assert.AreEqual(SchemaValueType.Enum, node.ValueType);
        CollectionAssert.AreEquivalent(new[] { "foo", "bar" }, node.EnumValues.ToList());
    }

    [TestMethod]
    public void Metadata_DeprecatedKeyword_SetsIsDeprecated()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""string"",""deprecated"":true}");
        Assert.IsTrue(node.IsDeprecated);
    }

    [TestMethod]
    public void Metadata_DeprecatedDescriptionPrefix_SetsIsDeprecated()
    {
        // A description beginning with "DEPRECATED" sets IsDeprecated=true.
        // Unlike "UNDOCUMENTED", the prefix is NOT stripped — the full text is preserved
        // so the tooltip can still show the deprecation message to the user.
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""string"",""description"":""DEPRECATED. Use newProp instead.""}");
        Assert.IsTrue(node.IsDeprecated);
        Assert.IsNotNull(node.Description);
        StringAssert.StartsWith(node.Description, "DEPRECATED");
    }

    [TestMethod]
    public void Metadata_MinimumMaximum_PopulatedForInteger()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""integer"",""minimum"":1,""maximum"":100}");
        Assert.AreEqual(1.0, node.Minimum);
        Assert.AreEqual(100.0, node.Maximum);
    }

    [TestMethod]
    public void Metadata_MinimumMaximum_PopulatedForNumber()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""number"",""minimum"":0.5,""maximum"":9.9}");
        Assert.AreEqual(0.5, node.Minimum!.Value, 1e-9);
        Assert.AreEqual(9.9, node.Maximum!.Value, 1e-9);
    }

    // -----------------------------------------------------------------------
    // Nullable collapsing
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Nullable_AnyOfStringAndNull_IsNullableTrueAndTypeString()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""anyOf"":[{""type"":""string""},{""type"":""null""}]}");
        Assert.IsTrue(node.IsNullable);
        Assert.AreEqual(SchemaValueType.String, node.ValueType);
    }

    // -----------------------------------------------------------------------
    // anyOf / oneOf
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AnyOf_TwoNonNullVariants_ReturnsComplex()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""anyOf"":[{""type"":""string""},{""type"":""integer""}]}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    [TestMethod]
    public void OneOf_TwoVariants_ReturnsComplex()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""oneOf"":[{""type"":""string""},{""type"":""boolean""}]}");
        Assert.AreEqual(SchemaValueType.Complex, node.ValueType);
    }

    // -----------------------------------------------------------------------
    // IsNew flag
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsNew_KnownPathsNull_AlwaysFalse()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""string""}", knownPaths: null);
        Assert.IsFalse(node.IsNew);
    }

    [TestMethod]
    public void IsNew_EmptyKnownPaths_FalseFirstRun()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""string""}",
            knownPaths: new HashSet<string>());
        Assert.IsFalse(node.IsNew);
    }

    [TestMethod]
    public void IsNew_NonEmptyKnownPathsWithoutThisPath_True()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""string""}",
            knownPaths: new HashSet<string> { "other" });
        Assert.IsTrue(node.IsNew);
    }

    [TestMethod]
    public void IsNew_KnownPathsContainsThisPath_False()
    {
        SchemaNode node = FirstTopLevel("myProp", @"{""type"":""string""}",
            knownPaths: new HashSet<string> { "myProp" });
        Assert.IsFalse(node.IsNew);
    }

    // ── --showAllNew debug override ──────────────────────────────────────────

    [TestMethod]
    public void IsNew_FlagAllAsNewTrue_OverridesKnownPaths_TopLevel()
    {
        // debug flag --showAllNew forces every node to render
        // with the badge regardless of the snapshot.  Even when knownPaths
        // CONTAINS the path (which would normally suppress the badge),
        // flagAllAsNew=true wins.
        JsonSchemaNode root = WrapProperty("myProp", @"{""type"":""string""}");
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(
            root,
            knownPaths: new HashSet<string> { "myProp" },
            flagAllAsNew: true);
        Assert.IsTrue(nodes[0].IsNew,
            "flagAllAsNew=true must stamp IsNew=true even when the path is in the snapshot.");
    }

    [TestMethod]
    public void IsNew_FlagAllAsNewTrue_PropagatesToNestedChildren()
    {
        // Locks the recursion: BuildNode forwards flagAllAsNew to its
        // children + array items so the badge appears at every depth, not
        // just top-level.
        JsonSchemaNode root = WrapProperty("parent",
            @"{""type"":""object"",""properties"":{""child"":{""type"":""string""}}}");
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(
            root,
            knownPaths: new HashSet<string> { "parent", "parent.child" },
            flagAllAsNew: true);

        Assert.IsTrue(nodes[0].IsNew, "parent must be flagged new");
        Assert.AreEqual(1, nodes[0].Properties.Count, "precondition: parent has one child");
        Assert.IsTrue(nodes[0].Properties[0].IsNew,
            "Nested child must also be flagged new under flagAllAsNew=true.");
    }

    [TestMethod]
    public void IsNew_FlagAllAsNewFalse_NormalDiffSemanticsApply()
    {
        // Symmetric guard: flagAllAsNew=false is the production default and
        // must behave identically to the original two-arg overload.
        JsonSchemaNode root = WrapProperty("myProp", @"{""type"":""string""}");
        IReadOnlyList<SchemaNode> inSnapshot = SchemaTreeBuilder.BuildTopLevel(
            root,
            knownPaths: new HashSet<string> { "myProp" },
            flagAllAsNew: false);
        IReadOnlyList<SchemaNode> notInSnapshot = SchemaTreeBuilder.BuildTopLevel(
            root,
            knownPaths: new HashSet<string> { "other" },
            flagAllAsNew: false);

        Assert.IsFalse(inSnapshot[0].IsNew,
            "flagAllAsNew=false: paths in snapshot must NOT be flagged new.");
        Assert.IsTrue(notInSnapshot[0].IsNew,
            "flagAllAsNew=false: paths missing from snapshot must be flagged new.");
    }

    // -----------------------------------------------------------------------
    // ExtractSuggestedEnvVarNames (static)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_NoEnvironmentVariableMention_EmptyList()
    {
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(
            "Set this to the API key for authentication.");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_WithAnthropicApiKey_ReturnsThatToken()
    {
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(
            "Set the environment variable ANTHROPIC_API_KEY to authenticate.");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("ANTHROPIC_API_KEY", result[0]);
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_WithClaudeDebug_ReturnsThatToken()
    {
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(
            "You can also set the environment variable CLAUDE_DEBUG to enable debug output.");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("CLAUDE_DEBUG", result[0]);
    }

    [TestMethod]
    public void ExtractSuggestedEnvVarNames_WithEnvironmentVariablePhraseButNoMatchingToken_EmptyList()
    {
        IReadOnlyList<string> result = SchemaTreeBuilder.ExtractSuggestedEnvVarNames(
            "Use the environment variable to configure this option.");
        Assert.AreEqual(0, result.Count);
    }

    // -----------------------------------------------------------------------
    // Child properties
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ChildProperties_ObjectWithTwoProperties_HasTwoChildrenWithCorrectNames()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""object"",""properties"":{""alpha"":{""type"":""string""},""beta"":{""type"":""boolean""}}}");
        Assert.AreEqual(SchemaValueType.Object, node.ValueType);
        Assert.AreEqual(2, node.Properties.Count);
        HashSet<string> names = node.Properties.Select(p => p.Name).ToHashSet();
        Assert.IsTrue(names.Contains("alpha"), "Expected child named 'alpha'");
        Assert.IsTrue(names.Contains("beta"), "Expected child named 'beta'");
    }

    // -----------------------------------------------------------------------
    // ItemsSchema
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ItemsSchema_ArrayWithItemsDefinition_ItemsSchemaNotNull()
    {
        SchemaNode node = FirstTopLevel("myProp",
            @"{""type"":""array"",""items"":{""type"":""string""}}");
        Assert.AreEqual(SchemaValueType.Array, node.ValueType);
        Assert.IsNotNull(node.ItemsSchema, "ItemsSchema should not be null when items keyword is present");
        Assert.AreEqual(SchemaValueType.String, node.ItemsSchema!.ValueType);
    }

    // -----------------------------------------------------------------------
    // CollectPaths
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CollectPaths_FlatList_ReturnsAllPaths()
    {
        JsonSchemaNode root = ParseNode(@"{
            ""type"": ""object"",
            ""properties"": {
                ""propA"": {""type"": ""string""},
                ""propB"": {""type"": ""boolean""}
            }
        }");
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root);
        List<string> paths = SchemaTreeBuilder.CollectPaths(nodes).ToList();
        CollectionAssert.Contains(paths, "propA");
        CollectionAssert.Contains(paths, "propB");
        Assert.AreEqual(2, paths.Count);
    }

    [TestMethod]
    public void CollectPaths_NestedObject_ReturnsParentAndChildPaths()
    {
        JsonSchemaNode root = ParseNode(@"{
            ""type"": ""object"",
            ""properties"": {
                ""parent"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""child"": {""type"": ""string""}
                    }
                }
            }
        }");
        IReadOnlyList<SchemaNode> nodes = SchemaTreeBuilder.BuildTopLevel(root);
        List<string> paths = SchemaTreeBuilder.CollectPaths(nodes).ToList();
        CollectionAssert.Contains(paths, "parent");
        CollectionAssert.Contains(paths, "parent.child");
        Assert.AreEqual(2, paths.Count);
    }
}