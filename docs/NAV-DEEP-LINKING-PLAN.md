# Plan — list filtering, deep-path restore, and `--deep-link`

> Status: **SHIPPED — historical record.** This is not a work item; it documents a
> feature that is in `main` and in the product. The working branch
> `feat/nav-deep-linking-and-list-search` was merged and pruned; `--deep-link` lives in
> `ClaudeForge/Services/DebugFlags.cs` and the search/filter surface in
> `SearchViewModel` + `MainWindowViewModel`. Verify by those members, not by ancestry —
> the repo squash-merges, which defeats ancestry, patch-id, and subject checks.
> Gates **as recorded at the time of shipping**: build 0 warnings · 2702 passed /
> 11 skipped / 0 failed · trim publish 0 IL warnings. The suite has grown since; do not
> read that count as current.
> Fact-shaped per [`AGENTS.md`](../AGENTS.md): claims cite a file, type, or member,
> never a line number.
>
> The approved plan lives at `~/.claude/plans/modular-moseying-micali.md`. This copy
> records what SHIPPED, including the six corrections below that only surfaced once
> the code existed. Manual test plan: [`NAV-DEEP-LINKING-TEST-PLAN.md`](./NAV-DEEP-LINKING-TEST-PLAN.md).
>
> **Still unverified by hand:** the copy-deep-link pill (J2) and virtualization (G1).

---

## ✔ RESOLVED — status bar showed nothing (two separate causes)

The maintainer reported **no status bar change at all** for both the copy confirmation
and the unresolvable-deep-link warning. These turned out to be two unrelated things, one
a real defect and one a stale build.

### 1. The warning — a real defect, now fixed

`announceFailure` only reached the **synchronous** part of `TryQueueDeepRestore`, which
fails when the *page id* is unknown. But the overwhelmingly common real-world failure is a
**real page and a real tab with a stale item** — a shared link to a skill since renamed or
deleted. That resolves at the node level, so `TryQueueDeepRestore` returns `true` and never
warns; the miss is only knowable later, inside the fire-and-forget `RestoreDeepPathAsync`,
whose `false` return was logged and dropped.

The maintainer's log says it outright:

```
[DeepLink] path=agents-skills/skills/definitely-not-a-skill mode="Full" resolved=true node=agents-skills below=2
[DeepLink] artifact not found segment=skills item=definitely-not-a-skill
[DeepLink] restore applied=false mode="Full" below=skills/definitely-not-a-skill
```

`resolved=true` then `applied=false` — the shallow check passed, so nothing ever warned.

**Why the test stayed green:** `DeepLinkArgument_UnresolvablePath_LeavesAVisibleStatusWarning`
uses `no-such-page/no-such-tab`, which fails at the node level — the *one* shape that did
work. The test covered the shallow failure; the user hit the deep one.

**Fix:** `PendingDeepRestore` now carries `AnnouncePath`, so the intent survives into the
async restore, and every failure site routes through one `RaiseDeepLinkWarning`. Because
the restore can finish either side of `SetStatusState(StatusReady)`, that method queues
into `_pendingDeepLinkWarning` before `_startupStatusSettled` and sets the bar directly
after — removing the ordering question rather than betting on one order. It also declines
to bury a `Failure`, the one kind that never auto-clears.

Locked by `DeepLinkArgument_RealPageButUnknownItem_LeavesAVisibleStatusWarning`.
**Both ordering branches are canaried and load-bearing:** dropping the deferred branch
fails the older node-level test and leaves the new one passing, which is the proof they
exercise different paths.

### 2. The copy pill — never actually broken, just never run

The only `CopyDeepLink` in any log is `app-20260806-16.txt:179` at **12:24:24**, on
`v2026.3.806.1220` (a **12:20** build). `ShowStatusMessage.cs` — the whole shell-pill
route — was created at **12:27:14**, three minutes later. The 12:44–12:45 session did run
a build that had it (`v2026.3.806.1245`) but contains no `CopyDeepLink` at all.

So the copy pill has never executed on a build containing it. Not broken — **unverified**.
Retest: open any skill → **Copy deep link** → expect a green ✓ pill for ~6 s *and* the
page-local line under the detail toolbar.

> Reusable trick: the `Starting ClaudeForge v2026.3.<MMDD>.<HHmm>` line encodes the build
> time, so any log can be dated against a source file's mtime to tell "broken" from "stale
> binary" without asking.

