# ClaudeForge ViewModels — Agent Operational Guide

Cross-file invariants for the ViewModel layer.
Read alongside the root [`AGENTS.md`](../../../AGENTS.md) and
[`Editors/AGENTS.md`](./Editors/AGENTS.md).

---

## §1 `MainWindowViewModel` — integration hub

`MainWindowViewModel` (MWVM) owns everything that bridges the SDK, Core, and UI:

| Owned resource            | Field / property                                                 |
|---------------------------|------------------------------------------------------------------|
| Claude Code SDK client    | `ClaudeCodeSdk : ClaudeCodeClient?`                              |
| Claude Desktop SDK client | `ClaudeDesktopSdk : ClaudeDesktopClient?`                        |
| Shared schema registry    | `_schemaRegistry : SchemaRegistry`                               |
| Navigation tree           | `NavigationTree : ObservableCollection<NavigationNodeViewModel>` |
| Search VM                 | `SearchVm : SearchViewModel`                                     |
| Snapshot service          | `_snapshotService`                                               |
| Dirty-flag                | `HasUnsavedChanges` (recomputed from SDK `HasActualChanges()`)   |

MWVM is the **only** place where SDK clients are constructed, opened, and disposed.
Editor VMs and search VMs receive delegates or already-constructed objects — they
never `new` an SDK client themselves.

## §2 Navigation tree structure

```
NavigationTree                                    NodeId
 ├─ NavigationNodeViewModel("Claude Code")      ← "claude-code"       header; .Editor = null
 │   ├─ NavigationNodeViewModel("General")      ← "general"           .Editor = SettingsGroupEditorViewModel
 │   ├─ NavigationNodeViewModel("Permissions")  ← "permissions"       .Editor = PermissionsEditorViewModel
 │   ├─ NavigationNodeViewModel("Hooks")        ← "hooks"             .Editor = HooksEditorViewModel
 │   ├─ NavigationNodeViewModel("MCP Servers")  ← "mcp-servers"       .Editor = McpServersEditorViewModel
 │   └─ ...
 └─ NavigationNodeViewModel("Claude Desktop")   ← "claude-desktop"    header; .Editor = null
     └─ ...
```

**`Title` vs `NodeId`.** `Title` is the display label, and it is still hardcoded
English precisely because programmatic lookups compare against it. `NodeId` is the
culture-invariant lookup key — added so deep links and persisted UI state keep
resolving once the nav tree is localized. **New lookups should match on `NodeId`.**
Ids are unique among SIBLINGS only (`version-info` exists under both products);
dividers carry none; settings-group children use `NavDeepPath.Slug(group.Title)`.

**Two editor types:**

| Editor type                                                                                                                                                         | Schema access                                     | Search treatment                                           |
|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------|------------------------------------------------------------|
| `SettingsGroupEditorViewModel`                                                                                                                                      | Exposes `SchemaNodes : IReadOnlyList<SchemaNode>` | Walk schema nodes; match by name / title / desc / JsonPath |
| Specialized VMs (`PermissionsEditorViewModel`, `HooksEditorViewModel`, `McpServersEditorViewModel`, `MarketplacesEditorViewModel`, `EnabledPluginsEditorViewModel`) | No schema node list                               | Match by page title only                                   |

A node with `Editor == null` is a header (section divider); never add a result that navigates to a header node.

## §3 `SearchViewModel` contract

`SearchViewModel` (`SearchViewModel.cs`) is intentionally **decoupled from the SDK**.
It receives delegates, not `IClaudeConfigClient` references:

```csharp
new SearchViewModel(
    getNavigationTree:  () => NavigationTree,
    isLoadingProbe:     () => _isLoadingWorkspaces,
    claudeCodeNavTitle: "Claude Code")
```

**Why delegates, not SDK refs?**

- Keeps `SearchViewModel` unit-testable without Avalonia or SDK dependencies.
- Nav tree is already in-memory; schema nodes inside `SettingsGroupEditorViewModel`
  are the same objects built from `SchemaTreeBuilder.BuildTopLevel` — no double-fetch needed.

**If you need SDK-backed search** (e.g. to add ranking, to expose search to non-GUI
consumers), use `IClaudeConfigClient.SearchSchema(query)` from the SDK layer and
map results back to nav nodes via the path-to-node lookup described in §5 below.
See `src/ClaudeForge.Sdk/AGENTS.md §2` for the SDK / navigation boundary contract.

