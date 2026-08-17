using System.Collections.ObjectModel;
using System.Globalization;
using System.Security;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.AgentForge.Abstractions.Dialogs;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
public sealed partial class AgentsSkillsEditorViewModel : ObservableObject, IDisposable, IDeepNavigable
{
    // ── Segment ids ──────────────────────────────────────────────────────
    //
    // Stable, culture-invariant ids for the three in-page segments, mirroring
    // GroupTab.PropertiesId / EffectiveId / JsonId.  SelectedSegmentIndex stays
    // the TabControl's binding — these ids are the EXTERNAL contract (deep links,
    // persisted state), so reordering the tabs can't silently repoint a saved
    // path the way a bare index would.

    /// <summary>Segment id for the Sub-agents tab.</summary>
    public const string SegmentSubagentsId = "subagents";

    /// <summary>Segment id for the Skills tab.</summary>
    public const string SegmentSkillsId = "skills";

    /// <summary>Segment id for the Slash Commands tab.</summary>
    public const string SegmentCommandsId = "commands";

    /// <summary>Map a segment id to its tab index, or <see langword="null"/> if unknown.</summary>
    internal static int? SegmentIndexFor(string? segmentId)
    {
        return segmentId?.ToLowerInvariant() switch
        {
            SegmentSubagentsId => 0,
            SegmentSkillsId => 1,
            SegmentCommandsId => 2,
            var _ => null,
        };
    }

    /// <summary>Map a tab index to its stable segment id.</summary>
    internal static string SegmentIdFor(int index)
    {
        return index switch
        {
            0 => SegmentSubagentsId,
            1 => SegmentSkillsId,
            2 => SegmentCommandsId,
            var _ => SegmentSubagentsId,
        };
    }

    /// <summary>
    /// Select a segment by its stable id; no-op when the id is unknown.
    /// Mirrors <see cref="SettingsGroupEditorViewModel.SelectTab"/>.
    /// </summary>
    public void SelectSegment(string? segmentId)
    {
        if (SegmentIndexFor(segmentId) is { } index)
        {
            SelectedSegmentIndex = index;
        }
    }

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

    /// <summary>Sub-agent segment: grouped headers + rows. Unfiltered source of truth.</summary>
    public ObservableCollection<object> AgentItems { get; }

    /// <summary>Skill segment: grouped headers + rows. Unfiltered source of truth.</summary>
    public ObservableCollection<object> SkillItems { get; }

    /// <summary>Slash-command segment: grouped headers + rows. Unfiltered source of truth.</summary>
    public ObservableCollection<object> CommandItems { get; }

    /// <summary>
    /// Every artifact row across all three segments (headers excluded), in the
    /// order they were built.  Exists so a future multi-select export can read
    /// <c>AllRows.Where(r =&gt; r.IsSelected)</c> without caring how the three
    /// per-segment lists are shaped or filtered.
    /// </summary>
    public IReadOnlyList<ArtifactRowViewModel> AllRows => _allRows;

    private List<ArtifactRowViewModel> _allRows = [];

    // ── Filter ───────────────────────────────────────────────────────────
    //
    // Mirrors the per-page filter that every other list-like page already has
    // (EffectiveSettingsViewModel.FilterText / SettingsGroupEditorViewModel
    // .FilteredEditors).  Matches an artifact's name, description, and source.
    //
    // The three Filtered* properties are COMPUTED, which means the bound
    // collections are no longer observable: FillGrouped's Clear()/Add() no
    // longer reaches the UI on its own.  NotifyFilteredListsChanged() must be
    // called after every rebuild — see the call sites in RefreshAsync and
    // FillDescriptionsThenNotifyAsync.  Same contract as
    // SettingsGroupEditorViewModel, which raises FilteredEditors by hand for
    // exactly this reason.

