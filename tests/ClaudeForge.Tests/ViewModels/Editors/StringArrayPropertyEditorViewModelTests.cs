using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) StringArray leaf editor.
/// The App-bridge StringArrayPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / Add / Remove /
/// Reset).
/// </summary>
public class StringArrayPropertyEditorViewModelTests
{
    private static SchemaNode ArraySchema()
    {
        return new SchemaNode("tags", "tags") { ValueType = SchemaValueType.Array };
    }

    private static LibVm.StringArrayPropertyEditorViewModel NewVm(
        SchemaNode? schema = null, ConfigScope? scope = null)
    {
        return new LibVm.StringArrayPropertyEditorViewModel(new SchemaNodeAdapter(schema ?? ArraySchema()),
            ConfigScopeAdapter.For(scope ?? ConfigScope.User));
    }

    private static void Load(
        LibVm.StringArrayPropertyEditorViewModel vm,
        LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new LayeredValueAdapter(layered), ConfigScopeAdapter.For(scope));
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

    [Fact]
    public void InitialState_IsEmpty()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Assert.Empty(vm.Items);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void AddItem_AddsToCollection()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "hello";
        vm.AddItemCommand.Execute(null);

        Assert.Single(vm.Items);
        Assert.Equal("hello", vm.Items[0]);
        Assert.Equal(string.Empty, vm.NewItemText);
    }

    [Fact]
    public void AddItem_NoDuplicates()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);
        vm.NewItemText = "dup";
        vm.AddItemCommand.Execute(null);

        Assert.Single(vm.Items);
    }

    [Fact]
    public void RemoveItem_RemovesFromCollection()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "a";
        vm.AddItemCommand.Execute(null);
        vm.RemoveItemCommand.Execute("a");

        Assert.Empty(vm.Items);
    }

    [Fact]
    public void LoadFromValue_PopulatesItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWithArray(ConfigScope.User, "x", "y", "z"), ConfigScope.User);

        MessageAssert.SameElements(new[] { "x", "y", "z" }, vm.Items.ToArray());
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void ToValue_ReturnsNull_WhenEmpty()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        Assert.Null(vm.ToValue());
    }

    [Fact]
    public void ToValue_ReturnsList_WhenHasItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "one";
        vm.AddItemCommand.Execute(null);
        vm.NewItemText = "two";
        vm.AddItemCommand.Execute(null);

        IReadOnlyList<object?>? list = vm.ToValue() as IReadOnlyList<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Equal("one", list[0]);
        Assert.Equal("two", list[1]);
    }

    [Fact]
    public void Reset_ClearsItems()
    {
        LibVm.StringArrayPropertyEditorViewModel vm = NewVm();
        vm.NewItemText = "a";
        vm.AddItemCommand.Execute(null);
        vm.ResetToInheritedCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.False(vm.IsModified);
    }
}