using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

public class PropertyEditorFactoryTests
{
    private static SchemaNode Make(string name, SchemaValueType type,
                                   string[]? enumValues = null, double? min = null, double? max = null)
    {
        return new SchemaNode(name, name)
            { ValueType = type, EnumValues = enumValues ?? [], Minimum = min, Maximum = max };
    }

    [Fact]
    public void Boolean_CreatesBooleanEditor()
    {
        Assert.IsAssignableFrom<LibVm.BooleanPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Boolean), ConfigScope.User));
    }

    [Fact]
    public void String_CreatesStringEditor()
    {
        Assert.IsAssignableFrom<LibVm.StringPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.String), ConfigScope.User));
    }

    [Fact]
    public void Path_CreatesPathEditor()
    {
        Assert.IsAssignableFrom<LibVm.PathPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Path), ConfigScope.User));
    }

    [Fact]
    public void Enum_CreatesEnumEditor()
    {
        Assert.IsAssignableFrom<LibVm.EnumPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Enum, ["a", "b"]), ConfigScope.User));
    }

    [Fact]
    public void Integer_CreatesNumberEditor()
    {
        Assert.IsAssignableFrom<LibVm.NumberPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Integer), ConfigScope.User));
    }

    [Fact]
    public void Number_CreatesNumberEditor()
    {
        Assert.IsAssignableFrom<LibVm.NumberPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Number), ConfigScope.User));
    }

    [Fact]
    public void Array_StringItems_CreatesStringArrayEditor()
    {
        Assert.IsAssignableFrom<LibVm.StringArrayPropertyEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("x", "x")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("x[]", "x[]") { ValueType = SchemaValueType.String },
                }, ConfigScope.User));
    }

    [Fact]
    public void Array_NoItemsSchema_FallsBackToStringArray()
    {
        // Items unspecified → ValueType.Unknown → safe to render as strings.
        Assert.IsAssignableFrom<LibVm.StringArrayPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Array), ConfigScope.User));
    }

    [Fact]
    public void Array_AllowedMcpServers_DispatchesToMcpServerListEditor()
    {
        Assert.IsAssignableFrom<McpServerListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("allowedMcpServers", "allowedMcpServers")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("allowedMcpServers[]", "allowedMcpServers[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [Fact]
    public void Array_DeniedMcpServers_DispatchesToMcpServerListEditor()
    {
        Assert.IsAssignableFrom<McpServerListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("deniedMcpServers", "deniedMcpServers")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("deniedMcpServers[]", "deniedMcpServers[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [Fact]
    public void Array_StrictKnownMarketplaces_DispatchesToMarketplaceListEditor()
    {
        Assert.IsAssignableFrom<MarketplaceListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("strictKnownMarketplaces", "strictKnownMarketplaces")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("strictKnownMarketplaces[]", "strictKnownMarketplaces[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [Fact]
    public void Array_BlockedMarketplaces_DispatchesToMarketplaceListEditor()
    {
        Assert.IsAssignableFrom<MarketplaceListEditorViewModel>(
            PropertyEditorFactory.Create(
                new SchemaNode("blockedMarketplaces", "blockedMarketplaces")
                {
                    ValueType = SchemaValueType.Array,
                    ItemsSchema = new SchemaNode("blockedMarketplaces[]", "blockedMarketplaces[]")
                        { ValueType = SchemaValueType.Complex },
                }, ConfigScope.User));
    }

    [Fact]
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
        Assert.IsAssignableFrom<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [Fact]
    public void Complex_Permissions_CreatesPermissionsEditor()
    {
        SchemaNode schema = new("permissions", "permissions") { ValueType = SchemaValueType.Complex };
        Assert.IsAssignableFrom<PermissionsEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [Fact]
    public void Complex_McpServers_CreatesMcpServersEditor()
    {
        SchemaNode schema = new("mcpServers", "mcpServers") { ValueType = SchemaValueType.Complex };
        Assert.IsAssignableFrom<McpServersEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [Fact]
    public void Complex_Hooks_CreatesHooksEditor()
    {
        SchemaNode schema = new("hooks", "hooks") { ValueType = SchemaValueType.Complex };
        Assert.IsAssignableFrom<HooksEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [Fact]
    public void Object_CreatesObjectEditor()
    {
        SchemaNode schema = new("env", "env") { ValueType = SchemaValueType.Object };
        Assert.IsAssignableFrom<ObjectPropertyEditorViewModel>(
            PropertyEditorFactory.Create(schema, ConfigScope.User));
    }

    [Fact]
    public void Unknown_FallsBackToJsonRawEditor()
    {
        Assert.IsAssignableFrom<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("x", SchemaValueType.Unknown), ConfigScope.User));
    }

    [Fact]
    public void Complex_ModelOverrides_DispatchesToStringMapEditor()
    {
        LibVm.PropertyEditorViewModel vm = PropertyEditorFactory.Create(
            Make("modelOverrides", SchemaValueType.Complex), ConfigScope.User);
        Assert.IsAssignableFrom<StringMapPropertyEditorViewModel>(vm);
        StringMapPropertyEditorViewModel smap = (StringMapPropertyEditorViewModel)vm;
        // Factory injects the same model-id list the standalone `model`
        // editor offers — sonnet must appear so the AutoCompleteBox
        // dropdown is populated.
        Assert.Contains("sonnet", smap.KeySuggestions.ToArray());
    }

    [Fact]
    public void Complex_UnknownName_FallsBackToJsonRawEditor()
    {
        Assert.IsAssignableFrom<JsonRawPropertyEditorViewModel>(
            PropertyEditorFactory.Create(Make("someUnknownComplex", SchemaValueType.Complex), ConfigScope.User));
    }

    [Fact]
    public void EnumEditor_ReceivesOptions()
    {
        LibVm.EnumPropertyEditorViewModel vm = (LibVm.EnumPropertyEditorViewModel)PropertyEditorFactory.Create(
            Make("x", SchemaValueType.Enum, ["alpha", "beta"]), ConfigScope.User);
        Assert.Equal(new[] { "alpha", "beta" }, vm.EnumOptions.ToArray());
    }

    // ── CompositeEditorFactory ─────────────────────────────────────────────────

    [Fact]
    public void Composite_RegisteredMatcher_WinsOverDefault()
    {
        CompositeEditorFactory factory = new();
        factory.Register(
            s => s.Name == "special",
            (s, scope) => new LibVm.StringPropertyEditorViewModel(new SchemaNodeAdapter(s), ConfigScopeAdapter.For(scope)));

        SchemaNode schema = Make("special", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        // The matcher overrides the Boolean dispatch and returns a StringPropertyEditorViewModel
        Assert.IsAssignableFrom<LibVm.StringPropertyEditorViewModel>(vm);
    }

    [Fact]
    public void Composite_UnmatchedSchema_FallsThroughToDefault()
    {
        CompositeEditorFactory factory = new();
        factory.Register(s => s.Name == "special",
            (s, scope) => new LibVm.StringPropertyEditorViewModel(new SchemaNodeAdapter(s), ConfigScopeAdapter.For(scope)));

        SchemaNode schema = Make("other", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        Assert.IsAssignableFrom<LibVm.BooleanPropertyEditorViewModel>(vm);
    }

    [Fact]
    public void Composite_FirstMatchWins_WhenMultipleMatchersMatch()
    {
        CompositeEditorFactory factory = new();
        factory.Register(s => s.ValueType == SchemaValueType.Boolean,
            (s, scope) =>
                new LibVm.StringPropertyEditorViewModel(new SchemaNodeAdapter(s),
                    ConfigScopeAdapter.For(scope))); // first registration
        factory.Register(s => s.Name == "flag",
            (s, scope) =>
                new LibVm.EnumPropertyEditorViewModel(new SchemaNodeAdapter(s),
                    ConfigScopeAdapter.For(scope))); // second registration

        SchemaNode schema = Make("flag", SchemaValueType.Boolean);
        LibVm.PropertyEditorViewModel vm = factory.Create(schema, ConfigScope.User);

        Assert.IsAssignableFrom<LibVm.StringPropertyEditorViewModel>(vm); // first matcher fires
    }

    [Fact]
    public void DefaultEditorFactory_Create_MatchesStaticShim()
    {
        DefaultEditorFactory factory = new();
        SchemaNode schema = Make("x", SchemaValueType.Boolean);

        LibVm.PropertyEditorViewModel instanceResult = factory.Create(schema, ConfigScope.User);
        LibVm.PropertyEditorViewModel staticResult = PropertyEditorFactory.Create(schema, ConfigScope.User);

        Assert.IsAssignableFrom<LibVm.BooleanPropertyEditorViewModel>(instanceResult);
        Assert.IsAssignableFrom<LibVm.BooleanPropertyEditorViewModel>(staticResult);
    }
}