using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Updates;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The <c>autoupdate</c> editor: a four-state picker over <c>true</c> | <c>false</c> |
/// <c>"notify"</c> | absent.
/// </summary>
/// <remarks>
/// The behaviour worth guarding is that all four states are reachable and distinct through the
/// picker, and that the one state a value can arrive in but a user cannot choose — unrecognised —
/// stays out of the list while still being reported.
/// </remarks>
[TestClass]
public sealed class OpenCodeAutoupdateEditorViewModelTests
{
    private static OpenCodeAutoupdateEditorViewModel Editor(object? value, bool defined = true)
    {
        OpenCodeAutoupdateEditorViewModel vm = new(new TestSchema("autoupdate"), TestScope.Project);
        TestValue layered = new("autoupdate");
        if (defined)
        {
            layered = layered.With(TestScope.Project, value);
        }

        vm.LoadFromValue(layered, TestScope.Project);
        return vm;
    }

    private static int CountIsModifiedFires(PropertyEditorViewModel vm, Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PropertyEditorViewModel.IsModified))
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

    private static void Select(
        OpenCodeAutoupdateEditorViewModel vm, OpenCodeAutoupdateMode mode) =>
        vm.SelectedOption = OpenCodeAutoupdateEditorViewModel.Options.Single(o => o.Mode == mode);

    [TestMethod]
    public void EveryValueRoundTripsThroughTheEditor()
    {
        Assert.AreEqual(true, Editor(true).ToValue());
        Assert.AreEqual(false, Editor(false).ToValue());
        Assert.AreEqual("notify", Editor("notify").ToValue());
        Assert.IsNull(Editor(null, defined: false).ToValue());
    }

    [TestMethod]
    public void TheModeReflectsWhatWasLoaded()
    {
        Assert.AreEqual(OpenCodeAutoupdateMode.Automatic, Editor(true).Mode);
        Assert.AreEqual(OpenCodeAutoupdateMode.Disabled, Editor(false).Mode);
        Assert.AreEqual(OpenCodeAutoupdateMode.Notify, Editor("notify").Mode);
        Assert.AreEqual(OpenCodeAutoupdateMode.NotSet, Editor(null, defined: false).Mode);
    }

    /// <remarks>
    /// ⚠ Every option must produce a DIFFERENT written value, or the picker is offering the user a
    /// choice that does not exist. Three of these four have overlapping runtime behaviour, which is
    /// exactly why the written forms have to be checked rather than assumed.
    /// </remarks>
    [TestMethod]
    public void EachOptionWritesADistinctValue()
    {
        List<object?> written = [];
        foreach (OpenCodeAutoupdateOption option in OpenCodeAutoupdateEditorViewModel.Options)
        {
            OpenCodeAutoupdateEditorViewModel vm = Editor(null, defined: false);
            vm.SelectedOption = option;
            written.Add(vm.ToValue());
        }

        CollectionAssert.AreEquivalent(
            new object?[] { null, false, "notify", true },
            written,
            "Two options write the same value, so the picker is offering a distinction the file "
            + "does not have.");
    }

    [TestMethod]
    public void SelectingNotSetFromAValue_RemovesTheKey()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("notify");
        Select(vm, OpenCodeAutoupdateMode.NotSet);

        Assert.IsNull(
            vm.ToValue(),
            "Choosing 'not set' must remove the key, which is a different claim from writing false.");
    }

    /// <remarks>
    /// The picker never offers <see cref="OpenCodeAutoupdateMode.Unrecognised"/>: it is a state a
    /// value arrives in, and its held value may be a JSON null that the currency can only express
    /// as "remove the key".
    /// </remarks>
    [TestMethod]
    public void TheOptionsListOffersTheFourChoosableStatesAndNotTheFifth()
    {
        IReadOnlyList<OpenCodeAutoupdateOption> options =
            OpenCodeAutoupdateEditorViewModel.Options;

        Assert.AreEqual(4, options.Count);
        CollectionAssert.AreEquivalent(
            new[]
            {
                OpenCodeAutoupdateMode.NotSet,
                OpenCodeAutoupdateMode.Disabled,
                OpenCodeAutoupdateMode.Notify,
                OpenCodeAutoupdateMode.Automatic,
            },
            options.Select(o => o.Mode).ToArray());
        Assert.IsFalse(
            options.Any(o => o.Mode == OpenCodeAutoupdateMode.Unrecognised),
            "The unrecognised state became selectable. Choosing it would write a held value that "
            + "may be null, i.e. silently delete the key.");
    }

    [TestMethod]
    public void EveryOptionCarriesLabelAndHelpText()
    {
        foreach (OpenCodeAutoupdateOption option in OpenCodeAutoupdateEditorViewModel.Options)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(option.Label), $"{option.Mode} has no label.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(option.Description),
                $"{option.Mode} has no help text — and the help is the only thing that tells the "
                + "user these overlapping behaviours are different files.");
        }
    }

    [TestMethod]
    public void AnUnrecognisedValue_IsHeldVerbatimAndSaidSo()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("Notify");

        Assert.IsTrue(vm.IsUnrecognised, "'Notify' is not the schema's literal and must be flagged.");
        Assert.AreEqual(OpenCodeAutoupdateMode.Unrecognised, vm.Mode);
        Assert.AreEqual("Notify", vm.ToValue());
        Assert.IsTrue(vm.UnrecognisedText.Contains("Notify", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplacingAnUnrecognisedValue_LeavesTheHeldStateForASelectableOne()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor(5L);
        Assert.IsTrue(vm.IsUnrecognised);

        vm.ReplaceUnrecognisedCommand.Execute(null);

        Assert.IsFalse(vm.IsUnrecognised);
        Assert.AreEqual(string.Empty, vm.UnrecognisedText);
        Assert.AreEqual(
            "notify",
            vm.ToValue(),
            "Replacing must land on a value the schema admits, not back on the held one.");
    }

    [TestMethod]
    public void ReplaceIsUnavailableWhenTheValueWasReadable()
    {
        Assert.IsFalse(Editor("notify").ReplaceUnrecognisedCommand.CanExecute(null));
        Assert.IsTrue(Editor("nope").ReplaceUnrecognisedCommand.CanExecute(null));
    }

    // ── Modification signalling ────────────────────────────────────────────────

    [TestMethod]
    public void ChangingTheSelection_MarksTheEditorModified()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("notify");

        Assert.IsTrue(
            CountIsModifiedFires(vm, () => Select(vm, OpenCodeAutoupdateMode.Automatic)) >= 1,
            "The host only writes when it sees PropertyChanged(IsModified), and the flag was "
            + "already true from the load — so an elided re-raise means the edit never saves.");
    }

    /// <remarks>
    /// The specific trap: moving between two values while <c>IsModified</c> is already
    /// <see langword="true"/>. <c>[ObservableProperty]</c> elides equal assignments, so without the
    /// explicit re-raise the Save button stays disabled on the second edit.
    /// </remarks>
    [TestMethod]
    public void ASecondEditAlsoFires()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("notify");

        Select(vm, OpenCodeAutoupdateMode.Automatic);
        Assert.IsTrue(CountIsModifiedFires(vm, () => Select(vm, OpenCodeAutoupdateMode.Disabled)) >= 1);
    }

    [TestMethod]
    public void AFreshEditorIsNotModifiedBeforeAnythingIsLoaded()
    {
        OpenCodeAutoupdateEditorViewModel vm =
            new(new TestSchema("autoupdate"), TestScope.Project);

        Assert.IsFalse(
            vm.IsModified,
            "The field initialiser for the selected option must not run through MarkModified, or "
            + "every editor claims an edit before the user arrives.");
        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void LoadingAnUndefinedKey_DoesNotMarkModified()
    {
        Assert.IsFalse(Editor(null, defined: false).IsModified);
    }

    [TestMethod]
    public void LoadingADefinedKey_MarksModified()
    {
        Assert.IsTrue(Editor("notify").IsModified);
    }

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedState()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("notify");
        Select(vm, OpenCodeAutoupdateMode.Automatic);
        Assert.AreEqual(true, vm.ToValue());

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual("notify", vm.ToValue());
        Assert.IsFalse(vm.IsModified);
    }

    /// <remarks>
    /// Reloading must not leave the previous load's state behind — the unrecognised flag in
    /// particular, which would otherwise keep a now-readable value permanently held verbatim.
    /// </remarks>
    [TestMethod]
    public void ReloadingOverAnUnrecognisedValue_ClearsTheHeldState()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor("Notify");
        Assert.IsTrue(vm.IsUnrecognised);

        vm.LoadFromValue(new TestValue("autoupdate").With(TestScope.Project, true), TestScope.Project);

        Assert.IsFalse(vm.IsUnrecognised);
        Assert.AreEqual(string.Empty, vm.UnrecognisedText);
        Assert.AreEqual(true, vm.ToValue());
    }

    [TestMethod]
    public void TheHelpTextTracksTheSelection()
    {
        OpenCodeAutoupdateEditorViewModel vm = Editor(null, defined: false);
        string notSet = vm.ModeHelp;

        Select(vm, OpenCodeAutoupdateMode.Automatic);

        Assert.AreNotEqual(
            notSet, vm.ModeHelp, "ModeHelp did not follow the picker, so it describes another state.");
    }
}