---

## Corrections found during implementation

Each of these contradicts something the approved plan asserted. Recorded so the next
reader trusts the code over the plan.

1. **The item key can NOT be an absolute path.** The plan had persistence writing
   `AbsolutePath` for precision. Paths contain `/` — the segment separator — so a
   persisted deep path would have been unparseable. Item keys are now always
   `name` or `name@source` (`NavDeepPath.FormatItemKey`, split on the LAST `@`),
   which is also portable and shareable. Locked by
   `AgentsSkillsDeepPathTests.CaptureDeepPath_NeverContainsAPathSeparator`.

2. **`NodeId` uniqueness is per-PARENT, not tree-wide.** The plan called for a
   global uniqueness guard; that would fail on the real tree, because
   `version-info` legitimately exists under both product headers and a settings-group
   name may repeat across products. The grammar is `<parent-id>/<child-id>`, so
   sibling-scoped uniqueness is exactly what it needs. Dividers (two share one
   placeholder title) carry no id at all.

3. **Five localized keys, not six.** The filter `TextBox` reuses
   `WatermarkFilterArtifacts` for its `AutomationProperties.Name`, exactly as
   `GroupPropertiesView` reuses `WatermarkFilterProps` — so `AutoNameArtifactFilter`
   was unnecessary. 40 translations rather than 48.

4. **`CaptureDeepPath` derives the segment from the ARTIFACT, not the visible tab.**
   The plan implied `SelectedSegmentIndex`. Deriving from
   `SelectedArtifact.Entry.Category` makes the captured pair self-consistent by
   construction — a path can never name a segment that doesn't contain the item it
   points at. Two tests failed on the original approach before this was fixed.

5. **A two-argument format string can't be an AXAML `StringFormat`.** The count
   beside the filter box was first bound as
   `{Binding VisibleRowCount, StringFormat=…"{0} of {1}"}`, which fills `{0}` and
   leaves a literal `{1}` on screen. Now formatted in the view-model as
   `FilterSummary`.

6. **A deep link fails at two different depths, and only the shallow one was wired.**
   The plan treated "unresolvable path" as a single event handled where the path is
   parsed. In practice the node resolves and the *item* doesn't, which is a different
   place, a different moment, and — because the restore is fire-and-forget — a
   different thread of control. Announcement intent has to be carried into the async
   restore (`PendingDeepRestore.AnnouncePath`), not consumed at parse time. See the
   resolved-defect section above.

Also noted, not acted on: **`CLAUDE.md` does not exist in this repo** even though
`AGENTS.md` links to it in several places (pointer index, gotcha references). The
`--deep-link` documentation therefore went to `README.md`, which is where the
debug-flags table actually lives. The dangling links are pre-existing.

---

## 1. Scope

Four asks, in dependency order:

| # | Ask | Workstream |
|---|-----|------------|
| 1 | Filter/search on Agents & Skills; assess grouping + virtualization | **W1** |
| 2 | Edited item updates its list row; Reload Window keeps the deep position | **W2** |
| 3 | `--deep-link <path>` CLI argument | **W3** |
| 4 | Don't make future multi-select + export harder | **W4** |

### Decisions taken

| Decision | Choice |
|---|---|
| Deep-link path identity | **Stable `NodeId` + segment ids**, culture-invariant. Reuses the `GroupTab.Id` precedent and unblocks the nav-tree i18n the code already defers ("full i18n would require a separate NodeId" — `MainWindowViewModel` NavTitle block). |
| Restore fidelity | **Reload Window restores fully** (including the editing experience); **cold launch restores page → tab → item only**, not edit mode. |
| Multi-select | **Groundwork only.** No user-visible selection UI this branch. |
| Reuse breadth | **Define the abstraction, adopt on Agents & Skills only.** Other pages adopt incrementally. |

---

## 2. What the code actually does today

Findings that shape the design. Each is verifiable by grep.

### 2.1 The page

`AgentsSkillsEditorViewModel` holds three flat `ObservableCollection<object>` lists
(`AgentItems` / `SkillItems` / `CommandItems`), each a mixed sequence of
`ArtifactSectionHeaderViewModel` ("Yours", "Plugin") and `ArtifactRowViewModel`.
`FillGrouped` rebuilds them; a header is emitted only when its group is non-empty.
Detail state is `SelectedArtifact`, `IsEditing`, `IsRawMode`; the active segment is
`SelectedSegmentIndex` — a bare `int` (0/1/2) with **no stable id**.

