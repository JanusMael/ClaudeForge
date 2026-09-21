using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// ClaudeForge's half of the Backup / Restore page: which products it backs up, through which
/// engine, and in whose words.
/// </summary>
/// <remarks>
/// <para>
/// The page itself moved to <c>AgentForge.Avalonia.Shell.Backup</c>. Its machinery — listing
/// archives, retention, progress, drag-drop validation, restore orchestration, the credentials
/// prompt — was already product-neutral: <b>18 Claude references in 1,405 lines</b>, and the only
/// <c>PlatformPaths</c> use was <c>PlatformId</c>. What stayed behind is this file.
/// </para>
/// <para>
/// ⭐ <b>This also keeps the resx keys referenced, which is load-bearing.</b> ClaudeForge's
/// build fails on an unreferenced <c>Strings</c> key (the dead-string guard in
/// <c>Directory.Build.targets</c>). Moving the page out without mapping its 49 keys here would
/// have deleted them from the build's view and failed it — or, worse, invited someone to delete
/// nine locales' worth of translations to get back to green.
/// </para>
/// </remarks>
internal static class ClaudeBackupPage
{
    /// <summary>
    /// The products ClaudeForge backs up when no narrower set is supplied.
    /// </summary>
    /// <remarks>
    /// ⚠ The window passes its own section list instead, so this is the fallback rather than the
    /// usual path — it exists so a caller without a window (tests, tooling) still gets Claude's two
    /// rather than an empty page.
    /// </remarks>
    internal static IReadOnlyList<ProductDescriptor> DefaultProductsFor(ClaudeEnvironment env) =>
        [SchemaRegistry.ClaudeCodeProductFor(env), SchemaRegistry.ClaudeDesktopProduct];

    /// <summary>
    /// Options for the shell's Backup / Restore page.
    /// </summary>
    /// <param name="products">
    /// The products to offer, which the window passes from its own section list so the two cannot
    /// drift: a product this window hosts is a product the user can back up.
    /// </param>
    /// <remarks>
    /// ⚠ The engine is built here rather than shared, and its restorable products are Claude Code
    /// and Claude Desktop. A host whose products differ must pass an engine that matches, or it
    /// writes archives it cannot restore.
    /// <para>
    /// ⛔ The engine takes the same <paramref name="env"/> the products were built from. Handing it
    /// a different one would archive a directory the page is not showing.
    /// </para>
    /// </remarks>
    internal static BackupPageOptions Options(
        ClaudeEnvironment env,
        IReadOnlyList<ProductDescriptor> products) => new()
    {
        Engine = new BackupEngine(env),
        Products = products,

        // Claude Code's CLI and the Desktop app. Advisory only — the engine still handles
        // per-file lock failures individually.
        AgentProcessNames = ["claude", "claude-desktop"],

        // The file the include-credentials prompt asks about. A display string, not a resolved
        // path: the prompt is about WHICH secrets travel, and "~" reads in every locale.
        CredentialsPathDisplay = "~/.claude/.credentials.json",

        // Translated into nine locales, unlike ProductDescriptor.DisplayName — which is why these
        // are a lookup rather than the descriptor's own name.
        ProductCheckboxLabel = static product =>
        {
            if (string.Equals(product.Id, SchemaRegistry.ClaudeCodeProductId, StringComparison.Ordinal))
            {
                return Strings.CheckboxClaudeCode;
            }

            if (string.Equals(product.Id, SchemaRegistry.ClaudeDesktopProduct.Id, StringComparison.Ordinal))
            {
                return Strings.CheckboxClaudeDesktop;
            }

            return product.DisplayName;
        },

        Text = new BackupPageText
        {
            // Keyed by ArchiveFolder, which is what BackupEngine writes into manifest.clients.
            // ⚠ These were the shell's own two hardcoded arms until the map moved here; the
            // values are unchanged, so the cell still reads "Code+Desktop".
            ClientAbbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SchemaRegistry.ClaudeCodeArchiveFolder] = Strings.LabelClientAbbrevClaudeCode,
                [SchemaRegistry.ClaudeDesktopProduct.ArchiveFolder] = Strings.LabelClientAbbrevClaudeDesktop,
            },

            // ⚠ Keyed by ProductArchiveSection.ProgressLabelId — the archive SUB-PATH, which is
            // what the section's own remarks call it ("claude-dir", "profiles"). Not the file
            // name, not the destination: a key that misses simply shows the engine's English, so
            // a mistake here is invisible at runtime. ClaudeBackupPageProgressTests pins every id
            // against the descriptors rather than against a copy of this list.
            ProgressLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["claude.json"] = Strings.ProgressRestoreClaudeJson,
                ["claude-dir"] = Strings.ProgressRestoreClaudeHome,
                ["claude_desktop_config.json"] = Strings.ProgressRestoreDesktopConfig,
                ["profiles"] = Strings.ProgressRestoreDesktopProfiles,
                [".desktop-current"] = Strings.ProgressRestoreDesktopActiveProfile,

