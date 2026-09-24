using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Enum leaf editor.
/// The App-bridge EnumPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue).
/// </summary>
public class EnumPropertyEditorViewModelTests
{
    private static SchemaNode EnumSchema(params string[] values)
    {
        return new SchemaNode("mode", "mode")
        {
            ValueType = SchemaValueType.Enum,
            EnumValues = values,
        };
    }

    private static SchemaNode FreeFormSchema(params string[] examples)
    {
        return new SchemaNode("mode", "mode")
        {
            ValueType = SchemaValueType.Enum,
            Examples = examples,
        };
    }

    private static LibVm.EnumPropertyEditorViewModel NewVm(
        SchemaNode schema, ConfigScope? scope = null)
    {
        return new LibVm.EnumPropertyEditorViewModel(new SchemaNodeAdapter(schema), ConfigScopeAdapter.For(scope ?? ConfigScope.User));
    }

    private static void Load(
        LibVm.EnumPropertyEditorViewModel vm, LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new LayeredValueAdapter(layered), ConfigScopeAdapter.For(scope));
    }

    private static LayeredValue LayeredWith(ConfigScope scope, string value)
    {
        ScopeEntry entry = new(scope, JsonValue.Create(value), "/fake");
        return new LayeredValue("mode", [entry])
        {
            EffectiveValue = JsonValue.Create(value),
            EffectiveScope = scope,
        };
    }

    [Fact]
    public void EnumOptions_ReflectSchemaValues()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b", "c"));
        Assert.Equal(new[] { "a", "b", "c" }, vm.EnumOptions.ToArray());
    }

    [Fact]
    public void StrictEnum_DoesNotAllowFreeForm()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b"));
        Assert.True(vm.IsStrictEnum);
        Assert.False(vm.AllowsFreeForm);
    }

    [Fact]
    public void EnumPromotedFromExamples_AllowsFreeForm()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(FreeFormSchema("alpha", "beta"));
        Assert.True(vm.AllowsFreeForm);
        Assert.False(vm.IsStrictEnum);
    }

    [Fact]
    public void LoadFromValue_SetsSelectedValue()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("x", "y"));
        Load(vm, LayeredWith(ConfigScope.User, "x"), ConfigScope.User);
        Assert.Equal("x", vm.SelectedValue);
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void ToValue_ReturnsNull_WhenNoSelection()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a"));
        Assert.Null(vm.ToValue());
    }

    [Fact]
    public void ToValue_ReturnsString_WhenSelected()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b"));
        vm.SelectedValue = "b";
        Assert.Equal("b", vm.ToValue());
    }

    [Fact]
    public void Reset_ClearsSelection()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a"));
        vm.SelectedValue = "a";
        vm.ResetToInheritedCommand.Execute(null);
        Assert.Null(vm.SelectedValue);
        Assert.False(vm.IsModified);
    }
}