**There is no filter or search on this page at all.**

### 2.2 Confirmed bug — a saved edit leaves the list row stale

`SaveAsync` writes the file, re-reads it, and calls `PopulateCard(row, saved)` — which
sets the `Card*` properties consumed by the detail pane. It **never updates
`row.Subtitle`**, and `Subtitle` is what the list renders under the name. So editing a
`description` and saving leaves the old description visible in the list until the next
full `RefreshAsync`.

`DisplayName` is path-derived (`EditableMemoryEntry.DisplayName` — file name for
agents/commands, parent directory for skills), so editing the front-matter `name` key
correctly does *not* change the row label. That is not a bug; don't "fix" it.

### 2.3 Reload Window is an in-process rebuild, not a restart

`ReloadAsync` → `ReloadCoreAsync` → `LoadAllWorkspacesAsync` → `BuildNavigationTree` →
`RestoreSelectedNode`. The nav tree is torn down and rebuilt with **fresh editor VMs**;
`AgentsSkillsEditorViewModel` is constructed inline in `BuildNavigationTree` and is not
one of the cached persistent tool VMs (`_backupVm` / `_profilesVm` / `_essentialsVm`,
spared by `DisposeEditorNode`). `RestoreSelectedNode` matches on **node `Title` only**,
so everything below the node — segment, selected artifact, edit mode, scroll offset — is
lost.

Because the reload is in-process, transient state can be carried across it **in memory**.
That matters in §4.3.

### 2.4 In-progress artifact edits are silently discarded on reload

The Agents & Skills editor writes files directly via `MemoryFileWriter.WriteAsync`; its
edit buffer (`EditName` / `EditDescription` / `EditBody` / `EditRawFrontMatter`) is not
part of the config workspace, so it does not contribute to `HasUnsavedChanges`. Reload
Window therefore throws away typed-but-unsaved front-matter today with no warning.

This is pre-existing, but the feature forces a decision: naively "restoring edit mode"
would re-enter the editor seeded from **disk**, which looks like the user's unsaved text
came back when it did not. §4.3 resolves this without a dialog.

### 2.5 Existing deep-link machinery to reuse, not reinvent

| Mechanism | Where |
|---|---|
| Deep-link navigation + Back affordance | `MainWindowViewModel._isDeepLinkNavigation`, `_backNode`, `CanGoBack` |
| Deep-link entry point | `MainWindowViewModel.SelectSearchResult` |
| Stable, culture-invariant tab ids + `SelectTab(tabId)` | `GroupTab.PropertiesId` / `EffectiveId` / `JsonId`; `SettingsGroupEditorViewModel.SelectTab` |
| Per-page property filter | `EffectiveSettingsViewModel.FilterText` + computed `FilteredRows`; `SettingsGroupEditorViewModel.FilteredEditors` + `ClearFilterCommand`; `EnvironmentEditorViewModel.FilterText` |
| **Reveal-by-filter for jump links** | `SettingsGroupEditorViewModel.ApplyNavigationFilter(filter)` — sets `FilterText` behind an `_applyingNavFilter` latch and raises `FilterFromNavigation`, which draws the orange "navigated" frame until the user edits or clears. Its docs state deep-link handlers **must** use it rather than assigning `FilterText` directly, which would read as a user edit and skip the frame. |
| **Post-navigation re-apply** | `MainWindowViewModel.RequestExpandPermissionsAdvanced` — sets the value synchronously *and again* at `DispatcherPriority.Loaded`, because selecting a node triggers an editor/tab rebuild that can land after the synchronous set |
| VM → view "do a view thing" signal | `MainWindowViewModel.SearchFocusRequestId` (bumped int) |
| Two-token CLI flag | `DebugFlags` `case "--culture":` with `args[++i]` + `_deferredWarnings` |

The `DispatcherPriority.Loaded` re-apply is **load-bearing** for this feature: tab
selection and scroll-into-view both run against controls that the navigation rebuild
re-materializes.

### 2.6 Virtualization — probably already fine; verify, don't rewrite

`AgentsSkillsEditorView.axaml` uses `ScrollViewer > ItemsControl > VirtualizingStackPanel`
— the **same shape** as `GroupPropertiesView.axaml`, which `AGENTS.md` documents as
realizing only 7 of 94 top-level editors. So the earlier worry that an outer
`ScrollViewer` defeats virtualization does not apply to this shape.

