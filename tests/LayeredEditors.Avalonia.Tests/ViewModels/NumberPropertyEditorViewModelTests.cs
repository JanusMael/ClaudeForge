namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class NumberPropertyEditorViewModelTests
{
    private static FakeEditorSchema IntSchema()
    {
        return new FakeEditorSchema("settings.timeout", EditorValueType.Integer) { Minimum = 0, Maximum = 3600 };
    }

    private static FakeEditorSchema FloatSchema()
    {
        return new FakeEditorSchema("settings.ratio", EditorValueType.Number);
    }

    private static FakeEditorValue Empty()
    {
        return new FakeEditorValue("settings.timeout");
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void IntegerSchema_SetsIsInteger_True()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        Assert.IsTrue(vm.IsInteger);
        Assert.AreEqual(0d, vm.Minimum);
        Assert.AreEqual(3600d, vm.Maximum);
    }

    [TestMethod]
    public void FloatSchema_SetsIsInteger_False()
    {
        NumberPropertyEditorViewModel vm = new(FloatSchema(), FakeEditorScope.User);
        Assert.IsFalse(vm.IsInteger);
    }

    [TestMethod]
    public void Initial_Value_IsNull_NotModified()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    // ── LoadFromValue — various numeric types ─────────────────────────────────

    [TestMethod]
    public void LoadFromValue_Long()
    {
        FakeEditorValue value = new FakeEditorValue("settings.timeout").With(FakeEditorScope.User, (long)42);
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(42d, vm.Value);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_Int()
    {
        FakeEditorValue value = new FakeEditorValue("settings.timeout").With(FakeEditorScope.User, 100);
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(100d, vm.Value);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_Double()
    {
        FakeEditorValue value = new FakeEditorValue("settings.ratio").With(FakeEditorScope.User, 3.14);
        NumberPropertyEditorViewModel vm = new(FloatSchema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(3.14, vm.Value);
    }

    [TestMethod]
    public void LoadFromValue_Empty_ValueStaysNull()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.LoadFromValue(Empty(), FakeEditorScope.User);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNotSet()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsLong_ForIntegerSchema()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.Value = 7.0;
        Assert.AreEqual((long)7, vm.ToValue());
        Assert.IsInstanceOfType(vm.ToValue(), typeof(long));
    }

    [TestMethod]
    public void ToValue_ReturnsDouble_ForFloatSchema()
    {
        NumberPropertyEditorViewModel vm = new(FloatSchema(), FakeEditorScope.User);
        vm.Value = 2.5;
        Assert.AreEqual(2.5, vm.ToValue());
        Assert.IsInstanceOfType(vm.ToValue(), typeof(double));
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsValue()
    {
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.Value = 99.0;

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    // ── OtherScopesWithData + InheritedFromScope ──────────────────

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_PopulatedFromDefiningScopes()
    {
        FakeEditorValue value = new FakeEditorValue("settings.timeout")
                                .With(FakeEditorScope.User, (long)60)
                                .With(FakeEditorScope.Project, (long)120);
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void LoadFromValue_InheritedFromScope_PopulatedWhenEditingScopeEmpty()
    {
        FakeEditorValue value = new FakeEditorValue("settings.timeout").With(FakeEditorScope.User, (long)42);
        NumberPropertyEditorViewModel vm = new(IntSchema(), FakeEditorScope.Local);
        vm.LoadFromValue(value, FakeEditorScope.Local);

        Assert.IsNull(vm.Value);
        Assert.AreEqual("user", vm.InheritedFromScope?.Id);
        Assert.AreEqual("42", vm.InheritedDisplay);
        Assert.IsTrue(vm.HasInheritedFromOtherScope);
    }
}