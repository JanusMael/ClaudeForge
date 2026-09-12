using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Localization;

namespace Bennewitz.Ninja.OpenCodeForge.ViewModels;

/// <summary>
/// OpenCodeForge's half of the Backup / Restore page: which products it backs up, through which
/// engine, and in whose words.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <c>ClaudeForge.ViewModels.ClaudeBackupPage</c>. The page itself lives in
/// <c>AgentForge.Avalonia.Shell.Backup</c> and knows nothing about either product; everything
/// product-shaped is in this file and in <c>Views/BackupRestoreView.axaml</c> beside it.
/// </para>
/// <para>
/// ⭐ <b>This also keeps the resx keys referenced, which is load-bearing.</b> The repo's
/// dead-string guard fails the build on any <c>Strings</c> key not referenced as the literal token
/// <c>Strings.&lt;Key&gt;</c>. 49 of the Backup page's keys are named only here; the other 55 are
/// named only by the view.
/// </para>
/// </remarks>
internal static class OpenCodeBackupPage
{
    /// <summary>
    /// The products OpenCodeForge backs up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b><c>OpenCodeProducts.Tui</c> archives nothing of its own today</b>, and that is not a
    /// wiring mistake. Its descriptor carries no <c>BackupLayout</c>, so its sections are
    /// <c>ProductBackupLayout.Empty</c> — but <c>tui.json</c> lives beside <c>opencode.json</c>
    /// inside the config root that <c>OpenCodeProducts.Config</c> archives whole, so it travels
    /// regardless. Listing it here is still right: it puts the TUI in the archive's
    /// <c>manifest.clients</c>, bundles its schema, and means the checkbox starts doing real work
    /// the day the product gains a layout of its own rather than needing to be remembered then.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<ProductDescriptor> DefaultProducts { get; } = OpenCodeProducts.All;

    /// <summary>
    /// Options for the shell's Backup / Restore page.
    /// </summary>
    /// <param name="products">
    /// The products to offer, which the window passes from its own section list so the two cannot
    /// drift: a product this window hosts is a product the user can back up.
    /// </param>
    /// <remarks>
    /// ⛔ <b><c>OpenCodeBackup.Engine</c>, never <c>BackupEngine.Default</c>.</b> The default
    /// engine's restorable products are Claude Code and Claude Desktop. It writes OpenCode archives
    /// perfectly happily and restores <i>nothing</i> from them, reporting success — a backup the
    /// user believes in and cannot use. <c>OpenCodeBackupRoundTripTests</c> is the guard, and
    /// <c>AGENTS.md</c> carries the invariant.
    /// </remarks>
    internal static BackupPageOptions Options(IReadOnlyList<ProductDescriptor> products) => new()
    {
        Engine = OpenCodeBackup.Engine,
        Products = products,

        // The CLI, which is the only OpenCode process that holds these files. Advisory only — the
        // engine still handles per-file lock failures individually.
        AgentProcessNames = ["opencode"],

        // ⛔ NOT auth.json, which is never archived at all. What the include-credentials prompt is
        // actually asking about is the session database and its two SQLite sidecars: account and
        // control_account hold access_token and refresh_token, credential holds `value`.
        CredentialsPathDisplay = "~/.local/share/opencode/opencode.db",

        // The nav section headings, reused rather than duplicated: the checkbox for a product and
        // the tree node for that same product should not be able to disagree about its name.
        ProductCheckboxLabel = static product =>
        {
            if (string.Equals(product.Id, OpenCodeProducts.Config.Id, StringComparison.Ordinal))
            {
                return Strings.SectionOpenCode;
            }

            if (string.Equals(product.Id, OpenCodeProducts.Tui.Id, StringComparison.Ordinal))
            {
                return Strings.SectionOpenCodeTui;
            }

            return product.DisplayName;
        },

        Text = new BackupPageText
        {
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
            StatusAgentRunningFmt = Strings.StatusOpenCodeRunningFmt,

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
