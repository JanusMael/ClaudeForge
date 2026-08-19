# ClaudeForge ViewModels — Agent Operational Guide

Cross-file invariants for the ViewModel layer.
Read alongside the root [`AGENTS.md`](../../../AGENTS.md) and
[`Editors/AGENTS.md`](./Editors/AGENTS.md).

---

## §1 `MainWindowViewModel` — integration hub

`MainWindowViewModel` (MWVM) owns everything that bridges the SDK, Core, and UI:

| Owned resource            | Field / property                                                 |
|---------------------------|------------------------------------------------------------------|
| **Hosted products**       | **`_sections : List<ProductSection>`** — the storage (see below) |
| Claude Code SDK client    | `ClaudeCodeSdk : ClaudeConfigClientBase?` — **facade** over `_sections` |
| Claude Desktop SDK client | `ClaudeDesktopSdk : ClaudeConfigClientBase?` — **facade** over `_sections` |
| Shared schema registry    | `_schemaRegistry : SchemaRegistry`                               |
| Navigation tree           | `NavigationTree : ObservableCollection<NavigationNodeViewModel>` |
| Search VM                 | `Search : SearchViewModel` (neutral shell; fed this app's synthetic table) |
| Snapshot service          | `_snapshotService`                                               |
| Dirty-flag                | `HasUnsavedChanges` (recomputed from SDK `HasActualChanges()`)   |

MWVM is the **only** place where SDK clients are constructed, opened, and disposed.
Editor VMs and search VMs receive delegates or already-constructed objects — they
never `new` an SDK client themselves.

### §1.1 `ProductSection` is the storage; the two named SDK properties are facades

⚠ **Do not add a third named `…Sdk` field.** A hosted product is a `ProductSection`
(`ProductSection.cs`): its `ProductDescriptor`, nav title, workspace display name, export
entry path, and its live `Client`. `_sections` is the list; `Sections` exposes it,
`OpenSections` filters to the opened ones, and `SectionFor(product)` resolves one by
`ProductDescriptor.Id`.

⚠ **`ExportEntryPath` is composed, not given.** A section is constructed with only the path
*inside* its product's folder (`".claude/settings.json"`); the folder segment comes from
`ProductDescriptor.ArchiveFolder`. Do not pass a whole path back in. `ExportManifest.Clients`
lists those same folder names, and a reader of an export archive takes that list as the
folders to look in — deriving both from one property is what keeps them from disagreeing.
Covered by `ExportArchiveTests`, which asserts every folder the manifest names really exists
in the archive.

`ClaudeCodeSdk` / `ClaudeDesktopSdk` still exist and are still widely called, but they now
read *through* `_sections` — they are a convenience for the many editor call sites that
genuinely mean "the Claude Code client", not the place the client lives.

**Every lifecycle operation iterates the list**, and must keep doing so: open, save,
validate, snapshot, subscribe/unsubscribe, dirty check, export, dispose. `Client` is typed
`ClaudeConfigClientBase` (not the neutral core) because the editor VMs take
`IClaudeConfigClient` — correct for this app; Phase 5 parameterises it.

> ⚠⚠ **This is the single largest coverage hole found in the whole OpenCodeForge effort.**
> Making every one of those loops cover only the *first* section — a silently one-product
> save, validate, subscribe, dispose and export — **passed all 2,814 tests.** The suite
> exercises one product at a time, so a regression here is invisible by default.
> **Any test asserting multi-product behaviour must open two sections deliberately.**
>
> Two-section guards exist now, and they are the ones to extend rather than replace:
> `SavePreservationTests` (save reaches every product; the last section alone marks the window
> dirty) and `ExportArchiveTests` (the export manifest names every open product). Both open
> two sections and both were canaried against a first-section-only implementation.

**Deliberately still per-product, not oversights:** the navigation tree (different icons,
node ids and descriptions, and Claude Code has pages Desktop has none of — Phase 5 owns it),
and `UpdateScopeContextScopes`, whose Desktop branch carries a documented binding workaround.

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

| Editor type | Declares | Search treatment |
|---|---|---|
| `SettingsGroupEditorViewModel` | **`ISchemaGroupEditor`** (`GroupName` + `SchemaNodes`) | Walk schema nodes; match by name / title / desc / JsonPath |
| `PermissionsEditorViewModel`, `HooksEditorViewModel`, `McpServersEditorViewModel` | **`IJsonPathScopedEditor`** (`OwnedJsonPathPrefix`) | Ask the SDK's `SearchSchema`, keep the hits inside the owned subtree; page-title match only as a fallback |
| `MarketplacesEditorViewModel`, `EnabledPluginsEditorViewModel` | neither | Match by page title only |

⚠ **Search dispatches on those two interfaces, never on the concrete type.** It used
to pattern-match `editor is SettingsGroupEditorViewModel` and carry a hardcoded
type→JsonPath map, which was defensible only while the walk and the editors shared an
assembly. The walk is neutral now, so a new specialised page becomes searchable by
declaring `IJsonPathScopedEditor` — there is no central list to update.

A node with `Editor == null` is a header (section divider); never add a result that navigates to a header node.

## §3 `SearchViewModel` contract

⚠ **`SearchViewModel` lives in `src/AgentForge.Avalonia.Shell/Search/`, not here.**
Phase 5 slice 3 moved the whole search machinery — the debounce, the tree walk, the
schema flattening, the snippet, the result cap — to the neutral shell. This app
supplies the parts that are Claude's.

It is decoupled from the SDK by construction: it receives delegates, not
`IAgentConfigClient` references.

```csharp
new SearchViewModel(
    getNavigationTree:        () => NavigationTree,
    isLoadingProbe:           () => IsLoading,
    getSyntheticEntries:      () => ClaudeSyntheticSearch.Build(NavTitleClaudeCode),
    getSchemaSearchProviders: BuildSchemaSearchProviders)
```

**Why delegates, not SDK refs?**

- Keeps `SearchViewModel` unit-testable without Avalonia or SDK dependencies.
- Nav tree is already in-memory; schema nodes inside `SettingsGroupEditorViewModel`
  are the same objects built from `SchemaTreeBuilder.BuildTopLevel` — no double-fetch needed.
- Every delegate is re-invoked **per search pass**, so a workspace reload (which
  rebuilds the tree and swaps the SDK clients) needs no re-wiring. `getSyntheticEntries`
  is re-invoked for a second reason: its rows carry localized text, and `Strings` is not
  culture-aware until `ApplyCulture` runs in `Program.Main`. **A cached entry list would
  pin the startup culture into every row.**

### §3.1 Synthetic rows are this app's statement — `ClaudeSyntheticSearch.cs`

Pinned rows for queries no schema property matches: `--dangerouslySkipPermissions`
(a CLI flag with a config equivalent), the `bypassPermissions` deep link, and the
Essentials-card triggers. All three used to be hardcoded inside the search VM; they
are now `SyntheticSearchEntry` records the product hands over.

| Piece | Owner |
|---|---|
| Which phrases exist, what a row says, where it lands | **this app** — `ClaudeSyntheticSearch.Build(sectionTitle)` |
| When a phrase matches a query | **the shell** — `SearchTrigger` |
| Ordering, suppression, node resolution, row construction | **the shell** — `SearchViewModel.AddSyntheticHits` |

`SearchTrigger` has three positive rule kinds and they are **not** interchangeable:

- `Phrases` — bidirectional. Query contains the phrase **or** the phrase contains the
  query. The second direction is what makes partial typing land early (`san` → sandbox).
- `PrefixOf` — query must be a **prefix** of the term. Narrower; use it for one long
  identifier typed from the front, so `skip` does not reach `--dangerouslySkipPermissions`.
- `Mentions` — query must **contain** the term. One-directional, so `pass` does not
  reach a `bypass` row. **Swapping this for `Phrases` silently widens the row.**

Plus `Excluding` (veto — how `disable bypass` avoids the enable row) and
`MinQueryLength`. A trigger with no rules matches **nothing**, deliberately.

The query is lower-cased **and trimmed** once before any rule sees it, so all rule
kinds normalise identically. (They did not before the slice: a leading space defeated
the prefix rules while a contains rule on the same row still fired.)

`Suppresses` names entry ids this row displaces when it fires — resolved after the
whole list is walked, so it works regardless of declaration order, and an entry whose
target page is absent suppresses nothing.

`MainWindowViewModel.SelectSearchResult` is where a clicked row is *acted on*, and it
stays here: the bypass row lands on the Permissions Overview tab and calls
`permEditor.ActivateBypassHint()`; the danger row calls `ActivateDangerHint()` and
expands Advanced; an Essentials row activates the card's amber callout.

When adding a synthetic, keep the trigger distinct from existing ones and add a
`SearchViewModelTests` / `SearchViewModelBypassTests` case (present-vs-absent node,
distinctness).

**If you need SDK-backed search** beyond this (ranking, non-GUI consumers), use
`IAgentConfigClient.SearchSchema(query)` and map results back to nav nodes via the
path-to-node lookup in §5. See `src/AgentForge.Sdk/AGENTS.md §2` for the SDK /
navigation boundary contract.

**Internal test surface** (visible via `InternalsVisibleTo("ClaudeForge.Tests")` on
the shell):

- `SearchViewModel.ExecuteSearch(string query)` — `internal`; drives matching directly,
  no debounce, no dispatcher. Safe to call from unit tests.
- `SearchViewModel.AddSyntheticHits(query)` — `internal`; drives the pinned-row walk alone.
- `SearchViewModel.FlattenSchemaNodes(nodes)` — `internal static`; depth-first schema walk.
- `SearchViewModel.BuildSnippet(text, query, maxLen)` — `internal static`; excerpt helper.
- `SearchViewModel.StripPhraseQuotes(query)` — `internal static`; phrase-quote stripping.

⚠ `SearchTrigger` and the entry walk are covered by `SearchTriggerTests` and
`SearchViewModelSeamTests`, which drive a **fabricated** product — no Claude table, no
Claude editor types. That is the point: the Claude-fixture tests cannot tell you
whether a second product can reach the same behaviour.

## §4 Specialized editors — search implications

When adding a new specialized editor page:

1. Create the editor VM (e.g. `FooEditorViewModel`).
2. Register in `NavigationTreeBuilder` so a `NavigationNodeViewModel` is created with
   `Editor = new FooEditorViewModel(...)`.
3. **Nothing to update in search** — the walk handles any editor by page title. If the
   page is rooted at one JSON path and should surface property-level hits, declare
   `IJsonPathScopedEditor` on the VM and return that path; if it renders a flat schema
   list, declare `ISchemaGroupEditor`. There is no central type map to edit.
4. Add a `SearchViewModelTests` test case for the new page (see
   `ExecuteSearch_SpecializedEditor_MatchedByPageTitle` as a template).

**Known gap — global search does not find Agents & Skills *items*.** That page is
matched by page TITLE only, so global search surfaces config properties but not
individual skills / agents / commands. Now that artifacts have stable item keys
(`NavDeepPath.FormatItemKey`) and reveal-by-filter exists, `SelectSearchResult`
could reuse the same restore machinery to deep-link straight to one. Deliberately
not done yet — it is a scope decision, not an oversight.

## §4b Page navigation lifecycle — `INavigablePage`

A page that must re-read something on arrival, or drop transient state on the way
out, says so itself:

```csharp
public sealed partial class FooEditorViewModel : ObservableObject, INavigablePage
{
    public void OnNavigatedTo() => Refresh();
    public void OnNavigatedFrom(bool replaced) { if (replaced) ApplyNavigationFilter(null); }
}
```

Both members have default no-op bodies — implement only the half you need.
`MainWindowViewModel.OnSelectedNodeChanged` calls them through the interface and
knows nothing about any page type.

⚠ **`replaced` is load-bearing, and it is the half that is easy to get wrong.** It is
`false` when the incoming editor is *this same instance*, which happens because
several pages deliberately survive a workspace reload and are re-attached to a
freshly built node. A page that discards a typed filter unconditionally would throw
it away on every reload — the user never navigated away.

**Why this replaced a type switch.** `OnSelectedNodeChanged` used to be a 128-line
chain of `editor is SomeConcreteViewModel`, one arm per page, each calling that
page's differently-named refresh method (`Refresh`, `Reload`, `Activate`,
`RefreshConfigAvailability`, `RefreshAsync`). Two costs: the host had to name every
page type in the app, and **a newly added page was simply never refreshed until
someone remembered to extend the chain** — no compiler signal, and no symptom beyond
content that is quietly stale.

