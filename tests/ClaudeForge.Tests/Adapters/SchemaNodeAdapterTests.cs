using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Adapters;

[TestClass]
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

    [TestMethod]
    public void MapValueType_Boolean_MapsToBoolean()
    {
        SchemaNodeAdapter adapter = new(Node("b", SchemaValueType.Boolean));
        Assert.AreEqual(EditorValueType.Boolean, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_String_MapsToString()
    {
        SchemaNodeAdapter adapter = new(Node("s"));
        Assert.AreEqual(EditorValueType.String, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Number_MapsToNumber()
    {
        SchemaNodeAdapter adapter = new(Node("n", SchemaValueType.Number));
        Assert.AreEqual(EditorValueType.Number, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Integer_MapsToInteger()
    {
        SchemaNodeAdapter adapter = new(Node("i", SchemaValueType.Integer));
        Assert.AreEqual(EditorValueType.Integer, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Path_MapsToPath()
    {
        SchemaNodeAdapter adapter = new(Node("p", SchemaValueType.Path));
        Assert.AreEqual(EditorValueType.Path, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Enum_MapsToEnum()
    {
        SchemaNodeAdapter adapter = new(Node("e", SchemaValueType.Enum));
        Assert.AreEqual(EditorValueType.Enum, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Array_MapsToStringArray()
    {
        SchemaNodeAdapter adapter = new(Node("a", SchemaValueType.Array));
        Assert.AreEqual(EditorValueType.StringArray, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Object_MapsToObject()
    {
        SchemaNodeAdapter adapter = new(Node("o", SchemaValueType.Object));
        Assert.AreEqual(EditorValueType.Object, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Complex_MapsToComplex()
    {
        SchemaNodeAdapter adapter = new(Node("c", SchemaValueType.Complex));
        Assert.AreEqual(EditorValueType.Complex, adapter.ValueType);
    }

    [TestMethod]
    public void MapValueType_Unknown_MapsToUnknown()
    {
        SchemaNodeAdapter adapter = new(Node("u", SchemaValueType.Unknown));
        Assert.AreEqual(EditorValueType.Unknown, adapter.ValueType);
    }

    // -----------------------------------------------------------------------
    // Passthrough properties
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DisplayName_DelegatesToSchemaNode()
    {
        SchemaNode node = new("myProp", "myProp") { Title = "My Property" };
        SchemaNodeAdapter adapter = new(node);

        // IEditorSchema exposes Title and Name; callers compute DisplayName as Title ?? Name.
        Assert.AreEqual("My Property", adapter.Title);
        Assert.AreEqual("myProp", adapter.Name);
    }

    [TestMethod]
    public void Description_DelegatesToSchemaNode()
    {
        SchemaNode node = new("x", "x") { Description = "A helpful description" };
        SchemaNodeAdapter adapter = new(node);

        Assert.AreEqual("A helpful description", adapter.Description);
    }

    [TestMethod]
    public void IsReadOnly_TrueWhenSchemaManagedOnly()
    {
        SchemaNode readonlyNode = new("r", "r") { IsManagedOnly = true };
        SchemaNode writableNode = new("w", "w") { IsManagedOnly = false };

        Assert.IsTrue(new SchemaNodeAdapter(readonlyNode).IsReadOnly,
            "IsManagedOnly=true must map to IsReadOnly=true");
        Assert.IsFalse(new SchemaNodeAdapter(writableNode).IsReadOnly,
            "IsManagedOnly=false must map to IsReadOnly=false");
    }

    [TestMethod]
    public void IsNew_DelegatesToSchemaNode()
    {
        SchemaNode newNode = new("n", "n") { IsNew = true };
        SchemaNode oldNode = new("o", "o") { IsNew = false };

        Assert.IsTrue(new SchemaNodeAdapter(newNode).IsNew);
        Assert.IsFalse(new SchemaNodeAdapter(oldNode).IsNew);
    }

    [TestMethod]
    public void IsDeprecated_DelegatesToSchemaNode()
    {
        SchemaNode deprecatedNode = new("d", "d") { IsDeprecated = true };
        SchemaNode activeNode = new("a", "a") { IsDeprecated = false };

        Assert.IsTrue(new SchemaNodeAdapter(deprecatedNode).IsDeprecated);
        Assert.IsFalse(new SchemaNodeAdapter(activeNode).IsDeprecated);
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

        SchemaNodeAdapter adapter = new(parent);

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
        SchemaNodeAdapter adapter = new(node);

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

        SchemaNodeAdapter adapter = new(arrNode);

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
        SchemaNodeAdapter adapter = new(node);

        Assert.IsNull(adapter.DefaultValue,
            "null SchemaNode.DefaultValue must produce null adapter.DefaultValue.");
    }

    [TestMethod]
    public void DefaultValue_StringJsonLiteral_ReturnsString()
    {
        // SchemaNode.DefaultValue = "\"hello\"" is a JSON-encoded string literal.
        // ParseDefault → JsonNode.Parse → LayeredValueAdapter.Normalise → string "hello".
        SchemaNode node = new("x", "x") { DefaultValue = "\"hello\"" };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsInstanceOfType<string>(adapter.DefaultValue);
        Assert.AreEqual("hello", (string)adapter.DefaultValue!);
    }

    [TestMethod]
    public void DefaultValue_BoolLiteral_ReturnsBool()
    {
        SchemaNode trueNode = new("x", "x") { DefaultValue = "true" };
        SchemaNode falseNode = new("y", "y") { DefaultValue = "false" };

        Assert.IsInstanceOfType<bool>(new SchemaNodeAdapter(trueNode).DefaultValue);
        Assert.IsTrue((bool)new SchemaNodeAdapter(trueNode).DefaultValue!);
        Assert.IsFalse((bool)new SchemaNodeAdapter(falseNode).DefaultValue!);
    }

    [TestMethod]
    public void DefaultValue_NumberLiteral_ReturnsDouble()
    {
        // JSON floating-point numbers normalise to double via NormaliseScalar.
        SchemaNode node = new("x", "x") { DefaultValue = "3.14" };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsInstanceOfType<double>(adapter.DefaultValue);
        Assert.AreEqual(3.14, (double)adapter.DefaultValue!, delta: 1e-10);
    }

    [TestMethod]
    public void DefaultValue_InvalidJson_ReturnsRawString()
    {
        // ParseDefault catches JsonException and returns the raw string unchanged.
        const string rawValue = "not-valid-json{{{";
        SchemaNode node = new("x", "x") { DefaultValue = rawValue };
        SchemaNodeAdapter adapter = new(node);

        Assert.IsInstanceOfType<string>(adapter.DefaultValue);
        Assert.AreEqual(rawValue, (string)adapter.DefaultValue!,
            "Invalid JSON must be returned as the raw string.");
    }
}