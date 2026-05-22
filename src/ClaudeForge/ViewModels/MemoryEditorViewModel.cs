using System.Collections.ObjectModel;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// "Memory" page — surfaces Tier 1 user-authored memory (audit + read-only
/// viewer) and Tier 2 behavioural footprint (audit + privacy / cleanup).
/// Renders both products side-by-side: Claude Code shows the typed
/// inventory; Claude Desktop shows an explainer panel because it has no
/// <c>CLAUDE.md</c>-equivalent surface.
/// </summary>
public sealed partial class MemoryEditorViewModel : ObservableObject
{
    private readonly IClaudeConfigClient? _codeClient;
    private readonly string? _projectRoot;
    private readonly IDialogService? _dialogService;
    private readonly IShellLauncher? _shellLauncher;

    /// <summary>
    /// Serialises concurrent <see cref="RefreshAsync"/> calls so that the
    /// rebuild Clear/Add sequence on each ObservableCollection is atomic
    /// across overlapping invocations. Without this, the ctor's
    /// fire-and-forget <c>Refresh()</c> can race with the GUI's bound
    /// Refresh button or a test's explicit <c>await RefreshAsync()</c> —
    /// both runs Clear then Add and the interleaving produces duplicate
    /// rows. Owned by this VM (1-of-1 capacity, no IDisposable contract
    /// around the SDK clients keeping things simple).
    /// </summary>
    private readonly SemaphoreSlim _refreshLock = new(initialCount: 1, maxCount: 1);

    public MemoryEditorViewModel(
        IClaudeConfigClient? codeClient,
        string? projectRoot,
        IDialogService? dialogService,
        IShellLauncher? shellLauncher)
    {
        _codeClient = codeClient;
        _projectRoot = projectRoot;
        _dialogService = dialogService;
        _shellLauncher = shellLauncher;
        Tier1Groups = [];
        FootprintRows = [];
        ProjectTranscripts = [];
        Refresh();
    }

    /// <summary>
    /// Convenience constructor used by tests / fixtures that don't need
    /// shell-launch or dialog plumbing.
    /// </summary>
    public MemoryEditorViewModel(IClaudeConfigClient? codeClient, string? projectRoot)
        : this(codeClient, projectRoot, dialogService: null, shellLauncher: null)
    {
    }

    /// <summary><see langword="true"/> when the Code SDK client is non-null AND the snapshot returned at least one entry.</summary>
    public bool HasCodeMemory => _codeClient is not null;

    /// <summary>One per <see cref="UserMemoryCategory"/> — empty groups are still rendered with a placeholder.</summary>
    public ObservableCollection<UserMemoryGroupViewModel> Tier1Groups { get; }

    /// <summary>One per <see cref="FootprintCategory"/> — always 7 rows, missing dirs render as zero-size.</summary>
    public ObservableCollection<FootprintRowViewModel> FootprintRows { get; }

    /// <summary>
    /// Per-project breakdown of the SessionTranscripts category — one row
    /// per <c>~/.claude/projects/&lt;mangled&gt;</c> subdirectory.  The View
    /// renders this list inside an Expander below the main FootprintRows
    /// table; expander visibility is gated on <see cref="HasProjectBreakdown"/>
    /// so the section disappears when there are no per-project transcripts
    /// to delete.
    /// </summary>
    public ObservableCollection<ProjectTranscriptRowViewModel> ProjectTranscripts { get; }

    /// <summary>
    /// True when the per-project breakdown has at least one project to
    /// surface.  Drives the visibility of the breakdown Expander on the
    /// Footprint tab — empty state is hidden so the user isn't confused
    /// by an empty section header.
    /// </summary>
    public bool HasProjectBreakdown => ProjectTranscripts.Count > 0;

    /// <summary>
    /// Threshold (in bytes) at which the Footprint tab surfaces a warning
    /// banner suggesting the user clean up old transcripts.  Set to 5 GiB
    /// — large enough that most users never hit it, but tight enough that
    /// a developer with weeks of unattended sessions sees the prompt
    /// before disk pressure becomes a problem.  Configurable via the
    /// constant; a future version may surface this as a user setting.
    /// </summary>
    public const long FootprintWarningThresholdBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// Aggregate size of every <see cref="FootprintCategory"/> row, summed
    /// across the table.  Recomputed by <see cref="RebuildFootprintRows"/>
    /// alongside the row collection.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFootprintLarge))]
    [NotifyPropertyChangedFor(nameof(FootprintTotalDisplay))]
    [NotifyPropertyChangedFor(nameof(FootprintWarningMessage))]
    private long _footprintTotalBytes;