Action is a measurement, not a rewrite: add an `[AgentsSkills.Realized] tab=… items=N`
trace mirroring the `[PropView.Realized]` probe in `GroupPropertiesView.axaml.cs`. A
healthy page realizes a screenful.

**Plain `ItemsControl` has no `ScrollIntoView`.** Assembly metadata for
`Avalonia.Controls` 12.1.0 contains exactly one `ScrollIntoView` (on
`SelectingItemsControl`, the `ListBox` base) plus `ContainerFromIndex` /
`ContainerFromItem` / `IndexFromContainer`; the only in-repo usage is
`LiveLogWindow._logList.ScrollIntoView(...)`, on a `ListBox`. That gap is why §4.5
reveals a deep-linked item **by prepopulating the filter** instead of scrolling — which
is the mechanism the app already uses for property jump links, needs no control
conversion, and is the reason the `ListBox` question is deferred to whenever
multi-select UI ships.

### 2.7 Grouping

Today: exactly two sections, "Yours" and "Plugin", built by `FillGrouped`.
With a filter in place, grouping stops being the primary way to find things.
**Recommendation: no new grouping this branch.** Revisit only if the filter proves
insufficient in use — adding a group-by axis (scope / plugin) interacts with filtering
and multi-select and is cheaper to design once those exist.

---

## 3. W1 — Filter on Agents & Skills

