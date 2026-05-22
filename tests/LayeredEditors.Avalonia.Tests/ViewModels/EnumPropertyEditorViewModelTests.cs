using System.Collections;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

[TestClass]
public class EnumPropertyEditorViewModelTests
{
    private static readonly IReadOnlyList<string> Options = ["red", "green", "blue"];

    private static FakeEditorSchema Schema()
    {
        return new FakeEditorSchema("settings.color", EditorValueType.Enum) { EnumValues = Options };
    }

    private static FakeEditorValue Empty()
    {
        return new FakeEditorValue("settings.color");
    }

    private static FakeEditorValue WithUser(string v)
    {
        return new FakeEditorValue("settings.color").With(FakeEditorScope.User, v);
    }

    // ── Construction ──────────────────────────────────────────────────────────

    [TestMethod]
    public void EnumOptions_PopulatedFromSchema()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        CollectionAssert.AreEqual((ICollection)Options, (ICollection)vm.EnumOptions);
    }

    [TestMethod]
    public void Initial_SelectedValue_IsNull_NotModified()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.SelectedValue);
        Assert.IsFalse(vm.IsModified);
        Assert.IsFalse(vm.IsValueSet);
    }

    // ── LoadFromValue ─────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadFromValue_SetsSelectedValue()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(WithUser("green"), FakeEditorScope.User);

        Assert.AreEqual("green", vm.SelectedValue);
        Assert.IsTrue(vm.IsModified);
        Assert.IsTrue(vm.IsValueSet);
    }

    [TestMethod]
    public void LoadFromValue_Empty_SelectedValueIsNull()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(Empty(), FakeEditorScope.User);

        Assert.IsNull(vm.SelectedValue);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromValue_OtherScope_NoValueAtEditingScope()
    {
        FakeEditorValue value = new FakeEditorValue("settings.color").With(FakeEditorScope.Project, "blue");

        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.IsNull(vm.SelectedValue);
    }

    // ── ToValue ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToValue_ReturnsNull_WhenNotSet()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void ToValue_ReturnsSelectedString()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.SelectedValue = "red";
        Assert.AreEqual("red", vm.ToValue());
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetToInherited_ClearsSelectedValue()
    {
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.SelectedValue = "blue";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsNull(vm.SelectedValue);
        Assert.IsFalse(vm.IsModified);
    }

    // ── EnumOptions null schema ───────────────────────────────────────────────

    [TestMethod]
    public void EnumOptions_EmptyList_WhenSchemaHasNoEnumValues()
    {
        FakeEditorSchema schema = new("settings.x", EditorValueType.Enum); // EnumValues is null
        EnumPropertyEditorViewModel vm = new(schema, FakeEditorScope.User);
        Assert.AreEqual(0, vm.EnumOptions.Count);
    }

    // ── Free-form vs strict enum ──────────────────────────────────────────────

    /// <summary>
    /// Enums promoted from the schema <c>examples</c> keyword (AllowsFreeForm=true) must
    /// accept arbitrary values so users can type custom model identifiers etc.
    /// </summary>
    [TestMethod]
    public void FreeForm_SelectedValue_AcceptsArbitraryString()
    {
        FakeEditorSchema schema = new("settings.model", EditorValueType.Enum)
        {
            EnumValues = ["claude-sonnet-4-5", "claude-opus-4"],
            Examples = ["claude-sonnet-4-5", "claude-opus-4"], // promotes to free-form
        };
        EnumPropertyEditorViewModel vm = new(schema, FakeEditorScope.User);

        Assert.IsTrue(vm.AllowsFreeForm, "Schema with examples should be free-form.");
        Assert.IsFalse(vm.IsStrictEnum);

        vm.SelectedValue = "my-custom-model-id";
        Assert.AreEqual("my-custom-model-id", vm.SelectedValue);
        Assert.AreEqual("my-custom-model-id", vm.ToValue());
    }

    /// <summary>
    /// Strict enums (no <c>examples</c>, AllowsFreeForm=false) with a schema default
    /// should surface the default in the Watermark via <c>(inherits: X)</c> rather
    /// than the fallback <c>(not set)</c>. Pins the fix for Issue F.1.
    /// </summary>
    [TestMethod]
    public void StrictEnum_WithSchemaDefault_WatermarkShowsInheritsDefault()
    {
        FakeEditorSchema schema = new("settings.effortLevel", EditorValueType.Enum)
        {
            EnumValues = ["low", "medium", "high"],
            DefaultValue = "medium",
        };
        EnumPropertyEditorViewModel vm = new(schema, FakeEditorScope.User);
        vm.LoadFromValue(Empty() /* no value at any scope */, FakeEditorScope.User);

        Assert.IsFalse(vm.AllowsFreeForm);
        Assert.AreEqual("(inherits: medium)", vm.Watermark);
    }

    // ── OtherScopesWithData + InheritedFromScope ──────────────────

    [TestMethod]
    public void LoadFromValue_OtherScopesWithData_PopulatedFromDefiningScopes()
    {
        FakeEditorValue value = new FakeEditorValue("settings.color")
                                .With(FakeEditorScope.User, "red")
                                .With(FakeEditorScope.Project, "blue");
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.User);
        vm.LoadFromValue(value, FakeEditorScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void LoadFromValue_InheritedFromScope_PopulatedWhenEditingScopeEmpty()
    {
        FakeEditorValue value = new FakeEditorValue("settings.color").With(FakeEditorScope.User, "red");
        EnumPropertyEditorViewModel vm = new(Schema(), FakeEditorScope.Local);
        vm.LoadFromValue(value, FakeEditorScope.Local);

        Assert.IsNull(vm.SelectedValue);
        Assert.AreEqual("user", vm.InheritedFromScope?.Id);
        Assert.AreEqual("red", vm.InheritedDisplay);
        Assert.IsTrue(vm.HasInheritedFromOtherScope);
    }
}