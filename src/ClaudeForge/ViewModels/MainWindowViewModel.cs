using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Core;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.FileIO;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Profile;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Sdk.Internal;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Status;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Json.Schema;
using Serilog;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

// SDK clients live alongside the legacy SettingsWorkspace
// during the editor migration. Aliases disambiguate types that exist in both
// Core and Sdk (ConfigScope, SchemaValidationError). Code in MWVM continues to
// use the Core types by default; the SDK-typed names are reached via Sdk.* .
// the public Status property below shadows the
// ClaudeForge.ViewModels.Status namespace; this alias gives unambiguous
// access to the namespace's types (StatusKind, StatusController) inside
// MainWindowViewModel.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Root ViewModel for the main window.
/// Owns the navigation tree, active editor, settings workspaces, and app-level state.
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SchemaRegistry _schemaRegistry;

    /// <summary>
    /// View-side accessor for the dialog service so the View can run
    /// modals it owns (e.g. <c>MainWindow.OnClosing</c>'s unsaved-changes
    /// prompt).  The VM exposes this rather than passing the service in
    /// the ctor of the View because the service is a constructor-time
    /// dependency of the VM and shouldn't be re-instantiated.
    /// </summary>
    public IDialogService DialogServiceForViewAccess { get; }

    private readonly IShareService? _shareService;
    private readonly SchemaSnapshotService _snapshotService = new();

    private ConfigFileWatcher? _watcher;
    // legacy _workspace / _desktopWorkspace fields
    // retired. The SDK clients (ClaudeCodeSdk / ClaudeDesktopSdk) are the
    // only state holders. NavigationTreeBuilder.BuildGroups derives the
    // backing SettingsWorkspace via ClaudeConfigClientCore.WorkspaceForGui
    // for the SettingsGroupEditorViewModel + factory chain that still
    // consumes workspace.GetLayeredValue.

    // SDK clients constructed alongside the legacy workspaces.
    // Both wrap the SAME SettingsWorkspace as the legacy fields above via
    // ClaudeCodeClient.FromExistingWorkspace / ClaudeDesktopClient.FromExistingWorkspace,
    // so any mutation through the SDK is immediately visible to the legacy
    // editor pipeline (and vice versa). After 4.3.7 the legacy workspace
    // fields are deleted and the SDK clients become the only state holders.
    // Typed as the concrete public-abstract base rather than the interface so
    // MWVM can reach internal helpers (SnapshotDirtyDocuments — 4.3.7 step 9)
    // without a cast at every call site. Both ClaudeCodeClient and
    // ClaudeDesktopClient derive from this base.
    internal ClaudeConfigClientCore? ClaudeCodeSdk { get; private set; }
    internal ClaudeConfigClientCore? ClaudeDesktopSdk { get; private set; }

    private bool _disposed;

    // Remember the full path set we rendered this session so Dispose can
    // persist it as the next "already seen" baseline.
    private readonly Dictionary<string, IEnumerable<string>> _renderedPathsBySchema = new();

    // search lives on its own VM now (Search property below).
    // The CancellationTokenSource that used to debounce typing moved with it.

    // Debounce token for OnBackupStateChanged saves: Refresh() fires the event up to
    // three times in quick succession (BackupDirectory, RestoreDirectory, credentials).
    // We collapse those into a single disk write by cancelling the previous pending save
    // and scheduling a new one with a short delay.
    private CancellationTokenSource? _backupStateSaveCts;

    // Reload guard: prevents concurrent calls to LoadAllWorkspacesAsync.
    // If a reload arrives while one is already running, _reloadPending is set
    // and a new reload starts automatically when the in-flight one finishes.
    private bool _reloadPending;

    // reload-loop guard (companion to _reloadPending).
    //
    // LoadAllWorkspacesAsync raises OnPropertyChanged(AvailableProfileEntries)
    // near its tail so the toolbar ComboBox re-evaluates against fresh disk
    // state.  That notification can cause Avalonia's TwoWay-bound
    // SelectedItem binding to write back through SelectedProfileEntry.set,
    // which sets SelectedProfile, which fires OnSelectedProfileChanged →
    // _ = ReloadCoreAsync().  Without this guard, every reload re-arms the next
    // one and the app spins in a tight reload loop (observed when switching
    // when switching to a freshly-created profile — the binding wrote back
    // because the new AvailableProfileEntries instance contained a fresh
    // record reference for the selected entry, and the SelectingItemsControl
    // selection-match fast path didn't honour the record's value equality).
    //
    // Setting this flag suppresses the OnSelectedProfileChanged reload-kick
    // for the duration of LoadAllWorkspacesAsync's tail.  Saving window
    // state is also skipped because the value isn't actually changing from
    // the user's intent — we're just absorbing a transient binding bounce.
    private bool _suppressProfileChangeReload;

    // self-write watcher suppression.
    //
    // Problem: when SaveCoreAsync writes settings.json, the file watcher
    // sees its own write (~50–400ms later, after the FileSystemWatcher
    // debounce) and fires OnFileChangedExternally → ReloadAsync. The
    // reload rebuilds the entire navigation tree, which DISPOSES every
    // editor in it — including BackupRestoreViewModel, which cancels any
    // in-flight backup. Symptom: user starts a backup, navigates to
    // Permissions, edits a rule, clicks Save → backup cancelled, no
    // progress bar when navigating back to Backup page.
    //
    // Fix: stamp a UTC deadline at the end of SaveCoreAsync. The watcher
    // event handler returns early when the current time is before that
    // deadline. The window is generous (2 s) because the watcher's own
    // debounce is 400 ms, OS-level file flush + atomic temp+rename can
    // add several hundred ms more, and on Windows the watcher
    // occasionally double-fires for a single write.
    //
    // External edits during the suppression window are ignored — those
    // are extremely rare (it's the user's own machine) and the next
    // external edit re-fires the watcher anyway. Net trade-off favours
    // the common case (correctness on every save).
    //
    // A future cleanup is to keep long-running tool VMs (Backup,
    // Profiles) alive across reloads so spurious reloads can't kill
    // their work.
    private DateTime _suppressWatcherUntilUtc;

    // covers the dialog-open phase of SaveCoreAsync.
    //
    // Problem (3.8 manual test failure): the `_suppressWatcherUntilUtc`
    // stamp lands AT THE END of SaveCoreAsync, but the most damaging race
    // is BEFORE that — during the await on ShowSaveChangesDialogAsync.
    // Sequence pre-fix:
    //   1. ApplyToWorkspace flushes user edits to workspace.Root
    //   2. Save dialog opens (await)  ← IsLoading is false here
    //   3. User externally modifies settings.json
    //   4. Watcher → OnFileChangedExternally → ReloadAsync (no guard
    //      catches this; IsLoading=false; suppression deadline not yet
    //      stamped)
    //   5. ReloadAsync replaces SDK clients with fresh ones from disk,
    //      losing the in-memory user edits
    //   6. User confirms dialog
    //   7. SaveAsync runs against the fresh SDK clients with no dirty
    //      documents — silent no-op. User's edits gone.
    //
    // Fix: a counter incremented at the START of SaveCoreAsync's "save
    // is committed" phase (after early-return guards) and decremented in
    // finally. While > 0, OnFileChangedExternally returns without
    // reloading. Counter (not bool) so concurrent saves — defensive
    // even though SaveCoreAsync's IsLoading guard makes this rare —
    // don't clear suppression prematurely.
    private int _saveInProgressCount;

    /// <summary>
    /// Save-confirmation dialog preference: user can uncheck "Show this
    /// dialog on save" to skip the confirmation dialog for the rest of the
    /// session.  Re-enabled automatically on Reload Window so users aren't
    /// permanently locked out.
    /// </summary>
    [ObservableProperty] private bool _showSaveChangesDialog = true;

    // System-theme follow mode — true until the user manually toggles
    private bool _isFollowingSystem;

    // Back-navigation: single-level "return from deep link" stack.
    // Set before any programmatic navigation (env-var links, search results).
    // Cleared when the user navigates manually or clicks the Back button.
    private NavigationNodeViewModel? _backNode;
    private bool _isDeepLinkNavigation;

    // One shared scope context per product section so that changing the scope
    // dropdown on any Claude Code (or Desktop) page propagates to all other
    // pages in that section.  The contexts survive workspace reloads so the
    // user's selected scope is preserved across file-watcher reloads.
    private readonly SharedScopeContext _ccScopeContext = new();
    private readonly SharedScopeContext _dtScopeContext = new();

    // Per-header expand/collapse state, persisted to ClaudeForge-gui-state.json
    private readonly Dictionary<string, bool> _navHeaderExpanded;

    // persistent tool-page VMs.  These VMs do NOT bind to
    // workspace.Root (they manage on-disk state via dedicated services /
    // engines), so they're safe to reuse across BuildNavigationTree calls.
    // Persisting them solves three Phase 3 issues:
    //   - Backup: in-flight backup operations no longer get cancelled by
    //     unrelated workspace reloads (file-watcher, profile switch,
    //     "Reload Window" button) that re-walk the nav tree.
    //   - Profiles: Refresh re-reads disk state on reload, no full re-init.
    //   - About: dialog/share service wires + product type stay stable.
    // The schema-driven editor pages (Permissions / Hooks / MCP / etc.)
    // continue to rebuild on every reload — they bind to workspace.Root
    // sub-trees that the SDK swap replaces.
    private AboutEditorViewModel? _aboutCodeVm;
    private AboutEditorViewModel? _aboutDesktopVm;
    private ProfilesViewModel? _profilesVm;
    private BackupRestoreViewModel? _backupVm;

    // persistent Essentials VM.  Like the other tool VMs
    // above, it survives workspace reloads so:
    //   - the user's in-flight edits aren't discarded mid-keystroke when a
    //     file-watcher reload fires;
    //   - the synthetic-search amber callout isn't dismissed before the user
    //     reads it.
    // Cards are re-bound to the post-reload SDK client via RefreshAsync.
    private EssentialsViewModel? _essentialsVm;

    /// <summary>Sentinel value displayed in the profile ComboBox to mean "no named profile — use global user settings".</summary>
    public const string GlobalProfileSentinel = UnifiedProfileEntry.GlobalName;

    // Navigation node title constants — used both as display labels and as lookup keys
    // for programmatic navigation (n.Title == NavTitle*).  Keeping them as English constants
    // means the lookup comparisons remain stable; full i18n would require a separate NodeId.
    private const string NavTitleClaudeCode = "Claude Code";
    private const string NavTitleClaudeDesktop = "Claude Desktop";
    private const string NavTitleVersionInfo = "Version Information";
    private const string NavTitleEffectiveSettings = "Effective Settings";
    private const string NavTitleProfiles = "Profiles";
    private const string NavTitleBackupRestore = "Backup / Restore";
    private const string NavTitleEnvironment = "Environment";
    private const string NavTitleMemory = "Memory";
    private const string NavTitleAgentsSkills = "Agents & Skills";

    /// <summary>
    /// Title of the synthetic top-of-tree "Essentials" node.  Hardcoded
    /// English (parallels the other NavTitle* constants) so both the node's
    /// display label and the programmatic <c>n.Title</c> lookups stay
    /// culture-invariant.  The localized <c>Strings.NavTitleEssentials</c>
    /// is consumed separately by <c>SearchViewModel</c> as the search-results
    /// group label; full nav-tree localization is still pending (see the
    /// NavDesc* note below).
    /// </summary>
    private const string NavTitleEssentials = "Essentials";

    /// <summary>
    /// Title of the synthetic top-of-tree "Welcome" node (2026-05-19).
    /// Placed BEFORE Essentials so it's the natural top-of-tree landing
    /// spot.  Has no <c>Editor</c> — selecting it leaves
    /// <c>ActiveEditor = null</c> which renders the existing
    /// <c>WelcomeView</c> orientation panel.  Selected by default on
    /// a fresh launch (no persisted <c>LastSelectedNodeTitle</c>) so
    /// new users see the orientation content instead of being dropped
    /// straight into the first Claude Code editor.
    /// </summary>
    private const string NavTitleWelcome = "Welcome";

    // One-sentence descriptions that surface as hover tooltips in the
    // navigation tree. Hard-coded English (the constants above are too,
    // for the same reason: programmatic comparisons against n.Title need
    // a stable invariant string). Translation will need a parallel set
    // of localized lookups when we tackle nav-tree localization.
    private const string NavDescClaudeCode =
        "Edit ~/.claude/settings.json (and project / local layers when a project is open). Includes Permissions, Hooks, MCP servers, Plugins.";

    private const string NavDescClaudeDesktop = "Edit %APPDATA%\\Claude\\claude_desktop_config.json (User scope only).";

    private const string NavDescVersionInfo =
        "Show Claude binary location, version, and config-file paths for this product.";

    private const string NavDescEffectiveSettings =
        "Show the merged values Claude actually sees at runtime, with origin scope and overridden flag for each property.";

    private const string NavDescProfiles =
        "Manage named settings profiles in ~/.claude/profiles/ — switch, create, delete, or sync between Claude Code and Claude Desktop.";

    private const string NavDescBackupRestore =
        "Create timestamped .zip archives of your Claude config (and optionally projects + credentials), and restore them later.";

    private const string NavDescEnvironment =
        "View and edit Claude-related environment variables across Process, Claude env, User, and Machine scopes.";

    private const string NavDescMemory =
        "Audit, view, and clean up the files Claude reads on every session and the behavioural footprint left on disk.";

    private const string NavDescAgentsSkills =
        "Browse the sub-agents, skills, and slash commands Claude Code loads — across User, Project, and Plugin scopes.";

    private const string NavDescEssentialsTooltip =
        "High-importance Claude Code settings — token budgets, sandbox, MCP trust, model and update channel — pinned for quick review and change.";

    private const string NavDescWelcome =
        "Orientation for the editor — what gets edited, how scopes layer, and where to find the high-impact settings. Shown by default on first launch.";

    public MainWindowViewModel(SchemaRegistry schemaRegistry, IDialogService dialogService,
                               IShareService? shareService = null)
    {
        _schemaRegistry = schemaRegistry;
        DialogServiceForViewAccess = dialogService;
        _shareService = shareService;
        NavigationTree = [];

        // Search VM. Constructed early so AXAML
        // {Binding Search.SearchQuery} et al. resolves before the first render.
        // The closures pull the live navigation tree and IsLoading flag on
        // every search pass — no stale captures across reloads.
        Search = new SearchViewModel(
            getNavigationTree: () => NavigationTree,
            isLoadingProbe: () => IsLoading,
            claudeCodeNavTitle: NavTitleClaudeCode,
            getSchemaSearchProviders: BuildSchemaSearchProviders);

        // Restore persisted state — single hydrate per session. Subsequent saves
        // mutate _cachedState in place rather than re-reading from disk.
        _cachedState = WindowStateService.Load();
        _projectRoot = _cachedState.ProjectRoot;
        _isFollowingSystem = _cachedState.Theme == "System";
        _isDarkTheme = _cachedState.Theme == "Dark"; // false when "System" — corrected in ApplyRestoredTheme
        _lastNodeTitle = _cachedState.LastSelectedNodeTitle;
        _navHeaderExpanded = new Dictionary<string, bool>(_cachedState.NavHeaderExpanded);

        // Welcome-on-launch preference: hydrated from disk and consulted
        // by BuildNavigationTree's Welcome-node conditional below.  Use
        // the source-generated backing field directly so the partial
        // OnShowWelcomeOnLaunchChanged handler doesn't fire prematurely
        // (it would otherwise trigger ApplyWelcomeNodeVisibility before
        // BuildNavigationTree has even run — _navTreeBuilt is still
        // false, so the call would no-op, but explicit field-init is
        // clearer about intent).
        _showWelcomeOnLaunch = _cachedState.ShowWelcomeNode;

        // Seed the auto-update-check preference from persisted state.
        // Same pattern as _showWelcomeOnLaunch above — backing-field
        // assignment avoids triggering OnCheckForUpdatesOnLaunchChanged
        // during construction.
        _checkForUpdatesOnLaunch = _cachedState.CheckForUpdatesOnLaunch;

        // Seed geometry cache so no-arg SaveWindowState() calls preserve the
        // restored dimensions instead of falling back to 1200×750 defaults.
        _savedWidth = _cachedState.Width;
        _savedHeight = _cachedState.Height;
        _savedX = _cachedState.X;
        _savedY = _cachedState.Y;

        // Default profile selection — set backing field directly to avoid triggering
        // OnSelectedProfileChanged (which would queue a reload before InitializeAsync runs).
        _selectedProfile = _cachedState.SelectedProfile ?? GlobalProfileSentinel;

        // Deep-link: env-var tokens in property descriptions are clickable; they send this
        // message so we can navigate to the Environment section and filter to that variable.
        WeakReferenceMessenger.Default.Register<NavigateToEnvVarMessage>(
            this, (_, msg) => OnNavigateToEnvVar(msg));

        // Deep-link: "View in <group>" button on Essentials-page cards (and any
        // future surface that needs to deep-link into a schema-driven nav node)
        // sends this message.  Receiver finds the child node by Title and
        // selects it, optionally applying a property filter on the editor.
        WeakReferenceMessenger.Default.Register<NavigateToNavGroupMessage>(
            this, (_, msg) => OnNavigateToNavGroup(msg));
    }

    public ObservableCollection<NavigationNodeViewModel> NavigationTree { get; }

    [ObservableProperty] private NavigationNodeViewModel? _selectedNode;

    /// <summary>
    /// Currently-active editor view-model rendered in the main content area.
    /// <c>null</c> means the welcome view is shown instead — either because
    /// no node has been selected yet, or because the user clicked one of
    /// the navigation tree's header rows (Claude Code / Claude Desktop)
    /// which deliberately clear this property to surface the welcome view
    /// (see <c>OnSelectedNodeChanged</c>).
    /// </summary>
    [ObservableProperty] private object? _activeEditor;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoading;

    /// <summary>
    /// Centre status-bar text + lifecycle.  Replaces the prior bare
    /// <c>string? StatusMessage</c> that never auto-cleared — see
    /// <see cref="Bennewitz.Ninja.ClaudeForge.ViewModels.Status.StatusController"/> and
    /// <c>CLAUDE.md</c> "Status bar" for the full lifecycle rules.
    /// </summary>
    /// <remarks>
    /// Every status emit goes through one of <see cref="SetStatusActive"/>,
    /// <see cref="SetStatusSuccess"/>, <see cref="SetStatusWarning"/>,
    /// <see cref="SetStatusFailure"/>, <see cref="SetStatusState"/> so the
    /// kind / severity is explicit at the callsite — instead of every
    /// callsite writing a bare string that the View has no way to
    /// distinguish.  See <see cref="DismissStatusCommand"/> for the
    /// user-driven clear path.
    /// </remarks>
    public StatusController Status { get; } = new();

    /// <summary>
    /// Legacy alias for <see cref="Status"/>.<c>Text</c> — kept so existing
    /// AXAML bindings and tests that read <c>StatusMessage</c> continue to
    /// resolve.  New code should not write to this directly; use the
    /// <c>SetStatus*</c> helpers instead so the kind is explicit.
    /// </summary>
    public string? StatusMessage
    {
        get => Status.Text;
        // Keep the legacy setter for the small number of test sites that
        // still assign directly — they get a State-kind entry (no auto-
        // clear) since the caller didn't classify severity.
        set => Status.Set(value, StatusKind.State);
    }

    /// <summary>
    /// Convenience: route an "in-flight verb" update through the
    /// controller.  Used wherever the prior code wrote a gerund / "ing"
    /// status (Loading / Reloading / Opening project / Reloading (foo
    /// changed)).  Active messages stick until the next <c>SetStatus*</c>
    /// call replaces them.
    /// </summary>
    private void SetStatusActive(string text)
    {
        Status.Set(text, StatusKind.Active);
    }

    /// <summary>Terminal positive outcome.  Auto-clears after ~6 s.</summary>
    private void SetStatusSuccess(string text)
    {
        Status.Set(text, StatusKind.Success);
    }

    /// <summary>Informational "nothing to act on" notice.  Auto-clears after ~10 s.</summary>
    private void SetStatusWarning(string text)
    {
        Status.Set(text, StatusKind.Warning);
    }

    /// <summary>Terminal negative outcome.  Sticks until <see cref="DismissStatusCommand"/> fires.</summary>
    private void SetStatusFailure(string text)
    {
        Status.Set(text, StatusKind.Failure);
    }

    /// <summary>
    /// Convert an exception into a status-bar-safe message.  Strips
    /// absolute filesystem paths and truncates the message so the
    /// Failure pill (which sticks indefinitely until the user
    /// dismisses) doesn't loiter on screen with the user's home
    /// directory layout or other internal detail visible.
    /// </summary>
    /// <param name="ex">Caught exception whose <see cref="Exception.Message"/>
    /// would otherwise be inlined verbatim.</param>
    /// <param name="hintPath">Optional path to the operand file.
    /// When supplied, only its filename component
    /// (<see cref="Path.GetFileName"/>) appears in the rendered
    /// message — never the directory portion.  When null, no file
    /// name is appended.</param>
    /// <returns>Compact, sanitised message of the form
    /// <c>"&lt;ExceptionType&gt; on &lt;filename&gt;: &lt;short
    /// message&gt;"</c> (filename omitted when no hint), capped at
    /// roughly 120 characters.</returns>
    /// <remarks>
    /// Failure status pills don't auto-clear; the
    /// user dismisses them with the × button.  Pre-fix,
    /// `IOException.Message` typically embedded the full filesystem
    /// path (`Access to the path 'C:\Users\brian\.claude\settings.json'
    /// is denied`) and the message stuck on-screen indefinitely.
    /// Privacy concern when the user is screen-sharing or recording.
    /// </remarks>
    internal static string SanitiseExceptionForStatus(Exception ex, string? hintPath = null)
    {
        string typeName = ex.GetType().Name;
        string fileSuffix = hintPath is null
            ? string.Empty
            : $" on {Path.GetFileName(hintPath)}";
        string shortMessage = SafeShortMessage(ex.Message);
        return $"{typeName}{fileSuffix}: {shortMessage}";
    }

    /// <summary>
    /// Sanitise an exception message: strip absolute-path fragments
    /// and truncate to the status-bar length budget.
    /// </summary>
    /// <remarks>
    /// Path-stripping is pattern-based — Windows
    /// drive-letter prefixes (<c>C:\…\</c>, <c>D:/…/</c>) and
    /// POSIX absolute paths (<c>/home/user/…</c>,
    /// <c>/Users/someone/…</c>) are detected and reduced to their
    /// filename component.  An unrecognised path shape (e.g. UNC,
    /// custom non-standard root) passes through unchanged — better
    /// to show the path than to silently mangle the message.
    /// </remarks>
    private static string SafeShortMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message ?? string.Empty;
        }

        // Pattern: 'C:\…\filename.ext' or "C:\…\filename.ext" or unquoted.
        // Capture the filename component; replace the whole match with it.
        string stripped = MyRegex().Replace(message, "$1");
        // POSIX absolute paths.
        stripped = MyRegex1().Replace(stripped, "$1");

        const int maxLen = 120;
        if (stripped.Length > maxLen)
        {
            stripped = stripped[..(maxLen - 1)] + "…";
        }

        return stripped;
    }

    /// <summary>
    /// Long-lived identity text (Ready / Project: foo).  Sticks until
    /// another <c>SetStatus*</c> call replaces it.  Rendered in the muted
    /// secondary-text colour with no icon.
    /// </summary>
    private void SetStatusState(string text)
    {
        Status.Set(text, StatusKind.State);
    }

    /// <summary>
    /// Clear the centre status text.  Bound to the close (×) button the
    /// View renders for <see cref="Status.StatusKind.Failure"/> messages —
    /// users don't lose error context until they explicitly acknowledge it.
    /// </summary>
    [RelayCommand]
    private void DismissStatus()
    {
        Log.Information("[App.Command] action=DismissStatus");
        Status.Dismiss();
    }

    /// <summary>
    /// Currently-open project folder (or <c>null</c> when no project has been
    /// opened — the "default mode" where only User-scope is editable).
    /// <para>
    /// Setting this raises change notifications for every editing-context
    /// surface — the Welcome page, the persistent status row at the top of
    /// MainWindow, and any tooltip or computed string that distinguishes
    /// "user-only" mode from "user + project + local" mode.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen))]
    [NotifyPropertyChangedFor(nameof(ProjectFolderName))]
    [NotifyPropertyChangedFor(nameof(ProjectClaudeDirPath))]
    [NotifyPropertyChangedFor(nameof(EditingContextSummary))]
    [NotifyPropertyChangedFor(nameof(EditingContextIcon))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _projectRoot;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ThemeButtonLabel))]
    private bool _isDarkTheme;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private string? _selectedProfile;

    /// <summary>
    /// Search bar VM. Owns SearchQuery / IsSearchOpen /
    /// SearchResults plus the debounced typing pipeline. AXAML bindings reach
    /// these via <c>Search.SearchQuery</c> et al.
    /// </summary>
    public SearchViewModel Search { get; }

    /// <summary>
    /// Build the per-section list of <see cref="SchemaSearchProvider"/> entries that
    /// <see cref="SearchViewModel"/> consults each search pass.  Each entry pairs a
    /// product's nav-section title with the SDK client's <c>SearchSchema</c> delegate;
    /// the list is rebuilt on every call so a workspace reload (which swaps the SDK
    /// client) is reflected without re-creating the search VM.  When an SDK client is
    /// not yet open, its product is omitted — the search VM will then silently fall
    /// back to title-only matching for that section.
    /// </summary>
    private IReadOnlyList<SchemaSearchProvider> BuildSchemaSearchProviders()
    {
        // Capture references locally so the lambdas don't observe a mid-search SDK
        // swap (workspace reload). Method-group conversion isn't usable here because
        // SearchSchema has a defaulted maxResults parameter — so wrap in a one-arg
        // lambda that takes the SDK default.
        List<SchemaSearchProvider> list = new(2);
        ClaudeConfigClientCore? cc = ClaudeCodeSdk;
        if (cc is not null)
        {
            list.Add(new SchemaSearchProvider(NavTitleClaudeCode, q => cc.SearchSchema(q)));
        }

        ClaudeConfigClientCore? dt = ClaudeDesktopSdk;
        if (dt is not null)
        {
            list.Add(new SchemaSearchProvider(NavTitleClaudeDesktop, q => dt.SearchSchema(q)));
        }

        return list;
    }

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(NavigateBackCommand))]
    private bool _canGoBack;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _showInstallBanner;

    /// <summary>
    /// Schema-validation errors found in the loaded workspace after the most
    /// recent <see cref="LoadAllWorkspacesAsync"/> pass.  When non-empty the
    /// schema-banner above the editor area renders with a click-through to the
    /// detailed list (Phase 3.15 — surfaced post-reload, separate from the
    /// pre-save validation flow).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSchemaErrors))]
    [NotifyPropertyChangedFor(nameof(SchemaErrorsBannerText))]
    [NotifyPropertyChangedFor(nameof(IsSchemaErrorsBannerVisible))]
    [NotifyCanExecuteChangedFor(nameof(ShowSchemaErrorsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissSchemaErrorsBannerCommand))]
    private IReadOnlyList<SchemaValidationError> _schemaErrors = [];

    /// <summary>True when <see cref="SchemaErrors"/> has at least one entry.</summary>
    public bool HasSchemaErrors => SchemaErrors.Count > 0;

    /// <summary>
    /// Banner headline rendered when <see cref="HasSchemaErrors"/> is true.
    /// Pulled from <see cref="Strings.LabelSchemaBannerTitleFmt"/> with the
    /// error count substituted in.
    /// </summary>
    public string SchemaErrorsBannerText =>
        string.Format(Strings.LabelSchemaBannerTitleFmt, SchemaErrors.Count);

    /// <summary>
    /// Drives the schema-errors banner's <c>IsVisible</c> binding. True when
    /// errors exist AND the user hasn't dismissed the banner for the current
    /// working session.
    /// <para>
    /// The dismiss flag <see cref="_schemaErrorsBannerDismissed"/> resets
    /// automatically when <see cref="ProjectRoot"/> changes (project load),
    /// so opening a different project always re-surfaces any schema errors
    /// that profile carries. It does NOT reset on workspace reload — the
    /// user has already seen the errors and chosen to ignore them for this
    /// session; nagging them on every file-watcher reload would be hostile.
    /// </para>
    /// </summary>
    public bool IsSchemaErrorsBannerVisible =>
        HasSchemaErrors && !_schemaErrorsBannerDismissed;

    /// <summary>
    /// True when any loaded workspace has in-memory changes that have not yet been
    /// written to disk.  Drives the Save button's enabled state.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// Window title in the format:
    /// <code>
    /// "ClaudeForge — &lt;project indicator&gt;[ *]"
    /// </code>
    /// where the project indicator is either the loaded git branch name,
    /// the leaf folder name, or "No Project Loaded" — see
    /// <see cref="ProjectIndicator.BuildIndicator"/> for the resolution
    /// tree.  The trailing <c>" *"</c> appears when <see cref="HasUnsavedChanges"/>
    /// is true (Windows convention; see Notepad / VS Code et al. so users
    /// can tell at a glance whether their session has pending writes).
    /// <para>
    /// Bound by <c>MainWindow.axaml</c> via <c>Title="{Binding WindowTitle}"</c>.
    /// Cross-platform; reads <c>.git/HEAD</c> as plain text rather than
    /// shelling out to <c>git</c>, so it works uniformly on Windows /
    /// macOS / Linux.  extended same day with the
    /// project indicator per the audit follow-up.
    /// </para>
    /// </summary>
    public string WindowTitle
    {
        get
        {
            string indicator = ProjectIndicator.BuildIndicator(ProjectRoot);
            string dirty = HasUnsavedChanges ? " *" : string.Empty;
            // Em-dash (—) for the separator — visually heavier than a
            // hyphen so the prefix and indicator read as two distinct
            // segments.  Matches the Notepad / VS Code "AppName — FileName"
            // convention.
            return $"{Strings.AppTitle} — {indicator}{dirty}";
        }
    }

    [ObservableProperty] private string? _cliActiveProfileName;
    [ObservableProperty] private string? _desktopActiveProfileName;

    public string ThemeButtonLabel => IsDarkTheme ? Strings.ThemeLabelLight : Strings.ThemeLabelDark;

    // ── Editing-context properties (Welcome page + persistent status row) ──
    //
    // These derive purely from `ProjectRoot`; their `OnPropertyChanged`
    // notifications fire automatically via the [NotifyPropertyChangedFor]
    // attributes on `_projectRoot` above. No backing fields, no manual
    // INotifyPropertyChanged plumbing — the values are recomputed on every
    // read so they always reflect current state.

    /// <summary>
    /// True when a project folder is currently open (and Project / Local
    /// scopes are therefore loaded and editable). Drives "no-project hint"
    /// vs "project active" UI in the Welcome page and status row.
    /// </summary>
    public bool IsProjectOpen => !string.IsNullOrEmpty(ProjectRoot);

    /// <summary>
    /// Display-friendly leaf folder name of <see cref="ProjectRoot"/>
    /// (e.g. <c>"my-app"</c>), used in the "Project: foo" labels in the
    /// status row, Welcome panel, and tooltip text. Empty string when no
    /// project is open — bindings should hide the label in that case via
    /// <see cref="IsProjectOpen"/>.
    /// </summary>
    public string ProjectFolderName =>
        string.IsNullOrEmpty(ProjectRoot) ? string.Empty : Path.GetFileName(ProjectRoot.TrimEnd('/', '\\'));

    /// <summary>
    /// Full path to the project's <c>.claude/</c> directory (e.g.
    /// <c>"C:\repos\my-app\.claude"</c>), used by the Welcome page's
    /// "Project: foo" panel to show the user exactly which directory will be
    /// affected by Project / Local scope edits. Empty string when no project
    /// is open.
    /// </summary>
    public string ProjectClaudeDirPath =>
        string.IsNullOrEmpty(ProjectRoot) ? string.Empty : Path.Combine(ProjectRoot, ".claude");

    /// <summary>
    /// User-global settings file path (always <c>~/.claude/settings.json</c>
    /// on the current OS). Bound by the Welcome page's "What you're editing
    /// now" panel as the always-active User-scope target.
    /// </summary>
    public string UserSettingsPath => PlatformPaths.UserSettingsPath;

    /// <summary>
    /// One-line summary of the current editing context, formatted for the
    /// persistent status row at the top of MainWindow:
    /// <list type="bullet">
    ///   <item><c>"User config — no project open"</c> when ProjectRoot is null</item>
    ///   <item><c>"User + Project + Local — project: foo"</c> when a project is open</item>
    /// </list>
    /// Both forms come from <see cref="Strings"/> so the text is localized.
    /// </summary>
    public string EditingContextSummary =>
        IsProjectOpen
            ? string.Format(Strings.TextEditingContextWithProject, ProjectFolderName)
            : Strings.TextEditingContextNoProject;

    /// <summary>
    /// Single-character icon shown next to <see cref="EditingContextSummary"/>
    /// in the status row. House icon when no project is open (editing
    /// user-global config); folder icon when a project is open. Plain string
    /// rather than a glyph font so it works on every platform without an
    /// extra font asset.
    /// </summary>
    public string EditingContextIcon => IsProjectOpen ? "📁" : "🏠";

    /// <summary>
    /// Version string read from the entry assembly attributes (set via &lt;Version&gt; in
    /// the .csproj). Displayed in the status bar so users can report which build they're on.
    /// Instance property (not static) so compiled Avalonia bindings can resolve it.
    /// </summary>
    public string AppVersion => BackupConstants.AppVersion;

    /// <summary>
    /// View model for the reusable install-command panel shown in the
    /// install-guidance banner.  Produces the platform-appropriate Claude Code
    /// shell one-liner and drives the Copy / Run-in-terminal buttons.  The
    /// same <see cref="InstallCommandViewModel"/> type is also used on the
    /// About page (via <c>AboutEditorViewModel</c>) for the per-product
    /// "(not detected)" row, so the UX stays consistent.
    /// </summary>
    public InstallCommandViewModel InstallPanel { get; } = InstallCommandViewModel.ForClaudeCode();

    /// <summary>
    /// Post-install next-step note shown at the bottom of the install banner.
    ///
    /// On Linux/macOS the installer places the binary at <c>~/.local/bin/claude</c>,
    /// which is not always in PATH on a freshly configured machine.  The terminal
    /// shows the exact <c>export PATH=…</c> command but users may miss it, so we
    /// repeat the guidance here.
    ///
    /// On Windows the PowerShell installer updates PATH at the system/user level,
    /// but the currently running app process inherits the old environment and cannot
    /// detect the new installation without a restart.
    /// </summary>
    public string InstallPostInstallNote =>
        OperatingSystem.IsWindows()
            ? Strings.TextInstallPostInstallNoteWindows
            : Strings.TextInstallPostInstallNoteUnix;

    /// <summary>
    /// bound by AXAML <c>IsVisible</c> on Windows-only hint UI
    /// (today: the WSL bullet on the Welcome page).  Reads through
    /// <see cref="PlatformInfo.Current"/> rather than
    /// <see cref="OperatingSystem.IsWindows"/> so the
    /// <c>--linux</c> / <c>--macos</c> debug-flag emulation cleanly
    /// hides the hint on non-Windows simulated runs — matches the
    /// CLAUDE.md "Platform abstraction" decision tree for UI surfaces.
    /// </summary>
    public bool IsWindowsPlatform => PlatformInfo.Current.IsWindows;

    /// <summary>
    /// Formats the save-metadata stamp written as a top-level <c>"//"</c> key in each
    /// config file so a user who opens the file in a text editor knows when the tool
    /// last touched it.  Uses local time with AM/PM so the value is immediately readable.
    /// The wording makes clear this only tracks tool saves — manual edits won't update it.
    /// </summary>
    private string MakeHeaderComment()
    {
        return $"ClaudeForge v{AppVersion} last saved this file on {DateTime.Now:MM-dd-yyyy hh:mm:ss tt}" +
               " — manual edits outside this tool are not reflected by this timestamp";
    }

    // Set to true when the user explicitly clicks Dismiss. Prevents reloads
    // (file-watcher, profile switch) from re-showing the banner as long as
    // neither product is detected. Cleared automatically when a reload
    // detects at least one product — so the banner returns correctly if the
    // user later removes both products again.
    private bool _bannerDismissedByUser;

    /// <summary>
    /// Set to true when the user explicitly dismisses the schema-errors
    /// banner. Reset to false by <see cref="OnProjectRootChanged"/> so
    /// opening a new project always re-surfaces that project's schema
    /// errors. Does NOT reset on workspace reload — see
    /// <see cref="IsSchemaErrorsBannerVisible"/> for the rationale.
    /// </summary>
    private bool _schemaErrorsBannerDismissed;

    /// <summary>Hides the install-guidance banner for the rest of this session.</summary>
    [RelayCommand]
    private void DismissInstallBanner()
    {
        _bannerDismissedByUser = true;
        ShowInstallBanner = false;
    }

    /// <summary>
    /// Hides the schema-errors banner for the rest of the current working
    /// session. Resets on next project load via
    /// <see cref="OnProjectRootChanged"/>. Symmetric with
    /// <see cref="DismissInstallBanner"/>. CanExecute gates on
    /// <see cref="HasSchemaErrors"/> so the dismiss button disables when
    /// errors clear (e.g. the user fixed the file externally and reloaded).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSchemaErrors))]
    private void DismissSchemaErrorsBanner()
    {
        _schemaErrorsBannerDismissed = true;
        OnPropertyChanged(nameof(IsSchemaErrorsBannerVisible));
    }

    /// <summary>
    /// Source-gen partial fired when <see cref="ProjectRoot"/> changes
    /// (user opened a different project). Resets the schema-errors
    /// banner-dismiss flag so the newly-loaded project's errors surface
    /// even if the previous project's errors had been dismissed by the
    /// user.
    /// <para>
    /// App-start case is handled naturally: <see cref="_schemaErrorsBannerDismissed"/>
    /// defaults to <see langword="false"/> on construction so the first
    /// post-load evaluation of <see cref="IsSchemaErrorsBannerVisible"/>
    /// always shows the banner if errors exist.
    /// </para>
    /// </summary>
    partial void OnProjectRootChanged(string? value)
    {
        if (_schemaErrorsBannerDismissed)
        {
            _schemaErrorsBannerDismissed = false;
            OnPropertyChanged(nameof(IsSchemaErrorsBannerVisible));
        }

        // Push the new project root into the Backup VM so the Backup-tab's
        // "Includes open project" label refreshes immediately (the field is
        // an [ObservableProperty] with NotifyPropertyChangedFor on
        // OpenProjectName + BackupIncludesProjectLabel).  Defensive against
        // any future code path that mutates ProjectRoot without going
        // through a full nav-tree rebuild; the ctor + pre-Refresh re-sync
        // already cover the canonical paths.
        if (_backupVm is not null)
        {
            _backupVm.InitialProjectRoot = value;
        }
    }

    /// <summary>
    /// Opens a Destructive-styled dialog listing every <see cref="SchemaErrors"/>
    /// entry by file + DisplayPath + message.  Same formatter the pre-save
    /// "Cannot Save — Schema Validation Error" path uses, for visual
    /// consistency between the two surfaces.
    /// </summary>
    /// <remarks>
    /// CanExecute is gated on <see cref="HasSchemaErrors"/> so the button
    /// disables itself when the user fixes the underlying file externally
    /// and the next reload clears <see cref="SchemaErrors"/>.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(HasSchemaErrors))]
    private async Task ShowSchemaErrorsAsync()
    {
        if (SchemaErrors.Count == 0)
        {
            return;
        }

        DialogMessage msg = DialogMessage.Builder()
                                         // Code() renders the validation body in a monospace font so
                                         // the path / value / index alignment from FormatSchemaErrors
                                         // (file headers + indented bullets with InstancePath, error
                                         // text) reads as a single coherent block rather than reflowed
                                         // prose.  Mirrors the convention used by `Path()` segments —
                                         // monospace signals "this is structured config text, not
                                         // narration."
                                         .Code(FormatSchemaErrors(SchemaErrors))
                                         .Text("\n\n")
                                         .Text(
                                             "These errors are in the loaded files. Fix them in your editor and reload, ")
                                         .Text(
                                             "or save other changes from this app — the schema-invalid sections write through ")
                                         .Text("verbatim and Claude may reject them at runtime.")
                                         .Build();

        await DialogServiceForViewAccess.ShowAlertAsync(
            Strings.TitleSchemaValidationDetails,
            msg,
            DialogCategory.Destructive);
    }

    // -----------------------------------------------------------------------
    // Profile management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new named profile under <c>~/.claude/profiles/&lt;name&gt;/</c>,
    /// seeds it with an empty <c>settings.json</c>, and switches to it.
    /// </summary>
    [RelayCommand]
    private async Task NewProfileAsync()
    {
        string? name = await DialogServiceForViewAccess.ShowInputAsync(
            Strings.DialogTitleNewProfile,
            Strings.DialogPromptNewProfile,
            placeholder: Strings.DialogPlaceholderNewProfile);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Log.Information("[App.Command] action=NewProfile (toolbar) name=\"{Name}\"", name);

        // Validate: reserved names, invalid path chars, duplicates
        if (name.Equals(GlobalProfileSentinel, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("global", StringComparison.OrdinalIgnoreCase))
        {
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleInvalidName,
                DialogMessage.Plain(string.Format(Strings.MsgReservedProfileName, name)));
            return;
        }

        if (name is "." or "..")
        {
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleInvalidName,
                DialogMessage.Plain(Strings.MsgDotProfileName));
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleInvalidName,
                DialogMessage.Plain(Strings.MsgInvalidPathChars));
            return;
        }

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        if (Directory.Exists(profileDir))
        {
            DialogMessage existsMsg = DialogMessage.Builder()
                                                   .Text("A profile named '").Bold(name)
                                                   .Text("' already exists. Choose a different name.")
                                                   .Build();
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleProfileExists, existsMsg);
            return;
        }

        try
        {
            // Seed from the current live ~/.claude/settings.json (+ CLAUDE.md + mcpServers)
            // so the profile starts from real state rather than an empty object.
            // CreateFromLiveAsync falls back to writing {} when the live file is absent.
            await ProfileEngine.CreateFromLiveAsync(name);

            // Refresh the ComboBox first so the new item is visible when the
            // binding updates SelectedProfile.
            OnPropertyChanged(nameof(AvailableProfileEntries));
            SelectedProfile = name; // triggers OnSelectedProfileChanged → reload
            SetStatusSuccess(string.Format(Strings.StatusProfileCreatedFmt, name));
        }
        catch (Exception ex)
        {
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleError,
                DialogMessage.Plain(string.Format(Strings.MsgCannotCreateProfile, ex.Message)),
                DialogCategory.Error);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when a named (non-global) profile is the active selection.
    /// Used as the CanExecute gate for <see cref="DeleteProfileCommand"/>.
    /// </summary>
    // Delete is only available for non-global entries that have a CLI profile.
    // Desktop-only profiles can be deleted from the Profiles management page instead.
    private bool CanDeleteProfile()
    {
        return SelectedProfileEntry is { IsGlobal: false, HasCli: true };
    }

    /// <summary>
    /// Permanently deletes the active named profile directory (after confirmation)
    /// and switches back to the global settings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfileAsync()
    {
        string? name = SelectedProfile;
        if (string.IsNullOrEmpty(name) || name == GlobalProfileSentinel)
        {
            return;
        }

        Log.Information("[App.Command] action=DeleteProfile (toolbar) name=\"{Name}\"", name);

        // Build the confirm prose around a Bold(name) span so the profile being
        // deleted reads as the focal point, not buried inline. Uses the
        // localized format string but slices around the {0} placeholder.
        string rawDeleteMsg = string.Format(Strings.MsgConfirmDeleteProfile, name);
        int splitIdx = rawDeleteMsg.IndexOf(name, StringComparison.Ordinal);
        DialogMessage deleteMsg = splitIdx >= 0
            ? DialogMessage.Builder()
                           .Text(rawDeleteMsg[..splitIdx])
                           .Bold(name)
                           .Text(rawDeleteMsg[(splitIdx + name.Length)..])
                           .Build()
            : DialogMessage.Plain(rawDeleteMsg);
        bool? confirmed = await DialogServiceForViewAccess.ShowConfirmAsync(
            Strings.DialogTitleDeleteProfile,
            deleteMsg,
            DialogCategory.Destructive,
            confirmLabel: Strings.ButtonDeleteProfile);

        if (confirmed != true)
        {
            return;
        }

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        try
        {
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }

            // Clear the .claudectx-current pointer if it pointed to this profile,
            // so the CLI active badge disappears and we don't reference a deleted profile.
            if (string.Equals(ProfileEngine.ReadCurrentProfileName(), name, StringComparison.OrdinalIgnoreCase))
            {
                ProfileEngine.WriteCurrentProfileName(null);
                CliActiveProfileName = null;
            }

            // Switch to global first so the ComboBox doesn't briefly sit on a
            // missing item while AvailableProfiles refreshes.
            SelectedProfile = GlobalProfileSentinel; // triggers reload
            OnPropertyChanged(nameof(AvailableProfileEntries));
            SaveWindowState();
            SetStatusSuccess(string.Format(Strings.StatusProfileDeletedFmt, name));
        }
        catch (Exception ex)
        {
            await DialogServiceForViewAccess.ShowAlertAsync(Strings.DialogTitleError,
                DialogMessage.Plain(string.Format(Strings.MsgCannotDeleteProfile, ex.Message)),
                DialogCategory.Error);
        }
    }

    private string? _lastNodeTitle;

    /// <summary>
    /// user preference: when <see langword="true"/>, a
    /// dedicated "Welcome" nav node is added to the top of
    /// <see cref="NavigationTree"/>; when <see langword="false"/>, the
    /// node is omitted (the Welcome page remains reachable by selecting
    /// the "Claude Code" / "Claude Desktop" header nodes).  Bound to the
    /// "Show this page on launch" checkbox on the Welcome page itself.
    /// Persisted via <c>WindowState.ShowWelcomeNode</c>.
    /// </summary>
    [ObservableProperty] private bool _showWelcomeOnLaunch;

    partial void OnShowWelcomeOnLaunchChanged(bool value)
    {
        // Persist immediately + rebuild the navigation tree so the
        // Welcome node appears / disappears in real time when the user
        // toggles the checkbox.  Skip during construction (before
        // BuildNavigationTree has run for the first time) — the initial
        // load reads the preference and adds / omits the node natively.
        _cachedState.ShowWelcomeNode = value;
        SaveWindowState();
        if (_navTreeBuilt)
        {
            ApplyWelcomeNodeVisibility();
        }
    }

    /// <summary>
    /// User preference: when <see langword="true"/>, ClaudeForge
    /// queries GitHub once per launch to see if a newer release is
    /// available (silent on failure).  Toggled via the
    /// "Check for updates on launch" Essentials card.  Persisted via
    /// <c>WindowState.CheckForUpdatesOnLaunch</c>.
    /// </summary>
    [ObservableProperty] private bool _checkForUpdatesOnLaunch;

    partial void OnCheckForUpdatesOnLaunchChanged(bool value)
    {
        // Same persistence pattern as OnShowWelcomeOnLaunchChanged.
        // The check itself ALREADY fired (once per launch) by the
        // time the user is in a position to toggle this card, so the
        // change takes effect on the NEXT launch — exactly the
        // behaviour the body text on the Essentials card describes.
        _cachedState.CheckForUpdatesOnLaunch = value;
        SaveWindowState();
        Log.Information(
            "[UpdateCheck] User changed CheckForUpdatesOnLaunch to {Value}",
            value);
    }

    /// <summary>
    /// Backing VM for the "Update available" banner.  Constructed once
    /// in the MWVM ctor and bound to the
    /// <c>&lt;views:UpdateBanner DataContext="{Binding UpdateBanner}" /&gt;</c>
    /// slot in <c>MainWindow.axaml</c>.  Starts hidden — surfaces only
    /// after <see cref="AppUpdateService.CheckOncePerLaunchAsync"/>
    /// resolves with an UpdateAvailable result that hasn't been
    /// previously dismissed.
    /// </summary>
    public UpdateBannerViewModel UpdateBanner { get; } = new();

    /// <summary>
    /// Process-static latch: <see langword="true"/> once the
    /// once-per-launch update check has been kicked off by this MWVM.
    /// Subsequent re-binds (profile switches, file-watcher reloads,
    /// etc.) do NOT re-trigger the check.  The
    /// <see cref="AppUpdateService"/> has its own once-per-process
    /// latch as well, but this one short-circuits BEFORE the async
    /// task is even kicked — saving a tiny amount of work on every
    /// secondary load.
    /// </summary>
    private static int _updateCheckKickedThisProcess;

    /// <summary>
    /// Test seam: reset the once-per-process update-check latch so a
    /// test can re-exercise the launch-time kick path.  Paired with
    /// <see cref="AppUpdateService.ResetForTesting"/>; tests typically
    /// call both.
    /// </summary>
    internal static void ResetUpdateCheckLatchForTesting()
    {
        Interlocked.Exchange(ref _updateCheckKickedThisProcess, 0);
    }

    /// <summary>
    /// Kick the once-per-launch GitHub update check.  Fire-and-forget;
    /// the resulting Task is discarded.  The check is silent on every
    /// failure mode — caller doesn't await and doesn't need to.
    ///
    /// <para>
    /// Called from the first successful return of
    /// <see cref="LoadAllWorkspacesAsync"/>.  Process-static latch
    /// guards against re-firing on subsequent reloads (profile switch,
    /// file-watcher, etc.) within the same process.
    /// </para>
    /// </summary>
    private void KickOnceLaunchUpdateCheck()
    {
        if (Interlocked.CompareExchange(ref _updateCheckKickedThisProcess, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                Core.Updates.UpdateCheckResult result =
                    await AppUpdateService.CheckOncePerLaunchAsync().ConfigureAwait(true);
                // Marshal the apply to the UI thread — UpdateBannerViewModel
                // raises PropertyChanged events that must hit the UI sync
                // context.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateBanner.ApplyResult(result);
                });
            }
            catch (Exception ex)
            {
                // AppUpdateService has its own try/catch around every
                // network path — anything escaping here is a real bug
                // (e.g. an unhandled exception in ApplyResult).  Log it
                // but don't propagate; the banner just stays hidden.
                Log.Information(
                    ex,
                    "[UpdateCheck] Unhandled exception during launch-time check; banner stays hidden");
            }
        });
    }

    /// <summary>
    /// Latched after <see cref="BuildNavigationTree"/> finishes for the
    /// first time so that the runtime checkbox toggle path
    /// (<see cref="ApplyWelcomeNodeVisibility"/>) doesn't fire from the
    /// initial preference assignment in the constructor.
    /// </summary>
    private bool _navTreeBuilt;

    /// <summary>
    /// Add or remove the Welcome node from <see cref="NavigationTree"/>
    /// to reflect the current <see cref="ShowWelcomeOnLaunch"/> value.
    /// Called when the user toggles the Welcome-page checkbox at
    /// runtime; not used during the initial tree build (which respects
    /// the preference natively).
    /// </summary>
    private void ApplyWelcomeNodeVisibility()
    {
        NavigationNodeViewModel? existing = NavigationTree.FirstOrDefault(n => n.Title == NavTitleWelcome);
        if (ShowWelcomeOnLaunch)
        {
            if (existing != null)
            {
                return; // already present
            }

            NavigationNodeViewModel welcome = new(NavTitleWelcome, "🏠", NavDescWelcome)
            {
                IsTopLevel = true,
                // Editor is intentionally null — WelcomeView renders when ActiveEditor is null.
            };
            NavigationTree.Insert(0, welcome);
        }
        else
        {
            if (existing == null)
            {
                return; // already absent
            }

            // If the user is currently ON the Welcome page when they
            // un-toggle, move selection to the natural next landing
            // spot before removing the node so the editor area doesn't
            // briefly bind to a stale removed node.
            if (SelectedNode == existing)
            {
                SelectedNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleEssentials)
                               ?? NavigationTree.FirstOrDefault(n => n.Title == NavTitleClaudeCode)?.Children
                                                .FirstOrDefault()
                               ?? NavigationTree.FirstOrDefault(n => !n.IsDivider);
            }

            NavigationTree.Remove(existing);
        }
    }

    // Cache of the last-known window geometry so that no-arg SaveWindowState() calls
    // (triggered by node selection, profile changes, etc.) don't reset the saved size
    // back to the default 1200×750.  Seeded from the persisted state on construction
    // and updated whenever MainWindow reports new geometry.
    private double _savedWidth = 1200;
    private double _savedHeight = 750;
    private double? _savedX;
    private double? _savedY;

    // Single in-memory truth for the persisted window/UI state.
    // Hydrated once in the constructor; every subsequent SaveWindowState /
    // OnBackupStateChanged mutates this object in place and writes it through
    // WindowStateService.Save without re-reading from disk. Eliminates the
    // 5–7 disk reads per startup that the previous load-modify-save pattern
    // performed on every node selection, profile change, header expand/collapse,
    // project open, and theme toggle.
    //
    // Trade-off: if another process (or a future feature) writes to the file,
    // our cache goes stale. Mitigation: the file is single-writer in current
    // architecture; if that changes, pair the cache with a FileSystemWatcher.
    private readonly WindowState _cachedState;

    /// <summary>
    /// Unified list of CLI and Desktop profiles for the toolbar ComboBox.
    /// Shared profiles (same name in both systems) appear as a single merged entry.
    /// CLI-only profiles come first; Desktop-only profiles come last.
    /// </summary>
    public IReadOnlyList<UnifiedProfileEntry> AvailableProfileEntries
    {
        get
        {
            HashSet<string> cliNames = new(PlatformPaths.DiscoverProfiles(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> dtNames = new(PlatformPaths.DiscoverDesktopProfiles(), StringComparer.OrdinalIgnoreCase);

            List<UnifiedProfileEntry> entries = [UnifiedProfileEntry.Global];

            // CLI profiles first — merged with Desktop when a same-named Desktop profile exists
            foreach (string name in cliNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new UnifiedProfileEntry(name, HasCli: true, HasDesktop: dtNames.Contains(name)));
            }

            // Desktop-only profiles last
            foreach (string name in dtNames.Except(cliNames, StringComparer.OrdinalIgnoreCase)
                                           .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new UnifiedProfileEntry(name, HasCli: false, HasDesktop: true));
            }

            return entries;
        }
    }

    /// <summary>
    /// The currently selected <see cref="UnifiedProfileEntry"/> — bound to the ComboBox
    /// <c>SelectedItem</c>.  Getting this property resolves <see cref="SelectedProfile"/>
    /// (a persisted string) back to a list entry; setting it updates the string and
    /// triggers the existing reload/save chain via <see cref="OnSelectedProfileChanged"/>.
    /// </summary>
    public UnifiedProfileEntry? SelectedProfileEntry
    {
        get => AvailableProfileEntries.FirstOrDefault(e =>
                   string.Equals(e.Name, SelectedProfile, StringComparison.OrdinalIgnoreCase))
               ?? UnifiedProfileEntry.Global;
        set => SelectedProfile = value?.Name ?? UnifiedProfileEntry.GlobalName;
    }

    // -----------------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsLoading = true;
        SetStatusActive(Strings.StatusLoadingSettings);
        try
        {
            await LoadAllWorkspacesAsync();
            SetStatusState(Strings.StatusReady);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Initialize] Failed to load workspaces");
            SetStatusFailure(string.Format(Strings.StatusErrorFmt, SanitiseExceptionForStatus(ex)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Save is also disabled while IsLoading is true so a save cannot race a concurrent
    // reload. ReloadAsync uses IsLoading as its in-flight guard; gating Save the same way
    // makes the two operations mutually exclusive on the workspace fields they share.
    private bool CanSave()
    {
        return !ShowInstallBanner && HasUnsavedChanges && !IsLoading;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync()
    {
        Log.Information("[App.Command] action=Save (user clicked Save / pressed Ctrl+S)");
        return SaveCoreAsync(isRestoreContext: false);
    }

    /// <summary>
    /// Save variant called by the BackupRestoreViewModel's pre-Backup or
    /// pre-Restore dirty-workspace guard.  Forwards <paramref name="isRestoreContext"/>
    /// to <see cref="SaveCoreAsync"/> so the confirmation dialog uses the
    /// terminology that matches the pending action: regular "Save Changes" /
    /// "Saving N changes" labels for Backup, "Restore Preview" / "Restoring N
    /// changes" labels for Restore.
    /// </summary>
    /// <remarks>
    /// replaces the prior <c>SaveForRestoreAsync</c> helper which
    /// always passed <c>isRestoreContext: true</c>, mislabelling the dialog
    /// when invoked from the Backup-flow's pre-save guard.
    /// </remarks>
    public Task SaveForBackupOrRestoreAsync(bool isRestoreContext)
    {
        return SaveCoreAsync(isRestoreContext);
    }

    private async Task SaveCoreAsync(bool isRestoreContext)
    {
        // Reload-in-flight guard: skip save if a reload is mid-await.
        //
        // Previously this method had no guard. If the user clicked Save while
        // ReloadAsync was awaiting LoadAllWorkspacesAsync, save would read the
        // about-to-be-replaced _workspace, write its dirty docs, and call MarkClean()
        // on them; reload would then assign a freshly-loaded workspace whose disk
        // baseline is *prior to* the just-written changes — silently losing the
        // edits in memory the next time anything observed _workspace.
        //
        // SaveCommand's CanSave already disables the button via IsLoading, but
        // SaveForRestoreAsync is a public entry point and bypasses CanSave;
        // keep the early-return so the contract holds at the source.
        if (IsLoading)
        {
            Log.Debug("[Save] Skipped: workspace reload in progress");
            return;
        }

        // Flush every group editor to its workspace — not just the currently visible one.
        // A user may have edited multiple groups before pressing Save.  Each group's
        // ApplyToWorkspace internally gates on its own _userEditedPaths set so that
        // untouched-but-IsModified-true editors don't clobber out-of-band SDK writes;
        // see the bug entry on SettingsGroupEditorViewModel._userEditedPaths.
        foreach (NavigationNodeViewModel node in NavigationTree.SelectMany(n => n.Children.Prepend(n)))
        {
            if (node.Editor is SettingsGroupEditorViewModel g)
            {
                g.ApplyToWorkspace();
            }
        }

        if (ClaudeCodeSdk is null && ClaudeDesktopSdk is null)
        {
            SetStatusWarning(Strings.StatusNothingToSave);
            return;
        }

        // from this point on, treat the save as "in progress"
        // so the file watcher will not trigger a reload that races our
        // dialog or disk write. See _saveInProgressCount field comment.
        // The counter is incremented AFTER editor flush + null guards so
        // a no-op save (no SDK clients) doesn't toggle the suppression
        // unnecessarily.
        Interlocked.Increment(ref _saveInProgressCount);
        try
        {
            // Log all pending changes before any modal dialog appears so the
            // changes are always visible in the rolling log and the F12 debug window.
            WorkspaceDiagnostics.LogPendingChanges(ClaudeCodeSdk, ClaudeDesktopSdk);

            // ── Schema validation ────────────────────────────────────────────────
            // Validate dirty documents before showing any confirmation or writing files.
            // validation runs against the WHOLE dirty document, not just
            // the changed subtree. So pre-existing schema issues in untouched data
            // (e.g. legacy hook entries the editor doesn't natively render, hand-
            // edited fields, or schema drift from older Claude Code versions) would
            // block ALL saves — even when the user's actual change is unrelated and
            // valid. Surface the errors but offer "Save anyway" so the user is not
            // locked out of saving valid changes elsewhere in the document. The
            // SDK's SaveAsync is already invoked with force:true (it does its own
            // validation pass that we'd otherwise re-trigger here), so the
            // override has no second gate behind it.
            IReadOnlyList<SchemaValidationError> schemaErrors = await ValidatePendingChangesAsync();
            if (schemaErrors.Count > 0)
            {
                Log.Warning("[Schema] {Count} validation error(s) found before save", schemaErrors.Count);
                foreach (SchemaValidationError err in schemaErrors)
                {
                    Log.Warning("[Schema]   {File} | {Path}: {Message}",
                        Path.GetFileName(err.FilePath), err.DisplayPath, err.Message);
                }

                SetStatusFailure(Strings.StatusSchemaValidationFailed);
                DialogMessage schemaMsg = DialogMessage.Builder()
                                                       // Monospace body — see ShowSchemaErrorsAsync for rationale.
                                                       .Code(FormatSchemaErrors(schemaErrors))
                                                       .Text(
                                                           "\n\nThese errors may be in data you didn't change — for example ")
                                                       .Text(
                                                           "hooks of types this editor doesn't natively support, or hand-edited ")
                                                       .Text(
                                                           "fields. You can save anyway to write your other changes; the schema-")
                                                       .Text(
                                                           "invalid sections are written verbatim and Claude Code may reject them ")
                                                       .Text("at runtime.")
                                                       .Build();
                bool? saveAnyway = await DialogServiceForViewAccess.ShowConfirmAsync(
                    Strings.TitleSchemaValidationFailed,
                    schemaMsg,
                    DialogCategory.Destructive,
                    confirmLabel: "Save anyway",
                    cancelLabel: "Cancel");
                // Binary destructive yes/no — both Cancel and X abort.
                if (saveAnyway != true)
                {
                    Log.Information("[Schema] User cancelled save after seeing {Count} validation error(s)",
                        schemaErrors.Count);
                    return;
                }

                Log.Warning("[Schema] User chose 'Save anyway' despite {Count} validation error(s)",
                    schemaErrors.Count);
            }

            // Show a "what you're about to save" confirmation when there are actual changes.
            // If there are no content differences (e.g., Save is pressed twice), or the user
            // previously unchecked "Show this dialog on save", skip the dialog.
            SaveChangesDialogViewModel? summaryVm =
                SaveDialogBuilder.Build(ClaudeCodeSdk, ClaudeDesktopSdk, isRestoreContext);
            // diagnostic logging for the "silent save" bug report.
            // The user reported clicking Save (button enabled => HasUnsavedChanges
            // is true => structural diff is non-empty) but no dialog appearing.
            // The skip-dialog branch fires when EITHER summaryVm is null (the
            // structural diff didn't materialise into dialog sections — usually a
            // dirty-flag mismatch where IsModified is true but HasActualChanges
            // is false) OR _showSaveChangesDialog is false (user previously
            // unchecked "show this dialog on save" this session).  Log both flags
            // so the next iteration of the bug reproducer can pinpoint which
            // branch fired.
            Log.Information(
                "[Save] dialog gate: summaryNull={SummaryNull} sectionCount={SectionCount} showFlag={ShowFlag} hasUnsaved={HasUnsaved}",
                summaryVm is null,
                summaryVm?.Sections.Count ?? 0,
                ShowSaveChangesDialog,
                HasUnsavedChanges);

            // Silent-save safety net.  If the SDK reports unsaved
            // changes (Save button is enabled because HasUnsavedChanges = true,
            // routed via SDK.HasUnsavedChanges → SettingsDocument.HasActualChanges)
            // but SaveDialogBuilder.Build returned null (no sections to render),
            // the two structural-diff implementations disagree.  Pre-2026-05-08
            // root-cause: HasActualChanges used JsonNode.DeepEquals (everything
            // counts) while JsonDiff.Compute strips the "//" metadata key — a
            // timestamp-only delta passed the former and produced zero rows from
            // the latter, dropping into the silent-save branch.  Fixed by
            // harmonising the strip in HasActualChanges.
            //
            // This safety net stays as a defence-in-depth: any FUTURE divergence
            // between the two implementations (new metadata keys, custom diff
            // ignores, etc.) routes to a fallback confirmation dialog rather
            // than silent-saving.  Saves should always be either explicitly
            // confirmed or explicitly opted-out via "Show this dialog on save".
            if (summaryVm == null && HasUnsavedChanges)
            {
                Log.Warning(
                    "[Save] SDK reports unsaved changes but SaveDialogBuilder produced no sections — " +
                    "falling back to a generic confirmation dialog.  This indicates a divergence between " +
                    "SettingsDocument.HasActualChanges and JsonDiff.Compute that future maintenance " +
                    "should investigate; the dialog gate log line above carries the diagnostic context");
                bool? fallbackOk = await DialogServiceForViewAccess.ShowConfirmAsync(
                    title: Strings.DialogTitleSaveChanges,
                    message: DialogMessage.Plain(Strings.TextSaveSilentSaveSafetyNet),
                    category: DialogCategory.Confirmation,
                    confirmLabel: Strings.ButtonSaveDialog,
                    cancelLabel: Strings.ButtonCancel);
                // Permanent: record the user's choice on the silent-save fallback
                // so post-mortem debugging can correlate disappeared-edit reports
                // with the user's confirm/cancel action.
                Log.Information("[Save] Fallback-dialog choice: {Choice}",
                    fallbackOk switch
                    {
                        true => "Confirm (proceed with save)",
                        false => "Cancel (do nothing)",
                        null => "Dismissed via X (do nothing)",
                    });
                // Both Cancel (false) and X (null) abort.  Only an explicit
                // Confirm click proceeds with the silent-save fallback.
                if (fallbackOk != true)
                {
                    return;
                }
                // No need to honour _showSaveChangesDialog here — the user
                // already saw a confirmation; falling through to the actual
                // SaveAsync below.
            }
            else if (summaryVm != null && ShowSaveChangesDialog)
            {
                bool confirmed = await DialogServiceForViewAccess.ShowSaveChangesDialogAsync(summaryVm);

                // Permanent: record the user's choice on the save-changes dialog
                // (Save vs. Cancel) so post-mortem debugging can correlate
                // disappeared-edit reports with the user's action.  This pairs
                // with the [Save] per-file change listing logged above by
                // WorkspaceDiagnostics.LogPendingChanges — together they form the
                // post-mortem of "what was about to be saved and what the user
                // chose".  Section count is included so a quick scan reveals
                // whether the dialog was empty (silent-save fallback path) or
                // populated.
                Log.Information("[Save] {Mode}-dialog choice: {Choice} (sections={Sections}, showAgain={ShowAgain})",
                    summaryVm.Mode,
                    confirmed
                        ? summaryVm.Mode == SaveDialogMode.Restore
                            ? "Confirm (proceed with restore)"
                            : "Save (proceed with write)"
                        : "Cancel (return to in-memory edits)",
                    summaryVm.Sections.Count,
                    summaryVm.ShowDialogAgain);

                // Persist the user's "Show this dialog on save" preference for this session.
                // the "Save confirmation dialog disabled" notice no longer
                // squats in the centre status bar (it would get overwritten by the next
                // save's "Saved." and the user lost the warning).  ShowSaveChangesDialog
                // is now an ObservableProperty bound to a "Save dialog off" badge next
                // to the Save toolbar button instead — a standing setting belongs in a
                // standing affordance, not the transient status slot.
                ShowSaveChangesDialog = summaryVm.ShowDialogAgain;

                if (!confirmed)
                {
                    // Cancel in Save mode means "abort the save and
                    // return to my in-progress edits."  Earlier behaviour treated
                    // the button as "Discard Changes" and called ReloadAsync to
                    // wipe the in-memory workspace back to disk baseline.  User
                    // feedback established that destructive interpretation was
                    // not what the user expected: "I wasn't expecting a reload
                    // to occur when pressing it and I wouldn't expect my changes
                    // to disappear."  Now Cancel simply dismisses the dialog and
                    // leaves the in-memory edits intact — the user can refine
                    // their changes and try Save again.  In Restore mode Cancel
                    // already had this no-op semantics.
                    return;
                }
            }

            // IsLoading also flips during save so that a ConfigFileWatcher-driven reload
            // arriving mid-save does not race the disk write. ReloadAsync already checks
            // IsLoading and queues via _reloadPending, so the reload runs immediately
            // after save completes and picks up the freshly-written file.
            IsLoading = true;
            try
            {
                string comment = MakeHeaderComment();
                // Before writing any config file for the first time through this app,
                // copy the original to <path>{B4ForgeSuffix} so the user always has a
                // pre-ClaudeForge snapshot. Subsequent saves leave the suffix file
                // untouched if it already exists.
                // 4.3.7 step 9: route through the SDK's dirty-doc snapshot when
                // available — only the FilePath is needed for the B4Forge copy.
                if (ClaudeCodeSdk is not null)
                {
                    CreateB4ForgeBackupsFromPaths(ClaudeCodeSdk.SnapshotDirtyDocuments());
                }

                if (ClaudeDesktopSdk is not null)
                {
                    CreateB4ForgeBackupsFromPaths(ClaudeDesktopSdk.SnapshotDirtyDocuments());
                }

                // route through the SDK's SaveAsync
                // overload that carries the header comment. force: true skips the
                // SDK's pre-save validation because the GUI ran its own validation
                // pass earlier (ValidatePendingChangesAsync at line ~786) and surfaced
                // any errors to the user via the confirmation dialog. Letting SaveAsync
                // re-validate here would either re-block the save with the same errors
                // (after the user opted into "save anyway"), or silently re-validate
                // after auto-fixes — both are wrong. Falls back to the legacy
                // SaveDirtyAsync path before the SDK clients are constructed.
                if (ClaudeCodeSdk is not null)
                {
                    await ClaudeCodeSdk.SaveAsync(force: true, comment, CancellationToken.None);
                }

                if (ClaudeDesktopSdk is not null)
                {
                    await ClaudeDesktopSdk.SaveAsync(force: true, comment, CancellationToken.None);
                }

                // Suppress the file watcher's reaction to our own write. See
                // _suppressWatcherUntilUtc field comment for the rationale.
                // Stamped INSIDE the IsLoading=true block so a watcher event
                // arriving even before SDK.SaveAsync's atomic-write completes
                // sees the future deadline and skips the reload.
                _suppressWatcherUntilUtc = DateTime.UtcNow.AddSeconds(2);

                HasUnsavedChanges = false;
                SetStatusSuccess(Strings.StatusSaved);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Save] Writing settings to disk failed");
                SetStatusFailure(string.Format(Strings.StatusSaveFailedFmt, SanitiseExceptionForStatus(ex)));
            }
            finally
            {
                IsLoading = false;
            }
        } // end try (Interlocked.Increment _saveInProgressCount)
        finally
        {
            // release the dialog-and-save watcher suppression.
            // _suppressWatcherUntilUtc takes over for the brief post-save
            // window where atomic-write/debounce events still echo back.
            Interlocked.Decrement(ref _saveInProgressCount);
        }
    }

    /// <summary>
    /// For each dirty document in <paramref name="workspace"/>, creates a
    /// <c>&lt;filepath&gt;-b4Forge</c> copy of the file as it exists on disk right now
    /// (before ClaudeForge modifies it).  If the <c>-b4Forge</c> file already exists it is
    /// left untouched so the snapshot always reflects the state before the very first save.
    /// Files that do not yet exist on disk (brand-new config files) are skipped — there is
    /// nothing to snapshot.
    /// </summary>
    private static void CreateB4ForgeBackupsIfNeeded(SettingsWorkspace workspace)
    {
        CopyB4ForgeBackups(workspace.DirtyDocuments().Select(d => d.FilePath));
    }

    /// <summary>SDK-snapshot variant — only the FilePath is needed.</summary>
    private static void CreateB4ForgeBackupsFromPaths(
        IReadOnlyList<DirtyDocumentSnapshot> snapshots)
    {
        CopyB4ForgeBackups(snapshots.Select(s => s.FilePath));
    }

    /// <summary>
    /// Shared file-copy loop. For each path that exists on disk and has no
    /// existing <c>-b4Forge</c> sibling, copy the original to
    /// <c>&lt;path&gt;{BackupConstants.B4ForgeSuffix}</c> as the
    /// pre-ClaudeForge snapshot. Failures are logged but non-fatal —
    /// the save itself proceeds.
    /// </summary>
    private static void CopyB4ForgeBackups(IEnumerable<string> filePaths)
    {
        foreach (string filePath in filePaths)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                continue;
            }

            string backup = filePath + BackupConstants.B4ForgeSuffix;
            if (File.Exists(backup))
            {
                continue;
            }

            try
            {
                File.Copy(filePath, backup);
                Log.Information("[Save] Created pre-ClaudeForge backup: {Backup}", backup);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Save] Could not create -b4Forge backup for {File}", filePath);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Save diagnostics (logged before any modal dialog)
    // -----------------------------------------------------------------------

    // Diagnostic helpers (LogPendingChanges, IsSensitiveKey, the diff machinery)
    // moved to ClaudeForge.Services.WorkspaceDiagnostics in cleanup.
    // Call sites use the static helpers directly.

    // -----------------------------------------------------------------------
    // Schema-validation helpers (run before the confirmation dialog)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Schema-validate every dirty document across both products before the
    /// confirmation dialog is shown. step 4 routes through the
    /// SDK clients now that <see cref="SchemaValidationError"/> lives on Core
    /// (so the SDK passes it through without losing the structural
    /// <c>InstancePath</c> the GUI's friendly-message formatter relies on).
    /// </summary>
    /// <remarks>
    /// Falls back to the raw <see cref="SchemaRegistry.ValidateWorkspaceAsync"/>
    /// path during the brief window before <c>LoadAllWorkspacesAsync</c>
    /// populates the SDK clients; both paths produce identical results
    /// because the SDK and the legacy workspace fields share state via
    /// <c>FromExistingWorkspace</c>.
    /// </remarks>
    private async Task<IReadOnlyList<SchemaValidationError>> ValidatePendingChangesAsync(
        CancellationToken ct = default)
    {
        List<SchemaValidationError> all = [];
        if (ClaudeCodeSdk is not null)
        {
            all.AddRange(await ClaudeCodeSdk.ValidateAsync(ct));
        }

        if (ClaudeDesktopSdk is not null)
        {
            all.AddRange(await ClaudeDesktopSdk.ValidateAsync(ct));
        }

        return all;
    }

    /// <summary>
    /// Thin passthrough \u2014 the actual translation lives in
    /// <see cref="Bennewitz.Ninja.ClaudeForge.Services.SchemaErrorMessages"/> so the
    /// translation table can be unit-tested without going through the
    /// full save-flow integration.
    /// </summary>
    private static string FormatSchemaErrors(IReadOnlyList<SchemaValidationError> errors)
    {
        return SchemaErrorMessages.Format(errors);
    }

    // -----------------------------------------------------------------------
    // Change-summary helpers (used by SaveAsync confirmation dialog)
    // -----------------------------------------------------------------------

    // Save-confirmation dialog construction (BuildChangeSummaryViewModel +
    // AppendSdkSections + AppendSection + DiffJsonObjects + TruncateJson +
    // ToDisplayPath) moved to ClaudeForge.Services.SaveDialogBuilder in
    // cleanup. Call sites use SaveDialogBuilder.Build directly.

    /// <summary>
    /// Confirms with the user, deletes the persisted UI-state file, and
    /// quits the process so the next launch starts from clean defaults.
    /// Bound to the "Clear App Data" button in the status bar.
    /// <para>
    /// Only the UI-state file (<c>~/.claude/cache/ClaudeForge-gui-state.json</c>) is
    /// removed — Claude config files are intentionally untouched.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ClearAppDataAsync()
    {
        Log.Information("[App.Command] action=ClearAppData (user clicked, confirmation pending)");
        bool? confirmed = await DialogServiceForViewAccess.ShowConfirmAsync(
            Strings.TitleClearAppDataConfirm,
            DialogMessage.Plain(Strings.TextClearAppDataConfirm),
            DialogCategory.Destructive,
            Strings.ConfirmClearAppData,
            Strings.ButtonCancel);

        // Destructive binary — both Cancel (false) and X (null) abort.
        if (confirmed != true)
        {
            return;
        }

        // Latch SaveWindowState before deleting the file. Without this guard
        // the OnClosed handler (and any other deferred persister chained to
        // shutdown) would re-create the file we just deleted before the
        // process actually exits.
        _suppressStateSave = true;

        string path = WindowStateService.Delete();
        string logsDir = PlatformPaths.AppLogsDirectory;
        Log.Information(
            "[ClearAppData] deleted persisted UI state at {Path}; purging {LogDir} and exiting",
            path, logsDir);

        // Also purge rolling log files written by this app
        // so a "Clear App Data" leaves no diagnostic crumbs from the
        // prior session on disk.  The active bucket is locked by
        // Serilog's file sink; CloseAndFlush releases the lock so the
        // delete below can succeed.  After CloseAndFlush, Log.* calls
        // are no-ops; surface any delete failures via stderr instead.
        await Log.CloseAndFlushAsync();
        if (Directory.Exists(logsDir))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(logsDir, "*.txt"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        await Console.Error.WriteLineAsync(
                            $"[ClaudeForge] ClearAppData: failed to delete log {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException)
            {
                await Console.Error.WriteLineAsync(
                    $"[ClaudeForge] ClearAppData: failed to enumerate logs in {logsDir}: {ex.Message}");
            }
        }

        // Signal the classic-desktop lifetime to shut down. Falling back to
        // Environment.Exit if the lifetime is unavailable (e.g. a test
        // harness or a non-classic startup).
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// bumped every time Ctrl+F is pressed.  The View
    /// observes this property and calls <c>SearchBox.Focus()</c> on
    /// every change.  An int (not a bool) so consecutive Ctrl+F presses
    /// each fire a PropertyChanged event — a bool would elide after
    /// the first press (CommunityToolkit's source-gen setter skips
    /// equal assignments).
    /// </summary>
    [ObservableProperty] private int _searchFocusRequestId;

    /// <summary>
    /// Ctrl+F shortcut handler — wired via <c>&lt;KeyBinding Gesture="Ctrl+F"&gt;</c>
    /// in MainWindow.axaml.  Bumps <see cref="SearchFocusRequestId"/>
    /// so the View's subscriber moves keyboard focus to the search
    /// TextBox.  No-op if the search box isn't materialized (e.g. very
    /// early startup before the toolbar lays out).
    /// </summary>
    [RelayCommand]
    private void FocusSearch()
    {
        Log.Information("[App.Command] action=FocusSearch (Ctrl+F)");
        SearchFocusRequestId++;
    }

    /// <summary>
    /// User-initiated reload — bound to the toolbar Reload Window button and
    /// the F5 key shortcut via the source-gen <c>ReloadCommand</c>. Treats
    /// the action as semantically equivalent to "fresh app launch" from the
    /// banners' perspective: any per-session banner-dismiss flags reset so
    /// install / schema-errors banners re-surface if they're applicable.
    /// The actual workspace re-read is delegated to <see cref="ReloadCoreAsync"/>.
    /// <para>
    /// Other reload triggers — <see cref="OnSelectedProfileChanged"/> (profile
    /// switch), <c>OnFileChangedExternally</c> (FileSystemWatcher), and the
    /// post-restore flow — call <see cref="ReloadCoreAsync"/> directly so
    /// they keep the conservative no-reset behaviour. The user has already
    /// seen any dismissed banner; firing it again on every automatic reload
    /// would be hostile (file watchers can fire many times per minute).
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        // Reset per-session banner-dismiss flags. Install banner: clear so
        // ConfigureInstallBanner inside LoadAllWorkspacesAsync re-evaluates
        // ShowInstallBanner from scratch (it recomputes based on product-
        // detection + the dismiss flag every reload). Schema banner: clear
        // and explicitly raise PropertyChanged because IsSchemaErrorsBannerVisible
        // is a hand-rolled computed property — the [NotifyPropertyChangedFor]
        // on _schemaErrors won't fire if SchemaErrors itself doesn't change
        // during the reload.
        if (_bannerDismissedByUser)
        {
            _bannerDismissedByUser = false;
        }

        if (_schemaErrorsBannerDismissed)
        {
            _schemaErrorsBannerDismissed = false;
            OnPropertyChanged(nameof(IsSchemaErrorsBannerVisible));
        }

        await ReloadCoreAsync();
    }

    /// <summary>
    /// Workspace re-read shared by the user-driven Reload Window action
    /// (<see cref="ReloadAsync"/>) and the automatic triggers (profile
    /// switch, file watcher, post-restore). Does NOT reset banner-dismiss
    /// flags — that's the caller's responsibility based on user intent.
    /// </summary>
    private async Task ReloadCoreAsync()
    {
        // Permanent: log every reload entry.  Multiple callers — the toolbar
        // Reload button (user-driven, via ReloadAsync above), OnSelectedProfileChanged
        // (profile switch), OnFileChangedExternally (FileSystemWatcher), the post-
        // restore flow.  No source attribution here; surrounding [Profiles]
        // / [FileWatcher] context lines in the log identify which path
        // triggered the reload.
        Log.Information("[App.Command] action=Reload isLoading={IsLoading}", IsLoading);
        // If a reload is already in flight, mark pending and let the current one
        // restart after it finishes.  This prevents concurrent LoadAllWorkspacesAsync
        // calls racing over the shared _workspace / _desktopWorkspace fields.
        if (IsLoading)
        {
            _reloadPending = true;
            return;
        }

        // Reloading restores the save-dialog preference so users aren't permanently
        // locked out if they accidentally uncheck "Show this dialog on save".
        // Use the public property setter so the bound "Save dialog off" badge
        // (next to the Save toolbar button) updates on reload.
        ShowSaveChangesDialog = true;

        do
        {
            _reloadPending = false;
            IsLoading = true;
            SetStatusActive(Strings.StatusReloading);
            try
            {
                await LoadAllWorkspacesAsync();
                if (!_reloadPending)
                {
                    SetStatusSuccess(Strings.StatusReloaded);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Reload] Failed to reload workspaces");
                if (!_reloadPending)
                {
                    SetStatusFailure(string.Format(Strings.StatusReloadFailedFmt, SanitiseExceptionForStatus(ex)));
                }
            }
            finally
            {
                IsLoading = false;
            }
        } while (_reloadPending);
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        string? folder = await DialogServiceForViewAccess.PickFolderAsync(Strings.DialogTitleOpenProject);
        if (folder == null)
        {
            return;
        }

        Log.Information("[App.Command] action=OpenProject folder={Folder}", folder);

        ProjectRoot = folder;
        SaveWindowState();

        IsLoading = true;
        SetStatusActive(string.Format(Strings.StatusOpeningProjectFmt, Path.GetFileName(folder)));
        try
        {
            await LoadAllWorkspacesAsync();
            SetStatusState(string.Format(Strings.StatusProjectOpenFmt, Path.GetFileName(folder)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OpenProject] Failed to open project folder {Folder}", folder);
            SetStatusFailure(string.Format(Strings.StatusOpenProjectFailedFmt, SanitiseExceptionForStatus(ex, folder)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        // Once the user explicitly toggles, stop following the OS
        if (_isFollowingSystem)
        {
            _isFollowingSystem = false;
            IPlatformSettings? ps = Application.Current?.PlatformSettings;
            if (ps != null)
            {
                ps.ColorValuesChanged -= OnSystemThemeChanged;
            }
        }

        IsDarkTheme = !IsDarkTheme;
        Log.Information("[App.Command] action=ToggleTheme newValue={Theme}", IsDarkTheme ? "Dark" : "Light");
        Application app = Application.Current!;
        app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        SaveWindowState();
    }

    partial void OnSelectedNodeChanged(NavigationNodeViewModel? value)
    {
        // Deactivate the previously visible group editor so subsequent shared-scope
        // changes are deferred (lazy rebuild) rather than executed while hidden.
        // Also clear any active filter so the next visit starts with the full list.
        if (ActiveEditor is SettingsGroupEditorViewModel prevGroup)
        {
            prevGroup.Deactivate();
        }
        else if (ActiveEditor is EnvironmentEditorViewModel prevEnv)
        {
            prevEnv.FilterText = string.Empty;
        }

        // Manual navigation (not triggered by a deep link) clears the back stack
        // so the Back button disappears until the next deep link is followed.
        if (!_isDeepLinkNavigation)
        {
            _backNode = null;
            CanGoBack = false;
        }

        _isDeepLinkNavigation = false; // always reset — consumed exactly once

        // Tree was cleared programmatically (e.g. between workspace reloads):
        // leave the current editor in place; a real selection will arrive in a moment.
        if (value == null)
        {
            return;
        }

        // Selecting a header node ("Claude Code", "Claude Desktop") or any other
        // node without an Editor (e.g. the visual separator) clears ActiveEditor
        // so the Welcome view becomes visible. This gives the user a one-click
        // path back to the scope legend / editing-context overview without having
        // to reopen the app — and replaces the temporary --showWelcomeView debug
        // flag, which is no longer needed now that headers are interactive.
        ActiveEditor = value.Editor;

        // Activate the newly selected group editor; this flushes any pending
        // rebuild that accumulated while the page was not visible.
        if (value.Editor is SettingsGroupEditorViewModel newGroup)
        {
            newGroup.Activate();
        }
        else if (value.Editor is AgentsSkillsEditorViewModel agentsVm)
        {
            // Deferred first-load: the VM is constructed without an eager
            // disk scan so profile switches don't pay the filesystem cost when
            // the user never visits this page.  EnsureLoaded is idempotent.
            agentsVm.EnsureLoaded();
        }

        _lastNodeTitle = value.Title;
        SaveWindowState();
    }

    /// <summary>
    /// Returns to the page the user was on before following a deep link.
    /// Only available (CanGoBack == true) immediately after a deep-link navigation;
    /// any manual tree click in between clears the back state.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void NavigateBack()
    {
        if (_backNode == null)
        {
            return;
        }

        NavigationNodeViewModel? target = _backNode;
        _backNode = null;
        CanGoBack = false;
        SelectedNode = target;
    }

    /// <summary>
    /// Deep-links the sidebar selection to the Profiles node. Used by the toolbar ✏ button
    /// so users can reach profile management without scrolling through the nav tree.
    /// </summary>
    [RelayCommand]
    private void NavigateToProfiles()
    {
        NavigationNodeViewModel? profilesNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleProfiles);
        if (profilesNode != null)
        {
            SelectedNode = profilesNode;
        }
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        // Notify the ComboBox binding so it re-resolves the UnifiedProfileEntry from
        // the new string value — required when SelectedProfile is set from code rather
        // than via the ComboBox SelectedItem binding.
        OnPropertyChanged(nameof(SelectedProfileEntry));

        // reload-loop guard.  When LoadAllWorkspacesAsync raises
        // OnPropertyChanged(AvailableProfileEntries) near its tail (so the
        // toolbar ComboBox re-evaluates against fresh disk state), Avalonia's
        // TwoWay binding on SelectedItem can write back the freshly-resolved
        // record reference, which sets SelectedProfile (potentially to the
        // SAME string, but the source-gen equality skip can be defeated by
        // case differences between persisted state and disk-discovered
        // names).  Skip the reload kick AND the window-state write when
        // we're inside that bounce — the user didn't ask for a reload.
        if (_suppressProfileChangeReload)
        {
            Log.Debug("[Profiles] Suppressed reload-from-profile-change during in-flight reload (value={Value})",
                value);
            return;
        }

        // Permanent audit log of the user-driven profile switch (toolbar
        // dropdown or programmatic SelectedProfile = name).  Pairs with
        // [App.Command] action=Reload that fires immediately after.
        Log.Information("[App.UserEdit] action=SelectedProfileChanged newValue=\"{Value}\"", value ?? "(null)");

        // Profile change triggers a reload with the new profile context.
        // Uses ReloadCoreAsync (NOT ReloadAsync) because this is an automatic
        // trigger, not a user-clicked Reload Window action — dismissed banners
        // should stay dismissed across profile switches in the same session.
        // Persist before reloading so the selection survives a crash/restart.
        SaveWindowState();
        _ = ReloadCoreAsync();
    }


    [RelayCommand]
    private void SelectSearchResult(SearchResultViewModel result)
    {
        // Save current position so the Back button can return here.
        if (SelectedNode != result.Node)
        {
            _backNode = SelectedNode;
            CanGoBack = true;
            _isDeepLinkNavigation = true;
        }

        SelectedNode = result.Node;
        Search.IsSearchOpen = false;
        Search.SearchQuery = string.Empty;

        // Filter the target editor to the matched property so it's immediately
        // visible without scrolling — the user can clear the filter bar to see context.
        if (result.Node.Editor is SettingsGroupEditorViewModel groupEditor)
        {
            groupEditor.FilterText = result.PropertyKey;

            PermissionsEditorViewModel? permEditor = groupEditor.Editors
                                                                .OfType<PermissionsEditorViewModel>()
                                                                .FirstOrDefault();

            // For the synthetic --dangerouslySkipPermissions result, activate the
            // contextual amber hint banner AND open the Advanced accordion — the
            // related safety control (disableBypassPermissionsMode) lives there, and
            // the synthetic hit carries an empty PropertyKey so the filter-based
            // check below won't catch it.
            if (result.IsSynthetic && permEditor is not null
                && string.Equals(result.PropertyKey, "permissions.defaultMode", StringComparison.Ordinal))
            {
                // Synthetic "bypass" hit: prompt the user to pick bypassPermissions
                // in Default Mode (Overview tab). Deep-link + hint only — we never
                // auto-select a dangerous mode for the user.
                permEditor.ActivateBypassHint();
                groupEditor.SelectTab(GroupTab.PropertiesId);
            }
            else if (result.IsSynthetic && permEditor is not null)
            {
                permEditor.ActivateDangerHint();
                // Land on Overview (the Advanced accordion + Default Mode live there)
                // regardless of the remembered tab, then expand Advanced.
                groupEditor.SelectTab(GroupTab.PropertiesId);
                RequestExpandPermissionsAdvanced(permEditor);
            }

            // A hit on an advanced permission setting (disableBypassPermissionsMode /
            // additionalDirectories) must pop the Overview "Advanced" accordion so
            // the deep-linked control is visible, not hidden behind a closed section.
            if (permEditor is not null && IsAdvancedPermissionFilter(result.PropertyKey))
            {
                groupEditor.SelectTab(GroupTab.PropertiesId);
                RequestExpandPermissionsAdvanced(permEditor);
            }
        }
        // Essentials page deep-link.  Synthetic search hits
        // routed at the Essentials node carry the target card id in
        // PropertyKey; activate the matching card's amber callout so the
        // user knows which card their query matched.
        else if (result.IsSynthetic
                 && result.Node.Editor is EssentialsViewModel essentials
                 && !string.IsNullOrEmpty(result.PropertyKey))
        {
            essentials.ActivateAmberCalloutFor(result.PropertyKey);
        }
    }

    /// <summary>
    /// Open the permissions "Advanced" accordion for a deep-link. Sets the flag
    /// immediately AND re-applies once the navigation's view rebuild has settled
    /// (<see cref="DispatcherPriority.Loaded"/>): selecting a node triggers an
    /// editor/tab rebuild that re-materializes the bound Expander, which can land
    /// AFTER the synchronous set and leave it collapsed. The deferred re-apply
    /// runs after layout so the final state is expanded.
    /// </summary>
    private static void RequestExpandPermissionsAdvanced(PermissionsEditorViewModel? perms)
    {
        if (perms is null)
        {
            Log.Information("[DeepLink] Permissions editor not found; cannot expand Advanced accordion.");
            return;
        }

        perms.IsAdvancedExpanded = true;
        Dispatcher.UIThread.Post(() => perms.IsAdvancedExpanded = true, DispatcherPriority.Loaded);
        Log.Information("[DeepLink] Requested permissions Advanced accordion expansion.");
    }

    /// <summary>
    /// Handles deep-link navigation from clickable env-var tokens in property descriptions.
    /// Switches the active editor to Environment and pre-selects the variable if visible.
    /// Saves the current node so the Back button can return here.
    /// </summary>
    private void OnNavigateToEnvVar(NavigateToEnvVarMessage msg)
    {
        NavigationNodeViewModel? envNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleEnvironment);
        if (envNode == null)
        {
            return;
        }

        // Save current position for Back navigation (only if we're moving away).
        if (SelectedNode != envNode)
        {
            _backNode = SelectedNode;
            CanGoBack = true;
            _isDeepLinkNavigation = true;
        }

        SelectedNode = envNode;

        if (ActiveEditor is EnvironmentEditorViewModel envVm)
        {
            envVm.NavigateTo(msg.VarName);
        }
    }

    /// <summary>
    /// Deep-link from the Essentials page's "View in &lt;group&gt;"
    /// button.  Finds the navigation node whose Title matches
    /// <see cref="NavigateToNavGroupMessage.GroupTitle"/> (top-level OR
    /// child of any header), selects it, and applies the optional
    /// <see cref="NavigateToNavGroupMessage.PropertyFilter"/> to the
    /// target editor.  Silently no-ops when no matching node is found.
    /// <para>
    /// Searches in this order: top-level synthetic nodes (Environment,
    /// Memory, etc.) → children of every header (Claude Code groups,
    /// Claude Desktop groups).  The first match wins, so a future
    /// rename collision should be caught by the
    /// <c>NavGroupTitlesAreUnique</c> guard test.
    /// </para>
    /// </summary>
    private void OnNavigateToNavGroup(NavigateToNavGroupMessage msg)
    {
        if (string.IsNullOrEmpty(msg.GroupTitle))
        {
            return;
        }

        NavigationNodeViewModel? target = FindNavNodeByTitle(msg.GroupTitle);
        if (target is null)
        {
            return;
        }

        // Save current position for Back navigation (only if we're moving away).
        if (SelectedNode != target)
        {
            _backNode = SelectedNode;
            CanGoBack = true;
            _isDeepLinkNavigation = true;
        }

        SelectedNode = target;

        // Apply the property filter if the target editor supports one.
        // Schema-driven groups expose FilterText on SettingsGroupEditorViewModel.
        // Specialized editors (Environment, Memory) handle their own filter
        // surfaces; for those, the filter on Essentials cards is left empty.
        if (!string.IsNullOrEmpty(msg.PropertyFilter))
        {
            if (target.Editor is SettingsGroupEditorViewModel groupEditor)
            {
                groupEditor.FilterText = msg.PropertyFilter;

                // Advanced permission settings (disableBypassPermissionsMode,
                // additionalDirectories) live inside the permissions compound
                // editor's collapsed "Advanced" accordion on the Overview tab.
                // The filter surfaces the editor (sub-path fallback), but the
                // accordion would stay closed — expand it so the deep-linked
                // control is actually visible.
                if (IsAdvancedPermissionFilter(msg.PropertyFilter))
                {
                    groupEditor.SelectTab(GroupTab.PropertiesId);
                    RequestExpandPermissionsAdvanced(
                        groupEditor.Editors.OfType<PermissionsEditorViewModel>().FirstOrDefault());
                }
            }
            else if (target.Editor is EnvironmentEditorViewModel envEditor)
            {
                envEditor.NavigateTo(msg.PropertyFilter);
            }
        }
    }

    /// <summary>
    /// Merge the schema-extracted env-var suggestions (parsed from
    /// property descriptions) with the SDK's hand-curated list of
    /// well-known keys.  Sorted ordinal + de-duplicated so the
    /// Environment editor's add-row picker lists each key once.
    /// </summary>
    private static IReadOnlyList<string> MergeSuggestedEnvVars(
        IReadOnlyList<string> fromSchema,
        IReadOnlyList<string> fromSdk)
    {
        SortedSet<string> merged = new(StringComparer.Ordinal);
        foreach (string v in fromSchema)
        {
            merged.Add(v);
        }

        foreach (string v in fromSdk)
        {
            merged.Add(v);
        }

        return [..merged];
    }

    /// <summary>
    /// Walks the navigation tree depth-1 (header → children) plus the
    /// top-level synthetic nodes (Essentials, Effective Settings, Profiles,
    /// Backup / Restore, Environment, Memory) and returns the first node
    /// whose Title matches <paramref name="title"/> exactly.  Used by the
    /// <c>NavigateToNavGroupMessage</c> handler.
    /// </summary>
    // The permissions "Advanced" accordion houses these keys; a deep-link to one
    // should pop the accordion open (see OnNavigateToNavGroup).
    private static bool IsAdvancedPermissionFilter(string? filter) =>
        filter is not null &&
        (filter.Contains("disableBypassPermissionsMode", StringComparison.Ordinal) ||
         filter.Contains("additionalDirectories", StringComparison.Ordinal));

    private NavigationNodeViewModel? FindNavNodeByTitle(string title)
    {
        // Top-level pass — synthetic nodes (Essentials, Effective Settings,
        // Profiles, Environment, Memory, Backup) all live here.
        foreach (NavigationNodeViewModel node in NavigationTree)
        {
            if (string.Equals(node.Title, title, StringComparison.Ordinal)
                && node.Editor is not null)
            {
                return node;
            }
        }

        // Child pass — Claude Code / Claude Desktop schema groups.
        foreach (NavigationNodeViewModel node in NavigationTree)
        {
            foreach (NavigationNodeViewModel child in node.Children)
            {
                if (string.Equals(child.Title, title, StringComparison.Ordinal)
                    && child.Editor is not null)
                {
                    return child;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Exports the fully-merged effective configs into a single <c>.zip</c> archive.
    /// The archive contains <c>ClaudeCode/.claude/settings.json</c> and/or
    /// <c>ClaudeDesktop/claude_desktop_config.json</c> (depending on what is loaded),
    /// plus a <c>manifest.json</c> identifying the export and its source.
    /// </summary>
    /// <remarks>
    /// Piggy-backs on the same <see cref="ZipArchiveWriter"/> used by the Backup /
    /// Restore feature so archive output is uniform across the app. The format change
    /// from loose folder → single zip is a deliberate one-time UX improvement.
    /// </remarks>
    [RelayCommand]
    private async Task ExportAsync()
    {
        string suggestedName = $"claude-export-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        string? destination = await DialogServiceForViewAccess.PickSaveFileAsync(
            Strings.DialogTitleExportConfigs,
            suggestedName,
            [new FilePickerFilter(Strings.FileFilterZipArchive, ["zip"])]);
        if (destination == null)
        {
            return;
        }

        if (ClaudeCodeSdk is null && ClaudeDesktopSdk is null)
        {
            SetStatusWarning(Strings.StatusNothingToExport);
            return;
        }

        try
        {
            string comment = MakeHeaderComment();

            await using (ZipArchiveWriter writer = ZipArchiveWriter.Create(destination))
            {
                ExportManifest manifest = new()
                {
                    CreatedUtc = DateTime.UtcNow,
                    Platform = PlatformPaths.PlatformId,
                    AppVersion = AppVersion,
                    IncludesClaudeCode = ClaudeCodeSdk is not null,
                    IncludesClaudeDesktop = ClaudeDesktopSdk is not null,
                    HeaderComment = comment,
                };

                if (ClaudeCodeSdk is not null)
                {
                    JsonObject stamped = EffectiveConfigBuilder.Stamp(
                        ClaudeCodeSdk.ComputeEffectiveSnapshot(), comment);
                    writer.AddTextEntry(
                        "ClaudeCode/.claude/settings.json",
                        stamped.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }

                if (ClaudeDesktopSdk is not null)
                {
                    JsonObject stamped = EffectiveConfigBuilder.Stamp(
                        ClaudeDesktopSdk.ComputeEffectiveSnapshot(), comment);
                    writer.AddTextEntry(
                        "ClaudeDesktop/claude_desktop_config.json",
                        stamped.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }

                writer.AddTextEntry("manifest.json", ZipArchiveWriter.SerialiseExportManifest(manifest));
                await writer.CommitAsync();
            }

            SetStatusSuccess(string.Format(Strings.StatusExportedFmt, Path.GetFileName(destination)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Export] Failed to export settings to {Destination}", destination);
            SetStatusFailure(string.Format(Strings.StatusExportFailedFmt, SanitiseExceptionForStatus(ex, destination)));
        }
    }

    // -----------------------------------------------------------------------
    // Window state (called from MainWindow code-behind)
    // -----------------------------------------------------------------------

    public void ApplyRestoredTheme()
    {
        Application app = Application.Current!;

        if (_isFollowingSystem)
        {
            // Sync immediately, then follow OS changes
            ApplyOsTheme(app);
            IPlatformSettings? ps = app.PlatformSettings;
            if (ps != null)
            {
                ps.ColorValuesChanged += OnSystemThemeChanged;
            }
        }
        else
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private void ApplyOsTheme(Application app)
    {
        bool isDark = app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        IsDarkTheme = isDark;
        app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void OnSystemThemeChanged(object? sender, PlatformColorValues e)
    {
        if (!_isFollowingSystem)
        {
            return;
        }

        bool isDark = e.ThemeVariant == PlatformThemeVariant.Dark;
        IsDarkTheme = isDark;
        Application? app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    /// <summary>
    /// Set by <see cref="ClearAppDataAsync"/> just before exit so the
    /// <c>OnClosed → SaveWindowState</c> chain in MainWindow.axaml.cs
    /// (and any other deferred persisters) does not re-create the file
    /// we just deleted. Read-only outside this VM.
    /// </summary>
    private bool _suppressStateSave;

    public void SaveWindowState(double? width = null, double? height = null,
                                double? x = null, double? y = null)
    {
        if (_suppressStateSave)
        {
            return;
        }

        // Update the cached geometry whenever real values are provided so that
        // no-arg calls (from node selection, profile changes, etc.) persist the
        // last-known window size rather than reverting to the 1200×750 defaults.
        if (width.HasValue)
        {
            _savedWidth = width.Value;
        }

        if (height.HasValue)
        {
            _savedHeight = height.Value;
        }

        if (x.HasValue)
        {
            _savedX = x;
        }

        if (y.HasValue)
        {
            _savedY = y;
        }

        // Mutate _cachedState in place. Backup-related fields
        // (BackupDirectory, RestoreDirectory, IncludeCredentialsInBackup,
        // LastBackupUtc) are owned by OnBackupStateChanged below, which writes
        // into the same cached object — no disk re-read needed to preserve them.
        _cachedState.Width = _savedWidth;
        _cachedState.Height = _savedHeight;
        _cachedState.X = _savedX;
        _cachedState.Y = _savedY;
        _cachedState.LastSelectedNodeTitle = _lastNodeTitle;
        _cachedState.ProjectRoot = ProjectRoot;
        _cachedState.Theme = _isFollowingSystem ? "System" : IsDarkTheme ? "Dark" : "Light";
        _cachedState.SelectedProfile = SelectedProfile;
        _cachedState.NavHeaderExpanded = new Dictionary<string, bool>(_navHeaderExpanded);

        // Defensive backstop for the "backup folder lost on
        // relaunch" bug.  The primary owner of these fields IS
        // OnBackupStateChanged (it sync-mutates `_cachedState.BackupDirectory`
        // when the user picks a folder), but the event flow could be
        // disrupted by:
        //   • A handler exception that bubbles before the cache mutation.
        //   • A Reload-Window race that recreates _backupVm subscriptions
        //     across the PersistentStateChanged event boundary.
        //   • Future maintenance accidentally breaking the
        //     PersistentStateChanged → OnBackupStateChanged plumbing.
        // Pulling LIVE values from _backupVm here guarantees that any
        // SaveWindowState call (including the OnClosed shutdown hook)
        // picks up the user's actual current selection — self-healing on
        // every save, not just on the explicit change event.  The same
        // !IsNullOrEmpty guard is used so a transient empty value during
        // mid-reload doesn't clobber a previously-saved path.
        if (_backupVm is not null)
        {
            if (!string.IsNullOrEmpty(_backupVm.BackupDirectory))
            {
                _cachedState.BackupDirectory = _backupVm.BackupDirectory;
            }

            if (!string.IsNullOrEmpty(_backupVm.RestoreDirectory))
            {
                _cachedState.RestoreDirectory = _backupVm.RestoreDirectory;
            }

            // Credentials preference + LastBackupUtc are also VM-owned, but
            // are nullable by design (null = "never asked" / "never backed up")
            // so they ALWAYS overwrite — no IsNullOrEmpty guard.
            _cachedState.IncludeCredentialsInBackup = _backupVm.CredentialsPreference;
            _cachedState.LastBackupUtc = _backupVm.LastBackupUtc;
        }

        WindowStateService.Save(_cachedState);
    }

    public SavedWindowGeometry GetSavedGeometry()
    {
        return new SavedWindowGeometry(_cachedState.Width, _cachedState.Height, _cachedState.X, _cachedState.Y);
    }

    /// <summary>
    /// Persists backup-related preferences (credentials choice + last-backup time) back
    /// to <c>ClaudeForge-gui-state.json</c>. Invoked whenever <see cref="BackupRestoreViewModel"/>
    /// raises <see cref="BackupRestoreViewModel.PersistentStateChanged"/>.
    /// </summary>
    /// <remarks>
    /// Debounced: <c>Refresh()</c> fires the event up to three times in rapid
    /// succession (BackupDirectory, RestoreDirectory, credentials). We cancel any
    /// pending save and schedule a new one after a 150 ms quiet period so all three
    /// changes collapse into a single disk write.
    /// </remarks>
    private void OnBackupStateChanged(object? sender, EventArgs e)
    {
        if (sender is not BackupRestoreViewModel vm)
        {
            return;
        }

        // Capture VM values on the UI thread before the async continuation.
        bool? credPref = vm.CredentialsPreference;
        DateTime? lastBackup = vm.LastBackupUtc;
        string backupDir = vm.BackupDirectory;
        string restoreDir = vm.RestoreDirectory;

        // Mutate _cachedState SYNCHRONOUSLY so any concurrent
        // SaveWindowState() (geometry change, node selection, app shutdown)
        // serialises the latest backup-state values immediately.  Pre-fix the
        // mutation happened INSIDE the 150 ms Task.Delay below, which meant:
        //
        //   T=0   user picks folder → schedule debounced save
        //   T=10  geometry-change SaveWindowState() runs, serialises
        //         _cachedState where BackupDirectory is STILL NULL
        //   T=20  user closes the window → debounced task gets cancelled
        //         OR runs after the process is gone
        //   Next launch: state file shows null backupDirectory.
        //
        // Now the cache mutation is sync; only the disk WRITE is debounced
        // (so chatty multi-property updates collapse to one write per quiet
        // window).  SaveWindowState() always sees the latest values.
        //
        // The !IsNullOrEmpty guard stays: an empty string during mid-reload
        // must not clobber a previously saved path.
        _cachedState.IncludeCredentialsInBackup = credPref;
        _cachedState.LastBackupUtc = lastBackup;
        if (!string.IsNullOrEmpty(backupDir))
        {
            _cachedState.BackupDirectory = backupDir;
        }

        if (!string.IsNullOrEmpty(restoreDir))
        {
            _cachedState.RestoreDirectory = restoreDir;
        }

        // Capture-and-dispose the previous debounce CTS so we don't leak handles
        // across rapid VM updates. Same pattern as OnSearchQueryChanged above.
        CancellationTokenSource? previousCts = _backupStateSaveCts;
        _backupStateSaveCts = new CancellationTokenSource();
        CancellationToken ct = _backupStateSaveCts.Token;
        previousCts?.Cancel();
        previousCts?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // Debounced disk write only — cache mutation already happened
                // above.  Even if THIS task is cancelled, the cache mutation
                // already happened, and any subsequent SaveWindowState() (or
                // the OnClosed shutdown hook) will persist the correct value.
                WindowStateService.Save(_cachedState);
            }
            catch (OperationCanceledException)
            {
                /* superseded by a newer change — normal */
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BackupStateSave] Failed to persist backup state");
            }
        }, ct);
    }

    // -----------------------------------------------------------------------
    // Initialization
    // -----------------------------------------------------------------------

    /// <summary>
    /// Subscribes to the workspaces' <see cref="SettingsWorkspace.Changed"/> event
    /// so any in-memory edit enables the Save button.
    /// Old subscriptions are always removed first to prevent duplicates across reloads.
    /// </summary>
    /// <summary>
    /// Refreshes the scope-selector dropdown lists to show only scopes that have a
    /// loaded document in the current workspace.  Called after every workspace reload.
    ///
    /// Without a project open, the workspace only contains User (and optionally Managed)
    /// documents — offering Project/Local in the dropdown would immediately throw
    /// "No document loaded for scope X" on the first edit attempt.
    /// </summary>
    private void UpdateScopeContextScopes()
    {
        // Source from the SDK client's EditableScopes (4.3.7 step 7).
        // Pre-load fallback: at least User so the ComboBox has a sensible default.
        _ccScopeContext.AvailableScopes = ClaudeCodeSdk?.EditableScopes ?? [ConfigScope.User];
        _dtScopeContext.AvailableScopes = [ConfigScope.User]; // Desktop is always User-only

        // If the previously selected scope is no longer available (e.g. a project was
        // closed), fall back to User so the UI doesn't retain a stale selection.
        if (!_ccScopeContext.AvailableScopes.Contains(_ccScopeContext.EditingScope))
        {
            _ccScopeContext.EditingScope = ConfigScope.User;
        }

        // Desktop is always single-scope (User only).  Unconditionally snap the
        // editing scope back to User on every reload so any stale value that crept
        // in (e.g. via an Avalonia binding artefact during a DataContext switch)
        // is cleared before the nav tree is rebuilt.
        _dtScopeContext.EditingScope = ConfigScope.User;
    }

    /// <summary>
    /// Wire dirty-tracking onto the SDK clients' <see cref="Sdk.IClaudeConfigClient.Changed"/>
    /// event. the SDK forwards every workspace mutation
    /// (SDK-initiated or via direct <c>workspace.SetValue</c> from the editor
    /// live-write loop), so MWVM only needs to subscribe in one place.
    /// </summary>
    /// <remarks>
    /// Falls back to the legacy <c>workspace.Changed</c> wiring when the SDK
    /// clients aren't constructed yet — only possible during the brief window
    /// before <see cref="LoadAllWorkspacesAsync"/> runs. After load both
    /// products' SDK clients are guaranteed non-null.
    /// </remarks>
    private void SubscribeWorkspaceChangedEvents()
    {
        if (ClaudeCodeSdk is not null)
        {
            ClaudeCodeSdk.Changed += OnSdkClientChanged;
        }

        if (ClaudeDesktopSdk is not null)
        {
            ClaudeDesktopSdk.Changed += OnSdkClientChanged;
        }
    }

    private void UnsubscribeWorkspaceChangedEvents()
    {
        if (ClaudeCodeSdk is not null)
        {
            ClaudeCodeSdk.Changed -= OnSdkClientChanged;
        }

        if (ClaudeDesktopSdk is not null)
        {
            ClaudeDesktopSdk.Changed -= OnSdkClientChanged;
        }
    }

    /// <summary>
    /// Adapter from the SDK's <c>EventHandler&lt;ClientChangedEventArgs&gt;</c>
    /// signature to the existing MWVM dirty-tracking handler. The SDK's event
    /// kind is observed (Mutation / Saved / Reloaded) but the dirty
    /// recomputation is identical for all three.
    /// </summary>
    /// <remarks>
    /// Phase 1.1 — UI-thread dispatch. The SDK raises <see cref="Sdk.IClaudeConfigClient.Changed"/>
    /// synchronously from whatever thread invoked the mutation. Most callers
    /// are on the UI thread (editor property changes, Save command), but
    /// file-watcher reloads, backup/restore, and profile-switch paths can
    /// fire from a worker thread or a <c>Task.Run</c> continuation. Writing
    /// <see cref="HasUnsavedChanges"/> off-thread would emit
    /// <c>PropertyChanged</c> on the wrong thread, breaking the Save button
    /// binding under Avalonia's strict thread-affinity check. Marshal back
    /// to the UI thread when needed.
    /// <para>
    /// In test contexts (no <see cref="Application.Current"/>) there is no
    /// running dispatcher to drain a posted action; fall through to a
    /// synchronous write so existing unit tests that call
    /// <c>client.SetValue(...)</c> and then immediately assert against
    /// <c>vm.HasUnsavedChanges</c> still observe the update.
    /// </para>
    /// </remarks>
    private void OnSdkClientChanged(object? sender, ClientChangedEventArgs e)
    {
        // Synchronous when:
        //   (a) we're on the UI thread (the common case), OR
        //   (b) Avalonia isn't running — there's no Post pump to drain,
        //       so a Post would silently drop the update.
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            HasUnsavedChanges = ComputeHasActualChanges();
        }
        else
        {
            Dispatcher.UIThread.Post(() => HasUnsavedChanges = ComputeHasActualChanges());
        }
    }

    /// <summary>
    /// True when either product's SDK client has structural diffs against
    /// its baseline. Pre-load (before <see cref="LoadAllWorkspacesAsync"/>
    /// runs) returns <see langword="false"/> — nothing is loaded so nothing
    /// can be dirty.
    /// <para>
    /// <strong>Why <see cref="SettingsDocument.HasActualChanges"/> rather
    /// than <see cref="SettingsWorkspace.DirtyDocuments"/>:</strong> the
    /// SDK's <c>HasUnsavedChanges</c> probes <c>doc.HasActualChanges()</c>,
    /// which performs a structural <c>JsonNode.DeepEquals</c> against the
    /// baseline. The latched <c>IsDirty</c> flag stays true after a
    /// set-then-reset cycle even when the JSON is structurally identical to
    /// baseline; using it here would leave Save enabled after a Reset,
    /// which is the symptom users hit on the MCP / Permissions / Hooks
    /// pages before the structural-compare fix landed.
    /// </para>
    /// </summary>
    private bool ComputeHasActualChanges()
    {
        return (ClaudeCodeSdk?.HasUnsavedChanges ?? false) ||
               (ClaudeDesktopSdk?.HasUnsavedChanges ?? false);
    }

    /// <summary>
    /// Test seam: returns the live <see cref="Sdk.ClaudeConfigClientCore"/>
    /// so tests can drive <c>SetValue</c> / <c>RemoveValue</c> through the
    /// SDK and see the same Changed-event flow that production code does.
    /// Returns <see langword="null"/> before
    /// <see cref="LoadAllWorkspacesAsync"/> populates it.
    /// </summary>
    /// <remarks>
    /// Marked <c>internal</c> + uses <c>InternalsVisibleTo("Bennewitz.Ninja.ClaudeForge.Tests")</c>
    /// in the csproj so it does not pollute the public surface. The legacy
    /// <c>GetClaudeCodeWorkspaceForTesting()</c> seam was retired in step 15
    /// — every test path now flows through the SDK.
    /// </remarks>
    internal ClaudeConfigClientCore? GetClaudeCodeSdkClientForTesting()
    {
        return ClaudeCodeSdk;
    }

    /// <summary>
    /// Test seam: returns the persistent <see cref="BackupRestoreViewModel"/>
    /// instance.  H-2 contract is that this reference survives across
    /// <see cref="LoadAllWorkspacesAsync"/> calls (the nav-tree rebuild
    /// reuses the cached field rather than constructing a new VM).  Tests
    /// capture the reference, trigger reload, and assert <c>AreSame</c>.
    /// Returns <see langword="null"/> before the first build.
    /// </summary>
    internal BackupRestoreViewModel? GetBackupVmForTesting()
    {
        return _backupVm;
    }

    /// <summary>
    /// Test seam: returns the persistent Claude Code <see cref="AboutEditorViewModel"/>
    /// instance.  H-2 contract — same as <see cref="GetBackupVmForTesting"/>.
    /// </summary>
    internal AboutEditorViewModel? GetAboutCodeVmForTesting()
    {
        return _aboutCodeVm;
    }

    /// <summary>
    /// Test seam: returns the persistent Claude Desktop <see cref="AboutEditorViewModel"/>
    /// instance.  H-2 contract — same as <see cref="GetBackupVmForTesting"/>.
    /// </summary>
    internal AboutEditorViewModel? GetAboutDesktopVmForTesting()
    {
        return _aboutDesktopVm;
    }

    /// <summary>
    /// Test seam: returns the persistent <see cref="ProfilesViewModel"/>
    /// instance.  H-2 contract — same as <see cref="GetBackupVmForTesting"/>.
    /// </summary>
    internal ProfilesViewModel? GetProfilesVmForTesting()
    {
        return _profilesVm;
    }

    /// <summary>
    /// Test seam: returns the persistent <see cref="EssentialsViewModel"/>
    /// instance.  H-2 contract — same as <see cref="GetBackupVmForTesting"/>.
    /// </summary>
    internal EssentialsViewModel? GetEssentialsVmForTesting()
    {
        return _essentialsVm;
    }

    /// <summary>
    /// Loads (or reloads) both Claude Code and Claude Desktop workspaces from
    /// disk and rebuilds the navigation tree. Internal so that the H-1
    /// transactional-reload tests can drive it directly without spinning
    /// up the full app event loop.
    /// </summary>
    internal async Task LoadAllWorkspacesAsync()
    {
        // Transactional reload.
        //
        // Reload runs in two phases:
        //   PHASE 1 — out-of-band candidate build.  Schema + node + file-
        //     discovery work, plus the throw-prone JSON parses, all happen
        //     BEFORE we touch any in-memory SDK state.  If any parse fails
        //     (truncate-then-rewrite from an external editor producing
        //     briefly-malformed JSON, an aggressive permissions-denied,
        //     etc.), we surface a status message and bail without
        //     disturbing the existing workspace.
        //   PHASE 2 — destructive swap.  Detach listeners, dispose old
        //     SDK clients, install candidates, re-subscribe.  No throw
        //     points; if we got here both candidates parsed cleanly.
        //
        // Prior shape detached listeners + disposed each SDK in an
        // interleaved sequence with the loads.  An external save that
        // landed an invalid intermediate JSON between the watcher fire
        // and our parse would throw partway through — leaving SDKs
        // half-disposed and listeners half-attached.  A subsequent save
        // could then write a partial / empty workspace to disk.

        // Both product sections are always loaded regardless of installation state.
        // This lets users create or edit config files for machines that do have Claude
        // installed (e.g. setting up a new machine, sharing configs with colleagues)
        // and ensures Export is always functional.  The install-guidance banner is
        // shown separately when neither product is detected on this machine.

        // ── PHASE 1 — out-of-band candidate build ───────────────────────────

        // Resolve the selected profile entry — fall back to Global when nothing is selected.
        UnifiedProfileEntry entry = SelectedProfileEntry ?? UnifiedProfileEntry.Global;

        // Schemas + node trees (don't depend on file content, no per-load throw).
        // --showAllNew debug flag forces every node to render with
        // the "✨ NEW" badge regardless of the on-disk snapshot.  Used by QA
        // / screenshots to exercise badge styling without bumping the schema.
        bool flagAllAsNew = DebugFlags.ShowAllNewBadges;

        JsonSchemaNode ccSchema = await _schemaRegistry.GetClaudeCodeSettingsNodeAsync();
        HashSet<string> ccKnown = _snapshotService.LoadSnapshot("claude-code-settings");
        IReadOnlyList<SchemaNode> ccNodes = SchemaTreeBuilder.BuildTopLevel(ccSchema, ccKnown, flagAllAsNew);
        _renderedPathsBySchema["claude-code-settings"] = SchemaTreeBuilder.CollectPaths(ccNodes);
        if (ccKnown.Count == 0)
        {
            _snapshotService.SaveSnapshot("claude-code-settings", _renderedPathsBySchema["claude-code-settings"]);
        }

        JsonSchemaNode dtSchema = await _schemaRegistry.GetClaudeDesktopConfigNodeAsync();
        HashSet<string> dtKnown = _snapshotService.LoadSnapshot("claude-desktop-config");
        IReadOnlyList<SchemaNode> dtNodes = SchemaTreeBuilder.BuildTopLevel(dtSchema, dtKnown, flagAllAsNew);
        _renderedPathsBySchema["claude-desktop-config"] = SchemaTreeBuilder.CollectPaths(dtNodes);
        if (dtKnown.Count == 0)
        {
            _snapshotService.SaveSnapshot("claude-desktop-config", _renderedPathsBySchema["claude-desktop-config"]);
        }

        // File discovery for both products (resolves which on-disk files
        // contribute to each workspace given the active profile).
        string? cliProfile = (!entry.IsGlobal && entry.HasCli) ? entry.Name : null;
        IReadOnlyList<DiscoveredFile> settingsFiles =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ProjectRoot, cliProfile);
        IReadOnlyList<DiscoveredFile> mcpFiles = ConfigFileDiscoverer.DiscoverMcpFiles(ProjectRoot, cliProfile);
        IReadOnlyList<DiscoveredFile> ccFiles = (IReadOnlyList<DiscoveredFile>)[..settingsFiles, ..mcpFiles];

        string? dtProfile = (!entry.IsGlobal && entry.HasDesktop) ? entry.Name : null;
        IReadOnlyList<DiscoveredFile> dtFiles = (IReadOnlyList<DiscoveredFile>)
            [ConfigFileDiscoverer.DiscoverDesktopConfig(dtProfile)];

        // Load BOTH candidate workspaces inside a single try/catch.  If
        // EITHER throws (malformed JSON, IO error, etc.), neither
        // candidate is installed and the existing in-memory workspace is
        // preserved untouched.
        SettingsWorkspace ccCandidate;
        SettingsWorkspace dtCandidate;
        try
        {
            ccCandidate = await ConfigFileLoader.LoadWorkspaceAsync(ccFiles);
            dtCandidate = await ConfigFileLoader.LoadWorkspaceAsync(dtFiles);
        }
        catch (Exception ex) when (ex is JsonException
                                       or IOException
                                       or UnauthorizedAccessException)
        {
            string path = ccFiles.FirstOrDefault(f => f.Exists)?.FilePath
                          ?? dtFiles.FirstOrDefault(f => f.Exists)?.FilePath
                          ?? "settings.json";
            Log.Warning(ex,
                "[Reload] Parse / IO failure on {File}; existing in-memory workspace preserved",
                path);
            SetStatusFailure(string.Format(
                Strings.StatusReloadFailedFmt,
                SanitiseExceptionForStatus(ex, path)));
            return; // bail BEFORE touching any state
        }

        // ── PHASE 2 — destructive swap (no throw points past here) ──────────

        // Detach change listeners from the about-to-be-replaced workspaces and reset the
        // dirty flag — fresh documents are always clean.
        UnsubscribeWorkspaceChangedEvents();
        HasUnsavedChanges = false;

        // Wrap the freshly-loaded workspaces in SDK clients.
        // the legacy `_workspace` field is retired; the
        // SDK client is the only state holder. On reload, dispose the
        // previous SDK client so its SemaphoreSlim doesn't leak. The
        // injected SchemaRegistry is owned by MWVM, so it survives.
        ClaudeCodeSdk?.Dispose();
        ClaudeCodeSdk = ClaudeCodeClient.FromExistingWorkspace(
            ccCandidate, ConfigScope.User, _schemaRegistry);

        ClaudeDesktopSdk?.Dispose();
        ClaudeDesktopSdk = ClaudeDesktopClient.FromExistingWorkspace(
            dtCandidate, ConfigScope.User, _schemaRegistry);

        // Recompute the install-guidance banner.
        //
        // The banner shows when neither product is detected.  But the user can
        // dismiss it (e.g. to pre-configure a machine before installing) — that
        // dismiss should survive reloads triggered by file-watcher, profile
        // switch, etc.  _bannerDismissedByUser tracks that intent.
        //
        // When a product is detected (config file appears after a Restore, app
        // binary becomes visible, etc.) we auto-clear the dismissed flag: the
        // user is no longer in the "neither installed" state, and if they later
        // remove both products again the banner should return.
        //
        // The --showInstallBanner debug flag forces the banner on unconditionally
        // (useful for UI testing the banner on a machine with Claude installed).
        bool neitherInstalled = !PlatformPaths.IsClaudeCodeInstalled && !PlatformPaths.IsDesktopInstalled;
        if (!neitherInstalled)
        {
            _bannerDismissedByUser = false; // product detected — reset so banner can re-show later
        }

        ShowInstallBanner = DebugFlags.ShowInstallBanner
                            || (neitherInstalled && !_bannerDismissedByUser);

        // Refresh the active-profile badges so they reflect the current pointer files.
        CliActiveProfileName = ProfileEngine.ReadCurrentProfileName();
        DesktopActiveProfileName = ProfileEngine.ReadCurrentDesktopProfileName();

        // Update the editing-scope selector to only show scopes with loaded documents.
        // Project and Local scopes are only loaded when a project folder is open; showing
        // them otherwise causes "No document loaded for scope X" errors on first edit.
        UpdateScopeContextScopes();

        // Wire up change listeners so any in-memory edit enables the Save button.
        SubscribeWorkspaceChangedEvents();

        BuildNavigationTree(ccNodes, dtNodes);
        SetupFileWatcher(ccFiles.Concat(dtFiles).ToList());

        // surface schema-violation banner after every reload.
        // Uses ValidateAllAsync (not ValidateAsync) so PRE-EXISTING violations
        // in the on-disk file are surfaced — the user might have edited the
        // file externally between sessions, and those errors are exactly what
        // the banner is meant to highlight. ValidateAsync's delta-only
        // semantics are correct for the pre-save flow but would silently
        // return empty here.
        // Wrapped in try/catch because validation is advisory at this point
        // (the user can still edit and save with a force flag); a failure to
        // validate (e.g. SchemaRegistry transient) must not block the UI.
        try
        {
            SchemaErrors = await ValidateAllOnDiskAsync();
            if (SchemaErrors.Count > 0)
            {
                Log.Information(
                    "[Schema] Post-reload validation found {Count} issue(s); banner shown",
                    SchemaErrors.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Schema] Post-reload validation threw; banner suppressed");
            SchemaErrors = [];
        }

        // Once-per-launch GitHub update check.  Fire-and-forget; runs on
        // the thread pool with its own try/catch wall.  Guarded by an
        // Interlocked latch so subsequent reloads within the same
        // process (profile switch, file-watcher reload, etc.) do NOT
        // re-trigger the check.
        KickOnceLaunchUpdateCheck();
    }

    /// <summary>
    /// Aggregate of <see cref="IClaudeConfigClient.ValidateAllAsync"/> across
    /// both product clients, returning every currently-invalid field in the
    /// loaded workspace (not just user-introduced deltas).  Backs the
    /// post-reload schema-violation banner.
    /// </summary>
    private async Task<IReadOnlyList<SchemaValidationError>> ValidateAllOnDiskAsync(
        CancellationToken ct = default)
    {
        List<SchemaValidationError> all = [];
        if (ClaudeCodeSdk is not null)
        {
            all.AddRange(await ClaudeCodeSdk.ValidateAllAsync(ct));
        }

        if (ClaudeDesktopSdk is not null)
        {
            all.AddRange(await ClaudeDesktopSdk.ValidateAllAsync(ct));
        }

        return all;
    }

    // Latch so the unsupported-shape heads-up is shown at most once per session,
    // not on every navigation-tree rebuild (scope switch, file-watcher reload).
    private bool _unsupportedShapesNoticeShown;

    /// <summary>
    /// Show one aggregated, non-fatal notice listing every setting that has no
    /// structured editor (rendered as raw JSON). No-op when nothing was reported
    /// or the notice has already been shown this session. The fields remain fully
    /// editable; this is purely informational.
    /// </summary>
    private void MaybeShowUnsupportedShapeNotice(UnsupportedShapeCollector collector)
    {
        if (_unsupportedShapesNoticeShown || !collector.HasAny)
        {
            return;
        }

        _unsupportedShapesNoticeShown = true;
        string body = string.Join(
            Environment.NewLine,
            collector.Snapshot().Select(s =>
                s.DisplayName is { Length: > 0 } name ? $"{s.JsonPath}    ({name})" : s.JsonPath));
        AvaloniaDiagnostics.ShowNonFatalNotice(
            UnsupportedShapeText.NoticeTitle, UnsupportedShapeText.NoticeHeader, body);
    }

    private void BuildNavigationTree(
        IReadOnlyList<SchemaNode> ccNodes,
        IReadOnlyList<SchemaNode> dtNodes)
    {
        // Dispose existing SettingsGroupEditorViewModels before discarding them
        // so they unsubscribe from the shared scope contexts and don't accumulate
        // as phantom listeners across workspace reloads.
        DisposeNavigationEditors();
        NavigationTree.Clear();

        Func<Task<string?>> browsePath = () => DialogServiceForViewAccess.PickFolderAsync();

        // Collects any schema property surfaced via the raw-JSON fallback (a shape
        // the factory can't classify) across BOTH product sections, so we can raise
        // one aggregated heads-up after the tree is built.
        UnsupportedShapeCollector unsupportedShapes = new();

        // Both sections are always shown — even without an active installation —
        // so users can create configs for other machines or prepare for first-time
        // setup. The SDK clients are always non-null here because
        // LoadAllWorkspacesAsync always populates them (files are empty-document
        // placeholders when the product is not installed). The backing
        // SettingsWorkspace is reached through ClaudeCodeSdk.WorkspaceForGui /
        // ClaudeDesktopSdk.WorkspaceForGui — it's the same object the SDK
        // wraps internally; the explicit accessor exists so MWVM doesn't
        // need a separate field.
        //
        // explicit null guards on WorkspaceForGui. A
        // partially-initialised SDK client (e.g. OpenAsync threw on
        // schema/marketplace fetch and was caught upstream) can return
        // null. Bang-operators here would throw NullReferenceException
        // deep inside BuildGroups; instead, render an empty group list
        // for the failed product and log a warning so the app stays usable.

        // --- Essentials (top of tree, synthetic — 2026-05-07) ---
        // Pinned curated cards for the high-importance Claude Code settings:
        // token budgets (env-var based), MCP auto-trust, sandbox, model,
        // effort, fast mode, auto-update channel, auto-memory, and the
        // disable-bypass-permissions safety knob.  See docs/ESSENTIALS-PAGE.md.
        // H-2: cached field — reload re-binds via RefreshAsync rather than
        // re-constructing, so synthetic-search amber callouts aren't dropped
        // by a file-watcher reload arriving between search-click and render.
        if (_essentialsVm is null)
        {
            _essentialsVm = new EssentialsViewModel(
                ClaudeCodeSdk,
                new DefaultEnvironmentProvider(),
                // WindowState-backed "Check for updates on launch" toggle.
                // The card writes back through MWVM's ObservableProperty
                // so persistence + _cachedState stay consistent — the
                // card itself doesn't know about WindowStateService.
                checkForUpdatesRead: () => CheckForUpdatesOnLaunch,
                checkForUpdatesWrite: v => CheckForUpdatesOnLaunch = v);
        }
        else
        {
            _ = _essentialsVm.RefreshAsync(ClaudeCodeSdk);
        }

        // Welcome node: top-of-tree orientation landing spot.
        // No Editor — selecting it leaves ActiveEditor=null, which renders
        // the existing WelcomeView orientation panel.  Default selection
        // on a fresh launch (see RestoreSelectedNode) so new users see the
        // orientation content instead of getting dropped straight into the
        // first Claude Code editor before they know what they're editing.
        // Icon: 🏠 (HOUSE) — already used as the Welcome view's heading
        // glyph (WelcomeView.axaml line ~30) so the tree row and the
        // header glyph match.
        // gated on the ShowWelcomeOnLaunch preference (checkbox
        // on the Welcome page itself); when the user has opted out, the
        // node is omitted from the tree entirely.  The Welcome page
        // remains reachable by selecting the "Claude Code" / "Claude
        // Desktop" header nodes — those have no Editor either.
        if (ShowWelcomeOnLaunch)
        {
            NavigationTree.Add(new NavigationNodeViewModel(NavTitleWelcome, "🏠", NavDescWelcome)
            {
                IsTopLevel = true,
                // Editor is intentionally null.
            });
        }

        // Icon: ★ (U+2605, BLACK STAR) — basic Unicode glyph supported by
        // Inter and most system fonts.  Earlier draft used ⭐ (U+2B50,
        // emoji-presentation star) which silently fell through to
        // missing-glyph on Linux when no system emoji font was installed
        // (CachyOS / COSMIC reproducer).  Plain stars render uniformly.
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleEssentials, "★", NavDescEssentialsTooltip)
        {
            Editor = _essentialsVm,
            IsTopLevel = true,
        });
        NavigationTree.Add(new NavigationNodeViewModel("─────────────") { IsDivider = true, IsTopLevel = true });

        // --- Claude Code section ---
        NavigationNodeViewModel ccHeader = new(NavTitleClaudeCode, "⚙", NavDescClaudeCode)
        {
            IsTopLevel = true,
        };
        ccHeader.IsExpanded = _navHeaderExpanded.GetValueOrDefault(NavTitleClaudeCode, true);
        ccHeader.PropertyChanged += OnNavHeaderPropertyChanged;

        SettingsWorkspace? ccWorkspace = ClaudeCodeSdk?.WorkspaceForGui;
        if (ClaudeCodeSdk is not null && ccWorkspace is not null)
        {
            IReadOnlyList<NavigationGroup> ccGroups = NavigationTreeBuilder.BuildGroups(
                ccNodes, ccWorkspace, browsePath, _ccScopeContext, ClaudeCodeSdk, unsupportedShapes);
            foreach (NavigationGroup group in ccGroups)
            {
                ccHeader.Children.Add(new NavigationNodeViewModel(group.Title) { Editor = group.Editor });
            }
        }
        else
        {
            Log.Warning("[NavigationTree] Claude Code SDK or workspace is null; rendering empty section");
        }

        // H-2: persistent About VM (lazy-init once, reuse across reloads).
        _aboutCodeVm ??= new AboutEditorViewModel(
            AboutProduct.ClaudeCode,
            dialogService: DialogServiceForViewAccess,
            shareService: _shareService);
        ccHeader.Children.Add(new NavigationNodeViewModel(NavTitleVersionInfo, "\u2139", NavDescVersionInfo)
        {
            Editor = _aboutCodeVm,
        });

        NavigationTree.Add(ccHeader);

        // --- Claude Desktop section ---
        NavigationNodeViewModel dtHeader = new(NavTitleClaudeDesktop, "🖥", NavDescClaudeDesktop)
        {
            IsTopLevel = true,
        };
        dtHeader.IsExpanded = _navHeaderExpanded.GetValueOrDefault(NavTitleClaudeDesktop, true);
        dtHeader.PropertyChanged += OnNavHeaderPropertyChanged;

        SettingsWorkspace? dtWorkspace = ClaudeDesktopSdk?.WorkspaceForGui;
        if (ClaudeDesktopSdk is not null && dtWorkspace is not null)
        {
            IReadOnlyList<NavigationGroup> dtGroups = NavigationTreeBuilder.BuildGroups(
                dtNodes, dtWorkspace, browsePath, _dtScopeContext, ClaudeDesktopSdk, unsupportedShapes);
            foreach (NavigationGroup group in dtGroups)
            {
                dtHeader.Children.Add(new NavigationNodeViewModel(group.Title) { Editor = group.Editor });
            }
        }
        else
        {
            Log.Warning("[NavigationTree] Claude Desktop SDK or workspace is null; rendering empty section");
        }

        // H-2: persistent About VM (lazy-init once, reuse across reloads).
        _aboutDesktopVm ??= new AboutEditorViewModel(
            AboutProduct.ClaudeDesktop,
            dialogService: DialogServiceForViewAccess,
            shareService: _shareService);
        dtHeader.Children.Add(new NavigationNodeViewModel(NavTitleVersionInfo, "\u2139", NavDescVersionInfo)
        {
            Editor = _aboutDesktopVm,
        });

        NavigationTree.Add(dtHeader);

        // One-time aggregated heads-up if any setting in either section has no
        // structured editor (raw-JSON fallback). No-op when the schema is fully
        // covered (the common case today) or after the first show this session.
        MaybeShowUnsupportedShapeNotice(unsupportedShapes);

        // --- Effective Settings ---
        NavigationTree.Add(new NavigationNodeViewModel("─────────────") { IsDivider = true, IsTopLevel = true });
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleEffectiveSettings, "📊", NavDescEffectiveSettings)
        {
            Editor = new EffectiveSettingsViewModel(ClaudeCodeSdk!, ProjectRoot, _shareService),
            IsTopLevel = true,
        });

        // --- Profiles ---
        // H-2: persistent VM. Callbacks are wired ONCE on first construction
        // (they capture MWVM by closure; subsequent reloads reuse the same
        // lambda instances which still observe current MWVM state via the
        // captured `this`). Refresh() is called on every BuildNavigationTree
        // so the disk-backed list reflects post-reload state.
        if (_profilesVm is null)
        {
            _profilesVm = new ProfilesViewModel(DialogServiceForViewAccess);
            _profilesVm.OnProfileApplied = async appliedName =>
            {
                // Sync the toolbar ComboBox so the GUI now edits the same profile the CLI uses.
                CliActiveProfileName = appliedName;
                SelectedProfile = appliedName;
                // guarded by _suppressProfileChangeReload (I14)
                // for the same reason as the LoadAllWorkspacesAsync tail:
                // the TwoWay-bound ComboBox can write back through
                // SelectedProfileEntry when ItemsSource refreshes, which
                // would re-fire OnSelectedProfileChanged → another
                // SaveWindowState + _ = ReloadCoreAsync() kick.  We're about
                // to await ReloadCoreAsync() below explicitly; the binding
                // bounce here is redundant work, not user intent.
                _suppressProfileChangeReload = true;
                try
                {
                    OnPropertyChanged(nameof(AvailableProfileEntries));
                }
                finally
                {
                    _suppressProfileChangeReload = false;
                }

                // Reload the workspace so the editor reflects the applied profile's content.
                // Uses ReloadCoreAsync (NOT ReloadAsync) — post-restore is an automatic
                // continuation of a user action that already happened, not a fresh
                // user-initiated Reload Window. Dismissed banners stay dismissed.
                await ReloadCoreAsync();
            };
            _profilesVm.OnProfileDeleted = deletedName =>
            {
                // Toolbar ComboBox needs refreshing in case the deleted profile was listed.
                // I14 guard: same reload-loop reasoning as LoadAllWorkspacesAsync and
                // OnProfileApplied — the binding bounce from refreshing
                // AvailableProfileEntries could trigger OnSelectedProfileChanged.
                _suppressProfileChangeReload = true;
                try
                {
                    OnPropertyChanged(nameof(AvailableProfileEntries));
                }
                finally
                {
                    _suppressProfileChangeReload = false;
                }

                // The CLI-active badge is already cleared inside ProfilesViewModel.DeleteAsync
                // when the deleted profile was CLI-active; re-read to be safe.
                CliActiveProfileName = ProfileEngine.ReadCurrentProfileName();
            };
            _profilesVm.OnProfileCreated = createdName =>
            {
                // refresh the toolbar dropdown so the new
                // profile appears immediately rather than only after the
                // next Apply/Delete or app restart.
                _ = createdName; // name not directly needed; AvailableProfileEntries re-queries disk
                // I14 guard: see OnProfileDeleted comment above.
                _suppressProfileChangeReload = true;
                try
                {
                    OnPropertyChanged(nameof(AvailableProfileEntries));
                }
                finally
                {
                    _suppressProfileChangeReload = false;
                }
            };
            _profilesVm.OnDesktopProfileApplied = appliedName => { DesktopActiveProfileName = appliedName; };
            _profilesVm.OnDesktopProfileDeleted = _ =>
            {
                DesktopActiveProfileName = ProfileEngine.ReadCurrentDesktopProfileName();
            };
        }

        _profilesVm.Refresh();

        // fire OnPropertyChanged AFTER nav-tree build so the
        // toolbar Editing-Profile ComboBox re-evaluates AvailableProfileEntries
        // against fresh disk state.  Pre-existing bug: the AXAML binding
        // evaluates AvailableProfileEntries at DataContext-set time
        // (in App.OnFrameworkInitializationCompleted) — which happens
        // BEFORE LoadAllWorkspacesAsync runs.  On a cold start where the
        // user created a profile in a previous session, the binding would
        // therefore evaluate against a stale ~/.claude/profiles/ directory
        // tree (since the Avalonia compiled binding caches the result and
        // only re-evaluates on PropertyChanged) and the new profile
        // wouldn't appear until something fired OnPropertyChanged
        // (typically Apply/Delete/SelectedProfile change).  Firing it here
        // guarantees a fresh re-evaluation after every workspace load
        // (cold start AND reload).  Logging the count for diagnostics.
        //
        // guarded by _suppressProfileChangeReload because the
        // ComboBox TwoWay-bound SelectedItem can write back the freshly-
        // resolved record reference (see field comment for the full
        // mechanism).  Without the guard, every reload re-armed the next
        // one.  Try/finally so the flag is cleared even if a downstream
        // notification handler throws.
        _suppressProfileChangeReload = true;
        try
        {
            OnPropertyChanged(nameof(AvailableProfileEntries));
        }
        finally
        {
            _suppressProfileChangeReload = false;
        }

        Log.Information(
            "[Profiles] After load: {Count} unified entries discovered (CLI: {CliCount}, Desktop: {DesktopCount})",
            AvailableProfileEntries.Count,
            PlatformPaths.DiscoverProfiles().Count,
            PlatformPaths.DiscoverDesktopProfiles().Count);

        NavigationTree.Add(new NavigationNodeViewModel(NavTitleProfiles, "👤", NavDescProfiles)
        {
            Editor = _profilesVm,
            IsTopLevel = true,
        });

        // --- Backup / Restore ---
        // H-2: persistent VM — KEY motivation for this work. The previous
        // shape disposed BackupRestoreViewModel on every reload, and Dispose()
        // cancels the in-flight backup CTS.  So a workspace reload (e.g.
        // file-watcher firing on an external write) during a backup
        // would silently abort the user's backup.  Now the VM survives
        // reloads; only the displayed state (CredentialsPreference /
        // LastBackupUtc / etc.) is re-seeded on first construction from
        // the persisted gui-state cache.
        //
        // Read from the in-memory cache rather than re-loading
        // from disk. The cache is hydrated once in the ctor and kept in sync by
        // OnBackupStateChanged on every Backup VM update.
        if (_backupVm is null)
        {
            _backupVm = new BackupRestoreViewModel(DialogServiceForViewAccess, _shareService)
            {
                CredentialsPreference = _cachedState.IncludeCredentialsInBackup,
                LastBackupUtc = _cachedState.LastBackupUtc,
                InitialBackupDirectory = _cachedState.BackupDirectory,
                InitialRestoreDirectory = _cachedState.RestoreDirectory,
                // Drives BackupRequest.ExplicitProjectDirs so the open project's
                // `.claude` directory is included in the archive.  Pre-fix this
                // field was always unset → BackupEngine.AddProjectClaudeData
                // was never invoked → project settings were silently absent.
                InitialProjectRoot = ProjectRoot,
                // 4.3.7 step 15: route through SDK.HasUnsavedChanges (structural diff)
                // for parity with the rest of the dirty-tracking flow. Pre-load both
                // clients are null → returns false (nothing to be dirty).
                IsAnyWorkspaceDirty = () => ComputeHasActualChanges(),
                SaveAllWorkspaces = SaveForBackupOrRestoreAsync,
                OnRestoreCompleted = ReloadAsync,
                // route backup/restore terminal outcomes
                // through the centre status bar pill in addition to
                // the page-local label.
                OnTerminalStatus = (text, isFailure) =>
                {
                    if (isFailure)
                    {
                        SetStatusFailure(text);
                    }
                    else
                    {
                        SetStatusSuccess(text);
                    }
                },
            };
            _backupVm.PersistentStateChanged += OnBackupStateChanged;
        }

        // Reload Window re-uses the cached _backupVm (H-2
        // pattern: editor instances survive reload to preserve in-flight
        // state).  But Refresh() reseeds the page-local BackupDirectory /
        // RestoreDirectory / IncludeCredentials FROM the Initial* bridge
        // properties — which were set ONCE at construction.  If the user
        // changed any of these during the session (e.g. picked a new
        // backup folder), the change persisted to _cachedState via
        // OnBackupStateChanged, but the cached VM's Initial* still
        // pointed at the construction-time values.  Result: Refresh()
        // clobbered the user's session edits with stale Initial* values.
        // Fix: re-sync Initial* from the latest _cachedState BEFORE
        // every Refresh so the seed is always current.
        _backupVm.InitialBackupDirectory = _cachedState.BackupDirectory;
        _backupVm.InitialRestoreDirectory = _cachedState.RestoreDirectory;
        _backupVm.CredentialsPreference = _cachedState.IncludeCredentialsInBackup;
        _backupVm.LastBackupUtc = _cachedState.LastBackupUtc;
        // Re-sync so Reload-Window picks up project-root changes since ctor time.
        _backupVm.InitialProjectRoot = ProjectRoot;
        _backupVm.Refresh();
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleBackupRestore, "💾", NavDescBackupRestore)
        {
            Editor = _backupVm,
            IsTopLevel = true,
        });

        // --- Environment variables ---
        // Collect env-var names hinted at in Claude Code schema descriptions
        // (e.g. CLAUDE_CODE_PLUGIN_GIT_TIMEOUT_MS) so they appear as settable
        // suggestions even when not already present in the user's environment.
        // Merged with the SDK's well-known list (Sdk.Env.EnvVarKey.AllWellKnown)
        // so high-importance keys like MAX_THINKING_TOKENS — which the schema
        // does NOT mention in any description — still surface as suggestions
        // for users who navigate to the Environment page expecting to find
        // them there as a peer of the existing CLAUDE_CODE_*  suggestions.
        IReadOnlyList<string> schemaSuggested = SchemaTreeBuilder.CollectSuggestedEnvVars(ccNodes);
        IReadOnlyList<string> suggestedEnvVars = MergeSuggestedEnvVars(schemaSuggested, EnvVarKey.AllWellKnown);
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleEnvironment, "🌐", NavDescEnvironment)
        {
            Editor = new EnvironmentEditorViewModel(new DefaultEnvironmentProvider(), ClaudeCodeSdk, suggestedEnvVars),
            IsTopLevel = true,
        });

        // --- Memory & Footprint (Phase 5) ---
        // Top-level node — surfaces Tier 1 user-authored memory (read-only
        // viewer) and Tier 2 behavioural footprint (audit + delete) for
        // Claude Code; the page also renders an explainer panel for
        // Claude Desktop (which has no CLAUDE.md-equivalent surface).
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleMemory, "🧠", NavDescMemory)
        {
            Editor = new MemoryEditorViewModel(
                ClaudeCodeSdk,
                ProjectRoot,
                DialogServiceForViewAccess,
                ShellLauncher.Instance),
            IsTopLevel = true,
        });

        // --- Agents & Skills (Skills/Sub-agents/Slash-Commands editor) ---
        // Top-level node — single page with an in-page segmented control
        // (Sub-agents / Skills / Slash Commands).  Group #2: scope-aware
        // read-only inventory + structured front-matter card + body viewer.
        // Read-only in this group, so created inline (not cached) like the
        // Memory node; group #3 (editing + dirty state) will promote it to
        // a cached _agentsSkillsVm field per the AGENTS.md nav-page checklist.
        NavigationTree.Add(new NavigationNodeViewModel(NavTitleAgentsSkills, "🧩", NavDescAgentsSkills)
        {
            Editor = new AgentsSkillsEditorViewModel(ProjectRoot, ShellLauncher.Instance),
            IsTopLevel = true,
        });

        // Restore previously selected node
        RestoreSelectedNode();

        // Latch so subsequent ShowWelcomeOnLaunch toggles fire
        // ApplyWelcomeNodeVisibility on the live tree.  Cleared if the
        // tree is rebuilt (e.g. workspace reload); set after each rebuild.
        _navTreeBuilt = true;
    }

    private void OnNavHeaderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationNodeViewModel.IsExpanded))
        {
            return;
        }

        if (sender is NavigationNodeViewModel node)
        {
            _navHeaderExpanded[node.Title] = node.IsExpanded;
            SaveWindowState();
        }
    }

    private void RestoreSelectedNode()
    {
        // preference-aware case: if the saved node was
        // "Welcome" but the user has since opted out (or this is a
        // fresh state and the preference is off), fall through to the
        // non-Welcome default below rather than trying to restore a
        // node that's no longer in the tree.
        bool wantWelcomeAsDefault = ShowWelcomeOnLaunch
                                    && NavigationTree.Any(n => n.Title == NavTitleWelcome);

        if (_lastNodeTitle == null)
        {
            // Fresh install / cleared state: land on the Welcome node
            // when the preference allows; otherwise land on Essentials
            // (the next top-of-tree user-actionable surface).
            if (wantWelcomeAsDefault)
            {
                SelectedNode = NavigationTree.First(n => n.Title == NavTitleWelcome);
                return;
            }

            SelectedNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleEssentials)
                           ?? NavigationTree.FirstOrDefault(n => n.Title == NavTitleClaudeCode)?.Children
                                            .FirstOrDefault()
                           ?? NavigationTree.FirstOrDefault(n => !n.IsDivider);
            return;
        }

        // Search all nodes for the saved title
        foreach (NavigationNodeViewModel node in NavigationTree)
        {
            if (node.Title == _lastNodeTitle)
            {
                SelectedNode = node;
                return;
            }

            foreach (NavigationNodeViewModel child in node.Children)
            {
                if (child.Title == _lastNodeTitle)
                {
                    SelectedNode = child;
                    return;
                }
            }
        }

        // Fallback when a previously-saved node no longer exists.  Two
        // sub-cases:
        //   (a) The saved title was "Welcome" and the user has since
        //       opted out — land on Essentials, NOT Claude Code's first
        //       child, because the user explicitly chose to skip
        //       Welcome and Essentials is the natural next top-of-tree
        //       surface.
        //   (b) The saved title was something else that the schema
        //       dropped — land on Claude Code's first child (the
        //       previously-known landing spot for "returning user with
        //       stale state file").
        if (_lastNodeTitle == NavTitleWelcome)
        {
            SelectedNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleEssentials)
                           ?? NavigationTree.FirstOrDefault(n => n.Title == NavTitleClaudeCode)?.Children
                                            .FirstOrDefault()
                           ?? NavigationTree.FirstOrDefault(n => !n.IsDivider);
            return;
        }

        NavigationNodeViewModel? ccNode = NavigationTree.FirstOrDefault(n => n.Title == NavTitleClaudeCode);
        SelectedNode = ccNode?.Children.FirstOrDefault() ?? NavigationTree.FirstOrDefault(n => !n.IsDivider);
    }

    private void SetupFileWatcher(IReadOnlyList<DiscoveredFile> files)
    {
        _watcher?.Dispose();
        _watcher = new ConfigFileWatcher();
        _watcher.FileChanged += OnFileChangedExternally;
        foreach (DiscoveredFile f in files.Where(f => f.Exists))
        {
            _watcher.Watch(f.FilePath);
        }
    }

    private void OnFileChangedExternally(object? sender, string filePath)
    {
        // FileSystemWatcher fires on a thread-pool thread — marshal to UI thread
        // before touching any ObservableCollection or ViewModel properties.
        // Do NOT guard with "if (IsLoading) return;" here: that would silently drop
        // the reload signal when multiple watcher events are queued while a reload is
        // already in flight.  Instead let ReloadAsync's _reloadPending guard handle it —
        // it correctly restarts one additional reload after the current one finishes.
        Dispatcher.UIThread.Post(() =>
        {
            // in-progress-save suppression. Covers the dialog
            // window of SaveCoreAsync (between editor flush and disk
            // write). Without this, an external mod during the dialog
            // would trigger a reload that disposes the SDK clients and
            // discards the in-memory user edits, causing the subsequent
            // SaveAsync to write a no-op or stale state. See
            // _saveInProgressCount field comment.
            if (Volatile.Read(ref _saveInProgressCount) > 0)
            {
                Log.Debug("[FileWatcher] Suppressed reload for {File} (save in progress)",
                    Path.GetFileName(filePath));
                return;
            }

            //  self-write suppression. SaveCoreAsync stamps a
            // ~2 s deadline on _suppressWatcherUntilUtc; the watcher
            // re-firing on our own write within that window must NOT
            // trigger a reload, because the reload rebuilds the navigation
            // tree and disposes long-running tool VMs (Backup, Profiles)
            // mid-operation. See _suppressWatcherUntilUtc field comment.
            if (DateTime.UtcNow < _suppressWatcherUntilUtc)
            {
                Log.Debug("[FileWatcher] Suppressed reload for {File} (within post-save window)",
                    Path.GetFileName(filePath));
                return;
            }

            SetStatusActive(string.Format(Strings.StatusReloadingFileFmt, Path.GetFileName(filePath)));
            // FileSystemWatcher fire — automatic trigger, NOT user-initiated.
            // Use ReloadCoreAsync so dismissed banners stay dismissed (a file
            // watcher can fire many times per minute on a busy edit session;
            // resetting banners on each fire would nag the user constantly).
            _ = ReloadCoreAsync();
        });
    }

    /// <summary>
    /// Dispose all editor ViewModels currently held in the navigation tree and unsubscribe
    /// nav-header <c>PropertyChanged</c> handlers registered by <see cref="BuildNavigationTree"/>.
    /// Must be called before <see cref="NavigationTree"/> is cleared.
    /// </summary>
    private void DisposeNavigationEditors()
    {
        foreach (NavigationNodeViewModel node in NavigationTree)
        {
            // Unsubscribe nav-header expand/collapse handler so old header nodes do
            // not accumulate as phantom listeners across workspace reloads.
            node.PropertyChanged -= OnNavHeaderPropertyChanged;

            DisposeEditorNode(node);
            foreach (NavigationNodeViewModel child in node.Children)
            {
                // persistent tool VMs survive reload, so
                // we must NOT dispose them here.  IsPersistentToolVm checks
                // identity against _backupVm / _profilesVm / _aboutCodeVm /
                // _aboutDesktopVm; everything else (schema-driven editors,
                // Memory / Environment / EffectiveSettings which carry SDK
                // refs that change on swap) gets disposed as before.
                DisposeEditorNode(child);
            }
        }
    }

    /// <summary>
    /// Dispose the editor at <paramref name="node"/> unless it's one of the
    /// persistent tool VMs we keep alive across workspace reloads (H-2).
    /// </summary>
    private void DisposeEditorNode(NavigationNodeViewModel node)
    {
        if (node.Editor is null)
        {
            return;
        }

        if (IsPersistentToolVm(node.Editor))
        {
            return;
        }

        if (node.Editor is IDisposable d)
        {
            d.Dispose();
        }
    }

    private bool IsPersistentToolVm(object? editor)
    {
        return editor is not null
               && (ReferenceEquals(editor, _backupVm)
                   || ReferenceEquals(editor, _profilesVm)
                   || ReferenceEquals(editor, _aboutCodeVm)
                   || ReferenceEquals(editor, _aboutDesktopVm)
                   || ReferenceEquals(editor, _essentialsVm));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unregister all WeakReferenceMessenger subscriptions to avoid memory leaks
        WeakReferenceMessenger.Default.UnregisterAll(this);

        // Unsubscribe from OS theme changes
        IPlatformSettings? ps = Application.Current?.PlatformSettings;
        if (ps != null)
        {
            ps.ColorValuesChanged -= OnSystemThemeChanged;
        }

        // Persist the per-schema path set so next launch shows no "✨ NEW"
        // badges for anything we already rendered this session.
        // --showAllNew debug flag suppresses persistence so a QA
        // run with the flag does not pollute the user's real snapshot — the
        // next normal launch resumes correctly with the old baseline.
        if (!DebugFlags.ShowAllNewBadges)
        {
            foreach ((string schemaName, IEnumerable<string> paths) in _renderedPathsBySchema)
            {
                _snapshotService.SaveSnapshot(schemaName, paths);
            }
        }

        _backupStateSaveCts?.Cancel();
        _backupStateSaveCts?.Dispose();
        _backupStateSaveCts = null;
        // search debounce CTS lives on SearchViewModel now.
        Search.Dispose();
        DisposeNavigationEditors();

        // app-shutdown disposal of persistent tool VMs.
        // DisposeNavigationEditors deliberately skips these (so they survive
        // workspace reloads); on shutdown the surviving in-flight Backup
        // operation must be cancelled and the OnBackupStateChanged event
        // unsubscribed.  Profiles/About are not IDisposable but are nulled
        // for symmetry / GC clarity.
        if (_backupVm is not null)
        {
            _backupVm.PersistentStateChanged -= OnBackupStateChanged;
            _backupVm.Dispose();
            _backupVm = null;
        }

        _profilesVm = null;
        _aboutCodeVm = null;
        _aboutDesktopVm = null;
        _essentialsVm?.Dispose();
        _essentialsVm = null;
        // StatusController is IDisposable and owns a
        // CancellationTokenSource (timer handle) for any pending
        // Success/Warning auto-clear.  Dispose here so window-close
        // reclaims it deterministically rather than leaving the CTS
        // alive for the GC's finaliser to mop up.
        Status.Dispose();

        // SDK clients release their internal SemaphoreSlim. Dispose them before
        // the SchemaRegistry below so they don't observe a half-disposed registry.
        ClaudeCodeSdk?.Dispose();
        ClaudeCodeSdk = null;
        ClaudeDesktopSdk?.Dispose();
        ClaudeDesktopSdk = null;
        _watcher?.Dispose();
        _schemaRegistry.Dispose();
    }

    [GeneratedRegex("""[A-Za-z]:[\\/](?:[^\\/'"\n]+[\\/])*([^\\/'"\n]+)""")]
    private static partial Regex MyRegex();

    [GeneratedRegex("""/(?:home|Users|root|tmp|var)/[^\s'"\n]+/([^\s'"\n]+)""")]
    private static partial Regex MyRegex1();
}

// ── Companion records ────────────────────────────────────────────────────────

// PropertyDiff moved to its own file (ViewModels/PropertyDiff.cs) when the
// diff machinery moved into ClaudeForge.Services.WorkspaceDiagnostics.

/// <summary>Persisted window geometry returned by <see cref="MainWindowViewModel.GetSavedGeometry"/>.</summary>
public sealed record SavedWindowGeometry(double W, double H, double? X, double? Y);