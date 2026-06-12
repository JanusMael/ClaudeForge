# ClaudeForge.Sdk — Agent Operational Guide

Cross-file invariants and "if you do X also touch Y" rules for the SDK layer.
Read alongside the root [`AGENTS.md`](../../AGENTS.md).

---

## §1 What the SDK IS and what it has

The SDK wraps `SettingsWorkspace` + `SchemaRegistry` behind a typed,
thread-safe interface. It has:

| Capability                                      | Where it lives                                                       |
|-------------------------------------------------|----------------------------------------------------------------------|
| Workspace I/O (load, save, reload)              | `ClaudeConfigClientCore.OpenAsync` / `SaveAsync` / `ReloadAsync`     |
| Sync read/write                                 | `GetEffective` / `SetValue` / `RemoveValue` in `ClaudeConfigClientCore.cs` |
| Async read/write (cancellable, single-lock)     | `GetEffectiveAsync` / `SetValueAsync` / `RemoveValueAsync`; `SetValueIfChangedAsync` = atomic, **scope-specific** ghost-guard (writes only if the value at the target scope changes) |
| Schema content tree (`SchemaNode` objects)      | cached in `_cachedSchemaNodes` after `OpenAsync`                     |
| Text search over schema content                 | `SearchSchema` in `ClaudeConfigClientCore.cs`                        |
| Typed accessors (Permissions, Hooks, MCP, etc.) | `Permissions/`, `Hooks/`, `McpServers/`, `Marketplaces/`, `Plugins/` |
| `Changed` event                                 | fires after every mutation / save / reload                           |
| Backup / restore                                | `Backup/BackupClient.cs` + Core `BackupEngine`                       |

## §2 What the SDK does NOT have

**The SDK has NO navigation tree, NO page hierarchy, NO `NavigationNodeViewModel`.**
Those are Avalonia UI concerns and live in `src/ClaudeForge/ViewModels/`.

The rule:

- Schema hierarchy (schema nodes, JsonPaths, descriptions) → **SDK owns it**
- Navigation hierarchy (which page hosts which properties, deep-link targets) → **App layer owns it**

Consequence for search: `SearchSchema` returns `SchemaSearchResult` with `JsonPath`
and display metadata, but no navigation target. Callers that need to navigate to
an editor page must maintain their own `JsonPath → NavigationNodeViewModel` map
(see `src/ClaudeForge/ViewModels/AGENTS.md §5`).

## §3 Key files

| File                        | Role                                                                     |
|-----------------------------|--------------------------------------------------------------------------|
| `IClaudeConfigClient.cs`    | Public interface; the contract consumers depend on                       |
| `ClaudeConfigClientCore.cs` | Abstract base; all shared implementation                                 |
| `ClaudeCodeClient.cs`       | Claude Code concrete; overrides `DiscoverFiles`, `IsClaudeCode=true`     |
| `ClaudeDesktopClient.cs`    | Claude Desktop concrete; overrides `DiscoverFiles`, `IsClaudeCode=false` |
| `SchemaSearchResult.cs`     | Return type of `SearchSchema`                                            |
| `Models/IModelCatalogAccessor.cs` | `IClaudeConfigClient.Models` — allowed `model`/`effortLevel`/`permissions.defaultMode` values + their relationships (which efforts a model supports, auto-mode gating) + the nearest-analog coercion rule. Backed by Core's bundled `model-catalog.json`; shared via `ModelCatalogProvider.Default`. The relationship/coercion logic is domain code here (not in any view-model) so it's CLI/MCP-usable. See [docs/MODEL-CATALOG.md](../../docs/MODEL-CATALOG.md). |

## §4 `preLoadedWorkspace` injection — migration artifact

`ClaudeConfigClientCore(ConfigScope, SchemaRegistry?, preLoadedWorkspace?)` accepts
an already-loaded `SettingsWorkspace` so the GUI's `MainWindowViewModel` can share
one workspace object with the SDK client during the in-progress SDK-migration pass.

**When to remove it:** after `SettingsGroupEditorViewModel` + the full editor
pipeline migrates off direct `workspace.SetValue` / `GetLayeredValue` calls to
SDK accessors, the parameter and `InternalsVisibleTo("ClaudeForge")` become unused.
A TODO comment near the constructor in `ClaudeConfigClientCore.cs` marks the
removal site — `grep` for it when the migration completes.

Note: clients created via `FromExistingWorkspace` have a pre-loaded workspace but
**skip** `OpenAsync`. Calling `OpenAsync` on such a client overwrites the
workspace with a freshly discovered one — do this only when a full reload is
intended (e.g. `ReloadAsync` internally does this correctly).

## §5 `_suppressForwarder` + `_selfWriting` dual guard