**Synthetic results (deep-links with no backing schema property):** `ExecuteSearch`
adds pinned `IsSynthetic` rows for common gotchas — `--dangerouslySkipPermissions`
(prefix `danger…`, empty `PropertyKey`) and `bypassPermissions` (query contains
`bypass`, excludes `disable`, `PropertyKey="permissions.defaultMode"`), plus the
Essentials-card triggers. `MainWindowViewModel.SelectSearchResult` branches on
`PropertyKey`: the bypass row lands on the Permissions Overview tab and calls
`permEditor.ActivateBypassHint()`; the danger row calls `ActivateDangerHint()` +
expands Advanced. When adding a synthetic, keep the trigger distinct from
existing ones and add a `SearchViewModelTests` / `SearchViewModelBypassTests` case
(present-vs-absent node, distinctness).

**Internal test surface:**

- `SearchViewModel.ExecuteSearch(string query)` — `internal`; drives matching directly,
  no debounce, no dispatcher. Safe to call from unit tests.
- `SearchViewModel.FlattenSchemaNodes(nodes)` — `internal static`; depth-first schema walk.
- `SearchViewModel.BuildSnippet(text, query, maxLen)` — `internal static`; excerpt helper.

## §4 Specialized editors — search implications

When adding a new specialized editor page:

1. Create the editor VM (e.g. `FooEditorViewModel`).
2. Register in `NavigationTreeBuilder` so a `NavigationNodeViewModel` is created with
   `Editor = new FooEditorViewModel(...)`.
3. **Update `SearchViewModel.ExecuteSearch`** — the `else if (child.Editor is not null)`
   branch handles all non-`SettingsGroupEditorViewModel` editors by title match. No code
   change needed IF the page title alone is sufficient for discovery. If the page needs
   richer search (e.g. matching individual entries), add a new branch.
4. Add a `SearchViewModelTests` test case for the new page (see
   `ExecuteSearch_SpecializedEditor_MatchedByPageTitle` as a template).

**Known gap — global search does not find Agents & Skills *items*.** That page is
matched by page TITLE only, so global search surfaces config properties but not
individual skills / agents / commands. Now that artifacts have stable item keys
(`NavDeepPath.FormatItemKey`) and reveal-by-filter exists, `SelectSearchResult`
could reuse the same restore machinery to deep-link straight to one. Deliberately
not done yet — it is a scope decision, not an oversight.

## §5 JsonPath → NavigationNodeViewModel mapping

The SDK's `SearchSchema` returns `SchemaSearchResult` with `JsonPath` but no nav target.
To map a `JsonPath` back to the `NavigationNodeViewModel` that hosts it, build a lookup
dictionary from the nav tree:

```csharp
// Build once per search call (nodes are already in-memory — cheap).
var map = new Dictionary<string, (NavigationNodeViewModel child, string sectionTitle, string groupName)>(
    StringComparer.OrdinalIgnoreCase);
foreach (var navNode in NavigationTree)
{
    foreach (var child in navNode.Children)
    {
        if (child.Editor is not SettingsGroupEditorViewModel groupEditor) continue;
        foreach (var schema in SearchViewModel.FlattenSchemaNodes(groupEditor.SchemaNodes))
        {
            if (!string.IsNullOrEmpty(schema.JsonPath))
                map.TryAdd(schema.JsonPath, (child, navNode.Title!, groupEditor.GroupName));
        }
    }
}
```

This lookup is O(total schema nodes) to build and O(1) per result lookup.
If you wire `SearchViewModel` to use `SearchSchema`, build this map inside the
existing `ExecuteSearch` method rather than caching it on the VM — the nav tree
is rebuilt on each workspace reload.

## §5b Deep-path capture / restore

Addressing a position *below* a nav node — a tab, an item, an open editor — so it
survives a reload and can be reached from the command line.

| Piece | Where |
|---|---|
| Grammar, slug, resolution | `NavDeepPath` (pure static, no Avalonia/SDK dep) |
| Page contract | `IDeepNavigable` + `DeepRestoreMode` |
| Persistence | `WindowState.LastDeepPath` ← `MainWindowViewModel._lastDeepPath` |
| Command line | `DebugFlags.DeepLinkPath` (`--deep-link <path>`) |
| Wiring | `MainWindowViewModel.CaptureDeepPath` / `TryQueueDeepRestore` / `ApplyPendingDeepRestore` / `RestoreDeepPathAsync` |
| Only adopter today | `AgentsSkillsEditorViewModel` |

**Grammar** — `<top-level-id>[/<child-id>][/<tab-id>[/<item-key>]]`, resolved
strictly left-to-right. That ordering is what disambiguates
`claude-code/permissions`: segment 2 is consumed as a CHILD NODE when it matches
one, and only later segments mean tab and item.