Follows the established `FilterText` + computed `Filtered*` pattern
(`EffectiveSettingsViewModel.FilteredRows` is the closest match).

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(FilteredAgentItems))]
[NotifyPropertyChangedFor(nameof(FilteredSkillItems))]
[NotifyPropertyChangedFor(nameof(FilteredCommandItems))]
[NotifyPropertyChangedFor(nameof(HasActiveFilter))]
[NotifyPropertyChangedFor(nameof(FilterSummary))]
private string _filterText = string.Empty;
```

**Match fields:** `DisplayName`, `Subtitle` (the front-matter description), `Source`.
Case-insensitive `Contains`, matching `FilteredRows`.

**Section headers must not survive an empty group.** A plain `Where` over the flat mixed
list would leave orphan "Yours" / "Plugin" headers above nothing. Filtering needs a
single-pass rebuild that buffers the current header and emits it lazily before the first
surviving row beneath it:

```csharp
private static IEnumerable<object> ApplyFilter(IEnumerable<object> flat, string filter)
```

Pure and static ⇒ unit-testable with no VM.

**Known nuance — lazy subtitles.** `Subtitle` is populated asynchronously by
`FillDescriptionsAsync` after the rows are already on screen. A filter typed before the
fill finishes cannot match on description. Mitigation: re-raise the three
`Filtered*Items` notifications once when the fill completes (the task is already captured
as `LastDescriptionFill`). Mid-fill partial matching is accepted; the fill is a bounded
8 KiB head-read per file.

**The filter text is never persisted, but restore *sets* it.** A user-typed filter is
cleared on navigate-away, following the convention `OnSelectedNodeChanged` already
applies to `EnvironmentEditorViewModel.FilterText` ("so the next visit starts with the
full list"). Deep-path restore then applies its own navigation filter to reveal the
target item — see §4.5. So the filter is an *output* of restore, not an input to it.

**View:** filter `TextBox` + `ClearFilterCommand` button in the header pill, copying the
`GroupPropertiesView.axaml` filter-row markup including the `Border.filter-frame` /
`.filter-frame.nav-filter` navigated-frame styling. Both controls need
`AutomationProperties.Name` (enforced by `AxamlAccessibilityCoverageTests`).

### New localized strings

Per the `AGENTS.md` "Adding a new localized string" checklist each key costs: `Strings.resx`
+ a **real** translation in all 8 culture resx files + a manual `Strings.Designer.cs`
entry + a literal `Strings.<Key>` reference (the dead-string guard fails the build
otherwise, and its tripwire forbids reflective lookup).

| Key | Why not reuse |
|---|---|
| `WatermarkFilterArtifacts` | `WatermarkFilterProps` says "properties" |
| `LabelClearArtifactFilter` | `LabelClearPropertyFilter` says "property filter" |
| `TipClearArtifactFilter` | ditto |
| `AutoNameClearArtifactFilter` | ditto |
| `AutoNameArtifactFilter` | no generic equivalent |
| `LabelArtifactFilterCountFmt` | new ("{0} of {1}") |

**6 keys × 8 cultures = 48 translations.** If that cost isn't worth it, the fallback is
reusing the four `*PropertyFilter*` keys and accepting slightly-off wording — say so and
I'll drop to 2 new keys.

---

## 4. W2 — Reusable deep-path capture/restore

### 4.1 Stable identity

Add to `NavigationNodeViewModel` (in `LayeredEditors.ViewModels`), as `init`-only
alongside `Editor` / `IsDivider` / `IsTopLevel`:

```csharp
/// <summary>Stable, culture-invariant id for deep links and persisted UI state.</summary>
public string? NodeId { get; init; }
```

Nullable so the many existing test constructions compile unchanged — the same rationale
the type already documents for `IsTopLevel` not being a ctor parameter.

Ids as consts in `MainWindowViewModel` beside the `NavTitle*` block: `welcome`,
`essentials`, `claude-code`, `claude-desktop`, `effective-settings`, `profiles`,
`backup-restore`, `environment`, `memory`, `agents-skills`, `version-info`.
Settings-group children get a deterministic slug of `GroupName` in
`NavigationTreeBuilder` ("MCP Servers" → `mcp-servers`): lowercase, non-alphanumeric → `-`,
collapse runs, trim. A guard test asserts every node in a built tree has a `NodeId` and
that all ids are unique, so a future group name can't silently collide.

Segment ids on `AgentsSkillsEditorViewModel`, mirroring `GroupTab`:

```csharp
public const string SegmentSubagentsId = "subagents";
public const string SegmentSkillsId    = "skills";
public const string SegmentCommandsId  = "commands";
public void SelectSegment(string segmentId);   // mirrors SettingsGroupEditorViewModel.SelectTab
```

`SelectedSegmentIndex` stays as the `TabControl` binding — no view churn. The id is the
external contract; the index remains an implementation detail.

### 4.2 Path grammar

```
<path> := <node-path> [ "/" <tab-id> [ "/" <item-key> ] ]
<node-path> := <top-level-id> [ "/" <child-id> ]
```

Resolved strictly left-to-right, which removes the `claude-code/permissions`
node-vs-tab ambiguity: segment 1 must match a top-level `NodeId`; if that node has
children and segment 2 matches a child `NodeId`, segment 2 is consumed as the node; the
next segment is the tab/segment id; the last is the item key.

```
agents-skills
agents-skills/skills
agents-skills/skills/pdf
agents-skills/skills/pdf@user          # disambiguated by ArtifactRowViewModel.Source
claude-code/permissions/properties
essentials
```

**Item key resolution** (`AgentsSkillsEditorViewModel`): accept, in order — an exact
`AbsolutePath`; `name@source` (case-insensitive on both); bare `name`
(case-insensitive, first match, warn when ambiguous). Persistence writes the absolute
path for precision; a hand-typed `--deep-link` uses the portable `name` / `name@source`
form.

Parsing/formatting lives in one pure static type, `NavDeepPath`, so it is testable
without a dispatcher and shared by the CLI flag, the persisted state, and any future
`claude://` protocol handler.

### 4.3 The abstraction

```csharp
public enum DeepRestoreMode
{
    /// Select the page, tab, and item. Do not enter any editing experience.
    Locate,
    /// Restore the full in-page experience, including edit mode.
    Full,
}

/// Implemented by an editor VM that owns navigable state below its nav node.
public interface IDeepNavigable
{
    /// Culture-invariant segments describing the current in-page position.
    /// Empty ⇒ nothing to restore. Safe to persist.
    IReadOnlyList<string> CaptureDeepPath();

    /// Opaque snapshot of transient state (e.g. an unsaved edit buffer).
    /// NEVER persisted — carried only across an in-process Reload Window.
    object? CaptureTransientState() => null;

    Task<bool> TryRestoreDeepPathAsync(
        IReadOnlyList<string> segments,
        DeepRestoreMode mode,
        object? transientState,
        CancellationToken ct);
}
```

`CaptureTransientState` is a default interface member, so a page that has no transient
state adopts the interface with two members, not three.

