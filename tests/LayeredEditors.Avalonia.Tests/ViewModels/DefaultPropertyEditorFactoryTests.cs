namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class DefaultPropertyEditorFactoryTests
{
    private readonly DefaultPropertyEditorFactory _factory = new();

    private static IEditorScope Scope => FakeEditorScope.User;

    private static FakeEditorSchema Schema(EditorValueType t, string name = "prop")
    {
        return new FakeEditorSchema($"root.{name}", t) { EnumValues = t == EditorValueType.Enum ? ["a", "b"] : null };
    }

    // ── Basic dispatch ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Create_Boolean_ReturnsBooleanVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Boolean), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(BooleanPropertyEditorViewModel));
    }

    [TestMethod]
    public void Create_String_ReturnsStringVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.String), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(StringPropertyEditorViewModel));
    }

    [TestMethod]
    public void Create_Path_ReturnsPathVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Path), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(PathPropertyEditorViewModel));
    }

    [TestMethod]
    public void Create_Enum_ReturnsEnumVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Enum), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(EnumPropertyEditorViewModel));
        Assert.AreEqual(2, ((EnumPropertyEditorViewModel)vm).EnumOptions.Count);
    }

    [TestMethod]
    public void Create_Integer_ReturnsNumberVM_IsInteger()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Integer), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(NumberPropertyEditorViewModel));
        Assert.IsTrue(((NumberPropertyEditorViewModel)vm).IsInteger);
    }

    [TestMethod]
    public void Create_Number_ReturnsNumberVM_NotInteger()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Number), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(NumberPropertyEditorViewModel));
        Assert.IsFalse(((NumberPropertyEditorViewModel)vm).IsInteger);
    }

    [TestMethod]
    public void Create_StringArray_ReturnsStringArrayVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.StringArray), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(StringArrayPropertyEditorViewModel));
    }

    [TestMethod]
    public void Create_Object_ReturnsObjectVM_WithChildren()
    {
        FakeEditorSchema childSchema = new("root.obj.child");
        FakeEditorSchema parentSchema = new("root.obj", EditorValueType.Object)
        {
            Properties = [childSchema],
        };

        PropertyEditorViewModel vm = _factory.Create(parentSchema, null, Scope);
        Assert.IsInstanceOfType(vm, typeof(ObjectPropertyEditorViewModel));
        Assert.AreEqual(1, ((ObjectPropertyEditorViewModel)vm).Children.Count);
    }

    [TestMethod]
    public void Create_Unknown_FallsBackToStringVM()
    {
        PropertyEditorViewModel vm = _factory.Create(Schema(EditorValueType.Unknown), null, Scope);
        Assert.IsInstanceOfType(vm, typeof(StringPropertyEditorViewModel));
    }

    // ── CreateForGroup ─────────────────────────────────────────────────────────

    [TestMethod]
    public void CreateForGroup_CreatesManyEditors()
    {
        IReadOnlyList<IEditorSchema> schemas =
        [
            Schema(EditorValueType.Boolean, "flag"),
            Schema(EditorValueType.String, "label"),
            Schema(EditorValueType.Integer, "count"),
        ];

        IReadOnlyList<PropertyEditorViewModel> vms = _factory.CreateForGroup(schemas, null, Scope);

        Assert.AreEqual(3, vms.Count);
        Assert.IsInstanceOfType(vms[0], typeof(BooleanPropertyEditorViewModel));
        Assert.IsInstanceOfType(vms[1], typeof(StringPropertyEditorViewModel));
        Assert.IsInstanceOfType(vms[2], typeof(NumberPropertyEditorViewModel));
    }
}

[TestClass]
public class CompositePropertyEditorFactoryTests
{
    private static IEditorScope Scope => FakeEditorScope.User;

    // ── Registration and dispatch ─────────────────────────────────────────────

    [TestMethod]
    public void Register_MatchedSchema_UsesCustomFactory()
    {
        CompositePropertyEditorFactory factory = new();
        factory.Register(
            s => s.Name == "special",
            (s, ws, scope, ctx) => new StringArrayPropertyEditorViewModel(s, scope));

        FakeEditorSchema schema = new("root.special");
        PropertyEditorViewModel vm = factory.Create(schema, null, Scope);

        // Even though the schema says String, the registered factory returned StringArray
        Assert.IsInstanceOfType(vm, typeof(StringArrayPropertyEditorViewModel));
    }

