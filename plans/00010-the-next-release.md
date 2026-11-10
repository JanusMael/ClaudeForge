# 00010 — The next release: new models, Ultracode corrected, quality packages adopted

> Status: **approved 2026-11-10**. **Supersedes `00009`** in part: see *What stands from
> 00009*. ⓘ The AgentForge repository move, once named `00009` by `plans/00008`, now takes
> `00011` or later.

The maintainer wants one ClaudeForge release, after `v2026.3.922`, to carry three things together:
the current model lineup, a correct Ultracode, and the adoption of the Inspections quality packages
(maintainer, 2026-11-10). `00009` cut its app release straight after its Phase 2 (decision 15) and
left Phase 3, the quality adoption, for later. This plan replaces that ordering, corrects 00009's
Phase 3 mapping against a measurement, and adds the Ultracode correction.

## What stands from 00009

| 00009 part | Status |
|---|---|
| Phase 1, the dependency PR | ✅ **Done**: #138, merged 2026-11-10 with 17/17 green. The ten Dependabot PRs are closed |
| Phase 1b, the three new models (decisions 9–10) | **Stands**, with one change: the rows carry no `ultracode` (decision 3 below) |
| Phase 2 steps 1–5: the schema PR from `fix/opencode-schema-redesign`, closing #114, the `schema-refresh.yml` fix (decisions 11–13) | **Stands** |
| Phase 2 steps 6–8, the package release and pin | **Moved** to the end: one release after everything else (decision 1) |
| Decision 14, sequencing with `00008` | **Stands**: `00008` stays paused until this plan's pin merges |
| Decision 15, app release straight after Phase 2 | **Replaced** by decision 1 |
| Phase 3 and decisions 7–8 | **Replaced** by *Quality packages* below. The mapping table was wrong in three places (see *Measured*) |
| Decision 5, `AppServices.EntryPoint` (measure; adopt or decline, recorded) | **Stands**: Q step 7 |

## Measured 2026-11-10

Two throwaway worktrees, one per package, with the findings reconciled against the in-tree guards by
a third reader. AssemblyQuality `2026.4.1031` ran over the 8 `src` assemblies; CodeQuality
`2026.4.1104` ran over the 8 `src` projects, Debug, with `TreatWarningsAsErrors=false` so every
diagnostic was counted.

| Rule | Findings | What they are | Counterpart in the tree |
|---|---|---|---|
| BNAQ1001 / BNCQ1001: `CancellationToken` with a default value | **32 / 32**, all in `AgentForge.Core` | Real against the house rule. 9 of the methods have optional parameters before or after `ct`, so making it required means reordering them or making those required too | `PublicSurfaceContractTests.EveryAsyncMethod_HasRequiredCancellationToken`, **Sdk only**, clean. ⚠ 00009 said "None" |
| BNAQ1002 / BNCQ1002: `System.Text.Json.Nodes` in a public signature | **60 / 95**. Sdk: **0** under BNAQ1002, **3** under BNCQ1002, which walks transitively | By design: `JsonNode` is the settings model's currency in Core, Abstractions and Shell | `PublicSurfaceContractTests.NoPublicApi_LeaksSystemTextJsonNodes`, **Sdk only**, direct members. ⚠ 00009 named the baseline test |
| BNAQ1003: forbidden reference by layer | **0**; inspected 69 and 71 | Clean | `AssemblyLayeringTests`, reflection half. Its csproj-XML half sees what this cannot |
| BNAQ1004 / BNCQ1004: a namespace segment shadows a referenced root | **13 / 52**, one cause | `.Avalonia` in `AgentForge.Avalonia.Shell` (40 files, published) and `ClaudeForge.Avalonia` (12 files). Latent: nothing qualifies `Avalonia.*` today | None |
| BNAQ1005: friend grants against an allow-list | **0**; inspected 112 | Clean | ⚠ `SharedFriendGrantsTests` has **no** allow-list half, so this only adds coverage. 00009 said "replace" |
| BNAQ1006: references resolve, internals still granted | **0**; inspected 1,770 | Near-vacuous in project mode | None. It is meaningful in `package-canary.ps1`, where published binaries meet newer dependencies |
| BNCQ1005–1007: System.Reactive | **0, inert** | No `src` project references System.Reactive, so nothing was checked | Must not be counted as a guard |
| BNCQ1008: gate released outside `finally` | **0**, live | 11 gate calls in 5 files, all clean | None |
| BNCQ1009: uncaught callback | **4**, `ConfigFileWatcher.cs:106-109` | One real route: `ConfigFileWatcher(TimeSpan debounce = default)` accepts a negative debounce, and `Task.Delay` throws inside the watcher callback, which ends the process | None |
| BNCQ3001: `DispatcherTimer.Tick` outside try/catch | **1**, `MainWindow.axaml.cs:174` | Real, unlikely: `WindowStateService.Save` catches only `IOException` and `UnauthorizedAccessException` | None |

