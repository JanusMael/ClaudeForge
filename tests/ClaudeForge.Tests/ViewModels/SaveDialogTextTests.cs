using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The neutral half of the save seam: which of the host's words the dialog picks for
/// a given mode and change kind.
///
/// <para>
/// Every string here belongs to a product that does not exist, so a rule can be told
/// apart from the wording it was applied to. Asserting Claude's real labels — which
/// <see cref="SaveChangesDialogViewModelTests"/> does — cannot distinguish "restore
/// mode picks the restore title" from "both modes happen to read the same key".
/// </para>
/// </summary>
[TestClass]
public sealed class SaveDialogTextTests
{
    private static SaveDialogText Fake => new()
    {
        SaveTitle = "save-title",
        RestoreTitle = "restore-title",
        SaveConfirmButton = "save-confirm",
        RestoreConfirmButton = "restore-confirm",
        CancelButton = "cancel",
        SaveSummaryFormat = "saving {0} in {1}",
        RestoreSummaryFormat = "restoring {0} in {1}",
        WillBeWrittenTo = "written-to",
        WillBeRestoredTo = "restored-to",
        KindAdded = "added",
        KindRemoved = "removed",
        KindModified = "modified",
    };

    [TestMethod]
    public void EveryModeDependentLookup_PicksTheMatchingHalf()
    {
        SaveDialogText t = Fake;

        Assert.AreEqual("save-title", t.TitleFor(SaveDialogMode.Save));
        Assert.AreEqual("restore-title", t.TitleFor(SaveDialogMode.Restore));
        Assert.AreEqual("save-confirm", t.ConfirmButtonFor(SaveDialogMode.Save));
        Assert.AreEqual("restore-confirm", t.ConfirmButtonFor(SaveDialogMode.Restore));
        Assert.AreEqual("saving {0} in {1}", t.SummaryFormatFor(SaveDialogMode.Save));
        Assert.AreEqual("restoring {0} in {1}", t.SummaryFormatFor(SaveDialogMode.Restore));
        Assert.AreEqual("written-to", t.ActionVerbFor(SaveDialogMode.Save));
        Assert.AreEqual("restored-to", t.ActionVerbFor(SaveDialogMode.Restore));
    }

    [TestMethod]
    public void AccessibleName_IsDistinctPerChangeKind()
    {
        SaveDialogText t = Fake;

        Assert.AreEqual("added", t.AccessibleNameFor(ChangeKind.Added));
        Assert.AreEqual("removed", t.AccessibleNameFor(ChangeKind.Removed));
        Assert.AreEqual("modified", t.AccessibleNameFor(ChangeKind.Modified));
    }

    /// <summary>
    /// The dialog formats the summary itself, so the host's format string has to be a
    /// composite one taking the change count then the file count — in that order.
    /// </summary>
    [TestMethod]
    public void SummaryLine_SubstitutesChangeCountThenFileCount()
    {
        SaveChangesDialogViewModel dlg = new()
        {
            Text = Fake,
            Mode = SaveDialogMode.Save,
            Sections =
            [
                new SaveChangeSectionViewModel
                {
                    ActionVerb = "written-to",
                    Entries =
                    [
                        new SaveChangeEntryViewModel { Key = "a", KindAccessibleName = "added" },
                        new SaveChangeEntryViewModel { Key = "b", KindAccessibleName = "added" },
                        new SaveChangeEntryViewModel { Key = "c", KindAccessibleName = "added" },
                    ],
                },
                new SaveChangeSectionViewModel
                {
                    ActionVerb = "written-to",
                    Entries = [new SaveChangeEntryViewModel { Key = "d", KindAccessibleName = "added" }],
                },
            ],
        };

        Assert.AreEqual("saving 4 in 2", dlg.SummaryLine,
            "{0} is the total change count across sections; {1} is the number of sections.");
    }

    /// <summary>
    /// This app must fill in every slot. A missing one cannot happen — the record's
    /// members are <c>required</c> — but a <em>duplicated</em> one can, and it would
    /// silently make two different labels identical.
    /// </summary>
    [TestMethod]
    public void ClaudeText_FillsEverySlot_WithDistinctModeDependentWording()
    {
        SaveDialogText t = ClaudeSaveDialogText.Create();

        foreach ((string name, string value) in new[]
                 {
                     (nameof(t.SaveTitle), t.SaveTitle),
                     (nameof(t.RestoreTitle), t.RestoreTitle),
                     (nameof(t.SaveConfirmButton), t.SaveConfirmButton),
                     (nameof(t.RestoreConfirmButton), t.RestoreConfirmButton),
                     (nameof(t.CancelButton), t.CancelButton),
                     (nameof(t.SaveSummaryFormat), t.SaveSummaryFormat),
                     (nameof(t.RestoreSummaryFormat), t.RestoreSummaryFormat),
                     (nameof(t.WillBeWrittenTo), t.WillBeWrittenTo),
                     (nameof(t.WillBeRestoredTo), t.WillBeRestoredTo),
                     (nameof(t.KindAdded), t.KindAdded),
                     (nameof(t.KindRemoved), t.KindRemoved),
                     (nameof(t.KindModified), t.KindModified),
                 })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"{name} must carry text.");
        }

        Assert.AreNotEqual(t.SaveTitle, t.RestoreTitle);
        Assert.AreNotEqual(t.SaveConfirmButton, t.RestoreConfirmButton);
        Assert.AreNotEqual(t.SaveSummaryFormat, t.RestoreSummaryFormat);
        Assert.AreNotEqual(t.WillBeWrittenTo, t.WillBeRestoredTo);
        Assert.AreNotEqual(t.KindAdded, t.KindRemoved);
        Assert.AreNotEqual(t.KindAdded, t.KindModified);
        Assert.AreNotEqual(t.KindRemoved, t.KindModified);
    }
}