    [TestMethod]
    public void Register_UnmatchedSchema_FallsThroughToDefault()
    {
        CompositePropertyEditorFactory factory = new();
        factory.Register(s => s.Name == "other", (s, ws, scope, ctx) =>
            new BooleanPropertyEditorViewModel(s, scope));

        FakeEditorSchema schema = new("root.prop");
        PropertyEditorViewModel vm = factory.Create(schema, null, Scope);

        Assert.IsInstanceOfType(vm, typeof(StringPropertyEditorViewModel));
    }

    [TestMethod]
    public void Register_MultipleMatchers_FirstMatchWins()
    {
        CompositePropertyEditorFactory factory = new();
        factory.Register(
            s => s.ValueType == EditorValueType.String,
            (s, ws, scope, ctx) => new BooleanPropertyEditorViewModel(s, scope)); // first: string→bool
        factory.Register(
            s => s.Name == "label",
            (s, ws, scope, ctx) => new NumberPropertyEditorViewModel(s, scope)); // second: label→number

        FakeEditorSchema schema = new("root.label");
        PropertyEditorViewModel vm = factory.Create(schema, null, Scope);

        // First matcher fires (type = String) → Boolean
        Assert.IsInstanceOfType(vm, typeof(BooleanPropertyEditorViewModel));
    }

    // ── Acceptance test: non-Claude, non-JSON consumer ─────────────────────────
    // Verifies the plan's hard Definition of Done:
    //   "A non-Claude, non-JSON consumer can drive the library end-to-end
    //    — creating editors, reading/writing values through the workspace,
    //    firing ValueChanged — without touching ClaudeForge.Core or System.Text.Json."

    [TestMethod]
    public void NonClaudeConsumer_EndToEnd_NoJsonNoDomainTypes()
    {
        // Arrange: a factory with one custom editor registered
        CompositePropertyEditorFactory factory = new();
        factory.Register(
            s => s.Name == "tags",
            (s, ws, scope, ctx) => new StringArrayPropertyEditorViewModel(s, scope));

        // Arrange: a workspace pre-seeded entirely through the interface
        FakeEditorWorkspace ws = new([FakeEditorScope.User, FakeEditorScope.Project]);
        ws.TrackEvents();

        ws.Seed("app.enabled", FakeEditorScope.User, false);
        ws.Seed("app.label", FakeEditorScope.Project, "project-label");
        ws.Seed("app.tags", FakeEditorScope.User, (IReadOnlyList<object?>)["x", "y"]);

        IReadOnlyList<IEditorSchema> schemas =
        [
            new FakeEditorSchema("app.enabled", EditorValueType.Boolean),
            new FakeEditorSchema("app.label"),
            new FakeEditorSchema("app.tags", EditorValueType.StringArray),
        ];

        // Act: create editors
        IReadOnlyList<PropertyEditorViewModel> editors = factory.CreateForGroup(schemas, ws, FakeEditorScope.User);

        // Act: load values
        foreach (PropertyEditorViewModel ed in editors)
        {
            ed.LoadFromValue(ws.GetValue(ed.Path), FakeEditorScope.User);
        }

        BooleanPropertyEditorViewModel enabledVm = (BooleanPropertyEditorViewModel)editors[0];
        StringPropertyEditorViewModel labelVm = (StringPropertyEditorViewModel)editors[1];
        StringArrayPropertyEditorViewModel tagsVm = (StringArrayPropertyEditorViewModel)editors[2];

        // Assert: editors reflect loaded values
        Assert.IsFalse(enabledVm.Value); // set at User
        Assert.IsNull(labelVm.Value); // set at Project only, editing User
        Assert.AreEqual(2, tagsVm.Items.Count); // custom factory used
        Assert.AreEqual("x", tagsVm.Items[0]);

        // Act: mutate through workspace
        ws.SetValue("app.label", "user-override", FakeEditorScope.User);
        Assert.AreEqual(1, ws.TrackedEventCount);

        // Reload label editor
        labelVm.LoadFromValue(ws.GetValue("app.label"), FakeEditorScope.User);
        Assert.AreEqual("user-override", labelVm.Value);

        // Act: round-trip
        object? roundTrip = labelVm.ToValue();
        Assert.AreEqual("user-override", roundTrip);

        // Act: reset
        enabledVm.ResetToInheritedCommand.Execute(null);
        Assert.IsNull(enabledVm.Value);
        Assert.IsFalse(enabledVm.IsModified);
    }
}