`_suppressForwarder` (in `ClaudeConfigClientCore.cs`) pairs with `_selfWriting`
(in `src/ClaudeForge/ViewModels/SettingsGroupEditorViewModel.cs`) to prevent
the feedback loop:

```
SDK.SetValue → workspace.Changed → OnWorkspaceChanged → SDK.Changed
             → SettingsGroupEditor reloads → editor calls SDK.SetValue → ...
```

Contract:

- `_suppressForwarder` must be set **before** `_workspace.SetValue(...)` and cleared
  in `finally`. The workspace event fires synchronously inside `SetValue`.
- `_selfWriting` must be set **before** the editor's own `_workspace.SetValue(...)` call.
- If you add a new code path that calls `workspace.SetValue` inside any `Changed` handler,
  replicate the guard or you will get an infinite loop.
- Regression tests: `tests/ClaudeForge.Tests/ViewModels/SettingsGroupEditorViewModelTests.cs`
  and `tests/ClaudeForge.Tests/ViewModels/ApplyToWorkspaceDeadlockRegressionTests.cs`.

## §6 `Changed` event threading — MUST marshal to UI thread

`Changed` fires on whatever thread triggered the change:

- `SetValue` / `RemoveValue` — called on whatever thread the caller uses
- `workspace.Changed` forwarder — fires synchronously on the mutation thread
- `SaveAsync` / `ReloadAsync` — fires on the `await` continuation thread
- `SetValueAsync` / `RemoveValueAsync` / `SetValueIfChangedAsync` — raise `Changed` **after the state lock is released**, on the `await` continuation thread, and **only when a write actually occurred** (a no-op conditional write / absent-key remove raises nothing)

**Avalonia bindings require `PropertyChanged` to fire on the UI thread.**
`MainWindowViewModel.OnSdkClientChanged` MUST dispatch to
`Dispatcher.UIThread.Post(...)` when not already on the UI thread.
See `src/ClaudeForge/ViewModels/MainWindowViewModel.cs` `OnSdkClientChanged`.
A test for this contract is in `tests/ClaudeForge.Tests/ViewModels/HasUnsavedChangesRecheckTests.cs`.

## §7 `_cachedSchemaNodes` — populated by Open/Reload

`ClaudeConfigClientCore._cachedSchemaNodes` is:

- **Null** before `OpenAsync` is called.
- **Populated** inside `OpenAsync` and `ReloadAsync` — both fetch the schema root via
  `_schemaRegistry.GetClaudeCode/DesktopSettingsNodeAsync` (memory-cached after first
  call, so cheap on reload) and then call `SchemaTreeBuilder.BuildTopLevel`.
- **Thread-safe read**: `SearchSchema` snapshots the reference under the state lock,
  then walks the (immutable) node list without holding the lock.
- **`SearchSchema` returns `[]`** (not throws) if called before `OpenAsync`.

## §8 Test seams

| Seam                                                                 | How to use                                                            |
|----------------------------------------------------------------------|-----------------------------------------------------------------------|
| `internal ClaudeCodeClient(ConfigScope, SchemaRegistry)`             | Inject a test-controlled `SchemaRegistry`                             |
| `ClaudeCodeClient.FromExistingWorkspace(workspace, scope, registry)` | Supply pre-built workspace (GUI migration tests)                      |
| `PlatformPaths.TestUserProfileOverride = sandbox`                    | Redirect `~/.claude/` to a temp dir                                   |
| `DebugFlags.ResetForTesting()`                                       | Reset all debug flags + `PlatformInfo.Current` in `[TestCleanup]`     |
| `InternalsVisibleTo("ClaudeForge.Tests")`                            | In `ClaudeForge.Sdk.csproj` — grants access to all `internal` members |

`SearchSchema` does not have its own seam; the integration test pattern is:
set `TestUserProfileOverride`, construct `ClaudeCodeClient()`, call `OpenAsync(null, ct)`,
then assert on `SearchSchema` results. The bundled schema is always used (no HTTP call needed).

**Async-method contract template.** `tests/ClaudeForge.Sdk.Tests/ClaudeConfigClientAsyncTests.cs`
is the reference every future async SDK method must mirror. It locks: cancellation
(a pre-cancelled token throws `OperationCanceledException` with no partial mutation),
lock-release-on-throw (an in-lock failure doesn't leak the lock → no deadlock),
`ConfigureAwait(false)` (no deadlock when a non-pumping `SynchronizationContext` is
captured), atomic compare-and-set under contention (`SetValueIfChangedAsync` — exactly
one of N racing writers commits), and `Changed` fires once per real write / never on a
no-op. The scope-specific compare basis is exercised with a Managed-shadows-User workspace.