⚠⚠ **This entire surface was uncovered.** Deleting the leave dispatch failed **zero**
of 2,910 tests, and forcing the `replaced` flag to a constant in *either* direction
also failed zero. `tests/ClaudeForge.Tests/Headless/NavigationPageLifecycleTests.cs`
now pins it, including with a page type this app does not own — which is the only way
to prove dispatch goes through the interface rather than a list of known types.

Pages implementing it today: settings groups (`Activate` / `Deactivate`), Agents &
Skills, Profiles, Backup / Restore, About, Memory, Essentials, Environment.

## §4c Splitting the schema into pages — `SchemaPageLayout`

`NavigationTreeBuilder` holds three tables — property→page, page order, page
descriptions — and hands them to the neutral
`SchemaPageLayout.Arrange(nodes)`, which does the bucketing and ordering. The tables
are Claude knowledge; the arrangement is not.

Rules worth knowing before editing the tables:

- A property absent from the map lands on `FallbackPage` (`"Advanced"`).
- Pages come out in `PageOrder`; a listed page with no properties is skipped.
- A page named in the map but **missing from `PageOrder` still renders** — appended
  after the ordered pages, sorted by title. So a typo in either table quietly moves a
  whole page to the bottom of the tree and changes nothing else.
  `SchemaPageLayoutTests.ClaudeLayout_EveryMappedPageIsAlsoOrdered` is the guard.
