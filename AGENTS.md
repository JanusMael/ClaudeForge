# AGENTS.md — operational rules for LLM contributors

> Audience: an agent (Claude or otherwise) returning to ClaudeForge cold.
> Purpose: surface cross-file contracts that aren't visible from a single-file
> read, so you don't break invariants you can't see.
> Methodology rationale: see [`AGENT-ONBOARDING.md`](./AGENT-ONBOARDING.md).
> Narrative architecture and prose context live in [`CLAUDE.md`](./CLAUDE.md).

This file is **fact-shaped**: every claim cites a file path, function name, or
test name. If a fact here is wrong, grep for the identifier — code drift will
surface as a missing or relocated symbol, not as silently-stale prose.

Two specific anti-patterns this file refuses on principle:

- **No hardcoded source-line numbers** (`Foo.cs:245`). They drift on every
  refactor and turn the doc into a liar. Cite the file, the type, the method,
  or `nameof()` — let `grep` do the locating.
- **No timestamps in prose** ("Reported 2026-05-13", "shipped 2026-05-07").
  `git log` and `git blame` are the authoritative source for when a thing
  happened; carrying the date in prose adds maintenance debt with no
  corresponding benefit.

---

## 1. Hard invariants

| Invariant | Failure signature if you break it | Canonical source |
|-----------|-----------------------------------|------------------|
| **Compound editors must use the force-fire `MarkModified()` pattern**, never bare `IsModified = true`. CommunityToolkit.Mvvm's `[ObservableProperty]` setter elides equal assignments, so a bare assignment when the flag is already `true` (e.g. after `LoadFromLayered` set it for an already-populated scope) is a no-op and the live-write / Save-button-enable chain never runs. | User edits or removes an item on a loaded compound editor → Save button stays disabled. Or: user edits a property whose backing field was already populated → no live-write to disk. | Helper: `MarkModified()` in `McpServersEditorViewModel`, mirrored in `HooksEditorViewModel`, `PermissionsEditorViewModel`, `EnabledPluginsEditorViewModel`, `MarketplacesEditorViewModel`. Sidecar: [`src/ClaudeForge/ViewModels/Editors/AGENTS.md`](./src/ClaudeForge/ViewModels/Editors/AGENTS.md). |
| **`ConfigScope` is a struct, and two of its semantics are invisible to the compiler.** (1) `default(ConfigScope)` MUST stay `Managed` — it is backed by a single ordinal for exactly this reason, and a dozen editors declare `private ConfigScope _lastScope;` with no initialiser. (2) `ToString()` MUST keep returning the old enum member names; it is consumed as data, not displayed. Also: it cannot be a default parameter value or a `case` label — use an overload and `when` guards. | A richer struct shape (e.g. a record of `Id`/`Priority`/`DisplayName`/`IsReadOnly`) compiles, passes **2,791 of 2,792 tests**, and silently changes what an uninitialised scope means. Breaking `ToString()` silently breaks scope brushes, tooltips and labels across the editors. | Type: `src/AgentForge.Core/Settings/ConfigScope.cs`. Guard: `tests/AgentForge.Core.Tests/Settings/ConfigScopeTests.cs`. Sidecar: [`src/AgentForge.Core/Settings/AGENTS.md`](./src/AgentForge.Core/Settings/AGENTS.md). |
| **A guard's comment is not evidence the guard covers what it says.** `_reloadPending` claimed to "prevent concurrent calls to `LoadAllWorkspacesAsync`"; it guards `ReloadCoreAsync`, one of three callers. Serialisation now lives in `LoadAllWorkspacesAsync` itself. When a concurrency invariant matters, put it in the method performing the destructive change — not in each caller. | A use-after-dispose race (`ObjectDisposedException` from the nav-tree build) survived for years because the test written to cover it named the wrong guard AND could not fail. `OpenProjectAsync` sets `IsLoading` without checking it and awaits a dialog first, so a file-watcher reload starts underneath it. | Type: `MainWindowViewModel.LoadAllWorkspacesAsync` — read its remarks before changing it; **serialise, never coalesce** (a joined load never opens the new `ProjectRoot`) and **chain, never lock** (the load path can re-enter). Guard: `ReloadHardeningTests`. |
| **`Session.Dispatch(async () => …)` in a headless test CANNOT FAIL.** It binds `Dispatch<T>(Func<T>)` with `T = Task`, yielding `Task<Task>` whose inner task is never awaited. Return a value from the lambda so it binds `Dispatch<T>(Func<Task<T>>)`, then canary with `Assert.Fail`. Non-`async` lambdas are fine — they bind `Dispatch(Action, ct)`. | 19 tests were inert from the day they were written and hid **two real defects**, including a data-loss path where an unparseable config was swapped into memory and then saved over. | Guards: `tests/ClaudeForge.Tests/Headless/*`. Working pattern: `ExportArchiveTests`. |
| **The scope ladder belongs to the product, and `ScopeLadder.Default` must stay encoded as a `null` field inside `ConfigScope`.** A product supplies its ladder via `AgentConfigClientCore.Scopes`; `ClaudeConfigClientBase` returns `ScopeLadder.Default` itself rather than a copy, because ladder identity is by instance. Also: `ScopeLadder._isDefault` is a field on purpose — `ReferenceEquals(this, Default)` is `false` during `Default`'s own construction. | A private copy of Claude's four rungs yields scopes that compare unequal to `ConfigScope.User` at ~1,100 sites. The `ReferenceEquals` form makes `ConfigScope.All`'s scopes unequal to `ConfigScope.Managed`, and `ClaudeScopeTests` does **not** notice — its cache is built from `ConfigScope.All`, so it stays self-consistent while wrong. | Type: `src/AgentForge.Core/Settings/ScopeLadder.cs`. Guards: `ScopeLadderTests`, `ProductScopeLadderTests`. |
| **Product-neutral code must ask `ConfigScope.IsReadOnly`, never compare against `ConfigScope.Managed`.** A second product's ladder can have more than one policy rung — OpenCode has managed *and* macOS MDM. | A settings value set by an unrecognised policy scope reads as user-editable: the UI offers to change it and the change never takes effect. | `LayeredValue.IsManagedLocked`, `AgentConfigClientCore.EditableScopes`. Claude-specific code (the discoverer, the scope legend, the converters) may still name `Managed` freely. |
| **`_suppressStateSave` latch must be set BEFORE `Shutdown()` in `ClearAppData`**. Otherwise `OnClosed → SaveWindowState` re-creates the file `WindowStateService.Delete()` just removed. | User clicks Clear App Data → app exits → next launch reads the freshly re-saved file instead of clean defaults. | Latch declared on `MainWindowViewModel`. Set in `ClearAppData` (must precede `WindowStateService.Delete()` and the `Shutdown()` call). |
| **`PlatformInfo.Current` for UI / display branches; `OperatingSystem.IsWindows()` (or `RuntimeInformation.IsOSPlatform`) for platform-intrinsic APIs (registry, MSIX, env-var Machine scope)**. Emulation flags `--windows` / `--macos` / `--linux` swap `PlatformInfo.Current` but cannot make Windows registry calls work on Linux, so platform-intrinsic call sites must keep using the real-OS check. | Running on Windows with `--linux` shows Windows install commands instead of Linux ones (UI used real-OS check). Or: registry call attempted on Linux because the call site went through `PlatformInfo.Current`. | Abstraction: `src/AgentForge.Core/Platform/PlatformInfo.cs` (`PlatformInfo.Current`, `RuntimePlatformInfo`, `EmulatedPlatformInfo`). Decision tree: [`PLATFORM.md`](./PLATFORM.md). |
| **`WindowStateService.StatePath` is a property, not `static readonly`**. Tests mutate `PlatformPaths.TestUserProfileOverride` between runs; a cached path captures the host's real `%USERPROFILE%` at type-init and bypasses the sandbox forever after. | Tests touch `~/.claude/cache/ClaudeForge-gui-state.json` on the developer's real machine instead of the per-test sandbox. | `src/ClaudeForge/Services/WindowStateService.cs` — must declare `private static string StatePath =>` (the `=>`, NOT `=`). |
| **Every `DataTemplate` and `UserControl` in AXAML sets `x:DataType`**. Compiled-binding mode requires it; reflection-based binding triggers IL2026 warnings under `PublishTrimmed=true`. **It is also the strongest correctness guard available for AXAML in this repo**: with it, a renamed or mistyped bound member is a **build error** (`AVLN2000: Unable to resolve property or method of name 'X' on type 'Y'`) instead of a control that silently renders nothing. One attribute buys what no unit test here can — the headless app is deliberately stripped of the App's resource dictionaries, so views cannot be instantiated in tests. | `dotnet publish -c Release` emits IL2026 warnings; bindings silently fail at runtime in trimmed Release builds. Without it, a view-model rename compiles clean and the surface is empty at runtime. | Convention from `CLAUDE.md` "Key conventions". Trim-warning baseline: [`TRIMMING.md`](./TRIMMING.md). Verified both directions 2026-08-18 on `BackupRestoreView.axaml`'s per-product checkbox template. |
| **`ProductDescriptor.ArchiveFolder` is a PERSISTED value, and both sides of the archive now read it.** It names the top-level folder a product's files occupy inside a backup zip *and* the string listed in that archive's `manifest.clients`. Changing an existing product's value moves the writer and the reader together, so it stays self-consistent — new archives work perfectly while every archive already on a user's disk stops being recognised. | Restore silently finds nothing for that product; the restore browser shows a raw unrecognised client name in its column. No exception, no failing assertion. | Values: `SchemaRegistry.ClaudeCodeProduct` / `ClaudeDesktopProduct`. Guards: `BackupEngineTests.ArchiveFolderNames_AreTheValuesAlreadyOnUsersDisks` (+ the two `manifest.clients` writer tests) — they look tautological on purpose. |
| **Every `JsonSerializer.Serialize/Deserialize` call uses a source-generated context (`AppJsonContext` or `CoreJsonContext`)**. Reflection-based overloads (`JsonSerializer.Deserialize<T>(json)`) emit IL2026 warnings and break in trimmed/AOT builds. | IL2026 warnings; runtime `MissingMetadataException` in published builds. | Contexts: `src/ClaudeForge/Services/AppJsonContext.cs`, `src/AgentForge.Core/Schema/CoreJsonContext.cs`. Existing pattern: `WindowStateService.Load/Save` uses `AppJsonContext.Default.WindowState`. |
| **`JsonArray.Add(...)` calls cast the value to `(JsonNode?)` before passing.** The generic `Add<T>(T)` overload is `RequiresUnreferencedCode`; overload resolution picks the generic over the non-generic `Add(JsonNode?)` when the argument is a concrete `JsonValue` / `JsonObject`. Cast forces the safe overload. | IL2026 errors in trim publish; the same call sites we hit on the Hooks / MCP / Marketplaces / Permissions / Essentials editors. | Pattern: `arr.Add((JsonNode?)JsonValue.Create(s))`. Documented: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Trim safety" section. |
| **Tooltips set on a parent `Border` must ALSO be set on inner `TextBlock`s the user is likely to hover.** Avalonia tooltip resolution does not walk up the visual tree; child controls without tooltips show nothing on hover. | User hovers a badge / icon and sees no tooltip; only the empty padding fires. | Documented in `PropertyEditorWrapper.axaml`'s scope-badge ("set on BOTH") and applied to NEW badge + nav-tree icons in `MainWindow.axaml`. Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Tooltips don't propagate". |
| **`Orientation="Horizontal" StackPanel` does NOT constrain its children's width** — `TextWrapping="Wrap"` on a child `TextBlock` will not wrap. Use `DockPanel LastChildFill="True"` with the bullet / icon docked Left and the wrappable text in the fill slot. | Long bullet text overflows the right edge of the panel. | Documented: WelcomeView's bullet rows. Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "TextWrapping never engages". |
| **Computed `bool IsXyz => predicate()` properties bound from AXAML are unreliable on Linux.** Avalonia compiled bindings sometimes fail to re-evaluate the getter on manual `OnPropertyChanged(nameof(IsXyz))` notifications. Use an `[ObservableProperty]`-backed field with a `RecomputeIsXyz()` helper called from value-changed partials instead. | Visual state stuck stale until a workspace reload — reproduced on the Essentials page's danger banner before the conversion. | Pattern: `EssentialsCardViewModel.IsDanger` / `RecomputeIsDanger`. Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Compiled bindings don't reliably re-evaluate". |
| **A property a Style sets must NOT also be set as an attribute on the element.** Avalonia precedence is Animation > **LocalValue** > Style; an attribute is a LocalValue and outranks EVERY Style setter regardless of selector. So a conditional `Classes.foo="{Binding Bool}"` cannot work if the element also writes that property inline — put the default in a style too. Related: a control's `Styles` apply to its **descendants, not to itself**, so hoist class selectors to the parent. | A class-driven visual (e.g. the deep-link orange frame on the property-filter box) never appears even though the flag is true and the selector matches. Both traps can be present at once and mask each other. | Correct shape: `Border.filter-frame` / `.filter-frame.nav-filter` in `GroupPropertiesView.axaml`; working reference `Button.hint-segment` / `.active` in `GuidedRuleBuilderView.axaml`. Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Styling / theming". Token policy: [`docs/UI-STYLE-GUIDE.md`](./docs/UI-STYLE-GUIDE.md) §2. |
| **A `DataTemplate` for a SUBCLASS view-model must be declared BEFORE the base type's template.** Templates match in declaration order and a base-type template also matches derived instances, so the base wins if it comes first. | A new derived editor VM silently renders with the generic base template (e.g. the `model` picker falling back to the raw-id enum box). | `PropertyEditorWrapper.axaml`: `ModelPropertyEditorViewModel` template sits immediately above `EnumPropertyEditorViewModel`. Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Templates / controls". |
| **Virtualization is per items-host and needs a bounded viewport — it does NOT reach into a nested items host.** A virtualized outer row whose template contains a plain `ItemsControl` realizes that inner list in FULL. For large nested collections, gate them behind a collapsed section whose `ItemsSource` is **empty while collapsed** (`IsVisible="False"` still realizes the subtree). | Page takes seconds to open despite a virtualized list and cached VMs — `env`'s ~305 declared vars built 306 `PropertyEditorWrapper`s (~4.4 s), while `Advanced`'s 94 *top-level* editors realized only 7 and opened instantly. | Lazy gate: `ObjectPropertyEditorViewModel.VisibleChildren` + `PropertyCategoryViewModel`. Regression probe: the `[PropView.Realized] group=… wrappers=N` trace in `GroupPropertiesView.axaml.cs` (a healthy page realizes a screenful; hundreds = something is eagerly building a subtree). Gotcha entry: [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) "Virtualization / perf". |
| **`SettingsDocument.HasActualChanges` and `JsonDiff.Compute` MUST agree on what counts as a user-visible change.** Both strip the tool-managed `"//"` header-comment key (timestamp marker). If they diverge, `HasUnsavedChanges` can report dirty while the per-property dialog has nothing to show — silent-save bug. | Save button enabled but no save-changes dialog appears; rolling log shows `[Save] dialog gate: summaryNull=True ... hasUnsaved=True`. | Strip site: `SettingsDocument.HasActualChanges` (`DeepEqualsIgnoringMetadata`). Mirror site: `JsonDiff.Compute` (top of file comment block). Safety net: `MainWindowViewModel.SaveCoreAsync` falls back to a generic confirmation dialog if the two ever disagree again. |
| **`SensitiveKeys.IsSensitive` checks PATH SEGMENTS, not the full dotted path string.** Anything under `env`, `headers`, `credentials`, `auth`, `authorization` is redacted regardless of nesting depth. The substring pass (token / secret / password / apikey / api_key / api-key / bearer) is the secondary catcher for keys NAMED with secret-bearing terminology outside a known section. | A nested secret-bearing key (e.g. an MCP server's `headers.Authorization`) leaks into the rolling log if a callsite uses full-path exact-matching. | Source: `src/AgentForge.Sdk/Diagnostics/SensitiveKeys.cs` `_segmentExact` set + per-segment loop. Tests: `tests/AgentForge.Sdk.Tests/Diagnostics/SensitiveKeysTests.cs` covers a representative set of nested-path cases. |
| **`OnPropertyChanged(nameof(AvailableProfileEntries))` from `LoadAllWorkspacesAsync`, `OnProfileApplied`, `OnProfileDeleted`, AND `OnProfileCreated` MUST be wrapped in `_suppressProfileChangeReload`.** The toolbar ComboBox's TwoWay-bound `SelectedItem` (→ `SelectedProfileEntry`) can write back the freshly-resolved record reference when ItemsSource refreshes; the setter feeds `SelectedProfile`, which fires `OnSelectedProfileChanged` → `_ = ReloadAsync()`. Without the suppression flag the app spins in a reload loop (inside `LoadAllWorkspacesAsync`) or produces a redundant queued reload (inside `OnProfileApplied`). Sibling rule: **VM `Refresh()`/`RefreshAsync()` methods called from `BuildNavigationTree` must offload heavy IO to the thread pool** so the dispatcher stays responsive across a reload. Three currently-correct examples: `BackupRestoreViewModel.RebuildBackupListAsync`, `MemoryEditorViewModel.RefreshAsync`, `EssentialsViewModel.UpdateEnvSourceLabelsAsync`. | Switching to a fresh profile produces a permanently-spinning reload (`[Profiles] After load` + `[Schema] Post-reload validation` loop) and a debugger pause catches the active frame deep inside `UserMemoryService.ReadFirstNonEmptyLine` on the dispatcher thread. | Flag: `MainWindowViewModel._suppressProfileChangeReload`; set/cleared around the AvailableProfileEntries notifications in `LoadAllWorkspacesAsync` and the `OnProfileApplied` callback; checked in `OnSelectedProfileChanged`. Regression tests: `RefreshAsync_RunsTier1ScanOnThreadPool` (Memory), `RefreshAsync_RunsEnvProbeOnThreadPool` (Essentials) — both assert via `Thread.CurrentThread.IsThreadPoolThread` at the moment IO is invoked. |
| **`IsLoading`-style re-entry guards on `[ObservableProperty]`-bound VMs MUST scope tightly around the synchronous property assignment, NEVER span an `await`.** Pattern: `IsLoading = true; <read + assign properties>; IsLoading = false; <THEN await any IO>`. Reason: bound editors (NumericUpDown, TextBox, CheckBox) call the OnXChanged partial method when the user mutates the property; if `IsLoading` is still true because a read is awaiting an IO continuation, the partial method short-circuits and the user write is silently dropped — appears in the UI but never reaches the SDK, doesn't surface on the save-changes dialog, and reverts on the next reload. Currently-correct site: `EssentialsViewModel.ReadEnvIntAsync` (sync `IsLoading=true/false` around `card.IntValue = …`, then `await UpdateEnvSourceLabelsAsync` OUTSIDE the guard). | User types into a numeric editor; spinner accepts the value but Save dialog shows nothing and the value vanishes on next reload. | Regression test: `IntValueWrite_NotSuppressed_WhileReadIsInAsyncPhase` in `EssentialsViewModelTests` — uses a `ManualResetEventSlim`-gated env provider to keep `UpdateEnvSourceLabelsAsync` suspended at its Task.Run, then writes IntValue and asserts the SDK reflects it. Fails if anyone widens the guard scope back over the await. |
| **`SettingsGroupEditorViewModel.ApplyToWorkspace` MUST gate flush on `_userEditedPaths`, NOT on `editor.IsModified`.** Compound editors set `IsModified=true` at load time whenever their scope has data (per the editor sidecar contract — that's how the Save button knows the scope is non-empty), so an IsModified-only gate flushes every loaded editor's in-memory snapshot back to the SDK on every save. When ANOTHER view-model (Essentials, the Environment top-level VM, future SDK consumers) writes to the same top-level key out-of-band, the flush clobbers it. `_userEditedPaths` is populated only by `OnEditorPropertyChanged` — which fires only on post-load user mutations because subscription happens AFTER `LoadFromValue` in `RebuildEditors` — and is cleared on every rebuild. This restricts the flush to its actual safety-net role: re-applying user edits whose live-write may have failed. **Edge case:** `_userEditedPaths` is cleared on EVERY `RebuildEditors`, including the one fired by scope-change (`OnEditingScopeChanged` → `RebuildEditors`). A user who edits + sees a live-write fail + changes scope before clicking Save will lose the failed write (the safety-net flush has nothing to retry). Currently considered acceptable because live-write failures are themselves rare; document explicitly if you ever widen the failure surface. | Typing into an Essentials card → save dialog showed only unrelated changes, value vanished on reload. Log line `[Editor.Flush] writing path 'env'…` from the Environment group editor revealed it was flushing its load-time env snapshot back over the Essentials write. Same shape would clobber out-of-band writes to `permissions`, `mcpServers`, `hooks`, `enabledPlugins`, `extraKnownMarketplaces`, `preferences`, etc. | Set declared on `SettingsGroupEditorViewModel` as `_userEditedPaths`; populated at the top of the IsModified branch in `OnEditorPropertyChanged`; cleared at the top of `RebuildEditors`; checked in `ApplyToWorkspace`. Regression test: `ApplyToWorkspace_DoesNotClobberOutOfBandWrites_OnUntouchedEditors` — loads a group editor over a workspace with existing data, writes to the same path via the workspace directly (simulating an out-of-band SDK write), calls `ApplyToWorkspace`, asserts the out-of-band write survives. Permanent companion audit log: `[Editor.UserEdit]` emitted on every user-driven mutation through the live-write path, with values routed through `FormatValueForAuditLog` so sensitive paths (env, headers, …) are redacted and compound values are summarised structurally (see the redaction-classifier invariant below). |
| **Centre status-bar emissions MUST use the typed `SetStatusActive` / `SetStatusSuccess` / `SetStatusWarning` / `SetStatusFailure` / `SetStatusState` helpers on `MainWindowViewModel`.** Writing to the legacy `StatusMessage` setter still compiles (it's an alias kept for older tests / a couple of doc examples) but routes the value to `StatusKind.State` — gray plain text, no icon, no auto-clear, no × dismiss button. A new failure emitted via the legacy setter renders looking exactly like "Ready" / "Project: foo" — the visual urgency the user needs is silently lost. The five typed helpers force the caller to classify severity at the callsite so the View can render the matching pill (green ✓ / amber ⚠ / red ✗ with × / blue …) and the auto-clear / dismiss lifecycle works correctly. | A `Save failed: Access denied` message added via `StatusMessage = "Save failed: …"` renders as quiet gray text instead of the red dismissible pill the user expects; the error blends into background chrome and can be missed. | Helpers: `SetStatusActive` / `SetStatusSuccess` / `SetStatusWarning` / `SetStatusFailure` / `SetStatusState` on `MainWindowViewModel`. Substrate: `src/AgentForge.Avalonia.Shell/Status/StatusController.cs` + `StatusKind.cs` — **product-neutral as of Phase 5 slice 1**; the typed helpers stay on `MainWindowViewModel`, only the substrate moved. Lock: `tests/ClaudeForge.Tests/ViewModels/Status/StatusControllerTests.cs` — pins each kind's lifecycle (auto-clear for Success / Warning, sticks-until-dismiss for Failure, replace-cancels-pending). |
| **Config saves go through `IConfigWriter`, and the writer is chosen at the composition point — never read from `DebugFlags` inside Core.** `ConfigFileLoader.SaveAsync` defaults to the comment-preserving `JsoncEditWriter`; `--writer legacy` selects `LegacySerializingWriter`. `DebugFlags` lives in the **app** assembly and `AgentForge.Core` must never reference it, so the flag is parsed in the app, resolved to an instance in `MainWindowViewModel.SelectedConfigWriter()`, and threaded through the SDK client constructor. ⏳ **`--writer legacy`, `SelectedConfigWriter`, and `LegacySerializingWriter` are a ONE-RELEASE hatch — delete all three after one clean release.** Two writers means every save-path change must be correct twice, and the lossy one is the one nobody remembers to test. | Reading `DebugFlags` from Core is a compile error today and would invert the layering if "fixed" by adding a reference. Letting the hatch persist: a comment-destroying writer stays reachable indefinitely, and the next save-path change silently regresses only under the flag. | Contract + rationale: [`docs/JSONC-WRITER.md`](./docs/JSONC-WRITER.md). Interface: `src/AgentForge.Abstractions/Configuration/IConfigWriter.cs`. Selection: `MainWindowViewModel.SelectedConfigWriter()`. Flag: `DebugFlags.ConfigWriterName`. Lossiness is asserted, not assumed, by `ConfigFileLoaderPreservationTests.LegacyWriter_StillReSerializes_SoTheContrastIsExplicit`. |
| **`JsonRedactor.IsSensitiveKey` (Core) and `SensitiveKeys.IsSensitive` (Sdk) MUST agree on every single-segment key.** They're parallel classifiers — duplicated because `AgentForge.Core` can't reference `AgentForge.Sdk` per the layering contract — and they back three different redaction surfaces: the audit-log live-write (Sdk), the save-diff log (Sdk via `WorkspaceDiagnostics`), and the `BackupMode.Sanitized` JSON walker (Core). The `RedactedMarker` literal (`"[redacted]"`) MUST be identical too so log greps / report templates / support workflows don't have to handle two different placeholders. If you add a new sensitive-token to one side and forget the other, one redaction surface starts leaking secrets the others scrub. | A new sensitive-key name (e.g. `"clientCertificate"`) added to `SensitiveKeys._segmentExact` but not mirrored in `JsonRedactor.SegmentExact` → audit logs scrub `clientCertificate` values but sanitized backups emit them verbatim. | Sources: `src/AgentForge.Sdk/Diagnostics/SensitiveKeys.cs` (`_segmentExact`, substring list, `RedactedMarker`), `src/AgentForge.Core/Backup/JsonRedactor.cs` (`SegmentExact`, `SubstringTokens`, `RedactedMarker`). Drift guard: `tests/AgentForge.Sdk.Tests/Diagnostics/SensitiveKeysParityTests.cs` runs a representative sample through both classifiers and asserts identical answers + identical markers. |
| **`[Editor.UserEdit]` / `[Editor.Flush]` audit-log emission of editor values MUST route through `SettingsGroupEditorViewModel.FormatValueForAuditLog`.** Direct `value?.ToJsonString()` calls leak secrets: the `env` group editor's value is the WHOLE env JSON object (which contains `ANTHROPIC_API_KEY` and friends), and compound editors like `mcpServers` / `hooks` nest secret-bearing keys (`headers.Authorization`) one level below where `SensitiveKeys.IsSensitive(editor.Path)` can classify them from the top-level path alone. `FormatValueForAuditLog` enforces three rules: (a) path is sensitive per `SensitiveKeys` → emit `RedactedMarker` only; (b) value is `JsonObject` or `JsonArray` → emit shape+size summary (`"(JsonObject, 1234 chars)"`), NOT contents; (c) scalar leaf on non-sensitive path → emit `value.ToJsonString()`. The Save-time `WorkspaceDiagnostics.LogDiffs` is the complementary path — it uses `JsonDiff` to recurse and apply per-nested-leaf redaction, so the "what changed?" forensic detail is preserved at save time without ever inlining secrets at the audit-log layer. | Permanent audit-log trail that logs raw `value.ToJsonString()` leaks the entire env map into the rolling log file on the first edit. | Helper: `FormatValueForAuditLog` on `SettingsGroupEditorViewModel`. Regression tests in `SettingsGroupEditorViewModelTests`: `FormatValueForAuditLog_RedactsEnvPath`, `FormatValueForAuditLog_CompoundValue_ReturnsStructuralSummaryNotContents`, `FormatValueForAuditLog_LeafValue_LogsValueAsJson`, `FormatValueForAuditLog_NullValue_RendersExplicitNullToken`. |
| **`return Session.Dispatch(async () => { … })` makes a headless test PASS UNCONDITIONALLY.** The lambda binds to `Dispatch<TResult>(Func<TResult>, …)` with `TResult = Task`, so the call returns `Task<Task>`. `Task<Task>` *is* a `Task`, so `return`ing it compiles — but the test framework then awaits only the OUTER task, which completes the instant the lambda returns its inner task. Everything after the first `await` in the body, including every assertion, runs unobserved; exceptions are swallowed. `.Unwrap()` makes the failure propagate but then DEADLOCKS, because the dispatcher stops pumping once `Dispatch` returns, so the inner continuations never run. **Don't use `Session.Dispatch` for a body that awaits** — write a plain `async Task` test and construct the view-model directly (`NavigationHeaderClickTests`, `NavigationNodeIdTests`, `DeepPathReloadTests` all do this and assert for real). Verify any new headless test with a temporary `Assert.Fail` at the top of the body: if it still reports Passed, the test is inert. | A test that cannot fail. Provably: adding `Assert.Fail(...)` as the first statement of the lambda in `TransactionalReloadTests.LoadAllWorkspacesAsync_ValidReload_SwapsSdkClients` still reports **Passed**. | **Pre-existing scope: 19 test methods across `Headless/NavigationTreeWelcomeNodeTests.cs` (9), `Headless/ReloadHardeningTests.cs` (7), `Headless/TransactionalReloadTests.cs` (3) currently use this pattern and are therefore inert.** Not fixed here (unrelated to the change that found it, and un-inerting them will likely surface real failures) — needs its own pass. |
| **`WindowState.LastDeepPath` is persisted from the `MainWindowViewModel._lastDeepPath` FIELD — never computed inside `SaveWindowState`.** `SaveWindowState` has ~14 call sites and one is the tail of `OnSelectedNodeChanged`. During a reload, `RestoreSelectedNode` sets `SelectedNode` → `OnSelectedNodeChanged` → `SaveWindowState`, and the editor at that instant is freshly rebuilt and empty — so calling `IDeepNavigable.CaptureDeepPath()` there would overwrite the good path with an empty one BEFORE the async restore reads it. Capture only at the explicit points (`CaptureDeepPath(bool)` from navigate-away and from `ReloadCoreAsync`, the latter **outside** the `do/while (_reloadPending)` loop). | The user's in-page position silently stops being restored after any reload. Nothing throws, nothing logs an error, and the persisted `lastDeepPath` degrades to empty on every reload. | Field + rationale comment on `MainWindowViewModel._lastDeepPath`; capture helper `CaptureDeepPath(bool captureTransient)`. Regression test: `tests/ClaudeForge.Tests/Headless/DeepPathReloadTests.cs` → `Reload_DoesNotBlankAPersistedDeepPath`. |
| **A deep-link handler applies a filter via `ApplyNavigationFilter(...)`, never by assigning `FilterText`.** The latch inside it (`_applyingNavFilter`) is what tells `OnFilterTextChanged` this was navigation rather than a user edit, which in turn raises `FilterFromNavigation` and draws the orange "navigated" frame. A direct assignment reads as a user edit and silently skips the frame, so the user sees a mysteriously narrowed list with no explanation. Two view-models now implement this pair. | A deep-linked or reload-restored list is filtered but shows no navigated frame; the user can't tell why results are missing. | `SettingsGroupEditorViewModel.ApplyNavigationFilter` (original) and `AgentsSkillsEditorViewModel.ApplyNavigationFilter`. Tests: `AgentsSkillsFilterTests.ApplyNavigationFilter_FlagsNavigationThenUserEditClearsIt`, `DeepPathReloadTests.Reload_RevealsTheRestoredItemByFiltering_WithTheNavigatedFrame`. |
| **A view that binds a COMPUTED filtered projection must be told when the underlying collection is rebuilt.** Binding `ItemsSource` to a computed `Filtered*` property means the source `ObservableCollection`'s `Clear()`/`Add()` no longer reaches the UI — the collection the view watches is a new list each time the property is read. The rebuild has to raise `PropertyChanged` for the projection explicitly. | The list silently stops updating on refresh: a newly-created skill/agent never appears, and no error is logged. | `SettingsGroupEditorViewModel` raises `FilteredEditors` by hand for this reason; `AgentsSkillsEditorViewModel.NotifyFilteredListsChanged()` does the same after `FillGrouped` (both success and error paths) and after the async description fill. Regression test: `AgentsSkillsFilterTests.RefreshAsync_RaisesFilteredListNotifications`. |
| **Every interactive control in `Views/*.axaml` MUST have `AutomationProperties.Name` set** (Button, TextBox, ComboBox, CheckBox, ToggleSwitch, RadioButton, Slider, NumericUpDown, DataGrid, ListBox). Screen readers (Windows Narrator, NVDA, JAWS, macOS VoiceOver) read this property to announce the control to blind / low-vision users. Avalonia 12's `ContentControl` auto-derives `Name` from text Content on Buttons, but the auto-derived name picks up emoji glyphs and Alt-mnemonic `_` prefixes verbatim ("Underscore S a v e" / "Floppy disk save"), and non-`ContentControl` controls (ComboBox, TextBox, Slider, DataGrid) have NO auto-derivation at all. Explicit `AutomationProperties.Name="{x:Static loc:Strings.AutoNameXxx}"` is the only reliable surface. Pair with `AutomationProperties.HelpText` when the visible label is ambiguous (e.g. an icon-only `×` button with no surrounding context). Resx convention: `AutoName<Context>` / `AutoHelp<Context>` keys per `docs/LOCALIZATION-B2-RESOLVED.md`; values are clean text (no emoji, no `_` mnemonic prefix). Reuse existing string keys when the visible label IS a good announcement (e.g. `AutomationProperties.Name="{x:Static loc:Strings.ButtonDelete}"` when Content is `"Delete"`). AccessText (Alt-mnemonics) is for keyboard nav, NOT a substitute for screen-reader names — both must be present on the same button. | Blind user with NVDA tabs onto an icon-only Backup-tab Delete button → screen reader announces "button" with no further context, or worse "🗑️" rendered as a literal emoji glyph name; user has no way to know what the button does. | Resx convention: `AutoName*` / `AutoHelp*` keys in `src/ClaudeForge/Localization/Strings.resx`. Existing examples: `AutoNameSearchBox`, `AutoNameButtonSave`, `AutoNameToggleTheme`. Guard test: `tests/ClaudeForge.Tests/Accessibility/AxamlAccessibilityCoverageTests.cs` scans every `Views/*.axaml` file and reports any interactive control without `AutomationProperties.Name`; new controls added without a Name fail CI. Existing-gap baseline is tracked in the same test as an explicit allow-list so the test can pass green today while the backfill rolls in. |

---

## 2. "If you're doing X, also touch Y" checklists

### X = Adding a new compound editor (sixth one beyond MCP / Hooks / Permissions / EnabledPlugins / Marketplaces)

- [ ] New file in `src/ClaudeForge/ViewModels/Editors/<Name>EditorViewModel.cs` extending `PropertyEditorViewModel` (the app shim at `Editors/PropertyEditorViewModel.cs`, NOT the library base).
- [ ] Implement `MarkModified()` with the force-fire pattern (force-fire invariant above). Copy from `EnabledPluginsEditorViewModel.MarkModified`.
- [ ] If `LoadFromLayered` mutates collections that have subscribed handlers, add a `private bool _isLoading;` guard around the load body and return early in `MarkModified` when set. See parity table in [editor sidecar](./src/ClaudeForge/ViewModels/Editors/AGENTS.md).
- [ ] Override `LoadFromLayered(LayeredValue, ConfigScope)`. Call `SetScopeState(layered, editingScope)` first; set `IsModified = scopeValue != null` (or `Count > 0` if empty objects should NOT count).
- [ ] Override `OnResetToInherited()`. Cache `_lastLayered` and `_lastScope` in `LoadFromLayered`, then re-call `LoadFromLayered(_lastLayered, _lastScope)` here so reset restores the on-disk state instead of clearing.
- [ ] Override `ToJsonValue()`. Return `null` (NOT empty object) when the editor has no content — that's how `RemoveValue` is signalled to the workspace.
- [ ] Subscribe child `PropertyChanged` and nested `CollectionChanged` if the editor has any. Pattern: `OnXxxChanged(NotifyCollectionChangedEventArgs)` walks `e.NewItems` (subscribe) and `e.OldItems` (unsubscribe). Existing items present at subscription time must be hooked manually — see `McpServersEditorViewModel.SubscribeEntry`.
- [ ] Filter transient input fields in `OnEntryPropertyChanged` (e.g. `NewArg`, `NewServerName`). They MUST NOT mark modified or the Save button flickers per keystroke. Existing filter list: `NewArg`, `NewEnvKey`, `NewEnvValue` (MCP); `NewItemText`, `NewAllowText`, `NewDenyText`, `NewAskText` (Permissions); `NewPluginRef` (EnabledPlugins); `NewName`, `NewSourceType`, `NewSourceValue` (Marketplaces).
- [ ] Register the editor type in `src/ClaudeForge/ViewModels/Editors/CompositeEditorFactory.cs` (or `PropertyEditorFactory.cs`, follow the existing dispatch).
- [ ] Add a `DataTemplate` for the new VM in `src/ClaudeForge/Controls/PropertyEditorWrapper.axaml`. Set `x:DataType="vm:<Name>EditorViewModel"`.
- [ ] Add a regression test pair in `tests/ClaudeForge.Tests/ViewModels/Editors/<Name>EditorViewModelTests.cs`: `EditingXxxAfterLoad_FiresIsModifiedPropertyChanged` and `RemovingXxxAfterLoad_FiresIsModifiedPropertyChanged`. Templates in §3 below.
- [ ] If the editor lives under a new top-level navigation node, also touch `src/ClaudeForge/Services/NavigationTreeBuilder.cs` and the `NavTitle*` / `NavDesc*` constants in `MainWindowViewModel`.

### X = Adding a new debug flag (e.g. `--simulate-no-claude`)

- [ ] New `public static bool MyFlag { get; private set; }` property in `src/ClaudeForge/Services/DebugFlags.cs`.
- [ ] New `case "--myflag":` branch in `DebugFlags.Initialize`. Comparison is `ToLowerInvariant()` — the case label MUST be lowercase.
- [ ] **Don't call `Log.*` inside `Initialize`.** It runs BEFORE `Program.Main` configures Serilog (so the culture flag can take effect at step 2). Any warning to emit goes into `_deferredWarnings.Add(...)`; `LogActiveFlags()` flushes them after `ConfigureLogging`. See `--culture` for the canonical two-token pattern.
- [ ] Add the flag to `ListActive()` so it appears in the startup log line.
- [ ] Reset it in `ResetForTesting()`.
- [ ] If the flag takes a VALUE (two-token, e.g. `--culture en-US`): use the `for (var i = 0; i < args.Length; i++)` loop pattern. Validate the value before assigning; reject-with-warning via `_deferredWarnings.Add` rather than throwing. **Consume vs peek — pick deliberately, they behave differently on a typo:**
  - **Open value set** (any string is plausible — `--culture`, `--deep-link`): `args[++i]` unconditionally. A missing value eats the next token; that is accepted, because you cannot tell a bad value from a flag.
  - **Closed value set** (`--writer legacy|jsonc`): read `args[i + 1]` WITHOUT advancing, and `i++` only once the value is recognised. `--writer --linux` then rejects the writer *and* still honours `--linux`, instead of silently swallowing it. Consuming first is a real bug — it was caught here only because the test asserted the next flag still took effect.
- [ ] Read the flag at the relevant call site, ORed with the production condition (existing template: `ShowInstallBanner = DebugFlags.ShowInstallBanner || (!Detected)`).
- [ ] Add a test in `tests/ClaudeForge.Tests/Services/DebugFlagsTests.cs` that asserts the flag flips on the matching arg and stays default otherwise. For two-token flags, add cases for missing-value (last arg with no value), invalid-value (validation rejects), and value-then-next-flag (consumes the value and lets the outer loop see the next flag).
- [ ] Document the flag in `CLAUDE.md`'s debug-flags table.
- [ ] Document the flag in `README.md`'s features section if it's user-visible.

### X = Adding a new GUI-bypass CLI tool (e.g. `--cleanup-restore-sidecars`, hypothetical `--vacuum-sessions`)

**Distinct from debug flags.** Debug flags tweak GUI state; CLI tools run a task and exit. Pattern established by `--cleanup-restore-sidecars`.

- [ ] Implement the actual work as `internal static` in `AgentForge.Core` (NOT in the GUI assembly). The Core project has no Avalonia dependency, so the logic is testable without spinning up a headless dispatcher and importable by future out-of-tree consumers (CLI wrapper, MCP server, etc.). Example: `src/AgentForge.Core/Backup/RestoreSidecarCleanup.cs`.
- [ ] Return a structured `Result` record from the worker — counts, byte totals, failure messages capped at a sensible threshold (20 for the existing tool) to bound process memory.
- [ ] Accept an optional `Action<int>? onProgress` callback for long-running operations. Heartbeat every 1 000 items (or analogous granularity) so the CLI surface can stream status to stderr.
- [ ] In `src/ClaudeForge/Program.cs`, add the flag to the CLI-bypass `foreach (var arg in args)` loop ABOVE `BuildAvaloniaApp()` and return after dispatching — never start Avalonia for these tools.
- [ ] Wrap the CLI entry in a private static `RunXxx()` helper that:
   - **MUST** call `TryAttachParentConsole()` at the top. The binary is `<OutputType>WinExe</OutputType>` on Windows, which detaches stdout/stderr from the parent terminal at startup. Without `AttachConsole(ATTACH_PARENT_PROCESS)`, every `Console.WriteLine` vanishes silently.
   - Prints a `[ClaudeForge] <action>…` start line to `Console.Error`.
   - Logs the action to Serilog at `[<Area>.Command] action=…` so the rolling log records the run even when the terminal is detached.
   - Streams progress to `Console.Error` via the `onProgress` callback.
   - Prints a final summary to `Console.Error` AND mirrors it to `Log.Information(…)`.
   - On failure: dump the first ≤20 failure messages to BOTH surfaces and log each at `Log.Warning(…)`.
   - Calls `Log.CloseAndFlush()` before returning. Avoid the outer `finally` in `Main` for CLI tools — the explicit flush makes intent obvious and avoids racing with the parent terminal which may already be re-attached to the next command.
- [ ] Document the tool in `CLAUDE.md`'s **CLI-bypass tools** section (NOT the debug-flags table — they're conceptually different).
- [ ] Document the tool in `README.md` under the relevant user-facing section (Backup workflow for the cleanup tool, etc.).
- [ ] Update `--debug-help`'s emitted line in `DebugFlags.Initialize` to include the new flag so users discover it.
- [ ] Tests: cover the worker's Result shape in `AgentForge.Core.Tests` (no GUI deps). Verify the file/directory side effects, the failure-resilience path (locked / read-only / missing inputs), and the progress callback contract.

### X = Adding a new persisted UI-state field

- [ ] New property on `WindowState` in `src/ClaudeForge/Services/WindowStateService.cs`. Add `[JsonPropertyName("yourKey")]`.
- [ ] If the type is not already in `AppJsonContext`, add it. The serializer call at `WindowStateService.Load` / `Save` already uses `AppJsonContext.Default.WindowState`, so as long as the new property's type is reachable from `WindowState` the existing context covers it. Verify by running `dotnet publish` once and checking for IL2026.
- [ ] Cache the field in `MainWindowViewModel` if it's read more than once per session, and load it from `WindowStateService.Load()` at construction time.
- [ ] Save it via `MainWindowViewModel.SaveWindowState(...)` — that is the ONE call site that writes UI state.
- [ ] Regression test in `tests/ClaudeForge.Tests/Services/WindowStateServiceTests.cs` — round-trip: write a `WindowState` with the new field set, read back, assert equal. Use `PlatformPaths.TestUserProfileOverride` for sandboxing (template in §3).

### X = Adding a new localized string

- [ ] Add the key to `src/ClaudeForge/Localization/Strings.resx` (+ a `<comment>`).
- [ ] Add the key with a **real translation** to EVERY `Strings.<culture>.resx` (de/es/fr/ja/ko/pt/ru/zh). `LocalizationParityTests` enforces full parity, forbids `TODO` markers, and rejects near-copies of English — a missing/placeholder translation fails the test gate.
- [ ] Add the matching `public static string KeyName { get; }` entry to `Strings.Designer.cs` (NOT auto-generated by source gen — manually edit to mirror the .resx entry).
- [ ] Reference it as the literal token `Strings.KeyName` (C#) or `{x:Static loc:Strings.KeyName}` (AXAML) — the `loc:` namespace is already declared in existing views. The **dead-string guard** (`Directory.Build.targets`) fails the build for an unreferenced key, and its **dynamic-access tripwire** fails the build on by-name/reflective access (`Strings.ResourceManager`, `typeof(Strings)`). For an id→string map use a `switch` whose arms each return a literal `Strings.<Key>` (see `ViewModels/Catalog/CatalogLocalization.cs`).
- [ ] DO NOT inline user-visible English in code or AXAML. The existing convention is in [`LOCALIZATION.md`](./LOCALIZATION.md).

### X = Adding / changing a `model`, `effortLevel`, or `permissions.defaultMode` value

- [ ] Edit the catalog `src/AgentForge.Core/Assets/ModelCatalog/model-catalog.json` — it's the single source of truth for these lists + their relationships (which efforts a model supports, auto-mode capability). DO NOT hardcode a new list in a view-model.
- [ ] `pwsh scripts/validate-model-catalog.ps1` must pass (structural + cross-relationship checks).
- [ ] If you added a `permissions.defaultMode` value, add its localized label/description to `ViewModels/Catalog/CatalogLocalization.cs` (literal `Strings.<Key>` arms) and the matching `Strings` keys (all locales). Keep `ModelCatalogSchemaParityTests` green (catalog enums ↔ bundled JSON-schema enums).
- [ ] App consumers read through the SDK: `client.Models` (`IModelCatalogAccessor`) — never the Core catalog directly (SDK-first). Full design: [`docs/MODEL-CATALOG.md`](./docs/MODEL-CATALOG.md).

### X = A new model launches and takes over a family alias (e.g. Opus 5 → `opus`)

The schema refresh (`scripts/refresh-schema.{ps1,sh}`) does **NOT** carry model names — schemastore.org omits them, so expect it to report "already up to date". The pickers come entirely from the catalog + two hand-curated overlays. When the `opus`/`sonnet`/… alias moves to a new snapshot, edit these four and keep the invariant **one non-legacy row per family alias**:

- [ ] `ModelCatalog/model-catalog.json` — add the new row (copy the outgoing build's effort/`supports1m`/`supportsAutoMode` flags unless docs say otherwise), move the `alias` onto it, repoint the `aliases` map, and **demote the previous holder to `legacy: true`, `alias: null`** (keep the row — still a valid pin).
- [ ] `Schemas/claude-code-settings.overlay.json` — swap the family's pinned snapshot id in `model.examples` (+ the `model.description` e.g.). This is the AutoCompleteBox list; the refresh never touches the overlay.
- [ ] `Descriptions/claude-code-settings.enumdescriptions.json` — mirror that swap and update the alias tooltip ("today Opus N"). **Every `model.examples` value needs a key here** or `ModelPropertyPromotionTests` fails (each picker item needs a tooltip). Replace in place, don't accumulate stale rows.
- [ ] `tests/AgentForge.Core.Tests/Catalog/ModelCatalogTests.cs` — update the `Resolve("opus")` / `Resolve("opus[1m]")` asserts to the new id (they hardcode the alias target). Tests that use the demoted id as a *concrete* model stay green — its capabilities didn't move.
- [ ] Verify: `pwsh scripts/validate-model-catalog.ps1`, then `dotnet test ClaudeForge.slnx --filter "FullyQualifiedName~ModelCatalog|FullyQualifiedName~ModelPropertyPromotion"`. Full playbook + rationale: [`docs/MODEL-CATALOG.md`](./docs/MODEL-CATALOG.md) § New model launch.

### X = Adding a new share operation (new `ShareXxxCommand` in a view-model)

- [ ] Accept `IShareService? shareService` as a constructor parameter (nullable — unit tests pass `null`; the app wires the real service).
- [ ] For file-sharing commands (`ShareFileAsync`): also accept or inject a `Func<string?>` that returns the path lazily at execute-time (makes the provider testable without real Serilog / disk setup). See `AboutEditorViewModel._logPathProvider` pattern.
- [ ] Implement the async handler: null-guard the service; call `ShareFileAsync` or `ShareTextAsync`; catch broadly and log via `Log.Error`. Never throw to the caller.
- [ ] Wire `[RelayCommand]` on the async handler. If the command should only be enabled when the path is known, add `CanExecute = nameof(CanShareXxx)` and evaluate the service + path together.
- [ ] Bind in the corresponding view AXAML: `Command="{Binding ShareXxxCommand}"`.
- [ ] Test stub: `RecordingShareService` (file-scoped in `tests/ClaudeForge.Tests/ViewModels/ShareServiceTests.cs`) records all calls; `NullDialogService` stubs the dialog dependency. Copy both into the new test class or move them to a shared helper.
- [ ] Minimum test coverage: `NullService_IsNoOp`, `NullPath_CannotExecute` (if path-conditional), `Calls_ServiceWithExpectedPayload`, `Title_MatchesDisplayName`.

Reference implementations: `BackupRestoreViewModel.ShareBackupCommand`, `EffectiveSettingsViewModel.ShareConfigCommand`, `AboutEditorViewModel.ShareLogCommand`.

---

### X = Refactoring a platform check

Decision tree:

```
Is this branch about UI / display content (install command preview, About-page
platform name, scope colour, path string shown to user)?
  YES → use PlatformInfo.Current.IsWindows / IsMacOS / IsLinux.
        Emulation flags will toggle this, which is what you want.
  NO  → is this a platform-intrinsic API (Windows registry, MSIX, env-var
        Machine scope, macOS Keychain, Linux secret-service)?
        YES → use OperatingSystem.IsWindows() / IsMacOS() / IsLinux().
              Wrap in [SupportedOSPlatform("windows")] guards as needed —
              the analyzer enforces this for registry / MSIX APIs.
              Emulation flags MUST NOT redirect these calls.
```

Reference: [`PLATFORM.md`](./PLATFORM.md).

### X = Adding a new `SchemaValueType` branch

- [ ] Add the case to the editor-construction dispatch in `src/ClaudeForge/ViewModels/Editors/PropertyEditorFactory.cs` / `DefaultEditorFactory.cs` / `CompositeEditorFactory.cs`.
- [ ] Add the case to the JSON-tab placeholder builder (`BuildPlaceholder` switch — search for it; emits a typed stub for the "Show all" toggle).
- [ ] Override `ToJsonValue()` for the new editor to emit the right shape.
- [ ] Add a regression test in `tests/ClaudeForge.Tests/ViewModels/Editors/PropertyEditorFactoryTests.cs`.

### X = Adding a new bundled-schema property whose `SchemaValueType` is `Complex` / `Object` / `Array<Object>`

The factory has typed-fallback dispatch helpers that concentrate per-property knowledge. Without explicit dispatch, an unmatched Complex / Unknown property falls through to `JsonRawPropertyEditorViewModel` (safe but raw JSON) — fine as a default, but a typed editor is usually one switch arm and a per-shape VM.

- [ ] **Complex (object with no declared properties)** — extend `DefaultEditorFactory.CreateComplexFallback` with a `schema.Name` arm pointing at the right typed VM. Examples live there: `modelOverrides → StringMapPropertyEditorViewModel`. For a string→string map, reuse `StringMapPropertyEditorViewModel` and pass any per-property `keySuggestions` list at construction. For richer shapes, write a new VM and add it.
- [ ] **Array<Object>** — extend `DefaultEditorFactory.CreateArrayObjectFallback` with a `schema.Name` arm. Examples: `allowedMcpServers/deniedMcpServers → McpServerListEditorViewModel` (3-variant discriminated union), `strictKnownMarketplaces/blockedMarketplaces → MarketplaceListEditorViewModel` (8-variant `source`-discriminated union).
- [ ] **Truly unknown** — leave the fall-through to `JsonRawPropertyEditorViewModel`; it parses on every keystroke and refuses to write garbage, so the user can't silently corrupt the property even without a typed editor.
- [ ] Regression test in `PropertyEditorFactoryTests` asserting the dispatch returns the expected VM type AND that any injected suggestion lists are populated (e.g. `Complex_ModelOverrides_DispatchesToStringMapEditor` asserts "sonnet" appears in `KeySuggestions`).
- [ ] If the new VM aggregates a collection or subscribes to children in its constructor, follow the [editors-AGENTS sidecar](./src/ClaudeForge/ViewModels/Editors/AGENTS.md) `_isLoading` + `MarkModified` contract.

### X = Making a page deep-navigable (implementing `IDeepNavigable`)

A page becomes addressable by `--deep-link` and restorable across a reload by
implementing `src/AgentForge.Avalonia.Shell/Navigation/IDeepNavigable.cs`. Grammar and nav-tree
resolution are already done — see `ViewModels/NavDeepPath.cs`. Reference
implementation: `AgentsSkillsEditorViewModel`.

- [ ] The node needs a `NodeId` (see the nav-page checklist below). Without one the page can never be addressed.
- [ ] Give every in-page tab / segment a **stable id const** plus a `SelectSegment(string id)`-style setter, mirroring `GroupTab.PropertiesId` + `SettingsGroupEditorViewModel.SelectTab`. Do NOT let the external contract be a bare tab index — reordering tabs would silently repoint every saved path.
- [ ] `CaptureDeepPath()` returns culture-invariant, persistable segments. **Never a filesystem path** — the separator is `/`, so a path can't survive a round trip. For an item key use `NavDeepPath.FormatItemKey(name, source)`; qualify it so a restore can't land on a same-named item from another scope or plugin.
- [ ] Derive the tab segment from the **selected item's own identity** where possible, not from the currently-visible tab index, so the captured pair is self-consistent by construction (`AgentsSkillsEditorViewModel.SegmentIdForCategory`).
- [ ] Override `CaptureTransientState()` **only** if the page holds state that must survive an in-process reload but must never be persisted — the canonical case is an unsaved edit buffer on a page that writes files directly (so it isn't part of `HasUnsavedChanges`). Return an immutable record. The default returns `null`.
- [ ] `TryRestoreDeepPathAsync` must: select the tab FIRST (landing on the right tab beats landing on the default one even when the item is gone); await any **in-flight** load rather than starting a competing one (expose a `Task? LastRefresh` seam — see the `LastRefresh` docstring for why a second walk is actively harmful); honour `DeepRestoreMode.Locate` by NOT entering an editing experience; and return `false` rather than throwing when the target no longer exists.
- [ ] Reveal the target with `ApplyNavigationFilter(...)` (see the invariant above), not by scrolling — plain `ItemsControl` has no `ScrollIntoView`.
- [ ] Treat an unrecognised `transientState` as `null` instead of casting blindly.
- [ ] Tests: capture/restore round-trip through the STRING form (`NavDeepPath.Format` → `TryParse` → `Resolve`); `Locate` does not enter edit mode while `Full` does; a missing item returns `false` but still selects the tab; the captured path contains no path separators. Templates in `tests/ClaudeForge.Tests/ViewModels/AgentsSkillsDeepPathTests.cs`.

### X = Adding a new top-level navigation page

- [ ] Constants in `src/ClaudeForge/ViewModels/MainWindowViewModel.cs` — `NavTitleXxx` and `NavDescXxx` (search for existing examples like `NavTitleMemory`/`NavDescMemory`). Both go through `Strings.resx`.
- [ ] **A `NavIdXxx` const and `NodeId = NavIdXxx` on the node.** This is the culture-invariant key deep links and persisted UI state resolve against; a node without one is permanently unaddressable. Ids must be unique among **SIBLINGS**, not tree-wide (`version-info` legitimately exists under both product headers, and a settings-group name may repeat across products) — the path grammar is `<parent-id>/<child-id>`. Divider nodes get NO id. Settings-group children derive theirs from `NavDeepPath.Slug(group.Title)`. Guard test: `tests/ClaudeForge.Tests/ViewModels/NavigationNodeIdTests.cs`.
- [ ] Add the node directly in `MainWindowViewModel`'s nav-tree construction loop (look for the `// --- Memory & Footprint ---` block — top-level pages are added inline alongside Profiles / Backup / Environment / Memory, NOT through `NavigationTreeBuilder` which is settings-group-specific).
- [ ] **Set `IsTopLevel = true`** on the `NavigationNodeViewModel`. Without it, the icon column collapses (no icon renders) and the node looks like a sub-item. Top-level dividers (`new NavigationNodeViewModel("─────") { IsDivider = true, IsTopLevel = true }`) also need both flags.
- [ ] **Pick a basic-Unicode icon glyph**, NOT an emoji-presentation one. `★` (U+2605) is good; `⭐` (U+2B50) renders as missing-glyph on Linux without an emoji font. Other working glyphs already in the tree: `⚙` (U+2699), `🖥` (U+1F5A5), `📊` (U+1F4CA), `👤` (U+1F464), `💾` (U+1F4BE), `🌐` (U+1F310), `🧠` (U+1F9E0), `ℹ` (ℹ). Test by setting `--linux` (emulation) and / or running on real Linux.
- [ ] If the page should survive workspace reload (long-running tool VM with state mid-edit, like Backup / Profiles / About / Essentials): cache the VM in a `_xxxVm` field, lazy-init in `BuildNavigationTree`, add it to `IsPersistentToolVm()` so `DisposeNavigationEditors` skips it, and dispose in `MainWindowViewModel.Dispose`. See `_essentialsVm` for the canonical wiring. Add a `GetXxxVmForTesting()` test seam.
- [ ] New view + view-model under `src/ClaudeForge/Views/` and `src/ClaudeForge/ViewModels/`. Register the DataTemplate in `App.axaml`'s `<Application.DataTemplates>`.
- [ ] AXAML accent-pill heading pattern — copy from any existing settings page (e.g. `src/ClaudeForge/Views/PermissionsEditorView.axaml`, `MemoryEditorView.axaml`). The pill uses `{DynamicResource NavSelectedAccentBrush}`.
- [ ] Resource keys for title / description in `Strings.resx` + `Strings.zh-CN.resx` (with `TODO zh-CN translation` comment) + `Strings.Designer.cs`.
- [ ] If the page hosts dialog content with titlebars: use `AppIcon.SmallInstance` (64-px render of the small SVG) on the dialog's `Icon`, NOT `AppIcon.Instance` (256-px detailed master). Dialog titlebars scale down — the simplified small SVG reads more clearly. See `AboutDialog.axaml.cs` / `SaveChangesDialog.axaml.cs`.

---

## 3. Test seam quick-reference

### `PlatformPaths.TestUserProfileOverride` sandbox

Every test that reads or writes anything path-relative MUST scope the writes to a temp dir.

```csharp
private string _sandbox = null!;

[TestInitialize]
public void Init()
{
    _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_sandbox);
    Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
    PlatformPaths.TestUserProfileOverride = _sandbox;
}

[TestCleanup]
public void Cleanup()
{
    PlatformPaths.TestUserProfileOverride = null;
    if (Directory.Exists(_sandbox))
        Directory.Delete(_sandbox, recursive: true);
}
```

Live example: `tests/ClaudeForge.Tests/ViewModels/HasUnsavedChangesRecheckTests.cs`.

### `MainWindowViewModel.GetClaudeCodeWorkspaceForTesting()` test seam

For tests that need to mutate the workspace directly without driving the UI:

```csharp
var vm = new MainWindowViewModel(new SchemaRegistry(), new NullDialogService());
await vm.InitializeCommand.ExecuteAsync(null);
var workspace = vm.GetClaudeCodeWorkspaceForTesting();
Assert.IsNotNull(workspace);

workspace!.SetValue("model", JsonValue.Create("opus")!, ConfigScope.User);
Assert.IsTrue(vm.HasUnsavedChanges);
```

The seam is `internal` and exposed to `ClaudeForge.Tests` via `InternalsVisibleTo`. Declared on `MainWindowViewModel`.

### `DebugFlags.ResetForTesting()`

Static state isolation between tests:

```csharp
[TestCleanup]
public void Cleanup() => DebugFlags.ResetForTesting();
```

Internally it also calls `PlatformInfo.ResetForTesting()`, so a single call covers both.

### `PlatformInfo.ResetForTesting()` / `OverrideForDebug()`

Direct platform emulation in a test:

```csharp
PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
try { /* assertions */ }
finally { PlatformInfo.ResetForTesting(); }
```

Source: `src/AgentForge.Core/Platform/PlatformInfo.cs`.

### Force-fire fired-count assertion (the force-fire contract test)

The pattern that locks the force-fire invariant in place:

```csharp
[TestMethod]
public void RemovingXxxAfterLoad_FiresIsModifiedPropertyChanged()
{
    // Arrange: load a populated scope so IsModified starts true.
    var vm = new MyEditorViewModel(SchemaRegistry.Empty, ConfigScope.User);
    vm.LoadFromLayered(LayeredWith(ConfigScope.User, populatedJsonObject), ConfigScope.User);
    Assert.IsTrue(vm.IsModified, "Precondition: load must leave IsModified=true.");

    var fired = 0;
    vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(MyEditorViewModel.IsModified))
            fired++;
    };

    // Act: simulate the user mutation.
    vm.MyCollection.RemoveAt(0);

    // Assert: the force-fire pattern emitted PropertyChanged even though
    // IsModified was already true — that's what wakes the live-write chain.
    Assert.IsTrue(fired >= 1,
        "PropertyChanged(IsModified) must fire on user remove, even though " +
        "the flag was already true from the load.");
}
```

Live examples in `tests/ClaudeForge.Tests/ViewModels/Editors/McpServersEditorViewModelTests.cs`:
`RemoveServerAfterLoad_FiresIsModifiedPropertyChanged` and
`AddServerAfterLoad_FiresIsModifiedPropertyChanged`.

### `RestoreEngine` internal-static seams

The eight `internal static` methods in `src/AgentForge.Core/Backup/RestoreEngine.cs` are test seams (callable via `InternalsVisibleTo("AgentForge.Core.Tests")`):

| Method | What it tests |
|---|---|
| `ResolveSafeExtractPath(baseDir, entryFullName)` | Zip-slip defence: traversal / absolute path / ADS / containment check |
| `IsUnderUserProfile(candidate)` | Security predicate gating manifest-provided paths (UNC reject, malformed reject, equality vs. startswith branches) |
| `RestoreSection(srcFile, destFile, stamp)` | Single-file restore + `.pre-restore-{stamp}.bak` sidecar |
| `RestoreDirectory(srcDir, destDir, stamp)` | Recursive directory restore + per-file containment re-check |
| `RestoreProjects(tempRoot, manifest, stamp, skipped, fileFailures)` | Manifest-driven projects subtree restore + `IsUnderUserProfile` gate |
| `RestoreWorktrees(tempRoot, stamp, skipped, fileFailures)` | Worktree-metadata-driven restore + `IsUnderUserProfile` gate |
| `EvictOldSidecarsIfNeeded(liveFile)` | Sidecar cap (3 per file, evict oldest at write time) |
| `ContainsRedactedMarker(tempRoot)` | Tamper detection — scans extracted `*.json` for the `[redacted]` literal |

All exercised by `tests/AgentForge.Core.Tests/Backup/RestoreEngineTests.cs`. When refactoring, prefer keeping these `internal` rather than `private` — a future contributor adding a test for a private static would otherwise reach for reflection. Tests under-user-profile paths (RestoreProjects / RestoreWorktrees happy-path) use a `CreateUnderUserProfile(suffix)` helper that drains via the `_underProfileCleanup` queue in `Teardown` to avoid leaking real home-directory subtrees.

### `SchemaRegistry` overlay-merge seams

Two `internal static` methods in `src/AgentForge.Core/Schema/SchemaRegistry.cs` lock the overlay-merge plumbing (see CLAUDE.md "Schema loading priority"):

| Method | What it tests |
|---|---|
| `TryReadBundledBytesMerged(cacheFileName)` | Production E2E path — reads base + applies sibling `.overlay.json` |
| `ApplyMergePatch(target, patch)` | RFC 7396 unit semantics — primitive replace, recursive object merge, null-deletes-key, array wholesale replace, primitive patch replaces object target, null target with object patch, key-order preservation |

Both exercised by `tests/AgentForge.Core.Tests/Schema/SchemaRegistryOverlayTests.cs`. Adding a new bundled schema with hand-curated additions: create the base file + sibling `<name>.overlay.json` under `src/AgentForge.Core/Assets/Schemas/` (the existing `EmbeddedResource Include="Assets\Schemas\**\*.json"` glob picks both up automatically); the loader merges them at load time.

### `LayeredWithXxx(scope, jsonObj)` builder helpers

Most editor tests need a `LayeredValue` with one or two scope entries. The convention is a private helper:

```csharp
private static LayeredValue LayeredWith(ConfigScope scope, JsonNode value) =>
    new("myKey", new[] { new ScopeEntry(scope, value, "/test/path") })
    {
        EffectiveValue = value,
        EffectiveScope = scope,
    };
```

Pattern visible in: `tests/ClaudeForge.Tests/ViewModels/Editors/McpServersEditorViewModelTests.cs`, `PermissionsEditorViewModelTests.cs`. Search for `LayeredWith` to find existing helpers in any new test file you write.

---

## 4. Anti-patterns (side-by-side)

### Bare `IsModified = true` when the flag is already true

```csharp
// WRONG — silently elided when IsModified was already true from the load.
private void OnSomethingChanged() => IsModified = true;
```

```csharp
// RIGHT — force-fire pattern.
private void MarkModified()
{
    if (_isLoading) return;
    if (IsModified)
        OnPropertyChanged(nameof(IsModified));
    else
        IsModified = true;
}
```

What breaks: the live-write to disk and the Save-button-enable chain are both subscribed to `PropertyChanged(IsModified)`; an elided assignment means neither fires. Locked by `RemoveServerAfterLoad_FiresIsModifiedPropertyChanged`.

### Reflection-based `JsonSerializer`

```csharp
// WRONG — IL2026 in trimmed builds; runtime crash in AOT.
var state = JsonSerializer.Deserialize<WindowState>(json);
```

```csharp
// RIGHT — source-generated context.
var state = JsonSerializer.Deserialize(json, AppJsonContext.Default.WindowState);
```

What breaks: published Release builds (`PublishTrimmed=true`) emit IL2026, and the deserializer fails at runtime because the property metadata was trimmed. See [`TRIMMING.md`](./TRIMMING.md).

### `Environment.GetFolderPath` not honoring the test override

```csharp
// WRONG — goes around TestUserProfileOverride, touches the developer's real ~/.
private static readonly string ConfigPath =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                 ".claude", "settings.json");
```

```csharp
// RIGHT — route through PlatformPaths so the test sandbox applies.
private static string ConfigPath =>
    Path.Combine(PlatformPaths.UserProfile, ".claude", "settings.json");
```

What breaks: tests pollute the developer's real Claude config; CI is fine but local runs leave artefacts behind. The `=>` (property) instead of `=` (field) is the second half of the next anti-pattern.

### `RuntimeInformation.IsOSPlatform` on a UI / display surface

```csharp
// WRONG — UI shows the host OS's install command even when --linux is set.
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    InstallCommand = "winget install ...";
```

```csharp
// RIGHT — emulation-aware.
if (PlatformInfo.Current.IsWindows)
    InstallCommand = "winget install ...";
```

What breaks: the `--linux` debug flag (and the equivalent for macOS) is meant to preview cross-platform UI without rebooting. Using the runtime check defeats it. The reverse applies: registry / MSIX call sites MUST use `OperatingSystem.IsWindows()` because they cannot run on Linux regardless of emulation.

### `static readonly` capturing host state at type-init

```csharp
// WRONG — captured at type init, before the test's TestInitialize runs.
private static readonly string StatePath =
    Path.Combine(PlatformPaths.ClaudeHome, "cache", "ClaudeForge-gui-state.json");
```

```csharp
// RIGHT — recomputed on every access. Cheap (3 Path.Combine calls).
private static string StatePath =>
    Path.Combine(PlatformPaths.ClaudeHome, "cache", "ClaudeForge-gui-state.json");
```

What breaks: tests that run BEFORE the type is referenced see the override; tests that run AFTER see the cached real-host path. Order-dependent failures — passes alone, fails in suite. Live fix: `src/ClaudeForge/Services/WindowStateService.cs`.

### Bare `catch { }`

```csharp
// WRONG — swallows OutOfMemoryException, ThreadAbortException, etc.
try { Save(state); } catch { }
```

```csharp
// RIGHT — filter exception types you actually expect.
try { Save(state); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
{
    Log.Warning(ex, "Failed to save state");
}
```

What breaks: real bugs become invisible. Existing convention in CLAUDE.md "Key conventions". Pattern visible in `WindowStateService.Load/Save/Delete`.

---

## 5. Verify-before-shipping checklist

```bash
# 1. Build clean.
dotnet build
# Expected: 0 errors, 0 warnings.

# 2. Tests green.
dotnet test --no-build
# Expected: 0 failed. The total / skip counts drift per release; the green
# baseline is whatever the most recent successful run on main reported.

# 3. Trim-safe publish — required after touching anything reflection-y, JSON
#    serialization, AXAML DataTemplate/UserControl, or third-party deps.
pwsh src/publish/publish.ps1 -All -Rids win-x64
# Expected: 0 IL2026 / IL2070 / IL3050 warnings.
# Repeat across RIDs you actually need to verify (the script handles all six
# in one invocation when called with -All and no -Rids restriction).
# Reference: TRIMMING.md.

# 4. Manual smoke for UI changes:
#    - Launch the app, verify no startup exception in Serilog output.
#    - Click through Claude Code → General → MCP → Permissions → Hooks → Plugins → Marketplaces.
#    - For each compound editor: edit a value, confirm Save enables; click
#      Reset, confirm Save disables.
#    - Toggle Effective and JSON tabs on at least one page.
```

---

## 6. Pointer index — which doc owns which concern

| Concern | Owning doc |
|---------|------------|
| Why these agent docs exist; methodology rationale | [`AGENT-ONBOARDING.md`](./AGENT-ONBOARDING.md) |
| Architecture decisions, build/run, gotchas | [`CLAUDE.md`](./CLAUDE.md) |
| Platform abstraction, debug flags, `PlatformInfo` decision tree | [`PLATFORM.md`](./PLATFORM.md) |
| Trimming, `PublishTrimmed`, ILLink, IL2026 diagnostics | [`TRIMMING.md`](./TRIMMING.md) |
| Avalonia / .NET 10 foot-guns: style precedence (LocalValue beats Style), `Styles` scoping, `DataTemplate` order, virtualization, TextWrapping, tooltip propagation, lifetime, JsonArray.Add | [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) |
| Linux desktop integration: X11 vs Wayland, `.desktop` file install, icon themes | [`docs/LINUX-DESKTOP-INTEGRATION.md`](./docs/LINUX-DESKTOP-INTEGRATION.md) |
| Essentials-page card list, severity tiers, add-a-card checklist | [`docs/ESSENTIALS-PAGE.md`](./docs/ESSENTIALS-PAGE.md) |
| Localized-string workflow (`Strings.resx` + Designer + `{x:Static}`) | [`LOCALIZATION.md`](./LOCALIZATION.md) |
| Build / test / PR workflow, contributor setup | [`CONTRIBUTING.md`](./CONTRIBUTING.md) |
| CI / release workflow reference, publish.ps1 wiring | [`.github/WORKFLOWS.md`](./.github/WORKFLOWS.md) |
| Public-facing description, install instructions, feature list | [`README.md`](./README.md) |
| Compound-editor contract: force-fire, `_isLoading`, child subs, parity table | [`src/ClaudeForge/ViewModels/Editors/AGENTS.md`](./src/ClaudeForge/ViewModels/Editors/AGENTS.md) |
| Workspace / scope semantics: `ConfigScope` order, `IsDirty` vs `HasActualChanges`, merge rules | [`src/AgentForge.Core/Settings/AGENTS.md`](./src/AgentForge.Core/Settings/AGENTS.md) |
| SDK architecture: what the SDK has/doesn't have, `_suppressForwarder`, `_cachedSchemaNodes`, `Changed` threading, `SearchSchema`, test seams | [`src/AgentForge.Sdk/AGENTS.md`](./src/AgentForge.Sdk/AGENTS.md) |
| ViewModel layer: MWVM integration hub, nav tree structure, `SearchViewModel` contract, specialized editors, JsonPath→NavNode mapping, deep-path capture/restore | [`src/ClaudeForge/ViewModels/AGENTS.md`](./src/ClaudeForge/ViewModels/AGENTS.md) |
| Deep linking: `NodeId` identity, path grammar, `--deep-link`, reload restore, `Locate` vs `Full` | `src/AgentForge.Avalonia.Shell/Navigation/NavDeepPath.cs`, `IDeepNavigable.cs`; wiring in `MainWindowViewModel` (`CaptureDeepPath`, `TryQueueDeepRestore`, `ApplyPendingDeepRestore`); §2 checklist above |
| Share-sheet service (cross-platform share of text / files) | `src/LayeredEditors.Avalonia.Services/IShareService.cs`, `DefaultShareService.cs`; view-model integrations in `BackupRestoreViewModel`, `EffectiveSettingsViewModel`, `AboutEditorViewModel` |

When in doubt, follow the pointer instead of duplicating content here.
