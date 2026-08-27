using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Keybinds;
using Bennewitz.Ninja.OpenCode.Sdk.Keybinds;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The <c>keybinds</c> editor: 184 actions, each a four-arm union nested three deep.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour worth guarding here is what only a cross-row view can produce — clash detection,
/// the filter, and file-order preservation — plus the four folds that would each silently rewrite a
/// user's file: <c>"x"</c> collapsing to <c>["x"]</c>, an untouched page writing 184 keys, an
/// unknown action vanishing, and a mode flip destroying the arm not being written.
/// </para>
/// <para>
/// ⚠ These tests use a small schema double rather than the real 184-action node, because the
/// contract is "one row per declared action" and asserting it over five names is the same assertion
/// over 184 with a readable failure message. The real schema's shape is pinned separately, in
/// <c>OpenCodeKeybindSchemaDriftTests</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeKeybindEditorViewModelTests
{
    /// <summary>
    /// A stand-in for the real node's 184 children, spanning both separators and the one name that
    /// has neither.
    /// </summary>
    private static TestSchema Schema() => new("keybinds")
    {
        Children =
        [
            TestSchema.Child("app_exit", "Exit the application"),
            TestSchema.Child("app_help", "Show help"),
            TestSchema.Child("editor_open", "Open the editor"),
            TestSchema.Child("dialog.select.prev", "Select the previous item"),
            TestSchema.Child("leader", "The leader key"),
        ],
    };

    private static OpenCodeKeybindEditorViewModel Editor(object? value, bool defined = true)
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);
        TestValue layered = new("keybinds");
        if (defined)
        {
            layered = layered.With(TestScope.Project, value);
        }

        vm.LoadFromValue(layered, TestScope.Project);
        return vm;
    }

    private static OpenCodeKeybindEditorViewModel EditorFromJson(string json) =>
        Editor(CurrencyText.Parse(json));

    private static OpenCodeKeybindActionViewModel Row(
        OpenCodeKeybindEditorViewModel vm, string action) =>
        vm.Actions.Single(a => a.Action == action);

    private static void Select(OpenCodeKeybindActionViewModel row, OpenCodeKeybindMode mode) =>
        row.SelectedMode =
            OpenCodeKeybindActionViewModel.ModeOptions.Single(o => o.Mode == mode);

    private static void SelectForm(
        OpenCodeKeyBindingViewModel binding, OpenCodeBindingForm form) =>
        binding.SelectedForm =
            OpenCodeKeyBindingViewModel.FormOptions.Single(o => o.Form == form);

    private static string Written(OpenCodeKeybindEditorViewModel vm) =>
        CurrencyText.Render(vm.ToValue());

    /// <summary>
    /// Assert that loading <paramref name="json"/> and saving it again yields the same value.
    /// </summary>
    /// <remarks>
    /// Compared through <see cref="CurrencyText.Render"/> at both ends, which preserves key order —
    /// a comparison that sorted keys would pass while the editor reshuffled a 184-key file.
    /// </remarks>
    private static void AssertRoundTrips(string json) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            Written(EditorFromJson(json)));

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

    // ── Rows come from the schema, not from the value ────────────────────────────────────────────

    [TestMethod]
    public void EveryDeclaredAction_GetsARow_EvenWhenTheFileSaysNothing()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        Assert.AreEqual(5, vm.Actions.Count);
        CollectionAssert.AreEqual(
            new[] { "app_exit", "app_help", "editor_open", "dialog.select.prev", "leader" },
            vm.Actions.Select(a => a.Action).ToArray());
    }

    [TestMethod]
    public void TheRowLabelIsTheSchemasOwnDescription()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        Assert.AreEqual("Exit the application", Row(vm, "app_exit").Label);
    }

    /// <summary>
    /// ⭐ The fold that would produce a 184-key diff from opening a page and pressing save.
    /// </summary>
    [TestMethod]
    public void AnUntouchedEditorWritesNothing()
    {
        Assert.IsNull(Editor(null, defined: false).ToValue());
    }

    /// <summary>
    /// ⚠ An empty <c>keybinds: {}</c> is not an absent <c>keybinds</c>. Neither changes a binding,
    /// so the only thing at stake is not rewriting the file the user opened.
    /// </summary>
    [TestMethod]
    public void AnEmptyObjectIsWrittenBack_ButAnAbsentKeyIsNot()
    {
        Assert.AreEqual("{}", Written(EditorFromJson("{}")));
        Assert.IsNull(Editor(null, defined: false).ToValue());
    }

    // ── The four-arm union, and the two folds that must never happen ─────────────────────────────

    [TestMethod]
    public void EachArmLoadsAsItsOwnMode()
    {
        Assert.AreEqual(
            OpenCodeKeybindMode.Disabled,
            Row(EditorFromJson("""{"app_exit": false}"""), "app_exit").Mode);
        Assert.AreEqual(
            OpenCodeKeybindMode.None,
            Row(EditorFromJson("""{"app_exit": "none"}"""), "app_exit").Mode);
        Assert.AreEqual(
            OpenCodeKeybindMode.Bound,
            Row(EditorFromJson("""{"app_exit": "ctrl+q"}"""), "app_exit").Mode);
        Assert.AreEqual(
            OpenCodeKeybindMode.Sequence,
            Row(EditorFromJson("""{"app_exit": ["ctrl+q"]}"""), "app_exit").Mode);
        Assert.AreEqual(
            OpenCodeKeybindMode.NotSet,
            Row(EditorFromJson("{}"), "app_exit").Mode);
    }

    /// <summary>
    /// ⚠⚠ <c>"x"</c> and <c>["x"]</c> are different files. A one-element array is arm 4, not arm 3,
    /// and collapsing it looks like tidying while silently moving the value to the other arm.
    /// </summary>
    [TestMethod]
    public void ASingleBindingAndAOneElementSequenceStayDifferentValues()
    {
        Assert.AreEqual("{app_exit:ctrl+q}", Written(EditorFromJson("""{"app_exit":"ctrl+q"}""")));
        Assert.AreEqual("{app_exit:[ctrl+q]}", Written(EditorFromJson("""{"app_exit":["ctrl+q"]}""")));
    }

    /// <summary>
    /// ⚠ <c>false</c> and <c>"none"</c> have the same effect and are not the same text. This phase
    /// never rewrites one spelling of a value into another.
    /// </summary>
    [TestMethod]
    public void DisabledAndNoneAreNotFoldedTogether()
    {
        Assert.AreEqual("{app_exit:false}", Written(EditorFromJson("""{"app_exit":false}""")));
        Assert.AreEqual("{app_exit:none}", Written(EditorFromJson("""{"app_exit":"none"}""")));
    }

    [TestMethod]
    public void TheStructuredKeyFormRoundTrips()
    {
        const string Json = """{"app_exit":{"name":"q","ctrl":true}}""";

        AssertRoundTrips(Json);
    }

    /// <summary>
    /// ⚠ A modifier stated as <c>false</c> and one left out are different files, so a two-state box
    /// would delete an explicit <c>"ctrl": false</c> on the first save.
    /// </summary>
    [TestMethod]
    public void AnExplicitlyFalseModifierSurvives_AndAnAbsentOneStaysAbsent()
    {
        const string Json = """{"app_exit":{"name":"q","ctrl":false}}""";

        AssertRoundTrips(Json);
        AssertRoundTrips("""{"app_exit":{"name":"q"}}""");
    }

    [TestMethod]
    public void TheEventFormRoundTripsWithItsDeliveryOptions()
    {
        const string Json =
            """{"app_exit":{"key":"ctrl+q","event":"release","preventDefault":true}}""";

        AssertRoundTrips(Json);
    }

    // ── Preservation: the arm not being written, and what the schema does not declare ────────────

    /// <summary>
    /// ⭐ The preserve-the-other-arm rule, which this phase has now needed five times. Flipping an
    /// action off and back on must not make the user retype the key.
    /// </summary>
    [TestMethod]
    public void FlippingAModeOffAndBackOnRestoresTheBinding()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Select(row, OpenCodeKeybindMode.Disabled);
        Assert.AreEqual("{app_exit:false}", Written(vm));

        Select(row, OpenCodeKeybindMode.Bound);
        Assert.AreEqual("{app_exit:ctrl+q}", Written(vm));
    }

    /// <summary>
    /// ⚠ <c>keybinds</c> sets <c>additionalProperties: false</c>, so an unknown action is a schema
    /// violation — which is exactly why it is preserved. A config from a newer OpenCode is the
    /// ordinary way to meet one, and an editor that drops what it has not heard of makes upgrading
    /// lossy.
    /// </summary>
    [TestMethod]
    public void AnUnknownActionGetsARowAndIsWrittenBack()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"future_action":"ctrl+z"}""");

        OpenCodeKeybindActionViewModel row = Row(vm, "future_action");
        Assert.IsFalse(row.IsKnown);
        Assert.AreEqual(1, vm.UnknownCount);
        Assert.IsTrue(vm.HasUnknown);
        Assert.AreEqual("{future_action:ctrl+z}", Written(vm));
    }

    /// <summary>
    /// A reload has to drop the rows only a previous file created, or the unknown actions of every
    /// file opened this session would accumulate in the list.
    /// </summary>
    [TestMethod]
    public void ReloadingDropsTheRowsOnlyAFileCreated()
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);

        vm.LoadFromValue(
            new TestValue("keybinds").With(
                TestScope.Project, CurrencyText.Parse("""{"future_action":"ctrl+z"}""")),
            TestScope.Project);
        Assert.AreEqual(6, vm.Actions.Count);

        vm.LoadFromValue(
            new TestValue("keybinds").With(TestScope.Project, CurrencyText.Parse("{}")),
            TestScope.Project);

        Assert.AreEqual(5, vm.Actions.Count);
        Assert.AreEqual(0, vm.UnknownCount);
    }

    /// <summary>
    /// ⚠ 184 keys is why file order is tracked. Reordering them turns a one-line change into a diff
    /// nobody can review.
    /// </summary>
    [TestMethod]
    public void ActionsTheFileStatedKeepTheirPlace_AndNewOnesFollowInSchemaOrder()
    {
        // The file states them in the reverse of schema order.
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"leader":"space","editor_open":"ctrl+o"}""");

        Select(Row(vm, "app_exit"), OpenCodeKeybindMode.None);

        Assert.AreEqual("{leader:space,editor_open:ctrl+o,app_exit:none}", Written(vm));
    }

    // ── The whole value, and one action, held verbatim ───────────────────────────────────────────

    [TestMethod]
    public void AWholeValueThatIsNotAnObjectIsHeldVerbatim()
    {
        OpenCodeKeybindEditorViewModel vm = Editor("nonsense");

        Assert.IsTrue(vm.IsUnrecognised);
        Assert.AreEqual("nonsense", vm.ToValue());
        Assert.IsTrue(vm.UnrecognisedText.Contains("nonsense", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplacingAHeldValueLeavesAnEditablePage()
    {
        OpenCodeKeybindEditorViewModel vm = Editor("nonsense");

        Assert.IsTrue(vm.ReplaceUnrecognisedCommand.CanExecute(null));
        vm.ReplaceUnrecognisedCommand.Execute(null);

        Assert.IsFalse(vm.IsUnrecognised);
        Assert.IsFalse(vm.ReplaceUnrecognisedCommand.CanExecute(null));
        // Nothing is set, but the key was present, so the empty object is what gets written.
        Assert.AreEqual("{}", Written(vm));
    }

    [TestMethod]
    public void AnActionWhoseValueMatchesNoArmIsHeldVerbatim()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":42}""");
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Assert.AreEqual(OpenCodeKeybindMode.Unrecognised, row.Mode);
        Assert.AreEqual("{app_exit:42}", Written(vm));
    }

    /// <summary>
    /// ⚠ Unrecognised is a state a value arrives in, never one a user picks — its raw may be
    /// <see langword="null"/>, which the value currency can only express as "remove the key".
    /// </summary>
    [TestMethod]
    public void UnrecognisedIsNotOfferedInEitherPicker()
    {
        Assert.IsFalse(
            OpenCodeKeybindActionViewModel.ModeOptions.Any(
                o => o.Mode == OpenCodeKeybindMode.Unrecognised));
        Assert.IsFalse(
            OpenCodeKeyBindingViewModel.FormOptions.Any(
                o => o.Form == OpenCodeBindingForm.Opaque));
    }

    /// <summary>
    /// ⚠ There is no "enabled" option: the schema's boolean arm is <c>enum: [false]</c>, so the
    /// literal <c>true</c> is not admitted anywhere in this union.
    /// </summary>
    [TestMethod]
    public void NoModeOptionWritesTheLiteralTrue()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        foreach (OpenCodeKeybindModeOption option in OpenCodeKeybindActionViewModel.ModeOptions)
        {
            row.SelectedMode = option;
            Assert.AreNotEqual(
                "{app_exit:true}",
                Written(vm),
                $"the '{option.Label}' option wrote a value the schema rejects");
        }
    }

    // ── Clash detection: the thing only a cross-row view can do ──────────────────────────────────

    [TestMethod]
    public void TwoActionsOnTheSameKeyNameEachOther()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson(
            """{"app_exit":{"name":"q","ctrl":true},"app_help":{"name":"q","ctrl":true}}""");

        Assert.AreEqual(2, vm.ConflictCount);
        Assert.IsTrue(vm.HasConflicts);
        Assert.AreEqual("app_help", Row(vm, "app_exit").ConflictWith);
        Assert.AreEqual("app_exit", Row(vm, "app_help").ConflictWith);
        Assert.IsTrue(Row(vm, "app_exit").HasConflict);
    }

    [TestMethod]
    public void TwoActionsOnTheSameChordTextNameEachOther()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":"ctrl+q","app_help":"ctrl+q"}""");

        Assert.AreEqual(2, vm.ConflictCount);
    }

    /// <summary>
    /// ⚠⚠ <c>"ctrl+q"</c> and <c>{"name":"q","ctrl":true}</c> almost certainly mean the same
    /// keystroke — and saying so would mean inventing the chord parser this editor deliberately
    /// refuses to invent. Like is compared with like.
    /// </summary>
    [TestMethod]
    public void AChordAndAStructuredKeyAreNeverReportedAsTheSameKey()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson(
            """{"app_exit":"ctrl+q","app_help":{"name":"q","ctrl":true}}""");

        Assert.AreEqual(0, vm.ConflictCount);
        Assert.AreEqual(string.Empty, Row(vm, "app_exit").ConflictWith);
    }

    /// <summary>
    /// An action may bind the same key twice in one sequence. That is odd, but it is not a clash
    /// BETWEEN actions, and reporting it as one would name the row against itself.
    /// </summary>
    [TestMethod]
    public void AnActionBindingOneKeyTwiceIsNotAClashWithItself()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":["ctrl+q","ctrl+q"]}""");

        Assert.AreEqual(0, vm.ConflictCount);
    }

    /// <summary>
    /// ⚠⚠ The single form writes exactly one binding, so a clash reported from the second would be
    /// a warning about text the file will not contain.
    /// </summary>
    [TestMethod]
    public void OnlyTheBindingsThatWillBeWrittenCanClash()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":["ctrl+q","ctrl+x"],"app_help":"ctrl+x"}""");

        Assert.AreEqual(2, vm.ConflictCount);

        // Moving app_exit to the single form leaves only ctrl+q written, so the clash is gone.
        Select(Row(vm, "app_exit"), OpenCodeKeybindMode.Bound);

        Assert.AreEqual(0, vm.ConflictCount);
        Assert.AreEqual("{app_exit:ctrl+q,app_help:ctrl+x}", Written(vm));
    }

    /// <summary>
    /// The event form's delivery options change WHEN the key fires, not WHICH key it is, so two
    /// actions on one key with different <c>event</c> values are still worth showing.
    /// </summary>
    [TestMethod]
    public void DifferentEventValuesOnOneKeyAreStillAClash()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson(
            """
            {"app_exit":{"key":{"name":"q"},"event":"press"},
             "app_help":{"key":{"name":"q"},"event":"release"}}
            """);

        Assert.AreEqual(2, vm.ConflictCount);
    }

    [TestMethod]
    public void AnIncompleteBindingDoesNotClashWithAnotherIncompleteOne()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":{"name":""},"app_help":{"name":""}}""");

        Assert.AreEqual(2, vm.IncompleteCount);
        Assert.AreEqual(0, vm.ConflictCount);
    }

    /// <summary>
    /// Clash detection has to follow an edit, or the banner goes stale the moment a key is typed —
    /// the same defect 9a-6's canary C3 found in the command editor's counts.
    /// </summary>
    [TestMethod]
    public void ClashesAreRecomputedAfterAnEdit()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":"ctrl+q","app_help":"ctrl+x"}""");
        Assert.AreEqual(0, vm.ConflictCount);

        Row(vm, "app_help").Bindings[0].Chord = "ctrl+q";

        Assert.AreEqual(2, vm.ConflictCount);
    }

    // ── The banners ─────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheCountsReportWhatTheFileStates()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson(
            """{"app_exit":"ctrl+q","app_help":{"name":""},"future_action":"ctrl+z"}""");

        Assert.AreEqual(3, vm.SetCount);
        Assert.AreEqual(1, vm.IncompleteCount);
        Assert.AreEqual(1, vm.UnknownCount);
        Assert.IsTrue(vm.HasSet);
        Assert.IsTrue(vm.HasIncomplete);
    }

    /// <summary>
    /// ⚠ An incomplete binding is reachable by one obvious click — clearing the key's name — and is
    /// reported rather than repaired. Inventing a name would claim a keystroke the user never made.
    /// </summary>
    [TestMethod]
    public void ClearingTheKeyNameReportsIncompleteAndWritesWhatTheUserTyped()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":{"name":"q","ctrl":true}}""");

        Row(vm, "app_exit").Bindings[0].KeyName = string.Empty;

        Assert.AreEqual(1, vm.IncompleteCount);
        Assert.AreEqual("{app_exit:{name:,ctrl:true}}", Written(vm));
    }

    // ── The filter, which is the primary control on a 184-row list ───────────────────────────────

    [TestMethod]
    public void TheFilterMatchesTheActionName()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        vm.FilterText = "app_";

        CollectionAssert.AreEqual(
            new[] { "app_exit", "app_help" },
            vm.FilteredActions.Select(a => a.Action).ToArray());
        Assert.AreEqual(3, vm.HiddenCount);
        Assert.IsTrue(vm.HasHidden);
    }

    [TestMethod]
    public void TheFilterMatchesTheSchemasDescription()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        vm.FilterText = "previous item";

        Assert.AreEqual(1, vm.FilteredActions.Count);
        Assert.AreEqual("dialog.select.prev", vm.FilteredActions[0].Action);
    }

    /// <summary>
    /// ⭐ The match that earns its keep: it is what answers "what is Ctrl+Q already bound to?",
    /// which a list of 184 action names cannot.
    /// </summary>
    [TestMethod]
    public void TheFilterMatchesTheCurrentBinding()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"editor_open":"ctrl+q"}""");

        vm.FilterText = "ctrl+q";

        Assert.AreEqual(1, vm.FilteredActions.Count);
        Assert.AreEqual("editor_open", vm.FilteredActions[0].Action);
    }

    [TestMethod]
    public void TheFilterIsCaseInsensitive()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        vm.FilterText = "APP_EXIT";

        Assert.AreEqual(1, vm.FilteredActions.Count);
    }

    /// <summary>
    /// ⚠ The 184 names use TWO separators. Splitting on <c>_</c> alone scatters the 16 dotted names
    /// into their own single-action groups.
    /// </summary>
    [TestMethod]
    public void GroupsComeFromBothSeparators_AndTheUnseparatedNameIsItsOwnGroup()
    {
        Assert.AreEqual("app", OpenCodeKeybindEditorViewModel.GroupOf("app_exit"));
        Assert.AreEqual("dialog", OpenCodeKeybindEditorViewModel.GroupOf("dialog.select.prev"));
        Assert.AreEqual("leader", OpenCodeKeybindEditorViewModel.GroupOf("leader"));
        Assert.AreEqual(string.Empty, OpenCodeKeybindEditorViewModel.GroupOf(string.Empty));
    }

    [TestMethod]
    public void TheGroupPickerOffersEveryGroupPlusTheAllPlaceholder()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        Assert.AreEqual(OpenCodeKeybindEditorViewModel.AllGroups, vm.Groups[0]);
        CollectionAssert.AreEquivalent(
            new[] { OpenCodeKeybindEditorViewModel.AllGroups, "app", "dialog", "editor", "leader" },
            vm.Groups.ToArray());
    }

    [TestMethod]
    public void TheGroupPickerNarrowsTheList()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        vm.SelectedGroup = "app";

        Assert.AreEqual(2, vm.FilteredActions.Count);
    }

    /// <summary>
    /// ⭐ The filter people want first: 184 rows of "not set" is what a fresh config looks like.
    /// </summary>
    [TestMethod]
    public void OnlyShowSetHidesEveryActionTheFileSaysNothingAbout()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");

        vm.OnlyShowSet = true;

        Assert.AreEqual(1, vm.FilteredActions.Count);
        Assert.AreEqual("app_exit", vm.FilteredActions[0].Action);
    }

    /// <summary>
    /// The filter reads <c>Summary</c> and <c>IsSet</c>, both of which an edit can change — so a row
    /// the user has just unset has to leave an only-set list.
    /// </summary>
    [TestMethod]
    public void TheFilteredListFollowsAnEdit()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        vm.OnlyShowSet = true;
        Assert.AreEqual(1, vm.FilteredActions.Count);

        Select(Row(vm, "app_exit"), OpenCodeKeybindMode.NotSet);

        Assert.AreEqual(0, vm.FilteredActions.Count);
    }

    [TestMethod]
    public void ClearingTheFilterRestoresTheWholeList()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        vm.FilterText = "nothing matches this";
        vm.SelectedGroup = "app";
        vm.OnlyShowSet = true;

        vm.ClearFilterCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.FilterText);
        Assert.AreEqual(OpenCodeKeybindEditorViewModel.AllGroups, vm.SelectedGroup);
        Assert.IsFalse(vm.OnlyShowSet);
        Assert.AreEqual(5, vm.FilteredActions.Count);
        Assert.AreEqual(0, vm.HiddenCount);
    }

    // ── The row's own controls ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Choosing a bound mode seeds one empty binding, so the choice produces something to type
    /// into.
    /// </summary>
    [TestMethod]
    public void ChoosingABoundModeSeedsOneEmptyBinding()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Select(row, OpenCodeKeybindMode.Bound);

        Assert.AreEqual(1, row.Bindings.Count);
        Assert.IsTrue(row.IsIncomplete, "the seeded binding names no key yet");
    }

    /// <summary>
    /// ⚠ The load path must NOT seed: assigning the mode runs the same handler, and without its own
    /// guard every bound action would create and discard a row.
    /// </summary>
    [TestMethod]
    public void LoadingABoundActionDoesNotSeedAnExtraBinding()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");

        Assert.AreEqual(1, Row(vm, "app_exit").Bindings.Count);
    }

    [TestMethod]
    public void AddIsOfferedForASequenceAndNotForTheSingleForm()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Select(row, OpenCodeKeybindMode.Bound);
        Assert.IsFalse(row.AddBindingCommand.CanExecute(null));

        Select(row, OpenCodeKeybindMode.Sequence);
        Assert.IsTrue(row.AddBindingCommand.CanExecute(null));

        row.AddBindingCommand.Execute(null);
        Assert.AreEqual(2, row.Bindings.Count);
    }

    [TestMethod]
    public void ASequencesBindingsCanBeReorderedAndRemoved()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":["a","b","c"]}""");
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        row.Bindings[1].MoveUpCommand.Execute(null);
        Assert.AreEqual("{app_exit:[b,a,c]}", Written(vm));

        row.Bindings[2].RemoveCommand.Execute(null);
        Assert.AreEqual("{app_exit:[b,a]}", Written(vm));
    }

    /// <summary>
    /// Reorder <c>CanExecute</c> depends on the row's index, which nothing raises a change for — so
    /// the owner has to refresh every row after any structural change.
    /// </summary>
    [TestMethod]
    public void TheEndsOfASequenceCannotMovePastThem()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":["a","b"]}""");
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Assert.IsFalse(row.Bindings[0].CanMoveUp);
        Assert.IsTrue(row.Bindings[0].CanMoveDown);
        Assert.IsTrue(row.Bindings[1].CanMoveUp);
        Assert.IsFalse(row.Bindings[1].CanMoveDown);

        row.Bindings[0].MoveDownCommand.Execute(null);

        Assert.IsTrue(row.Bindings[0].CanMoveDown, "the row that moved into first place");
        Assert.IsFalse(row.Bindings[0].CanMoveUp);
    }

    [TestMethod]
    public void ReorderingIsNotOfferedForTheSingleForm()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.IsFalse(binding.IsInSequence);
        Assert.IsFalse(binding.CanMoveUp);
        Assert.IsFalse(binding.CanMoveDown);
        Assert.IsFalse(binding.RemoveCommand.CanExecute(null));
    }

    /// <summary>
    /// ⚠⚠ Extra bindings from a previous sequence stay in the list — trimming them would destroy
    /// them on a mode flip the user may be about to undo — but only the first is written.
    /// </summary>
    [TestMethod]
    public void TheSingleFormWritesOneBindingAndKeepsTheRest()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":["a","b"]}""");
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");

        Select(row, OpenCodeKeybindMode.Bound);

        Assert.AreEqual("{app_exit:a}", Written(vm));
        Assert.AreEqual(2, row.Bindings.Count, "the second binding is kept, just not written");

        Select(row, OpenCodeKeybindMode.Sequence);
        Assert.AreEqual("{app_exit:[a,b]}", Written(vm));
    }

    // ── The form picker, and the key control that serves two places ──────────────────────────────

    [TestMethod]
    public void ChoosingTheKeyFormImpliesTheStructuredKey()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.IsFalse(binding.UsesStructuredKey);
        SelectForm(binding, OpenCodeBindingForm.Key);
        Assert.IsTrue(binding.UsesStructuredKey);

        SelectForm(binding, OpenCodeBindingForm.Chord);
        Assert.IsFalse(binding.UsesStructuredKey);
    }

    /// <summary>
    /// ⭐ The event form's <c>key</c> is itself <c>string | key-object</c>, so one key control has to
    /// render in both places.
    /// </summary>
    [TestMethod]
    public void TheEventFormAcceptsEitherKeyShape()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson(
            """{"app_exit":{"key":{"name":"q"},"event":"press"}}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.IsTrue(binding.ShowsEventFields);
        Assert.IsTrue(binding.ShowsKeyFields);
        Assert.IsFalse(binding.ShowsChordField);

        binding.UsesStructuredKey = false;
        binding.Chord = "ctrl+q";

        Assert.IsTrue(binding.ShowsChordField);
        Assert.AreEqual("{app_exit:{key:ctrl+q,event:press}}", Written(vm));
    }

    /// <summary>
    /// The chord text survives a switch to the key form and back — the same
    /// preserve-the-other-arm rule, one level down.
    /// </summary>
    [TestMethod]
    public void SwitchingFormAndBackRestoresTheChordText()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        SelectForm(binding, OpenCodeBindingForm.Key);
        binding.KeyName = "q";
        SelectForm(binding, OpenCodeBindingForm.Chord);

        Assert.AreEqual("{app_exit:ctrl+q}", Written(vm));
    }

    [TestMethod]
    public void AnOpaqueBindingIsHeldAndCanBeReplaced()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":[42]}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.IsTrue(binding.IsOpaque);
        Assert.IsFalse(binding.IsEditable);
        Assert.IsFalse(binding.StartCaptureCommand.CanExecute(null));
        Assert.AreEqual("{app_exit:[42]}", Written(vm));

        binding.ReplaceOpaqueCommand.Execute(null);

        Assert.IsFalse(binding.IsOpaque);
        Assert.IsTrue(binding.IsEditable);

        // ⚠ Asserted as state, not as rendered text: the canonical renderer prints strings
        // unquoted, so an empty chord and an empty sequence render identically. Replacing a held
        // value leaves an editable-but-incomplete binding, which is the honest result — inventing a
        // key would claim a keystroke the user never made.
        Assert.AreEqual(OpenCodeBindingForm.Chord, binding.Form);
        Assert.AreEqual(string.Empty, binding.Chord);
        Assert.IsTrue(binding.IsIncomplete);
        Assert.AreEqual(1, vm.IncompleteCount);
    }

    // ── Capture ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Only modifiers actually held are written. Writing <c>"ctrl": false</c> for every unheld
    /// modifier would turn one keystroke into five keys of noise and would claim the user made a
    /// decision about Hyper that they did not.
    /// </summary>
    [TestMethod]
    public void CaptureWritesOnlyTheModifiersThatWereHeld()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);
        OpenCodeKeybindActionViewModel row = Row(vm, "app_exit");
        Select(row, OpenCodeKeybindMode.Bound);
        OpenCodeKeyBindingViewModel binding = row.Bindings[0];

        // ⚠ A MIX is required, and every modifier needs a case where it is UNHELD — a canary proved
        // why twice over. With a modifier held, "held → true" and "held → the bool itself" produce
        // identical output, so a case that holds Ctrl cannot see a Ctrl regression at all. Ctrl and
        // Super are deliberately unheld here.
        binding.ApplyCapture(
            new CapturedKey("q", Ctrl: false, Shift: true, Meta: true, Super: false));

        Assert.AreEqual("{app_exit:{name:q,shift:true,meta:true}}", Written(vm));
        Assert.IsNull(binding.Ctrl, "an unheld modifier is absent, not present-and-false");
        Assert.IsNull(binding.Super);
        Assert.IsNull(binding.Hyper, "hyper is never captured — no platform reports it");
    }

    [TestMethod]
    public void CaptureSwitchesTheChordFormToTheKeyForm_AndKeepsTheChordText()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"typed by hand"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        binding.ApplyCapture(
            new CapturedKey("q", Ctrl: false, Shift: false, Meta: false, Super: false));

        Assert.AreEqual(OpenCodeBindingForm.Key, binding.Form);
        Assert.AreEqual("{app_exit:{name:q}}", Written(vm));

        // The other arm was preserved, so switching back restores what the user typed.
        SelectForm(binding, OpenCodeBindingForm.Chord);
        Assert.AreEqual("{app_exit:typed by hand}", Written(vm));
    }

    [TestMethod]
    public void CaptureIsAnExplicitModeWithAWayOut()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.IsFalse(binding.IsCapturing);
        Assert.IsFalse(binding.CancelCaptureCommand.CanExecute(null));

        binding.StartCaptureCommand.Execute(null);
        Assert.IsTrue(binding.IsCapturing);
        Assert.IsTrue(binding.CancelCaptureCommand.CanExecute(null));

        binding.CancelCaptureCommand.Execute(null);
        Assert.IsFalse(binding.IsCapturing);
        Assert.AreEqual("{app_exit:ctrl+q}", Written(vm), "cancelling changed nothing");
    }

    /// <summary>
    /// ⚠⚠ One gesture, one report — even though capture writes six fields.
    /// </summary>
    /// <remarks>
    /// Each report runs a full clash recompute over every row plus a filter pass that resets the
    /// collection the virtualizing list is bound to, so six reports rebuild the visible list six
    /// times for one keystroke. Guards the gesture wrapper; without it this is 6.
    /// </remarks>
    [TestMethod]
    public void CaptureReportsOneChange_NotOnePerFieldItWrites()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.AreEqual(
            1,
            CountIsModifiedFires(
                vm,
                () => binding.ApplyCapture(
                    new CapturedKey("x", Ctrl: true, Shift: true, Meta: true, Super: true))));
    }

    /// <summary>
    /// Switching form writes <c>UsesStructuredKey</c> as well as the form itself, so it is a gesture
    /// too.
    /// </summary>
    [TestMethod]
    public void SwitchingFormReportsOneChange()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.AreEqual(
            1,
            CountIsModifiedFires(vm, () => SelectForm(binding, OpenCodeBindingForm.Key)));
    }

    /// <summary>
    /// ⚠ Opening the capture control changes no value, so it must not make the page look edited.
    /// </summary>
    [TestMethod]
    public void StartingAndCancellingCaptureDoesNotMarkTheEditorModified()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];

        Assert.AreEqual(
            0,
            CountIsModifiedFires(
                vm,
                () =>
                {
                    binding.StartCaptureCommand.Execute(null);
                    binding.CancelCaptureCommand.Execute(null);
                }));
    }

    [TestMethod]
    public void CaptureLeavesTheWaitingState()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        OpenCodeKeyBindingViewModel binding = Row(vm, "app_exit").Bindings[0];
        binding.StartCaptureCommand.Execute(null);

        binding.ApplyCapture(
            new CapturedKey("q", Ctrl: true, Shift: false, Meta: false, Super: false));

        Assert.IsFalse(binding.IsCapturing);
    }

    // ── The modified signal ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ATypedKeyMarksTheEditorModified()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");

        Assert.AreEqual(
            1,
            CountIsModifiedFires(vm, () => Row(vm, "app_exit").Bindings[0].Chord = "ctrl+x"));
    }

    [TestMethod]
    public void AModeChangeMarksTheEditorModified()
    {
        OpenCodeKeybindEditorViewModel vm = Editor(null, defined: false);

        Assert.AreEqual(
            1,
            CountIsModifiedFires(vm, () => Select(Row(vm, "app_exit"), OpenCodeKeybindMode.None)));
    }

    [TestMethod]
    public void LoadingDoesNotMarkTheEditorModified()
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);

        vm.LoadFromValue(new TestValue("keybinds"), TestScope.Project);

        Assert.IsFalse(vm.IsModified);
    }

    /// <summary>
    /// ⚠⚠ <b>The exact <c>== 1</c> is the point.</b> The <c>_isLoading</c> guard suppresses
    /// <c>MarkModified</c> but not the subscription, so a reload that hooked each row again would
    /// fire twice per keystroke — and a bounded <c>1..4</c> assertion tolerates exactly that. This
    /// is the guard 9a-8's canary T5 showed was missing in three editors.
    /// </summary>
    [TestMethod]
    public void ReloadingDoesNotAccumulateRowSubscriptions()
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);
        TestValue layered = new TestValue("keybinds").With(
            TestScope.Project, CurrencyText.Parse("""{"app_exit":"ctrl+q"}"""));

        for (int i = 0; i < 4; i++)
        {
            vm.LoadFromValue(layered, TestScope.Project);
        }

        Assert.AreEqual(
            1,
            CountIsModifiedFires(vm, () => Row(vm, "app_exit").Bindings[0].Chord = "ctrl+x"),
            "one keystroke must raise IsModified exactly once, however many times the page loaded");
    }

    /// <summary>
    /// ⚠ The derived-property filter is load-bearing: <c>RefreshDerived</c> writes
    /// <c>ConflictWith</c> on every row, so reacting to that change is
    /// mark → recompute → row changed → mark. Removing the equivalent filter from the permission
    /// grid stack-overflows and aborts the test host.
    /// </summary>
    [TestMethod]
    public void RecomputingClashesDoesNotCascade()
    {
        OpenCodeKeybindEditorViewModel vm =
            EditorFromJson("""{"app_exit":"ctrl+q","app_help":"ctrl+x"}""");

        // Creating a clash makes RefreshDerived write ConflictWith on two rows. If the editor
        // reacted to that, this would not terminate.
        Assert.AreEqual(
            1,
            CountIsModifiedFires(vm, () => Row(vm, "app_help").Bindings[0].Chord = "ctrl+q"));
        Assert.AreEqual(2, vm.ConflictCount);
    }

    // ── Reset ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResettingReturnsToTheLoadedValue()
    {
        OpenCodeKeybindEditorViewModel vm = EditorFromJson("""{"app_exit":"ctrl+q"}""");
        Row(vm, "app_exit").Bindings[0].Chord = "ctrl+x";
        Assert.IsTrue(vm.IsModified);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual("{app_exit:ctrl+q}", Written(vm));
        Assert.IsFalse(vm.IsModified);
    }

    // ── The scope plumbing the library expects ───────────────────────────────────────────────────

    [TestMethod]
    public void AnOverriddenValueReportsItsScopes()
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);
        TestValue layered = new TestValue("keybinds")
            .With(TestScope.Project, CurrencyText.Parse("""{"app_exit":"ctrl+q"}"""))
            .With(TestScope.User, CurrencyText.Parse("""{"app_exit":"ctrl+x"}"""));

        vm.LoadFromValue(layered, TestScope.Project);

        Assert.IsTrue(vm.IsOverridden);
        Assert.AreEqual(TestScope.User.Id, vm.EffectiveScope?.Id);
        Assert.AreEqual(
            "ctrl+q",
            Row(vm, "app_exit").Bindings[0].Chord,
            "the editor edits its own scope, not the effective one");
    }

    [TestMethod]
    public void LoadingANullValueThrows()
    {
        OpenCodeKeybindEditorViewModel vm = new(Schema(), TestScope.Project);

        Assert.ThrowsExactly<ArgumentNullException>(
            () => vm.LoadFromValue(null!, TestScope.Project));
    }
}
