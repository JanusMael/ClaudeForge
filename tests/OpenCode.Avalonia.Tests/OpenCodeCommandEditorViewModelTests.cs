using System.ComponentModel;
using Bennewitz.Ninja.OpenCode.Avalonia.Commands;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The command editor: the required template, and the shell-execution warning.
/// </summary>
[TestClass]
public sealed class OpenCodeCommandEditorViewModelTests
{
    private static OpenCodeCommandEditorViewModel LoadedWith(string json)
    {
        OpenCodeCommandEditorViewModel vm = new(new TestSchema("command"), TestScope.Project);
        vm.LoadFromValue(
            new TestValue("command").With(TestScope.Project, CurrencyText.Parse(json)),
            TestScope.Project);
        return vm;
    }

    private static void AssertRoundTrips(string json, string because) =>
        Assert.AreEqual(
            CurrencyText.Render(CurrencyText.Parse(json)),
            CurrencyText.Render(LoadedWith(json).ToValue()),
            because);

    [TestMethod]
    public void AFullyPopulatedCommand_RoundTripsThroughTheEditor()
    {
        AssertRoundTrips(
            """
            {"review":{"template":"Review $ARGUMENTS","description":"Review code","agent":"build",
            "model":"anthropic/claude","variant":"fast","subtask":true}}
            """,
            "Opening a config and saving it untouched must not change it.");
    }

    [TestMethod]
    public void ToValue_IsNullWhenThereAreNoCommands()
    {
        OpenCodeCommandEditorViewModel vm = new(new TestSchema("command"), TestScope.Project);
        vm.LoadFromValue(new TestValue("command"), TestScope.Project);

        Assert.IsNull(vm.ToValue());
    }

    [TestMethod]
    public void AnUnsetSubtaskOmitsTheKeyRatherThanWritingFalse()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");

        Assert.IsNull(vm.Commands[0].Subtask);

        var review = (IReadOnlyDictionary<string, object?>)
            ((IReadOnlyDictionary<string, object?>)vm.ToValue()!)["review"]!;