                // The four phases the engine drives itself, which belong to no product.
                [RestoreProgressIds.Applying] = Strings.ProgressRestoreApplying,
                [RestoreProgressIds.Projects] = Strings.ProgressRestoreProjects,
                [RestoreProgressIds.Worktrees] = Strings.ProgressRestoreWorktrees,
                [RestoreProgressIds.Complete] = Strings.ProgressRestoreComplete,

                // The backup side's only phrase — everything else it reports is a file name.
                [BackupProgressIds.DiscoveringProjects] = Strings.ProgressBackupDiscoveringProjects,
            },

            ButtonContinueWithoutSaving = Strings.ButtonContinueWithoutSaving,
            ButtonDiscardAndRestore = Strings.ButtonDiscardAndRestore,
            ButtonIncludeCredentialsConfirm = Strings.ButtonIncludeCredentialsConfirm,
            ButtonOmitCredentials = Strings.ButtonOmitCredentials,
            ButtonRestore = Strings.ButtonRestore,
            ButtonRestoreAnyway = Strings.ButtonRestoreAnyway,
            ButtonSaveDialog = Strings.ButtonSaveDialog,

            DialogTitleBackupFailed = Strings.DialogTitleBackupFailed,
            DialogTitleBackupWithoutSaving = Strings.DialogTitleBackupWithoutSaving,
            DialogTitleCrossPlatformRestore = Strings.DialogTitleCrossPlatformRestore,
            DialogTitleDiscardUnsavedEdits = Strings.DialogTitleDiscardUnsavedEdits,
            DialogTitleDropRestore = Strings.DialogTitleDropRestore,
            DialogTitleDropRestoreInvalid = Strings.DialogTitleDropRestoreInvalid,
            DialogTitleIncludeCredentials = Strings.DialogTitleIncludeCredentials,
            DialogTitleRestoreCompletedSkippedFmt = Strings.DialogTitleRestoreCompletedSkippedFmt,
            DialogTitleRestoreFailed = Strings.DialogTitleRestoreFailed,
            DialogTitleUnsavedChanges = Strings.DialogTitleUnsavedChanges,

            LabelBackupIncludesProject = Strings.LabelBackupIncludesProject,
            LabelBackupNoProjectOpen = Strings.LabelBackupNoProjectOpen,
            LabelLastBackupDaysFmt = Strings.LabelLastBackupDaysFmt,
            LabelLastBackupHoursFmt = Strings.LabelLastBackupHoursFmt,
            LabelLastBackupJustNow = Strings.LabelLastBackupJustNow,
            LabelLastBackupMinutesFmt = Strings.LabelLastBackupMinutesFmt,
            LabelLastBackupNever = Strings.LabelLastBackupNever,

            ProgressCreatingJunction = Strings.ProgressCreatingJunction,
            ProgressPreparing = Strings.ProgressPreparing,
            ProgressRestoring = Strings.ProgressRestoring,
            ProgressStarting = Strings.ProgressStarting,

            StatusBackupDeleteFailedFmt = Strings.StatusBackupDeleteFailedFmt,
            StatusBackupDeletedFmt = Strings.StatusBackupDeletedFmt,
            StatusBackupWarningsFmt = Strings.StatusBackupWarningsFmt,
            StatusChooseBackupFolder = Strings.StatusChooseBackupFolder,
            StatusAgentRunningFmt = Strings.StatusClaudeRunningFmt,

            // Share on a backup row reveals the archive; it does not open a share sheet, because
            // no platform here has one. Three sentences and not six: ShareFileAsync can only
            // report revealed / unavailable / failed.
            StatusShareArchiveRevealed = Strings.StatusShareArchiveRevealed,
            StatusShareArchiveUnavailable = Strings.StatusShareArchiveUnavailable,
            StatusShareArchiveFailed = Strings.StatusShareArchiveFailed,

            TextCredentialsExplainer = Strings.TextCredentialsExplainer,
            TextCrossPlatformRestoreMiddle = Strings.TextCrossPlatformRestoreMiddle,
            TextCrossPlatformRestorePrefix = Strings.TextCrossPlatformRestorePrefix,
            TextCrossPlatformRestoreSuffix = Strings.TextCrossPlatformRestoreSuffix,
            TextDiscardUnsavedForRestore = Strings.TextDiscardUnsavedForRestore,
            TextDropRestoreNotABackup = Strings.TextDropRestoreNotABackup,
            TextDropRestoreNotZip = Strings.TextDropRestoreNotZip,
            TextDropRestorePromptPrefix = Strings.TextDropRestorePromptPrefix,
            TextDropRestorePromptSuffix = Strings.TextDropRestorePromptSuffix,
            TextProceedWithoutSavingBackup = Strings.TextProceedWithoutSavingBackup,
            TextRestoreSkippedExplainer = Strings.TextRestoreSkippedExplainer,
            TextRestoreSkippedTrailerFmt = Strings.TextRestoreSkippedTrailerFmt,
            TextSaveBeforeBackupPrompt = Strings.TextSaveBeforeBackupPrompt,
            TextSaveBeforeRestorePrompt = Strings.TextSaveBeforeRestorePrompt,
        },
    };
}
