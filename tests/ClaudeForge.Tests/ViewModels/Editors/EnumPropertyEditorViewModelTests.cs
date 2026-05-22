using Bennewitz.Ninja.ClaudeForge.Adapters;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests for the (now library-only) Enum leaf editor.
/// The App-bridge EnumPropertyEditorViewModel shim was deleted; this file
/// constructs the library type directly via the Claude schema/scope adapters
/// and exercises the library API (ToValue / LoadFromValue).
/// </summary>
[TestClass]
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
        SchemaNode schema, ConfigScope scope = ConfigScope.User)
    {
        return new LibVm.EnumPropertyEditorViewModel(new ClaudeSchemaAdapter(schema), ClaudeScope.For(scope));
    }

    private static void Load(
        LibVm.EnumPropertyEditorViewModel vm, LayeredValue layered, ConfigScope scope)
    {
        vm.LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(scope));
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

    [TestMethod]
    public void EnumOptions_ReflectSchemaValues()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b", "c"));
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, vm.EnumOptions.ToArray());
    }

    [TestMethod]
    public void StrictEnum_DoesNotAllowFreeForm()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b"));
        Assert.IsTrue(vm.IsStrictEnum);
        Assert.IsFalse(vm.AllowsFreeForm);
    }

    [TestMethod]
    public void EnumPromotedFromExamples_AllowsFreeForm()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(FreeFormSchema("alpha", "beta"));
        Assert.IsTrue(vm.AllowsFreeForm);
        Assert.IsFalse(vm.IsStrictEnum);
    }

    [TestMethod]
    public void LoadFromValue_SetsSelectedValue()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("x", "y"));
        Load(vm, LayeredWith(ConfigScope.User, "x"), ConfigScope.User);
        Assert.AreEqual("x", vm.SelectedValue);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNoSelection()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a"));
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsString_WhenSelected()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a", "b"));
        vm.SelectedValue = "b";
        Assert.AreEqual("b", vm.ToValue());
    }

    [TestMethod]
    public void Reset_ClearsSelection()
    {
        LibVm.EnumPropertyEditorViewModel vm = NewVm(EnumSchema("a"));
        vm.SelectedValue = "a";
        vm.ResetToInheritedCommand.Execute(null);
        Assert.IsNull(vm.SelectedValue);
        Assert.IsFalse(vm.IsModified);
    }
}