**Two fidelities.** `DeepRestoreMode.Full` is for an in-process Reload Window and
restores the editing experience; `Locate` is for a cold launch or an explicit
`--deep-link` and stops at selecting the item. Cold launch is deliberately
`Locate`: the buffer that made an edit meaningful died with the previous process,
so re-entering the editor seeded from disk would look like unsaved work had come
back.

**Capture points** — navigating away from a deep-navigable page, and
`ReloadCoreAsync` (once, *before* its `do/while (_reloadPending)` loop; capturing
inside would record the already-rebuilt empty state on a second iteration).
Reload capture applies to EVERY caller — toolbar button, profile switch, file
watcher, post-restore — because an automatic reload eating an in-progress edit is
no better than a manual one doing it. It is cheap: pure in-memory bookkeeping, no
disk write.

**Persistable vs transient.** `CaptureDeepPath()` returns short, culture-invariant
segments that are safe to persist. `CaptureTransientState()` returns an opaque
in-memory snapshot that **must never** be persisted — it holds the unsaved edit
buffer. Reload is in-process, so that payload simply rides across in memory, which
is what makes the reload lossless without writing user content into
`ClaudeForge-gui-state.json` and without needing a confirm dialog. Guarded by
`DeepPathReloadTests.Reload_DoesNotPersistTheUnsavedEditBuffer`.

**Three traps**, each with an invariant in the root [`AGENTS.md`](../../../AGENTS.md) §1:
never compute the deep path inside `SaveWindowState`; reveal via
`ApplyNavigationFilter`, not by assigning `FilterText`; and re-raise a computed
filtered projection after any rebuild of its source collection. A fourth, smaller
one: a restore must await the page's **in-flight** load (`LastRefresh`) rather than
starting its own, or the second walk rebuilds the rows underneath the restore that
is busy resolving one.

Restore is fire-and-forget (`OnSelectedNodeChanged` is a synchronous partial), so
tests await `MainWindowViewModel.LastDeepRestore`. It also re-asserts the tab at
`DispatcherPriority.Loaded`, for the same reason
`RequestExpandPermissionsAdvanced` does: selecting a node triggers a view rebuild
that can land after the synchronous part of the restore.

## §6 `WorkspaceForGui` — migration artifact

`ClaudeConfigClientCore.WorkspaceForGui` (internal property) returns the live
`SettingsWorkspace` directly. It exists so MWVM and editor factory chains can keep
their `workspace.GetLayeredValue` / `workspace.SetValue` paths during the partial
SDK migration (Pass 4.3.7). Once the full editor pipeline migrates to SDK accessors,
`WorkspaceForGui` and its callers can be removed.

**Never store `WorkspaceForGui` in a long-lived field** — the workspace object is
replaced on `ReloadAsync`. Always call it at the point of use.

## §7 Key files and their roles

| File                                       | Role                                                                         |
|--------------------------------------------|------------------------------------------------------------------------------|
| `MainWindowViewModel.cs`                   | Integration hub; SDK lifecycle, nav tree build, search VM construction       |
| `SearchViewModel.cs`                       | Debounced search; schema walk + specialized editor title match               |
| `SearchResultViewModel.cs`                 | Immutable row in search results; carries `Node`, `PropertyKey`, `Snippet`    |
| `SettingsGroupEditorViewModel.cs`          | Generic property group editor; exposes `SchemaNodes`, `Editors`, `GroupName` |
| `Editors/PermissionsEditorViewModel.cs`    | Specialized editor; no schema node list                                      |
| `Editors/HooksEditorViewModel.cs`          | Specialized editor                                                           |
| `Editors/McpServersEditorViewModel.cs`     | Specialized editor                                                           |
| `Editors/MarketplacesEditorViewModel.cs`   | Specialized editor                                                           |
| `Editors/EnabledPluginsEditorViewModel.cs` | Specialized editor                                                           |

## §8 Test seams

| Seam                                    | File                             | Usage                                                           |
|-----------------------------------------|----------------------------------|-----------------------------------------------------------------|
| `GetClaudeCodeSdkClientForTesting()`    | `MainWindowViewModel.cs`         | Access live SDK client for mutation-based integration tests     |
| `SearchViewModel` delegate constructor  | `SearchViewModel.cs`             | Pass fake `getNavigationTree` + `isLoadingProbe` for unit tests |
| `PlatformPaths.TestUserProfileOverride` | `Core/Platform/PlatformPaths.cs` | Redirect `~/.claude/` to sandbox                                |
| `DebugFlags.ResetForTesting()`          | `Services/DebugFlags.cs`         | Reset flags + `PlatformInfo.Current` in `[TestCleanup]`         |
