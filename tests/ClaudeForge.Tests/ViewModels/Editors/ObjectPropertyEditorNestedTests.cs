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

    // ── Collapse into prefix categories (large-object load perf) ──────

    private static ObjectPropertyEditorViewModel ParentWithNames(params string[] names)
    {
        PropertyEditorViewModel[] kids = new PropertyEditorViewModel[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            kids[i] = Leaf(names[i]);
        }

        return Parent(kids);
    }

    private static string[] Prefixed(string prefix, int count)
    {
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = prefix + i;
        }

        return names;
    }

    /// <summary>
    /// A "pathological" object: over the threshold, with real prefix structure — mirrors
    /// <c>env</c> (a couple of dominant prefixes plus a short tail). 80 + 70 + 2 = 152.
    /// </summary>
    private static ObjectPropertyEditorViewModel HugePrefixedParent()
    {
        List<string> names = new();
        names.AddRange(Prefixed("CLAUDE_", 80));
        names.AddRange(Prefixed("OTEL_", 70));
        names.Add("misc1");
        names.Add("misc2");
        return ParentWithNames(names.ToArray());
    }

    [TestMethod]
    public void SmallObject_RendersInline_NoCategories()
    {
        ObjectPropertyEditorViewModel vm = ParentWithNames("a", "b", "c");

        Assert.IsFalse(vm.IsCollapsible, "A small object renders inline, not as categories.");
        Assert.AreEqual(0, vm.Categories.Count);
        Assert.AreEqual(3, vm.Children.Count);
    }

    [TestMethod]
    public void MidSizedObject_StillRendersInline_NoAccordionImposed()
    {
        // A sandbox-sized object (35 children) must render inline exactly as it always did.
        // The accordion is reserved for the pathological case (env's ~305): a list this size
        // renders in acceptable time, so we must NOT impose a collapsed accordion on it.
        ObjectPropertyEditorViewModel vm = ParentWithNames(Prefixed("field", 35));

        Assert.IsFalse(vm.IsCollapsible,
            "A mid-sized object must NOT be forced into a collapsed accordion.");
        Assert.AreEqual(0, vm.Categories.Count);
        Assert.AreEqual(35, vm.Children.Count);
    }

    [TestMethod]
    public void HugeObject_NoUsefulPrefixes_SingleAllCategory_Collapsed()
    {
        // No shared prefix → one bounded "All" section (still collapsed → zero realized),
        // never a pile of singleton categories.
        ObjectPropertyEditorViewModel vm = ParentWithNames(Prefixed("field", 200));

        Assert.IsTrue(vm.IsCollapsible);
        Assert.AreEqual(1, vm.Categories.Count);
        Assert.AreEqual("All", vm.Categories[0].Name);
        Assert.AreEqual(200, vm.Categories[0].Count);
        Assert.IsFalse(vm.Categories[0].IsExpanded, "Sections start collapsed.");
        Assert.AreEqual(0, vm.Categories[0].VisibleChildren.Count,
            "Collapsed → zero realized child editors (the multi-second-load fix).");
    }

    [TestMethod]
    public void HugeObject_GroupsByBareNamePrefix_WithOther_AllCollapsed()
    {
        ObjectPropertyEditorViewModel vm = HugePrefixedParent();

        Assert.IsTrue(vm.IsCollapsible);
        CollectionAssert.AreEqual(
            new[] { "CLAUDE", "OTEL", "Other" },
            vm.Categories.Select(c => c.Name).ToArray(),
            "Categories are the BARE prefix (no trailing '_'), alphabetical, with Other last.");
        Assert.AreEqual(2, vm.Categories.First(c => c.Name == "Other").Count);
        Assert.AreEqual(0, vm.Categories.Sum(c => c.VisibleChildren.Count),
            "Every section starts collapsed → nothing realized on page load.");
    }

    [TestMethod]
    public void ExpandingOneCategory_RealizesOnlyItsChildren()
    {
        ObjectPropertyEditorViewModel vm = HugePrefixedParent();

        PropertyCategoryViewModel claude = vm.Categories.First(c => c.Name == "CLAUDE");
        claude.IsExpanded = true;

        Assert.AreEqual(80, claude.VisibleChildren.Count, "Expanded section binds its own children.");
        Assert.AreEqual(0, vm.Categories.Where(c => c.Name != "CLAUDE").Sum(c => c.VisibleChildren.Count),
            "Other sections stay collapsed → still nothing realized there.");
        StringAssert.Contains(claude.Header, "80");
        StringAssert.Contains(claude.Header, "CLAUDE");
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