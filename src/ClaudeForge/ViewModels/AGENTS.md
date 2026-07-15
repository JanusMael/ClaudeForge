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
NavigationTree
 ├─ NavigationNodeViewModel("Claude Code")      ← header; .Editor = null
 │   ├─ NavigationNodeViewModel("General")      ← .Editor = SettingsGroupEditorViewModel
 │   ├─ NavigationNodeViewModel("Permissions")  ← .Editor = PermissionsEditorViewModel
 │   ├─ NavigationNodeViewModel("Hooks")        ← .Editor = HooksEditorViewModel
 │   ├─ NavigationNodeViewModel("MCP Servers")  ← .Editor = McpServersEditorViewModel
 │   └─ ...
 └─ NavigationNodeViewModel("Claude Desktop")   ← header; .Editor = null
     └─ ...
```

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
