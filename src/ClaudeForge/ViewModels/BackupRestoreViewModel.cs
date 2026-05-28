using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Top-level ViewModel for the "Backup / Restore" navigation section.
/// Coordinates the <see cref="BackupEngine"/> with the GUI, enforces the dirty-workspace
/// guard before any restore, manages the credentials-prompt preference, and raises
/// <see cref="RestoreCompleted"/> so <see cref="MainWindowViewModel"/> can reload afterwards.
/// </summary>
/// <remarks>
/// The VM is self-contained: all long-running operations are guarded by
/// <see cref="IsBusy"/> and flow cancellation through a single <see cref="CancellationTokenSource"/>.
/// Progress is reported through <see cref="ProgressPercent"/> and <see cref="ProgressMessage"/>
/// so the view can bind to them directly.
/// </remarks>
public partial class BackupRestoreViewModel : ObservableObject, IDisposable
{
    private bool _vmDisposed;
    private readonly IDialogService _dialogService;
    private readonly IShareService? _shareService;

    /// <summary>
    /// Source for the cancellation token passed to the current Backup or Restore
    /// operation.  Created just before each long-running call and cleared in the
    /// finally block.  <see cref="CancelOperationCommand"/> cancels through this.
    /// </summary>
    private CancellationTokenSource? _operationCts;

    /// <summary>
    /// Invoked by the host ViewModel to check whether any workspace has unsaved edits —
    /// the dirty-guard calls this before a restore to prompt the user.
    /// </summary>
    public Func<bool>? IsAnyWorkspaceDirty { get; set; }

    /// <summary>Invoked after a successful restore so the host can reload workspaces.</summary>
    public Func<Task>? OnRestoreCompleted { get; set; }

    /// <summary>
    /// host hook for routing user-visible terminal outcomes
    /// (backup created, backup deleted, restore complete, …) through the
    /// centre status bar pill in addition to the page-local
    /// <see cref="StatusMessage"/>.  Three params: the message text, an
    /// <c>isFailure</c> flag (false → green ✓ Success pill, true → red ✗
    /// Failure pill), and a label kept short enough for the pill (~120
    /// chars; the controller truncates further if needed).
    /// </summary>
    /// <remarks>
    /// Pre-fix, the BackupRestoreVM only set its page-local
    /// <c>StatusMessage</c>; the centre pill never fired on
    /// backup/restore outcomes.  Users on other nav nodes therefore
    /// missed the success signal entirely.  The host (MainWindowViewModel)
    /// wires this to <c>SetStatusSuccess</c> / <c>SetStatusFailure</c> so
    /// the pill lifecycle (auto-clear for Success, manual dismiss for
    /// Failure) Just Works.
    /// </remarks>
    public Action<string, bool /* isFailure */>? OnTerminalStatus { get; set; }

    /// <summary>
    /// Invoked before a Backup OR Restore when there are dirty workspaces —
    /// should Save them.  The boolean argument is <c>isRestoreContext</c>: pass
    /// <see langword="true"/> when this Save is being performed as part of a
    /// pre-restore guard so the SaveDialog uses restore-themed labels
    /// ("Restore Preview" / "Restoring N changes" / "will be restored to"); pass
    /// <see langword="false"/> for a pre-backup guard so the dialog stays in
    /// regular save terminology ("Save Changes" / "Saving N changes" / "will
    /// be written to").
    /// </summary>
    /// <remarks>
    /// split from the prior single-context callback.  Previously
    /// the host always wired this to <c>SaveForRestoreAsync</c> which forced
    /// restore-themed labels even when the user was about to back up.
    /// </remarks>
    public Func<bool /* isRestoreContext */, Task>? SaveAllWorkspaces { get; set; }

    /// <summary>
    /// Bridge properties set by the host (<see cref="MainWindowViewModel"/>) before
    /// <see cref="Refresh"/> is called.  Values come from the persisted
    /// <c>ClaudeForge-gui-state.json</c> via <c>WindowStateService</c>.
    /// </summary>
    public bool? CredentialsPreference { get; set; }

    public DateTime? LastBackupUtc { get; set; }

    /// <summary>
    /// Initial backup output folder loaded from persisted state.
    /// Null / empty means the user has never configured it — Backup is blocked until set.
    /// </summary>
    public string? InitialBackupDirectory { get; set; }

    /// <summary>
    /// Initial restore scan folder loaded from persisted state.
    /// Null / empty means the user has never configured it — Restore list is hidden until set.
    /// </summary>
    public string? InitialRestoreDirectory { get; set; }

    /// <summary>
    /// Absolute path of the currently-open project (i.e. <c>MainWindowViewModel.ProjectRoot</c>),
    /// or <see langword="null"/> when no project is open.  Seeded by the host in the
    /// ctor initializer and re-pushed by <c>MainWindowViewModel.OnProjectRootChanged</c>
    /// so mid-session project switches are reflected immediately.
    ///
    /// <para>
    /// Threaded into <see cref="BackupRequest.ExplicitProjectDirs"/> in
    /// <see cref="CreateBackupAsync"/>.  Pre-fix, that field was always empty —
    /// <see cref="BackupEngine.AddProjectClaudeData"/> is only invoked for items in
    /// the list, so the open project's <c>.claude</c> directory (settings.json,
    /// settings.local.json, hooks, MCP config, permissions, …) was silently absent
    /// from every backup archive the app produced.
    /// </para>
    ///
    /// <para>
    /// Diverges from the plain <c>{ get; set; }</c> shape of the other <c>Initial*</c>
    /// properties: this one is an <see cref="ObservableProperty"/> so the Backup-tab
    /// inclusion label can react to mid-session project switches without requiring an
    /// explicit <see cref="Refresh"/> call.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenProjectName))]
    [NotifyPropertyChangedFor(nameof(BackupIncludesProjectLabel))]
    private string? _initialProjectRoot;

