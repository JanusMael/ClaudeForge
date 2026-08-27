using System.ComponentModel;
using Bennewitz.Ninja.OpenCode.Avalonia.Plugins;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The plugin list editor and the TUI's plugin-toggle editor.
/// </summary>
[TestClass]
public sealed class OpenCodePluginEditorViewModelTests
{
    private static OpenCodePluginEditorViewModel LoadedWith(string json)
    {
        OpenCodePluginEditorViewModel vm = new(new TestSchema("plugin"), TestScope.Project);
        vm.LoadFromValue(
            new TestValue("plugin").With(TestScope.Project, CurrencyText.Parse(json)),
            TestScope.Project);
        return vm;
    }

    private static OpenCodePluginEnabledEditorViewModel TogglesLoadedWith(string json)
    {
        OpenCodePluginEnabledEditorViewModel vm =
            new(new TestSchema("plugin_enabled"), TestScope.Project);
        vm.LoadFromValue(
            new TestValue("plugin_enabled").With(TestScope.Project, CurrencyText.Parse(json)),
            TestScope.Project);
        return vm;
    }

    private static void AssertRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(LoadedWith(json).ToValue()),
            because);

    // ── Round trips ──────────────────────────────────────────────────────────

    [TestMethod]
    public void BothArmsRoundTripThroughTheEditor()
    {
        AssertRoundTrips(
            """["bare",["with-opts",{"a":1}],"another"]""",
            "Opening a config and saving it untouched must not change it.");
    }

    [TestMethod]
    public void AnEmptyOptionsObjectSurvivesTheEditor()
    {
        AssertRoundTrips(
            """[["foo",{}]]""",
            "Collapsing this to the bare form would silently move the entry to the other arm of "
            + "the union.");
    }

    [TestMethod]
    public void ToValue_IsNullWhenThereAreNoPlugins()
    {
        OpenCodePluginEditorViewModel vm = new(new TestSchema("plugin"), TestScope.Project);
        vm.LoadFromValue(new TestValue("plugin"), TestScope.Project);

        Assert.IsNull(vm.ToValue());
    }

    // ── ⭐ The options arm and its preservation ──────────────────────────────

    /// <remarks>
    /// ⭐ Third place in this phase the preserve-the-other-arm rule has mattered. Flipping the
    /// toggle off and on again must give the user their options back.
    /// </remarks>
    [TestMethod]
    public void TogglingOptionsOffAndOnKeepsTheOptionsText()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["foo",{"a":1}]]""");
        OpenCodePluginRowViewModel row = vm.Plugins.Rows.Single();

        string original = row.OptionsText;
        Assert.IsTrue(original.Contains('a', StringComparison.Ordinal));

        row.HasOptions = false;
        Assert.AreEqual(
            "[foo]",
            CurrencyText.Render(vm.ToValue()),
            "With options off, the bare form is what gets written.");
        Assert.AreEqual(
            original,
            row.OptionsText,
            "The text stays in memory while the bare form is being written.");

        row.HasOptions = true;

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "a:1",
            "And switching back restores the options rather than an empty object.");
    }

    [TestMethod]
    public void TurningOptionsOffWritesTheBareForm()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["foo",{"a":1}]]""");
        vm.Plugins.Rows[0].HasOptions = false;

        Assert.AreEqual(
            "[foo]",
            CurrencyText.Render(vm.ToValue()),
            "The element becomes a bare string, not a one-element array.");
    }

    [TestMethod]
    public void BlankOptionsTextCountsAsAnEmptyObject()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["foo"]""");
        OpenCodePluginRowViewModel row = vm.Plugins.Rows.Single();

        row.HasOptions = true;
        row.OptionsText = "   ";

        Assert.IsFalse(row.HasOptionsError, "Blank is a valid 'options, but none yet'.");
        Assert.AreEqual("[[foo,{}]]", CurrencyText.Render(vm.ToValue()));
    }

    /// <remarks>
    /// ⚠ A JSON text box is unparseable most of the time it is being used. Treating every
    /// intermediate keystroke as "no options" would delete the user's configuration on a live-write
    /// host before they finished typing it.
    /// </remarks>
    [TestMethod]
    public void HalfTypedOptionsKeepTheLastGoodValue()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["foo",{"a":1}]]""");
        OpenCodePluginRowViewModel row = vm.Plugins.Rows.Single();

        row.OptionsText = "{\"a\": ";

        Assert.IsTrue(row.HasOptionsError);
        Assert.AreEqual(1, vm.OptionErrorCount);
        Assert.IsTrue(vm.HasOptionErrors);

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "a:1",
            "The last valid options survive mid-typing.");
    }

    [TestMethod]
    public void OptionsThatAreValidJsonButNotAnObjectAreRejected()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["foo",{}]]""");
        OpenCodePluginRowViewModel row = vm.Plugins.Rows.Single();

        row.OptionsText = "[1,2,3]";

        Assert.IsTrue(
            row.HasOptionsError,
            "The schema's prefixItems types this element as an object, so an array here would "
            + "produce a config OpenCode rejects.");
    }

    // ── Opaque elements ──────────────────────────────────────────────────────

    [TestMethod]
    public void AnUnreadableElementIsNotEditableAndSurvivesASave()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["only-one"],"good"]""");

        Assert.AreEqual(1, vm.OpaqueEntryCount);
        Assert.IsTrue(vm.HasOpaqueEntries);
        Assert.IsTrue(vm.Plugins.Rows[0].IsOpaqueEntry);
        Assert.IsFalse(vm.Plugins.Rows[0].IsEditable);
        Assert.IsFalse(vm.Plugins.Rows[1].IsOpaqueEntry);

        AssertRoundTrips(
            """[["only-one"],"good"]""",
            "A plugin silently dropped is a plugin the user believes is loaded.");
    }

    [TestMethod]
    public void EditingOneElementDoesNotDisturbAnUnreadableNeighbour()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["only-one"],"good"]""");

        vm.Plugins.Rows[1].Name = "better";

        string written = CurrencyText.Render(vm.ToValue());
        StringAssert.Contains(written, "only-one");
        StringAssert.Contains(written, "better");
    }

    // ── Order ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ReorderingAnElementChangesTheWrittenOrder()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["a","b","c"]""");

        vm.Plugins.Rows[2].MoveUpCommand.Execute(null);

        Assert.AreEqual("[a,c,b]", CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void ANewlyAddedRowCanBeReorderedImmediately()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["a"]""");

        vm.Plugins.NewName = "b";
        vm.Plugins.AddCommand.Execute(null);

        Assert.IsTrue(
            vm.Plugins.Rows[1].MoveUpCommand.CanExecute(null),
            "A row added through the UI must be adopted by the list, or its reorder buttons stay "
            + "disabled for the life of the editor.");
    }

    // ── Modification plumbing ────────────────────────────────────────────────

    private static int CountIsModifiedFires(OpenCodePluginEditorViewModel vm, Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpenCodePluginEditorViewModel.IsModified))
            {
                fired++;
            }
        }

        vm.PropertyChanged += Handler;
        try
        {
            mutate();
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }

        return fired;
    }

    /// <remarks>
    /// ⚠ The load path repopulates the collection while the <c>_isLoading</c> guard is up, so
    /// <c>CollectionChanged</c> never hooked these rows — the editor subscribes them explicitly.
    /// The same trap as the MCP editor's nested lists, reached by a different route.
    /// </remarks>
    [TestMethod]
    public void EditingALoadedRow_FiresIsModifiedPropertyChanged()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["a"]""");
        Assert.IsTrue(vm.IsModified);

        int fired = CountIsModifiedFires(vm, () => vm.Plugins.Rows[0].Name = "b");

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void EditingLoadedOptions_FiresIsModifiedPropertyChanged()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["a",{"x":1}]]""");

        int fired = CountIsModifiedFires(vm, () => vm.Plugins.Rows[0].OptionsText = "{\"x\":2}");

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void TypingInTheAddBox_DoesNotMarkTheEditorModified()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["a"]""");

        int fired = CountIsModifiedFires(vm, () => vm.Plugins.NewName = "b");

        Assert.AreEqual(0, fired);
    }

    /// <remarks>
    /// ⭐⭐ <b>Added in 9a-8 after a canary there exposed this exact defect in the new editors, and
    /// reading this one showed it had it too.</b> The load path unsubscribes, calls
    /// <c>Reset</c>, then re-subscribes every row — but <c>Rows.CollectionChanged</c> is still
    /// attached during <c>Reset</c>, so each <c>Add</c> already subscribed the row on the way in and
    /// the explicit loop subscribed it a second time. Every row carried two handlers,
    /// <c>MarkModified</c> fired twice per keystroke, and <c>OneEditDoesNotCascadeThroughTheRecount</c>
    /// tolerates the doubling because it only bounds the count at four. Nothing else noticed.
    /// </remarks>
    [TestMethod]
    public void ReloadingDoesNotAccumulateHandlers()
    {
        OpenCodePluginEditorViewModel vm = new(new TestSchema("plugin"), TestScope.Project);
        TestValue value = new TestValue("plugin")
            .With(TestScope.Project, CurrencyText.Parse("""["a"]"""));

        vm.LoadFromValue(value, TestScope.Project);
        int afterOneLoad = CountIsModifiedFires(vm, () => vm.Plugins.Rows[0].Name = "first");

        vm.LoadFromValue(value, TestScope.Project);
        vm.LoadFromValue(value, TestScope.Project);
        int afterThreeLoads = CountIsModifiedFires(vm, () => vm.Plugins.Rows[0].Name = "second");

        Assert.AreEqual(
            1,
            afterOneLoad,
            "One keystroke should mark the editor modified exactly once; more means the row "
            + "carries duplicate handlers.");
        Assert.AreEqual(
            afterOneLoad,
            afterThreeLoads,
            $"Handlers accumulated across reloads: {afterOneLoad} fire(s) after one load, "
            + $"{afterThreeLoads} after three.");
    }

    /// <remarks>Same defect, same fix, in the sibling toggle editor.</remarks>
    [TestMethod]
    public void ReloadingTheToggleEditorDoesNotAccumulateHandlers()
    {
        OpenCodePluginEnabledEditorViewModel vm =
            new(new TestSchema("plugin_enabled"), TestScope.Project);
        TestValue value = new TestValue("plugin_enabled")
            .With(TestScope.Project, CurrencyText.Parse("""{"a":true}"""));

        // ⚠ Both mutations must set the SAME target value, not opposite ones. A reload restores
        // the row to `true`, so a second mutation to `true` is elided by the generated setter and
        // counts zero fires — which reads exactly like a lost handler.
        vm.LoadFromValue(value, TestScope.Project);
        int afterOneLoad = CountIsModifiedFires2(vm, () => vm.Toggles.Rows[0].Enabled = false);

        vm.LoadFromValue(value, TestScope.Project);
        vm.LoadFromValue(value, TestScope.Project);
        int afterThreeLoads = CountIsModifiedFires2(vm, () => vm.Toggles.Rows[0].Enabled = false);

        Assert.AreEqual(1, afterOneLoad, "Duplicate handlers on a loaded toggle row.");
        Assert.AreEqual(afterOneLoad, afterThreeLoads, "Handlers accumulated across reloads.");
    }

    private static int CountIsModifiedFires2(
        OpenCodePluginEnabledEditorViewModel vm,
        Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpenCodePluginEnabledEditorViewModel.IsModified))
            {
                fired++;
            }
        }

        vm.PropertyChanged += Handler;
        try
        {
            mutate();
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }

        return fired;
    }

    [TestMethod]
    public void OneEditDoesNotCascadeThroughTheRecount()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""[["a",{"x":1}]]""");

        int fired = CountIsModifiedFires(vm, () => vm.Plugins.Rows[0].OptionsText = "{bad");

        Assert.IsTrue(fired is >= 1 and <= 4, $"Expected a small bounded number of fires, got {fired}.");
    }

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedState()
    {
        OpenCodePluginEditorViewModel vm = LoadedWith("""["a"]""");

        vm.Plugins.NewName = "extra";
        vm.Plugins.AddCommand.Execute(null);
        Assert.AreEqual(2, vm.Plugins.Rows.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Plugins.Rows.Count);
        Assert.AreEqual("a", vm.Plugins.Rows[0].Name);
        Assert.IsFalse(vm.IsModified);
    }

    // ── plugin_enabled ───────────────────────────────────────────────────────

    [TestMethod]
    public void TheToggleMapRoundTrips()
    {
        OpenCodePluginEnabledEditorViewModel vm = TogglesLoadedWith("""{"a":true,"b":false}""");

        Assert.AreEqual(2, vm.Toggles.Rows.Count);
        Assert.AreEqual("{a:true,b:false}", CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void ANonBooleanToggleIsShownAsUneditableAndPreserved()
    {
        OpenCodePluginEnabledEditorViewModel vm =
            TogglesLoadedWith("""{"good":true,"weird":"yes"}""");

        Assert.IsFalse(vm.Toggles.Rows[0].IsOpaqueEntry);
        Assert.IsTrue(vm.Toggles.Rows[1].IsOpaqueEntry);
        Assert.IsFalse(vm.Toggles.Rows[1].IsEditable);

        Assert.AreEqual(
            "{good:true,weird:yes}",
            CurrencyText.Render(vm.ToValue()),
            "The entry this build cannot toggle still has to survive.");
    }

    [TestMethod]
    public void TogglingAValueAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodePluginEnabledEditorViewModel vm = TogglesLoadedWith("""{"a":true}""");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OpenCodePluginEnabledEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Toggles.Rows[0].Enabled = false;

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void ToValue_IsNullWhenThereAreNoToggles()
    {
        OpenCodePluginEnabledEditorViewModel vm =
            new(new TestSchema("plugin_enabled"), TestScope.Project);
        vm.LoadFromValue(new TestValue("plugin_enabled"), TestScope.Project);

        Assert.IsNull(vm.ToValue());
    }
}
