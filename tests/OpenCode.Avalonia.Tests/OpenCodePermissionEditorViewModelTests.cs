using System.ComponentModel;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Avalonia.Permissions;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The permission editor: order, both shapes, and the states where getting it wrong destroys a
/// user's rules rather than merely looking wrong.
/// </summary>
/// <remarks>
/// Every assertion about ordering is here rather than in the SDK tests because the SDK reads a
/// <c>JsonObject</c>, which preserves order for its own reasons. What these cover is the editor's
/// round trip — the leg where a plain dictionary or an alphabetical rebuild would silently invert
/// a policy.
/// </remarks>
[TestClass]
public sealed class OpenCodePermissionEditorViewModelTests
{
    private static OpenCodePermissionEditorViewModel NewEditor() =>
        new(new TestSchema(), TestScope.Project);

    /// <summary>An insertion-ordered currency map, the shape the shell's read path produces.</summary>
    private static OrderedPropertyMap Map(params (string Key, object? Value)[] entries)
    {
        OrderedPropertyMap map = new();
        foreach ((string key, object? value) in entries)
        {
            map.Set(key, value);
        }

        return map;
    }

    private static OpenCodePermissionEditorViewModel LoadedWith(object? value)
    {
        OpenCodePermissionEditorViewModel vm = NewEditor();
        vm.LoadFromValue(new TestValue().With(TestScope.Project, value), TestScope.Project);
        return vm;
    }

    private static IReadOnlyList<string> KeysOf(object? value) =>
        value is IReadOnlyDictionary<string, object?> map ? map.Keys.ToList() : [];