**Why the split, and why it removes the need for a warning dialog.** Reload Window is
in-process (§2.3), so the unsaved edit buffer can ride across the rebuild in memory as
`transientState` — the editing experience comes back *with the user's actual text*, not
a disk-seeded imitation. Nothing unsaved is persisted to `ClaudeForge-gui-state.json`,
and no new confirm dialog (and no 8 more translations) is required. This resolves §2.4
by making the discard not happen, rather than by warning about it.

Cold launch and `--deep-link` carry only the string path, hence `Locate` — which is
exactly the agreed cold-launch behaviour.

**Async because the restore has to wait for I/O:** selecting an artifact requires
`RefreshAsync`'s disk walk to have produced the rows, then `LoadArtifactAsync`'s file
read. `OnSelectedNodeChanged` is a sync partial, so it kicks the restore
fire-and-forget — consistent with the existing `agentsVm.Refresh()` call there.

**Home:** `src/ClaudeForge/ViewModels/IDeepNavigable.cs` (app-local). Only app editors
implement it. Promoting it to `LayeredEditors.Abstractions` is a follow-up if an
out-of-tree consumer ever needs it; putting it there now would be speculative layering.

### 4.4 MWVM wiring

- **Capture** — fold `CaptureDeepPath()` into `SaveWindowState()`, the single UI-state
  write site, storing the joined path in a new `WindowState.LastDeepPath`.
- **Reload** — `ReloadCoreAsync` captures path + `CaptureTransientState()` **before**
  `LoadAllWorkspacesAsync`, stashes them as a pending restore with mode `Full`, and
  applies after `RestoreSelectedNode`.
- **Cold launch** — `RestoreSelectedNode` seeds a pending restore from `LastDeepPath`
  with mode `Locate`.
- **`--deep-link`** — parsed at startup into a pending restore with mode `Full`
  (explicit user intent); takes precedence over persisted state.
- **Apply** — in `OnSelectedNodeChanged`, after the existing per-VM refresh branch, if a
  pending restore targets this node and its editor is `IDeepNavigable`, dispatch
  `TryRestoreDeepPathAsync`. Tab selection and scroll-into-view are re-applied at
  `DispatcherPriority.Loaded` per `RequestExpandPermissionsAdvanced`'s documented reason.
- Log `[DeepLink] path=… mode=… resolved=true|false`, matching the existing
  `[DeepLink]` prefix.

`WindowState.LastSelectedNodeTitle` stays for back-compat; `LastDeepPath` wins when
present. Retiring the title-keyed field (and with it the latent break when nav titles
get localized) is a follow-up, not this branch.

### 4.5 Revealing the target item — prepopulate the filter, don't scroll

The app already solves "make a deep-linked item visible without scrolling" for property
jump links: `SelectSearchResult` calls
`SettingsGroupEditorViewModel.ApplyNavigationFilter(result.PropertyKey)`, which filters
the page down to the target and marks the box with the orange "navigated" frame so the
user can see the list is filtered and clear it to get context back.

Agents & Skills mirrors that exactly. `AgentsSkillsEditorViewModel` gets:

```csharp
/// Filter applied BY navigation (deep link / restore), not typed by the user.
/// Deep-link handlers must call this instead of assigning FilterText — a direct
/// assignment reads as a user edit and skips the navigated frame.
public void ApplyNavigationFilter(string? filter);
[ObservableProperty] private bool _filterFromNavigation;
```

with the same `_applyingNavFilter` latch and the same `OnFilterTextChanged` rule: any
change that does not come through `ApplyNavigationFilter` is a user edit and drops
`FilterFromNavigation`.

Restore therefore reveals its target by calling
`ApplyNavigationFilter(row.DisplayName)` — the item is on screen, the frame explains
why, and clearing the filter restores the full list.

**This removes the need to convert the three lists to `ListBox`.** No `ScrollIntoView`,
no header-selectability problem, no second selection concept competing with
`SelectedArtifact`. The `ItemsControl` + `VirtualizingStackPanel` shape stays as-is.
Converting to `ListBox` becomes a step for whenever multi-select UI actually ships
(§6), not a prerequisite here.

Frame styling follows the two traps `AGENTS.md` calls out together for exactly this
control: a property a Style sets must **not** also appear as an inline attribute on the
same element (LocalValue outranks every Style setter), and a control's `Styles` apply to
its descendants rather than itself — so the class selectors are hoisted to the parent.
The working reference is `Border.filter-frame` / `.filter-frame.nav-filter` in
`GroupPropertiesView.axaml`.