    /// <summary>
    /// Bare folder name of <see cref="InitialProjectRoot"/> for display purposes —
    /// e.g. <c>C:\repos\MyApp</c> → <c>MyApp</c>.  Returns <see langword="null"/>
    /// when no project is open.
    /// </summary>
    public string? OpenProjectName
    {
        get
        {
            string? root = InitialProjectRoot?.Trim();
            return string.IsNullOrEmpty(root) ? null : Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    /// <summary>
    /// Human-readable label rendered on the Backup tab so the user knows whether
    /// the currently-open project's <c>.claude</c> directory will be included in
    /// the next backup.  Two shapes:
    /// <list type="bullet">
    ///   <item>Project open  → "Includes open project: {name}" (resx: <c>LabelBackupIncludesProject</c>)</item>
    ///   <item>No project    → "No project open — user-level config only" (resx: <c>LabelBackupNoProjectOpen</c>)</item>
    /// </list>
    /// </summary>
    public string BackupIncludesProjectLabel =>
        string.IsNullOrEmpty(OpenProjectName)
            ? Strings.LabelBackupNoProjectOpen
            : string.Format(CultureInfo.CurrentCulture, Strings.LabelBackupIncludesProject, OpenProjectName);

    /// <summary>Raised when either persisted value changes so the host can flush state.</summary>
    public event EventHandler? PersistentStateChanged;

    // Suppresses side-effect partial-methods during Refresh() initialisation so
    // PersistentStateChanged and RebuildBackupList are not invoked redundantly.
    private bool _initialized;

    public BackupRestoreViewModel(IDialogService dialogService, IShareService? shareService = null)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _shareService = shareService;
        Backups = new ObservableCollection<BackupRowViewModel>();
        _backupDirectory = string.Empty; // deliberately no default — user must choose
        _restoreDirectory = string.Empty;
        _mode = BackupMode.SettingsOnly;
        _includeClaudeCode = true;
        _includeClaudeDesktop = true;
        _keepLast = 7;
    }

    // -----------------------------------------------------------------------
    //  Observable state
    // -----------------------------------------------------------------------

    public ObservableCollection<BackupRowViewModel> Backups { get; }

    /// <summary>Folder where new backup .zip files are written. Empty = not yet configured.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCreateBackup))]
    private string _backupDirectory;

    /// <summary>Folder the Restore tab scans for existing archives. Empty = not yet configured.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasRestoreDirectory))]
    private string _restoreDirectory;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowSecretsWarning))]
    private BackupMode _mode;

    [ObservableProperty] private bool _includeClaudeCode;
    [ObservableProperty] private bool _includeClaudeDesktop;

    /// <summary>
    /// True when the selected mode does NOT scrub secrets — i.e. SettingsOnly
    /// or Full — so the View shows a one-line advisory that API keys, OAuth
    /// tokens, and MCP auth headers DO travel inside the archive in
    /// plaintext.  Sanitized mode suppresses the warning because the
    /// redactor scrubs those values.
    /// </summary>
    public bool ShowSecretsWarning => Mode != BackupMode.Sanitized;

    /// <summary>
    /// UI-bound mirror of <see cref="CredentialsPreference"/>. Bound to a checkbox on the
    /// Backup tab. When the VM loads the first time with no remembered preference (null),
    /// this starts at <c>false</c> (safe default), but the confirmation prompt still fires
    /// on the first Backup click until the user answers.
    /// </summary>
    [ObservableProperty] private bool _includeCredentials;

    [ObservableProperty] private int _keepLast;

    /// <summary>
    /// True while a backup, restore, or MSIX-fix operation is running.
    /// The check-and-set in each async command is effectively atomic because Avalonia
    /// <c>[RelayCommand]</c>-bound operations execute on the UI thread — no SemaphoreSlim
    /// needed. The bool pattern matches the codebase-wide convention.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCreateBackup))]
    private bool _isBusy;

    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string? _progressMessage;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Cancels the active Backup or Restore operation.</summary>
    [RelayCommand]
    private void CancelOperation()
    {
        Log.Information("[Backup.Command] action=CancelOperation");
        _operationCts?.Cancel();
    }

    /// <summary>
    /// The currently selected row in the Restore DataGrid.
    /// Two-way bound to <c>DataGrid.SelectedItem</c> so the right-click context menu
    /// always acts on the row under the pointer.
    /// </summary>
    [ObservableProperty] private BackupRowViewModel? _selectedBackup;

    /// <summary>True when both IsBusy is false AND a backup folder has been configured.</summary>
    public bool CanCreateBackup => !IsBusy && !string.IsNullOrEmpty(BackupDirectory);

    /// <summary>True when a restore folder has been configured. Controls Restore tab visibility.</summary>
    public bool HasRestoreDirectory => !string.IsNullOrEmpty(RestoreDirectory);

    /// <summary>"Last backup: N days ago" style string for the banner.</summary>
    public string LastBackupLabel =>
        LastBackupUtc is null
            ? Strings.LabelLastBackupNever
            : FormatAgo(DateTime.UtcNow - LastBackupUtc.Value);

    /// <summary>True when the user has never run a backup; drives the descriptive hint in the banner.</summary>
    public bool HasNeverBackedUp => LastBackupUtc is null;

    /// <summary>Current MSIX status (Windows-only). Null on other platforms.</summary>
    [ObservableProperty] private MsixStatus? _msixStatus;

    public bool ShowMsixTab => OperatingSystem.IsWindows() && MsixStatus?.NeedsFix == true;

    partial void OnMsixStatusChanged(MsixStatus? value)
    {
        OnPropertyChanged(nameof(ShowMsixTab));
    }

    // -----------------------------------------------------------------------
    //  Initialisation — called by the host after construction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Populates the Restore list, applies the remembered credential preference, and
    /// probes MSIX state. Safe to call multiple times (idempotent).
    /// </summary>
    public void Refresh()
    {
        // Suppress side-effect partial methods while we seed from the host bridge values
        // so PersistentStateChanged and RebuildBackupList fire only once at the end.
        _initialized = false;
        BackupDirectory = InitialBackupDirectory ?? string.Empty;
        RestoreDirectory = InitialRestoreDirectory ?? string.Empty;
        IncludeCredentials = CredentialsPreference ?? false; // seed before _initialized = true
        _initialized = true;

        OnPropertyChanged(nameof(CanCreateBackup));
        OnPropertyChanged(nameof(HasRestoreDirectory));

        RebuildBackupList();
        MsixStatus = OperatingSystem.IsWindows() ? MsixPathProbe.Instance.Probe() : null;
        OnPropertyChanged(nameof(LastBackupLabel));
    }

    private void RebuildBackupList()
    {
        // BackupEngine.Default.List opens every backup zip
        // in the directory and parses each manifest.json synchronously.
        // For users with many backups, this surfaced as a UI freeze on
        // every profile switch (Refresh() is called during BuildNavigationTree
        // → BackupRestoreViewModel construction, on the UI thread).
        //
        // Move the I/O onto a background thread; marshal back to UI thread
        // before mutating the Backups ObservableCollection. The list is
        // cleared synchronously so the UI shows an empty state during the
        // scan rather than stale entries.
        Backups.Clear();
        if (string.IsNullOrEmpty(RestoreDirectory))
        {
            return;
        }

        string captured = RestoreDirectory;
        _ = RebuildBackupListAsync(captured);
    }

    private async Task RebuildBackupListAsync(string restoreDirectory)
    {
        try
        {
            if (!Directory.Exists(restoreDirectory))
            {
                return;
            }

            // Off-UI-thread: scan + parse manifests.
            IReadOnlyList<BackupEntry> entries = await Task.Run(() =>
                BackupEngine.Default.List(restoreDirectory));

            // Marshal back to UI thread before touching the collection.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Defensive: if RestoreDirectory changed while we were
                // scanning (user switched profiles fast, or picked a new
                // dir), drop these results — a fresh scan was queued.
                if (!string.Equals(RestoreDirectory, restoreDirectory, StringComparison.Ordinal))
                {
                    return;
                }

                Backups.Clear(); // re-clear in case anything raced in
                foreach (BackupEntry entry in entries)
                {
                    Backups.Add(new BackupRowViewModel(entry));
                }

                // If the host did not provide a persisted last-backup
                // timestamp (e.g. first run after the feature was added,
                // or state file was deleted), infer it from the most-
                // recent backup in the restore directory so the label
                // reads correctly.
                if (Backups.Count > 0 && LastBackupUtc == null)
                {
                    LastBackupUtc = Backups.Max(b => b.Entry.LastModifiedUtc);
                    OnPropertyChanged(nameof(LastBackupLabel));
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "[Backup] Cannot read restore directory {Dir}", restoreDirectory);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Cannot read restore directory: {ex.Message}";
            });
        }
    }

    // -----------------------------------------------------------------------
    //  Directory picker commands
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lets the user pick the folder where new backup archives are written.
    /// If neither directory has been set yet (first-time setup), also pre-fills the
    /// Restore directory with the same choice so the user doesn't need to configure
    /// both separately for the common case where they're the same folder.
    /// </summary>
    [RelayCommand]
    private async Task BrowseBackupDirectoryAsync()
    {
        string? picked = await _dialogService.PickFolderAsync("Select backup output folder");
        if (picked == null)
        {
            return;
        }

        bool isFirstSet = string.IsNullOrEmpty(BackupDirectory) && string.IsNullOrEmpty(RestoreDirectory);
        BackupDirectory = picked;
        if (isFirstSet)
        {
            RestoreDirectory = picked; // mirror on first set; diverges from here if user changes either
        }
    }

    /// <summary>Lets the user pick a different folder to scan for existing backup archives.</summary>
    [RelayCommand]
    private async Task BrowseRestoreDirectoryAsync()
    {
        string? picked = await _dialogService.PickFolderAsync("Select folder to scan for backups");
        if (picked == null)
        {
            return;
        }

        bool isFirstSet = string.IsNullOrEmpty(BackupDirectory) && string.IsNullOrEmpty(RestoreDirectory);
        RestoreDirectory = picked;
        if (isFirstSet)
        {
            BackupDirectory = picked;
        }
    }

    partial void OnBackupDirectoryChanged(string value)
    {
        if (!_initialized)
        {
            return;
        }

        Log.Information("[Backup.UserEdit] BackupDirectory={Path}", value);
        PersistentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnRestoreDirectoryChanged(string value)
    {
        if (!_initialized)
        {
            return;
        }

        Log.Information("[Backup.UserEdit] RestoreDirectory={Path}", value);
        PersistentStateChanged?.Invoke(this, EventArgs.Empty);
        RebuildBackupList();
    }

    // -----------------------------------------------------------------------
    //  Backup / Restore / Delete
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task BackupAsync()
    {
        Log.Information("[Backup.Command] action=Backup destination={Dir} includeCredentials={Creds}",
            BackupDirectory, IncludeCredentials);
        // Set IsBusy FIRST so a second click during the credentials-prompt await
        // short-circuits on the next line rather than racing into a parallel backup.
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrEmpty(BackupDirectory))
        {
            StatusMessage = Strings.StatusChooseBackupFolder;
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;
        ProgressMessage = Strings.ProgressPreparing;
        StatusMessage = null;

        try
        {
            // dirty-guard for Backup. Mirrors the same pattern in
            // RestoreAsync (line ~393). Without this prompt, a user who edits
            // a setting and then clicks Backup gets a backup that does NOT
            // contain their unsaved edits — the backup zips only what's on
            // disk. The user typically expects "back up my current state",
            // so silently excluding in-memory edits is a footgun.
            //
            // Semantically different from Restore: backup does not DESTROY
            // unsaved edits, it just excludes them. So the second-prompt
            // copy is "continue without saving" rather than "discard".
            if (IsAnyWorkspaceDirty?.Invoke() == true)
            {
                DialogMessage saveFirstMsg = DialogMessage.Plain(Strings.TextSaveBeforeBackupPrompt);
                bool? saveFirst = await _dialogService.ShowConfirmAsync(
                    Strings.DialogTitleUnsavedChanges, saveFirstMsg,
                    confirmLabel: Strings.ButtonSaveDialog);
                // three-valued: null (X close) aborts the
                // whole backup; false (cancel = "don't save first") falls
                // through to the proceed-without-saving prompt.
                if (saveFirst is null)
                {
                    return;
                }

                if (saveFirst == true)
                {
                    // Backup context — pass isRestoreContext: false so the
                    // SaveDialog title says "Save Changes" / "Saving N changes"
                    // not "Restore Preview" / "Restoring N changes".
                    if (SaveAllWorkspaces is { } save)
                    {
                        await save( /* isRestoreContext: */ false);
                    }
                }
                else
                {
                    DialogMessage proceedMsg = DialogMessage.Plain(Strings.TextProceedWithoutSavingBackup);
                    bool? proceedAnyway = await _dialogService.ShowConfirmAsync(
                        Strings.DialogTitleBackupWithoutSaving, proceedMsg,
                        confirmLabel: Strings.ButtonContinueWithoutSaving);
                    // Both Cancel (false) and X (null) abort the backup at
                    // this stage — the only "proceed" answer is the explicit
                    // confirm button.
                    if (proceedAnyway != true)
                    {
                        return;
                    }
                }
            }

            // Credentials prompt is now ALWAYS shown for
            // non-Sanitized backups (the prior once-and-remember pattern
            // surprised users who'd hastily clicked through once and then
            // never got a chance to reconsider).  Sanitized mode hard-
            // drops credentials regardless of the user's answer, so we
            // skip the prompt entirely in that mode.
            //
            // The page-local IncludeCredentials checkbox was removed — its
            // tooltip text now lives inside the dialog body alongside the
            // existing TextCredentialsExplainer copy.
            if (Mode != BackupMode.Sanitized)
            {
                DialogMessage credMsg = DialogMessage.Builder()
                                                     .Path("~/.claude/.credentials.json")
                                                     .Text(Strings.TextCredentialsExplainer)
                                                     .Build();
                bool? include = await _dialogService.ShowConfirmAsync(
                    Strings.DialogTitleIncludeCredentials, credMsg,
                    confirmLabel: Strings.ButtonIncludeCredentialsConfirm,
                    cancelLabel: Strings.ButtonOmitCredentials);
                // trinary prompt: Include / Omit / X-to-abort.
                // null = X close means "I don't want this backup at all" —
                // abort entirely.  The pre-fix bug was that X collapsed to
                // false (= "Omit") which proceeded with the backup WITHOUT
                // credentials.  Now Include and Omit are the only two
                // ways to proceed.
                if (include is null)
                {
                    return;
                }

                IncludeCredentials = include.Value;
            }
            else
            {
                // Force-clear so Sanitized backups never carry credentials,
                // even if a prior non-Sanitized backup left the flag true.
                IncludeCredentials = false;
            }

            try
            {
                Directory.CreateDirectory(BackupDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "[Backup] Cannot create backup directory {Dir}", BackupDirectory);
                StatusMessage = null;
                DialogMessage errMsg = DialogMessage.Builder()
                                                    .Text("Cannot create backup folder ")
                                                    .Path(BackupDirectory)
                                                    .Text(":\n\n")
                                                    .Text(ex.Message)
                                                    .Build();
                await _dialogService.ShowAlertAsync(Strings.DialogTitleBackupFailed, errMsg, DialogCategory.Error);
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string prefix = IncludeCredentials ? "backup-with-creds" : "backup";
            string destPath = Path.Combine(BackupDirectory, $"{prefix}-{stamp}.zip");

            BackupRequest request = BuildBackupRequest(destPath);

            ProgressMessage = Strings.ProgressStarting;
            Progress<BackupProgress> progress = new(p =>
            {
                ProgressMessage = SanitiseProgressItem(p.CurrentItem);
                ProgressPercent = p.Total > 0 ? (double)p.Current / p.Total * 100.0 : 0;
            });

            using CancellationTokenSource cts = new();
            _operationCts = cts;
            BackupResult result = await BackupEngine.Default.CreateAsync(request, progress, cts.Token);
            if (result.Succeeded)
            {
                StatusMessage = result.Message;
                if (result.Manifest?.Warnings.Count > 0)
                {
                    StatusMessage += string.Format(Strings.StatusBackupWarningsFmt, result.Manifest.Warnings.Count);
                }

                LastBackupUtc = DateTime.UtcNow;
                OnPropertyChanged(nameof(LastBackupLabel));
                PersistentStateChanged?.Invoke(this, EventArgs.Empty);
                RebuildBackupList();
                // also route through the centre status bar
                // pill so users on other nav nodes see the green ✓ Success
                // signal (auto-clears after ~6 s).  The page-local
                // StatusMessage above stays for the in-page status row.
                OnTerminalStatus?.Invoke(StatusMessage ?? result.Message, /* isFailure: */ false);
            }
            else if (result.Message != "Backup cancelled.")
            {
                StatusMessage = null;
                // Failure path: surface via the modal dialog AND the centre
                // status bar pill (sticky until dismissed).
                OnTerminalStatus?.Invoke(result.Message, /* isFailure: */ true);
                await _dialogService.ShowAlertAsync(Strings.DialogTitleBackupFailed,
                    DialogMessage.Plain(result.Message), DialogCategory.Error);
            }
        }
        finally
        {
            _operationCts = null;
            IsBusy = false;
            ProgressPercent = 0;
            ProgressMessage = null;
        }
    }

    /// <summary>
    /// Builds the <see cref="BackupRequest"/> from the current VM state for the
    /// given destination archive path.  Extracted as an <c>internal</c> seam so
    /// the test project can verify the field-projection contract directly
    /// without spinning up a real <see cref="BackupEngine"/> or filesystem
    /// sandbox.
    ///
    /// <para>
    /// Pre-existing call site: <see cref="BackupAsync"/> at the request-build
    /// step (formerly an inline initializer).  Lifting the construction into
    /// a named method also documents the field mapping in one place — every
    /// future addition to <see cref="BackupRequest"/> that the VM should drive
    /// goes here.
    /// </para>
    /// </summary>
    internal BackupRequest BuildBackupRequest(string destinationZipPath) => new()
    {
        DestinationZipPath = destinationZipPath,
        Mode = Mode,
        IncludeClaudeCode = IncludeClaudeCode,
        IncludeClaudeDesktop = IncludeClaudeDesktop,
        IncludeCredentials = IncludeCredentials,
        KeepLast = KeepLast,

        // Open project's `.claude` directory (settings.json,
        // settings.local.json, hooks, MCP config, permissions, …) is added
        // to the archive by `BackupEngine.AddProjectClaudeData`, which is
        // ONLY invoked for items in this list.  Pre-fix the field was
        // always `Array.Empty<string>()`, so the project's own settings
        // were silently absent from every backup the app produced.
        ExplicitProjectDirs = string.IsNullOrEmpty(InitialProjectRoot)
            ? Array.Empty<string>()
            : new[] { InitialProjectRoot },
    };

    [RelayCommand]
    private async Task RestoreAsync(BackupRowViewModel row)
    {
        if (row?.Entry is null || IsBusy)
        {
            return;
        }

        if (row.Entry.IsCorrupt)
        {
            return;
        }

        Log.Information("[Backup.Command] action=Restore source={Path} lastModifiedUtc={Ts}",
            row.Entry.ArchivePath, row.Entry.LastModifiedUtc);

        // Set IsBusy FIRST so later confirm dialogs cannot be raced past by a second click.
        IsBusy = true;
        ProgressPercent = 0;
        ProgressMessage = Strings.ProgressPreparing;
        StatusMessage = null;

        try
        {
            // Dirty-guard: sequential two-prompt flow to avoid extending IDialogService.
            if (IsAnyWorkspaceDirty?.Invoke() == true)
            {
                bool? saveFirst = await _dialogService.ShowConfirmAsync(
                    Strings.DialogTitleUnsavedChanges,
                    DialogMessage.Plain(Strings.TextSaveBeforeRestorePrompt),
                    confirmLabel: Strings.ButtonSaveDialog);
                // trinary: null (X) aborts the restore;
                // false (Cancel = "don't save first") falls through to the
                // discard prompt.
                if (saveFirst is null)
                {
                    return;
                }

                if (saveFirst == true)
                {
                    // Restore context — pass isRestoreContext: true so the
                    // SaveDialog uses restore-themed labels.
                    if (SaveAllWorkspaces is { } save)
                    {
                        await save( /* isRestoreContext: */ true);
                    }
                }
                else
                {
                    DialogMessage discardMsg = DialogMessage.Plain(Strings.TextDiscardUnsavedForRestore);
                    bool? discard = await _dialogService.ShowConfirmAsync(
                        Strings.DialogTitleDiscardUnsavedEdits, discardMsg, DialogCategory.Destructive,
                        confirmLabel: Strings.ButtonDiscardAndRestore);
                    if (discard != true)
                    {
                        return;
                    }
                }
            }

            // Cross-platform warning (non-blocking — we still let the user proceed).
            if (row.Entry.IsCrossPlatform)
            {
                string srcPlatform = row.Entry.Manifest?.Platform ?? "unknown";
                DialogMessage crossMsg = DialogMessage.Builder()
                                                      .Text(Strings.TextCrossPlatformRestorePrefix).Bold(srcPlatform)
                                                      .Text(Strings.TextCrossPlatformRestoreMiddle)
                                                      .Bold(PlatformPaths.PlatformId)
                                                      .Text(Strings.TextCrossPlatformRestoreSuffix)
                                                      .Build();
                bool? proceed = await _dialogService.ShowConfirmAsync(
                    Strings.DialogTitleCrossPlatformRestore, crossMsg,
                    confirmLabel: Strings.ButtonRestoreAnyway);
                if (proceed != true)
                {
                    return;
                }
            }

            // Non-blocking pre-restore warning if Claude is running — files may be locked.
            CheckForRunningClaudeProcesses();

            ProgressMessage = Strings.ProgressRestoring;
            Progress<BackupProgress> progress = new(p =>
            {
                ProgressMessage = SanitiseProgressItem(p.CurrentItem);
                ProgressPercent = p.Total > 0 ? (double)p.Current / p.Total * 100.0 : 0;
            });

            using CancellationTokenSource cts = new();
            _operationCts = cts;
            RestoreResult result = await BackupEngine.Default.RestoreAsync(row.Entry, progress, cts.Token);
            if (result.Succeeded)
            {
                StatusMessage = result.Message;

                // Per-file failures: individual files that were locked or inaccessible
                // during restore. The restore still succeeded for all other files — we
                // log the failures and show a non-blocking aggregate alert so the user
                // knows which files they may need to copy manually.
                if (result.FileFailures is { Count: > 0 } fileFailures)
                {
                    Log.Warning(
                        "[Restore] {Count} file(s) could not be restored from {Archive} (locked or inaccessible):",
                        fileFailures.Count, row.Entry.FileName);
                    foreach (string f in fileFailures)
                    {
                        Log.Warning("[Restore]   {File}", f);
                    }

                    const int maxShown = 10;
                    string listed = string.Join("\n", fileFailures.Take(maxShown).Select(f => $"  • {f}"));
                    string trailer = fileFailures.Count > maxShown
                        ? string.Format(Strings.TextRestoreSkippedTrailerFmt, fileFailures.Count - maxShown)
                        : string.Empty;
                    DialogMessage skippedMsg = DialogMessage.Builder()
                                                            .Text(Strings.TextRestoreSkippedExplainer)
                                                            .Text(listed).Text(trailer)
                                                            .Build();
                    await _dialogService.ShowAlertAsync(
                        string.Format(Strings.DialogTitleRestoreCompletedSkippedFmt, fileFailures.Count),
                        skippedMsg);
                }

                if (result.ValidationWarnings is { Count: > 0 } warnings)
                {
                    Log.Warning("[Restore] {Count} schema issue(s) found in restored configs (backup: {Archive}):",
                        warnings.Count, row.Entry.FileName);
                    foreach (string w in warnings)
                    {
                        Log.Warning("[Restore]   {Issue}", w);
                    }
                }

                if (OnRestoreCompleted is { } reload)
                {
                    try
                    {
                        await reload();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Restore] OnRestoreCompleted callback threw an exception");
                    }
                }
            }
            else if (result.Message != "Restore cancelled.")
            {
                StatusMessage = null;
                await _dialogService.ShowAlertAsync(Strings.DialogTitleRestoreFailed,
                    DialogMessage.Plain(result.Message), DialogCategory.Error);
            }
        }
        finally
        {
            _operationCts = null;
            IsBusy = false;
            ProgressPercent = 0;
            ProgressMessage = null;
        }
    }

    // -------------------------------------------------------------------------
    // Drag-drop restore (W8 — 2026-05-15)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Entry point for the drag-drop restore flow: the user drops a backup zip
    /// onto the Restore tab and this method validates the file, prompts for
    /// confirmation, and routes to the existing <see cref="RestoreCommand"/>
    /// with a synthesised <see cref="BackupRowViewModel"/>.  The dropped file
    /// does NOT need to live in the user's configured restore directory — that
    /// is the whole point of the gesture (restore-from-anywhere).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Validation steps, refuse-with-message on any failure:
    /// </para>
    /// <list type="number">
    ///   <item>File exists and is a <c>.zip</c>.</item>
    ///   <item><see cref="BackupEngine.TryReadEntry"/> returns a non-corrupt
    ///   entry (a valid <c>manifest.json</c> of <c>Kind="backup"</c>).</item>
    ///   <item>Confirm prompt — explicit user click required.  X-close on the
    ///   prompt aborts (universal X-dismiss principle from 2026-05-15).</item>
    /// </list>
    /// <para>
    /// Sanitized-mode archives are allowed through to <c>RestoreCommand</c> —
    /// it will refuse them at the engine layer with the same message a
    /// click-from-list restore would, so users get one consistent error path.
    /// </para>
    /// <para>
    /// Exposed as <see cref="RestoreFromDroppedArchiveCommand"/> via the
    /// <see cref="RelayCommandAttribute"/> source generator so AXAML
    /// <c>FileDrop.DropCommand="{Binding RestoreFromDroppedArchiveCommand}"</c>
    /// can wire it directly without a code-behind handler.  The public async
    /// <c>Async</c> method is kept for VM-level tests that don't want the
    /// generated wrapper's fire-and-forget behaviour.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public async Task RestoreFromDroppedArchiveAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        Log.Information("[Backup.Command] action=DropRestore path={Path}", archivePath);

        // Validate extension first so we can give a clear error for "wrong
        // file type" without paying the cost of opening the zip.
        if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await _dialogService.ShowAlertAsync(
                Strings.DialogTitleDropRestoreInvalid,
                DialogMessage.Plain(Strings.TextDropRestoreNotZip),
                DialogCategory.Error);
            return;
        }

        BackupEntry? entry = BackupEngine.Default.TryReadEntry(archivePath);
        if (entry is null || entry.IsCorrupt)
        {
            await _dialogService.ShowAlertAsync(
                Strings.DialogTitleDropRestoreInvalid,
                DialogMessage.Plain(Strings.TextDropRestoreNotABackup),
                DialogCategory.Error);
            return;
        }

        string fileName = entry.FileName;
        DialogMessage promptMsg = DialogMessage.Builder()
                                               .Text(Strings.TextDropRestorePromptPrefix)
                                               .Path(fileName)
                                               .Text(Strings.TextDropRestorePromptSuffix)
                                               .Build();

        bool? confirmed = await _dialogService.ShowConfirmAsync(
            Strings.DialogTitleDropRestore,
            promptMsg,
            confirmLabel: Strings.ButtonRestore);
        // Trinary: null (X) and false (Cancel) both abort; only explicit
        // Restore click proceeds.  Matches the universal X-dismiss contract.
        if (confirmed != true)
        {
            return;
        }

        // Route into the existing RestoreCommand with a synthesised row so
        // every guard the click-from-list path enforces (dirty-workspace
        // prompt, cross-platform warning, running-Claude detection, Sanitized
        // refusal at the engine, success / failure status routing) fires
        // unchanged.  Single code path = single mental model for the user.
        await RestoreAsync(new BackupRowViewModel(entry));
    }

    [RelayCommand]
    private async Task DeleteAsync(BackupRowViewModel row)
    {
        if (row?.Entry is null || IsBusy)
        {
            return;
        }

        Log.Information("[Backup.Command] action=Delete file={Path}", row.Entry.ArchivePath);

        // Set IsBusy before the confirm dialog so a concurrent Backup/Restore started
        // via keyboard shortcut while the dialog is open cannot race past the guard.
        IsBusy = true;
        try
        {
            DialogMessage deleteMsg = DialogMessage.Builder()
                                                   .Text("Delete ").Path(row.Entry.FileName)
                                                   .Text("?\n\nThis cannot be undone.")
                                                   .Build();
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                "Delete Backup?", deleteMsg, DialogCategory.Destructive,
                confirmLabel: "Delete");
            // Binary destructive yes/no — both Cancel (false) and X (null)
            // mean "don't delete."  Only an explicit Delete click proceeds.
            if (confirmed != true)
            {
                return;
            }

            if (BackupEngine.Default.Delete(row.Entry))
            {
                Backups.Remove(row);
                StatusMessage = string.Format(Strings.StatusBackupDeletedFmt, row.Entry.FileName);
            }
            else
            {
                StatusMessage = string.Format(Strings.StatusBackupDeleteFailedFmt, row.Entry.FileName);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Sends the backup archive to another app or device via the OS share panel.
    /// On Windows 10+ opens the native share flyout; on macOS reveals the file in
    /// Finder; on Linux opens the containing directory.
    /// </summary>
    [RelayCommand]
    private async Task ShareBackupAsync(BackupRowViewModel? row)
    {
        if (row?.Entry is null || _shareService is null)
        {
            return;
        }

        try
        {
            Log.Information("[Backup] Share requested for {Archive}", row.Entry.FileName);
            await _shareService.ShareFileAsync(row.DisplayName, row.Entry.ArchivePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Backup] Share failed for {Archive}", row.Entry.FileName);
        }
    }

    /// <summary>
    /// Opens the platform file manager showing the folder that contains the selected
    /// backup archive, with the archive pre-selected where the platform supports it.
    /// Bound to the Restore tab's DataGrid right-click context menu.
    /// </summary>
    [RelayCommand]
    private void OpenFileLocation(BackupRowViewModel? row)
    {
        if (row?.Entry is null)
        {
            return;
        }

        ShellLauncher.Instance.RevealInFileManager(row.Entry.ArchivePath);
    }

    [RelayCommand]
    private async Task FixMsixAsync()
    {
        if (!OperatingSystem.IsWindows() || IsBusy)
        {
            return;
        }

        if (MsixStatus?.NeedsFix != true)
        {
            return;
        }

        DialogMessage msixMsg = DialogMessage.Builder()
                                             .Text("Create an NTFS junction from ").Path(MsixStatus.StandardPath)
                                             .Text(" to ").Path(MsixStatus.VirtualisedPath).Text("?\n\n")
                                             .Text(
                                                 "If a real folder already exists at the standard path, its contents will be ")
                                             .Text(
                                                 "merged into the MSIX path (MSIX wins on conflict) and then removed. ")
                                             .Text("This does not require admin rights.")
                                             .Build();
        bool? confirm = await _dialogService.ShowConfirmAsync(
            "Create MSIX junction?", msixMsg,
            confirmLabel: "Create junction");
        if (confirm != true)
        {
            return;
        }

        IsBusy = true;
        ProgressMessage = Strings.ProgressCreatingJunction;
        try
        {
            MsixFixResult result = await MsixPathProbe.Instance.CreateJunctionAsync();
            StatusMessage = result.Message;
            MsixStatus = MsixPathProbe.Instance.Probe();
        }
        finally
        {
            IsBusy = false;
            ProgressMessage = null;
        }
    }

    partial void OnIncludeCredentialsChanged(bool value)
    {
        // Guard suppresses firing during Refresh() seeding (before _initialized = true).
        if (!_initialized)
        {
            return;
        }

        Log.Information("[Backup.UserEdit] IncludeCredentials={Value}", value);
        CredentialsPreference = value;
        PersistentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the credentials filename with a generic label so it does not flash
    /// visibly in the progress area during a backup that includes credentials.
    /// </summary>
    private static string SanitiseProgressItem(string item)
    {
        return Path.GetFileName(item).Equals(".credentials.json", StringComparison.OrdinalIgnoreCase)
            ? "credentials file"
            : item;
    }

    /// <summary>
    /// Sets a non-blocking <see cref="StatusMessage"/> warning when Claude Code or
    /// Claude Desktop is detected as running. File-lock conflicts during restore are
    /// still handled individually by <see cref="BackupEngine"/>; this is an early heads-up.
    /// </summary>
    private void CheckForRunningClaudeProcesses()
    {
        // Process.GetProcessesByName returns IDisposable objects holding native kernel
        // handles. The previous version filtered with `.Where(p => !p.HasExited)` and
        // only disposed the survivors — every process that had already exited (or whose
        // HasExited access threw) leaked its handle for the lifetime of the app.
        //
        // Materialise *every* process up front into `all`, then dispose them all in a
        // finally block regardless of their state. The HasExited check now feeds a
        // simple counter rather than re-projecting the list.
        List<Process> all = new();
        try
        {
            all.AddRange(Process.GetProcessesByName("claude"));
            all.AddRange(Process.GetProcessesByName("claude-desktop"));

            int runningCount = 0;
            foreach (Process p in all)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        runningCount++;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                               or Win32Exception
                                               or NotSupportedException)
                {
                    // HasExited can throw if the process exits mid-enumeration or the
                    // current user lacks query rights — treat as "not running".
                    _ = ex;
                }
            }

            if (runningCount > 0)
            {
                StatusMessage = string.Format(Strings.StatusClaudeRunningFmt, runningCount);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or Win32Exception
                                       or NotSupportedException
                                       or PlatformNotSupportedException)
        {
            // Best-effort — never block the restore due to process detection failure.
            // Process.GetProcessesByName itself can throw on locked-down environments.
            _ = ex;
        }
        finally
        {
            foreach (Process p in all)
            {
                p.Dispose();
            }
        }
    }

    private static string FormatAgo(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
        {
            return Strings.LabelLastBackupJustNow;
        }

        if (span.TotalHours < 1)
        {
            return string.Format(Strings.LabelLastBackupMinutesFmt, (int)span.TotalMinutes);
        }

        if (span.TotalDays < 1)
        {
            return string.Format(Strings.LabelLastBackupHoursFmt, (int)span.TotalHours);
        }

        return string.Format(Strings.LabelLastBackupDaysFmt, (int)span.TotalDays);
    }

    public void Dispose()
    {
        if (_vmDisposed)
        {
            return;
        }

        _vmDisposed = true;

        // Cancel any in-flight backup or restore so it does not continue writing to
        // disk after the VM is discarded (e.g. on workspace reload).
        _operationCts?.Cancel();
        _operationCts = null;

        // PersistentStateChanged subscribers are held by the host (MainWindowViewModel).
        // Nulling it out severs the link so callbacks from stale VMs are silently dropped
        // rather than firing on the already-replaced host handler.
        PersistentStateChanged = null;
    }
}

/// <summary>
/// Two-way converters that map <see cref="BackupMode"/> to/from a boolean for radio
/// buttons. Each converter handles exactly one mode; checking a radio button sets the
/// underlying enum. Exposed as static fields for XAML <c>x:Static</c> lookup.
/// </summary>
public sealed class BackupModeConverter : IValueConverter
{
    public static readonly BackupModeConverter IsSettingsOnly = new(BackupMode.SettingsOnly);
    public static readonly BackupModeConverter IsFull = new(BackupMode.Full);
    public static readonly BackupModeConverter IsSanitized = new(BackupMode.Sanitized);

    private readonly BackupMode _target;

    private BackupModeConverter(BackupMode target)
    {
        _target = target;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is BackupMode m && m == _target;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? _target : BindingOperations.DoNothing;
    }
}

/// <summary>Row ViewModel for the Restore list.</summary>
/// <remarks>
/// Inherits <see cref="ObservableObject"/> so future
/// mutators (none today; rows are immutable in practice — they're
/// rebuilt on every <c>RebuildBackupList</c>) can raise
/// <c>PropertyChanged</c> via the inherited
/// <c>OnPropertyChanged(string)</c> method.  Computed properties
/// (<see cref="IsRestorable"/>, <see cref="IsSanitized"/>,
/// <see cref="DisplayMode"/>) read from the immutable
/// <see cref="Entry"/> field, so no backing field /
/// <c>[ObservableProperty]</c> is needed today — only the
/// participation in the property-change pipeline.
/// </remarks>
public sealed class BackupRowViewModel : ObservableObject
{
    public BackupRowViewModel(BackupEntry entry)
    {
        Entry = entry;
    }

    public BackupEntry Entry { get; }

    public string DisplayName => Entry.FileName;
    public string DisplayDate => Entry.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string DisplaySize => BytesToHumanReadableConverter.Format(Entry.SizeBytes);

    public string DisplayPlatform => Entry.Manifest?.Platform ?? "?";

    /// <summary>
    /// Compact display string for the backup's <see cref="BackupMode"/>.
    /// Sanitized backups render as just "Sanitized" in the cell — the
    /// amber chip background carries the "this is special" visual signal,
    /// and <see cref="DisplayModeTooltip"/> carries the long-form
    /// "for sharing — not restorable" explanation on hover.
    /// </summary>
    public string DisplayMode => Entry.Manifest?.Mode switch
    {
        null => "—",
        BackupMode.Sanitized => "Sanitized",
        var m => m.ToString()!,
    };

    /// <summary>
    /// Hover-tooltip variant of <see cref="DisplayMode"/>.  For Sanitized
    /// rows, surfaces the long-form "Sanitized (for sharing — not
    /// restorable)" so users hovering over the amber chip understand
    /// why the Restore button on the same row is disabled.  For other
    /// modes, mirrors DisplayMode verbatim.
    /// </summary>
    public string DisplayModeTooltip => Entry.Manifest?.Mode switch
    {
        BackupMode.Sanitized => "Sanitized (for sharing — not restorable)",
        var _ => DisplayMode,
    };

    /// <summary>
    /// Compact display string for the cell — "Code", "Desktop", or "Code+Desktop"
    /// (etc.).  Long-form product names round-trip via <see cref="DisplayClientsTooltip"/>
    /// so users hovering see the full "ClaudeCode+ClaudeDesktop" label without
    /// the cell needing 23 chars of horizontal real estate.
    /// </summary>
    public string DisplayClients => Entry.Manifest is null
        ? "corrupt"
        : string.Join("+", Entry.Manifest.Clients.Select(AbbreviateClient));

    /// <summary>Hover-tooltip variant showing the full product names.</summary>
    public string DisplayClientsTooltip => Entry.Manifest is null
        ? "Manifest is unreadable (corrupt archive)"
        : string.Join(", ", Entry.Manifest.Clients);

    /// <summary>
    /// Map a long-form product name to its compact display alias for the
    /// Clients column.  Falls through to the raw name for unknown future
    /// product names so a new client doesn't render as an empty cell.
    /// </summary>
    /// <remarks>
    /// case-insensitive matching so a manifest with
    /// <c>"claudecode"</c> (different case) doesn't fall through to the
    /// verbose-passthrough branch.  Today the manifest strings are
    /// written by <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.BackupEngine"/> with
    /// exact casing, but a drag-dropped archive from a third-party tool
    /// could supply any casing.  The unknown-name passthrough preserves
    /// the ORIGINAL casing so unfamiliar product names render as-typed.
    /// </remarks>
    internal static string AbbreviateClient(string client)
    {
        return client.ToLowerInvariant() switch
        {
            "claudecode" => "Code",
            "claudedesktop" => "Desktop",
            var _ => client,
        };
    }

    /// <summary>
    /// True when the row's manifest is parseable AND the mode is not
    /// <see cref="BackupMode.Sanitized"/>.  Sanitized backups are
    /// share-only — <c>RestoreEngine.RestoreAsync</c> refuses them with a
    /// clear message, but disabling the button at the row level means the
    /// user never sees a failed click.
    /// </summary>
    public bool IsRestorable =>
        !Entry.IsCorrupt && Entry.Manifest?.Mode != BackupMode.Sanitized;

    /// <summary>
    /// True when the row's manifest indicates <see cref="BackupMode.Sanitized"/>.
    /// The View binds this to a small "(sanitized — for sharing)" chip /
    /// tooltip so the row is visually distinguishable from restorable backups.
    /// </summary>
    public bool IsSanitized => Entry.Manifest?.Mode == BackupMode.Sanitized;
}