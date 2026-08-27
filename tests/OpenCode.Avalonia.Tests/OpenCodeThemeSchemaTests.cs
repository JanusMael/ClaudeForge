using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Themes;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The <c>theme</c> field's editor, which is the library's enum editor plus a schema wrapper.
/// </summary>
/// <remarks>
/// ⭐ <b>The contract under test is a composition, so the tests assert the composed result.</b> The
/// wrapper is only interesting because of what <c>EnumPropertyEditorViewModel</c> does when handed
/// it — a test that checked the wrapper's properties alone would pass even if the pairing produced a
/// closed ComboBox that rejected every theme this build has not heard of.
/// </remarks>
[TestClass]
public sealed class OpenCodeThemeSchemaTests
{
    private static readonly string[] Themes = ["opencode", "dracula", "nord"];

    private static OpenCodeThemeSchema Wrapped(params string[] themes) =>
        new(new TestSchema("theme") { Description = "Colour theme for the terminal UI." }, themes);

    private static EnumPropertyEditorViewModel Editor(params string[] themes) =>
        new(Wrapped(themes), TestScope.Project);

    // ── The composed result: a picker that still accepts anything ────────────────────────────────

    /// <summary>
    /// ⚠⚠ <b>The whole point of the slice.</b> A non-empty <c>Examples</c> is what the enum editor
    /// reads as "the user may type a value that is not on the list" — and for a schema whose only
    /// declaration is <c>{"type":"string"}</c>, anything else would be an invention. If this ever
    /// reports a strict enum, the editor has started refusing valid themes.
    /// </summary>
    [TestMethod]
    public void TheEditorIsAPickerThatStillAcceptsFreeText()
    {
        EnumPropertyEditorViewModel vm = Editor(Themes);

        Assert.IsTrue(vm.AllowsFreeForm, "a bare-string schema must never become a closed list");
        Assert.IsFalse(vm.IsStrictEnum);
    }

    [TestMethod]
    public void TheEditorOffersEveryDiscoveredTheme_InOrder()
    {
        EnumPropertyEditorViewModel vm = Editor(Themes);

        CollectionAssert.AreEqual(Themes, vm.EnumOptions.ToArray());
        CollectionAssert.AreEqual(Themes, vm.EnumOptionItems.Select(o => o.Value).ToArray());
    }

    /// <summary>
    /// Each option says where it came from, which is the difference between "a name you can use"
    /// and "a name this machine actually has".
    /// </summary>
    [TestMethod]
    public void TheFirstOptionIsLabelledBuiltIn_AndTheRestAsDiscovered()
    {
        EnumPropertyEditorViewModel vm = Editor(Themes);

        string? builtIn = vm.EnumOptionItems[0].Description;
        string? discovered = vm.EnumOptionItems[1].Description;

        Assert.IsFalse(string.IsNullOrWhiteSpace(builtIn));
        Assert.IsFalse(string.IsNullOrWhiteSpace(discovered));
        Assert.AreNotEqual(builtIn, discovered, "built-in and discovered must not read the same");
        Assert.AreEqual(discovered, vm.EnumOptionItems[2].Description);
    }

    // ── The wrapper's own contract ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ <c>Examples</c> exists to be non-empty, not to be a second copy of the list. Holding all
    /// 40 strings twice would leave two places to update and no reader for the second.
    /// </summary>
    [TestMethod]
    public void ExamplesCarriesOnlyTheBuiltIn_NotTheWholeList()
    {
        OpenCodeThemeSchema schema = Wrapped(Themes);

        CollectionAssert.AreEqual(new[] { "opencode" }, schema.Examples.ToArray());
        Assert.AreEqual(3, schema.EnumValues!.Count);
    }

    /// <summary>
    /// The wrapper is handed to the enum editor, so it must classify as one even if the overlay that
    /// normally promotes the inner node were missing.
    /// </summary>
    [TestMethod]
    public void TheValueTypeIsEnum_WhateverTheInnerNodeSays()
    {
        Assert.AreEqual(EditorValueType.Complex, new TestSchema("theme").ValueType);
        Assert.AreEqual(EditorValueType.Enum, Wrapped(Themes).ValueType);
    }

    /// <summary>
    /// The description belongs to the schema overlay, so it must survive the wrapper untouched —
    /// that text is where the other theme locations are explained.
    /// </summary>
    [TestMethod]
    public void TheInnerDescriptionAndIdentityPassThrough()
    {
        OpenCodeThemeSchema schema = Wrapped(Themes);

        Assert.AreEqual("Colour theme for the terminal UI.", schema.Description);
        Assert.AreEqual("theme", schema.Name);
        Assert.AreEqual("theme", schema.Path);
    }

    /// <summary>
    /// Discovery never returns an empty list, but the wrapper must not produce a dead control if it
    /// ever did — an empty <c>Examples</c> would make a closed ComboBox with nothing in it.
    /// </summary>
    [TestMethod]
    public void AnEmptyThemeListDoesNotClaimFreeForm()
    {
        OpenCodeThemeSchema schema = Wrapped();

        Assert.AreEqual(0, schema.Examples.Count);
        Assert.AreEqual(0, schema.EnumValues!.Count);
        Assert.AreEqual(0, schema.EnumValueDescriptions.Count);
    }

    [TestMethod]
    public void NullArgumentsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new OpenCodeThemeSchema(null!, Themes));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new OpenCodeThemeSchema(new TestSchema("theme"), null!));
    }

    // ── The value itself is untouched ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ A theme name this build has never heard of must round-trip exactly. The suggestion list is
    /// a convenience; the file is the user's.
    /// </summary>
    [TestMethod]
    public void AThemeNameOffTheListRoundTripsUnchanged()
    {
        EnumPropertyEditorViewModel vm = Editor(Themes);
        vm.LoadFromValue(
            new TestValue("theme").With(TestScope.Project, "a-theme-from-a-plugin"),
            TestScope.Project);

        Assert.AreEqual("a-theme-from-a-plugin", vm.ToValue());
    }

    [TestMethod]
    public void AnAbsentThemeWritesNothing()
    {
        EnumPropertyEditorViewModel vm = Editor(Themes);
        vm.LoadFromValue(new TestValue("theme"), TestScope.Project);

        Assert.IsNull(vm.ToValue());
    }
}