### 4.6 The stale-row fix (§2.2)

In `SaveAsync`, after the confirming re-read, set `row.Subtitle` from the saved
front-matter `description`, using `FillDescriptionsAsync`'s normalization
(null/whitespace → `"(no description)"`). Deliberately **not** a full `RefreshAsync`,
which would rebuild every row and drop the selection the user is looking at.

---

## 5. W3 — `--deep-link <path>`

This is a **debug-flag-shaped argument, not a CLI-bypass tool** — `AGENTS.md` separates
the two, and this one launches the GUI rather than running a task and exiting. It
follows the two-token `--culture` pattern exactly.

Per the "Adding a new debug flag" checklist:

- `DebugFlags.DeepLinkPath { get; private set; }` (`string?`).
- `case "--deep-link":` consuming `args[++i]`; **lowercase case label** (comparison is
  `ToLowerInvariant`).
- Validate before assigning: non-empty, no leading/trailing `/`, no empty segment,
  ≤ 4 segments, conservative charset. Reject via `_deferredWarnings.Add` — **never**
  `Log.*` inside `Initialize`, which runs before Serilog is configured.
- Add to `ListActive()` so it appears in the startup flags line.
- Clear in `ResetForTesting()`.
- Extend the `--debug-help` string.
- Document in the `CLAUDE.md` debug-flags table and in `README.md` (it is
  integration-facing, not QA-only).

Validation is shape-only. Whether the path *resolves* is decided later against the built
nav tree, and an unresolvable path logs and falls back to normal startup — a stale
shortcut must never block launch.

---

## 6. W4 — Multi-select groundwork (no visible UI)

- `[ObservableProperty] private bool _isSelected;` on `ArtifactRowViewModel`.
- **`ApplyFilter` stays a projection over the same row instances**, never a rebuild — so
  selection survives filtering. This is the one decision here that would genuinely make
  multi-select harder if got wrong: a filter that reconstructs row VMs silently drops
  every selection when the user narrows the list.
- Keep an `AllRows` view so a future export is `AllRows.Where(r => r.IsSelected)` with no
  change to the list shape.

Since §4.5 no longer needs `ListBox`, the lists stay `ItemsControl` — which has **no
selection support**. Converting the three lists to `ListBox` with
`SelectionMode="Multiple"` is therefore a known, self-contained step for whenever
multi-select UI ships, along with a `ListBoxItem` style that makes
`ArtifactSectionHeaderViewModel` items non-focusable. Recording it here so it is a
planned step rather than a surprise; nothing in this branch blocks it.

Nothing user-visible ships. The questions export will have to answer — destination
picker, whole-directory copy for skills, name-collision policy — are explicitly *not*
pre-decided here.

---

## 7. File-by-file

**New**

| File | Purpose |
|---|---|
| `src/ClaudeForge/ViewModels/IDeepNavigable.cs` | Interface + `DeepRestoreMode` |
| `src/ClaudeForge/ViewModels/NavDeepPath.cs` | Pure parse/format/resolve |
| `tests/ClaudeForge.Tests/ViewModels/NavDeepPathTests.cs` | Grammar |
| `tests/ClaudeForge.Tests/ViewModels/NavigationNodeIdTests.cs` | Presence + uniqueness guard |
| `tests/ClaudeForge.Tests/Headless/DeepPathReloadTests.cs` | Reload round-trip |

**Modified**

| File | Change |
|---|---|
| `src/LayeredEditors.ViewModels/NavigationNodeViewModel.cs` | `NodeId` |
| `src/ClaudeForge/ViewModels/MainWindowViewModel.cs` | `NavId*` consts; `NodeId` on every node; pending-restore state; capture in `SaveWindowState`; capture/restore around `ReloadCoreAsync`; seed in `RestoreSelectedNode`; apply in `OnSelectedNodeChanged` |
| `src/ClaudeForge/Services/NavigationTreeBuilder.cs` | Slugged `NodeId` for group children |
| `src/ClaudeForge/ViewModels/AgentsSkillsEditorViewModel.cs` | Filter + `ApplyNavigationFilter` / `FilterFromNavigation`; segment ids + `SelectSegment`; `IDeepNavigable`; subtitle fix in `SaveAsync` |
| `src/ClaudeForge/ViewModels/ArtifactRowViewModel.cs` | `IsSelected` |
| `src/ClaudeForge/Views/AgentsSkillsEditorView.axaml` | Filter row + navigated-frame styling (lists unchanged) |
| `src/ClaudeForge/Views/AgentsSkillsEditorView.axaml.cs` | Realized-count probe |
| `src/ClaudeForge/Services/WindowStateService.cs` | `LastDeepPath` |
| `src/ClaudeForge/Services/DebugFlags.cs` | `--deep-link` |
| `src/ClaudeForge/Program.cs` | Hand the parsed path to the app |
| `Localization/Strings*.resx` + `Strings.Designer.cs` | 6 keys × 9 files |
| `CLAUDE.md`, `README.md`, `AGENTS.md` | Flag table; feature note; `IDeepNavigable` pointer |

