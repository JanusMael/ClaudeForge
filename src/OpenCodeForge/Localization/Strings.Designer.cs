// ⚠ The compiler treats any *.Designer.cs as auto-generated, which switches OFF the project's
// nullable context — so nullable annotations here need this directive or they are CS8669.
// The file name is not cosmetic: it is also what exempts this file from the repo's resx
// dynamic-access guard.
#nullable enable

using System.Globalization;
using System.Resources;

namespace Bennewitz.Ninja.OpenCodeForge.Localization;

/// <summary>
/// This app's user-facing strings, read from <c>Strings.resx</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-maintained, but deliberately named <c>Strings.Designer.cs</c>: that is the file name the
/// repo's resx guard exempts from its dynamic-access check, because this file IS the accessor the
/// guard wants every other file to go through. <c>PublicResXFileCodeGenerator</c> only runs inside
/// an IDE, so the alternative is a generated file that silently drifts whenever the resource is
/// edited outside one.
/// </para>
/// <para>
/// ⚠ <b>Every key must also be referenced somewhere as a literal <c>Strings.Key</c>.</b> The dead-
/// key guard scans for exactly that form, so a key reachable only through this file's own
/// <c>nameof</c> would be reported unused and deleted. All seven are referenced by the app.
/// </para>
/// <para>
/// ⚠ English-only today, and structured so that is a translation gap rather than a code change:
/// adding <c>Strings.&lt;culture&gt;.resx</c> beside the resource is sufficient, since the csproj
/// deliberately leaves <c>SatelliteResourceLanguages</c> unset.
/// </para>
/// <para>
/// ⚠ The resource base name is a literal. It must match <c>RootNamespace</c> plus this folder, and
/// a mismatch fails at RUNTIME with a missing-manifest exception rather than at build time — which
/// is why <c>OpenCodeForgeStringsTests</c> asserts every key resolves to something other than its
/// own name.
/// </para>
/// </remarks>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Bennewitz.Ninja.OpenCodeForge.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Overrides the lookup culture. Test seam; null follows the UI culture.</summary>
    internal static CultureInfo? CultureOverride { get; set; }

    private static string Get(string key) =>
        Manager.GetString(key, CultureOverride ?? CultureInfo.CurrentUICulture) ?? key;

    /// <summary>The application's display name.</summary>
    public static string AppTitle => Get(nameof(AppTitle));

    /// <summary>Header for a settings page's properties tab.</summary>
    public static string HeaderTabProperties => Get(nameof(HeaderTabProperties));

    /// <summary>Header for a settings page's effective-value tab.</summary>
    public static string HeaderTabEffective => Get(nameof(HeaderTabEffective));

    /// <summary>Header for the JSON tab when every schema key is shown.</summary>
    public static string HeaderTabJsonAll => Get(nameof(HeaderTabJsonAll));

    /// <summary>Header for the JSON tab when only keys present in the file are shown.</summary>
    public static string HeaderTabJsonActive => Get(nameof(HeaderTabJsonActive));

    /// <summary>Navigation header for the main OpenCode configuration section.</summary>
    public static string SectionOpenCode => Get(nameof(SectionOpenCode));

    /// <summary>Navigation header for the terminal-UI configuration section.</summary>
    public static string SectionOpenCodeTui => Get(nameof(SectionOpenCodeTui));

    /// <summary>Clients-column short name for the main config product.</summary>
    public static string LabelClientAbbrevOpenCode => Get(nameof(LabelClientAbbrevOpenCode));

    /// <summary>
    /// Clients-column short name for the TUI product — the abbreviation that keeps
    /// <c>OpenCode+TUI</c> inside a 110 px cell where <c>OpenCode+OpenCodeTui</c> did not fit.
    /// </summary>
    public static string LabelClientAbbrevOpenCodeTui => Get(nameof(LabelClientAbbrevOpenCodeTui));

    // ── Restore progress ───────────────────────────────────────────────────
    // ⚠ Keyed to ProductArchiveSection.ProgressLabelId / RestoreProgressIds by
    // OpenCodeBackupPage, not by name. A rename here is free; a rename there falls silently back
    // to the engine's English.

    /// <summary>Progress label while OpenCode's config root is restored.</summary>
    public static string ProgressRestoreOpenCodeConfig => Get(nameof(ProgressRestoreOpenCodeConfig));

    /// <summary>Progress label while the second, default config root is restored.</summary>
    public static string ProgressRestoreOpenCodeConfigDefault =>
        Get(nameof(ProgressRestoreOpenCodeConfigDefault));

    /// <summary>Progress label while the session database is restored.</summary>
    public static string ProgressRestoreOpenCodeDb => Get(nameof(ProgressRestoreOpenCodeDb));

    /// <summary>Progress label while the database's write-ahead log is restored.</summary>
    public static string ProgressRestoreOpenCodeDbWal => Get(nameof(ProgressRestoreOpenCodeDbWal));

    /// <summary>Progress label while the database's shared-memory file is restored.</summary>
    public static string ProgressRestoreOpenCodeDbShm => Get(nameof(ProgressRestoreOpenCodeDbShm));

    /// <summary>Progress label for the engine's apply phase.</summary>
    public static string ProgressRestoreApplying => Get(nameof(ProgressRestoreApplying));

    /// <summary>Progress label for the engine's per-project phase.</summary>
    public static string ProgressRestoreProjects => Get(nameof(ProgressRestoreProjects));

    /// <summary>Progress label for the engine's worktree phase.</summary>
    public static string ProgressRestoreWorktrees => Get(nameof(ProgressRestoreWorktrees));

    /// <summary>Progress label for the engine's final step.</summary>
    public static string ProgressRestoreComplete => Get(nameof(ProgressRestoreComplete));

    /// <summary>The one phrase the BACKUP engine emits; everything else it reports is a file.</summary>
    public static string ProgressBackupDiscoveringProjects =>
        Get(nameof(ProgressBackupDiscoveringProjects));

    /// <summary>Navigation header for the artifacts page.</summary>
    public static string SectionArtifacts => Get(nameof(SectionArtifacts));

    /// <summary>Navigation header for the Essentials page.</summary>
    public static string SectionEssentials => Get(nameof(SectionEssentials));

    /// <summary>Badge text when the schema came from the binary.</summary>
    public static string SchemaBadgeBundled => Get(nameof(SchemaBadgeBundled));

    /// <summary>Badge text when the schema was downloaded. {0} = local time.</summary>
    public static string SchemaBadgeFetchedFmt => Get(nameof(SchemaBadgeFetchedFmt));

    /// <summary>Badge hover text for a bundled schema. {0} = short digest.</summary>
    public static string SchemaBadgeTooltipBundledFmt => Get(nameof(SchemaBadgeTooltipBundledFmt));

    /// <summary>Badge hover text for a fetched schema. {0} = local time, {1} = short digest.</summary>
    public static string SchemaBadgeTooltipFetchedFmt => Get(nameof(SchemaBadgeTooltipFetchedFmt));

    /// <summary>About-dialog title, and the accessible name of the status-bar version button.</summary>
    public static string MenuAbout => Get(nameof(MenuAbout));

    /// <summary>Dismisses the About dialog.</summary>
    public static string ButtonClose => Get(nameof(ButtonClose));

    /// <summary>App version line in the About dialog. {0} = version string.</summary>
    public static string LabelVersionFmt => Get(nameof(LabelVersionFmt));

    /// <summary>Re-fetches every hosted schema.</summary>
    public static string ButtonCheckForSchemaUpdates => Get(nameof(ButtonCheckForSchemaUpdates));

    /// <summary>Hover text for the schema-update button.</summary>
    public static string TipCheckForSchemaUpdates => Get(nameof(TipCheckForSchemaUpdates));

    /// <summary>Shown while the schema check is in flight.</summary>
    public static string SchemaCheckChecking => Get(nameof(SchemaCheckChecking));

    /// <summary>Every checked schema hashed to what was already loaded.</summary>
    public static string SchemaCheckUpToDate => Get(nameof(SchemaCheckUpToDate));

    /// <summary>At least one schema changed. {0} = comma-joined product names.</summary>
    public static string SchemaCheckUpdatedFmt => Get(nameof(SchemaCheckUpdatedFmt));

    /// <summary>Upstream did not answer, so the bundled copy is in use.</summary>
    public static string SchemaCheckUnavailable => Get(nameof(SchemaCheckUnavailable));

    /// <summary>No source could supply a schema. {0} = comma-joined product names.</summary>
    public static string SchemaCheckFailedFmt => Get(nameof(SchemaCheckFailedFmt));

    /// <summary>The About dialog's app-update button.</summary>
    public static string ButtonCheckForUpdates => Get(nameof(ButtonCheckForUpdates));

    /// <summary>Why the button works regardless of the Essentials toggle.</summary>
    public static string TipCheckForUpdates => Get(nameof(TipCheckForUpdates));

    /// <summary>Shown while the check is in flight.</summary>
    public static string LabelCheckForUpdatesChecking => Get(nameof(LabelCheckForUpdatesChecking));

    /// <summary>No newer release was found.</summary>
    public static string LabelCheckForUpdatesUpToDate => Get(nameof(LabelCheckForUpdatesUpToDate));

    /// <summary>A newer release exists. {0} = its tag.</summary>
    public static string LabelCheckForUpdatesAvailableFmt => Get(nameof(LabelCheckForUpdatesAvailableFmt));

    /// <summary>The check failed. Deliberately not alarming — a failed check is not a problem.</summary>
    public static string LabelCheckForUpdatesFailed => Get(nameof(LabelCheckForUpdatesFailed));

    /// <summary>Opens the release page for the version just found.</summary>
    public static string ButtonUpdateBannerOpenRelease => Get(nameof(ButtonUpdateBannerOpenRelease));

    /// <summary>Tooltip for the view-release button.</summary>
    public static string TipButtonUpdateBannerOpenRelease => Get(nameof(TipButtonUpdateBannerOpenRelease));

    /// <summary>Banner headline. {0} = the new version's tag.</summary>
    public static string LabelUpdateBannerTitleFmt => Get(nameof(LabelUpdateBannerTitleFmt));

    /// <summary>Banner body.</summary>
    public static string LabelUpdateBannerDesc => Get(nameof(LabelUpdateBannerDesc));

    /// <summary>Banner close glyph. AutomationProperties.Name carries the real label — a screen reader announcing a multiplication sign is why.</summary>
    public static string ButtonUpdateBannerDismiss => Get(nameof(ButtonUpdateBannerDismiss));

    /// <summary>Says that dismissal is per-version, not forever.</summary>
    public static string TipButtonUpdateBannerDismiss => Get(nameof(TipButtonUpdateBannerDismiss));

    /// <summary>Accessible name for the glyph-only dismiss button.</summary>
    public static string AutoNameButtonUpdateBannerDismiss => Get(nameof(AutoNameButtonUpdateBannerDismiss));

    /// <summary>Essentials card title for the auto-check opt-out.</summary>
    public static string EssentialsCardCheckForUpdatesTitle => Get(nameof(EssentialsCardCheckForUpdatesTitle));

    /// <summary>Essentials card body for the auto-check opt-out.</summary>
    public static string EssentialsCardCheckForUpdatesBody => Get(nameof(EssentialsCardCheckForUpdatesBody));
    /// <summary>“Backup and restore”</summary>
    public static string AutoNameBackupRestoreTabs => Get(nameof(AutoNameBackupRestoreTabs));

    /// <summary>“Available backups”</summary>
    public static string AutoNameBackupsGrid => Get(nameof(AutoNameBackupsGrid));

    /// <summary>“Browse for backup output folder”</summary>
    public static string AutoNameBrowseBackupFolder => Get(nameof(AutoNameBrowseBackupFolder));

    /// <summary>“Browse for restore source folder”</summary>
    public static string AutoNameBrowseRestoreFolder => Get(nameof(AutoNameBrowseRestoreFolder));

    /// <summary>“Browse…”</summary>
    public static string ButtonBrowse => Get(nameof(ButtonBrowse));

    /// <summary>“Cancel”</summary>
    public static string ButtonCancel => Get(nameof(ButtonCancel));

    /// <summary>“Continue without saving”</summary>
    public static string ButtonContinueWithoutSaving => Get(nameof(ButtonContinueWithoutSaving));

    /// <summary>“Create Backup”</summary>
    public static string ButtonCreateBackup => Get(nameof(ButtonCreateBackup));

    /// <summary>“Delete”</summary>
    public static string ButtonDeleteBackup => Get(nameof(ButtonDeleteBackup));

    /// <summary>“Discard and restore”</summary>
    public static string ButtonDiscardAndRestore => Get(nameof(ButtonDiscardAndRestore));

    /// <summary>“Include credentials”</summary>
    public static string ButtonIncludeCredentialsConfirm => Get(nameof(ButtonIncludeCredentialsConfirm));

    /// <summary>“Omit credentials”</summary>
    public static string ButtonOmitCredentials => Get(nameof(ButtonOmitCredentials));

    /// <summary>“Restore”</summary>
    public static string ButtonRestore => Get(nameof(ButtonRestore));

    /// <summary>“Restore anyway”</summary>
    public static string ButtonRestoreAnyway => Get(nameof(ButtonRestoreAnyway));

    /// <summary>“Save”</summary>
    public static string ButtonSaveDialog => Get(nameof(ButtonSaveDialog));

    /// <summary>“Share”</summary>
    public static string ButtonShareBackup => Get(nameof(ButtonShareBackup));

    /// <summary>“Backup Failed”</summary>
    public static string DialogTitleBackupFailed => Get(nameof(DialogTitleBackupFailed));

    /// <summary>“Backup Without Saving?”</summary>
    public static string DialogTitleBackupWithoutSaving => Get(nameof(DialogTitleBackupWithoutSaving));

    /// <summary>“Cross-platform restore”</summary>
    public static string DialogTitleCrossPlatformRestore => Get(nameof(DialogTitleCrossPlatformRestore));

    /// <summary>“Discard Unsaved Edits?”</summary>
    public static string DialogTitleDiscardUnsavedEdits => Get(nameof(DialogTitleDiscardUnsavedEdits));

    /// <summary>“Restore from dropped backup?”</summary>
    public static string DialogTitleDropRestore => Get(nameof(DialogTitleDropRestore));

    /// <summary>“Cannot restore from dropped file”</summary>
    public static string DialogTitleDropRestoreInvalid => Get(nameof(DialogTitleDropRestoreInvalid));

    /// <summary>“Include session history and credentials?”</summary>
    public static string DialogTitleIncludeCredentials => Get(nameof(DialogTitleIncludeCredentials));

    /// <summary>“Restore completed — {0} file(s) skipped”</summary>
    public static string DialogTitleRestoreCompletedSkippedFmt => Get(nameof(DialogTitleRestoreCompletedSkippedFmt));

    /// <summary>“Restore Failed”</summary>
    public static string DialogTitleRestoreFailed => Get(nameof(DialogTitleRestoreFailed));

    /// <summary>“Unsaved Changes”</summary>
    public static string DialogTitleUnsavedChanges => Get(nameof(DialogTitleUnsavedChanges));

    /// <summary>“Actions”</summary>
    public static string HeaderBackupActions => Get(nameof(HeaderBackupActions));

    /// <summary>“Clients”</summary>
    public static string HeaderBackupClients => Get(nameof(HeaderBackupClients));

    /// <summary>“Date”</summary>
    public static string HeaderBackupDate => Get(nameof(HeaderBackupDate));

    /// <summary>“File”</summary>
    public static string HeaderBackupFile => Get(nameof(HeaderBackupFile));

    /// <summary>“Mode”</summary>
    public static string HeaderBackupMode => Get(nameof(HeaderBackupMode));

    /// <summary>“Platform”</summary>
    public static string HeaderBackupPlatform => Get(nameof(HeaderBackupPlatform));

    /// <summary>“Size”</summary>
    public static string HeaderBackupSize => Get(nameof(HeaderBackupSize));

    /// <summary>“What backups include”</summary>
    public static string HeadingBackupContents => Get(nameof(HeadingBackupContents));

    /// <summary>“Backup / Restore”</summary>
    public static string HeadingBackupRestore => Get(nameof(HeadingBackupRestore));

    /// <summary>“Includes open project: {0}”</summary>
    public static string LabelBackupIncludesProject => Get(nameof(LabelBackupIncludesProject));

    /// <summary>“User-level config only”</summary>
    public static string LabelBackupNoProjectOpen => Get(nameof(LabelBackupNoProjectOpen));

    /// <summary>“Clients”</summary>
    public static string LabelClients => Get(nameof(LabelClients));

    /// <summary>“backups (0 = keep all)”</summary>
    public static string LabelKeepAll => Get(nameof(LabelKeepAll));

    /// <summary>“Keep last”</summary>
    public static string LabelKeepLast => Get(nameof(LabelKeepLast));

    /// <summary>“Last backup: {0} day(s) ago”</summary>
    public static string LabelLastBackupDaysFmt => Get(nameof(LabelLastBackupDaysFmt));

    /// <summary>“Last backup: {0} hour(s) ago”</summary>
    public static string LabelLastBackupHoursFmt => Get(nameof(LabelLastBackupHoursFmt));

    /// <summary>“Last backup: just now”</summary>
    public static string LabelLastBackupJustNow => Get(nameof(LabelLastBackupJustNow));

    /// <summary>“Last backup: {0} minute(s) ago”</summary>
    public static string LabelLastBackupMinutesFmt => Get(nameof(LabelLastBackupMinutesFmt));

    /// <summary>“No backup yet”</summary>
    public static string LabelLastBackupNever => Get(nameof(LabelLastBackupNever));

    /// <summary>“Output folder”</summary>
    public static string LabelOutputFolder => Get(nameof(LabelOutputFolder));

    /// <summary>“Restore folder”</summary>
    public static string LabelRestoreFolder => Get(nameof(LabelRestoreFolder));

    /// <summary>“Retention”</summary>
    public static string LabelRetention => Get(nameof(LabelRetention));

    /// <summary>“Scope”</summary>
    public static string LabelScope => Get(nameof(LabelScope));

    /// <summary>“Open file location”</summary>
    public static string MenuOpenFileLocation => Get(nameof(MenuOpenFileLocation));

    /// <summary>“Creating junction…”</summary>
    public static string ProgressCreatingJunction => Get(nameof(ProgressCreatingJunction));

    /// <summary>“Preparing…”</summary>
    public static string ProgressPreparing => Get(nameof(ProgressPreparing));

    /// <summary>“Restoring…”</summary>
    public static string ProgressRestoring => Get(nameof(ProgressRestoring));

    /// <summary>“Starting…”</summary>
    public static string ProgressStarting => Get(nameof(ProgressStarting));

    /// <summary>“Sanitized for sharing (secrets redacted — not restorable)”</summary>
    public static string RadioSanitizedBackup => Get(nameof(RadioSanitizedBackup));

    /// <summary>“Backup — everything OpenCode&apos;s config root holds”</summary>
    public static string RadioSettingsOnly => Get(nameof(RadioSettingsOnly));

    /// <summary>“Deleted {0}.”</summary>
    public static string StatusBackupDeletedFmt => Get(nameof(StatusBackupDeletedFmt));

    /// <summary>“Could not delete {0}.”</summary>
    public static string StatusBackupDeleteFailedFmt => Get(nameof(StatusBackupDeleteFailedFmt));

    /// <summary>“[!] {0} warning(s) — see manifest.”</summary>
    public static string StatusBackupWarningsFmt => Get(nameof(StatusBackupWarningsFmt));

    /// <summary>“Choose an output folder first (click Browse next to the folder path).”</summary>
    public static string StatusChooseBackupFolder => Get(nameof(StatusChooseBackupFolder));

    /// <summary>“Could not reveal the backup archive — see the log for details.”</summary>
    public static string StatusShareArchiveFailed => Get(nameof(StatusShareArchiveFailed));

    /// <summary>“Backup archive revealed in your file manager.”</summary>
    public static string StatusShareArchiveRevealed => Get(nameof(StatusShareArchiveRevealed));

    /// <summary>“Sharing the archive is not available on this system.”</summary>
    public static string StatusShareArchiveUnavailable => Get(nameof(StatusShareArchiveUnavailable));

    /// <summary>“[!] OpenCode is currently running ({0} process(es)). Some files may be locked during…”</summary>
    public static string StatusOpenCodeRunningFmt => Get(nameof(StatusOpenCodeRunningFmt));

    /// <summary>“Backup”</summary>
    public static string TabBackup => Get(nameof(TabBackup));

    /// <summary>“Restore”</summary>
    public static string TabRestore => Get(nameof(TabRestore));

    /// <summary>“Never archived: auth.json. Cache, state and logs are regenerable and are left out.”</summary>
    public static string TextBackupContentsExcluded => Get(nameof(TextBackupContentsExcluded));

    /// <summary>“All of ~/.config/opencode/ — opencode.json, tui.json, agents, commands, plugins and…”</summary>
    public static string TextBackupContentsLine1 => Get(nameof(TextBackupContentsLine1));

    /// <summary>“OpenCode&apos;s own .gitignore in that folder decides what is skipped, so node_modules/ a…”</summary>
    public static string TextBackupContentsLine2 => Get(nameof(TextBackupContentsLine2));

    /// <summary>“Optionally opencode.db and its -wal / -shm sidecars — session history, and the acces…”</summary>
    public static string TextBackupContentsLine3 => Get(nameof(TextBackupContentsLine3));

    /// <summary>“Per-project .opencode/ directories live with your repositories and are not archived…”</summary>
    public static string TextBackupContentsProjectNote => Get(nameof(TextBackupContentsProjectNote));

    /// <summary>“Backup archives (.zip) are written to this folder. The restore tab can scan a differ…”</summary>
    public static string TextBackupDirHint => Get(nameof(TextBackupDirHint));

    /// <summary>“Create timestamped backup archives of your OpenCode config and restore from them lat…”</summary>
    public static string TextBackupRestoreSubtitle => Get(nameof(TextBackupRestoreSubtitle));

    /// <summary>“⚠ This mode preserves secrets verbatim. API keys, OAuth tokens, and MCP authorizatio…”</summary>
    public static string TextBackupSecretsWarning => Get(nameof(TextBackupSecretsWarning));

    /// <summary>“Choose a folder above to see available backups.”</summary>
    public static string TextChooseRestoreFolder => Get(nameof(TextChooseRestoreFolder));

    /// <summary>“holds your session history and the OAuth tokens that go with it. Anyone with this ba…”</summary>
    public static string TextCredentialsExplainer => Get(nameof(TextCredentialsExplainer));

    /// <summary>“&apos; but you are on &apos;”</summary>
    public static string TextCrossPlatformRestoreMiddle => Get(nameof(TextCrossPlatformRestoreMiddle));

    /// <summary>“This backup was taken on &apos;”</summary>
    public static string TextCrossPlatformRestorePrefix => Get(nameof(TextCrossPlatformRestorePrefix));

    /// <summary>“&apos;. Some paths inside config files may need manual edits afterward. Continue?”</summary>
    public static string TextCrossPlatformRestoreSuffix => Get(nameof(TextCrossPlatformRestoreSuffix));

    /// <summary>“Restoring will overwrite your current settings. Discard your unsaved edits and conti…”</summary>
    public static string TextDiscardUnsavedForRestore => Get(nameof(TextDiscardUnsavedForRestore));

    /// <summary>“The dropped file is not a valid OpenCodeForge backup archive. It may be corrupt, fro…”</summary>
    public static string TextDropRestoreNotABackup => Get(nameof(TextDropRestoreNotABackup));

    /// <summary>“Only backup .zip files can be dropped here to restore.”</summary>
    public static string TextDropRestoreNotZip => Get(nameof(TextDropRestoreNotZip));

    /// <summary>“Restore from”</summary>
    public static string TextDropRestorePromptPrefix => Get(nameof(TextDropRestorePromptPrefix));

    /// <summary>“? This will overwrite your current settings with the contents of the dropped backup…”</summary>
    public static string TextDropRestorePromptSuffix => Get(nameof(TextDropRestorePromptSuffix));

    /// <summary>“⚠ No output folder selected. Click Browse to choose where backup files will be saved…”</summary>
    public static string TextNoBackupDirWarning => Get(nameof(TextNoBackupDirWarning));

    /// <summary>“Use &apos;Create Backup&apos; to make your first snapshot. Backups are zip files that can be r…”</summary>
    public static string TextNoBackupYetDescription => Get(nameof(TextNoBackupYetDescription));

    /// <summary>“⚠ No folder selected. Click Browse to choose the folder containing your backup archi…”</summary>
    public static string TextNoRestoreDirWarning => Get(nameof(TextNoRestoreDirWarning));

    /// <summary>“Continue with the backup? Your unsaved edits will not be in this backup, but they wi…”</summary>
    public static string TextProceedWithoutSavingBackup => Get(nameof(TextProceedWithoutSavingBackup));

    /// <summary>“Select a backup folder and choose a zip file to restore. This will overwrite your cu…”</summary>
    public static string TextRestoreDescription => Get(nameof(TextRestoreDescription));

    /// <summary>“Select a backup to restore. Existing files will be moved aside as .pre-restore-*.bak…”</summary>
    public static string TextRestoreInstructions => Get(nameof(TextRestoreInstructions));

    /// <summary>“The restore finished but the following file(s) could not be written (they may be loc…”</summary>
    public static string TextRestoreSkippedExplainer => Get(nameof(TextRestoreSkippedExplainer));

    /// <summary>“… and {0} more (see the log for the full list)”</summary>
    public static string TextRestoreSkippedTrailerFmt => Get(nameof(TextRestoreSkippedTrailerFmt));

    /// <summary>“Sanitized backup: secret-bearing values will be replaced with &quot;[redacted]&quot; so the ar…”</summary>
    public static string TextSanitizedModeExplainer => Get(nameof(TextSanitizedModeExplainer));

    /// <summary>“You have unsaved edits in the active workspace. Save them before backing up? Otherwi…”</summary>
    public static string TextSaveBeforeBackupPrompt => Get(nameof(TextSaveBeforeBackupPrompt));

    /// <summary>“You have unsaved edits in the active workspace. Save them before restoring?”</summary>
    public static string TextSaveBeforeRestorePrompt => Get(nameof(TextSaveBeforeRestorePrompt));

    /// <summary>“Cancel the current operation”</summary>
    public static string TipButtonCancel => Get(nameof(TipButtonCancel));

    /// <summary>“Send this backup archive to another app or device via the OS share panel”</summary>
    public static string TipButtonShareBackup => Get(nameof(TipButtonShareBackup));

    /// <summary>“Restore overwrites your live settings with this backup (existing files are backed up…”</summary>
    public static string TipHeaderBackupActions => Get(nameof(TipHeaderBackupActions));

    /// <summary>“Which OpenCode configs were included: OpenCode and/or OpenCode TUI.”</summary>
    public static string TipHeaderBackupClients => Get(nameof(TipHeaderBackupClients));

    /// <summary>“Date and time this backup was created (local time)”</summary>
    public static string TipHeaderBackupDate => Get(nameof(TipHeaderBackupDate));

    /// <summary>“Name of the backup archive file on disk”</summary>
    public static string TipHeaderBackupFile => Get(nameof(TipHeaderBackupFile));

    /// <summary>“Backup scope — Backup captures everything under ~/.config/opencode/; Sanitized redac…”</summary>
    public static string TipHeaderBackupMode => Get(nameof(TipHeaderBackupMode));

    /// <summary>“OS platform that created this backup: windows, macos, or linux. Restoring a backup f…”</summary>
    public static string TipHeaderBackupPlatform => Get(nameof(TipHeaderBackupPlatform));

    /// <summary>“Compressed size of the backup archive”</summary>
    public static string TipHeaderBackupSize => Get(nameof(TipHeaderBackupSize));

    /// <summary>“Same file scope as a normal backup, but every *.json value whose key matches the sen…”</summary>
    public static string TipRadioSanitizedBackup => Get(nameof(TipRadioSanitizedBackup));

    /// <summary>“No folder chosen — click Browse to set one”</summary>
    public static string WatermarkBackupDir => Get(nameof(WatermarkBackupDir));

    /// <summary>“No folder chosen — click Browse to locate backups”</summary>
    public static string WatermarkRestoreDir => Get(nameof(WatermarkRestoreDir));

    // ── Disk footprint page ───────────────────────────────────────────────────
    // The six category labels and their six tooltips are keyed by
    // FootprintCategory.Id in OpenCodeFootprintRowViewModel, not by position. A category
    // added to OpenCodeFootprint.Catalog without a pair here renders its PascalCase id.

    /// <summary>“Re-measure”</summary>
    public static string ButtonFootprintRefresh => Get(nameof(ButtonFootprintRefresh));

    /// <summary>“Reveal”</summary>
    public static string ButtonFootprintReveal => Get(nameof(ButtonFootprintReveal));

    /// <summary>“Disk footprint” — the page heading AND the navigation node's title.</summary>
    public static string HeadingFootprint => Get(nameof(HeadingFootprint));

    /// <summary>“None of this is in an ordinary backup”</summary>
    public static string HeadingFootprintNotBackedUp => Get(nameof(HeadingFootprintNotBackedUp));

    /// <summary>“Interrupted downloads”</summary>
    public static string LabelFootprintDownloadTemps => Get(nameof(LabelFootprintDownloadTemps));

    /// <summary>“Cannot be regenerated” — badge on the session-database row only.</summary>
    public static string LabelFootprintIrreplaceable => Get(nameof(LabelFootprintIrreplaceable));

    /// <summary>“Session locks”</summary>
    public static string LabelFootprintLocks => Get(nameof(LabelFootprintLocks));

    /// <summary>“Logs”</summary>
    public static string LabelFootprintLogs => Get(nameof(LabelFootprintLogs));

    /// <summary>“Model catalogue cache”</summary>
    public static string LabelFootprintModelCatalog => Get(nameof(LabelFootprintModelCatalog));

    /// <summary>“Plugin packages (node_modules)”</summary>
    public static string LabelFootprintNodeModules => Get(nameof(LabelFootprintNodeModules));

    /// <summary>“Session database”</summary>
    public static string LabelFootprintSessionDatabase => Get(nameof(LabelFootprintSessionDatabase));

    /// <summary>“{0} across {1} categories”</summary>
    public static string LabelFootprintTotalFmt => Get(nameof(LabelFootprintTotalFmt));

    /// <summary>“Measuring…”</summary>
    public static string TextFootprintMeasuring => Get(nameof(TextFootprintMeasuring));

    /// <summary>“Five of these six categories sit outside the config folder a backup archives…”</summary>
    public static string TextFootprintNotBackedUp => Get(nameof(TextFootprintNotBackedUp));

    /// <summary>“What OpenCode has left on this machine, listed most-disposable first…”</summary>
    public static string TextFootprintSubtitle => Get(nameof(TextFootprintSubtitle));

    /// <summary>“Leftovers from downloads that did not finish…”</summary>
    public static string TipFootprintDownloadTemps => Get(nameof(TipFootprintDownloadTemps));

    /// <summary>“One folder per lock OpenCode is holding, each with a heartbeat file…”</summary>
    public static string TipFootprintLocks => Get(nameof(TipFootprintLocks));

    /// <summary>“OpenCode's own run logs…”</summary>
    public static string TipFootprintLogs => Get(nameof(TipFootprintLogs));

    /// <summary>“The cached list of every model OpenCode knows how to talk to…”</summary>
    public static string TipFootprintModelCatalog => Get(nameof(TipFootprintModelCatalog));

    /// <summary>“Packages OpenCode unpacks so it can resolve plugin imports…”</summary>
    public static string TipFootprintNodeModules => Get(nameof(TipFootprintNodeModules));

    /// <summary>“Every session, message and part OpenCode has recorded…”</summary>
    public static string TipFootprintSessionDatabase => Get(nameof(TipFootprintSessionDatabase));
}
