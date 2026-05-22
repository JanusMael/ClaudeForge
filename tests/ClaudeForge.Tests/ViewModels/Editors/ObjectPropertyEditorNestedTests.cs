using System.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// focused tests for the App-side
/// <see cref="ObjectPropertyEditorViewModel"/>'s nested-object behaviour:
/// <c>ToJsonValue</c> recursion, per-scope projection in
/// <c>LoadFromLayered</c>, and child <c>IsModified</c> propagation
/// (including the force-fire path).
/// </summary>
/// <remarks>
/// Uses a <see cref="FakeLeafEditor"/> that directly inherits the App-bridge
/// <see cref="PropertyEditorViewModel"/> so test assertions don't depend on
/// the concrete library leaves' internals.
/// </remarks>
[TestClass]
public sealed class ObjectPropertyEditorNestedTests
{
    /// <summary>
    /// Construct a SchemaNode with explicit (path, name) — IMPORTANT: the
    /// object editor's <c>LoadFromLayered</c> projects from <c>parent[name]</c>
    /// using <c>child.Schema.Name</c>, so the leaf name MUST be just the
    /// last path segment, not the full dotted path.
    /// </summary>
    private static SchemaNode S(string path, string name)
    {
        return new SchemaNode(path, name);
    }

    private static FakeLeafEditor Leaf(string name)
    {
        return new FakeLeafEditor(S("settings.nested." + name, name), ConfigScope.User);
    }

    private static ObjectPropertyEditorViewModel Parent(params PropertyEditorViewModel[] children)
    {
        return new ObjectPropertyEditorViewModel(S("settings.nested", "nested"), ConfigScope.User, children);
    }

    // ── ToJsonValue recursion ────────────────────────────────────────

    [TestMethod]
    public void ToJsonValue_AllChildrenReturnNull_ReturnsNull()
    {
        ObjectPropertyEditorViewModel vm = Parent(Leaf("a"), Leaf("b"));
        // FakeLeaf default Value is null → ToJsonValue() = null per leaf.
        Assert.IsNull(vm.ToJsonValue(),
            "Object editor must return null when every child returns null — otherwise "
            + "save would emit an empty {} which the workspace then strips, masking the "
            + "real 'no children set' state.");
    }