    /// <summary>Free-text filter applied to all three segment lists.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredAgentItems))]
    [NotifyPropertyChangedFor(nameof(FilteredSkillItems))]
    [NotifyPropertyChangedFor(nameof(FilteredCommandItems))]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(VisibleRowCount))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private string _filterText = string.Empty;

    /// <summary>
    /// <see langword="true"/> when the current <see cref="FilterText"/> was
    /// applied BY navigation (a deep link or a reload restore) rather than typed
    /// by the user.  Drives the "navigated" frame on the filter box so the user
    /// can see the list is narrowed and why.
    /// </summary>
    [ObservableProperty] private bool _filterFromNavigation;

    // Set only around ApplyNavigationFilter's write so OnFilterTextChanged can
    // tell a navigation-applied filter from a user edit.
    private bool _applyingNavFilter;

    /// <summary>
    /// Apply a filter on behalf of navigation — a deep link or a deep-path
    /// restore — and flag it as such so the view draws the "navigated" frame.
    /// <para>
    /// Deep-link handlers MUST use this rather than assigning
    /// <see cref="FilterText"/> directly, which reads as a user edit and skips
    /// the frame.  Same contract as
    /// <see cref="SettingsGroupEditorViewModel.ApplyNavigationFilter"/>.
    /// </para>
    /// </summary>
    public void ApplyNavigationFilter(string? filter)
    {
        _applyingNavFilter = true;
        try
        {
            FilterText = filter ?? string.Empty;
            FilterFromNavigation = !string.IsNullOrEmpty(FilterText);
        }
        finally
        {
            _applyingNavFilter = false;
        }
    }

    /// <summary>
    /// Any change NOT coming through <see cref="ApplyNavigationFilter"/> is a
    /// user edit or clear, which drops the deep-link "navigated" frame.
    /// </summary>
    partial void OnFilterTextChanged(string value)
    {
        if (!_applyingNavFilter)
        {
            FilterFromNavigation = false;
        }
    }

    /// <summary>Clear the filter and return every segment to its full list.</summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    /// <summary><see langword="true"/> when a filter is narrowing the lists.</summary>
    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(FilterText);

    /// <summary>Sub-agent segment, narrowed by <see cref="FilterText"/>.</summary>
    public IReadOnlyList<object> FilteredAgentItems => ApplyFilter(AgentItems, FilterText);

    /// <summary>Skill segment, narrowed by <see cref="FilterText"/>.</summary>
    public IReadOnlyList<object> FilteredSkillItems => ApplyFilter(SkillItems, FilterText);

    /// <summary>Slash-command segment, narrowed by <see cref="FilterText"/>.</summary>
    public IReadOnlyList<object> FilteredCommandItems => ApplyFilter(CommandItems, FilterText);

    /// <summary>Rows visible in the ACTIVE segment after filtering (headers excluded).</summary>
    public int VisibleRowCount => ActiveFilteredItems.OfType<ArtifactRowViewModel>().Count();

    /// <summary>Rows in the ACTIVE segment before filtering (headers excluded).</summary>
    public int TotalRowCount => ActiveItems.OfType<ArtifactRowViewModel>().Count();

    /// <summary>
    /// "shown of total" for the active segment, e.g. <c>"3 of 47"</c>.
    /// <para>
    /// Formatted here rather than via an AXAML <c>StringFormat</c> because the
    /// format takes TWO arguments and a single-binding <c>StringFormat</c> can
    /// only ever supply <c>{0}</c> — the count would render with a literal
    /// <c>{1}</c> in it.
    /// </para>
    /// </summary>
    public string FilterSummary => string.Format(
        CultureInfo.CurrentCulture, Strings.LabelArtifactFilterCountFmt, VisibleRowCount, TotalRowCount);

    private IReadOnlyList<object> ActiveFilteredItems => SelectedSegmentIndex switch
    {
        0 => FilteredAgentItems,
        1 => FilteredSkillItems,
        2 => FilteredCommandItems,
        var _ => Array.Empty<object>(),
    };

    private IReadOnlyList<object> ActiveItems => SelectedSegmentIndex switch
    {
        0 => AgentItems,
        1 => SkillItems,
        2 => CommandItems,
        var _ => Array.Empty<object>(),
    };

    /// <summary>
    /// Narrow one segment's flat [headers + rows] sequence to the rows matching
    /// <paramref name="filter"/>, dropping any section header whose group has no
    /// surviving row.
    ///
    /// <para>
    /// Two properties this MUST keep, both load-bearing elsewhere:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     Returns a MATERIALIZED list. The view re-enumerates the bound
    ///     collection on layout passes, so handing back a lazy query would
    ///     re-run the whole filter each time.
    ///   </item>
    ///   <item>
    ///     PROJECTS the same <see cref="ArtifactRowViewModel"/> instances rather
    ///     than rebuilding them, so per-row state (notably
    ///     <see cref="ArtifactRowViewModel.IsSelected"/>) survives the user
    ///     narrowing the list.
    ///   </item>
    /// </list>
    /// <para>
    /// Pure and static so it is unit-testable without constructing the VM.
    /// </para>
    /// </summary>
    internal static List<object> ApplyFilter(IEnumerable<object> flat, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [.. flat];
        }

        List<object> result = [];
        ArtifactSectionHeaderViewModel? pendingHeader = null;

        foreach (object item in flat)
        {
            if (item is ArtifactSectionHeaderViewModel header)
            {
                // Buffered, not emitted: a header earns its place only once a
                // row beneath it survives the filter.
                pendingHeader = header;
                continue;
            }

            if (item is ArtifactRowViewModel row)
            {
                if (!MatchesFilter(row, filter!))
                {
                    continue;
                }

                if (pendingHeader is not null)
                {
                    result.Add(pendingHeader);
                    pendingHeader = null;
                }

                result.Add(row);
                continue;
            }

            // Fail open on an unrecognised item type: if a third item kind is
            // ever added to these lists, it stays visible rather than silently
            // vanishing the moment the user types in the filter box.
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Match an artifact against the filter on name, description, or source.
    /// <para>
    /// <see cref="ArtifactRowViewModel.Subtitle"/> (the front-matter
    /// description) is filled asynchronously by
    /// <see cref="FillDescriptionsAsync"/>, so a filter typed before that
    /// completes cannot match on it yet — which is why the fill re-raises the
    /// filtered-list notifications when it finishes.
    /// </para>
    /// </summary>
    private static bool MatchesFilter(ArtifactRowViewModel row, string filter)
    {
        return row.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || row.Source.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (row.Subtitle is { } subtitle
                   && subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Re-raise the computed filtered-list properties (and the counts derived
    /// from them).  Required after ANY rebuild of the underlying
    /// <see cref="ObservableCollection{T}"/>s, because the view binds the
    /// computed projections rather than the collections themselves.
    /// </summary>
    private void NotifyFilteredListsChanged()
    {
        OnPropertyChanged(nameof(FilteredAgentItems));
        OnPropertyChanged(nameof(FilteredSkillItems));
        OnPropertyChanged(nameof(FilteredCommandItems));
        OnPropertyChanged(nameof(VisibleRowCount));
        OnPropertyChanged(nameof(TotalRowCount));
        OnPropertyChanged(nameof(FilterSummary));
    }

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
    [NotifyCanExecuteChangedFor(nameof(CopyDeepLinkCommand))]
    [NotifyPropertyChangedFor(nameof(CanCopyDeepLink))]
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

    // Which segment (Sub-agents / Skills / Slash Commands) the TabControl shows.
    // VM-driven so the change is observable + logged, not view-only. 0=Sub-agents,
    // 1=Skills, 2=Slash Commands.  The counts are per-active-segment, so they
    // have to re-read when the segment changes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRowCount))]
    [NotifyPropertyChangedFor(nameof(TotalRowCount))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private int _selectedSegmentIndex;

    partial void OnSelectedSegmentIndexChanged(int value)
    {
        string segment = value switch
        {
            0 => "Subagents",
            1 => "Skills",
            2 => "SlashCommands",
            _ => "?",
        };
        Log.Information("[AgentsSkills.Tab] index={Index} segment={Segment}", value, segment);
    }

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
    [ObservableProperty] private string? _lastActionMessage;

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

    /// <summary>
    /// Synchronous shortcut for the ctor and for
    /// <c>MainWindowViewModel.OnSelectedNodeChanged</c> — fire-and-forget refresh.
    /// The task is retained in <see cref="LastRefresh"/> so a deep-path restore
    /// can await THIS walk instead of starting a competing one.
    /// </summary>
    public void Refresh()
    {
        LastRefresh = RefreshAsync();
    }

    /// <summary>
    /// The most recent refresh task, so a caller that needs the rows can await
    /// the walk already in flight.
    /// <para>
    /// Without this seam a deep-path restore would call
    /// <see cref="RefreshAsync"/> itself; because that serialises on
    /// <c>_refreshLock</c>, the restore would queue a SECOND full filesystem walk
    /// behind the one <c>OnSelectedNodeChanged</c> already started, and that
    /// second walk would rebuild every row underneath the restore that was busy
    /// resolving one. Same rationale as <see cref="LastDescriptionFill"/>.
    /// </para>
    /// </summary>
    public Task? LastRefresh { get; private set; }

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
                _allRows = rows;

                // The view binds the COMPUTED Filtered* projections, so the
                // Clear()/Add() above does not reach the UI on its own.  Without
                // this the lists would silently stop updating on refresh.
                NotifyFilteredListsChanged();

                // Rows only — FillGrouped appends artifacts to `rows` and never the
                // section headers, so the previous "incl. headers" wording was
                // misleading when cross-reading this against [AgentsSkills.Realized].
                Log.Information(
                    "[AgentsSkills.Refresh] rows={Rows} (agents+skills+commands, headers excluded)", rows.Count);

                // Kick the lazy description fill — rows are already on screen.
                // Captured (not discarded) so tests can await completion
                // deterministically; in the app it runs fire-and-forget.
                LastDescriptionFill = FillDescriptionsThenNotifyAsync(rows, fillCt);
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
                _allRows = [];
                // Same reason as the success path: the view binds the computed
                // projections, so the clear has to be announced explicitly or
                // the UI keeps rendering the stale lists.
                NotifyFilteredListsChanged();
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
    /// The list-row subtitle for a front-matter description: the description
    /// itself, or a placeholder when the file declares none.
    /// <para>
    /// Shared by the background fill and by <see cref="SaveAsync"/> so a saved
    /// edit renders its row exactly the way the initial load would have — the
    /// two drifting apart is what made a saved description look stale.
    /// </para>
    /// </summary>
    private static string NormaliseSubtitle(string? description)
    {
        return string.IsNullOrWhiteSpace(description) ? NoDescriptionPlaceholder : description!;
    }

    // Pre-existing untranslated placeholder, kept verbatim so this change is
    // behaviour-preserving; localising it is a separate concern from the
    // stale-row fix (it would need a new resx key in all eight cultures).
    private const string NoDescriptionPlaceholder = "(no description)";

    /// <summary>
    /// Run the lazy description fill, then re-raise the filtered-list
    /// notifications.
    /// <para>
    /// The description is one of the filter's match fields but arrives
    /// asynchronously, so a filter typed while the fill is still running can
    /// only match on name and source. Re-raising once at the end lets those
    /// description matches appear without re-running the filter per row.
    /// </para>
    /// </summary>
    private async Task FillDescriptionsThenNotifyAsync(
        IReadOnlyList<ArtifactRowViewModel> rows, CancellationToken ct)
    {
        await FillDescriptionsAsync(rows, ct).ConfigureAwait(true);

        // A superseded fill must not disturb the newer refresh's lists.
        if (ct.IsCancellationRequested)
        {
            return;
        }

        NotifyFilteredListsChanged();
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
                row.Subtitle = NormaliseSubtitle(description);
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
        LastActionMessage = null;

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
        Log.Information("[AgentsSkills.Command] action=Back");
        SelectedArtifact = null;
        ViewerBody = null;
        _currentFrontMatter = null;
        IsEditing = false;
        IsRawMode = false;
        LastActionMessage = null;
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
    /// Raised when <see cref="CopyMarkdownCommand"/> runs; the view copies the
    /// payload to the clipboard (clipboard access needs <c>TopLevel</c>, a view
    /// concern). Mirrors <c>MemoryEditorViewModel.CopyMarkdownRequested</c>.
    /// </summary>
    public event EventHandler<string>? CopyMarkdownRequested;

    /// <summary>
    /// Copies the full artifact file (front-matter + body) to the clipboard, so a
    /// paste is a self-contained agent/skill/command definition — matching what
    /// "Open in editor" opens. Recomposes from the current front-matter (which
    /// already carries the body); falls back to the raw body when there is none.
    /// </summary>
    [RelayCommand]
    public void CopyMarkdown()
    {
        string content = _currentFrontMatter is { Present: true } fm
            ? YamlFrontMatter.Compose(fm)
            : ViewerBody ?? string.Empty;
        CopyMarkdownRequested?.Invoke(this, content);
        Log.Information("[AgentsSkills.Command] action=CopyMarkdown");
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
        LastActionMessage = null;
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
        LastActionMessage = null;
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
            LastActionMessage = string.Format(
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

        // Push the saved description back onto the LIST row.  PopulateCard only
        // refreshes the detail pane's Card* properties, so without this the row
        // in the segment list keeps rendering the pre-edit description until the
        // next full refresh.  Deliberately not a RefreshAsync() — that would
        // rebuild every row and drop the selection the user is looking at.
        row.Subtitle = NormaliseSubtitle(CardDescription);

        IsEditing = false;
        IsRawMode = false;
        RawValidationMessage = null;
        LastActionMessage = FirstSaveHint();

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

    // ── Deep-path capture / restore (IDeepNavigable) ─────────────────────

    /// <summary>
    /// The in-memory snapshot carried across an in-process reload. Holds the
    /// UNSAVED edit buffer, which is why it is never persisted — see
    /// <see cref="IDeepNavigable.CaptureTransientState"/>.
    /// </summary>
    /// <remarks>
    /// A record so it is trivially immutable; the restore reads it once.
    /// </remarks>
    internal sealed record ArtifactEditSnapshot(
        bool IsEditing,
        bool IsRawMode,
        string? EditName,
        string? EditDescription,
        string? EditModel,
        string? EditTools,
        string? EditBody,
        string? EditRawFrontMatter);

    /// <summary>Map an artifact category to the segment that lists it.</summary>
    internal static string SegmentIdForCategory(UserMemoryCategory category)
    {
        return category switch
        {
            UserMemoryCategory.Subagent => SegmentSubagentsId,
            UserMemoryCategory.Skill => SegmentSkillsId,
            UserMemoryCategory.SlashCommand => SegmentCommandsId,
            var _ => SegmentSubagentsId,
        };
    }

    /// <summary>
    /// The navigation <c>NodeId</c> this page is hosted under, assigned by
    /// <c>MainWindowViewModel</c> at construction (same shape as
    /// <c>BackupRestoreViewModel.InitialProjectRoot</c>).
    /// <para>
    /// Needed because <see cref="CaptureDeepPath"/> returns only the segments
    /// BELOW the node — the node prefix is the host's knowledge. Passing it in
    /// rather than hardcoding the id here keeps a single source of truth: a
    /// duplicated literal would be exactly the kind of parallel list that drifts.
    /// </para>
    /// <para>
    /// <see langword="null"/> in unit tests, which simply disables
    /// <see cref="CopyDeepLinkCommand"/>.
    /// </para>
    /// </summary>
    public string? DeepLinkNodeId
    {
        get => _deepLinkNodeId;
        set
        {
            _deepLinkNodeId = value;
            CopyDeepLinkCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _deepLinkNodeId;

    /// <summary>
    /// <see langword="true"/> when there is an open artifact AND a host node id,
    /// so a shareable deep link can actually be composed.
    /// </summary>
    public bool CanCopyDeepLink => SelectedArtifact is not null && !string.IsNullOrEmpty(DeepLinkNodeId);

    /// <summary>
    /// Put a <c>--deep-link</c> path for the open artifact on the clipboard.
    /// <para>
    /// This is the feature's discoverability answer: the path grammar is
    /// documented, but nobody should have to derive an id by hand — they copy it
    /// from the item they are already looking at. It doubles as the way to share a
    /// pointer to an artifact in a ticket or a runbook.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyDeepLink))]
    private void CopyDeepLink()
    {
        if (DeepLinkNodeId is not { } nodeId || SelectedArtifact is null)
        {
            return;
        }

        string path = NavDeepPath.Format([nodeId, .. CaptureDeepPath()]);

        // Reuses the view's existing clipboard bridge — clipboard access needs
        // TopLevel, which is a view concern.
        CopyMarkdownRequested?.Invoke(this, path);

        string confirmation = string.Format(
            CultureInfo.CurrentCulture, Strings.StatusDeepLinkCopiedFmt, path);

        // Announced in TWO places on purpose. The page-local line keeps the copied
        // path readable next to the button that produced it (it's the thing the user
        // wants to see and maybe re-read), while the shell's status pill is where
        // this app confirms every other completed action — an 11px grey line alone
        // is easy to miss, which is exactly what happened in review.
        LastActionMessage = confirmation;
        WeakReferenceMessenger.Default.Send(
            new ShowStatusMessage(confirmation, StatusSeverity.Success));

        Log.Information("[AgentsSkills.Command] action=CopyDeepLink path={Path}", path);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CaptureDeepPath()
    {
        // No open artifact: the visible segment alone is the position.
        if (SelectedArtifact is not { } row)
        {
            return [SegmentIdFor(SelectedSegmentIndex)];
        }

        // With an artifact open, derive the segment from the ARTIFACT'S category
        // rather than from SelectedSegmentIndex.  The two normally agree (you have
        // to be on a tab to click a row in it), but deriving from the item makes
        // the pair self-consistent by construction — a captured path can never
        // name a segment that doesn't contain the item it points at.
        string segment = SegmentIdForCategory(row.Entry.Category);

        // Fully-qualified (name@source) rather than a bare name so the restore
        // can't land on a same-named artifact from a different scope or plugin.
        return [segment, NavDeepPath.FormatItemKey(row.DisplayName, row.Source)];
    }

    /// <inheritdoc />
    public object? CaptureTransientState()
    {
        // Only worth carrying when there is an edit in progress; a plain viewing
        // position is already fully described by the deep path.
        if (!IsEditing)
        {
            return null;
        }

        return new ArtifactEditSnapshot(
            IsEditing: true,
            IsRawMode: IsRawMode,
            EditName: EditName,
            EditDescription: EditDescription,
            EditModel: EditModel,
            EditTools: EditTools,
            EditBody: EditBody,
            EditRawFrontMatter: EditRawFrontMatter);
    }

    /// <inheritdoc />
    public async Task<bool> TryRestoreDeepPathAsync(
        IReadOnlyList<string> segments,
        DeepRestoreMode mode,
        object? transientState,
        CancellationToken ct)
    {
        if (segments is null || segments.Count == 0)
        {
            return false;
        }

        // Segment first: even if the item can't be found, landing on the right
        // tab is strictly better than landing on the default one.
        SelectSegment(segments[0]);

        if (segments.Count < 2)
        {
            return true;
        }

        // The rows come from a filesystem walk that OnSelectedNodeChanged has
        // already kicked off.  Await THAT one rather than starting a second.
        if (LastRefresh is { } inFlight)
        {
            try
            {
                await inFlight.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // RefreshAsync guards itself and leaves the lists empty on
                // failure; nothing to restore, but never surface it as a crash.
                Log.Warning(ex, "[DeepLink] refresh faulted before restore; nothing to select");
                return false;
            }
        }

        if (ct.IsCancellationRequested)
        {
            return false;
        }

        ArtifactRowViewModel? target = ResolveArtifact(segments[0], segments[1]);
        if (target is null)
        {
            Log.Information(
                "[DeepLink] artifact not found segment={Segment} item={Item}", segments[0], segments[1]);
            return false;
        }

        await LoadArtifactAsync(target).ConfigureAwait(true);
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        // Reveal the row in the list behind the detail pane by filtering to it,
        // which is how the app already surfaces deep-linked property targets
        // (SettingsGroupEditorViewModel.ApplyNavigationFilter).  Goes through
        // ApplyNavigationFilter, NOT FilterText, so the "navigated" frame shows
        // and the user can see why the list is narrowed.
        ApplyNavigationFilter(target.DisplayName);

        if (mode == DeepRestoreMode.Full && transientState is ArtifactEditSnapshot snapshot)
        {
            RestoreEditSnapshot(snapshot);
        }

        return true;
    }

    /// <summary>
    /// Find the row an item key names within one segment.
    /// <para>
    /// Accepts <c>name@source</c> (exact, and what <see cref="CaptureDeepPath"/>
    /// emits) or a bare <c>name</c> (what a human types). A bare name that
    /// matches more than one row logs the ambiguity and takes the first, because
    /// landing somewhere reasonable beats refusing to navigate.
    /// </para>
    /// </summary>
    private ArtifactRowViewModel? ResolveArtifact(string segmentId, string itemKey)
    {
        IReadOnlyList<object> items = SegmentIndexFor(segmentId) switch
        {
            0 => AgentItems,
            1 => SkillItems,
            2 => CommandItems,
            var _ => Array.Empty<object>(),
        };

        List<ArtifactRowViewModel> rows = items.OfType<ArtifactRowViewModel>().ToList();
        (string name, string? source) = NavDeepPath.SplitItemKey(itemKey);

        if (source is not null)
        {
            ArtifactRowViewModel? exact = rows.FirstOrDefault(
                r => string.Equals(r.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            // The source may have gone away (plugin uninstalled) while the name
            // survives elsewhere — fall through to a name-only match rather than
            // giving up on an otherwise-valid target.
        }

        List<ArtifactRowViewModel> byName = rows
                                            .Where(r => string.Equals(
                                                r.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                                            .ToList();

        if (byName.Count > 1)
        {
            Log.Information(
                "[DeepLink] item key '{Item}' is ambiguous ({Count} matches); taking the first. "
                + "Qualify it as name@source to disambiguate.",
                itemKey, byName.Count);
        }

        return byName.FirstOrDefault();
    }

    /// <summary>
    /// Put an in-progress edit back after an in-process reload, so the user's
    /// unsaved text survives a Reload Window instead of being silently discarded.
    /// </summary>
    private void RestoreEditSnapshot(ArtifactEditSnapshot snapshot)
    {
        if (!snapshot.IsEditing || !CanEdit)
        {
            return;
        }

        // BeginEdit seeds the fields from the freshly-loaded card, then the
        // snapshot overwrites them with what the user had actually typed.
        BeginEdit();

        EditName = snapshot.EditName;
        EditDescription = snapshot.EditDescription;
        EditModel = snapshot.EditModel;
        EditTools = snapshot.EditTools;
        EditBody = snapshot.EditBody;

        if (snapshot.IsRawMode)
        {
            // Setting IsRawMode re-seeds the raw box from the typed fields via
            // OnIsRawModeChanged, so the captured raw text has to be re-applied
            // after the toggle, not before.
            IsRawMode = true;
            EditRawFrontMatter = snapshot.EditRawFrontMatter;
        }

        Log.Information("[DeepLink] restored in-progress edit raw={Raw}", snapshot.IsRawMode);
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