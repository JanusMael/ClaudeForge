# AGENTS.md — workspace, scope, and dirty-tracking semantics

> Sidecar to the root [`AGENTS.md`](../../../AGENTS.md). Scope: the in-memory
> domain model in `src/ClaudeForge.Core/Settings/`. No Avalonia, no UI — every
> contract here is testable from `tests/ClaudeForge.Core.Tests/`.

---

## 1. `ConfigScope` — value table and priority rule

Source: `ConfigScope.cs`.

| Scope     | Value | Priority | File path                               | Notes                                                     |
|-----------|------:|---------:|-----------------------------------------|-----------------------------------------------------------|
| `Managed` |     0 |  Highest | (varies by OS)                          | Read-only. Set by enterprise/MDM. Cannot be overridden.   |
| `Local`   |     1 |          | `<project>/.claude/settings.local.json` | Gitignored, personal. Highest among user-editable scopes. |
| `Project` |     2 |          | `<project>/.claude/settings.json`       | Committed, shared with team.                              |
| `User`    |     3 |   Lowest | `~/.claude/settings.json`               | Applies to every project.                                 |

**Priority rule**: lower numeric value = higher priority. `MergeEngine` orders
documents by `(int)Scope` ascending so Managed comes first.

`LayeredValue` orders entries the same way:
`entries.OrderBy(e => (int)e.Scope).ToList()`.

---

## 2. `ClaudeScope._cache` invariant

Source: `src/ClaudeForge/Adapters/ClaudeScope.cs` (`_cache` array).

```csharp
private static readonly ClaudeScope[] _cache =
[
    new(ConfigScope.Managed), // index 0
    new(ConfigScope.Local),   // index 1
    new(ConfigScope.Project), // index 2
    new(ConfigScope.User),    // index 3
];

public static ClaudeScope For(ConfigScope scope) => _cache[(int)scope];
```

**The invariant**: cache array entries MUST appear in `ConfigScope` numeric
order, because `For(scope)` indexes by `(int)scope`. Reorder one → reorder
both. There is no runtime check; mismatch produces the wrong wrapper silently.

**Failure signature**: `For(ConfigScope.User)` returns the wrapper for, say,
`Project` because the Project entry sits at index 3. Callers think they're
asking about User-scope but get Project-scope priority and read-only flag.
Permission checks pass against the wrong scope.

If you reorder `ConfigScope` (or insert a new value) you MUST insert the
matching cache entry at the matching index. The `Priority` value is computed
by `ToLibraryPriority(scope) = 3 - (int)scope`, which also assumes a
contiguous 0..3 enum range — extending the enum past 3 requires updating that
formula too (`ClaudeScope.ToLibraryPriority`).

---

## 3. `IsDirty` (latch) vs `HasActualChanges()` (structural)

Source: `SettingsDocument.cs`.

| Property             | Set by                                                                    | Cleared by                                                                          | Returns                                    |
|----------------------|---------------------------------------------------------------------------|-------------------------------------------------------------------------------------|--------------------------------------------|
| `IsDirty`            | `MarkDirty()`, called from `SettingsWorkspace.SetValue` and `RemoveValue` | `MarkClean()`, called from `UpdateRoot` and from `SaveAsync` after successful write | `bool` write-latch                         |
| `HasActualChanges()` | (computed)                                                                | (computed)                                                                          | `!JsonNode.DeepEquals(Root, BaselineRoot)` |

Why both exist: `IsDirty` is a one-way latch. After a user types into a field
and then types the original value back (or clicks Reset), `IsDirty` stays
`true` even though the document content matches the on-disk baseline.

`HasActualChanges()` performs a structural comparison so it correctly returns
`false` after a set-then-reset cycle.

**When to use which**:

- Save-button enable, "do we need to actually write?" → `HasActualChanges()`.
  See `MainWindowViewModel.OnAnyWorkspaceChanged → ComputeHasActualChanges`
  (`MainWindowViewModel.ComputeHasActualChanges`).