    [TestMethod]
    public void ToJsonValue_SomeChildrenNull_OmitsThem()
    {
        FakeLeafEditor a = Leaf("a");
        a.Value = JsonValue.Create(1);
        FakeLeafEditor b = Leaf("b"); // value stays null → omitted
        FakeLeafEditor c = Leaf("c");
        c.Value = JsonValue.Create("x");
        ObjectPropertyEditorViewModel vm = Parent(a, b, c);

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json);
        Assert.IsTrue(json.ContainsKey("a"));
        Assert.IsFalse(json.ContainsKey("b"),
            "Children whose ToJsonValue returns null must NOT appear in the parent JSON.");
        Assert.IsTrue(json.ContainsKey("c"));
    }

    [TestMethod]
    public void ToJsonValue_RecursivelyAssemblesNestedObjects()
    {
        // Two ObjectPropertyEditors chained: outer has [inner] as child;
        // inner has [leaf]. Confirms the recursion works at depth > 1.
        // Leaf name is "flag" (must match the JSON key used by the parent's
        // ToJsonValue when assembling its object).
        FakeLeafEditor leaf = new(S("settings.nested.inner.flag", "flag"), ConfigScope.User);
        leaf.Value = JsonValue.Create(true);

        ObjectPropertyEditorViewModel inner = new(
            S("settings.nested.inner", "inner"), ConfigScope.User, [leaf]);

        ObjectPropertyEditorViewModel outer = new(
            S("settings.nested", "nested"), ConfigScope.User, [inner]);

        JsonObject? json = outer.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json);
        JsonObject? innerJson = json["inner"] as JsonObject;
        Assert.IsNotNull(innerJson);
        Assert.IsTrue(innerJson["flag"]!.GetValue<bool>());
    }

    // ── LoadFromLayered per-scope projection ──────────────────────────

    [TestMethod]
    public void LoadFromLayered_ProjectsParentScopesOntoChildren()
    {
        // Parent has entries at User and Local scopes; each entry is a JsonObject
        // with two child keys. The object editor must project these so each child
        // sees its own LayeredValue with two entries.
        JsonObject userParent = new() { ["a"] = "user-a", ["b"] = "user-b" };
        JsonObject localParent = new() { ["a"] = "local-a", ["b"] = "local-b" };

        LayeredValue layered = new(
            "settings.nested",
            [
                new ScopeEntry(ConfigScope.Local, localParent, "/loc.json"),
                new ScopeEntry(ConfigScope.User, userParent, "/usr.json"),
            ])
        {
            EffectiveValue = localParent,
            EffectiveScope = ConfigScope.Local,
        };

        FakeLeafEditor a = Leaf("a");
        FakeLeafEditor b = Leaf("b");
        ObjectPropertyEditorViewModel vm = Parent(a, b);

        vm.LoadFromLayered(layered, ConfigScope.User);

        // Each child should see a JsonValue projected from its parent key.
        Assert.IsNotNull(a.Value);
        Assert.IsNotNull(b.Value);
        Assert.AreEqual("local-a", a.Value!.GetValue<string>(),
            "Child 'a' should observe the highest-priority projected value (Local in this fixture).");
        Assert.AreEqual("local-b", b.Value!.GetValue<string>());
    }

    [TestMethod]
    public void LoadFromLayered_ChildAbsentFromAllScopes_StaysUnmodified()
    {
        // Parent has an entry but it doesn't contain "absent" — child stays null + unmodified.
        JsonObject parent = new() { ["present"] = "x" };
        LayeredValue layered = new(
            "settings.nested",
            [new ScopeEntry(ConfigScope.User, parent, "/usr.json")])
        {
            EffectiveValue = parent,
            EffectiveScope = ConfigScope.User,
        };

        FakeLeafEditor present = Leaf("present");
        FakeLeafEditor absent = Leaf("absent");
        ObjectPropertyEditorViewModel vm = Parent(present, absent);

        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.IsNotNull(present.Value, "Child 'present' must receive its projected value.");
        Assert.IsNull(absent.Value,
            "Child whose key is missing from every parent scope must remain null.");
        Assert.IsFalse(absent.IsModified,
            "Absent child must NOT be flagged as modified after LoadFromLayered.");
    }

    [TestMethod]
    public void LoadFromLayered_IsModifiedReflects_AnyChildModified()
    {
        JsonObject parent = new() { ["a"] = "x" };
        LayeredValue layered = new(
            "settings.nested",
            [new ScopeEntry(ConfigScope.User, parent, "/usr.json")])
        {
            EffectiveValue = parent,
            EffectiveScope = ConfigScope.User,
        };

        FakeLeafEditor a = Leaf("a");
        FakeLeafEditor b = Leaf("b"); // never set in any scope
        ObjectPropertyEditorViewModel vm = Parent(a, b);

        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.IsTrue(vm.IsModified,
            "Object editor IsModified must be true when ANY child is modified after load.");

        // Empty parent — no child gets a value, no child is modified.
        LayeredValue emptyLayered = new("settings.nested", []);
        a.Value = null;
        a.IsModified = false;
        b.Value = null;
        b.IsModified = false;
        vm.LoadFromLayered(emptyLayered, ConfigScope.User);
        Assert.IsFalse(vm.IsModified,
            "Object editor IsModified must be false when no child has a value at any scope.");
    }

    // ── Child IsModified propagation ──────────────────────────────────

    [TestMethod]
    public void ChildIsModified_FlippingTrue_PropagatesToParent()
    {
        FakeLeafEditor a = Leaf("a");
        ObjectPropertyEditorViewModel vm = Parent(a);

        Assert.IsFalse(vm.IsModified);

        // Trigger the propagation by setting child IsModified directly. The fake
        // leaf inherits from the bridge whose IsModified is observable via
        // CommunityToolkit, so PropertyChanged fires.
        a.IsModified = true;

        Assert.IsTrue(vm.IsModified,
            "Parent must observe a child going from unmodified → modified.");
    }

    [TestMethod]
    public void ChildIsModified_SecondChildFlipsTrue_StillFiresPropertyChanged()
    {
        // The force-fire contract: when a SECOND child becomes modified while
        // the parent's IsModified was already true, CommunityToolkit elides
        // the equal-value setter — but the View needs to know about every
        // change so it re-invokes ToJsonValue. The handler force-fires.
        FakeLeafEditor a = Leaf("a");
        FakeLeafEditor b = Leaf("b");
        ObjectPropertyEditorViewModel vm = Parent(a, b);

        a.IsModified = true;
        Assert.IsTrue(vm.IsModified, "Sanity: first child flips parent.");

        int fires = 0;
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ObjectPropertyEditorViewModel.IsModified))
            {
                fires++;
            }
        };
        vm.PropertyChanged += handler;
        try
        {
            b.IsModified = true;
        }
        finally
        {
            vm.PropertyChanged -= handler;
        }

        Assert.IsTrue(fires >= 1,
            "Parent must fire PropertyChanged(IsModified) when a second child becomes "
            + "modified, even though the parent's bool value did not change.");
    }

    // ── Reset cascade ─────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_CascadesToAllChildren()
    {
        // Construct parent BEFORE flipping children so the propagation handler
        // is wired up at the moment IsModified=true fires — otherwise the
        // parent misses the events that bring CanReset to true.
        FakeLeafEditor a = Leaf("a");
        a.Value = JsonValue.Create(1);
        FakeLeafEditor b = Leaf("b");
        b.Value = JsonValue.Create(2);
        ObjectPropertyEditorViewModel vm = Parent(a, b);
        a.IsModified = true;
        b.IsModified = true;

        Assert.IsTrue(vm.CanReset, "Pre-condition: parent should be resettable when modified.");

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsFalse(a.IsModified, "Reset must clear child A's IsModified.");
        Assert.IsFalse(b.IsModified, "Reset must clear child B's IsModified.");
        Assert.IsFalse(vm.IsModified,
            "After cascading reset, the parent's IsModified must also be false.");
    }

    // ── Test plumbing ────────────────────────────────────────────────

    /// <summary>
    /// Minimal concrete leaf inheriting the App-bridge editor base directly.
    /// Lets each test wire up exactly the JSON value and modification flag
    /// it cares about, without depending on any concrete library leaf
    /// (Boolean, String, etc.).
    /// </summary>
    private sealed class FakeLeafEditor : PropertyEditorViewModel
    {
        public FakeLeafEditor(SchemaNode schema, ConfigScope scope) : base(schema, scope)
        {
        }

        public JsonNode? Value { get; set; }

        public override JsonNode? ToJsonValue()
        {
            return Value?.DeepClone();
        }

        public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
        {
            SetScopeState(layered, editingScope);
            // Mirror the projection done by ObjectPropertyEditor: take the
            // highest-priority entry's value (entries are ordered priority-first).
            Value = layered.Entries.Count > 0
                ? layered.Entries[0].Value?.DeepClone()
                : null;
            IsModified = Value is not null;
        }
    }
}