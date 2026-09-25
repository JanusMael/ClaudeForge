using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ScopedEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

public class SchemaNodeAdapterTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal SchemaNode at the root path with default settings.
    /// </summary>
    private static SchemaNode Node(string name = "prop", SchemaValueType type = SchemaValueType.String)
    {
        return new SchemaNode(name, name) { ValueType = type };
    }

    // -----------------------------------------------------------------------
    // SchemaValueType → EditorValueType mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void MapValueType_Boolean_MapsToBoolean()
    {
        SchemaNodeAdapter adapter = new(Node("b", SchemaValueType.Boolean));
        Assert.Equal(EditorValueType.Boolean, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_String_MapsToString()
    {
        SchemaNodeAdapter adapter = new(Node("s"));
        Assert.Equal(EditorValueType.String, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Number_MapsToNumber()
    {
        SchemaNodeAdapter adapter = new(Node("n", SchemaValueType.Number));
        Assert.Equal(EditorValueType.Number, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Integer_MapsToInteger()
    {
        SchemaNodeAdapter adapter = new(Node("i", SchemaValueType.Integer));
        Assert.Equal(EditorValueType.Integer, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Path_MapsToPath()
    {
        SchemaNodeAdapter adapter = new(Node("p", SchemaValueType.Path));
        Assert.Equal(EditorValueType.Path, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Enum_MapsToEnum()
    {
        SchemaNodeAdapter adapter = new(Node("e", SchemaValueType.Enum));
        Assert.Equal(EditorValueType.Enum, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Array_MapsToStringArray()
    {
        SchemaNodeAdapter adapter = new(Node("a", SchemaValueType.Array));
        Assert.Equal(EditorValueType.StringArray, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Object_MapsToObject()
    {
        SchemaNodeAdapter adapter = new(Node("o", SchemaValueType.Object));
        Assert.Equal(EditorValueType.Object, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Complex_MapsToComplex()
    {
        SchemaNodeAdapter adapter = new(Node("c", SchemaValueType.Complex));
        Assert.Equal(EditorValueType.Complex, adapter.ValueType);
    }

    [Fact]
    public void MapValueType_Unknown_MapsToUnknown()
    {
        SchemaNodeAdapter adapter = new(Node("u", SchemaValueType.Unknown));
        Assert.Equal(EditorValueType.Unknown, adapter.ValueType);
    }

    // -----------------------------------------------------------------------
    // Passthrough properties
    // -----------------------------------------------------------------------

    [Fact]
    public void DisplayName_DelegatesToSchemaNode()
    {
        SchemaNode node = new("myProp", "myProp") { Title = "My Property" };
        SchemaNodeAdapter adapter = new(node);

        // IEditorSchema exposes Title and Name; callers compute DisplayName as Title ?? Name.
        Assert.Equal("My Property", adapter.Title);
        Assert.Equal("myProp", adapter.Name);
    }

    [Fact]
    public void Description_DelegatesToSchemaNode()
    {
        SchemaNode node = new("x", "x") { Description = "A helpful description" };
        SchemaNodeAdapter adapter = new(node);

        Assert.Equal("A helpful description", adapter.Description);
    }

    [Fact]
    public void IsReadOnly_TrueWhenSchemaManagedOnly()
    {
        SchemaNode readonlyNode = new("r", "r") { IsManagedOnly = true };
        SchemaNode writableNode = new("w", "w") { IsManagedOnly = false };

        Assert.True(new SchemaNodeAdapter(readonlyNode).IsReadOnly,
            "IsManagedOnly=true must map to IsReadOnly=true");
        Assert.False(new SchemaNodeAdapter(writableNode).IsReadOnly,
            "IsManagedOnly=false must map to IsReadOnly=false");
    }

    [Fact]
    public void IsNew_DelegatesToSchemaNode()
    {
        SchemaNode newNode = new("n", "n") { IsNew = true };
        SchemaNode oldNode = new("o", "o") { IsNew = false };

        Assert.True(new SchemaNodeAdapter(newNode).IsNew);
        Assert.False(new SchemaNodeAdapter(oldNode).IsNew);
    }

    [Fact]
    public void IsDeprecated_DelegatesToSchemaNode()
    {
        SchemaNode deprecatedNode = new("d", "d") { IsDeprecated = true };
        SchemaNode activeNode = new("a", "a") { IsDeprecated = false };

        Assert.True(new SchemaNodeAdapter(deprecatedNode).IsDeprecated);
        Assert.False(new SchemaNodeAdapter(activeNode).IsDeprecated);
    }

    [Fact]
    public void Properties_ReturnsWrappedChildren()
    {
        SchemaNode child = new("childProp", "childProp") { Title = "Child Title" };
        SchemaNode parent = new("parent", "parent")
        {
            ValueType = SchemaValueType.Object,
            Properties = [child],
        };

        SchemaNodeAdapter adapter = new(parent);

        MessageAssert.Equal(1, adapter.Properties.Count,
            "Adapter must expose the single child property.");
        MessageAssert.Equal("Child Title", adapter.Properties[0].Title,
            "Child adapter's Title must match the inner SchemaNode's Title.");
        MessageAssert.Equal("childProp", adapter.Properties[0].Name,
            "Child adapter's Name must match the inner SchemaNode's Name.");
    }

    [Fact]
    public void ItemsSchema_NullWhenSchemaHasNoItemsSchema()
    {
        SchemaNode node = new("arr", "arr") { ValueType = SchemaValueType.Array };
        SchemaNodeAdapter adapter = new(node);

        MessageAssert.Null(adapter.ItemsSchema,
            "ItemsSchema must be null when SchemaNode.ItemsSchema is null.");
    }

    [Fact]
    public void ItemsSchema_NonNullWhenSchemaHasItemsSchema()
    {
        SchemaNode itemNode = new("item", "item") { ValueType = SchemaValueType.String };
        SchemaNode arrNode = new("arr", "arr")
        {
            ValueType = SchemaValueType.Array,
            ItemsSchema = itemNode,
        };

        SchemaNodeAdapter adapter = new(arrNode);

        MessageAssert.NotNull(adapter.ItemsSchema,
            "ItemsSchema must be non-null when SchemaNode.ItemsSchema is set.");
        MessageAssert.Equal(EditorValueType.String, adapter.ItemsSchema!.ValueType,
            "ItemsSchema ValueType must be mapped from the inner SchemaNode.");
    }

    // -----------------------------------------------------------------------
    // ParseDefault
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultValue_NullSchemaDefault_ReturnsNull()
    {
        SchemaNode node = new("x", "x") { DefaultValue = null };
        SchemaNodeAdapter adapter = new(node);

        MessageAssert.Null(adapter.DefaultValue,
            "null SchemaNode.DefaultValue must produce null adapter.DefaultValue.");
    }

    [Fact]
    public void DefaultValue_StringJsonLiteral_ReturnsString()
    {
        // SchemaNode.DefaultValue = "\"hello\"" is a JSON-encoded string literal.
        // ParseDefault → JsonNode.Parse → LayeredValueAdapter.Normalise → string "hello".
        SchemaNode node = new("x", "x") { DefaultValue = "\"hello\"" };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsAssignableFrom<string>(adapter.DefaultValue);
        Assert.Equal("hello", (string)adapter.DefaultValue!);
    }

    [Fact]
    public void DefaultValue_BoolLiteral_ReturnsBool()
    {
        SchemaNode trueNode = new("x", "x") { DefaultValue = "true" };
        SchemaNode falseNode = new("y", "y") { DefaultValue = "false" };

        Assert.IsAssignableFrom<bool>(new SchemaNodeAdapter(trueNode).DefaultValue);
        Assert.True((bool)new SchemaNodeAdapter(trueNode).DefaultValue!);
        Assert.False((bool)new SchemaNodeAdapter(falseNode).DefaultValue!);
    }

    [Fact]
    public void DefaultValue_NumberLiteral_ReturnsDouble()
    {
        // JSON floating-point numbers normalise to double via NormaliseScalar.
        SchemaNode node = new("x", "x") { DefaultValue = "3.14" };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsAssignableFrom<double>(adapter.DefaultValue);
        Assert.Equal(3.14, (double)adapter.DefaultValue!, 1e-10);
    }

    [Fact]
    public void DefaultValue_InvalidJson_ReturnsRawString()
    {
        // ParseDefault catches JsonException and returns the raw string unchanged.
        const string rawValue = "not-valid-json{{{";
        SchemaNode node = new("x", "x") { DefaultValue = rawValue };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsAssignableFrom<string>(adapter.DefaultValue);
        MessageAssert.Equal(rawValue, (string)adapter.DefaultValue!,
            "Invalid JSON must be returned as the raw string.");
    }
}