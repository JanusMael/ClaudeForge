using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="SaveChangesDialogViewModel"/> — the destination-path
/// surfacing (<see cref="SaveChangeSectionViewModel.FilePath"/> /
/// <see cref="SaveChangeSectionViewModel.ActionVerb"/>) and the outer
/// <see cref="SaveChangesDialogViewModel.SummaryLine"/> /
/// <see cref="SaveChangesDialogViewModel.ActionVerb"/>.
/// <para>
/// Phase 5 slice 5 moved the model to the neutral shell, so the wording now arrives
/// as a <see cref="SaveDialogText"/> instead of being read from <c>Strings</c> inside
/// the view-model. These fixtures pass this app's real wording
/// (<see cref="ClaudeSaveDialogText"/>), so they still assert against the shipped
/// labels; <see cref="SaveDialogTextTests"/> covers the neutral resolution rules with
/// a product that does not exist.
/// </para>
/// </summary>
[TestClass]
public sealed class SaveChangesDialogViewModelTests
{
    private static SaveDialogText Text => ClaudeSaveDialogText.Create();

    private static SaveChangeSectionViewModel Section(string? actionVerb = null)
    {
        return new SaveChangeSectionViewModel
        {
            ActionVerb = actionVerb ?? Strings.LabelWillBeWrittenTo,
        };
    }

    // -----------------------------------------------------------------------
    // Section: FilePath round-trip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Section_FilePath_DefaultsToEmpty()
    {
        Assert.AreEqual(string.Empty, Section().FilePath);
    }

    [TestMethod]
    public void Section_FilePath_RoundTrips()
    {
        SaveChangeSectionViewModel section = new()
        {
            FilePath = "~/.claude/settings.json",
            ActionVerb = Strings.LabelWillBeWrittenTo,
        };
        Assert.AreEqual("~/.claude/settings.json", section.FilePath);
    }

    /// <summary>
    /// Replaces the old <c>Section_ActionVerb_DefaultsToSaveLabel</c>. The verb no
    /// longer has a default: it is <c>required</c>, because a section that silently
    /// claims "will be written to" inside a restore preview is exactly the mistake a
    /// default invites. What matters now is that the builder supplies the right one,
    /// which <see cref="SaveDialogBuilderTests"/> asserts against a real workspace.
    /// </summary>
    [TestMethod]
    public void Section_ActionVerb_IsWhateverItWasGiven()
    {
        Assert.AreEqual(Strings.LabelWillBeRestoredTo,
            Section(Strings.LabelWillBeRestoredTo).ActionVerb);
    }

    // -----------------------------------------------------------------------
    // Outer ViewModel: ActionVerb / SummaryLine vary by Mode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Outer_ActionVerb_SaveMode()
    {
        SaveChangesDialogViewModel dlg = new() { Mode = SaveDialogMode.Save, Text = Text };
        Assert.AreEqual(Strings.LabelWillBeWrittenTo, dlg.ActionVerb);
    }

    [TestMethod]
    public void Outer_ActionVerb_RestoreMode()
    {
        SaveChangesDialogViewModel dlg = new() { Mode = SaveDialogMode.Restore, Text = Text };
        Assert.AreEqual(Strings.LabelWillBeRestoredTo, dlg.ActionVerb);
    }

    [TestMethod]
    public void Outer_TitleAndConfirmButton_VaryByMode()
    {
        SaveChangesDialogViewModel save = new() { Mode = SaveDialogMode.Save, Text = Text };
        SaveChangesDialogViewModel restore = new() { Mode = SaveDialogMode.Restore, Text = Text };

        Assert.AreEqual(Strings.DialogTitleSaveChanges, save.WindowTitle);
        Assert.AreEqual(Strings.DialogTitleRestorePreview, restore.WindowTitle);
        Assert.AreEqual(Strings.ButtonSaveDialog, save.ConfirmButtonLabel);
        Assert.AreEqual(Strings.ButtonRestore, restore.ConfirmButtonLabel);
        Assert.AreEqual(Strings.ButtonCancel, save.CancelButtonLabel,
            "Cancel reads the same in both modes.");
        Assert.AreEqual(Strings.ButtonCancel, restore.CancelButtonLabel);
    }

    [TestMethod]
    public void SummaryLine_SaveMode_RendersCorrectCounts()
    {
        SaveChangesDialogViewModel dlg = BuildDialog(
            mode: SaveDialogMode.Save,
            counts: [2, 3]);

        // 2 + 3 = 5 changes across 2 files. The exact wording lives in
        // Strings.resx; we assert each numeric token appears so the test
        // remains stable across translations.
        StringAssert.Contains(dlg.SummaryLine, "5");
        StringAssert.Contains(dlg.SummaryLine, "2");
    }

    [TestMethod]
    public void SummaryLine_RestoreMode_UsesRestoreTemplate()
    {
        SaveChangesDialogViewModel save = BuildDialog(SaveDialogMode.Save, [1]);
        SaveChangesDialogViewModel restore = BuildDialog(SaveDialogMode.Restore, [1]);

        // The two summary lines must come from different format strings; if
        // they were identical the Mode switch would be silently broken.
        Assert.AreNotEqual(save.SummaryLine, restore.SummaryLine);
    }

    [TestMethod]
    public void SummaryLine_NoSections_RendersZeros()
    {
        SaveChangesDialogViewModel dlg = new()
        {
            Sections = [],
            Mode = SaveDialogMode.Save,
            Text = Text,
        };
        StringAssert.Contains(dlg.SummaryLine, "0");
    }

    [TestMethod]
    public void ChangesOnlyText_JoinsEverySectionAndEntry()
    {
        SaveChangesDialogViewModel dlg = BuildDialog(SaveDialogMode.Save, [2, 1]);

        string text = dlg.ChangesOnlyText;

        StringAssert.Contains(text, "key0_0");
        StringAssert.Contains(text, "key0_1");
        StringAssert.Contains(text, "key1_0",
            "The clipboard text must cover every section, not just the first.");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static SaveChangesDialogViewModel BuildDialog(SaveDialogMode mode, int[] counts)
    {
        SaveDialogText text = Text;
        List<SaveChangeSectionViewModel> sections = new();
        for (int i = 0; i < counts.Length; i++)
        {
            List<SaveChangeEntryViewModel> entries = new();
            for (int j = 0; j < counts[i]; j++)
            {
                entries.Add(new SaveChangeEntryViewModel
                {
                    Kind = ChangeKind.Modified,
                    Key = $"key{i}_{j}",
                    KindAccessibleName = text.AccessibleNameFor(ChangeKind.Modified),
                });
            }

            sections.Add(new SaveChangeSectionViewModel
            {
                WorkspaceName = "Test",
                ScopeText = "user",
                Entries = entries,
                FilePath = $"~/.claude/section{i}.json",
                ActionVerb = text.ActionVerbFor(mode),
            });
        }

        return new SaveChangesDialogViewModel
        {
            Sections = sections,
            Mode = mode,
            Text = text,
        };
    }
}
