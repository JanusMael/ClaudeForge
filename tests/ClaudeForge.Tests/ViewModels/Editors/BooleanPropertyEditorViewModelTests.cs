using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Boolean leaf editor.
/// The App-bridge BooleanPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / ResetToInherited).
/// </summary>
public class BooleanPropertyEditorViewModelTests
{
    private static SchemaNode BoolSchema(string name = "testBool")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Boolean };
    }

    private static LibVm.BooleanPropertyEditorViewModel NewVm(
        SchemaNode? schema = null, ConfigScope? scope = null)
    {
        return new LibVm.BooleanPropertyEditorViewModel(new SchemaNodeAdapter(schema ?? BoolSchema()),
            ConfigScopeAdapter.For(scope ?? ConfigScope.User));
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
        vm.LoadFromValue(new LayeredValueAdapter(layered), ConfigScopeAdapter.For(scope));
    }

    // -----------------------------------------------------------------------

    [Fact]
    public void InitialValue_IsNull_WhenNoLayeredEntry()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, EmptyLayered(), ConfigScope.User);

        Assert.Null(vm.Value);
        Assert.False(vm.IsModified);
        Assert.Null(vm.EffectiveScope);
    }

    [Fact]
    public void LoadFromValue_SetsValueFromScope()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWith("testBool", ConfigScope.User, true), ConfigScope.User);

        Assert.True(vm.Value);
        Assert.True(vm.IsModified);
        Assert.Equal("user", vm.EffectiveScope?.Id);
    }

    [Fact]
    public void LoadFromValue_DifferentScope_ValueIsNull()
    {
        // Value is set at Project scope, but we are editing User scope
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Load(vm, LayeredWith("testBool", ConfigScope.Project, false), ConfigScope.User);

        // GetValueAt(User) returns null since only Project has a value
        Assert.Null(vm.Value);
    }

    [Fact]
    public void ToValue_ReturnsNull_WhenValueIsNull()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Assert.Null(vm.ToValue());
    }

    [Fact]
    public void ToValue_ReturnsBool_WhenValueIsSet()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = false;

        object? value = vm.ToValue();
        Assert.NotNull(value);
        Assert.False((bool?)value);
    }

    [Fact]
    public void ResetToInherited_ClearsValue()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = true;
        vm.ResetToInheritedCommand.Execute(null);

        Assert.Null(vm.Value);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void CanReset_IsFalse_WhenNotModified()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        Assert.False(vm.CanReset);
    }

    [Fact]
    public void CanReset_IsTrue_WhenModified()
    {
        LibVm.BooleanPropertyEditorViewModel vm = NewVm();
        vm.Value = true;
        Assert.True(vm.CanReset);
    }
}