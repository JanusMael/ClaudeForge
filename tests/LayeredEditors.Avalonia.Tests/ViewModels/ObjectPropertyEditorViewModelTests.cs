namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class ObjectPropertyEditorViewModelTests
{
    private static FakeEditorSchema ParentSchema(string path = "settings.nested")
    {
        return new FakeEditorSchema(path, EditorValueType.Object);
    }

    private static BooleanPropertyEditorViewModel MakeBoolChild(string path = "settings.nested.flag")
    {
        return new BooleanPropertyEditorViewModel(new FakeEditorSchema(path, EditorValueType.Boolean),
            FakeEditorScope.User);
    }

    private static StringPropertyEditorViewModel MakeStringChild(string path = "settings.nested.label")
    {
        return new StringPropertyEditorViewModel(new FakeEditorSchema(path), FakeEditorScope.User);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Children_AreExposed_FromConstructor()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild]);

        Assert.AreEqual(1, vm.Children.Count);
        Assert.AreSame(boolChild, vm.Children[0]);
    }

    [TestMethod]
    public void IsExpanded_DefaultsToTrue()
    {
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User, []);
        Assert.IsTrue(vm.IsExpanded);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenAllChildrenReturnNull()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild(); // Value not set → ToValue() = null
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild]);

        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsDict_WithSetChildren()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        StringPropertyEditorViewModel stringChild = MakeStringChild();

        boolChild.Value = true;
        stringChild.Value = "hello";

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild, stringChild]);

        IReadOnlyDictionary<string, object?>? result = vm.ToValue() as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.IsTrue((bool?)result["flag"]);
        Assert.AreEqual("hello", result["label"]);
    }

    [TestMethod]
    public void ToValue_OmitsChildrenWithNullValue()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild(); // null → omitted
        StringPropertyEditorViewModel stringChild = MakeStringChild();
        stringChild.Value = "set";

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild, stringChild]);

        IReadOnlyDictionary<string, object?>? result = vm.ToValue() as IReadOnlyDictionary<string, object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result.ContainsKey("label"));
    }

    // ── LoadFromValue ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadFromValue_WithWorkspace_LoadsChildren()
    {
        FakeEditorWorkspace ws = new FakeEditorWorkspace()
                                 .Seed("settings.nested.flag", FakeEditorScope.User, true)
                                 .Seed("settings.nested.label", FakeEditorScope.User, "loaded");

        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        StringPropertyEditorViewModel stringChild = MakeStringChild();

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild, stringChild], ws);

        FakeEditorValue parentValue = new FakeEditorValue("settings.nested")
            .With(FakeEditorScope.User, new Dictionary<string, object?> { ["flag"] = true });

        vm.LoadFromValue(parentValue, FakeEditorScope.User);

        Assert.IsTrue(boolChild.Value);
        Assert.AreEqual("loaded", stringChild.Value);
    }

    [TestMethod]
    public void LoadFromValue_IsModified_WhenValueDefinedAtScope()
    {
        FakeEditorWorkspace ws = new();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [], ws);

        FakeEditorValue value = new FakeEditorValue("settings.nested")
            .With(FakeEditorScope.User, new Dictionary<string, object?>());

        vm.LoadFromValue(value, FakeEditorScope.User);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_IsNotModified_WhenValueNotDefinedAtScope()
    {
        FakeEditorWorkspace ws = new();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [], ws);

        FakeEditorValue value = new("settings.nested"); // not defined at any scope

        vm.LoadFromValue(value, FakeEditorScope.User);
        Assert.IsFalse(vm.IsModified);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsAllChildren()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        StringPropertyEditorViewModel stringChild = MakeStringChild();
        boolChild.Value = true;
        stringChild.Value = "text";

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild, stringChild]);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(boolChild.Value);
        Assert.IsNull(stringChild.Value);
    }

    // ── End-to-end: non-Claude consumer ──────────────────────────────────────
    // This is the hard acceptance test from the plan: a non-JSON, non-Claude
    // consumer can drive the library without touching ClaudeForge.Core or
    // System.Text.Json.

    // ── Child-change propagation (B.3 coverage gap #6) ───────────────────────
    //
    // The parent's OnChildPropertyChanged must propagate IsModified upward
    // *and* force-fire the PropertyChanged event even when the parent's flag
    // doesn't actually change (e.g. a second child mutation while the parent
    // is already IsModified). Without the force-fire, hosting group editors
    // miss subsequent edits — the precise bug commented in lines 102-108 of
    // the source.

    [TestMethod]
    public void ChildPropertyChanged_Bubbles_FirstModification_RaisesIsModified()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild]);

        int raisedCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsModified))
            {
                raisedCount++;
            }
        };

        Assert.IsFalse(vm.IsModified);
        boolChild.IsModified = true;

        // Parent's IsModified flipped false → true; PropertyChanged fired once.
        Assert.IsTrue(vm.IsModified);
        Assert.AreEqual(1, raisedCount);
    }

    [TestMethod]
    public void ChildPropertyChanged_Bubbles_SecondModification_ForceFires()
    {
        // Two children. First is already modified. Mutate the second.  The
        // parent's IsModified stays true, but PropertyChanged MUST still fire
        // so the hosting group editor re-invokes ToValue() and writes the
        // updated object.  This is the force-fire contract documented inline.
        BooleanPropertyEditorViewModel firstChild = MakeBoolChild();
        StringPropertyEditorViewModel secondChild = MakeStringChild();

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [firstChild, secondChild]);

        firstChild.IsModified = true; // parent now IsModified=true
        Assert.IsTrue(vm.IsModified);

        int raisedAfter = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsModified))
            {
                raisedAfter++;
            }
        };

        secondChild.IsModified = true; // parent stays true

        Assert.IsTrue(vm.IsModified);
        Assert.AreEqual(1, raisedAfter,
            "Force-fire required: hosting group editor must re-read ToValue() "
            + "even when the parent's IsModified flag didn't change.");
    }

    [TestMethod]
    public void ChildPropertyChanged_Ignores_NonIsModifiedNotifications()
    {
        // Other property changes on a child (e.g. Value) should NOT trigger
        // the parent's IsModified update path.  Only IsModified bubbles.
        StringPropertyEditorViewModel stringChild = MakeStringChild();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [stringChild]);

        int raised = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsModified))
            {
                raised++;
            }
        };

        stringChild.Value = "anything"; // raises Value PropertyChanged, not IsModified

        // Setting Value does fire IsModified=true on the child internally,
        // which IS forwarded — but the test verifies that raised==1 (the
        // single bubble), not that we get extra noise from the Value event.
        Assert.IsTrue(stringChild.IsModified);
        Assert.AreEqual(1, raised,
            "The IsModified bubble fires once; non-IsModified property changes "
            + "don't add additional events.");
    }

    [TestMethod]
    public void ChildPropertyChanged_LastChildClearsModified_FiresFalse()
    {
        // Parent is IsModified=true via firstChild.  Clearing firstChild's
        // IsModified should bring the parent back to false.
        BooleanPropertyEditorViewModel firstChild = MakeBoolChild();
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [firstChild]);

        firstChild.IsModified = true;
        Assert.IsTrue(vm.IsModified);

        int raisedAfter = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsModified))
            {
                raisedAfter++;
            }
        };

        firstChild.IsModified = false;

        Assert.IsFalse(vm.IsModified);
        Assert.AreEqual(1, raisedAfter,
            "Clearing the last modified child fires IsModified=false on the parent.");
    }

    // ── LoadFromValue without workspace ─────────────────────────────────────
    //
    // The else-branch on line 70 of the source — when _workspace is null —
    // is reachable through the test-only construction overload (no workspace
    // arg). Children retain whatever values they had before LoadFromValue
    // was called; the parent only updates EditingScope / EffectiveScope /
    // IsOverridden / IsModified.

    [TestMethod]
    public void LoadFromValue_WithoutWorkspace_LeavesChildValuesUntouched()
    {
        BooleanPropertyEditorViewModel boolChild = MakeBoolChild();
        boolChild.Value = true;

        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            [boolChild]); // no workspace

        FakeEditorValue parentValue = new FakeEditorValue("settings.nested")
            .With(FakeEditorScope.User, new Dictionary<string, object?> { ["flag"] = false });

        vm.LoadFromValue(parentValue, FakeEditorScope.User);

        // Child value untouched — load only flows through children when
        // _workspace is non-null.
        Assert.IsTrue(boolChild.Value);
    }

    [TestMethod]
    public void LoadFromValue_PropagatesEffectiveScopeAndIsOverridden()
    {
        // FakeEditorValue computes EffectiveScope/IsOverridden from its
        // seeded entries — IsOverridden is true when more than one scope
        // has a value, EffectiveScope is the highest-priority one.  Seed
        // both Project and User so both flags surface.
        ObjectPropertyEditorViewModel vm = new(ParentSchema(), FakeEditorScope.User,
            []);

        FakeEditorValue value = new FakeEditorValue("settings.nested")
                                .With(FakeEditorScope.User, new Dictionary<string, object?>())
                                .With(FakeEditorScope.Project, new Dictionary<string, object?>());

        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.IsNotNull(vm.EffectiveScope);
        Assert.IsTrue(vm.IsOverridden,
            "Two scopes have values → IsOverridden=true should propagate.");
    }

    [TestMethod]
    public void NonClaudeConsumer_CanDriveLibrary_EndToEnd()
    {
        // Arrange: a fake workspace with pre-seeded values
        FakeEditorWorkspace ws = new([FakeEditorScope.User, FakeEditorScope.Project]);
        ws.TrackEvents();

        ws.Seed("app.enabled", FakeEditorScope.Project, true);
        ws.Seed("app.label", FakeEditorScope.User, "user-label");

        BooleanPropertyEditorViewModel enabledChild = new(
            new FakeEditorSchema("app.enabled", EditorValueType.Boolean), FakeEditorScope.User);
        StringPropertyEditorViewModel labelChild = new(
            new FakeEditorSchema("app.label"), FakeEditorScope.User);

        FakeEditorSchema parentSchema = new("app", EditorValueType.Object);
        ObjectPropertyEditorViewModel vm = new(parentSchema, FakeEditorScope.User,
            [enabledChild, labelChild], ws);

        // Act: load
        vm.LoadFromValue(ws.GetValue("app"), FakeEditorScope.User);

        // Assert: children loaded correctly
        Assert.IsNull(enabledChild.Value); // User has no value; Project has it
        Assert.AreEqual("user-label", labelChild.Value);

        // Act: write through workspace
        ws.SetValue("app.enabled", true, FakeEditorScope.User);

        // Assert: event was raised
        Assert.AreEqual(1, ws.TrackedEventCount);

        // Act: re-load child after workspace mutation
        enabledChild.LoadFromValue(ws.GetValue("app.enabled"), FakeEditorScope.User);
        Assert.IsTrue(enabledChild.Value);

        // Act: reset child
        enabledChild.ResetToInheritedCommand.Execute(null);
        Assert.IsNull(enabledChild.Value);
        Assert.IsFalse(enabledChild.IsModified);
    }
}