using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Boolean leaf editor.
/// The App-bridge BooleanPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / ResetToInherited).
/// </summary>
[TestClass]
public class BooleanPropertyEditorViewModelTests
{
    private static SchemaNode BoolSchema(string name = "testBool")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Boolean };
    }

    private static LibVm.BooleanPropertyEditorViewModel NewVm(
        SchemaNode? schema = null, ConfigScope scope = ConfigScope.User)
    {
        return new LibVm.BooleanPropertyEditorViewModel(new ClaudeSchemaAdapter(schema ?? BoolSchema()),
            ClaudeScope.For(scope));
    }

    private static LayeredValue EmptyLayered(string key = "testBool")
    {
        return new LayeredValue(key, []);
    }

    private static LayeredValue LayeredWith(string key, ConfigScope scope, bool value)
    {
        ScopeEntry entry = new(scope, JsonValue.Create(value), "/fake");
        return new LayeredValue(key, [entry])
        {
            EffectiveValue = JsonValue.Create(value),
            EffectiveScope = scope,
        };
    }

    private static void Load(
        LibVm.BooleanPropertyEditorViewModel vm,
        LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(scope));
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void InitialValue_IsNull_WhenNoLayeredEntry()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, EmptyLayered(), ConfigScope.User);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.EffectiveScope);
    }

    [TestMethod]
    public void LoadFromValue_SetsValueFromScope()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWith("testBool", ConfigScope.User, true), ConfigScope.User);

        Assert.IsTrue(vm.Value);
        Assert.IsTrue(vm.IsModified);
        Assert.AreEqual("user", vm.EffectiveScope?.Id);
    }

    [TestMethod]
    public void LoadFromValue_DifferentScope_ValueIsNull()
    {
        // Value is set at Project scope, but we are editing User scope
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWith("testBool", ConfigScope.Project, false), ConfigScope.User);

        // GetValueAt(User) returns null since only Project has a value
        Assert.IsNull(vm.Value);
    }

    [TestMethod]
    public void ToValue_ReturnsNull_WhenValueIsNull()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsBool_WhenValueIsSet()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = false;

        object? value = vm.ToValue();
        Assert.IsNotNull(value);
        Assert.IsFalse((bool?)value);
    }

    [TestMethod]
    public void ResetToInherited_ClearsValue()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = true;
        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void CanReset_IsFalse_WhenNotModified()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Assert.IsFalse(vm.CanReset);
    }

    [TestMethod]
    public void CanReset_IsTrue_WhenModified()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = true;
        Assert.IsTrue(vm.CanReset);
    }
}