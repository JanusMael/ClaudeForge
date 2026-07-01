using System.Collections.ObjectModel;
using System.Globalization;
using System.Security;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// "Agents &amp; Skills" page — a single nav node with an in-page segmented
/// control (Sub-agents / Skills / Slash Commands tabs).  Group #2 (Tier 1):
/// scope-aware read-only inventory + structured front-matter card + markdown
/// body viewer.  No editing yet (Tier 2 / group #3 adds that).
///
/// <para>
/// Backed by the scope-aware <see cref="EditableMemoryService"/> — walks
/// User + Project + Plugin scopes.  Selecting a row reads the file lazily,
/// parses its front-matter via <see cref="YamlFrontMatter"/>, and projects
/// it to the per-kind typed view for the structured card; the markdown
/// body (post-front-matter) renders below.
/// </para>
/// </summary>
public sealed partial class AgentsSkillsEditorViewModel : ObservableObject, IDisposable
{
    private readonly string? _projectRoot;
    private readonly IShellLauncher? _shellLauncher;
    private readonly IDialogService? _dialogService;
    private bool _disposed;

    // Defers the initial filesystem scan until the page is first navigated to.
    // BuildNavigationTree creates a fresh VM on every profile switch; without
    // this guard the disk walk would fire even if the user never visits the page.
    private bool _loaded;

    // Serialises concurrent refreshes so the Clear+Add rebuild of each
    // ObservableCollection stays atomic across the ctor's fire-and-forget
    // Refresh() and any later bound Refresh button (same rationale as
    // MemoryEditorViewModel._refreshLock).
    private readonly SemaphoreSlim _refreshLock = new(initialCount: 1, maxCount: 1);

    // Reset on each refresh so a superseded background description-fill stops
    // writing into rows that are about to be replaced.
    private CancellationTokenSource _descriptionFillCts = new();

    // Cancels the in-flight artifact read when the user clicks a different row
    // before the previous file-read completes.  Without this, a slow read would
    // land after the user has already moved on and overwrite the selection.
    private CancellationTokenSource _loadCts = new();

    public AgentsSkillsEditorViewModel(
        string? projectRoot, IShellLauncher? shellLauncher, IDialogService? dialogService)
    {
        _projectRoot = projectRoot;
        _shellLauncher = shellLauncher;
        _dialogService = dialogService;
        AgentItems = [];
        SkillItems = [];
        CommandItems = [];
        // No eager Refresh() here — the disk walk is deferred until the page
        // is first selected (EnsureLoaded), so profile switches don't pay the
        // scan cost when the user never visits this page in that session.
    }

    /// <summary>Convenience ctor — shell-launch but no dialog plumbing.</summary>
    public AgentsSkillsEditorViewModel(string? projectRoot, IShellLauncher? shellLauncher)
        : this(projectRoot, shellLauncher, dialogService: null)
    {
    }

    /// <summary>Test/fixture convenience ctor — no shell-launch / dialog plumbing.</summary>
    public AgentsSkillsEditorViewModel(string? projectRoot)
        : this(projectRoot, shellLauncher: null, dialogService: null)
    {
    }

