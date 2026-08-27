using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Tooling;
using Bennewitz.Ninja.OpenCode.Sdk.Tooling;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The <c>formatter</c> and <c>lsp</c> editors: a shared four-state mode over two different
/// per-language shapes.
/// </summary>
/// <remarks>
/// Both editors are exercised against the shared mode contract, deliberately, because sharing the
/// implementation is only worth anything if both are held to the same behaviour.
/// </remarks>
[TestClass]
public sealed class OpenCodeToolingEditorViewModelTests
{
    private static OpenCodeFormatterEditorViewModel Formatter(string? json)
    {
        OpenCodeFormatterEditorViewModel vm = new(new TestSchema("formatter"), TestScope.Project);
        TestValue value = new("formatter");
        if (json is not null)
        {
            value = value.With(TestScope.Project, CurrencyText.Parse(json));
        }

        vm.LoadFromValue(value, TestScope.Project);
        return vm;
    }

    private static OpenCodeLspEditorViewModel Lsp(string? json)
    {
        OpenCodeLspEditorViewModel vm = new(new TestSchema("lsp"), TestScope.Project);
        TestValue value = new("lsp");
        if (json is not null)
        {
            value = value.With(TestScope.Project, CurrencyText.Parse(json));
        }

        vm.LoadFromValue(value, TestScope.Project);
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

    private static void AssertFormatterRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(Formatter(json).ToValue()),
            because);

