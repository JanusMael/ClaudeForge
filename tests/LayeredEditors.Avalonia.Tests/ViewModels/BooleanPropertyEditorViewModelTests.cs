namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class BooleanPropertyEditorViewModelTests
{
    private static FakeEditorSchema Schema()
    {
        return new FakeEditorSchema("settings.flag", EditorValueType.Boolean);
    }

    private static FakeEditorValue Empty()
    {
        return new FakeEditorValue("settings.flag");
    }

    private static FakeEditorValue WithUser(bool v)
    {
        return new FakeEditorValue("settings.flag").With(FakeEditorScope.User, v);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Initial_Value_IsNull_NotModified()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
        Assert.IsFalse(vm.IsValueSet);
    }

    // ── LoadFromValue ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadFromValue_Empty_ValueStaysNull()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(Empty(), FakeEditorScope.User);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.EffectiveScope);
    }

    [TestMethod]
    public void LoadFromValue_SetsTrue()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(WithUser(true), FakeEditorScope.User);

        Assert.IsTrue(vm.Value);
        Assert.IsTrue(vm.IsModified);
        Assert.IsTrue(vm.IsValueSet);
        Assert.AreEqual("user", vm.EffectiveScope?.Id);
    }

    [TestMethod]
    public void LoadFromValue_SetsFalse()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(WithUser(false), FakeEditorScope.User);

        Assert.IsFalse(vm.Value);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_OtherScope_NoValueAtEditingScope()
    {
        // Value at Project, editing User → no value at User
        FakeEditorValue value = new FakeEditorValue("settings.flag")
            .With(FakeEditorScope.Project, true);

        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_IsOverridden_WhenMultipleScopes()
    {
        FakeEditorValue value = new FakeEditorValue("settings.flag")
                                .With(FakeEditorScope.User, true)
                                .With(FakeEditorScope.Project, false);

        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.IsTrue(vm.IsOverridden);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNotSet()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsBool_WhenSet()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Value = false;
        Assert.IsFalse((bool?)vm.ToValue());
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsValue()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Value = true;

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void CanReset_IsFalse_WhenNotModified()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsFalse(vm.CanReset);
    }

    [TestMethod]
    public void CanReset_IsTrue_WhenModified()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Value = true;
        Assert.IsTrue(vm.CanReset);
    }

    [TestMethod]
    public void CanReset_IsFalse_WhenReadOnlyScope()
    {
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.Managed);
        vm.LoadFromValue(
            new FakeEditorValue("settings.flag").With(FakeEditorScope.Managed, true),
            FakeEditorScope.Managed);
        // Managed scope is read-only → CanReset must be false
        Assert.IsFalse(vm.CanReset);
    }

    // ── OtherScopesWithData (chiclets row) + InheritedFromScope ─────

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_ExcludesEditingScope_IncludesOthers()
    {
        // Value defined at both User and Project; editing at User.  The
        // "Defined in scopes:" wrapper row should list Project only (the
        // editing scope's own data is implicit in the editor itself).
        FakeEditorValue value = new FakeEditorValue("settings.flag")
                                .With(FakeEditorScope.User, true)
                                .With(FakeEditorScope.Project, false);

        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_OnlyEditingScope_IsEmpty()
    {
        // Value defined only at the editing scope → no OTHER scope has data.
        FakeEditorValue value = new FakeEditorValue("settings.flag").With(FakeEditorScope.User, true);
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(0, vm.OtherScopesWithData.Count,
            "When only the editing scope has data, OtherScopesWithData must be empty.");
    }

    [TestMethod]
    public void LoadFromValue_InheritedFromScope_PopulatedWhenEditingScopeEmpty()
    {
        // Editing at Local; only User has data.  Editor is empty at Local
        // → "Currently effective from User: …" row should fire.
        FakeEditorValue value = new FakeEditorValue("settings.flag")
            .With(FakeEditorScope.User, true);
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.Local);
        vm.LoadFromValue(value, FakeEditorScope.Local);

        Assert.IsNull(vm.Value, "Editor at empty scope shows null.");
        Assert.IsNotNull(vm.InheritedFromScope,
            "InheritedFromScope must be set when the editing scope is empty " +
            "but another scope owns the value.");
        Assert.AreEqual("user", vm.InheritedFromScope!.Id);
        Assert.AreEqual("true", vm.InheritedDisplay);
        Assert.IsTrue(vm.HasInheritedFromOtherScope,
            "Wrapper-row visibility flag must fire when both display + scope are set.");
    }

    [TestMethod]
    public void LoadFromValue_InheritedFromScope_NullWhenEditingScopeOwnsValue()
    {
        // When the editing scope itself has the value, there's nothing to
        // inherit — the editor IS the source of truth.
        FakeEditorValue value = new FakeEditorValue("settings.flag").With(FakeEditorScope.User, true);
        BooleanPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.IsNull(vm.InheritedFromScope);
        Assert.IsNull(vm.InheritedDisplay);
        Assert.IsFalse(vm.HasInheritedFromOtherScope);
    }
}