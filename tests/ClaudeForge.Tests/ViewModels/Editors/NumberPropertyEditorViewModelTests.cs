using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Number leaf editor.
/// The App-bridge NumberPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue / ResetToInherited).
/// </summary>
[TestClass]
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
        SchemaNode schema, ConfigScope scope = ConfigScope.User)
    {
        return new LibVm.NumberPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(scope));
    }

    private static void Load(
        LibVm.NumberPropertyEditorViewModel vm, LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(scope));
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

    [TestMethod]
    public void IsInteger_TrueForIntegerSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Assert.IsTrue(vm.IsInteger);
    }

    [TestMethod]
    public void IsInteger_FalseForNumberSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(DoubleSchema());
        Assert.IsFalse(vm.IsInteger);
    }

    [TestMethod]
    public void Bounds_AreExposedFromSchema()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema(min: 1, max: 100));
        Assert.AreEqual(1.0, vm.Minimum);
        Assert.AreEqual(100.0, vm.Maximum);
    }

    [TestMethod]
    public void LoadFromValue_SetsValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Load(vm, LayeredLong(ConfigScope.User, 42), ConfigScope.User);

        Assert.AreEqual(42.0, vm.Value);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void ToValue_ReturnsLongForInteger()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        vm.Value = 7.0;
        object? v = vm.ToValue();
        Assert.IsInstanceOfType<long>(v);
        Assert.AreEqual(7L, v);
    }

    [TestMethod]
    public void ToValue_ReturnsDoubleForNumber()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(DoubleSchema());
        vm.Value = 3.5;
        object? v = vm.ToValue();
        Assert.IsInstanceOfType<double>(v);
        Assert.AreEqual(3.5, v);
    }

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNoValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void Reset_ClearsValue()
    {
        LibVm.NumberPropertyEditorViewModel vm = NewVm(IntSchema());
        vm.Value = 5;
        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.Value);
        Assert.IsFalse(vm.IsModified);
    }
}