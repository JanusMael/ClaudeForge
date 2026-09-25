using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Number leaf editor.
/// The App-bridge NumberPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / ResetToInherited).
/// </summary>
public class NumberPropertyEditorViewModelTests
{
    private static SchemaNode IntSchema(double? min = null, double? max = null)
    {
        return new SchemaNode("count", "count") { ValueType = SchemaValueType.Integer, Minimum = min, Maximum = max };
    }

    private static SchemaNode DoubleSchema()
    {
        return new SchemaNode("ratio", "ratio") { ValueType = SchemaValueType.Number };
    }

    private static LibVm.NumberPropertyEditorViewModel NewVm(
        SchemaNode schema, ConfigScope? scope = null)
    {
        return new LibVm.NumberPropertyEditorViewModel(new SchemaNodeAdapter(schema), ConfigScopeAdapter.For(scope ?? ConfigScope.User));
    }

    private static void Load(
        LibVm.NumberPropertyEditorViewModel vm, LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new LayeredValueAdapter(layered), ConfigScopeAdapter.For(scope));
    }

    private static LayeredValue LayeredLong(ConfigScope scope, long value)
    {
        ScopeEntry entry = new(scope, JsonValue.Create(value), "/fake");
        return new LayeredValue("count", [entry])
        {
            EffectiveValue = JsonValue.Create(value),
            EffectiveScope = scope,
        };
    }

    [Fact]
    public void IsInteger_TrueForIntegerSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Assert.True(vm.IsInteger);
    }

    [Fact]
    public void IsInteger_FalseForNumberSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(DoubleSchema());
        Assert.False(vm.IsInteger);
    }

    [Fact]
    public void Bounds_AreExposedFromSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema(min: 1, max: 100));
        Assert.Equal(1.0, vm.Minimum);
        Assert.Equal(100.0, vm.Maximum);
    }

    [Fact]
    public void LoadFromValue_SetsValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Load(vm, LayeredLong(ConfigScope.User, 42), ConfigScope.User);

        Assert.Equal(42.0, vm.Value);
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void ToValue_ReturnsLongForInteger()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        vm.Value = 7.0;
        object? v = vm.ToValue();
        Assert.IsAssignableFrom<long>(v);
        Assert.Equal(7L, v);
    }

    [Fact]
    public void ToValue_ReturnsDoubleForNumber()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(DoubleSchema());
        vm.Value = 3.5;
        object? v = vm.ToValue();
        Assert.IsAssignableFrom<double>(v);
        Assert.Equal(3.5, v);
    }

    [Fact]
    public void ToValue_ReturnsNull_WhenNoValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Assert.Null(vm.ToValue());
    }

    [Fact]
    public void Reset_ClearsValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        vm.Value = 5;
        vm.ResetToInheritedCommand.Execute(null);

        Assert.Null(vm.Value);
        Assert.False(vm.IsModified);
    }
}