- Property order within a page is the schema's, not alphabetical.

## §4d Save / restore confirmation dialog — `SaveDialogText`

The dialog's model and its builder live in `src/AgentForge.Avalonia.Shell/Save/`.
Nothing in them is product-specific: which documents are dirty, how the diffs are
computed, how paths are shortened to `~/…`, how values are truncated, how the summary
counts read — all identical for any layered-config product.

The one piece that is ours is the **wording**, handed over as a `SaveDialogText`:

```csharp
SaveChangesDialogViewModel? vm =
    SaveDialogBuilder.Build(DirtySources(), ClaudeSaveDialogText.Create(), isRestoreContext);
```

`ClaudeSaveDialogText.Create()` is called **per invocation**, not cached — `Strings` is
not culture-aware until `ApplyCulture` runs in `Program.Main`, so a static table would
pin the startup culture.

⚠⚠ **Why the keys did NOT move into a resource set beside the dialog** — and why yours
should not either. `LocalizationParityTests` finds its resx files by walking to
`src/ClaudeForge/Localization` **by hardcoded path**. A resource set anywhere else is
checked by nothing: not the every-key-in-every-locale contract, not the `TODO`
-placeholder rejection, not the copy-of-English detection. `ClaudeForge.Avalonia`
already demonstrates the consequence — 93 strings, zero locale siblings, no failing
test. Moving these twelve translated keys out of the guarded directory would have
silently un-translated them in eight locales while looking like tidier architecture.
**Neutral code takes its words as data; it does not get its own resx.**