    // ── Order ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void LoadThenToValue_PreservesToolOrderAndRuleOrder()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("*", "ask"), ("git *", "allow"), ("rm -rf *", "deny"))),
            ("edit", "allow"),
            ("webfetch", "deny")));

        object? written = vm.ToValue();

        CollectionAssert.AreEqual(
            new[] { "bash", "edit", "webfetch" },
            KeysOf(written).ToArray(),
            "Tool order must survive the round trip: a lower layer's keys land first when scopes "
            + "merge, so reordering them changes which rule wins.");

        IReadOnlyDictionary<string, object?> map = (IReadOnlyDictionary<string, object?>)written!;
        CollectionAssert.AreEqual(
            new[] { "*", "git *", "rm -rf *" },
            KeysOf(map["bash"]).ToArray(),
            "Rule order IS the policy — the last match wins, so broad-first must stay broad-first.");
    }

    /// <remarks>
    /// ⚠ Asserts the TYPE on purpose. Every behavioural order assertion in this file stays green
    /// against a plain <see cref="Dictionary{TKey,TValue}"/>, because in practice one enumerates in
    /// insertion order until something is removed — an implementation detail the BCL documents as
    /// unspecified. Only naming the type holds the guarantee.
    /// </remarks>
    [TestMethod]
    public void ToValue_ReturnsAnOrderedMap_AtBothLevels()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("*", "ask"), ("git *", "allow")))));

        object? written = vm.ToValue();

        Assert.IsInstanceOfType<OrderedPropertyMap>(
            written,
            "The tool map must be emitted as an explicitly ordered map. A Dictionary happens to "
            + "enumerate in insertion order today and promises nothing.");

        // ⚠ Asserting only the outer map is not enough, and a canary proved it: swapping the
        // per-tool rule map for a plain Dictionary left all 24 tests green, including this one.
        // The inner map is the one whose order IS the policy — the outer merely groups by tool.
        Assert.IsInstanceOfType<OrderedPropertyMap>(
            ((IReadOnlyDictionary<string, object?>)written!)["bash"],
            "Each tool's RULE map is the one whose key order decides which rule wins, so it needs "
            + "the guarantee more than the tool map does.");
    }

    [TestMethod]
    public void MovingARuleDown_ChangesWhatOpenCodeWouldDecide()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("npm *", "deny"), ("*", "ask")))));

        OpenCodePermissionToolViewModel bash = vm.Tools.Single();

        Assert.AreEqual(
            PermissionOutcome.Ask,
            OpenCodePermissionModel.FromValue(vm.ToValue()).Resolve("bash", "npm install").Outcome,
            "As loaded, the broad ask sits last and defeats the narrow deny — the merge-inversion "
            + "hazard this editor exists to make visible.");

        bash.Rules[0].MoveDownCommand.Execute(null);

        Assert.AreEqual(
            PermissionOutcome.Deny,
            OpenCodePermissionModel.FromValue(vm.ToValue()).Resolve("bash", "npm install").Outcome,
            "Moving the deny below the broad rule must make it win. If this stays Ask, the reorder "
            + "did not reach the written value.");
    }

    // ── The two shapes ───────────────────────────────────────────────────────

    [TestMethod]
    public void BareActionValue_LoadsAsGlobalMode_AndWritesAStringBack()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith("ask");

        Assert.IsTrue(vm.IsGlobalMode);
        Assert.AreEqual(PermissionOutcome.Ask, vm.GlobalAction);
        Assert.AreEqual("ask", vm.ToValue(), "The global arm writes a bare string, not a map.");
    }

    [TestMethod]
    public void SwitchingToGlobalModeAndBack_KeepsThePerToolRules()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        vm.IsGlobalMode = true;
        Assert.IsInstanceOfType<string>(
            vm.ToValue(),
            "While global mode is on, the written value is the bare action.");

        vm.IsGlobalMode = false;

        Assert.AreEqual(
            1,
            vm.Tools.Count,
            "Toggling the mode must not destroy the arm that is not being written. A user who "
            + "clicks the toggle to see what the simple form looks like has not asked to delete "
            + "their grid.");
        CollectionAssert.AreEqual(
            new[] { "git *" },
            KeysOf(((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["bash"]).ToArray());
    }

    // ── Removal vs an empty object ───────────────────────────────────────────

    [TestMethod]
    public void ToValue_IsNullWhenNothingIsSet()
    {
        Assert.IsNull(
            LoadedWith(null).ToValue(),
            "An empty editor must write null so the workspace removes the key. An empty object "
            + "persists a permission setting that constrains nothing.");
    }

    [TestMethod]
    public void ToValue_OmitsAToolWithNoRules()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("bash", Map(("git *", "allow")))));
        vm.NewToolName = "edit";
        vm.AddToolCommand.Execute(null);

        Assert.AreEqual(2, vm.Tools.Count, "The tool row exists in the UI while it is being filled in.");
        CollectionAssert.AreEqual(
            new[] { "bash" },
            KeysOf(vm.ToValue()).ToArray(),
            "A pattern-arm tool with no rules yet must not be written — it would read back as a "
            + "tool the user configured and left empty.");
    }

    // ── Action-only tools ────────────────────────────────────────────────────

    [TestMethod]
    public void ActionOnlyTool_RefusesPatternMode()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("websearch", "deny")));
        OpenCodePermissionToolViewModel entry = vm.Tools.Single();

        Assert.IsFalse(entry.AcceptsPatterns);

        entry.UsesPatterns = true;

        Assert.IsFalse(
            entry.UsesPatterns,
            "The schema types these five tools as a bare action. Reporting the pattern shape would "
            + "produce a config OpenCode rejects.");
    }

    [TestMethod]
    public void RenamingIntoAnActionOnlyTool_HoldsTheRulesAndSaysSo()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        OpenCodePermissionToolViewModel entry = vm.Tools.Single();
        entry.Tool = "websearch";

        Assert.IsTrue(entry.HasModeNotice, "A silently discarded rule is the failure to avoid here.");
        StringAssert.Contains(entry.ModeNotice, "websearch");
        Assert.AreEqual(1, entry.Rules.Count, "The rules are held so undoing the rename restores them.");
        Assert.AreEqual(
            "ask",
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["websearch"],
            "What gets written is the shape the schema allows.");
    }

    // ── Shadowing ────────────────────────────────────────────────────────────

    [TestMethod]
    public void ShadowedRule_IsFlaggedAndNamesTheRuleThatCoversIt()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("npm *", "deny"), ("*", "ask")))));

        OpenCodePermissionToolViewModel bash = vm.Tools.Single();

        Assert.AreEqual(1, vm.ShadowedRuleCount);
        Assert.IsTrue(vm.HasShadowedRules);
        Assert.IsTrue(bash.Rules[0].IsShadowed, "The narrow deny is the inert one.");
        Assert.IsFalse(bash.Rules[1].IsShadowed);
        StringAssert.Contains(
            bash.Rules[0].ShadowReason,
            "*",
            "The reason has to name the covering rule, or the user cannot act on it.");
    }

    /// <remarks>
    /// The flag has to be recomputed from scratch on every change, not accumulated: a rule rescued
    /// by a reorder that kept a stale warning would tell the user their fix did not work.
    /// </remarks>
    [TestMethod]
    public void MovingTheShadowedRuleBelow_ClearsTheFlag()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("npm *", "deny"), ("*", "ask")))));

        vm.Tools.Single().Rules[0].MoveDownCommand.Execute(null);

        Assert.AreEqual(0, vm.ShadowedRuleCount);
        Assert.IsFalse(vm.Tools.Single().Rules.Any(r => r.IsShadowed));
    }

    [TestMethod]
    public void ShadowFlagsAreMatchedByPosition_NotByPattern()
    {
        // A permission object may legitimately repeat a pattern. Matching the SDK's report back
        // onto rows by pattern would attach the warning to whichever duplicate came first.
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        OpenCodePermissionToolViewModel bash = vm.Tools.Single();
        bash.Rules.Add(new OpenCodePermissionRowViewModel { Pattern = "git *", Action = PermissionOutcome.Deny });

        Assert.IsTrue(bash.Rules[0].IsShadowed, "The FIRST occurrence is the one that cannot fire.");
        Assert.IsFalse(bash.Rules[1].IsShadowed, "The last duplicate is the one that decides.");
    }

    /// <remarks>
    /// ⚠ The duplicate-collapse rule and the tester's claim to predict the file are the same
    /// statement. A JSON object cannot repeat a key, so the survivor's action AND position both
    /// decide the written policy — and putting it at the first occurrence's slot inverts the rule
    /// relative to the other keys. This is the test that caught that.
    /// </remarks>
    [TestMethod]
    public void ADuplicatePatternResolvesTheSameWayInTheGridAndInTheWrittenValue()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow"), ("*", "ask")))));

        OpenCodePermissionToolViewModel bash = vm.Tools.Single();
        bash.Rules.Add(new OpenCodePermissionRowViewModel
        {
            Pattern = "git *",
            Action = PermissionOutcome.Deny,
        });

        vm.TestTool = "bash";
        vm.TestInput = "git status";
        vm.RunTestCommand.Execute(null);

        Assert.AreEqual(
            PermissionOutcome.Deny,
            vm.TestOutcome,
            "In the grid the last matching row is the deny, so that is what the user is told.");

        Assert.AreEqual(
            PermissionOutcome.Deny,
            OpenCodePermissionModel.FromValue(vm.ToValue()).Resolve("bash", "git status").Outcome,
            "And the value actually written must decide the same way. Collapsing the duplicate onto "
            + "the FIRST occurrence's position yields {\"git *\":\"deny\",\"*\":\"ask\"}, whose last "
            + "match for this input is the broad rule — inverting the user's policy by saving it.");

        CollectionAssert.AreEqual(
            new[] { "*", "git *" },
            KeysOf(((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["bash"]).ToArray(),
            "The survivor sits at the later occurrence's position.");
    }

    // ── A value it could not read ────────────────────────────────────────────

    [TestMethod]
    public void UnparseableValue_IsReportedAndEchoedBackUnchanged()
    {
        OrderedPropertyMap original = Map(
            ("bash", Map(("git *", "allow"))),
            ("edit", "sometimes"));

        OpenCodePermissionEditorViewModel vm = LoadedWith(original);

        Assert.IsTrue(vm.HasLoadError);
        StringAssert.Contains(vm.LoadError, "sometimes", "The message must name what it choked on.");
        Assert.AreSame(
            original,
            vm.ToValue(),
            "The original value must be written back byte-for-byte. Serialising only what parsed "
            + "would delete the rest of the user's permission block on the next save — and the "
            + "entries that fail to parse are exactly the ones they most need to see.");
    }

    [TestMethod]
    public void UnparseableValue_LeavesTheGridEmptyRatherThanPartial()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow"))),
            ("edit", "sometimes")));

        Assert.AreEqual(
            0,
            vm.Tools.Count,
            "A half-populated grid invites the user to press Save and lose the half that is not "
            + "shown. The banner is the whole UI in this state.");
        Assert.AreEqual(0, vm.ShadowedRuleCount, "Shadowing is not computed over a value we cannot read.");
    }

    // ── The modified signal ──────────────────────────────────────────────────

    private static int CountIsModifiedFires(
        OpenCodePermissionEditorViewModel vm,
        Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpenCodePermissionEditorViewModel.IsModified))
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
    /// The force-fire case. After a load that already set the flag, <c>[ObservableProperty]</c>
    /// elides an equal assignment — and the live-write and save-enable subscriptions both watch
    /// the event, not the value. Without the re-raise the edit reaches neither.
    /// </remarks>
    [TestMethod]
    public void EditingARuleAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("bash", Map(("git *", "allow")))));
        Assert.IsTrue(vm.IsModified, "Loading a scope that has the key sets the flag.");

        int fired = CountIsModifiedFires(vm, () => vm.Tools[0].Rules[0].Pattern = "git push *");

        Assert.IsTrue(fired >= 1, "An inline edit on a loaded row must fire even though the flag was already true.");
    }

    [TestMethod]
    public void RemovingARuleAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("bash", Map(("git *", "allow")))));

        int fired = CountIsModifiedFires(vm, () => vm.Tools[0].Rules[0].RemoveCommand.Execute(null));

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void ChangingAToolsActionAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("edit", "allow")));

        int fired = CountIsModifiedFires(vm, () => vm.Tools[0].SingleAction = PermissionOutcome.Deny);

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void TypingInTheAddBoxes_DoesNotMarkTheEditorModified()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(("bash", Map(("git *", "allow")))));

        int fired = CountIsModifiedFires(vm, () =>
        {
            vm.NewToolName = "ed";
            vm.Tools[0].NewRulePattern = "npm";
        });

        Assert.AreEqual(
            0,
            fired,
            "These back the add boxes. Marking the file dirty per keystroke makes the Save button "
            + "flicker and writes half-typed patterns to disk on a live-write host.");
    }

    /// <remarks>
    /// The shadow scan writes <c>IsShadowed</c> / <c>ShadowReason</c> on rows the editor is
    /// subscribed to. Without those two names filtered out of the row handler, one edit becomes
    /// mark → recompute → row changed → mark, and the count here is unbounded rather than small.
    /// </remarks>
    [TestMethod]
    public void OneEditDoesNotCascadeThroughTheShadowRecompute()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("npm *", "deny"), ("*", "ask")))));

        int fired = CountIsModifiedFires(vm, () => vm.Tools[0].Rules[0].Pattern = "npm install *");

        Assert.IsTrue(fired is >= 1 and <= 4, $"Expected a small bounded number of fires, got {fired}.");
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedStateRatherThanClearing()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        vm.Tools[0].NewRulePattern = "rm -rf *";
        vm.Tools[0].AddRuleCommand.Execute(null);
        Assert.AreEqual(2, vm.Tools[0].Rules.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Tools.Count);
        CollectionAssert.AreEqual(
            new[] { "git *" },
            vm.Tools[0].Rules.Select(r => r.Pattern).ToArray(),
            "Reset means 'what is on disk', not 'empty'.");
        Assert.IsFalse(vm.IsModified);
    }

    // ── Tester ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void RunTest_NamesTheRuleThatDecided()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("*", "ask"), ("git *", "allow")))));

        vm.TestTool = "bash";
        vm.TestInput = "git status";
        vm.RunTestCommand.Execute(null);

        Assert.AreEqual(PermissionOutcome.Allow, vm.TestOutcome);
        StringAssert.Contains(vm.TestResult, "git *");
    }

    /// <remarks>
    /// The wording matters as much as the outcome: <c>Default</c> means no rule decided the call,
    /// which is not the same claim as "this is allowed".
    /// </remarks>
    [TestMethod]
    public void RunTest_ReportsNoMatchWithoutClaimingItIsPermitted()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        vm.TestTool = "bash";
        vm.TestInput = "npm install";
        vm.RunTestCommand.Execute(null);

        Assert.AreEqual(PermissionOutcome.Default, vm.TestOutcome);
        StringAssert.Contains(vm.TestResult, "No rule matched");
    }

    [TestMethod]
    public void RunTest_ResolvesAgainstUnsavedEdits()
    {
        OpenCodePermissionEditorViewModel vm = LoadedWith(Map(
            ("bash", Map(("git *", "allow")))));

        vm.Tools[0].NewRulePattern = "git push *";
        vm.Tools[0].AddRuleCommand.Execute(null);
        vm.Tools[0].Rules[^1].Action = PermissionOutcome.Deny;

        vm.TestTool = "bash";
        vm.TestInput = "git push origin main";
        vm.RunTestCommand.Execute(null);

        Assert.AreEqual(
            PermissionOutcome.Deny,
            vm.TestOutcome,
            "The question worth answering is whether the edit about to be saved does what the user "
            + "thinks, so the tester must read the live state and not the file.");
    }
}
