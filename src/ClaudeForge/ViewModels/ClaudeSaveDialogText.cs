using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// This app's wording for the save / restore confirmation dialog — the product half
/// of the save seam.
///
/// <para>
/// Read from <see cref="Strings"/> at call time rather than cached in a static, for
/// the same reason the synthetic search rows are: the resource lookups are not
/// culture-aware until <c>ApplyCulture</c> runs in <c>Program.Main</c>, so a table
/// built at type initialisation would pin the startup culture into every label.
/// </para>
/// <para>
/// These twelve keys stay in <c>Strings.resx</c> and keep their nine-locale
/// translations and their parity guard. Moving them into a resource set beside the
/// neutral dialog would have looked tidier and quietly un-translated them: the parity
/// test resolves <c>src/ClaudeForge/Localization</c> by path, so nothing outside that
/// directory is checked — which is exactly why the 93 strings already living in
/// <c>ClaudeForge.Avalonia</c> have no locales at all.
/// </para>
/// </summary>
public static class ClaudeSaveDialogText
{
    /// <summary>Build the dialog wording from the current culture's resources.</summary>
    public static SaveDialogText Create()
    {
        return new SaveDialogText
        {
            SaveTitle = Strings.DialogTitleSaveChanges,
            RestoreTitle = Strings.DialogTitleRestorePreview,
            SaveConfirmButton = Strings.ButtonSaveDialog,
            RestoreConfirmButton = Strings.ButtonRestore,
            CancelButton = Strings.ButtonCancel,
            SaveSummaryFormat = Strings.TextSaveSummary,
            RestoreSummaryFormat = Strings.TextRestoreSummary,
            WillBeWrittenTo = Strings.LabelWillBeWrittenTo,
            WillBeRestoredTo = Strings.LabelWillBeRestoredTo,
            KindAdded = Strings.SaveDialogKindAdded,
            KindRemoved = Strings.SaveDialogKindRemoved,
            KindModified = Strings.SaveDialogKindModified,
        };
    }
}
