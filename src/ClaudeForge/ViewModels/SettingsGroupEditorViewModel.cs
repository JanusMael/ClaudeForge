using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Hosts property editors for a named settings group.
/// Manages the active editing scope and pushes values to the workspace on demand.
/// Saving/reloading is coordinated by <see cref="MainWindowViewModel"/>.
/// </summary>
public partial class SettingsGroupEditorViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    private readonly SettingsWorkspace _workspace;

    // when supplied, mutations route through the
    // SDK client so the SDK's Changed forwarder + path-info raise unify
    // the dirty-tracking feed. Reads still go through _workspace because
    // the GUI's GetLayeredValue / placeholder-JSON paths are quasi-internal
    // and not yet promoted to a public SDK API. Null in test fixtures
    // that construct the VM via the workspace-only convenience ctor.
    private readonly ClaudeConfigClientCore? _sdkClient;
    private readonly Func<Task<string?>>? _browseDialog;
    private readonly DefaultEditorFactory _factory;
    private readonly SharedScopeContext _sharedScope;

    // Per-group tab-strip exception hook (insert/hide tabs). Null ⇒ built-ins only.
    private readonly IGroupTabCustomizer? _tabCustomizer;

    // Lazy-rebuild support: when this VM is not the active page, shared-scope
    // changes are deferred until Activate() is called.
    private bool _isActive;

    private bool _rebuildPending;

    // Prevents feedback loops between this VM and the shared context.
    private bool _updatingFromShared;

    // Guards against rebuilding in response to workspace writes that this VM itself initiates
    // (live-write path in OnEditorPropertyChanged), which would destroy the user's in-progress edits.
    //
    // dual-guard contract.
    // This flag pairs with `_suppressForwarder` in
    // `ClaudeForge.Sdk.ClaudeConfigClientCore` to prevent the SDK Changed →
    // editor reload → SDK SetValue → SDK Changed infinite loop:
    //   * `_selfWriting` (here) blocks the editor from rebuilding when the
    //     workspace.Changed it just caused fires back.
    //   * `_suppressForwarder` (SDK side) blocks the SDK from re-emitting
    //     Changed for SDK-initiated mutations that also trigger the
    //     workspace forwarder.
    // Both flags are set/cleared inside try/finally on the same thread (the
    // GUI dispatcher); they are NOT thread-safe and rely on the GUI being
    // single-threaded for write paths.
    //
    // If you change either flag's lifecycle, also review the other side and
    // re-run the canonical regression tests in
    // `tests/ClaudeForge.Tests/ViewModels/SettingsGroupEditorViewModelTests`.
    private bool _selfWriting;

    /// <summary>
    /// Paths the user has actually edited via the bound editor surface
    /// during this group's current load cycle.  Populated by
    /// <see cref="OnEditorPropertyChanged"/> (which only fires for live
    /// post-load mutations because subscription happens AFTER
    /// <see cref="LibVm.PropertyEditorViewModel.LoadFromValue"/> in
    /// <see cref="RebuildEditors"/>), cleared on every rebuild.
    /// <para>
    /// <b>Why this exists:</b> compound editors set
    /// <c>IsModified = scopeValue != null</c> in <c>LoadFromLayered</c>, so
    /// any editor displaying existing data is marked modified at load.
    /// Without this set, <see cref="ApplyToWorkspace"/> blindly flushed
    /// every IsModified-true editor and would overwrite out-of-band SDK
    /// writes (e.g. EssentialsViewModel writing to <c>env.MAX_OUTPUT_TOKENS</c>
    /// while the Environment group editor's in-memory env snapshot didn't
    /// know about that key).  Bug reported 2026-05-13.
    /// </para>
    /// </summary>
    private readonly HashSet<string> _userEditedPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// Primary constructor.  Accepts a <see cref="SharedScopeContext"/> so that
    /// changing the scope on any page in the same product section immediately
    /// propagates to all other pages.
    /// </summary>
    public SettingsGroupEditorViewModel(
        string groupName,
        IReadOnlyList<SchemaNode> schemaNodes,
        SettingsWorkspace workspace,
        SharedScopeContext sharedScope,
        Func<Task<string?>>? browseDialog = null,
        DefaultEditorFactory? factory = null,
        string groupDescription = "",
        ClaudeConfigClientCore? sdkClient = null,
        IGroupTabCustomizer? tabCustomizer = null)
    {
        GroupName = groupName;
        GroupDescription = groupDescription;
        SchemaNodes = schemaNodes;
        _workspace = workspace;
        _sdkClient = sdkClient;
        _browseDialog = browseDialog;
        _factory = factory ?? ClaudeEditorFactoryConfig.CreateDefault();
        _sharedScope = sharedScope;
        _tabCustomizer = tabCustomizer;
        _editingScope = sharedScope.EditingScope; // initialise from shared state
        sharedScope.PropertyChanged += OnSharedScopePropertyChanged;
        Editors = [];
        Tabs = [];
        RebuildEditors(); // also calls RebuildTabs()

        // Auto-refresh when the workspace changes externally (file watcher, another editor, etc.).
        // Own-writes are guarded by _selfWriting to prevent destroying the user's in-progress edits.
        _workspace.Changed += OnWorkspaceChanged;
    }

    /// <summary>
    /// Convenience overload for unit tests and other simple callers that do not
    /// need cross-page scope synchronisation.  Creates a private
    /// <see cref="SharedScopeContext"/> scoped to this VM only.
    /// </summary>
    public SettingsGroupEditorViewModel(
        string groupName,
        IReadOnlyList<SchemaNode> schemaNodes,
        SettingsWorkspace workspace,
        ConfigScope initialScope = ConfigScope.User,
        Func<Task<string?>>? browseDialog = null,
        DefaultEditorFactory? factory = null)
        : this(groupName, schemaNodes, workspace,
            new SharedScopeContext(initialScope),
            browseDialog, factory)
    {
    }

    public string GroupName { get; }

    /// <summary>
    /// The top-level tab strip, data-driven so groups can contribute extra tabs
    /// at any index and hide the built-ins via an <see cref="IGroupTabCustomizer"/>.
    /// Seeded with Properties / Effective / JSON; rebuilt whenever the editors
    /// are (see <see cref="RebuildTabs"/>).
    /// </summary>
    public ObservableCollection<GroupTab> Tabs { get; }

    /// <summary>
    /// The selected tab. Two-way bound to the view's <c>TabControl.SelectedItem</c>
    /// (replacing a hardcoded <c>SelectedIndex=0</c>), so the selection is
    /// remembered across rebuilds (see <see cref="RebuildTabs"/>) and navigation
    /// away and back. Deep-links set it explicitly via <see cref="SelectTab"/>.
    /// </summary>
    [ObservableProperty] private GroupTab? _selectedTab;

    /// <summary>
    /// Select the tab with <paramref name="tabId"/> when present; no-op otherwise.
    /// Used by deep-link navigation to land on a specific tab (e.g. Overview, so
    /// the permissions "Advanced" accordion it expands is on screen).
    /// </summary>
    public void SelectTab(string tabId)
    {
        GroupTab? tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    /// <summary>
    /// One-line page description shown in the editor header next to
    /// <see cref="GroupName"/>. Sourced from the <c>GroupDescriptions</c>
    /// table in <see cref="Services.NavigationTreeBuilder"/>; empty string
    /// for groups not in that table (the bound TextBlock is hidden when
    /// the description is empty so the heading collapses cleanly).
    /// </summary>
    public string GroupDescription { get; }

    public IReadOnlyList<SchemaNode> SchemaNodes { get; }

    /// <summary>
    /// Top-level editors rendered for this group. Public exposure is read-only:
    /// the property is assigned wholesale by <c>RebuildEditors</c> after every
    /// scope or workspace change. Direct mutation (Add/Remove/Clear) would bypass
    /// the OnPropertyChanged notification fired by RebuildEditors and silently
    /// desynchronise the bound view from the workspace.
    /// </summary>
    public IReadOnlyList<LibVm.PropertyEditorViewModel> Editors { get; private set; }

    /// <summary>
    /// Show the filter bar when the page has more than a handful of properties, OR
    /// when a filter is already active (e.g. set by a deep-link navigation from search).
    /// The second condition ensures users can always see and clear an active filter,
    /// even on small sections where the bar would otherwise be hidden.
    /// </summary>
    public bool ShowFilterBar => Editors.Count > 4 || !string.IsNullOrEmpty(FilterText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredEditors))]
    [NotifyPropertyChangedFor(nameof(ShowFilterBar))]
    private string _filterText = string.Empty;

    /// <summary>
    /// Clears contextual hint banners on compound editors when the filter is removed,
    /// matching the user expectation that clearing the filter returns the page to its
    /// default state.  Currently dismisses <see cref="PermissionsEditorViewModel.ShowDangerCliHint"/>.
    /// </summary>
    partial void OnFilterTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (PermissionsEditorViewModel editor in Editors.OfType<PermissionsEditorViewModel>())
        {
            editor.ShowDangerCliHint = false;
        }
    }

    /// <summary>Clears the property filter, restoring the full unfiltered list.</summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    public IEnumerable<LibVm.PropertyEditorViewModel> FilteredEditors
    {
        get
        {
            // Deprecated properties are hidden unless the user already has a value set
            // at some scope (in which case we still want to surface it so they can
            // unset it). This matches JSON-Schema Draft-2019-09's `deprecated` keyword
            // and our "DEPRECATED" description-prefix heuristic.
            IEnumerable<LibVm.PropertyEditorViewModel> source =
                Editors.Where(e => !e.IsDeprecated || e.IsSetAnywhere);

            if (string.IsNullOrWhiteSpace(FilterText))
            {
                return source;
            }

            return FlattenAndFilter(source, FilterText);
        }
    }

    /// <summary>
    /// Yields only editors whose name, path, or description matches <paramref name="filter"/>.
    /// For <see cref="ObjectPropertyEditorViewModel"/> entries that do NOT directly match,
    /// recursively descends into their children and yields any descendants that do match —
    /// so typing a nested property name (e.g. "allowUnsandboxedCommands") shows only that
    /// child property rather than the entire parent object with all siblings.
    /// <para>
    /// Sub-path fallback (2026-05-07): when the filter is a dotted JSON path that
    /// extends an editor's <see cref="LibVm.PropertyEditorViewModel.Path"/> (e.g.
    /// filter <c>"permissions.additionalDirectories"</c> with editor path
    /// <c>"permissions"</c>), the editor matches as a whole.  This lets a search
    /// hit on a sub-property of a SPECIALIZED editor (Permissions / Hooks /
    /// MCP / etc.) — which doesn't decompose into sibling sub-editors — still
    /// surface the correct page on click.  Previously the filter was strictly
    /// substring-on-Path, so a longer dotted filter never matched any specialized
    /// editor and the page rendered empty.
    /// </para>
    /// </summary>
    private static IEnumerable<LibVm.PropertyEditorViewModel> FlattenAndFilter(
        IEnumerable<LibVm.PropertyEditorViewModel> editors, string filter)
    {
        foreach (LibVm.PropertyEditorViewModel editor in editors)
        {
            if (editor.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                editor.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (editor.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                // Direct match — show the editor as-is (may be a whole Object editor;
                // that's fine when the object's own name/description is what matched).
                yield return editor;
            }
            else if (editor is ObjectPropertyEditorViewModel obj)
            {
                // Object didn't match directly — descend into children and yield
                // only matching descendants so the user sees just the target property,
                // not every sibling inside the same object.  If the descent yields
                // nothing AND the filter is a sub-path of this object, fall back
                // to yielding the whole object (covers the specialized-editor-style
                // case where children aren't fully indexed via Path/DisplayName).
                bool anyMatched = false;
                foreach (LibVm.PropertyEditorViewModel match in FlattenAndFilter(obj.Children, filter))
                {
                    yield return match;
                    anyMatched = true;
                }

                if (!anyMatched && IsSubPathOf(filter, editor.Path))
                {
                    yield return editor;
                }
            }
            else if (IsSubPathOf(filter, editor.Path))
            {
                // Specialized editor (Permissions / Hooks / MCP / etc.) — not an
                // ObjectPropertyEditorViewModel, so no recursion possible.  When the
                // filter targets a sub-property of this editor, the editor itself is
                // the only place the property can be edited, so yield it whole.
                yield return editor;
            }
        }
    }

    /// <summary>
    /// True when <paramref name="filter"/> is a strict descendant of
    /// <paramref name="editorPath"/> in dotted-JSON-path notation
    /// (e.g. filter <c>"permissions.allow"</c>, editorPath <c>"permissions"</c>).
    /// Equality alone is not enough — that case is already handled by the
    /// <c>Path.Contains</c> check upstream.
    /// </summary>
    private static bool IsSubPathOf(string filter, string editorPath)
    {
        if (string.IsNullOrEmpty(editorPath) || string.IsNullOrEmpty(filter))
        {
            return false;
        }

        if (filter.Length <= editorPath.Length)
        {
            return false;
        }

        return filter.StartsWith(editorPath + ".", StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty] private ConfigScope _editingScope;

    /// <summary>
    /// The available scopes for this group; delegates to the shared context so
    /// every page in the same product section reports the same choices.
    /// Updated whenever <see cref="SharedScopeContext.AvailableScopes"/> changes
    /// (e.g. when a project folder is opened or closed).
    /// </summary>
    public IReadOnlyList<ConfigScope> AvailableScopes => _sharedScope.AvailableScopes;

    /// <summary>
    /// Set when a workspace write fails (e.g. scope document not loaded).
    /// Displayed as a non-blocking inline error below the scope selector.
    /// Cleared on the next successful write or scope rebuild.
    /// </summary>
    [ObservableProperty] private string? _editorError;

    /// <summary>
    /// Read-only effective-value rows for all properties in this group.
    /// Mirrors the global Effective Settings view but scoped to this group.
    /// Updated whenever <see cref="RebuildEditors"/> or <see cref="RebuildJsonPreview"/> runs.
    /// </summary>
    public IReadOnlyList<EffectivePropertyRow> EffectiveRows { get; private set; } = [];

    /// <summary>Pretty-printed JSON snapshot of the current editor values (set at editing scope).</summary>
    public string JsonPreview { get; private set; } = "{}";

    /// <summary>
    /// When true the JSON tab shows all schema properties with placeholder / default values,
    /// rather than only the keys that have been explicitly set at the current editing scope.
    /// Useful for discovering every configurable option without having to set one first.
    /// <para>
    /// Defaults to <c>true</c> — most users opening the JSON tab want to see the
    /// full shape of the document they could write, not the (often empty) subset
    /// already set at the current scope. The "JSON (active)" mode is one click
    /// away via the toggle in the tab toolbar.
    /// </para>
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(JsonTabHeader))]
    private bool _showJsonPlaceholders = true;

    [SuppressMessage("ReSharper", "UnusedParameterInPartialMethod")]
    partial void OnShowJsonPlaceholdersChanged(bool value)
    {
        RebuildJsonPreview();
        // Keep the data-driven JSON tab's header in sync ("JSON (all)" / "(active)").
        GroupTab? jsonTab = Tabs.FirstOrDefault(t => t.Id == GroupTab.JsonId);
        if (jsonTab is not null)
        {
            jsonTab.Header = JsonTabHeader;
        }
    }

    public string JsonTabHeader => ShowJsonPlaceholders ? "JSON (all)" : "JSON (active)";

    [RelayCommand]
    private void CopyJson()
    {
        // Clipboard access requires the UI thread — handled in code-behind via CopyJsonRequested.
        CopyJsonRequested?.Invoke(this, JsonPreview);
    }

    /// <summary>Raised when the user clicks "Copy JSON"; the View wires up clipboard access.</summary>
    public event EventHandler<string>? CopyJsonRequested;

    partial void OnEditingScopeChanged(ConfigScope value)
    {
        if (_updatingFromShared)
        {
            return; // shared handler already schedules the rebuild
        }

        // Defensive guard: Avalonia's ComboBox TwoWay binding can push a stale
        // or default scope during a DataContext switch (e.g. when navigating from
        // a Claude Code page with "Local" selected to a Claude Desktop page whose
        // AvailableScopes is [User] only).  The ItemsSource and SelectedItem
        // bindings are not updated atomically, so the old SelectedItem value can
        // momentarily flow back to the new VM before the binding is refreshed.
        // Reject any scope that isn't in the available set and snap back to the
        // shared context's current valid value instead.
        if (!_sharedScope.AvailableScopes.Contains(value))
        {
            _updatingFromShared = true;
            try
            {
                EditingScope = _sharedScope.EditingScope;
            }
            finally
            {
                _updatingFromShared = false;
            }

            return;
        }

        // User interaction on this page: push to shared context so siblings update.
        if (_sharedScope.EditingScope != value)
        {
            _sharedScope.EditingScope = value;
        }

        // This page IS active (user clicked its dropdown), rebuild immediately.
        RebuildEditors();
    }

    /// <summary>
    /// Invoked when the shared scope changes (another page's dropdown was changed).
    /// Updates <see cref="EditingScope"/> without triggering <see cref="OnEditingScopeChanged(ConfigScope)"/>
    /// and defers <see cref="RebuildEditors"/> until this page becomes active.
    /// </summary>
    private void OnSharedScopePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedScopeContext.AvailableScopes))
        {
            // Propagate available-scope list change to the bound ComboBox.
            OnPropertyChanged(nameof(AvailableScopes));

            // If the currently selected scope was removed (e.g. project closed),
            // fall back to the first available scope (typically User).
            if (!_sharedScope.AvailableScopes.Contains(EditingScope))
            {
                _sharedScope.EditingScope = _sharedScope.AvailableScopes[0];
                // EditingScope will be updated via the EditingScope branch below,
                // triggered by the resulting PropertyChanged on the shared context.
            }

            return;
        }

        if (e.PropertyName != nameof(SharedScopeContext.EditingScope))
        {
            return;
        }

        if (_sharedScope.EditingScope == EditingScope)
        {
            return; // already in sync
        }

        _updatingFromShared = true;
        try
        {
            EditingScope = _sharedScope.EditingScope;
        }
        finally
        {
            _updatingFromShared = false;
        }

        if (_isActive)
        {
            RebuildEditors();
        }
        else
        {
            _rebuildPending = true;
        }
    }

    /// <summary>
    /// Called whenever the underlying workspace document is mutated by an external source
    /// (file watcher, another editor, or another scope's live-write).
    /// <para>
    /// Skipped when the mutation originated from this VM's own <see cref="OnEditorPropertyChanged"/>
    /// live-write path (<see cref="_selfWriting"/> guard) to prevent rebuilding and discarding
    /// the user's in-progress edits.
    /// </para>
    /// <para>
    /// When the page is <b>inactive</b> the rebuild is deferred: <see cref="_rebuildPending"/>
    /// is set so <see cref="Activate"/> picks it up on the next navigation.
    /// When the page is <b>active</b> the rebuild is dispatched at Background priority so
    /// Avalonia's current layout pass completes first and the user's active focus/cursor
    /// is not disturbed mid-keystroke.
    /// </para>
    /// </summary>
    private void OnWorkspaceChanged(object? sender, EventArgs _)
    {
        if (_selfWriting)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Rebuild();
        }
        else
        {
            Dispatcher.UIThread.Post(Rebuild);
        }

        return;

        void Rebuild()
        {
            if (_isActive)
            {
                RebuildEditors();
            }
            else
            {
                _rebuildPending = true;
            }
        }
    }

    /// <summary>
    /// Called by <see cref="MainWindowViewModel"/> when this VM's page becomes the
    /// active editor.  Flushes any deferred scope rebuild so the content is
    /// immediately up-to-date without blocking during navigation.
    /// </summary>
    public void Activate()
    {
        _isActive = true;
        if (_rebuildPending)
        {
            _rebuildPending = false;
            RebuildEditors();
        }
    }

    /// <summary>
    /// Called by <see cref="MainWindowViewModel"/> when this VM's page is navigated
    /// away from.  Subsequent shared-scope changes will be queued rather than
    /// executed immediately. Any active filter is cleared so the next visit starts
    /// with the full unfiltered list.
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        FilterText = string.Empty;
    }

    /// <summary>
    /// Write editor values back to the workspace (in memory — does not save to disk).
    /// Call before saving to ensure the workspace reflects UI state.
    /// Only editors whose <see cref="PropertyEditorViewModel.IsModified"/> flag is set
    /// are written; unmodified (inherited / default) values are intentionally skipped so
    /// that clicking Save never creates phantom diff entries for fields the user did not touch.
    /// </summary>
    public void ApplyToWorkspace()
    {
        // deadlock fix: set `_selfWriting` for the duration of the
        // bulk-save loop, mirroring what OnEditorPropertyChanged does for
        // single-editor live writes (line ~518).
        //
        // Original failing call stack:
        //   ApplyToWorkspace
        //     -> WriteEditorValue
        //       -> SDK.SetValue                       (acquires _stateLock)
        //         -> workspace.SetValue
        //           -> workspace.Changed (sync, lock still held)
        //             -> OnWorkspaceChanged
        //               -> RebuildEditors
        //                 -> PermissionsEditor.LoadFromLayered
        //                   -> PermissionsAccessor.GetDefaultModeAt
        //                     -> SDK.GetScopeValue   (waits on _stateLock — DEADLOCK)
        //
        // Defense in depth — TWO safeguards now protect this path:
        //
        //   1. (this fix) `_selfWriting` short-circuits OnWorkspaceChanged
        //      before the rebuild can fire SDK accessor reads. This is the
        //      cheap win — no rebuild work, no events fired.
        //
        //   2. (architectural fix) `ClaudeConfigClientCore`'s `_stateLock` is
        //      now thread-reentrant via EnterStateLock / ExitStateLock helpers.
        //      Even if a future workspace.Changed subscriber forgets to set a
        //      `_selfWriting`-style guard, same-thread re-entry into SDK
        //      accessors no longer deadlocks — the helper detects the
        //      already-holding-thread and bumps a depth counter rather than
        //      blocking.
        //
        // The single-editor write path was always guarded; the bulk path
        // wasn't — only manifested once the rebuild started reading via
        // SDK accessors (Permissions/Hooks/etc). See dual-guard
        // comment on `_selfWriting` for the contract.
        _selfWriting = true;
        try
        {
            foreach (LibVm.PropertyEditorViewModel editor in Editors)
            {
                // gate flush on actual user mutation, not on the
                // editor's IsModified flag.  The flag is set true at load time
                // for every compound editor whose scope has data (see
                // src/ClaudeForge/ViewModels/Editors/AGENTS.md), so an
                // IsModified-only gate would flush every loaded editor's
                // in-memory snapshot back to the SDK on every save —
                // clobbering any out-of-band SDK writes (e.g. the Essentials
                // page writing `env.CLAUDE_CODE_MAX_OUTPUT_TOKENS` while the
                // Environment group editor's in-memory env snapshot doesn't
                // include that key).  _userEditedPaths is populated only by
                // OnEditorPropertyChanged (which fires only on post-load user
                // mutations), so this gate restricts ApplyToWorkspace to its
                // actual safety-net role: re-flushing user edits whose
                // live-write may have failed.
                if (!_userEditedPaths.Contains(editor.Path))
                {
                    continue;
                }

                try
                {
                    // use the library API (ToValue + Path) so the
                    // App-bridge subclasses and the migrated library leaves are handled
                    // uniformly. App-bridge ToValue() routes through the legacy
                    // ToJsonValue override; migrated leaves return currency directly.
                    JsonNode? value = JsonCurrency.ToJsonNode(editor.ToValue());
                    Log.Information("[Editor.Flush] writing path '{Path}' scope={Scope} value={Value}",
                        editor.Path,
                        EditingScope,
                        FormatValueForAuditLog(value, editor.Path));
                    WriteEditorValue(editor.Path, value, EditingScope);
                }
                catch (Exception ex)
                {
                    // Same broad-catch rationale as OnEditorPropertyChanged —
                    // any write failure should be a copyable error message, not a crash.
                    Log.Error(ex, "[Editor] ApplyToWorkspace failed for {Path}", editor.Path);
                    EditorError = ex.Message;
                }
            }
        }
        finally
        {
            _selfWriting = false;
        }
    }

    /// <summary>
    /// Write a single editor's value, routing through the SDK client when one
    /// is wired so the SDK's path-info Changed raise
    /// + workspace-Changed forwarder dedup keeps the dirty-tracking feed
    /// unified. Falls back to <c>_workspace.SetValue</c> /
    /// <c>_workspace.RemoveValue</c> when the SDK client wasn't supplied
    /// (test fixtures using the workspace-only convenience constructor).
    /// </summary>
    private void WriteEditorValue(string jsonPath, JsonNode? value, ConfigScope scope)
    {
        if (_sdkClient is not null)
        {
            if (value != null)
            {
                _sdkClient.SetValue(jsonPath, value, scope);
            }
            else
            {
                _sdkClient.RemoveValue(jsonPath, scope);
            }
        }
        else
        {
            if (value != null)
            {
                _workspace.SetValue(jsonPath, value, scope);
            }
            else
            {
                _workspace.RemoveValue(jsonPath, scope);
            }
        }
    }

    /// <summary>Reload editors from the workspace (after an external reload).</summary>
    public void RefreshFromWorkspace()
    {
        RebuildEditors();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Render an editor value for the <c>[Editor.UserEdit]</c> / <c>[Editor.Flush]</c>
    /// audit log without leaking secret-bearing content.  Three cases:
    /// <list type="bullet">
    ///   <item><c>path</c> is sensitive per <see cref="SensitiveKeys.IsSensitive"/>
    ///     (env, headers, credentials, auth, authorization, etc.) — return the
    ///     redaction marker.  Catches the common case of the Environment
    ///     group editor whose <c>editor.Path</c> is <c>"env"</c> and whose
    ///     value is the WHOLE env map (potentially containing
    ///     <c>ANTHROPIC_API_KEY</c>).</item>
    ///   <item>Value is a <see cref="JsonObject"/> or <see cref="JsonArray"/> —
    ///     return a structural summary (shape + serialized length) but NOT
    ///     the JSON contents.  Compound editors (mcpServers, hooks, etc.)
    ///     can nest secrets in keys we cannot classify from the top-level
    ///     <c>path</c> alone (e.g. <c>mcpServers.{server}.headers.Authorization</c>) —
    ///     conservative: never inline a compound value into the rolling log.
    ///     The Save-time <c>WorkspaceDiagnostics.LogDiffs</c> path uses
    ///     <see cref="JsonDiff"/> which DOES recurse and apply per-path
    ///     redaction at every nested leaf, so the actual save-time log
    ///     still carries enough detail for "what changed?" forensics.</item>
    ///   <item>Otherwise (scalar leaf) — return <c>value.ToJsonString()</c>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// User report 2026-05-13 + code-review audit: the previous unconditional
    /// <c>value?.ToJsonString()</c> in both audit-log sites would emit the
    /// full <c>env</c> JSON object (including API keys) into the rolling log
    /// file at <c>~/.claude/cache/logs/</c>.  The log file travels with bug
    /// reports, so any leaked secret rides along.  Pinned by
    /// <c>FormatValueForAuditLog_RedactsEnvPath</c> in the test suite.
    /// </remarks>
    internal static string FormatValueForAuditLog(JsonNode? value, string path)
    {
        if (value is null)
        {
            return "(null)";
        }

        if (SensitiveKeys.IsSensitive(path))
        {
            return SensitiveKeys.RedactedMarker;
        }

        if (value is JsonObject or JsonArray)
        {
            // Structural summary only — no contents, no nested keys.  The
            // length tells reviewers whether the user added one row or
            // a hundred without revealing what those rows contain.
            string shape = value is JsonObject ? "JsonObject" : "JsonArray";
            return $"({shape}, {value.ToJsonString().Length} chars)";
        }

        return value.ToJsonString();
    }

    /// <summary>Serializes a <see cref="JsonObject"/> to an indented JSON string without going
    /// through <see cref="JsonSerializer"/>, which is not trim-safe for <see cref="JsonNode"/> types.</summary>
    private static string ToIndentedJson(JsonObject obj)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
        {
            obj.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void RebuildEditors()
    {
        // reuse existing editor instances by JsonPath instead of
        // always constructing fresh ones. Prior code:
        //
        //   foreach (var node in _schemaNodes)
        //   {
        //       var editor = _factory.Create(node, ...);   // NEW INSTANCE
        //       editor.LoadFromLayered(...);
        //       ...
        //   }
        //
        // Every RebuildEditors call (workspace.Changed → OnWorkspaceChanged
        // when not _selfWriting; EditingScope change; reload) created brand-
        // new editor instances. Compound editors (HooksEditorViewModel,
        // McpServersEditorViewModel) lose internal UI state — selected
        // event group, expanded panes, scroll position, in-progress new-row
        // text — across every reload, even though those editors had per-
        // instance "preserve selection across LoadFromLayered" logic
        // (commits bddd449, 79bb99d). The preservation only ran on the
        // SAME instance; once the instance was discarded, the captured
        // priorSelectedEventName / priorSelectedServerName on the new
        // instance was always null and the fallback "first non-empty"
        // pick won.
        //
        // Fix: when we're about to construct an editor for a JsonPath we
        // already have an editor for, reuse the existing one — same type,
        // just pump the latest layered state through LoadFromLayered. The
        // schema nodes list is itself rebuilt only when the schema fetch
        // returns a structurally different shape, which is rare; for the
        // common case (every save, every external reload), the existing
        // editors stay alive and their internal state is preserved.
        //
        // Key correctness: we still unsubscribe + resubscribe
        // OnEditorPropertyChanged on every editor (reused or new) so the
        // live-write loop doesn't double-fire. We unsubscribe FIRST, then
        // either reuse or create, then call LoadFromLayered, then
        // resubscribe — same shape as before, just with the construction
        // step replaced by the dictionary lookup.
        foreach (LibVm.PropertyEditorViewModel old in Editors)
        {
            old.PropertyChanged -= OnEditorPropertyChanged;
        }

        // reset the user-touched gate on every rebuild.  Any
        // pre-rebuild user edits already live-wrote to the SDK via
        // OnEditorPropertyChanged, so we don't need ApplyToWorkspace to
        // re-flush them.  See _userEditedPaths field comment.
        _userEditedPaths.Clear();

        Dictionary<string, LibVm.PropertyEditorViewModel> existingByPath =
            Editors.ToDictionary(e => e.Path, e => e, StringComparer.Ordinal);

        List<LibVm.PropertyEditorViewModel> editors = new(SchemaNodes.Count);
        foreach (SchemaNode node in SchemaNodes)
        {
            // Reuse the existing editor for this JsonPath when one exists —
            // preserves any internal state (selected list item, expansion,
            // scroll, in-progress new-row text). LoadFromLayered below
            // refreshes the data while keeping the instance.
            //
            // We rely on the schema/factory contract that the editor TYPE
            // for a given JsonPath is stable: the factory chooses by
            // SchemaValueType + node hints, neither of which changes
            // between rebuilds for the same JsonPath. If a future schema
            // upgrade ever changes the type for an existing path, the
            // reused editor would be the wrong shape; the integration
            // tests that round-trip per-editor-type would catch that.
            if (!existingByPath.TryGetValue(node.JsonPath, out LibVm.PropertyEditorViewModel? editor))
            {
                editor = _factory.Create(node, EditingScope, _browseDialog, _workspace);
            }

            LayeredValue layered = _workspace.GetLayeredValue(node.JsonPath);
            // use the library API (LoadFromValue + ClaudeValueAdapter
            // + ClaudeScope.For) so the App-bridge subclasses and the migrated library
            // leaves are handled uniformly. App-bridge LoadFromValue routes through the
            // legacy LoadFromLayered override.
            editor.LoadFromValue(new ClaudeValueAdapter(layered), ClaudeScope.For(EditingScope));
            editor.PropertyChanged += OnEditorPropertyChanged;
            editors.Add(editor);
        }

        Editors = editors;
        OnPropertyChanged(nameof(Editors));
        OnPropertyChanged(nameof(FilteredEditors));
        OnPropertyChanged(nameof(ShowFilterBar));
        RebuildEffectiveRows();
        RebuildJsonPreview();
        RebuildTabs();
    }

    /// <summary>
    /// Rebuilds <see cref="Tabs"/>: seeds the built-in Properties / Effective /
    /// JSON tabs (content = this group VM), then applies the per-group
    /// <see cref="IGroupTabCustomizer"/> so a group can insert/hide tabs. Called
    /// at the end of <see cref="RebuildEditors"/> so contributed tabs always
    /// reference the current editor instances.
    /// </summary>
    private void RebuildTabs()
    {
        // Remember which tab was selected so it survives this rebuild (Tabs is
        // cleared + repopulated with fresh GroupTab instances on every save /
        // reload / scope change). Matched back by Id below.
        string? priorId = SelectedTab?.Id;

        List<GroupTab> seed =
        [
            new() { Id = GroupTab.PropertiesId, Header = Strings.HeaderTabProperties, Content = this },
            new() { Id = GroupTab.EffectiveId, Header = Strings.HeaderTabEffective, Content = this },
            new() { Id = GroupTab.JsonId, Header = JsonTabHeader, Content = this },
        ];

        _tabCustomizer?.Customize(GroupName, seed, Editors.ToList());

        Tabs.Clear();
        foreach (GroupTab tab in seed)
        {
            Tabs.Add(tab);
        }

        // Initial selection (when this is NOT a deep-link, which overrides via
        // SelectTab afterward): the remembered tab → the customizer's default tab
        // → the first tab.
        SelectedTab = Tabs.FirstOrDefault(t => t.Id == priorId)
                      ?? Tabs.FirstOrDefault(t => t.IsDefaultTab)
                      ?? Tabs.FirstOrDefault();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibVm.PropertyEditorViewModel.IsModified)
            or nameof(LibVm.PropertyEditorViewModel.InheritedDisplay))
        {
            if (e.PropertyName is nameof(LibVm.PropertyEditorViewModel.IsModified))
            {
                LibVm.PropertyEditorViewModel editor = (LibVm.PropertyEditorViewModel)sender!;
                // Subscription happens AFTER LoadFromValue in RebuildEditors,
                // so this handler fires ONLY for post-load user mutations —
                // exactly the signal we need to gate ApplyToWorkspace's flush.
                _userEditedPaths.Add(editor.Path);
                try
                {
                    // Set _selfWriting before the workspace mutation so that the resulting
                    // workspace.Changed event (raised synchronously inside Set/RemoveValue)
                    // is ignored by OnWorkspaceChanged and does not trigger a rebuild that
                    // would destroy the user's in-progress edits.
                    _selfWriting = true;
                    try
                    {
                        if (editor.IsModified)
                        {
                            // Value was just set — write it to the workspace.
                            // Routes through the SDK client when wired (4.3.7 step 13).
                            // library API (ToValue → currency → JsonNode).
                            JsonNode? value = JsonCurrency.ToJsonNode(editor.ToValue());
                            // Permanent audit log of every user-driven mutation that
                            // flows through a schema-driven editor.  Pairs with the
                            // [Save.…] dialog-choice log so post-mortems can correlate
                            // user edits with the resulting save decision.  The value
                            // is routed through FormatValueForAuditLog so secret-bearing
                            // paths (env, headers, credentials, auth) are redacted, and
                            // compound values (whose nested keys might contain secrets
                            // we can't classify from the top-level path alone) are
                            // summarised to a size hint instead of inlining their JSON.
                            Log.Information("[Editor.UserEdit] path={Path} scope={Scope} value={Value}",
                                editor.Path,
                                EditingScope,
                                FormatValueForAuditLog(value, editor.Path));
                            WriteEditorValue(editor.Path, value, EditingScope);
                            EditorError = null; // clear any previous error
                        }
                        else
                        {
                            // IsModified just became false (user reset the field).
                            // IMPORTANT: ResetToInherited() sets IsModified=false *before* calling
                            // OnResetToInherited() which clears Value.  At this point ToValue()
                            // still returns the stale pre-reset value, so we must NOT call it.
                            // Always remove the key so the document reverts to inheriting.
                            Log.Information("[Editor.UserEdit] path={Path} scope={Scope} value=(reset to inherited)",
                                editor.Path, EditingScope);
                            WriteEditorValue(editor.Path, value: null, EditingScope);
                            EditorError = null;
                        }
                    }
                    finally
                    {
                        _selfWriting = false;
                    }
                }
                catch (Exception ex)
                {
                    // Surface the error as an inline message rather than letting it
                    // propagate into Avalonia's binding system (which shows non-copyable
                    // validation error overlays).  We catch Exception broadly because
                    // editor.ToValue() implementations, Changed-event subscribers,
                    // and explicit scope guards can all throw different types — any
                    // failure here should be a copyable error message, not a crash.
                    Log.Error(ex, "[Editor] Property change write failed for {Path}", editor.Path);
                    EditorError = ex.Message;

                    // Roll back: mark the editor clean so it doesn't appear to be saved.
                    //
                    // Detach the live-write subscription before the assignment so the
                    // resulting PropertyChanged(IsModified=false) does NOT re-enter this
                    // method. The re-entry would hit the IsModified=false branch above
                    // (line ~440), which calls RemoveValue AND clears EditorError back to
                    // null — silently swallowing the error message we just set. _selfWriting
                    // only guards OnWorkspaceChanged, not this handler, so unsub/resub is
                    // the right tool for the OnEditorPropertyChanged re-entry case.
                    editor.PropertyChanged -= OnEditorPropertyChanged;
                    try
                    {
                        editor.IsModified = false;
                    }
                    finally
                    {
                        editor.PropertyChanged += OnEditorPropertyChanged;
                    }
                }
            }

            RebuildEffectiveRows();
            RebuildJsonPreview();
        }

        // Re-evaluate FilteredEditors when a deprecated property transitions
        // between "set somewhere" and "nowhere set" — the visibility rule
        // depends on IsSetAnywhere so the list must refresh.
        if (e.PropertyName is nameof(LibVm.PropertyEditorViewModel.IsSetAnywhere))
        {
            OnPropertyChanged(nameof(FilteredEditors));
        }
    }

    private void RebuildEffectiveRows()
    {
        // The Effective tab answers the question "what value does Claude
        // actually see at runtime for each setting in this group?". A
        // property that no scope has ever set has no answer worth showing
        // — the row would just say "(not set)" with no scope chiclet, which
        // is noise for a user reading off effective values. Filter those
        // out here. The Properties tab still lists every schema-defined
        // setting whether or not it has a value, so users who want to see
        // "what could be set" have a separate surface for that view.
        List<EffectivePropertyRow> rows = SchemaNodes
                                          .Select(node =>
                                          {
                                              LayeredValue layered = _workspace.GetLayeredValue(node.JsonPath);
                                              if (layered.EffectiveValue is null)
                                              {
                                                  return null;
                                              }

                                              string display = layered.EffectiveValue.ToJsonString();
                                              // Truncate very long values (e.g. nested object JSON) for readability.
                                              if (display.Length > 120)
                                              {
                                                  display = display[..120] + "…";
                                              }

                                              return new EffectivePropertyRow(
                                                  node.Title ?? node.Name,
                                                  display,
                                                  layered.EffectiveScope,
                                                  layered.IsOverridden);
                                          })
                                          .Where(r => r is not null)
                                          .Select(r => r!)
                                          .ToList();
        EffectiveRows = rows;
        OnPropertyChanged(nameof(EffectiveRows));
    }

    private void RebuildJsonPreview()
    {
        JsonPreview = ShowJsonPlaceholders
            ? BuildPlaceholderJson()
            : BuildActiveJson();
        OnPropertyChanged(nameof(JsonPreview));
    }

    private string BuildActiveJson()
    {
        JsonObject obj = new();
        foreach (SchemaNode node in SchemaNodes)
        {
            JsonNode? value = _workspace.GetLayeredValue(node.JsonPath).GetValueAt(EditingScope);
            if (value == null)
            {
                continue;
            }

            obj[NodeKey(node)] = value.DeepClone();
        }

        return ToIndentedJson(obj);
    }

    /// <summary>
    /// Build a JSON object containing every schema property with either its active
    /// value (if set at the editing scope) or a type-appropriate placeholder / default.
    /// Complex/specialized nodes (e.g. permissions, hooks) are omitted — they can't be
    /// meaningfully represented as a simple scalar placeholder.
    /// </summary>
    private string BuildPlaceholderJson()
    {
        JsonObject obj = new();
        foreach (SchemaNode node in SchemaNodes)
        {
            JsonNode? active = _workspace.GetLayeredValue(node.JsonPath).GetValueAt(EditingScope);
            if (active != null)
            {
                obj[NodeKey(node)] = active.DeepClone();
                continue;
            }

            JsonNode? placeholder = BuildPlaceholder(node);
            if (placeholder != null)
            {
                obj[NodeKey(node)] = placeholder;
            }
        }

        return ToIndentedJson(obj);
    }

    private static JsonNode? BuildPlaceholder(SchemaNode node)
    {
        return node.ValueType switch
        {
            // Use explicit casts to select the non-generic JsonValue.Create overloads,
            // which are trim-safe — the generic Create<T>(T?) overload carries
            // [RequiresUnreferencedCode] and triggers IL2026 in Release/trimmed builds.
            //
            // Each typed branch emits a sensible non-null stub when the schema has
            // no DefaultValue. Previously the no-default branches built
            // JsonValue.Create((T?)null), which actually returns null (System.Text.
            // Json swallows nulls in the typed factory), so the calling code's
            // `if (placeholder != null)` guard dropped the key entirely. Users
            // saw empty {} in "show all" mode for any group whose schema nodes
            // lacked declared defaults. Emitting false / 0 / "" gives the user
            // a visible, syntactically correct stub they can edit.
            SchemaValueType.Boolean => JsonValue.Create(false),
            SchemaValueType.Integer => JsonValue.Create(0L),
            SchemaValueType.Number => JsonValue.Create(0.0),
            SchemaValueType.String => JsonValue.Create(node.DefaultValue ?? string.Empty),
            SchemaValueType.Enum => JsonValue.Create(
                node.DefaultValue
                ?? (node.EnumValues.Count > 0 ? node.EnumValues[0] : string.Empty)),
            SchemaValueType.Path => JsonValue.Create(node.DefaultValue ?? string.Empty),
            SchemaValueType.Array => new JsonArray(),
            // Object: recurse into the schema's child properties so nested settings
            // appear as their own placeholder keys, matching the runtime shape Claude
            // would actually read. Falls back to an empty {} when the schema does not
            // declare children (e.g. an open-ended dict like a name → config map).
            SchemaValueType.Object => BuildObjectPlaceholder(node),
            // Complex: a specialized editor (permissions, hooks, MCP servers,
            // marketplaces, enabled plugins). Emit a key-specific shape when we know
            // the structure (so the user sees something useful in "show all" mode);
            // otherwise emit an empty {} so the key still appears in the output.
            // This replaces the previous behaviour of dropping the key entirely,
            // which left pages like Permissions and Plugins showing only "{}".
            SchemaValueType.Complex => BuildComplexPlaceholder(node),
            // Unknown — skip; we have no type information to construct from.
            var _ => null,
        };
    }

    /// <summary>
    /// Build a placeholder JsonObject from a schema node's child Properties so
    /// nested keys show up in the "show all / include defaults" JSON output.
    /// Returns an empty <see cref="JsonObject"/> when no children are declared,
    /// which still gives the user a visible key for the property at the parent
    /// level (rather than silently dropping it).
    /// </summary>
    private static JsonObject BuildObjectPlaceholder(SchemaNode node)
    {
        JsonObject obj = new();
        foreach (SchemaNode child in node.Properties)
        {
            JsonNode? p = BuildPlaceholder(child);
            if (p != null)
            {
                obj[child.Name] = p;
            }
        }

        return obj;
    }

    /// <summary>
    /// Build a structural placeholder for a Complex schema node (one handled by a
    /// specialized editor). Known top-level keys get a richer skeleton so the user
    /// sees the expected substructure; everything else falls back to an empty
    /// <see cref="JsonObject"/> so the key is still rendered.
    /// <para>
    /// Per-key knowledge is intentionally minimal — we only encode the shape for
    /// <c>permissions</c> (allow / deny / ask / defaultMode) because that's the
    /// one with a fixed, well-known sub-schema. <c>hooks</c>, <c>mcpServers</c>,
    /// <c>enabledPlugins</c>, and <c>extraKnownMarketplaces</c> are all
    /// open-ended dictionaries (key → config); an empty <c>{}</c> is the most
    /// honest placeholder for them.
    /// </para>
    /// </summary>
    private static JsonObject BuildComplexPlaceholder(SchemaNode node)
    {
        return node.JsonPath switch
        {
            "permissions" => new JsonObject
            {
                ["defaultMode"] = JsonValue.Create((string?)null),
                ["allow"] = new JsonArray(),
                ["deny"] = new JsonArray(),
                ["ask"] = new JsonArray(),
            },
            // hooks, mcpServers, enabledPlugins, extraKnownMarketplaces, etc.
            var _ => [],
        };
    }

    private static string NodeKey(SchemaNode node)
    {
        string path = node.JsonPath;
        return path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..] : path;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspace.Changed -= OnWorkspaceChanged;
        _sharedScope.PropertyChanged -= OnSharedScopePropertyChanged;
        // Unsubscribe all current editor property-changed handlers to prevent
        // stale callbacks after this VM is discarded on workspace reload.
        foreach (LibVm.PropertyEditorViewModel editor in Editors)
        {
            editor.PropertyChanged -= OnEditorPropertyChanged;
        }
    }
}