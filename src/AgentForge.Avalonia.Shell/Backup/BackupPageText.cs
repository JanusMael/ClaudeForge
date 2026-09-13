namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;

/// <summary>
/// The wording the Backup / Restore page emits, supplied by the host.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <c>SaveDialogText</c>, and for the same reason: this page's
/// <i>machinery</i> — listing archives, retention, progress, drag-drop validation, restore
/// orchestration, the credentials prompt — is identical for any layered-config product, so it lives
/// on this side and the host hands over the words.
/// </para>
/// <para>
/// ⚠ <b>Deliberately NOT a resource set of its own.</b> Each app's <c>Strings.resx</c> carries its
/// locales and a parity test hard-wired to that one directory; a second resource set here would be
/// unguarded by it, so moving translated keys into one would silently un-translate them everywhere
/// but English. Verbatim the reasoning recorded on <c>SaveDialogText</c>.
/// </para>
/// <para>
/// ⭐ <b>Of the 49 strings this page used, 46 were already product-neutral</b> — "Restore",
/// "Preparing…", "Backup failed". Only the two product checkboxes and the running-agent warning
/// named Claude, and those are the two members below that changed shape rather than moving
/// verbatim: the checkbox labels became
/// <see cref="BackupPageOptions.ProductCheckboxLabel"/>, and the warning became
/// <see cref="StatusAgentRunningFmt"/>.
/// </para>
/// <para>
/// Every member is <c>required</c>: a missing entry is a compile error instead of English leaking
/// into a translated build.
/// </para>
/// </remarks>
public sealed record BackupPageText
{
    // ── Product names ──────────────────────────────────────────────────────

    /// <summary>
    /// Short names for the Clients column, keyed by the product name a manifest carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Supplied by the host because the neutral layer has no business knowing product
    /// names.</b> This mapping was two hardcoded arms — <c>"claudecode" =&gt; "Code"</c> and
    /// <c>"claudedesktop" =&gt; "Desktop"</c> — inside <c>BackupRowViewModel</c>, so OpenCode's
    /// two products fell through to the verbose passthrough and rendered
    /// <c>OpenCode+OpenCodeTui</c> in a 110 px cell.
    /// </para>
    /// <para>
    /// ⚠ <b>A product missing from this map is not an error.</b> An archive can name a product
    /// this build has never heard of — a newer release, or another app's backup sitting in the
    /// same folder — and the raw name is then rendered as-typed, preserving its casing.
    /// Abbreviating is a courtesy to a narrow column, never a correctness requirement, and
    /// <see cref="BackupRowViewModel.DisplayClientsTooltip"/> carries the full names either way.
    /// </para>
    /// <para>
    /// ⚠ Lookups are case-insensitive regardless of the comparer the host built this with: a
    /// manifest written by a third-party tool can spell the product any way it likes.
    /// </para>
    /// </remarks>
    public required IReadOnlyDictionary<string, string> ClientAbbreviations { get; init; }

    // ── Buttons ────────────────────────────────────────────────────────────

    /// <summary>Proceed past the unsaved-changes prompt without saving.</summary>
    public required string ButtonContinueWithoutSaving { get; init; }

    /// <summary>Discard unsaved edits and restore anyway.</summary>
    public required string ButtonDiscardAndRestore { get; init; }

    /// <summary>Confirm including credentials in the archive.</summary>
    public required string ButtonIncludeCredentialsConfirm { get; init; }

    /// <summary>Proceed with the backup but leave credentials out.</summary>
    public required string ButtonOmitCredentials { get; init; }

    /// <summary>Primary restore action.</summary>
    public required string ButtonRestore { get; init; }

    /// <summary>Restore despite a warning (cross-platform, running agent).</summary>
    public required string ButtonRestoreAnyway { get; init; }

    /// <summary>Save pending edits first.</summary>
    public required string ButtonSaveDialog { get; init; }

    // ── Dialog titles ──────────────────────────────────────────────────────

    /// <summary>The backup could not be written.</summary>
    public required string DialogTitleBackupFailed { get; init; }

    /// <summary>Backing up with unsaved edits in the editor.</summary>
    public required string DialogTitleBackupWithoutSaving { get; init; }

    /// <summary>Restoring an archive made on a different operating system.</summary>
    public required string DialogTitleCrossPlatformRestore { get; init; }

    /// <summary>Discarding unsaved edits in order to restore.</summary>
    public required string DialogTitleDiscardUnsavedEdits { get; init; }

    /// <summary>Confirming a restore started by dropping a file on the window.</summary>
    public required string DialogTitleDropRestore { get; init; }

    /// <summary>The dropped file is not a restorable archive.</summary>
    public required string DialogTitleDropRestoreInvalid { get; init; }

    /// <summary>Asking whether to include credentials.</summary>
    public required string DialogTitleIncludeCredentials { get; init; }

