using System.ComponentModel;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Agents;
using Bennewitz.Ninja.OpenCode.Sdk.Agents;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The agent editor: partial overrides, and the nested permission grid.
/// </summary>
/// <remarks>
/// The property that carries the most weight here is that an untouched control writes nothing. An
/// agent entry overrides a built-in, so a default written as a value silently replaces behaviour
/// the user never asked to change.
/// </remarks>
[TestClass]
public sealed class OpenCodeAgentEditorViewModelTests
{
    private static OpenCodeAgentEditorViewModel LoadedWith(string json)
    {
        OpenCodeAgentEditorViewModel vm = new(new TestSchema("agent"), TestScope.Project);
        vm.LoadFromValue(
            new TestValue("agent").With(TestScope.Project, CurrencyText.Parse(json)),
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
    public void AFullyPopulatedAgent_RoundTripsThroughTheEditor()
    {
        AssertRoundTrips(
            """
            {"build":{"model":"anthropic/claude","variant":"fast","temperature":0.3,"top_p":0.9,
            "prompt":"You build.","tools":{"bash":true},"disable":false,"description":"Builder",
            "mode":"primary","hidden":false,"options":{"x":1},"color":"#ff8800","steps":12,
            "maxSteps":40,"permission":{"bash":{"git *":"allow"}}}}
            """,
            "Opening a config and saving it untouched must not change it.");
    }

    [TestMethod]
    public void APartialOverride_StaysPartial()
    {
        AssertRoundTrips(
            """{"build":{"model":"anthropic/claude"}}""",
            "One overridden field must not become fifteen. Everything the user left alone has to "
            + "keep inheriting.");
    }

    [TestMethod]
    public void EditingOneFieldDoesNotMaterialiseTheOthers()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");
        vm.Agents[0].Description = "now described";

        var written = (IReadOnlyDictionary<string, object?>)vm.ToValue()!;
        var build = (IReadOnlyDictionary<string, object?>)written["build"]!;

        CollectionAssert.AreEquivalent(
            new[] { "model", "description" },
            build.Keys.ToArray(),
            "Touching one field must not write the other thirteen as defaults.");
    }

    [TestMethod]
    public void AgentOrderIsPreserved()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"zeta":{"model":"a"},"build":{"model":"b"}}""");

        CollectionAssert.AreEqual(
            new[] { "zeta", "build" },
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!).Keys.ToArray());
    }

    [TestMethod]
    public void ToValue_IsNullWhenThereAreNoAgents()
    {
        OpenCodeAgentEditorViewModel vm = new(new TestSchema("agent"), TestScope.Project);
        vm.LoadFromValue(new TestValue("agent"), TestScope.Project);

        Assert.IsNull(vm.ToValue(), "An empty object would persist an agent key configuring nothing.");
    }

    // ── Built-in vs user-defined ─────────────────────────────────────────────

    [TestMethod]
    public void BuiltInNamesAreDistinguishedFromUserDefinedOnes()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"model":"a"},"my-agent":{"model":"b"}}""");

        Assert.IsTrue(vm.Agents[0].IsBuiltIn, "'build' is one of the seven the schema names.");
        Assert.IsFalse(vm.Agents[1].IsBuiltIn);
        Assert.IsTrue(vm.Agents[1].IsUserDefined);
    }

    [TestMethod]
    public void RenamingAnAgentReclassifiesIt()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"my-agent":{"model":"a"}}""");
        Assert.IsFalse(vm.Agents[0].IsBuiltIn);

        vm.Agents[0].Name = "build";

        Assert.IsTrue(
            vm.Agents[0].IsBuiltIn,
            "The badge has to follow the name — a rename is exactly when the user needs telling "
            + "that they are now overriding shipped behaviour.");
    }

    // ── Numbers: absent, zero, and garbage ───────────────────────────────────

    [TestMethod]
    public void AnEmptyNumberBoxLeavesTheKeyOut()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");

        Assert.AreEqual(string.Empty, vm.Agents[0].TemperatureText);

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.IsFalse(build.ContainsKey("temperature"));
    }

    [TestMethod]
    public void AnExplicitZeroIsWritten()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");
        vm.Agents[0].TemperatureText = "0";

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.IsTrue(
            build.ContainsKey("temperature"),
            "Zero is a deliberate temperature. Dropping it because it looks like a default is the "
            + "mirror image of writing defaults.");
        Assert.AreEqual(0d, build["temperature"]);
    }

    /// <remarks>
    /// ⚠ Mid-typing states are normal — selecting the box and starting to retype leaves it briefly
    /// unparseable. Treating each intermediate state as "unset" would delete the setting on a
    /// live-write host before the user finished typing.
    /// </remarks>
    [TestMethod]
    public void UnparseableNumberKeepsTheLastGoodValueRatherThanClearingIt()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"temperature":0.5}}""");

        vm.Agents[0].TemperatureText = "0.7x";

        Assert.IsTrue(vm.Agents[0].HasNumberError, "And the user is told the box is not valid yet.");

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.AreEqual(
            0.5d,
            build["temperature"],
            "The last good value survives mid-typing rather than the key being dropped.");
    }

    /// <remarks>
    /// ⚠ Pinned because it is surprising: <c>double.TryParse("0.")</c> <b>succeeds</b> in .NET and
    /// yields zero. So the commonest half-typed decimal is not an error state at all — it is a
    /// valid zero that becomes 0.5 as soon as the next character lands. Worth stating, because the
    /// obvious assumption when writing the error path is the opposite.
    /// </remarks>
    [TestMethod]
    public void ATrailingDecimalPointIsAValidNumber_NotAnError()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"temperature":0.5}}""");

        vm.Agents[0].TemperatureText = "0.";

        Assert.IsFalse(vm.Agents[0].HasNumberError);

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.AreEqual(0d, build["temperature"]);
    }

    [TestMethod]
    public void ClearingANumberBoxDoesRemoveTheKey()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"temperature":0.5}}""");
        vm.Agents[0].TemperatureText = "   ";

        Assert.IsFalse(vm.Agents[0].HasNumberError, "Blank is a valid 'absent', not an error.");

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.IsFalse(
            build.ContainsKey("temperature"),
            "Deliberately clearing the box must remove the override, or the user cannot undo one.");
    }

    // ── ⭐ The nested permission grid ────────────────────────────────────────

    /// <remarks>
    /// ⭐ The whole point of hosting the real grid: shadow detection, reordering and the tester all
    /// work inside an agent because there is no second permission implementation.
    /// </remarks>
    [TestMethod]
    public void AnAgentPermissionOverrideIsEditedByTheRealPermissionGrid()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"permission":{"bash":{"npm *":"deny","*":"ask"}}}}""");

        var grid = vm.Agents[0].Permission;

        Assert.IsNotNull(grid);
        Assert.IsTrue(vm.Agents[0].HasPermissionOverride);
        Assert.AreEqual(1, grid.Tools.Count);
        Assert.AreEqual(
            1,
            grid.ShadowedRuleCount,
            "The nested grid runs the same shadow scan — the narrow deny sits before the broad ask, "
            + "which is the merge-inversion hazard the grid exists to surface.");
    }

    [TestMethod]
    public void EditingTheNestedGridReachesTheWrittenValue()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"permission":{"bash":{"git *":"allow"}}}}""");

        vm.Agents[0].Permission!.Tools[0].NewRulePattern = "rm -rf *";
        vm.Agents[0].Permission!.Tools[0].AddRuleCommand.Execute(null);

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "rm -rf *",
            "A rule added in the child grid has to appear in the parent's value, or the nested "
            + "editor is decorative.");
    }

    [TestMethod]
    public void EditingTheNestedGridMarksTheParentModified()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"permission":{"bash":{"git *":"allow"}}}}""");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OpenCodeAgentEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Agents[0].Permission!.Tools[0].Rules[0].Pattern = "git push *";

        Assert.IsTrue(
            fired >= 1,
            "The child's IsModified is the only signal that a nested rule changed. Miss it and Save "
            + "stays disabled, which looks exactly like the editor ignoring the user.");
    }

    /// <remarks>
    /// An absent <c>permission</c> means "inherit the global rules"; an empty one means "override
    /// with nothing". Collapsing the two would silently change what the agent may do.
    /// </remarks>
    [TestMethod]
    public void RemovingTheOverrideRemovesTheKeyEntirely()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"model":"m","permission":{"bash":"ask"}}}""");

        vm.Agents[0].RemovePermissionOverrideCommand.Execute(null);

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.IsFalse(build.ContainsKey("permission"));
        Assert.IsFalse(vm.Agents[0].HasPermissionOverride);
    }

    [TestMethod]
    public void AddingAnOverrideThenLeavingItEmptyWritesNoPermissionKey()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");

        vm.Agents[0].AddPermissionOverrideCommand.Execute(null);

        Assert.IsTrue(vm.Agents[0].HasPermissionOverride);

        var build = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["build"]!;

        Assert.IsFalse(
            build.ContainsKey("permission"),
            "An override with no rules yet is not a policy. The grid returns null when empty, and "
            + "that is exactly the signal to leave the key out.");
    }

    /// <remarks>
    /// ⭐ Filtering a settings page descends through <c>IChildEditorHost</c>. Without the nested
    /// grids showing up here, typing a permission pattern would stop at the collapsed agent row and
    /// the nested rules would be unreachable by search.
    /// </remarks>
    [TestMethod]
    public void NestedGridsAreExposedAsChildEditors()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"permission":{"bash":"ask"}},"plain":{"model":"m"}}""");

        IReadOnlyList<PropertyEditorViewModel> children = ((IChildEditorHost)vm).Children;

        Assert.AreEqual(1, children.Count, "Only the agent that has an override contributes a child.");

        vm.Agents[1].AddPermissionOverrideCommand.Execute(null);

        Assert.AreEqual(
            2,
            ((IChildEditorHost)vm).Children.Count,
            "Children is recomputed, not cached — an override added later must become searchable.");
    }

    // ── Deprecated tools, opaque entries, reset ──────────────────────────────

    [TestMethod]
    public void TheDeprecatedToolsMapIsSurfacedOnlyWhenAlreadyPresent()
    {
        OpenCodeAgentEditorViewModel withTools = LoadedWith(
            """{"build":{"tools":{"bash":true}}}""");
        Assert.IsTrue(withTools.Agents[0].HasDeprecatedTools);

        OpenCodeAgentEditorViewModel without = LoadedWith("""{"build":{"model":"m"}}""");
        Assert.IsFalse(
            without.Agents[0].HasDeprecatedTools,
            "The schema deprecates this field, so an empty section would only invite filling it in.");
    }

    [TestMethod]
    public void AnOpaqueEntryIsNotEditableAndSurvivesASave()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"weird":"just a string"}""");

        Assert.IsTrue(vm.Agents[0].IsOpaqueEntry);
        Assert.IsFalse(vm.Agents[0].IsEditable);

        AssertRoundTrips(
            """{"weird":"just a string"}""",
            "Nothing about it is editable, and nothing about it should be destroyed.");
    }

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedState()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");

        vm.NewAgentName = "extra";
        vm.AddAgentCommand.Execute(null);
        Assert.AreEqual(2, vm.Agents.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Agents.Count);
        Assert.AreEqual("build", vm.Agents[0].Name);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void TypingInTheAddBox_DoesNotMarkTheEditorModified()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OpenCodeAgentEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.NewAgentName = "my-";
        vm.Agents[0].Tools.NewTool = "ba";

        Assert.AreEqual(0, fired);
    }

    [TestMethod]
    public void EditingAFieldAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith("""{"build":{"model":"m"}}""");
        Assert.IsTrue(vm.IsModified);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OpenCodeAgentEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Agents[0].Model = "other";

        Assert.IsTrue(fired >= 1, "The force-fire case: the flag was already true from the load.");
    }

    // ── Global permission context ────────────────────────────────────────────

    [TestMethod]
    public void SummarisePermission_DescribesBothShapesAndNothingElse()
    {
        Assert.AreEqual(string.Empty, OpenCodeAgentEditorViewModel.SummarisePermission(null));
        Assert.AreEqual(
            string.Empty,
            OpenCodeAgentEditorViewModel.SummarisePermission(CurrencyText.Parse("{}")));

        StringAssert.Contains(
            OpenCodeAgentEditorViewModel.SummarisePermission("ask"),
            "ask",
            "The bare-action form applies to every tool and the summary should say so.");

        string map = OpenCodeAgentEditorViewModel.SummarisePermission(
            CurrencyText.Parse("""{"bash":"ask","edit":"allow"}"""));
        StringAssert.Contains(map, "bash");
        StringAssert.Contains(map, "edit");
    }

    [TestMethod]
    public void TheGlobalSummaryReachesEveryAgentRow()
    {
        OpenCodeAgentEditorViewModel vm = LoadedWith(
            """{"build":{"model":"m"},"plan":{"model":"n"}}""");

        vm.SetGlobalPermissionSummary("Global permission covers: bash");

        Assert.IsTrue(vm.Agents.All(a => a.HasGlobalPermissionContext));
        Assert.IsTrue(vm.Agents.All(a => a.GlobalPermissionSummary.Contains("bash", StringComparison.Ordinal)));
    }
}
