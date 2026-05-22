using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

[TestClass]
public class ClaudeSchemaAdapterTests
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

    [TestMethod]
    public void MapValueType_Boolean_MapsToBoolean()
    {
        ClaudeSchemaAdapter adapter = new(Node("b", SchemaValueType.Boolean));
        Assert.AreEqual(EditorValueType.Boolean, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_String_MapsToString()
    {
        ClaudeSchemaAdapter adapter = new(Node("s"));
        Assert.AreEqual(EditorValueType.String, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Number_MapsToNumber()
    {
        ClaudeSchemaAdapter adapter = new(Node("n", SchemaValueType.Number));
        Assert.AreEqual(EditorValueType.Number, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Integer_MapsToInteger()
    {
        ClaudeSchemaAdapter adapter = new(Node("i", SchemaValueType.Integer));
        Assert.AreEqual(EditorValueType.Integer, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Path_MapsToPath()
    {
        ClaudeSchemaAdapter adapter = new(Node("p", SchemaValueType.Path));
        Assert.AreEqual(EditorValueType.Path, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Enum_MapsToEnum()
    {
        ClaudeSchemaAdapter adapter = new(Node("e", SchemaValueType.Enum));
        Assert.AreEqual(EditorValueType.Enum, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Array_MapsToStringArray()
    {
        ClaudeSchemaAdapter adapter = new(Node("a", SchemaValueType.Array));
        Assert.AreEqual(EditorValueType.StringArray, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Object_MapsToObject()
    {
        ClaudeSchemaAdapter adapter = new(Node("o", SchemaValueType.Object));
        Assert.AreEqual(EditorValueType.Object, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Complex_MapsToComplex()
    {
        ClaudeSchemaAdapter adapter = new(Node("c", SchemaValueType.Complex));
        Assert.AreEqual(EditorValueType.Complex, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Unknown_MapsToUnknown()
    {
        ClaudeSchemaAdapter adapter = new(Node("u", SchemaValueType.Unknown));
        Assert.AreEqual(EditorValueType.Unknown, adapter.ValueType);
    }

    // -----------------------------------------------------------------------
    // Passthrough properties
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DisplayName_DelegatesToSchemaNode()
    {
        SchemaNode node = new("myProp", "myProp") { Title = "My Property" };
        ClaudeSchemaAdapter adapter = new(node);

        // IEditorSchema exposes Title and Name; callers compute DisplayName as Title ?? Name.
        Assert.AreEqual("My Property", adapter.Title);
        Assert.AreEqual("myProp", adapter.Name);
    }

    [TestMethod]
    public void Description_DelegatesToSchemaNode()
    {
        SchemaNode node = new("x", "x") { Description = "A helpful description" };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.AreEqual("A helpful description", adapter.Description);
    }

    [TestMethod]
    public void IsReadOnly_TrueWhenSchemaManagedOnly()
    {
        SchemaNode readonlyNode = new("r", "r") { IsManagedOnly = true };
        SchemaNode writableNode = new("w", "w") { IsManagedOnly = false };

        Assert.IsTrue(new ClaudeSchemaAdapter(readonlyNode).IsReadOnly,
            "IsManagedOnly=true must map to IsReadOnly=true");
        Assert.IsFalse(new ClaudeSchemaAdapter(writableNode).IsReadOnly,
            "IsManagedOnly=false must map to IsReadOnly=false");
    }

    [TestMethod]
    public void IsNew_DelegatesToSchemaNode()
    {
        SchemaNode newNode = new("n", "n") { IsNew = true };
        SchemaNode oldNode = new("o", "o") { IsNew = false };

        Assert.IsTrue(new ClaudeSchemaAdapter(newNode).IsNew);
        Assert.IsFalse(new ClaudeSchemaAdapter(oldNode).IsNew);
    }

    [TestMethod]
    public void IsDeprecated_DelegatesToSchemaNode()
    {
        SchemaNode deprecatedNode = new("d", "d") { IsDeprecated = true };
        SchemaNode activeNode = new("a", "a") { IsDeprecated = false };

        Assert.IsTrue(new ClaudeSchemaAdapter(deprecatedNode).IsDeprecated);
        Assert.IsFalse(new ClaudeSchemaAdapter(activeNode).IsDeprecated);
    }

    [TestMethod]
    public void Properties_ReturnsWrappedChildren()
    {
        SchemaNode child = new("childProp", "childProp") { Title = "Child Title" };
        SchemaNode parent = new("parent", "parent")
        {
            ValueType = SchemaValueType.Object,
            Properties = [child],
        };

        ClaudeSchemaAdapter adapter = new(parent);

        Assert.AreEqual(1, adapter.Properties.Count,
            "Adapter must expose the single child property.");
        Assert.AreEqual("Child Title", adapter.Properties[0].Title,
            "Child adapter's Title must match the inner SchemaNode's Title.");
        Assert.AreEqual("childProp", adapter.Properties[0].Name,
            "Child adapter's Name must match the inner SchemaNode's Name.");
    }

    [TestMethod]
    public void ItemsSchema_NullWhenSchemaHasNoItemsSchema()
    {
        SchemaNode node = new("arr", "arr") { ValueType = SchemaValueType.Array };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.IsNull(adapter.ItemsSchema,
            "ItemsSchema must be null when SchemaNode.ItemsSchema is null.");
    }

    [TestMethod]
    public void ItemsSchema_NonNullWhenSchemaHasItemsSchema()
    {
        SchemaNode itemNode = new("item", "item") { ValueType = SchemaValueType.String };
        SchemaNode arrNode = new("arr", "arr")
        {
            ValueType = SchemaValueType.Array,
            ItemsSchema = itemNode,
        };

        ClaudeSchemaAdapter adapter = new(arrNode);

        Assert.IsNotNull(adapter.ItemsSchema,
            "ItemsSchema must be non-null when SchemaNode.ItemsSchema is set.");
        Assert.AreEqual(EditorValueType.String, adapter.ItemsSchema!.ValueType,
            "ItemsSchema ValueType must be mapped from the inner SchemaNode.");
    }

    // -----------------------------------------------------------------------
    // ParseDefault
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DefaultValue_NullSchemaDefault_ReturnsNull()
    {
        SchemaNode node = new("x", "x") { DefaultValue = null };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.IsNull(adapter.DefaultValue,
            "null SchemaNode.DefaultValue must produce null adapter.DefaultValue.");
    }

    [TestMethod]
    public void DefaultValue_StringJsonLiteral_ReturnsString()
    {
        // SchemaNode.DefaultValue = "\"hello\"" is a JSON-encoded string literal.
        // ParseDefault → JsonNode.Parse → ClaudeValueAdapter.Normalise → string "hello".
        SchemaNode node = new("x", "x") { DefaultValue = "\"hello\"" };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.IsInstanceOfType<string>(adapter.DefaultValue);
        Assert.AreEqual("hello", (string)adapter.DefaultValue!);
    }

    [TestMethod]
    public void DefaultValue_BoolLiteral_ReturnsBool()
    {
        SchemaNode trueNode = new("x", "x") { DefaultValue = "true" };
        SchemaNode falseNode = new("y", "y") { DefaultValue = "false" };

        Assert.IsInstanceOfType<bool>(new ClaudeSchemaAdapter(trueNode).DefaultValue);
        Assert.IsTrue((bool)new ClaudeSchemaAdapter(trueNode).DefaultValue!);
        Assert.IsFalse((bool)new ClaudeSchemaAdapter(falseNode).DefaultValue!);
    }

    [TestMethod]
    public void DefaultValue_NumberLiteral_ReturnsDouble()
    {
        // JSON floating-point numbers normalise to double via NormaliseScalar.
        SchemaNode node = new("x", "x") { DefaultValue = "3.14" };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.IsInstanceOfType<double>(adapter.DefaultValue);
        Assert.AreEqual(3.14, (double)adapter.DefaultValue!, delta: 1e-10);
    }

    [TestMethod]
    public void DefaultValue_InvalidJson_ReturnsRawString()
    {
        // ParseDefault catches JsonException and returns the raw string unchanged.
        const string rawValue = "not-valid-json{{{";
        SchemaNode node = new("x", "x") { DefaultValue = rawValue };
        ClaudeSchemaAdapter adapter = new(node);

        Assert.IsInstanceOfType<string>(adapter.DefaultValue);
        Assert.AreEqual(rawValue, (string)adapter.DefaultValue!,
            "Invalid JSON must be returned as the raw string.");
    }
}