---

## 8. Verification

Baseline first: `dotnet build` (0 warnings) and `dotnet test` green **before** any edit,
so a later failure is attributable.

- **Unit** — `ApplyFilter` (match fields; header dropped when its group empties; header
  kept when a row survives; clear restores); segment id ↔ index; `NavDeepPath` round-trip
  and greedy node resolution; item-key resolution by path / `name@source` / bare name,
  plus the ambiguous and missing cases; `SaveAsync` updates `Subtitle`; `Locate` does not
  enter edit mode while `Full` does.
- **Guards** — every nav node has a unique `NodeId`; `DebugFlags` `--deep-link` cases
  (present, missing value, invalid value, value-then-next-flag); `WindowState.LastDeepPath`
  round-trip in the `TestUserProfileOverride` sandbox.
- **Headless** — select Agents & Skills, open an artifact, reload, assert segment +
  artifact + edit buffer restored (following `Headless/ReloadHardeningTests` /
  `TransactionalReloadTests`).
- **Existing gates** — `LocalizationParityTests` (real translations, no `TODO`, no
  near-copies of English), `AxamlAccessibilityCoverageTests` (new controls need
  `AutomationProperties.Name`).
- **Trim publish** — `pwsh src/publish/publish.ps1 -All -Rids win-x64`, 0 IL2026/IL2070/IL3050.
  Required: this touches `WindowState` JSON and AXAML templates.
- **Manual** — scroll deep into Skills, edit + save, confirm the row subtitle updates;
  Reload Window mid-edit and confirm the typed text returns *and* the list comes back
  filtered to the item with the orange navigated frame; clear the filter and confirm the
  full list returns; relaunch and confirm the item is revealed but edit mode is *not*
  re-entered; `--deep-link agents-skills/skills/<name>`; an unresolvable path still
  launches normally; check the realized-count probe.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Restore lands before the navigation rebuild re-materializes the tab/list, leaving it un-applied | Re-apply at `DispatcherPriority.Loaded` per `RequestExpandPermissionsAdvanced` |
| Restore assigns `FilterText` directly, so the navigated frame never appears and a nav filter looks user-typed | Restore goes through `ApplyNavigationFilter`; the `SettingsGroupEditorViewModel` original documents this as a required contract |
| Navigated-frame style never renders | Both `AGENTS.md` styling traps at once: no inline attribute for a style-set property, and class selectors hoisted to the parent. Copy `Border.filter-frame` / `.filter-frame.nav-filter` from `GroupPropertiesView.axaml` |
| Filter rebuilds row VMs, silently dropping future multi-selection | `ApplyFilter` is a projection over the same instances; §6 |
| Filter can't match descriptions until the lazy fill completes | Re-raise `Filtered*` once on `LastDescriptionFill` completion; documented, accepted |
| `NodeId` slug collision between two group names | Uniqueness guard test |
| 48 new translations | Flagged in §3 with a 2-key fallback |
| Transient edit buffer accidentally persisted | `CaptureTransientState` is contractually in-memory-only and never reaches `WindowState`; asserted in the reload test |

## 10. Explicitly out of scope

Multi-select UI and export (including the `ItemsControl` → `ListBox` conversion it
needs); new grouping axes; adopting `IDeepNavigable` on other pages; true scroll-offset
persistence (reveal-by-filter supersedes it for this feature); retiring
`WindowState.LastSelectedNodeTitle`; a `claude://` protocol handler; promoting
`IDeepNavigable` into `LayeredEditors.Abstractions`; caching
`AgentsSkillsEditorViewModel` as a persistent tool VM (the deep-path restore makes it
unnecessary, and the code comment proposing it predates editing landing).