**Ultracode** (a read-only map of every consumer, plus a completeness critic):

- **Where it appears.** Only in `model-catalog.json` (the `effortLevels` entry, order 5, and seven models' `supportedEffortLevels`), both enums in `model-catalog.schema.json`, one test (`ModelCatalogTests.cs:54-74`), two test comments and `CHANGELOG.md`.
- **What the user sees.** Nothing. The one effort editor filters to persistable levels, and nothing writes the value.
- **What it actually is.** Claude Code's settings schema has no enum containing `ultracode`. It is the prompt keyword that starts a dynamic workflow (`workflowKeywordTriggerEnabled`), and it *"reports as xhigh"*. Claude Desktop now shows it as a toggle beside the effort list, not as a sixth level.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **One package release and one app release, after every change below has merged** (maintainer, 2026-11-10). ⚠ **The red window, precisely.** `Published Version` (ci.yml:255) tests and trim-publishes against the *published* `AgentForge.*` at the pin, so it goes red at U's merge and stays red until P's pin merges. `Feed Restore` also goes red from R's merge, because the new Shell id does not exist at `2026.4.1019`. While they are red, they cannot report a **new** package-mode failure. **Substitute gate:** before every merge from U to Q, run `scripts/package-canary.ps1` locally and require it to pass. It packs the branch's own `AgentForge.*`, then builds, tests and trim-publishes against them, which is what `Published Version` would prove after the pin. Each red log is also read, and merged over only when its cause is the stale pin or the missing id | One CalVer day, and the release notes describe one coherent change. The red window is the price, and it was chosen. The canary is what keeps the window from also being a blind spot |
| 2 | **Ultracode leaves the effort vocabulary entirely**: the `effortLevels` entry, every model list, and both catalog-schema enums. No per-model flag replaces it. The catalog's own schema becomes the guard | Every consumer either filters it out or ranks it on an axis it is not on. A flag would need a consumer, and none exists: ClaudeForge cannot start Ultracode |
| 3 | **00009 Phase 1b's three new rows are written without `ultracode`**, so decision 2 lands first | Otherwise the rows are written and then edited again |
| 4 | **Ultracode becomes visible where effort is chosen** (maintainer, 2026-11-10). Three Essentials cards beside *Effort level*: `workflowKeywordTriggerEnabled` (Bool, *the "ultracode" keyword starts a workflow*), `disableWorkflows` (Bool) and `workflowSizeGuideline` (EnumString). Existing card kinds, real translations in all eight locales | These are the only persisted settings tied to Ultracode, and today they are reachable only in the generic tree. Localization parity is build-gating, so the translations are part of done |
| 5 | **The 32 defaulted `CancellationToken`s in `AgentForge.Core` become required in this release** (maintainer, 2026-11-10). In the 9 methods where an optional parameter precedes `ct`, that parameter becomes required too. Do not reorder: reordering would break binary compatibility as well as source. BNAQ1001 then runs over every packed assembly, and BNCQ1001 is turned on | It is a source-breaking change to a published package. It shares a release with decision 7's break, so consumers adapt once. ⚠ The parked `feat/agentforge-opencodeforge` branch breaks, and adapts when it rejoins |
| 6 | **The `JsonNode` rules are enforced on `AgentForge.Sdk` only, as an explicit exception to 00009 decision 8** (maintainer, 2026-11-10). BNAQ1002 scoped to Sdk replaces `NoPublicApi_LeaksSystemTextJsonNodes` once it has been canaried. BNCQ1002 is disabled per project outside Sdk, each with a written reason. Its 3 transitive Sdk sites are suppressed per site | `JsonNode` is the settings model by design. Redesigning it would dominate the release |
| 7 | **`.Avalonia` becomes `.AvaloniaUI` in both projects** (maintainer, 2026-11-10): the assembly, the namespace and the folder. `AgentForge.Avalonia.Shell` → `AgentForge.AvaloniaUI.Shell`, with the package id `Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell`. `ClaudeForge.Avalonia` → `ClaudeForge.AvaloniaUI`. ⚠ The old package id stops at `2026.4.1019` on the feed; nothing deletes it | The rename that holds, per the rule's own docs. The `AgentForge.*` family prefix is unchanged, so the package-mode selector, `PackageMetadataTests` and `nuget.config`'s mapping still match by prefix. That has to be measured, not assumed (step R4) |
| 8 | **Every AssemblyQuality test asserts zero findings, `Skipped == 0` and a non-zero inspected count.** It discovers its assemblies the way the in-tree guards do (glob, `IsPackable`), never from a literal list. BNAQ1005's allow-list is derived from the project names under `src/` and `tests/`, never copied from the grants file | A list copied from what it checks is a tautology. A literal assembly list silently stops covering a new project |
| 9 | **BNAQ1006 also runs inside `scripts/package-canary.ps1`** | That is the one place where published binaries meet newer dependencies, which is the case the rule exists for |
| 10 | **BNCQ1005–1007 are recorded as inert in `PROGRESS.md`**, and are not counted as guards | A zero that checked nothing reads exactly like a clean result |
| 11 | **BNCQ1009 and BNCQ3001 are fixed, not suppressed.** `ConfigFileWatcher` rejects a negative debounce (`ArgumentOutOfRangeException.ThrowIfNegative`), and its callbacks log rather than throw. `OnSaveDebounceTick` is wrapped in a try/catch that logs | Both are reachable. The first ends the process |

## Order of work

Each item is one PR on its own branch from `main`. The suite is counted across all six test assemblies before every merge.

| # | PR | Package-visible? |
|---|---|---|
| U | Ultracode leaves the catalog (decisions 2–3) | Yes, Core data |
| M | 00009 Phase 1b, the three new models, without `ultracode` | Yes, Core data |
| S | 00009 Phase 2 steps 1–5: the schema PR, closing #114, the refresh-bot fix | Yes, Core data |
| R | The `.AvaloniaUI` renames (decision 7) | Yes, the Shell id |
| C | Required `CancellationToken`s in Core (decision 5) | Yes, Core API |
| Q | The quality packages (decisions 6, 8–11) | Yes, through the BNCQ1009 fix in Core |
| W | The Essentials workflow cards (decision 4) | App only |
| P | Package release and pin | — |
| A | App release | — |

### U — Ultracode leaves the catalog

1. Remove `ultracode` from `model-catalog.json` and from both enums in `model-catalog.schema.json`. Run `validate-model-catalog.ps1`.
2. Rewrite `ModelCatalogTests.SessionOnlyEffortLevels_MirrorFullRange_ButStayNonPersistable` as `MaxIsTheTopEffortLevel_AndTheOnlySessionOnlyOne`. It asserts:
   - the ordered ids are exactly `low, medium, high, xhigh, max`;
   - `max` is the only non-persisting level;
   - no model names an id outside that set.
3. Update the comments: `ModelCatalogSchemaParityTests` (:21, :45), `ModelCatalogTests.cs:47`, `ModelCatalog.cs` (:22-25, :125, :166), `IModelCatalogAccessor` (:28-44), and the hard-coded test count in `model-catalog-refresh.yml` if it changes. Add a `docs/MODEL-CATALOG.md` bullet saying Ultracode is a workflow mode, not an effort level, and a CHANGELOG *Fixed* line.
4. **Canary.** Plant `"ultracode"` in one model's list. Both the schema-validation test and the rewritten test must go red. Revert.
5. ⓘ **One behaviour change.** A hand-edited `"effortLevel": "ultracode"` (already invalid per the settings schema) used to coerce to `xhigh` on a model change. Now it falls back to the model's default. `PublicSurface/*.txt` must not move.

### M, S

As 00009 Phase 1b and Phase 2 steps 1–5, with decision 3 above. The default effort for all three new rows is `medium`, Claude Code's documented default (maintainer, 2026-11-10).

### R — the renames

1. `git mv` both project folders and csproj files, and the test project `tests/ClaudeForge.Avalonia.Tests` → `ClaudeForge.AvaloniaUI.Tests`. Rename the namespaces, `RootNamespace`, AXAML `x:Class` and `xmlns`, `ClaudeForge.slnx`, `AssemblyInfo.InternalsVisibleTo.cs`, and every `ProjectReference`.
2. Rename `PublicSurface/AgentForge.Avalonia.Shell.txt` to match. `PublicSurfaceBaselineTests` writes the new surface beside it as `<name>.txt.actual`; copy that over the baseline in the same commit, and review the diff: it must be names only.
   - The product-neutrality allow-list (`ProductNeutrality/allow-list.tsv`, keyed per file) also moves with the files. Regenerate it with `ProductNeutralityTests.WriteAllowList` and review: paths change, and **no count grows**.
3. Read the four-place coupling in CLAUDE.md: the root `Directory.Build.targets` selector, `PackageMetadataTests`, `AssemblyLayeringTests`' selector, and `nuget.config`'s `packageSourceMapping`. Confirm each still matches by family prefix, and change any literal it holds.
4. **Measure, don't assume.** Run `package-canary.ps1` and see the new id packed, restored and consumed. Search case-insensitively for `Avalonia.Shell` and `ClaudeForge.Avalonia` outside generated trees: only history (CHANGELOG released sections, frozen plans) may remain.
5. Docs: CLAUDE.md, AGENTS.md, `PROGRESS.md`. The frozen plans keep the old names.
   - ⚠ `plans/00008`'s paused step 4 creates `AgentForge.Avalonia.Shell.Tests`. `PROGRESS.md` records that it will be `AgentForge.AvaloniaUI.Shell.Tests` instead, as drift against the frozen plan.

### C — required tokens

1. Remove `= default` from the 32 `ct` parameters. In the 9 methods with an optional parameter before `ct`, make that parameter required as well. Fix the callers in `src/` and `tests/`.
2. Update `PublicSurface/AgentForge.Core.txt` from its `.txt.actual`, and review it: only the defaults change. Several of the 32 methods carry `Claude` in their names (e.g. `GetClaudeCodeSettingsNodeAsync`), so the product-neutrality allow-list's line texts move too. Regenerate it with `WriteAllowList`: no count may grow.
3. CHANGELOG: a **Breaking** entry for `Bennewitz.Ninja.AgentForge.Core` consumers, naming the methods.

### Q — the quality packages

1. **AssemblyQuality** in `ClaudeForge.Tests`, one test per rule (decision 8):
   - BNAQ1001 over every packed assembly;
   - BNAQ1002 scoped to Sdk;
   - BNAQ1003 with the layering prefixes, the csproj-XML half of `AssemblyLayeringTests` staying;
   - BNAQ1004 over all, which is clean after R;
   - BNAQ1005 against the derived allow-list;
   - BNAQ1006.
2. **Canary before any removal.** Plant a defaulted `ct` in Sdk, then a `JsonNode` in an Sdk signature. Each must redden both the old `PublicSurfaceContractTests` test and the new rule. Only then remove the two old tests and repoint their `AGENTS.md` rows.
3. **CodeQuality** for every `src` project, centrally, `PrivateAssets="all"`, BNCQ1001 on. BNCQ1002 is disabled outside Sdk with a reason in each project, and the 3 Sdk sites are suppressed per site (decision 6).
4. Fix BNCQ1009 and BNCQ3001 (decision 11), each with a test that drives the bad input: a negative debounce, and a throwing save.
5. Add BNAQ1006 to `package-canary.ps1` (decision 9). Record the inert rules in `PROGRESS.md` (decision 10).
6. **The build must be zero under the real `TreatWarningsAsErrors=true`**, in Debug **and** a package-mode Release publish. The measurement saw neither.
7. Measure `Program.cs`'s unhandled-exception plumbing against `AppServices.EntryPoint`, which already arrives transitively since #138. Record adopt or decline, with the reason, in `PROGRESS.md` (00009 decision 5). Adopting it is a separate reviewed change, not part of this release.

### W — the workflow cards

1. Three cards beside *Effort level* in `EssentialsViewModel`, reading and writing the three settings through the existing card kinds. The keyword card's body says the keyword is ignored while workflows are disabled. Every card names the Claude Code version that introduced its setting, as the schema does (v2.1.157 for the keyword trigger). Title, body and automation name come from resx, with translations in all eight locales, and synonyms are added to `ClaudeSyntheticSearch`.
2. Tests: each card round-trips its setting through `settings.json`, an unset value shows as inherit, and the enum card offers exactly the schema's four values.
3. `docs/ESSENTIALS-PAGE.md`.

### P — package release and pin

1. `scripts/Publish-Packages.ps1 -PackageVersion 2026.4.MMDD -FeedUser JanusMael -PreflightOnly`, then the `packages-v2026.4.MMDD` tag.
2. Verify all five ids on the feed through the service index's flat container. That includes the **new** `Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell`, which has no history there.
3. Check the downloaded `AgentForge.Core`. It must embed:
   - the 48-property `opencode-config.json`;
   - the three new model rows;
   - no `ultracode`;
   - the required-token signatures.
4. Pin PR: `Published Version` must be green.

### A — app release

The release PR, carrying the CHANGELOG `## [Unreleased]` → version, is the last merge before the `v*.*.*` tag. Its description becomes the release notes. Signing and the winget submission are the maintainer's. Confirm the shipped binary by running it: the three new models on the *Model & Effort* page, and the workflow cards on Essentials.

## Done when

- [ ] U, M, S, R, C, Q and W are merged, each with the suite green across six assemblies.
- [ ] No `ultracode` remains in the catalog or its schema, and the canary was seen red.
- [ ] `.Avalonia` survives only in history. The new Shell id is on the feed, verified.
- [ ] Every BNAQ rule test asserts zero findings, `Skipped == 0` and inspected > 0. Both replaced tests were canaried first.
- [ ] CodeQuality is on for every `src` project, and the build is zero under `TreatWarningsAsErrors`.
- [ ] One `packages-v2026.4.MMDD`, pinned, with `Published Version` green.
- [ ] The app release is tagged and confirmed in the shipped binary.