    /// <summary>
    /// Triggers the initial filesystem scan the first time this page is
    /// navigated to.  Idempotent: subsequent calls after the first load
    /// are no-ops (explicit <see cref="Refresh"/> or the UI Refresh button
    /// still work normally after the initial load completes).
    /// Called by <c>MainWindowViewModel.OnSelectedNodeChanged</c> when this
    /// editor becomes active.
    /// </summary>
    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Refresh();
    }

    // ── Lists (one per segment) ──────────────────────────────────────────
    //
    // Each tab is a flat collection of section headers + rows:
    //   [ "Yours" header, ...writable rows, "Plugin" header, ...plugin rows ]
    // A header for a group is only present when that group is non-empty.
    // Headers are ArtifactSectionHeaderViewModel; rows are
    // ArtifactRowViewModel.  Flat-with-headers lets one virtualizing list
    // scroll the whole tab (nested per-group lists break virtualization).

    /// <summary>Sub-agent segment: grouped headers + rows.</summary>
    public ObservableCollection<object> AgentItems { get; }

    /// <summary>Skill segment: grouped headers + rows.</summary>
    public ObservableCollection<object> SkillItems { get; }

    /// <summary>Slash-command segment: grouped headers + rows.</summary>
    public ObservableCollection<object> CommandItems { get; }

    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Test seam: the in-flight lazy description-fill task from the last
    /// refresh, so tests can await subtitle population deterministically.  In
    /// the app this runs fire-and-forget after the rows are already on screen.
    /// </summary>
    public Task? LastDescriptionFill { get; private set; }

    // ── Viewer / detail state ────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewerVisible))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(ShowEditButton))]
    [NotifyPropertyChangedFor(nameof(SelectedArtifactPath))]
    [NotifyCanExecuteChangedFor(nameof(BeginEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleRawModeCommand))]
    private ArtifactRowViewModel? _selectedArtifact;

    /// <summary><see langword="true"/> when a row is selected and the detail pane should show.</summary>
    public bool IsViewerVisible => SelectedArtifact is not null;

    /// <summary>
    /// Null-safe path helper for CommandParameter bindings.
    /// Binding directly to <c>SelectedArtifact.AbsolutePath</c> when
    /// <c>SelectedArtifact</c> is null makes Avalonia log a binding-traversal
    /// warning on every deselect. This flattened property avoids the intermediate
    /// null step.
    /// </summary>
    public string? SelectedArtifactPath => SelectedArtifact?.AbsolutePath;

    /// <summary>The markdown body (everything after the front-matter) of the selected file.</summary>
    [ObservableProperty] private string? _viewerBody;

    // Structured front-matter card fields — populated on selection.  Kept as
    // plain strings + visibility flags so the read-only card needs no
    // per-kind View switching beyond IsVisible bindings.

    [ObservableProperty] private string? _cardName;
    [ObservableProperty] private string? _cardDescription;
    [ObservableProperty] private string? _cardModel;
    [ObservableProperty] private string? _cardTools;
    [ObservableProperty] private bool _cardShowToolsAndModel; // agents only
    [ObservableProperty] private bool _cardShowName; // agents + skills (not commands)

    // ── Edit state (group #3) ────────────────────────────────────────────

    // The front-matter parsed on the last load.  Save mutates a copy of this
    // (preserving comments + un-modelled keys) rather than rebuilding from
    // scratch, so a hand-written file keeps everything the editor doesn't
    // model.
    private FrontMatter? _currentFrontMatter;

    // Once-per-session gate for the "applies to your next session" hint.
    // Static so it survives the VM being recreated on each nav-tree rebuild
    // (the page is not cached in group #2).  Process-lifetime by design.
    private static bool _restartHintShownThisSession;

    /// <summary>
    /// Static Claude Code built-in tool names offered as autocomplete in the
    /// tools editor.  MCP server names are a planned follow-up (open question
    /// #4) — the suggestion source is kept here so it can be unioned with live
    /// MCP names later without reworking the editor.
    /// </summary>
    public static IReadOnlyList<string> KnownTools { get; } =
    [
        "Bash", "Edit", "Glob", "Grep", "Read", "Write", "NotebookEdit",
        "WebFetch", "WebSearch", "Task", "TodoWrite",
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypedEditVisible))]
    [NotifyPropertyChangedFor(nameof(IsRawEditVisible))]
    [NotifyPropertyChangedFor(nameof(ShowEditButton))]
    private bool _isEditing;

    /// <summary>
    /// <see langword="true"/> when the selected row is writable (User /
    /// Project scope).  Plugin rows are read-only — the Edit button is
    /// disabled and the read-only badge shows.
    /// </summary>
    public bool CanEdit => SelectedArtifact?.IsWritable == true;

    /// <summary>
    /// <see langword="true"/> when the Edit button should be shown.
    /// The button is hidden while in edit mode (Save/Cancel take over)
    /// AND for plugin (read-only) rows — showing a disabled Edit button
    /// next to the read-only badge is redundant and confusing.
    /// </summary>
    public bool ShowEditButton => !IsEditing && CanEdit;

    /// <summary>Typed edit card is shown when editing and NOT in raw mode.</summary>
    public bool IsTypedEditVisible => IsEditing && !IsRawMode;

    /// <summary>Raw front-matter editor is shown when editing AND in raw mode.</summary>
    public bool IsRawEditVisible => IsEditing && IsRawMode;

    [ObservableProperty] private string? _editName;
    [ObservableProperty] private string? _editDescription;
    [ObservableProperty] private string? _editModel;
    [ObservableProperty] private string? _editTools; // comma- or newline-separated
    [ObservableProperty] private string? _editBody;

    /// <summary>Transient post-save status line shown under the detail toolbar.</summary>
    [ObservableProperty] private string? _lastSaveMessage;

    // ── Raw front-matter editing (mutually exclusive with the typed fields) ──
    //
    // Editing raw is the escape hatch for front-matter keys the typed card
    // doesn't model (arbitrary / plugin-specific keys) and for comment edits.
    // It's deliberately mutually exclusive with the typed fields — while raw
    // is open, the typed card is disabled, so there is never a dual source of
    // truth.  Toggling raw OFF discards the raw text and reverts to the typed
    // values; saving from raw mode uses the raw text (typed fields ignored).

    /// <summary>
    /// <see langword="true"/> when the raw front-matter TextBox is the active
    /// editor (typed card disabled).  Seeded from the current typed state when
    /// turned on; cleared when turned off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypedEditEnabled))]
    [NotifyPropertyChangedFor(nameof(IsTypedEditVisible))]
    [NotifyPropertyChangedFor(nameof(IsRawEditVisible))]
    private bool _isRawMode;

    /// <summary>The raw front-matter block text (between the <c>---</c> fences, exclusive) while in raw mode.</summary>
    [ObservableProperty] private string? _editRawFrontMatter;

    /// <summary>Validation message shown when the raw front-matter can't be parsed on save.</summary>
    [ObservableProperty] private string? _rawValidationMessage;

    /// <summary>Typed fields are editable only when in edit mode AND not in raw mode.</summary>
    public bool IsTypedEditEnabled => !IsRawMode;

    /// <summary>
    /// Seed the raw box from the current typed edits when entering raw mode;
    /// clear it (and any validation) when leaving.  Reverting to typed mode
    /// discards raw edits by design — the typed fields keep their values.
    /// </summary>
    partial void OnIsRawModeChanged(bool value)
    {
        if (value)
        {
            if (SelectedArtifact is not { } row)
            {
                return;
            }

            FrontMatter fm = ApplyEdits(_currentFrontMatter ?? FrontMatter.None(string.Empty), row.Entry.Category);
            EditRawFrontMatter = ExtractFrontMatterBlock(fm);
            RawValidationMessage = null;
        }
        else
        {
            EditRawFrontMatter = null;
            RawValidationMessage = null;
        }
    }

    // ── Refresh ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Cancel and free the in-flight description fill and any pending load.
        _descriptionFillCts.Cancel();
        _descriptionFillCts.Dispose();
        _loadCts.Cancel();
        _loadCts.Dispose();
        _refreshLock.Dispose();
    }

    /// <summary>Synchronous shortcut for the ctor — fire-and-forget refresh.</summary>
    public void Refresh()
    {
        _ = RefreshAsync();
    }

    /// <summary>
    /// Re-walk the scope-aware service and rebuild the three segment lists.
    /// Runs the disk walk on the thread pool so a workstation with many
    /// artifact files doesn't freeze the dispatcher (same guard as the
    /// Memory page).
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync().ConfigureAwait(true);
        try
        {
            // Cancel any in-flight description fill from a previous refresh.
            await _descriptionFillCts.CancelAsync().ConfigureAwait(true);
            _descriptionFillCts.Dispose();
            _descriptionFillCts = new CancellationTokenSource();
            CancellationToken fillCt = _descriptionFillCts.Token;

            IsBusy = true;
            try
            {
                // Fast, stat-only walk on the thread pool — no file contents
                // read here, so the lists can render immediately.
                IReadOnlyList<EditableMemoryEntry> entries =
                    await Task.Run(() => EditableMemoryService.Snapshot(_projectRoot)).ConfigureAwait(true);

                var rows = new List<ArtifactRowViewModel>();
                FillGrouped(AgentItems, entries, UserMemoryCategory.Subagent, rows);
                FillGrouped(SkillItems, entries, UserMemoryCategory.Skill, rows);
                FillGrouped(CommandItems, entries, UserMemoryCategory.SlashCommand, rows);

                Log.Information(
                    "[AgentsSkills.Refresh] rows={Rows} (agents+skills+commands incl. headers)", rows.Count);

                // Kick the lazy description fill — rows are already on screen.
                // Captured (not discarded) so tests can await completion
                // deterministically; in the app it runs fire-and-forget.
                LastDescriptionFill = FillDescriptionsAsync(rows, fillCt);
            }
            catch (Exception ex)
            {
                // Snapshot is internally guarded, but a refresh is kicked
                // fire-and-forget from the ctor / Refresh button, so an
                // unexpected throw here would otherwise go unobserved.  Log
                // it and leave the lists empty rather than crashing the page.
                Log.Error(ex, "[AgentsSkills.Refresh] snapshot/build failed — clearing lists");
                AgentItems.Clear();
                SkillItems.Clear();
                CommandItems.Clear();
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Rebuild one segment's flat item collection: a "Yours" header + writable
    /// (User/Project) rows, then a "Plugin" header + read-only plugin rows.
    /// A group's header is omitted when that group is empty.  Every created
    /// row is also appended to <paramref name="allRows"/> for the lazy
    /// description fill.
    /// </summary>
    private static void FillGrouped(
        ObservableCollection<object> target,
        IReadOnlyList<EditableMemoryEntry> entries,
        UserMemoryCategory category,
        List<ArtifactRowViewModel> allRows)
    {
        target.Clear();

        List<ArtifactRowViewModel> Build(Func<EditableMemoryEntry, bool> pred)
        {
            return entries
                   .Where(e => e.Category == category && pred(e))
                   .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
                   .Select(e => new ArtifactRowViewModel(e))
                   .ToList();
        }

        List<ArtifactRowViewModel> yours = Build(e => e.Scope != EditableMemoryScope.Plugin);
        List<ArtifactRowViewModel> plugin = Build(e => e.Scope == EditableMemoryScope.Plugin);

        if (yours.Count > 0)
        {
            target.Add(new ArtifactSectionHeaderViewModel("Yours", IsReadOnly: false));
            foreach (ArtifactRowViewModel row in yours)
            {
                target.Add(row);
                allRows.Add(row);
            }
        }

        if (plugin.Count > 0)
        {
            target.Add(new ArtifactSectionHeaderViewModel("Plugin", IsReadOnly: true));
            foreach (ArtifactRowViewModel row in plugin)
            {
                target.Add(row);
                allRows.Add(row);
            }
        }
    }

    /// <summary>
    /// Lazily fill each row's <see cref="ArtifactRowViewModel.Subtitle"/> from
    /// the file's <c>description</c> front-matter.  Reads happen on the thread
    /// pool (bounded 8 KiB head-read each); results are marshalled back to set
    /// the observable subtitle.  Cancellable — superseded by the next refresh.
    /// </summary>
    private static async Task FillDescriptionsAsync(
        IReadOnlyList<ArtifactRowViewModel> rows, CancellationToken ct)
    {
        try
        {
            foreach (ArtifactRowViewModel row in rows)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string path = row.AbsolutePath;
                string? description = await Task.Run(
                    () => EditableMemoryService.LoadDescription(path), ct).ConfigureAwait(true);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // Back on the UI thread (ConfigureAwait(true)) — safe to set
                // the observable property.
                row.Subtitle = string.IsNullOrWhiteSpace(description) ? "(no description)" : description;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer refresh — nothing to do.
        }
        catch (Exception ex)
        {
            // Background, best-effort subtitle fill — never crash the app over
            // a description read.  Log so a systemic failure is still visible.
            Log.Warning(ex, "[AgentsSkills] background description fill failed");
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Load a row into the detail pane — read file, parse front-matter, populate the card + body.</summary>
    [RelayCommand]
    public async Task LoadArtifactAsync(ArtifactRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // Cancel any in-flight load (e.g. user clicked a second row before the
        // first file-read completed).  Without this the slow read would land
        // after the user has moved on and silently overwrite SelectedArtifact.
        await _loadCts.CancelAsync().ConfigureAwait(true);
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
        CancellationToken loadCt = _loadCts.Token;

        // Opening a new row always exits any in-progress edit, resets raw mode,
        // and clears the transient save message.
        IsEditing = false;
        IsRawMode = false;
        LastSaveMessage = null;

        string? text;
        try
        {
            text = await EditableMemoryService.ReadAsync(row.AbsolutePath, loadCt)
                                              .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer row was clicked — discard this load silently.
            return;
        }

        if (text is null)
        {
            // File vanished or unreadable — surface a placeholder rather than crash.
            ResetCard();
            _currentFrontMatter = null;
            ViewerBody = "(file no longer available)";
            SelectedArtifact = row;
            return;
        }

        // Parse + card population are pure string operations and shouldn't
        // throw, but a defensive guard means a pathologically-shaped file
        // degrades to "show the raw text" rather than breaking the page.
        try
        {
            FrontMatter fm = YamlFrontMatter.Parse(text);
            _currentFrontMatter = fm;
            PopulateCard(row, fm);
            ViewerBody = fm.Present ? fm.Body : text;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AgentsSkills] front-matter parse/populate failed for {Path} — showing raw text",
                row.AbsolutePath);
            ResetCard();
            _currentFrontMatter = null;
            ViewerBody = text;
        }

        SelectedArtifact = row;

        Log.Information("[AgentsSkills.Command] action=View kind={Kind} scope={Scope} name={Name}",
            row.Entry.Category, row.Entry.Scope, row.DisplayName);
    }

    /// <summary>Close the detail pane and return to the segmented lists.</summary>
    [RelayCommand]
    public void CloseViewer()
    {
        SelectedArtifact = null;
        ViewerBody = null;
        _currentFrontMatter = null;
        IsEditing = false;
        IsRawMode = false;
        LastSaveMessage = null;
        ResetCard();
    }

    /// <summary>Reveal the supplied path in the platform file manager.</summary>
    [RelayCommand]
    public void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _shellLauncher?.RevealInFileManager(path);
        Log.Information("[AgentsSkills.Command] action=Reveal");
    }

    /// <summary>Open the supplied path in the platform default editor.</summary>
    [RelayCommand]
    public void OpenExternally(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _shellLauncher?.OpenInDefaultEditor(path);
        Log.Information("[AgentsSkills.Command] action=OpenExternally");
    }

    /// <summary>
    /// Delete the row's artifact — a file, or the whole directory for a skill —
    /// after a Destructive confirm.  Gated on
    /// <see cref="ArtifactRowViewModel.IsDeletable"/>: plugin (read-only) rows are
    /// never deletable (the governing theme: never delete things installed by
    /// another thing).  Closes the detail pane if the deleted row was open, then
    /// refreshes the lists.
    /// </summary>
    /// <remarks>
    /// Reuses the footprint-delete localised strings — their values are generic
    /// and reading identically here avoids minting new keys that would each need
    /// a translation across all eight locale resx files.
    /// </remarks>
    [RelayCommand]
    public async Task DeleteArtifactAsync(ArtifactRowViewModel? row)
    {
        if (row is null || !row.IsDeletable)
        {
            return;
        }

        Log.Information("[AgentsSkills.Command] action=Delete kind={Kind} scope={Scope} name={Name}",
            row.Entry.Category, row.Entry.Scope, row.DisplayName);

        (string targetPath, int fileCount, long bytes) = await Task
            .Run(() => MemoryArtifactDeleter.StatTarget(row.AbsolutePath, row.IsSkill, row.Entry.SizeBytes))
            .ConfigureAwait(true);

        if (_dialogService is not null)
        {
            DialogMessage msg = DialogMessage.Builder()
                                             .Text(string.Format(
                                                 CultureInfo.CurrentCulture,
                                                 Strings.MsgDeleteFootprintConfirmFmt,
                                                 fileCount,
                                                 FormatBytes(bytes)))
                                             .Text("\n\n")
                                             .Path(targetPath)
                                             .Text("\n\nThis cannot be undone.")
                                             .Build();
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                string.Format(CultureInfo.CurrentCulture, Strings.TitleDeleteFootprintFmt, row.DisplayName),
                msg,
                DialogCategory.Destructive,
                confirmLabel: Strings.ButtonDeleteFootprint).ConfigureAwait(true);
            // Binary destructive yes/no — both Cancel (false) and X (null) abort.
            if (confirmed != true)
            {
                return;
            }
        }

        IsBusy = true;
        try
        {
            // Close the detail pane if the row being deleted is the one open.
            if (SelectedArtifact is not null
                && string.Equals(SelectedArtifact.AbsolutePath, row.AbsolutePath, StringComparison.Ordinal))
            {
                CloseViewer();
            }

            await MemoryArtifactDeleter.DeleteAsync(row.AbsolutePath, row.IsSkill, CancellationToken.None)
                                       .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        // Re-walk the scopes so the deleted row drops out of its segment list.
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Humanised byte count (e.g. "2.4 MB"); invariant separator for a technical badge.</summary>
    private static string FormatBytes(long bytes)
    {
        const double KB = 1024d;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        return bytes switch
        {
            >= (long)GB => FormattableString.Invariant($"{bytes / GB:0.0} GB"),
            >= (long)MB => FormattableString.Invariant($"{bytes / MB:0.0} MB"),
            >= (long)KB => FormattableString.Invariant($"{bytes / KB:0.0} KB"),
            var _ => FormattableString.Invariant($"{bytes} B"),
        };
    }

    // ── Edit / Save (group #3) ───────────────────────────────────────────

    /// <summary>
    /// Enter edit mode — seed the edit fields from the current card values.
    /// Gated on <see cref="CanEdit"/> so plugin (read-only) rows can't edit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void BeginEdit()
    {
        if (!CanEdit)
        {
            return;
        }

        EditName = CardName;
        EditDescription = CardDescription;
        EditModel = CardModel;
        EditTools = CardTools;
        EditBody = ViewerBody;
        LastSaveMessage = null;
        IsRawMode = false; // always start in the typed editor
        IsEditing = true;

        Log.Information("[AgentsSkills.Command] action=BeginEdit name={Name}",
            SelectedArtifact?.DisplayName);
    }

    /// <summary>Discard edits and return to the read-only detail view.</summary>
    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
        IsRawMode = false;
        LastSaveMessage = null;
    }

    /// <summary>
    /// Toggle the raw front-matter editor on/off.  On → seed the raw box from
    /// the current typed edits and disable the typed card (the
    /// <c>OnIsRawModeChanged</c> partial does the seeding).  Off → discard the
    /// raw text and revert to the typed fields.  Gated on edit mode.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void ToggleRawMode()
    {
        if (!IsEditing)
        {
            return;
        }

        IsRawMode = !IsRawMode;
    }

    /// <summary>
    /// Compose the edited front-matter (preserving comments + un-modelled
    /// keys from the parsed original) plus the edited body, write atomically,
    /// then refresh the read-only card / body from the saved content.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task SaveAsync()
    {
        if (SelectedArtifact is not { } row || !row.IsWritable)
        {
            return;
        }

        FrontMatter fm;
        if (IsRawMode)
        {
            // Raw mode: the raw front-matter text is authoritative (typed
            // fields ignored).  Parse + validate before writing.
            FrontMatter? parsed = ParseRawFrontMatter(EditRawFrontMatter);
            if (parsed is null)
            {
                RawValidationMessage = Strings.StatusRawFrontMatterInvalid;
                Log.Information("[AgentsSkills.Command] action=Save REJECTED — invalid raw front-matter");
                return;
            }

            RawValidationMessage = null;
            fm = parsed;
        }
        else
        {
            // Typed mode: start from the parsed original so comments + unknown
            // keys survive; fall back to an empty present block.
            fm = _currentFrontMatter ?? FrontMatter.None(string.Empty);
            fm = ApplyEdits(fm, row.Entry.Category);
        }

        string composed = YamlFrontMatter.Compose(fm with { Body = NormaliseBody(EditBody) });

        try
        {
            await MemoryFileWriter.WriteAsync(row.AbsolutePath, composed, CancellationToken.None)
                                  .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            LastSaveMessage = string.Format(
                CultureInfo.CurrentCulture, Strings.StatusArtifactSaveFailedFmt, ex.Message);
            Log.Warning(ex, "[AgentsSkills.Command] action=Save FAILED path={Path}", row.AbsolutePath);
            return;
        }

        // Re-read from disk so the card / body reflect exactly what was
        // written (confirms the round trip + picks up canonical re-rendering).
        string? written = await EditableMemoryService.ReadAsync(row.AbsolutePath, CancellationToken.None)
                                                     .ConfigureAwait(true);
        FrontMatter saved = written is not null ? YamlFrontMatter.Parse(written) : fm;
        _currentFrontMatter = saved;
        PopulateCard(row, saved);
        ViewerBody = saved.Present ? saved.Body : written ?? string.Empty;

        IsEditing = false;
        IsRawMode = false;
        RawValidationMessage = null;
        LastSaveMessage = FirstSaveHint();

        Log.Information("[AgentsSkills.UserEdit] action=Save kind={Kind} scope={Scope} name={Name}",
            row.Entry.Category, row.Entry.Scope, row.DisplayName);
    }

    /// <summary>
    /// Render a front-matter's block content (the lines between the
    /// <c>---</c> fences, fences excluded) for the raw editor.  Composes the
    /// front-matter with an empty body and strips the opening / closing
    /// delimiter lines.
    /// </summary>
    private static string ExtractFrontMatterBlock(FrontMatter fm)
    {
        // Compose with an empty body → "---\n{block}---\n".
        string composed = YamlFrontMatter.Compose(fm with { Present = true, Body = string.Empty });
        const string fence = "---\n";
        if (composed.StartsWith(fence, StringComparison.Ordinal))
        {
            composed = composed[fence.Length..];
        }

        if (composed.EndsWith(fence, StringComparison.Ordinal))
        {
            composed = composed[..^fence.Length];
        }

        return composed.TrimEnd('\n');
    }

    /// <summary>
    /// Parse the raw front-matter block text (fences excluded) back into a
    /// <see cref="FrontMatter"/>.  Wraps the text in <c>---</c> fences and
    /// runs it through <see cref="YamlFrontMatter.Parse"/>.  Returns
    /// <see langword="null"/> when the result isn't a valid present block
    /// (the editor then shows a validation message and refuses to save).
    /// </summary>
    private static FrontMatter? ParseRawFrontMatter(string? rawBlock)
    {
        string block = (rawBlock ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n');
        string assembled = "---\n" + block + "\n---\n";
        FrontMatter fm = YamlFrontMatter.Parse(assembled);
        return fm.Present ? fm : null;
    }

    /// <summary>
    /// Apply the edit-field values onto <paramref name="fm"/> per the kind's
    /// canonical keys.  Empty edits remove the key; non-empty set it.  Tools
    /// preserve the original list-vs-scalar shape to minimise the on-disk diff.
    /// </summary>
    private FrontMatter ApplyEdits(FrontMatter fm, UserMemoryCategory category)
    {
        switch (category)
        {
            case UserMemoryCategory.Subagent:
                fm = SetOrRemoveScalar(fm, "name", EditName);
                fm = SetOrRemoveScalar(fm, "description", EditDescription);
                fm = SetOrRemoveScalar(fm, "model", EditModel);
                fm = ApplyToolsEdit(fm);
                break;

            case UserMemoryCategory.Skill:
                fm = SetOrRemoveScalar(fm, "name", EditName);
                fm = SetOrRemoveScalar(fm, "description", EditDescription);
                break;

            case UserMemoryCategory.SlashCommand:
                fm = SetOrRemoveScalar(fm, "description", EditDescription);
                break;
        }

        return fm;
    }

    private FrontMatter ApplyToolsEdit(FrontMatter fm)
    {
        List<string> tools = ParseToolsInput(EditTools);
        if (tools.Count == 0)
        {
            return fm.Without("tools");
        }

        // Preserve the original shape: if the file had tools as a YAML list,
        // keep it a list; otherwise write Claude Code's native comma-scalar.
        bool originalWasList = _currentFrontMatter?.FindList("tools") is not null;
        return originalWasList
            ? fm.WithList("tools", tools)
            : fm.WithScalar("tools", string.Join(", ", tools));
    }

    /// <summary>Split a comma- or newline-separated tools input into trimmed, non-empty items.</summary>
    private static List<string> ParseToolsInput(string? input)
    {
        return string.IsNullOrWhiteSpace(input)
            ? []
            : input.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .ToList();
    }

    private static FrontMatter SetOrRemoveScalar(FrontMatter fm, string key, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? fm.Without(key) : fm.WithScalar(key, value!.Trim());
    }

    /// <summary>
    /// Ensure the body begins with a single blank line after the closing
    /// front-matter delimiter (the conventional shape) and isn't null.
    /// </summary>
    private static string NormaliseBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "\n";
        }

        // Compose appends body right after "---\n"; a leading blank line keeps
        // the conventional "---\n\n<content>" separation.
        return body.StartsWith('\n') || body.StartsWith("\r\n", StringComparison.Ordinal)
            ? body
            : "\n" + body;
    }

    /// <summary>
    /// The post-save status line.  The "applies to your next session" caveat
    /// shows once per process session (skills/agents are loaded at session
    /// start, so an edit doesn't affect a running Claude Code session).
    /// </summary>
    private static string FirstSaveHint()
    {
        if (_restartHintShownThisSession)
        {
            return Strings.StatusArtifactSaved;
        }

        _restartHintShownThisSession = true;
        return Strings.StatusArtifactSavedRestartHint;
    }

    // ── Card population ──────────────────────────────────────────────────

    private void PopulateCard(ArtifactRowViewModel row, FrontMatter fm)
    {
        switch (row.Entry.Category)
        {
            case UserMemoryCategory.Subagent:
                AgentFrontMatter agent = AgentFrontMatter.From(fm);
                CardName = agent.Name;
                CardDescription = agent.Description;
                CardModel = agent.Model;
                CardTools = agent.Tools.Count > 0 ? string.Join(", ", agent.Tools) : null;
                CardShowName = true;
                CardShowToolsAndModel = true;
                break;

            case UserMemoryCategory.Skill:
                SkillFrontMatter skill = SkillFrontMatter.From(fm);
                CardName = skill.Name;
                CardDescription = skill.Description;
                CardModel = null;
                CardTools = null;
                CardShowName = true;
                CardShowToolsAndModel = false;
                break;

            case UserMemoryCategory.SlashCommand:
                SlashCommandFrontMatter cmd = SlashCommandFrontMatter.From(fm);
                CardName = null;
                CardDescription = cmd.Description;
                CardModel = null;
                CardTools = null;
                CardShowName = false;
                CardShowToolsAndModel = false;
                break;

            default:
                ResetCard();
                break;
        }
    }

    private void ResetCard()
    {
        CardName = null;
        CardDescription = null;
        CardModel = null;
        CardTools = null;
        CardShowName = false;
        CardShowToolsAndModel = false;
    }
}