- "Should we ask the user before discarding?" / "What files were touched
  since load?" → either works, but `IsDirty` is cheap (no JSON walk).
- `SettingsWorkspace.DirtyDocuments()` returns docs where `IsDirty` is true.
  Save iterates that list, so a doc that has `HasActualChanges()=false` but
  `IsDirty=true` will be re-written verbatim on save (harmless, but
  bandwidth/timestamp churn).

Locked by `tests/ClaudeForge.Tests/ViewModels/HasUnsavedChangesRecheckTests.cs`
— `EditThenReset_FlipsHasUnsavedChangesBackToFalse` and
`SetThenRevertSameValue_ClearsHasUnsavedChanges`. Either test failing means
this contract was broken.

---

## 4. `MergeEngine` semantics

Source: `MergeEngine.cs`.

Three merge strategies, dispatched by JSON shape and `ArrayPaths`:

1. **Arrays → UNION across all scopes** (`MergeArrays`). Walk highest-priority
   first; union by stringified item. Effective scope is the highest-priority
   scope contributing at least one item.
2. **Objects → deep merge** (`MergeObjects`). Each key resolved independently
   by recursion. Dotted child paths (`"permissions.allow"`) threaded through
   so nested array-keys still get UNION semantics.
3. **Scalars / mixed → highest-priority scope wins** (`MergeCore`).

Array-path opt-in is explicit, governed by `SettingsWorkspace.ArrayPaths`:

```
claudeMdExcludes
availableModels
httpHookAllowedEnvVars
allowedHttpHookUrls
permissions.allow
permissions.deny
permissions.ask
permissions.additionalDirectories
enabledMcpjsonServers
disabledMcpjsonServers
companyAnnouncements
```

Adding a new array-merged path: add it to `ArrayPaths`, add a regression test
in `tests/ClaudeForge.Core.Tests/Settings/MergeEngineTests.cs` (or wherever
existing array-merge tests live).

**Subtle rule** in `MergeObjects`: passing `false` as `childIsArray` would
force scalar-wins semantics even for actual JSON arrays not listed in
`ArrayPaths`, silently dropping lower-scope contributions. The code passes
`null` (auto-detect from value type) instead. Don't change that to `false`.

---

## 5. `LayeredValue.Entries` may have duplicate scopes

`SettingsWorkspace.GetLayeredValue` produces one `ScopeEntry` per loaded
document that contains the key. `LayeredValue.Entries` therefore CAN
legitimately contain multiple entries at the same `Scope` — most commonly
when `~/.claude/managed-settings.d/` contains several drop-in files, each
producing its own `SettingsDocument` at `ConfigScope.Managed`.

**Implication for callers that filter by scope**: you must `.Distinct()` the
projection or the user sees duplicate scope-indicator chiclets in the editor
header.

Existing dedup sites:

- `PropertyEditorViewModel.SetScopeState` (the canonical place for the
  "other scopes with data" derivation).
- `McpServersEditorViewModel.LoadFromLayered`.
- `HooksEditorViewModel.LoadFromLayered`.

If you add a new editor that also computes `OtherScopesWithData` from
`layered.Entries`, you MUST `.Distinct()` it.

---

## 6. `_selfWriting` guard

Source: `src/ClaudeForge/ViewModels/SettingsGroupEditorViewModel.cs` —
the `_selfWriting` field, the `OnWorkspaceChanged` early-out, and the
live-write try/finally block in `ApplyToWorkspace`.

`SettingsGroupEditorViewModel` listens to `SettingsWorkspace.Changed` and
rebuilds its child editors on every event. Its OWN writes (the live-write
path that pushes editor state back into the workspace as the user types) also
raise `Changed`, which would cause the editor to rebuild mid-edit and destroy
the user's in-progress input.

The guard:

```csharp
private bool _selfWriting;

private void OnWorkspaceChanged(object? sender, EventArgs _)
{
    if (_selfWriting) return;       // skip own-writes
    Rebuild();                      // external write or file watcher
}

// Live-write path — guards the workspace mutation:
_selfWriting = true;
try
{
    _workspace.SetValue(key, newValue, scope);
}
finally
{
    _selfWriting = false;
}
```