    /// <summary>
    /// <see langword="true"/> when the aggregate footprint exceeds
    /// <see cref="FootprintWarningThresholdBytes"/>.  Bound to the
    /// warning-banner visibility on the Footprint tab.
    /// </summary>
    public bool IsFootprintLarge => FootprintTotalBytes > FootprintWarningThresholdBytes;

    /// <summary>Humanised aggregate footprint (e.g. "12.4 GB") — used in the warning banner copy.</summary>
    public string FootprintTotalDisplay => FormatBytes(FootprintTotalBytes);

    /// <summary>
    /// Warning-banner body string — interpolates <see cref="FootprintTotalDisplay"/>
    /// into the localised template.  The format string lives in
    /// <c>Strings.MsgFootprintLargeWarningFmt</c> so a translation pass
    /// can localise the surrounding prose.
    /// </summary>
    public string FootprintWarningMessage =>
        string.Format(Strings.MsgFootprintLargeWarningFmt, FootprintTotalDisplay);

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024d;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        return bytes switch
        {
            >= (long)GB => $"{bytes / GB:0.0} GB",
            >= (long)MB => $"{bytes / MB:0.0} MB",
            >= (long)KB => $"{bytes / KB:0.0} KB",
            var _ => $"{bytes} B",
        };
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsViewerVisible))]
    private UserMemoryFile? _selectedFile;

    [ObservableProperty] private string? _viewerContent;

    /// <summary><see langword="true"/> when a Tier 1 file is open in the viewer pane.</summary>
    public bool IsViewerVisible => SelectedFile is not null;

    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Bound to the per-project breakdown toggle on the Footprint tab.
    /// True when the user has expanded the section to see one row per
    /// <c>~/.claude/projects/&lt;mangled&gt;/</c> directory.  Persisted only
    /// for the lifetime of the page — collapsed by default on every
    /// navigate-in so the user starts at the high-level summary.
    /// </summary>
    [ObservableProperty] private bool _isProjectBreakdownExpanded;

    /// <summary>Toggles <see cref="IsProjectBreakdownExpanded"/> from a button click.</summary>
    [RelayCommand]
    private void ToggleProjectBreakdown()
    {
        IsProjectBreakdownExpanded = !IsProjectBreakdownExpanded;
    }

    /// <summary>
    /// Reload Tier 1 inventory + Tier 2 stats. Runs the footprint walk on a
    /// worker thread so a slow disk doesn't freeze the UI.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_codeClient is null)
        {
            return;
        }

        // Serialise concurrent calls so Clear+Add on each ObservableCollection
        // stays atomic across overlapping invocations. See the field comment.
        await _refreshLock.WaitAsync().ConfigureAwait(true);
        try
        {
            IsBusy = true;
            try
            {
                // Tier 1 — labelled "synchronous, fast" but in practice walks
                // ~/.claude/{agents,commands,hooks,plans,rules,skills,...}
                // and reads up to 4 KiB from every file to extract the
                // subtitle.  On a workstation with hundreds of memory files
                // (rules/ in particular is walked RECURSIVELY) this is
                // measured in hundreds of milliseconds.  Run it on the
                // thread pool so it doesn't freeze the dispatcher — without
                // this, every workspace reload (which constructs a fresh
                // MemoryEditorViewModel and kicks off Refresh() in its ctor)
                // blocked the UI thread for the duration of the scan.
                // Observed 2026-05-13 as the hang amplifier behind the
                // profile-switch reload-loop bug.
                IReadOnlyList<UserMemoryFile> tier1 = await Task.Run(() => _codeClient.SnapshotUserMemoryFiles(_projectRoot)
                ).ConfigureAwait(true);
                RebuildTier1Groups(tier1);

                // Tier 2 — async (disk walk).
                IReadOnlyList<FootprintCategoryStats> tier2 = await _codeClient.GetFootprintStatsAsync(CancellationToken.None).ConfigureAwait(true);
                RebuildFootprintRows(tier2);

                // Tier 2 per-project breakdown — additional async walk; runs
                // alongside the aggregate stats. Empty when ~/.claude/projects
                // is empty or absent.
                IReadOnlyList<ProjectTranscriptStats> perProject = await _codeClient.GetProjectTranscriptStatsAsync(CancellationToken.None).ConfigureAwait(true);
                RebuildProjectTranscripts(perProject);
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

    /// <summary>Synchronous shortcut for the constructor — kicks off the async refresh fire-and-forget.</summary>
    public void Refresh()
    {
        _ = RefreshAsync();
    }

    /// <summary>Load a Tier 1 file into the viewer pane.</summary>
    [RelayCommand]
    public async Task LoadFileAsync(UserMemoryFile? file)
    {
        if (file is null || _codeClient is null)
        {
            return;
        }

        SelectedFile = file;
        ViewerContent = await _codeClient.ReadMemoryFileAsync(file.AbsolutePath, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Close the viewer (return to the inventory list).</summary>
    [RelayCommand]
    public void CloseViewer()
    {
        SelectedFile = null;
        ViewerContent = null;
    }

    /// <summary>
    /// Raised when the user clicks the "Copy markdown" button on the viewer
    /// toolbar.  The View subscribes and pushes <see cref="ViewerContent"/>
    /// onto the platform clipboard via
    /// <c>TopLevel.GetTopLevel(this).Clipboard.SetTextAsync</c>.  Surfacing
    /// this as an event keeps the VM free of any direct dependency on the
    /// Avalonia visual tree (mirrors the EffectiveSettingsView pattern).
    /// </summary>
    public event EventHandler<string>? CopyMarkdownRequested;

    /// <summary>
    /// Copy the raw markdown source of the currently-open Tier 1 file to
    /// the clipboard.  Useful for pasting back into a chat to iterate on
    /// what the in-app renderer is doing well or poorly.
    /// </summary>
    [RelayCommand]
    public void CopyMarkdown()
    {
        if (string.IsNullOrEmpty(ViewerContent))
        {
            return;
        }

        CopyMarkdownRequested?.Invoke(this, ViewerContent);
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
    }

    /// <summary>
    /// Open the supplied path in the platform default editor (registered file
    /// handler on Windows; <c>open -t</c> on macOS; <c>xdg-open</c> on Linux).
    /// Surfaced on every Memory page row that exposes a path so the user
    /// can edit Tier 1 markdown in their preferred external editor without
    /// the GUI re-implementing markdown editing, and inspect Tier 2 footprint
    /// files (<c>history.jsonl</c>, <c>cost-tracker.log</c>, etc.) in their
    /// editor of choice.  When the path resolves to a directory the OS
    /// typically dispatches it to the file manager — equivalent to
    /// <see cref="Reveal"/> but without the parent-folder + select indirection.
    /// </summary>
    [RelayCommand]
    public void OpenInEditor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _shellLauncher?.OpenInDefaultEditor(path);
    }

    /// <summary>
    /// Delete an entire footprint category. Surfaces a Destructive-category
    /// confirm dialog citing the count + size; on confirm, calls the SDK
    /// and refreshes the row.
    /// </summary>
    [RelayCommand]
    public async Task DeleteFootprintAsync(FootprintRowViewModel? row)
    {
        if (row is null || _codeClient is null)
        {
            return;
        }

        Log.Information(
            "[Memory.Command] action=DeleteFootprint category={Category} fileCount={FileCount} bytes={Bytes}",
            row.Category, row.FileCount, row.TotalBytes);

        if (_dialogService is not null)
        {
            DialogMessage msg = DialogMessage.Builder()
                                             .Text(string.Format(
                                                 Strings.MsgDeleteFootprintConfirmFmt,
                                                 row.FileCount,
                                                 row.HumanSize))
                                             .Text("\n\n")
                                             .Path(row.AbsolutePath)
                                             .Text("\n\nThis cannot be undone.")
                                             .Build();
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                string.Format(Strings.TitleDeleteFootprintFmt, row.HumanLabel),
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
            await _codeClient.DeleteFootprintCategoryAsync(row.Category, CancellationToken.None).ConfigureAwait(true);
            // Refresh the affected row only — full GetStatsAsync is fine but
            // visibly slower for the user. We re-stat once and then update
            // every row to capture neighbour changes (the schema doesn't
            // require per-row delete to leave others untouched, but in
            // practice it does).
            IReadOnlyList<FootprintCategoryStats> stats = await _codeClient.GetFootprintStatsAsync(CancellationToken.None).ConfigureAwait(true);
            RebuildFootprintRows(stats);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Delete every <c>*.jsonl</c> transcript under one project's
    /// <c>~/.claude/projects/&lt;mangled&gt;/</c> directory.  Same Destructive-
    /// dialog flow as the category-level delete; on confirm the row's
    /// stats refresh to zero.  Use case: surgically wipe one project's
    /// chat history without touching the rest.
    /// </summary>
    [RelayCommand]
    public async Task DeleteProjectTranscriptsAsync(ProjectTranscriptRowViewModel? row)
    {
        if (row is null || _codeClient is null)
        {
            return;
        }

        Log.Information(
            "[Memory.Command] action=DeleteProjectTranscripts project=\"{Mangled}\" fileCount={FileCount} bytes={Bytes}",
            row.MangledName, row.FileCount, row.TotalBytes);

        if (_dialogService is not null)
        {
            DialogMessage msg = DialogMessage.Builder()
                                             .Text(string.Format(
                                                 Strings.MsgDeleteProjectTranscriptsConfirmFmt,
                                                 row.FileCount,
                                                 row.HumanSize))
                                             .Text("\n\n")
                                             .Path(row.AbsolutePath)
                                             .Text("\n\nThis cannot be undone.")
                                             .Build();
            bool? confirmed = await _dialogService.ShowConfirmAsync(
                string.Format(Strings.TitleDeleteProjectTranscriptsFmt, row.DisplayName),
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
            await _codeClient.DeleteProjectTranscriptsAsync(row.MangledName, CancellationToken.None).ConfigureAwait(true);

            // Re-stat both surfaces — the per-project row goes to 0/0 AND
            // the aggregate SessionTranscripts row shrinks accordingly, so
            // both lists should refresh together.
            IReadOnlyList<FootprintCategoryStats> stats = await _codeClient.GetFootprintStatsAsync(CancellationToken.None).ConfigureAwait(true);
            RebuildFootprintRows(stats);
            IReadOnlyList<ProjectTranscriptStats> perProject = await _codeClient.GetProjectTranscriptStatsAsync(CancellationToken.None).ConfigureAwait(true);
            RebuildProjectTranscripts(perProject);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RebuildTier1Groups(IReadOnlyList<UserMemoryFile> files)
    {
        Tier1Groups.Clear();
        // One group per category; empty categories are still rendered so the
        // user sees what was checked.
        foreach (UserMemoryCategory category in Enum.GetValues<UserMemoryCategory>())
        {
            List<UserMemoryFile> rows = files
                                        .Where(f => f.Category == category)
                                        .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                                        .ToList();
            Tier1Groups.Add(new UserMemoryGroupViewModel(category, rows));
        }
    }

    private void RebuildFootprintRows(IReadOnlyList<FootprintCategoryStats> stats)
    {
        FootprintRows.Clear();
        long total = 0;
        foreach (FootprintCategoryStats s in stats)
        {
            FootprintRows.Add(new FootprintRowViewModel(s));
            total += s.TotalBytes;
        }

        // Triggers IsFootprintLarge / FootprintTotalDisplay /
        // FootprintWarningMessage notifications via the source-generator
        // setter on the ObservableProperty.
        FootprintTotalBytes = total;
    }

    private void RebuildProjectTranscripts(IReadOnlyList<ProjectTranscriptStats> stats)
    {
        ProjectTranscripts.Clear();
        // Sort most-recently-active project first so the user's current
        // work surfaces at the top of the breakdown — matches typical
        // mental model ("what was I just doing?") and makes the empty
        // husks (LastWriteUtc == MinValue) sink to the bottom.
        foreach (ProjectTranscriptStats s in stats.OrderByDescending(p => p.LastWriteUtc))
        {
            ProjectTranscripts.Add(new ProjectTranscriptRowViewModel(s));
        }

        OnPropertyChanged(nameof(HasProjectBreakdown));
    }
}