# AGENTS.md — workspace, scope, and dirty-tracking semantics

> Sidecar to the root [`AGENTS.md`](../../../AGENTS.md). Scope: the in-memory
> domain model in `src/AgentForge.Core/Settings/`. No Avalonia, no UI — every
> contract here is testable from `tests/AgentForge.Core.Tests/`.

---

## 1. `ConfigScope` — value table and priority rule

Source: `ConfigScope.cs`.

| Scope     | Ordinal | Priority | File path                               | Notes                                                     |
|-----------|--------:|---------:|-----------------------------------------|-----------------------------------------------------------|
| `Managed` |       0 |  Highest | (varies by OS)                          | Read-only. Set by enterprise/MDM. Cannot be overridden.   |
| `Local`   |       1 |          | `<project>/.claude/settings.local.json` | Gitignored, personal. Highest among user-editable scopes. |
| `Project` |       2 |          | `<project>/.claude/settings.json`       | Committed, shared with team.                              |
| `User`    |       3 |   Lowest | `~/.claude/settings.json`               | Applies to every project.                                 |

**Priority rule**: lower ordinal = higher priority. `SettingsWorkspace` and
`LayeredValue` both order by `Scope.Ordinal` ascending, so Managed comes first.

**It is a `readonly record struct`, not an `enum`** (Phase 3). Three consequences
that the compiler will not always warn you about:

- **`default(ConfigScope)` is `Managed`**, exactly as the enum's `default` was. This
  is why the struct is backed by a single ordinal rather than a record of
  `(Id, Priority, DisplayName, IsReadOnly)`: a dozen editors declare
  `private ConfigScope _lastScope;` with no initialiser and depend on it. The
  richer shape compiles, passes 2,791 of 2,792 tests, and silently changes what
  those fields mean.
- **`ToString()` is consumed as data.** `ClaudeScope` builds its `Id` from
  `ToLowerInvariant()` and its `DisplayName` from `ToUpperInvariant()`, and three
  AXAML converters resolve brushes, tooltips and labels through it. It must keep
  returning `"Managed"` / `"Local"` / `"Project"` / `"User"`.
- **You cannot use it as a default parameter value or a `case` label** — neither is a
  compile-time constant. Use an overload (or `ConfigScope?` plus a coalesce when other
  optional parameters follow), and `when` guards instead of constant patterns.

`ConfigScope.All` replaces `Enum.GetValues<ConfigScope>()` and preserves declaration
order, which is visible: the scope legend and the property editor's per-scope rows
render in it. `ConfigScope.IsReadOnly` marks the policy-controlled rungs — **use it
instead of comparing against `Managed`** in product-neutral code, because another
product's ladder may have more than one (OpenCode has managed *and* macOS MDM).

All of the above is pinned by `tests/AgentForge.Core.Tests/Settings/ConfigScopeTests.cs`;
every assertion there exists because its failure is otherwise silent.

---

## 2. `ClaudeScope` — the ordering invariant is GONE (Phase 3)

Source: `src/ClaudeForge/Adapters/ClaudeScope.cs`.

This section used to document a hard invariant: `_cache` was an array indexed by
`(int)scope`, so its entries had to appear in `ConfigScope` numeric order, and getting
it wrong made `For(ConfigScope.User)` silently return another scope's wrapper.

**That invariant no longer exists.** `_cache` is now a dictionary built from
`ConfigScope.All`, so a scope cannot be mis-mapped by reordering, and a scope added to
the ladder is wrapped automatically instead of falling off the end of a hand-maintained
array:

```csharp
private static readonly Dictionary<ConfigScope, ClaudeScope> _cache =
    ConfigScope.All.ToDictionary(scope => scope, scope => new ClaudeScope(scope));
```

`ToLibraryPriority` likewise derives from `ConfigScope.All.Count - 1` rather than the
literal `3`, so extending the ladder no longer pushes every priority off by one.

Nothing here needs to stay in step with anything else by hand. Kept as a section only
because the invariant was load-bearing for long enough that its absence is worth stating
— if you remember the rule, it is gone.

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
in `tests/AgentForge.Core.Tests/Settings/MergeEngineTests.cs` (or wherever
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
| Disk I/O (`LoadAsync` / `SaveAsync`)     | `src/AgentForge.Core/FileIO/ConfigFileLoader.cs` (search for `LoadAsync`)                                                                                                                                                                                                                                                         |
| Workspace adapter for the editor library | `src/ClaudeForge/Adapters/ClaudeWorkspaceAdapter.cs`, `ClaudeValueAdapter.cs`                                                                                                                                                                                                                                                                      |
| Editor base class                        | `src/LayeredEditors.ViewModels/PropertyEditorViewModel.cs`                                                                                                                                                                                                                                                                                |
| App-shim editor base                     | `src/ClaudeForge/ViewModels/Editors/PropertyEditorViewModel.cs`                                                                                                                                                                                                                                                                                    |
| Top-level orchestration                  | `src/ClaudeForge/ViewModels/MainWindowViewModel.cs` (`OnAnyWorkspaceChanged`, `ComputeHasActualChanges`)                                                                                                                                                                                                                                           |
| Compound-editor contract                 | [`src/ClaudeForge/ViewModels/Editors/AGENTS.md`](../../ClaudeForge/ViewModels/Editors/AGENTS.md)                                                                                                                                                                                                                                                   |
| Backup / restore strategy                | `src/AgentForge.Core/Backup/BackupEngine.cs` — `ShouldSkipHomeSubdir` excludes `backups/`, `cache/`, `downloads/`, `statsig/`, `shell-snapshots/`, `local/` always; `projects/` only in non-Full modes. Add new exclusions there and add a matching `CreateAsync_ExcludesXxx` test in `tests/AgentForge.Core.Tests/Backup/BackupEngineTests.cs`. |

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
  `tests/AgentForge.Core.Tests/Settings/MergeEngineTests.cs`.