Identical pattern in `EnvironmentEditorViewModel` for the same reason
(constructor flag, workspace handler early-out, mutation try/finally).

**The trap**: setting `_selfWriting = true` AFTER calling `SetValue` is too
late — the `Changed` event fires synchronously inside `SetValue`. Set it
BEFORE the call.

---

## 7. `BaselineRoot` and the save round-trip

Source: `SettingsDocument.cs` (`BaselineRoot` field + `MarkClean()`).

`BaselineRoot` is a deep clone of `Root` taken at load time (constructor) and
refreshed inside `MarkClean()`. The Save-changes-summary dialog diffs `Root`
against `BaselineRoot` to enumerate what the user changed.

After a successful save (`SaveAsync`), the document calls `MarkClean()`,
which both clears `IsDirty` AND advances `BaselineRoot = Root.DeepClone()`.
The next round of dirty tracking compares against the just-written state, not
the original load state.

**Don't mutate `BaselineRoot` directly.** It's `private set;` for a reason.
The only writes happen in the constructor, `MarkClean()`, and `UpdateRoot()`.

---

## 8. Where the in-memory model meets the rest of the app

| Concern                                  | File                                                                                                                                                                                                                                                                                                                                               |
|------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Disk I/O (`LoadAsync` / `SaveAsync`)     | `src/ClaudeForge.Core/Settings/SettingsLoader.cs` (or equivalent — search for `LoadAsync`)                                                                                                                                                                                                                                                         |
| Workspace adapter for the editor library | `src/ClaudeForge/Adapters/ClaudeWorkspaceAdapter.cs`, `ClaudeValueAdapter.cs`                                                                                                                                                                                                                                                                      |
| Editor base class                        | `src/LayeredEditors.Avalonia/ViewModels/PropertyEditorViewModel.cs`                                                                                                                                                                                                                                                                                |
| App-shim editor base                     | `src/ClaudeForge/ViewModels/Editors/PropertyEditorViewModel.cs`                                                                                                                                                                                                                                                                                    |
| Top-level orchestration                  | `src/ClaudeForge/ViewModels/MainWindowViewModel.cs` (`OnAnyWorkspaceChanged`, `ComputeHasActualChanges`)                                                                                                                                                                                                                                           |
| Compound-editor contract                 | [`src/ClaudeForge/ViewModels/Editors/AGENTS.md`](../../ClaudeForge/ViewModels/Editors/AGENTS.md)                                                                                                                                                                                                                                                   |
| Backup / restore strategy                | `src/ClaudeForge.Core/Backup/BackupEngine.cs` — `ShouldSkipHomeSubdir` excludes `backups/`, `cache/`, `downloads/`, `statsig/`, `shell-snapshots/`, `local/` always; `projects/` only in non-Full modes. Add new exclusions there and add a matching `CreateAsync_ExcludesXxx` test in `tests/ClaudeForge.Core.Tests/Backup/BackupEngineTests.cs`. |

---

## 9. Test seams

- **Workspace mutation test**: instantiate `MainWindowViewModel`, call
  `await vm.InitializeCommand.ExecuteAsync(null)`, then
  `vm.GetClaudeCodeWorkspaceForTesting()` (internal seam at
  `MainWindowViewModel.GetClaudeCodeWorkspaceForTesting`). Mutate via `SetValue` / `RemoveValue`,
  observe `vm.HasUnsavedChanges`.
- **Sandbox file paths**: `PlatformPaths.TestUserProfileOverride = sandbox`
  in `[TestInitialize]`, restore to `null` in `[TestCleanup]`. Template in
  the root [`AGENTS.md`](../../../AGENTS.md) §3.
- **Merge-engine round-trip**: build `SettingsDocument` instances directly
  from `JsonObject` literals and feed to `MergeEngine.ComputeEffective`.
  No I/O, no workspace, no UI — fastest tests in the suite. See
  `tests/ClaudeForge.Core.Tests/Settings/MergeEngineTests.cs`.