Three members are `required` on purpose:

- `SaveChangesDialogViewModel.Text` — a dialog inheriting another product's wording is
  worse than one that fails to compile.
- `SaveChangeSectionViewModel.ActionVerb` — it used to default to the *save* label,
  which is precisely the wrong answer inside a restore preview.
- `SaveChangeEntryViewModel.KindAccessibleName` — the change pill renders a bare
  `+`/`-`/`~` glyph, so an empty automation name reads to a screen reader as nothing at
  all. A compile error is the only thing that reliably prevents that.

**The view stays here** (`src/ClaudeForge/Views/SaveChangesDialog.axaml`). Moving it
would drag three converters that five to nine other AXAML files also use — a converter
migration that is really a `LayeredEditors.Avalonia` question, not a shell one.

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
        if (child.Editor is not ISchemaGroupEditor groupEditor) continue;
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

`AgentConfigClientCore.WorkspaceForGui` (internal property) returns the live
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
| `ClaudeSyntheticSearch.cs`                 | This app's pinned search rows: trigger phrases, nav targets, card titles     |
| `ClaudeSaveDialogText.cs`                  | This app's wording for the save / restore dialog (12 keys); model is in the shell |
| `../Services/NavigationTreeBuilder.cs`     | This app's schema→page tables; arrangement is `SchemaPageLayout` (see §4c)   |
| ⚠ `SearchViewModel.cs` / `SearchResultViewModel.cs` | **moved** — `src/AgentForge.Avalonia.Shell/Search/` (see §3) |
| `SettingsGroupEditorViewModel.cs`          | Generic property group editor; exposes `SchemaNodes`, `Editors`, `GroupName` |
| `Editors/PermissionsEditorViewModel.cs`    | Specialized editor; `IJsonPathScopedEditor` ⇒ `"permissions"`                |
| `Editors/HooksEditorViewModel.cs`          | Specialized editor; `IJsonPathScopedEditor` ⇒ `"hooks"`                      |
| `Editors/McpServersEditorViewModel.cs`     | Specialized editor; `IJsonPathScopedEditor` ⇒ `"mcpServers"`                 |
| `Editors/MarketplacesEditorViewModel.cs`   | Specialized editor                                                           |
| `Editors/EnabledPluginsEditorViewModel.cs` | Specialized editor                                                           |

## §8 Test seams

| Seam                                    | File                             | Usage                                                           |
|-----------------------------------------|----------------------------------|-----------------------------------------------------------------|
| `GetClaudeCodeSdkClientForTesting()`    | `MainWindowViewModel.cs`         | Access live SDK client for mutation-based integration tests     |
| `SearchViewModel` delegate constructor  | `AgentForge.Avalonia.Shell/Search/SearchViewModel.cs` | Pass fake `getNavigationTree` + `isLoadingProbe`; omit `getSyntheticEntries` for a product-free fixture |
| `PlatformPaths.TestUserProfileOverride` | `Core/Platform/PlatformPaths.cs` | Redirect `~/.claude/` to sandbox                                |
| `DebugFlags.ResetForTesting()`          | `Services/DebugFlags.cs`         | Reset flags + `PlatformInfo.Current` in `[TestCleanup]`         |
