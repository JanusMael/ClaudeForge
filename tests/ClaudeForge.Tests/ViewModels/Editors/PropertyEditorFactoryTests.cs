using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class PropertyEditorFactoryTests
{
    private static SchemaNode Make(string name, SchemaValueType type,
                                   string[]? enumValues = null, double? min = null, double? max = null)
    {
        return new SchemaNode(name, name)
            { ValueType = type, EnumValues = enumValues ?? [], Minimum = min, Maximum = max };
    }

    [TestMethod]
    public void Boolean_CreatesBooleanEditor()
    {
        Assert.IsInstanceOfType<LibVm.BooleanPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Boolean), ConfigScope.User));
    }

    [TestMethod]
    public void String_CreatesStringEditor()
    {
        Assert.IsInstanceOfType<LibVm.StringPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.String), ConfigScope.User));
    }

    [TestMethod]
    public void Path_CreatesPathEditor()
    {
        Assert.IsInstanceOfType<LibVm.PathPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Path), ConfigScope.User));
    }

    [TestMethod]
    public void Enum_CreatesEnumEditor()
    {
        Assert.IsInstanceOfType<LibVm.EnumPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Enum, ["a", "b"]), ConfigScope.User));
    }

    [TestMethod]
    public void Integer_CreatesNumberEditor()
    {
        Assert.IsInstanceOfType<LibVm.NumberPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Integer), ConfigScope.User));
    }

    [TestMethod]
    public void Number_CreatesNumberEditor()
    {
        Assert.IsInstanceOfType<LibVm.NumberPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Number), ConfigScope.User));
    }

    [TestMethod]
    public void Array_StringItems_CreatesStringArrayEditor()
    {
        Assert.IsInstanceOfType<LibVm.StringArrayPropertyEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("x", "x")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("x[]", "x[]") { ValueType = SchemaValueType.String },
                }, ConfigScope.User));
    }

    [TestMethod]
    public void Array_NoItemsSchema_FallsBackToStringArray()
    {
        // Items unspecified → ValueType.Unknown → safe to render as strings.
        Assert.IsInstanceOfType<LibVm.StringArrayPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Array), ConfigScope.User));
    }

    [TestMethod]
    public void Array_AllowedMcpServers_DispatchesToMcpServerListEditor()
    {
        Assert.IsInstanceOfType<McpServerListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("allowedMcpServers", "allowedMcpServers")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("allowedMcpServers[]", "allowedMcpServers[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [TestMethod]
    public void Array_DeniedMcpServers_DispatchesToMcpServerListEditor()
    {
        Assert.IsInstanceOfType<McpServerListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("deniedMcpServers", "deniedMcpServers")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("deniedMcpServers[]", "deniedMcpServers[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [TestMethod]
    public void Array_StrictKnownMarketplaces_DispatchesToMarketplaceListEditor()
    {
        Assert.IsInstanceOfType<MarketplaceListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("strictKnownMarketplaces", "strictKnownMarketplaces")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("strictKnownMarketplaces[]", "strictKnownMarketplaces[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [TestMethod]
    public void Array_BlockedMarketplaces_DispatchesToMarketplaceListEditor()
    {
        Assert.IsInstanceOfType<MarketplaceListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("blockedMarketplaces", "blockedMarketplaces")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("blockedMarketplaces[]", "blockedMarketplaces[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [TestMethod]
    public void Array_OtherObjectItems_FallsBackToJsonRaw_NotStringArray()
    {
        // Anything we don't have a typed editor for stays on the JsonRaw safety
        // net so the corruption mechanic stays closed for future schemas.
        SchemaNode schema = new("someUnknownArrayProp", "someUnknownArrayProp")
        {
            ValueType = SchemaValueType.Array,
            ItemsSchema = new SchemaNode("someUnknownArrayProp[]", "someUnknownArrayProp[]")
                { ValueType = SchemaValueType.Complex },
        };
        Assert.IsInstanceOfType<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [TestMethod]
    public void Complex_Permissions_CreatesPermissionsEditor()
    {
        SchemaNode schema = new("permissions", "permissions") { ValueType = SchemaValueType.Complex };
        Assert.IsInstanceOfType<PermissionsEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [TestMethod]
    public void Complex_McpServers_CreatesMcpServersEditor()
    {
        SchemaNode schema = new("mcpServers", "mcpServers") { ValueType = SchemaValueType.Complex };
        Assert.IsInstanceOfType<McpServersEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [TestMethod]
    public void Complex_Hooks_CreatesHooksEditor()
    {
        SchemaNode schema = new("hooks", "hooks") { ValueType = SchemaValueType.Complex };
        Assert.IsInstanceOfType<HooksEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [TestMethod]
    public void Object_CreatesObjectEditor()
    {
        SchemaNode schema = new("env", "env") { ValueType = SchemaValueType.Object };
        Assert.IsInstanceOfType<ObjectPropertyEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [TestMethod]
    public void Unknown_FallsBackToJsonRawEditor()
    {
        Assert.IsInstanceOfType<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Unknown), ConfigScope.User));
    }

    [TestMethod]
    public void Complex_ModelOverrides_DispatchesToStringMapEditor()
    {
        LibVm.PropertyEditorViewModel vm = PropertyEditorFactory.Create(
            Make("modelOverrides", SchemaValueType.Complex), ConfigScope.User);
        Assert.IsInstanceOfType<StringMapPropertyEditorViewModel>(vm);
        StringMapPropertyEditorViewModel smap = (StringMapPropertyEditorViewModel)vm;
        // Factory injects the same model-id list the standalone `model`
        // editor offers — sonnet must appear so the AutoCompleteBox
        // dropdown is populated.
        CollectionAssert.Contains(smap.KeySuggestions.ToArray(), "sonnet");
    }

    [TestMethod]
    public void Complex_UnknownName_FallsBackToJsonRawEditor()
    {
        Assert.IsInstanceOfType<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("someUnknownComplex", SchemaValueType.Complex), ConfigScope.User));
    }

    [TestMethod]
    public void EnumEditor_ReceivesOptions()
    {
        LibVm.EnumPropertyEditorViewModel vm = (LibVm.EnumPropertyEditorViewModel)PropertyEditorFactory.Create(
            Make("x", SchemaValueType.Enum, ["alpha", "beta"]), ConfigScope.User);
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, vm.EnumOptions.ToArray());
    }

    // ── CompositeEditorFactory ─────────────────────────────────────────────────

    [TestMethod]
    public void Composite_RegisteredMatcher_WinsOverDefault()
    {
        CompositeEditorFactory factory = new();
        factory.Register(
            s => s.Name == "special",
            (s, scope) => new LibVm.StringPropertyEditorViewModel(new ClaudeSchemaAdapter(s), ClaudeScope.For(scope)));

        SchemaNode schema = Make("special", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        // The matcher overrides the Boolean dispatch and returns a StringPropertyEditorViewModel
        Assert.IsInstanceOfType<LibVm.StringPropertyEditorViewModel>(vm);
    }

    [TestMethod]
    public void Composite_UnmatchedSchema_FallsThroughToDefault()
    {
        CompositeEditorFactory factory = new();
        factory.Register(s => s.Name == "special",
            (s, scope) => new LibVm.StringPropertyEditorViewModel(new ClaudeSchemaAdapter(s), ClaudeScope.For(scope)));

        SchemaNode schema = Make("other", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        Assert.IsInstanceOfType<LibVm.BooleanPropertyEditorViewModel>(vm);
    }

    [TestMethod]
    public void Composite_FirstMatchWins_WhenMultipleMatchersMatch()
    {
        CompositeEditorFactory factory = new();
        factory.Register(s => s.ValueType == SchemaValueType.Boolean,
            (s, scope) =>
                new LibVm.StringPropertyEditorViewModel(new ClaudeSchemaAdapter(s),
                    ClaudeScope.For(scope))); // first registration
        factory.Register(s => s.Name == "flag",
            (s, scope) =>
                new LibVm.EnumPropertyEditorViewModel(new ClaudeSchemaAdapter(s),
                    ClaudeScope.For(scope))); // second registration

        SchemaNode schema = Make("flag", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        Assert.IsInstanceOfType<LibVm.StringPropertyEditorViewModel>(vm); // first matcher fires
    }

    [TestMethod]
    public void DefaultEditorFactory_Create_MatchesStaticShim()
    {
        DefaultEditorFactory factory = new();
        SchemaNode schema = Make("x", SchemaValueType.Boolean);

        LibVm.PropertyEditorViewModel instanceResult = factory.Create(schema, ConfigScope.User);
        LibVm.PropertyEditorViewModel staticResult = PropertyEditorFactory.Create(schema, ConfigScope.User);

        Assert.IsInstanceOfType<LibVm.BooleanPropertyEditorViewModel>(instanceResult);
        Assert.IsInstanceOfType<LibVm.BooleanPropertyEditorViewModel>(staticResult);
    }
}