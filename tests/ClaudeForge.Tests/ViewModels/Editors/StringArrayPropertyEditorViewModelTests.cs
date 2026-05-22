using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) StringArray leaf editor.
/// The App-bridge StringArrayPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / Add / Remove /
/// Reset).
/// </summary>
[TestClass]
public class StringArrayPropertyEditorViewModelTests
{
    private static SchemaNode ArraySchema()
    {
        return new SchemaNode("tags", "tags") { ValueType = SchemaValueType.Array };
    }

    private static LibVm.StringArrayPropertyEditorViewModel NewVm(
        SchemaNode? schema = null, ConfigScope scope = ConfigScope.User)
    {
        return new LibVm.StringArrayPropertyEditorViewModel(new ClaudeSchemaAdapter(schema ?? ArraySchema()),
            ClaudeScope.For(scope));
    }

    private static void Load(
        LibVm.StringArrayPropertyEditorViewModel vm,
        LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(scope));
    }

    private static LayeredValue LayeredWithArray(ConfigScope scope, params string[] items)
    {
        JsonArray arr = new();
        foreach (string s in items)
        {
            arr.Add(JsonValue.Create(s));
        }

        ScopeEntry entry = new(scope, arr, "/fake");
        return new LayeredValue("tags", [entry])
        {
            EffectiveValue = arr,
            EffectiveScope = scope,
        };
    }

    [TestMethod]
    public void InitialState_IsEmpty()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void AddItem_AddsToCollection()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "hello";
        vm.AddItemCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count);
        Assert.AreEqual("hello", vm.Items[0]);
        Assert.AreEqual(string.Empty, vm.NewItemText);
    }

    [TestMethod]
    public void AddItem_NoDuplicates()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count);
    }

    [TestMethod]
    public void RemoveItem_RemovesFromCollection()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "a";
        vm.AddItemCommand.Execute(null);
        vm.RemoveItemCommand.Execute("a");

        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void LoadFromValue_PopulatesItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWithArray(ConfigScope.User, "x", "y", "z"), ConfigScope.User);

        CollectionAssert.AreEquivalent(new[] { "x", "y", "z" }, vm.Items.ToArray());
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void ToValue_ReturnsNull_WhenEmpty()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsList_WhenHasItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "one";
        vm.AddItemCommand.Execute(null);
        vm.NewItemText = "two";
        vm.AddItemCommand.Execute(null);

        IReadOnlyList<object?>? list = vm.ToValue() as IReadOnlyList<object?>;
        Assert.IsNotNull(list);
        Assert.AreEqual(2, list!.Count);
        Assert.AreEqual("one", list[0]);
        Assert.AreEqual("two", list[1]);
    }

    [TestMethod]
    public void Reset_ClearsItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "a";
        vm.AddItemCommand.Execute(null);
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
    }
}