        Assert.IsFalse(
            review.ContainsKey("subtask"),
            "Absent means OpenCode's own default applies, which is not the same claim as false.");
    }

    // ── The required template ────────────────────────────────────────────────

    [TestMethod]
    public void AMissingTemplateIsCountedAndFlagged()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith(
            """{"good":{"template":"a"},"bad":{"description":"no body"}}""");

        Assert.AreEqual(1, vm.InvalidCommandCount);
        Assert.IsTrue(vm.HasInvalidCommands);
        Assert.IsFalse(vm.Commands[0].IsTemplateMissing);
        Assert.IsTrue(vm.Commands[1].IsTemplateMissing);
    }

    [TestMethod]
    public void ClearingATemplateFlagsItImmediately()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");
        Assert.AreEqual(0, vm.InvalidCommandCount);

        vm.Commands[0].Template = "   ";

        Assert.IsTrue(vm.Commands[0].IsTemplateMissing);
        Assert.AreEqual(
            1,
            vm.InvalidCommandCount,
            "The banner count is recomputed on every edit, not only on load — otherwise the user "
            + "breaks an entry and is told nothing until they reopen the page.");
    }

    [TestMethod]
    public void ANewCommandStartsInvalid_BecauseItHasNoTemplateYet()
    {
        OpenCodeCommandEditorViewModel vm = new(new TestSchema("command"), TestScope.Project);
        vm.LoadFromValue(new TestValue("command"), TestScope.Project);

        vm.NewCommandName = "review";
        vm.AddCommandEntryCommand.Execute(null);

        Assert.IsTrue(
            vm.Commands[0].IsTemplateMissing,
            "Saying so up front is better than letting the user save an invalid entry and find out "
            + "from OpenCode.");
        Assert.IsNull(
            vm.ToValue(),
            "And with nothing but an empty entry there is nothing worth writing yet.");
    }

    [TestMethod]
    public void AnOpaqueEntryIsNotFlaggedAsMissingATemplate()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"weird":"a string"}""");

        Assert.IsTrue(vm.Commands[0].IsOpaqueEntry);
        Assert.IsFalse(vm.Commands[0].IsEditable);
        Assert.AreEqual(
            0,
            vm.InvalidCommandCount,
            "An entry that is not an object cannot be missing a field, and saying it is sends the "
            + "user hunting for a box that is not there.");
    }

    // ── ⭐ The shell warning ─────────────────────────────────────────────────

    /// <remarks>
    /// ⭐ <c>!`…`</c> executes with the user's privileges every time the command runs. A template
    /// pasted from a shared config is exactly where that goes unnoticed, so the editor counts them
    /// and says so.
    /// </remarks>
    [TestMethod]
    public void ATemplateThatRunsAShellCommandIsCountedAndFlagged()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith(
            """{"diff":{"template":"Here: !`git diff`"},"plain":{"template":"Review $ARGUMENTS"}}""");

        Assert.AreEqual(1, vm.ShellCommandCount);
        Assert.IsTrue(vm.HasShellCommands);
        Assert.IsTrue(vm.Commands[0].UsesShellInterpolation);
        Assert.IsFalse(vm.Commands[1].UsesShellInterpolation);
    }

    [TestMethod]
    public void TypingShellInterpolationRaisesTheWarningWithoutReopeningThePage()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"diff":{"template":"safe"}}""");
        Assert.AreEqual(0, vm.ShellCommandCount);

        vm.Commands[0].Template = "now !`rm -rf /` unsafe";

        Assert.IsTrue(vm.Commands[0].UsesShellInterpolation);
        Assert.AreEqual(1, vm.ShellCommandCount);
    }

    // ── Preservation ─────────────────────────────────────────────────────────

    [TestMethod]
    public void UnknownFieldsSurviveAnEditElsewhereOnTheSameCommand()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith(
            """{"review":{"template":"a","futureField":true}}""");

        vm.Commands[0].Description = "described";

        StringAssert.Contains(
            CurrencyText.Render(vm.ToValue()),
            "futureField:true",
            "The schema forbids unknown fields, which is exactly why a config from a newer "
            + "OpenCode must not be stripped by the first save.");
    }

    [TestMethod]
    public void AnOpaqueEntryDoesNotStopItsNeighboursBeingEdited()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith(
            """{"weird":"a string","review":{"template":"a"}}""");

        vm.Commands[1].Description = "described";

        string written = CurrencyText.Render(vm.ToValue());
        StringAssert.Contains(written, "a string");
        StringAssert.Contains(written, "described");
    }

    // ── Modification plumbing ────────────────────────────────────────────────

    private static int CountIsModifiedFires(OpenCodeCommandEditorViewModel vm, Action mutate)
    {
        int fired = 0;
        void Handler(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpenCodeCommandEditorViewModel.IsModified))
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
    public void EditingATemplateAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");
        Assert.IsTrue(vm.IsModified);

        int fired = CountIsModifiedFires(vm, () => vm.Commands[0].Template = "b");

        Assert.IsTrue(fired >= 1, "The force-fire case: the flag was already true from the load.");
    }

    [TestMethod]
    public void RemovingACommandAfterLoad_FiresIsModifiedPropertyChanged()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Commands[0].RemoveCommand.Execute(null));

        Assert.IsTrue(fired >= 1);
    }

    [TestMethod]
    public void TypingInTheAddBox_DoesNotMarkTheEditorModified()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");

        int fired = CountIsModifiedFires(vm, () => vm.NewCommandName = "rev");

        Assert.AreEqual(0, fired);
    }

    /// <remarks>
    /// <c>RefreshCounts</c> writes derived state that the row reports, and the row is subscribed —
    /// so without those names filtered out, one keystroke becomes mark → recount → row changed →
    /// mark.
    /// </remarks>
    [TestMethod]
    public void OneEditDoesNotCascadeThroughTheRecount()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");

        int fired = CountIsModifiedFires(vm, () => vm.Commands[0].Template = "!`ls`");

        Assert.IsTrue(fired is >= 1 and <= 4, $"Expected a small bounded number of fires, got {fired}.");
    }

    [TestMethod]
    public void ResetAfterEdit_RestoresTheLoadedState()
    {
        OpenCodeCommandEditorViewModel vm = LoadedWith("""{"review":{"template":"a"}}""");

        vm.NewCommandName = "extra";
        vm.AddCommandEntryCommand.Execute(null);
        Assert.AreEqual(2, vm.Commands.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Commands.Count);
        Assert.AreEqual("review", vm.Commands[0].Name);
        Assert.IsFalse(vm.IsModified);
    }
}
