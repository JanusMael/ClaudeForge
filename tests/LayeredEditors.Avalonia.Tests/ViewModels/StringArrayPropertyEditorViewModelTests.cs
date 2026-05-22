namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class StringArrayPropertyEditorViewModelTests
{
    private static FakeEditorSchema Schema()
    {
        return new FakeEditorSchema("settings.tags", EditorValueType.StringArray);
    }

    private static FakeEditorValue Empty()
    {
        return new FakeEditorValue("settings.tags");
    }

    private static FakeEditorValue WithUser(IReadOnlyList<object?> items)
    {
        return new FakeEditorValue("settings.tags").With(FakeEditorScope.User, items);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Initial_Items_Empty_NotModified()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsFalse(vm.IsValueSet);
    }

    // ── LoadFromValue ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadFromValue_Empty_ItemsRemainEmpty()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(Empty(), FakeEditorScope.User);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_SetsItems()
    {
        FakeEditorValue value = WithUser(["alpha", "beta", "gamma"]);
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(3, vm.Items.Count);
        Assert.AreEqual("alpha", vm.Items[0]);
        Assert.AreEqual("beta", vm.Items[1]);
        Assert.AreEqual("gamma", vm.Items[2]);
        Assert.IsTrue(vm.IsModified);
        Assert.IsTrue(vm.IsValueSet);
    }

    [TestMethod]
    public void LoadFromValue_OtherScope_NoItems()
    {
        FakeEditorValue value = new FakeEditorValue("settings.tags")
            .With(FakeEditorScope.Project, (IReadOnlyList<object?>)["x"]);

        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void LoadFromValue_MixedTypeArray_CoercesToString()
    {
        // Non-string items are ToString()'d
        FakeEditorValue value = new FakeEditorValue("settings.tags")
            .With(FakeEditorScope.User, (IReadOnlyList<object?>)["text", 42, true]);

        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(3, vm.Items.Count);
        Assert.AreEqual("text", vm.Items[0]);
        Assert.AreEqual("42", vm.Items[1]);
        Assert.AreEqual("True", vm.Items[2]);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenEmpty()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsList_WhenItemsExist()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Items.Add("one");
        vm.Items.Add("two");

        IReadOnlyList<object?>? result = vm.ToValue() as IReadOnlyList<object?>;
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("one", result[0]);
        Assert.AreEqual("two", result[1]);
    }

    // ── Add/Remove commands ───────────────────────────────────────────────────

    [TestMethod]
    public void AddItem_AddsToList_AndClearsText()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.NewItemText = "hello";
        vm.AddItemCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count);
        Assert.AreEqual("hello", vm.Items[0]);
        Assert.AreEqual(string.Empty, vm.NewItemText);
    }

    [TestMethod]
    public void AddItem_NoDuplicates()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count);
    }

    [TestMethod]
    public void AddItemCommand_CannotExecute_WhenTextEmpty()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.NewItemText = "";
        Assert.IsFalse(vm.AddItemCommand.CanExecute(null));
    }

    [TestMethod]
    public void RemoveItem_RemovesFromList()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Items.Add("a");
        vm.Items.Add("b");
        vm.RemoveItemCommand.Execute("a");

        Assert.AreEqual(1, vm.Items.Count);
        Assert.AreEqual("b", vm.Items[0]);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsItems()
    {
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.Items.Add("item1");
        vm.Items.Add("item2");

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
    }

    // ── OtherScopesWithData + InheritedFromScope ──────────────────

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_PopulatedFromDefiningScopes()
    {
        FakeEditorValue value = new FakeEditorValue("settings.tags")
                                .With(FakeEditorScope.User, new object?[] { "a", "b" })
                                .With(FakeEditorScope.Project, new object?[] { "c" });
        StringArrayPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }
}