    /// <summary>Restore finished but skipped items; <c>{0}</c> = skipped count.</summary>
    public required string DialogTitleRestoreCompletedSkippedFmt { get; init; }

    /// <summary>The restore could not be applied.</summary>
    public required string DialogTitleRestoreFailed { get; init; }

    /// <summary>There are unsaved changes.</summary>
    public required string DialogTitleUnsavedChanges { get; init; }

    // ── Labels ─────────────────────────────────────────────────────────────

    /// <summary>The backup will include the currently-open project.</summary>
    public required string LabelBackupIncludesProject { get; init; }

    /// <summary>No project is open, so none is included.</summary>
    public required string LabelBackupNoProjectOpen { get; init; }

    /// <summary>Last backup was N days ago; <c>{0}</c> = days.</summary>
    public required string LabelLastBackupDaysFmt { get; init; }

    /// <summary>Last backup was N hours ago; <c>{0}</c> = hours.</summary>
    public required string LabelLastBackupHoursFmt { get; init; }

    /// <summary>Last backup was moments ago.</summary>
    public required string LabelLastBackupJustNow { get; init; }

    /// <summary>Last backup was N minutes ago; <c>{0}</c> = minutes.</summary>
    public required string LabelLastBackupMinutesFmt { get; init; }

    /// <summary>No backup has ever been taken.</summary>
    public required string LabelLastBackupNever { get; init; }

    // ── Progress ───────────────────────────────────────────────────────────

    /// <summary>Creating a directory junction during restore (Windows).</summary>
    public required string ProgressCreatingJunction { get; init; }

    /// <summary>Preparing to archive.</summary>
    public required string ProgressPreparing { get; init; }

    /// <summary>Applying a restore.</summary>
    public required string ProgressRestoring { get; init; }

    /// <summary>Work has begun.</summary>
    public required string ProgressStarting { get; init; }

    // ── Status line ────────────────────────────────────────────────────────

    /// <summary>An archive could not be deleted; <c>{0}</c> = reason.</summary>
    public required string StatusBackupDeleteFailedFmt { get; init; }

    /// <summary>An archive was deleted; <c>{0}</c> = name.</summary>
    public required string StatusBackupDeletedFmt { get; init; }

    /// <summary>The backup succeeded with warnings; <c>{0}</c> = count.</summary>
    public required string StatusBackupWarningsFmt { get; init; }

    /// <summary>Prompt to pick a destination folder.</summary>
    public required string StatusChooseBackupFolder { get; init; }

    /// <summary>
    /// The agent is running and files may be locked; <c>{0}</c> = process count.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Was <c>StatusClaudeRunningFmt</c>.</b> Renamed because the processes to look for are
    /// now <see cref="BackupPageOptions.AgentProcessNames"/> — the host's, not Claude's. The
    /// wording each host supplies still names its own agent.
    /// </remarks>
    public required string StatusAgentRunningFmt { get; init; }

    // ── Explanatory body text ──────────────────────────────────────────────

    /// <summary>What including credentials means, and why it is off by default.</summary>
    public required string TextCredentialsExplainer { get; init; }

    /// <summary>Cross-platform restore warning, middle segment.</summary>
    public required string TextCrossPlatformRestoreMiddle { get; init; }

    /// <summary>Cross-platform restore warning, leading segment.</summary>
    public required string TextCrossPlatformRestorePrefix { get; init; }

    /// <summary>Cross-platform restore warning, trailing segment.</summary>
    public required string TextCrossPlatformRestoreSuffix { get; init; }

    /// <summary>Discarding unsaved edits in order to restore.</summary>
    public required string TextDiscardUnsavedForRestore { get; init; }

    /// <summary>The dropped file is a zip but not one of our archives.</summary>
    public required string TextDropRestoreNotABackup { get; init; }

    /// <summary>The dropped file is not a zip at all.</summary>
    public required string TextDropRestoreNotZip { get; init; }

    /// <summary>Drop-to-restore confirmation, leading segment.</summary>
    public required string TextDropRestorePromptPrefix { get; init; }

    /// <summary>Drop-to-restore confirmation, trailing segment.</summary>
    public required string TextDropRestorePromptSuffix { get; init; }

    /// <summary>Proceeding with a backup while edits are unsaved.</summary>
    public required string TextProceedWithoutSavingBackup { get; init; }

    /// <summary>Why a restore skipped some projects or worktrees.</summary>
    public required string TextRestoreSkippedExplainer { get; init; }

    /// <summary>Trailer listing skipped items; <c>{0}</c> = the list.</summary>
    public required string TextRestoreSkippedTrailerFmt { get; init; }

    /// <summary>Offer to save before backing up.</summary>
    public required string TextSaveBeforeBackupPrompt { get; init; }

    /// <summary>Offer to save before restoring.</summary>
    public required string TextSaveBeforeRestorePrompt { get; init; }
}
