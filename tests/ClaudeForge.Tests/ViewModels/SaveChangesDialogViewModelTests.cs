using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="SaveChangesDialogViewModel"/> — specifically the
/// destination-path surfacing additions: <see cref="SaveChangeSectionViewModel.FilePath"/>,
/// <see cref="SaveChangeSectionViewModel.ActionVerb"/>, and the outer
/// <see cref="SaveChangesDialogViewModel.SummaryLine"/> /
/// <see cref="SaveChangesDialogViewModel.ActionVerb"/>.
/// <para>
/// Each section's <c>FilePath</c> and <c>ActionVerb</c> are populated by
/// <c>MainWindowViewModel.AppendWorkspaceSections</c> at build time; these
/// tests instantiate the section directly to exercise the projection logic
/// without spinning up a workspace.
/// </para>
/// </summary>
[TestClass]
public sealed class SaveChangesDialogViewModelTests
{
    // -----------------------------------------------------------------------
    // Section: FilePath round-trip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Section_FilePath_DefaultsToEmpty()
    {
        SaveChangeSectionViewModel section = new();
        Assert.AreEqual(string.Empty, section.FilePath);
    }

    [TestMethod]
    public void Section_FilePath_RoundTrips()
    {
        SaveChangeSectionViewModel section = new()
        {
            FilePath = "~/.claude/settings.json",
        };
        Assert.AreEqual("~/.claude/settings.json", section.FilePath);
    }

    // -----------------------------------------------------------------------
    // Section: ActionVerb defaults to Save
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Section_ActionVerb_DefaultsToSaveLabel()
    {
        SaveChangeSectionViewModel section = new();
        // Default is the "Will be written to:" label so a section instantiated
        // outside the dialog builder still renders coherent text.
        Assert.AreEqual(Strings.LabelWillBeWrittenTo, section.ActionVerb);
    }

    // -----------------------------------------------------------------------
    // Outer ViewModel: ActionVerb / SummaryLine vary by Mode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Outer_ActionVerb_SaveMode()
    {
        SaveChangesDialogViewModel dlg = new() { Mode = SaveDialogMode.Save };
        Assert.AreEqual(Strings.LabelWillBeWrittenTo, dlg.ActionVerb);
    }

    [TestMethod]
    public void Outer_ActionVerb_RestoreMode()
    {
        SaveChangesDialogViewModel dlg = new() { Mode = SaveDialogMode.Restore };
        Assert.AreEqual(Strings.LabelWillBeRestoredTo, dlg.ActionVerb);
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
        };
        StringAssert.Contains(dlg.SummaryLine, "0");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static SaveChangesDialogViewModel BuildDialog(SaveDialogMode mode, int[] counts)
    {
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
                });
            }

            sections.Add(new SaveChangeSectionViewModel
            {
                WorkspaceName = "Test",
                ScopeText = "user",
                Entries = entries,
                FilePath = $"~/.claude/section{i}.json",
                ActionVerb = mode == SaveDialogMode.Restore
                    ? Strings.LabelWillBeRestoredTo
                    : Strings.LabelWillBeWrittenTo,
            });
        }

        return new SaveChangesDialogViewModel
        {
            Sections = sections,
            Mode = mode,
        };
    }
}