    private static void AssertLspRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(Lsp(json).ToValue()),
            because);

    // ── The shared mode, held against both editors ────────────────────────────

    [TestMethod]
    public void EveryModeRoundTripsThroughBothEditors()
    {
        foreach (string json in new[] { "false", "true", "{}" })
        {
            AssertFormatterRoundTrips(
                json, $"Opening `formatter: {json}` and saving it untouched changed it.");
            AssertLspRoundTrips(json, $"Opening `lsp: {json}` and saving it untouched changed it.");
        }
    }

    [TestMethod]
    public void AnAbsentKeyIsNotSet_AndWritesNothing()
    {
        OpenCodeFormatterEditorViewModel formatter = Formatter(null);
        Assert.AreEqual(OpenCodeToolingMode.NotSet, formatter.Mode);
        Assert.IsFalse(formatter.IsConfigured);
        Assert.IsNull(formatter.ToValue());

        OpenCodeLspEditorViewModel lsp = Lsp(null);
        Assert.AreEqual(OpenCodeToolingMode.NotSet, lsp.Mode);
        Assert.IsNull(lsp.ToValue());
    }

    /// <remarks>
    /// ⚠ The whole reason the mode is a four-state picker rather than a checkbox. Selecting
    /// "configured" from "not set" turns the subsystem ON — absent means off — so writing
    /// <see langword="null"/> here would make the control do nothing while claiming otherwise.
    /// </remarks>
    [TestMethod]
    public void SelectingConfiguredFromNotSet_WritesAnEmptyObject_NotNull()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter(null);

        vm.SelectedMode = OpenCodeToolingEditorViewModel.ModeOptions
            .Single(o => o.Mode == OpenCodeToolingMode.Configured);

        Assert.IsTrue(vm.IsConfigured);
        Assert.AreEqual("{}", CurrencyText.Render(vm.ToValue()));
    }

    /// <remarks>
    /// The preserve-the-other-arm rule, fourth appearance in this phase. A user who flips to
    /// "built-ins only" to test something must get their overrides back when they flip back.
    /// </remarks>
    [TestMethod]
    public void SwitchingAwayFromConfiguredAndBack_KeepsTheEntries()
    {
        OpenCodeFormatterEditorViewModel vm =
            Formatter("""{"prettier":{"command":["prettier","--write"]}}""");

        vm.SelectedMode = OpenCodeToolingEditorViewModel.ModeOptions
            .Single(o => o.Mode == OpenCodeToolingMode.BuiltIns);
        Assert.AreEqual(true, vm.ToValue());
        Assert.AreEqual(1, vm.Entries.Count, "The overrides were destroyed by the mode switch.");

        vm.SelectedMode = OpenCodeToolingEditorViewModel.ModeOptions
            .Single(o => o.Mode == OpenCodeToolingMode.Configured);

        Assert.AreEqual(
            """{prettier:{command:[prettier,--write]}}""",
            CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void AnUnrecognisedValue_IsHeldVerbatimAndSaidSo()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter(""""  "prettier"  """");

        Assert.IsTrue(vm.IsUnrecognised);
        Assert.AreEqual(OpenCodeToolingMode.Unrecognised, vm.Mode);
        Assert.IsFalse(vm.IsConfigured, "The entry list must not be offered for a value that is "
            + "not a map.");
        Assert.AreEqual("prettier", vm.ToValue());
        Assert.IsTrue(
            vm.UnrecognisedText.Contains("prettier", StringComparison.Ordinal),
            "The held value must be visible, or the user cannot see what they would be replacing.");
    }

    /// <remarks>
    /// Replacing is destructive, so it is a command rather than a consequence of touching the
    /// picker — and afterwards the editor is in a normal, editable state.
    /// </remarks>
    [TestMethod]
    public void ReplacingAnUnrecognisedValue_LeavesTheHeldStateForAnEditableOne()
    {
        OpenCodeLspEditorViewModel vm = Lsp("42");
        Assert.IsTrue(vm.IsUnrecognised);

        vm.ReplaceUnrecognisedCommand.Execute(null);

        Assert.IsFalse(vm.IsUnrecognised);
        Assert.AreEqual(OpenCodeToolingMode.Configured, vm.Mode);
        Assert.AreEqual(string.Empty, vm.UnrecognisedText);
        Assert.AreEqual("{}", CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void ReplaceIsUnavailableWhenTheValueWasReadable()
    {
        Assert.IsFalse(Formatter("true").ReplaceUnrecognisedCommand.CanExecute(null));
        Assert.IsTrue(Formatter(""""  "nope"  """").ReplaceUnrecognisedCommand.CanExecute(null));
    }

    [TestMethod]
    public void ChangingTheMode_MarksTheEditorModified()
    {
        OpenCodeLspEditorViewModel vm = Lsp("true");

        int fired = CountIsModifiedFires(
            vm,
            () => vm.SelectedMode = OpenCodeToolingEditorViewModel.ModeOptions
                .Single(o => o.Mode == OpenCodeToolingMode.Disabled));

        Assert.IsTrue(fired >= 1);
    }

    /// <remarks>
    /// ⚠ Guards a real defect the first draft had: the base class seeded <c>SelectedMode</c> by
    /// ASSIGNING THE PROPERTY in its constructor, whose generated setter calls <c>MarkModified</c>
    /// and through it the abstract <c>RefreshDerived</c> — a virtual call into a derived editor
    /// whose collections did not exist yet, which also left every freshly-built editor claiming to
    /// be modified. Seeded by a field initialiser instead.
    /// </remarks>
    [TestMethod]
    public void AFreshEditorIsNotModifiedBeforeAnythingIsLoaded()
    {
        OpenCodeFormatterEditorViewModel formatter =
            new(new TestSchema("formatter"), TestScope.Project);
        OpenCodeLspEditorViewModel lsp = new(new TestSchema("lsp"), TestScope.Project);

        Assert.IsFalse(formatter.IsModified);
        Assert.IsFalse(lsp.IsModified);
        Assert.AreEqual(OpenCodeToolingMode.NotSet, formatter.Mode);
        Assert.AreEqual(OpenCodeToolingMode.NotSet, lsp.Mode);
    }

    [TestMethod]
    public void LoadingAnUndefinedKey_DoesNotMarkModified()
    {
        Assert.IsFalse(Formatter(null).IsModified);
        Assert.IsFalse(Lsp(null).IsModified);
    }

    // ── formatter ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AFullFormatterEntryRoundTrips()
    {
        AssertFormatterRoundTrips(
            """
            {"prettier":{"disabled":false,"command":["prettier","--write"],
             "environment":{"NODE_ENV":"production"},"extensions":[".ts"]}}
            """,
            "Opening a config and saving it untouched must not change it.");
    }

    [TestMethod]
    public void EditingAFormatterEntryAfterLoad_FiresIsModified()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{}}""");

        Assert.IsTrue(CountIsModifiedFires(vm, () => vm.Entries[0].Disabled = true) >= 1);
    }

    [TestMethod]
    public void RemovingAFormatterEntryAfterLoad_FiresIsModified()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{},"gofmt":{}}""");

        Assert.IsTrue(
            CountIsModifiedFires(vm, () => vm.Entries[0].RemoveCommand.Execute(null)) >= 1);
        Assert.AreEqual(1, vm.Entries.Count);
    }

    /// <remarks>
    /// ⚠ The trap this phase keeps re-meeting: the load path fills the nested collections BEFORE
    /// adding the entry, and under the loading guard, so <c>CollectionChanged</c> never hooked those
    /// rows. Hooking only future additions leaves every loaded row silent and Save disabled.
    /// </remarks>
    [TestMethod]
    public void EditingANestedCommandRowAfterLoad_FiresIsModified()
    {
        OpenCodeFormatterEditorViewModel vm =
            Formatter("""{"prettier":{"command":["prettier"]}}""");

        Assert.IsTrue(
            CountIsModifiedFires(vm, () => vm.Entries[0].Command.Rows[0].Value = "prettierd") >= 1);
        Assert.AreEqual(
            """{prettier:{command:[prettierd]}}""", CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void EditingANestedEnvironmentRowAfterLoad_FiresIsModified()
    {
        OpenCodeFormatterEditorViewModel vm =
            Formatter("""{"prettier":{"environment":{"A":"b"}}}""");

        Assert.IsTrue(
            CountIsModifiedFires(vm, () => vm.Entries[0].Environment.Rows[0].Value = "c") >= 1);
    }

    [TestMethod]
    public void TypingInTheAddBox_DoesNotMarkTheEditorModified()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{}}""");

        Assert.AreEqual(0, CountIsModifiedFires(vm, () => vm.NewEntryName = "gofmt"));
    }

    [TestMethod]
    public void OneEditDoesNotCascadeThroughTheRecount()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Entries[0].Disabled = true);

        Assert.IsTrue(fired is >= 1 and <= 4, $"Expected a small bounded number of fires, got {fired}.");
    }

    /// <remarks>
    /// ⭐⭐ <b>The test the first draft was missing, and the reason it was missing is the finding.</b>
    /// A canary that removed the load path's explicit subscribe loop broke <b>nothing</b> — because
    /// <c>CollectionChanged</c> was still attached during the rebuild and subscribed every row on
    /// the way in, so each row ended up with TWO handlers and the loop was decorative. Both halves
    /// read as correct in isolation, and the bounded-cascade test above tolerates the doubling.
    /// This asserts the property that actually distinguishes them: reloading must not accumulate
    /// handlers, which is only true if exactly one subscribe happens per row per load.
    /// </remarks>
    [TestMethod]
    public void ReloadingTheFormatterEditorDoesNotAccumulateHandlers()
    {
        OpenCodeFormatterEditorViewModel vm =
            new(new TestSchema("formatter"), TestScope.Project);
        TestValue value = new TestValue("formatter").With(
            TestScope.Project, CurrencyText.Parse("""{"prettier":{"command":["prettier"]}}"""));

        AssertReloadDoesNotAccumulateHandlers(
            vm, value, text => vm.Entries[0].Command.Rows[0].Value = text, "formatter");
    }

    [TestMethod]
    public void ReloadingTheLspEditorDoesNotAccumulateHandlers()
    {
        OpenCodeLspEditorViewModel vm = new(new TestSchema("lsp"), TestScope.Project);
        TestValue value = new TestValue("lsp").With(
            TestScope.Project, CurrencyText.Parse("""{"gopls":{"command":["gopls"]}}"""));

        AssertReloadDoesNotAccumulateHandlers(
            vm, value, text => vm.Entries[0].Command.Rows[0].Value = text, "lsp");
    }

    private static void AssertReloadDoesNotAccumulateHandlers(
        PropertyEditorViewModel vm,
        TestValue value,
        Action<string> editNestedRow,
        string label)
    {
        vm.LoadFromValue(value, TestScope.Project);
        int afterOneLoad = CountIsModifiedFires(vm, () => editNestedRow("first"));

        vm.LoadFromValue(value, TestScope.Project);
        vm.LoadFromValue(value, TestScope.Project);
        int afterThreeLoads = CountIsModifiedFires(vm, () => editNestedRow("second"));

        Assert.AreEqual(
            afterOneLoad,
            afterThreeLoads,
            $"({label}) Handlers accumulated across reloads: one keystroke fired {afterOneLoad} "
            + $"time(s) after one load and {afterThreeLoads} after three. Asymmetric "
            + "subscribe/unsubscribe is invisible until it is a performance bug.");
        Assert.AreEqual(
            1,
            afterOneLoad,
            $"({label}) One nested keystroke should mark the editor modified exactly once; more "
            + "means a row carries duplicate handlers.");
    }

    /// <remarks>
    /// Adding an override is only meaningful in the configured mode, so the add path selects it
    /// rather than accepting a row that <c>ToValue</c> would then not write.
    /// </remarks>
    [TestMethod]
    public void AddingAnEntryFromAnotherMode_SwitchesToConfigured()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("false");
        Assert.AreEqual(OpenCodeToolingMode.Disabled, vm.Mode);

        vm.NewEntryName = "prettier";
        vm.AddEntryCommand.Execute(null);

        Assert.AreEqual(OpenCodeToolingMode.Configured, vm.Mode);
        Assert.AreEqual("{prettier:{}}", CurrencyText.Render(vm.ToValue()));
    }

    /// <remarks>
    /// ⚠⚠ <b>The three-state checkbox earns its awkwardness here.</b> A two-state box would omit
    /// the key when unticked, silently deleting an explicit <c>"disabled": false</c> the first time
    /// the page was saved — and <c>false</c> versus absent is exactly the distinction this editor
    /// exists to keep.
    /// </remarks>
    [TestMethod]
    public void DisabledHasThreeDistinctWrittenForms()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{}}""");

        vm.Entries[0].Disabled = true;
        Assert.AreEqual("{prettier:{disabled:true}}", CurrencyText.Render(vm.ToValue()));

        vm.Entries[0].Disabled = false;
        Assert.AreEqual("{prettier:{disabled:false}}", CurrencyText.Render(vm.ToValue()));

        vm.Entries[0].Disabled = null;
        Assert.AreEqual(
            "{prettier:{}}",
            CurrencyText.Render(vm.ToValue()),
            "The indeterminate state must omit the key, not write one of the booleans.");
    }

    [TestMethod]
    public void AnOpaqueFormatterEntryIsCountedAndItsNeighbourStaysEditable()
    {
        OpenCodeFormatterEditorViewModel vm =
            Formatter("""{"weird":"prettier","gofmt":{"disabled":true}}""");

        Assert.AreEqual(1, vm.OpaqueEntryCount);
        Assert.IsTrue(vm.HasOpaqueEntries);
        Assert.IsFalse(vm.Entries[0].IsEditable);
        Assert.IsTrue(vm.Entries[1].IsEditable);

        AssertFormatterRoundTrips(
            """{"weird":"prettier","gofmt":{"disabled":true}}""",
            "One unreadable entry must not freeze its neighbours.");
    }

    [TestMethod]
    public void UnsurfacedFieldsArePreservedAndCounted()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"p":{"future":1,"disabled":true}}""");

        Assert.AreEqual(1, vm.EntriesWithExtrasCount);
        Assert.IsTrue(vm.Entries[0].HasExtras);
        Assert.AreEqual(1, vm.Entries[0].ExtraCount);
        Assert.AreEqual(
            "{p:{disabled:true,future:1}}",
            CurrencyText.Render(vm.ToValue()),
            "The unsurfaced field was dropped on save.");
    }

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedState()
    {
        OpenCodeFormatterEditorViewModel vm = Formatter("""{"prettier":{}}""");

        vm.NewEntryName = "gofmt";
        vm.AddEntryCommand.Execute(null);
        Assert.AreEqual(2, vm.Entries.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Entries.Count);
        Assert.AreEqual("prettier", vm.Entries[0].Name);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void HasEntries_TracksTheList()
    {
        OpenCodeFormatterEditorViewModel empty = Formatter("{}");
        Assert.IsFalse(empty.HasEntries);

        empty.NewEntryName = "prettier";
        empty.AddEntryCommand.Execute(null);
        Assert.IsTrue(empty.HasEntries);
    }

    // ── lsp: the two arms ─────────────────────────────────────────────────────

    [TestMethod]
    public void ADisableOnlyLspEntryRoundTrips()
    {
        AssertLspRoundTrips(
            """{"gopls":{"disabled":true}}""",
            "The commonest lsp entry there is — turning off a built-in server — must survive.");
    }

    [TestMethod]
    public void AFullLspEntryRoundTrips()
    {
        AssertLspRoundTrips(
            """
            {"pyright":{"command":["pyright-langserver","--stdio"],"extensions":[".py"],
             "disabled":false,"env":{"PYTHONPATH":"/src"},
             "initialization":{"settings":{"strict":true}}}}
            """,
            "Opening a config and saving it untouched must not change it.");
    }

    /// <remarks>
    /// ⚠⚠ <b>The headline behaviour of this editor.</b> Unticking "disabled" on a disable-only
    /// server produces <c>{ "disabled": false }</c>, which matches neither arm — the first needs the
    /// literal <c>true</c>, the second needs a command. A generic object editor renders that
    /// identically to the valid form and says nothing.
    /// </remarks>
    [TestMethod]
    public void UntickingDisabledOnADisableOnlyEntry_IsReportedAsIncomplete()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"gopls":{"disabled":true}}""");
        Assert.AreEqual(0, vm.IncompleteEntryCount);
        Assert.IsFalse(vm.Entries[0].NeedsCommand);

        vm.Entries[0].Disabled = false;

        Assert.IsTrue(
            vm.Entries[0].NeedsCommand,
            "The entry is now invalid and the row says nothing, so the user learns from a rejected "
            + "config instead of from the editor.");
        Assert.AreEqual(1, vm.IncompleteEntryCount);
        Assert.IsTrue(vm.HasIncompleteEntries);
    }

    /// <remarks>
    /// ⚠ A count computed on load only would be stale the instant the user made one of these — and
    /// the edit that creates one is a single click.
    /// </remarks>
    [TestMethod]
    public void TheIncompleteCountIsRecomputedOnEveryChange_NotOnLoadOnly()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"gopls":{"disabled":false}}""");
        Assert.AreEqual(1, vm.IncompleteEntryCount);

        vm.Entries[0].Command.NewValue = "gopls";
        vm.Entries[0].Command.AddCommand.Execute(null);

        Assert.AreEqual(
            0,
            vm.IncompleteEntryCount,
            "Adding the missing command fixed the entry but the banner still counts it.");
        Assert.IsFalse(vm.Entries[0].NeedsCommand);
    }

    [TestMethod]
    public void AnIncompleteEntryIsStillWritten()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"gopls":{"extensions":[".go"]}}""");

        Assert.IsTrue(vm.Entries[0].NeedsCommand);
        Assert.AreEqual(
            "{gopls:{extensions:[.go]}}",
            CurrencyText.Render(vm.ToValue()),
            "What the user typed was withheld, which is the worse of the two failures.");
    }

    [TestMethod]
    public void TheRowVerdictAndTheBannerCountAlwaysAgree()
    {
        OpenCodeLspEditorViewModel vm = Lsp(
            """{"a":{"disabled":false},"b":{"command":["b"]},"c":{"extensions":[".c"]}}""");

        Assert.AreEqual(
            vm.Entries.Count(e => e.NeedsCommand),
            vm.IncompleteEntryCount,
            "A row saying it is fine while the banner counts it is worse than either warning alone.");
        Assert.AreEqual(2, vm.IncompleteEntryCount);
    }

    /// <remarks>
    /// ⛔ The name guard at editor level. The plan lists both keys with the same field set; the
    /// schema spells this one <c>env</c> and forbids additional properties, so writing
    /// <c>environment</c> here produces a config OpenCode rejects.
    /// </remarks>
    [TestMethod]
    public void AnLspEnvironmentIsWrittenUnderEnv()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"gopls":{"command":["gopls"]}}""");

        vm.Entries[0].Env.NewKey = "GOFLAGS";
        vm.Entries[0].Env.NewValue = "-mod=mod";
        vm.Entries[0].Env.AddCommand.Execute(null);

        Assert.AreEqual(
            "{gopls:{command:[gopls],env:{GOFLAGS:-mod=mod}}}",
            CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void InitializationRoundTrips()
    {
        AssertLspRoundTrips(
            """{"g":{"command":["g"],"initialization":{"a":{"b":1}}}}""",
            "The initialization object is opaque but must survive verbatim.");
    }

    /// <remarks>
    /// ⚠ A JSON text box is unparseable most of the time it is in use. Treating each keystroke as
    /// "no initialization" would delete the user's configuration on a live-write host before they
    /// finished typing it. Same rule as the plugin editor's options box.
    /// </remarks>
    [TestMethod]
    public void UnparseableInitialization_KeepsTheLastGoodValue()
    {
        OpenCodeLspEditorViewModel vm =
            Lsp("""{"g":{"command":["g"],"initialization":{"a":1}}}""");

        vm.Entries[0].InitializationText = "{\"a\":";

        Assert.IsTrue(vm.Entries[0].HasInitializationError);
        Assert.AreEqual(1, vm.InitializationErrorCount);
        Assert.AreEqual(
            "{g:{command:[g],initialization:{a:1}}}",
            CurrencyText.Render(vm.ToValue()),
            "A half-typed edit deleted the user's initialization block.");
    }

    [TestMethod]
    public void ValidJsonThatIsNotAnObject_IsRejected()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"g":{"command":["g"]}}""");

        vm.Entries[0].HasInitialization = true;
        vm.Entries[0].InitializationText = "[1,2]";

        Assert.IsTrue(
            vm.Entries[0].HasInitializationError,
            "An array here would produce a config OpenCode rejects.");
    }

    [TestMethod]
    public void TogglingInitializationOffKeepsTheText()
    {
        OpenCodeLspEditorViewModel vm =
            Lsp("""{"g":{"command":["g"],"initialization":{"a":1}}}""");

        vm.Entries[0].HasInitialization = false;
        Assert.AreEqual("{g:{command:[g]}}", CurrencyText.Render(vm.ToValue()));

        vm.Entries[0].HasInitialization = true;
        Assert.AreEqual(
            "{g:{command:[g],initialization:{a:1}}}",
            CurrencyText.Render(vm.ToValue()),
            "Toggling the field back gave an empty object instead of what the user wrote.");
    }

    [TestMethod]
    public void BlankInitializationTextCountsAsAnEmptyObject()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"g":{"command":["g"]}}""");

        vm.Entries[0].HasInitialization = true;
        vm.Entries[0].InitializationText = string.Empty;

        Assert.IsFalse(vm.Entries[0].HasInitializationError);
        Assert.AreEqual(
            "{g:{command:[g],initialization:{}}}", CurrencyText.Render(vm.ToValue()));
    }

    [TestMethod]
    public void EditingANestedLspRowAfterLoad_FiresIsModified()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"g":{"command":["g"],"env":{"A":"b"}}}""");

        Assert.IsTrue(
            CountIsModifiedFires(vm, () => vm.Entries[0].Command.Rows[0].Value = "gopls") >= 1);
        Assert.IsTrue(CountIsModifiedFires(vm, () => vm.Entries[0].Env.Rows[0].Value = "c") >= 1);
    }

    /// <remarks>
    /// Deliberately unlike the formatter case: an empty <c>lsp</c> entry matches no arm, so writing
    /// one would persist a schema violation created by a half-finished click.
    /// </remarks>
    [TestMethod]
    public void AnEmptyLspEntryIsNotWritten()
    {
        OpenCodeLspEditorViewModel vm = Lsp("{}");

        vm.NewEntryName = "gopls";
        vm.AddEntryCommand.Execute(null);

        Assert.AreEqual(1, vm.Entries.Count, "The row must still be visible to the user.");
        Assert.AreEqual(
            "{}",
            CurrencyText.Render(vm.ToValue()),
            "An entry with nothing in it cannot be valid, so it is not written until it says "
            + "something.");
    }

    [TestMethod]
    public void OneLspEditDoesNotCascadeThroughTheRecount()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"g":{"command":["g"]}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Entries[0].Disabled = true);

        Assert.IsTrue(fired is >= 1 and <= 4, $"Expected a small bounded number of fires, got {fired}.");
    }

    [TestMethod]
    public void AnOpaqueLspEntryIsNotMistakenForAnEmptyOne()
    {
        OpenCodeLspEditorViewModel vm = Lsp("""{"weird":42,"gopls":{"disabled":true}}""");

        Assert.AreEqual(1, vm.OpaqueEntryCount);
        AssertLspRoundTrips(
            """{"weird":42,"gopls":{"disabled":true}}""",
            "An opaque entry read as empty would be silently deleted, because empty entries are "
            + "skipped on write.");
    }
}
