# 00009 — Dependency refresh, new models, one package release, and the Inspections quality packages

> Status: **approved 2026-11-10**. Supersedes nothing. ⓘ `plans/00008` names `00009` as
> the AgentForge repository move. That move takes `00010` or later instead (maintainer,
> 2026-11-10); `00008` is frozen and keeps its wording.

Since the last package release, `packages-v2026.4.1019`, four things have piled up. #114, the
OpenCode schema redesign, has been open since 2026-10-12, waiting on a package release, and the
weekly refresh bot has since erased its test changes from the remote.
Dependabot's queue is full of stale bumps. Three new Claude models have shipped that the model
catalog does not know about. And XamlQuality has moved into `Bennewitz.Ninja.Inspections`, which
now also ships AssemblyQuality and CodeQuality; nothing here uses either. This plan clears all
four in an order that spends **one** CalVer day on a package release rather than several.

## Measured 2026-11-10

| What | Measurement | How |
|---|---|---|
| Outdated top-level packages | **20 ids** (table below): 5 Avalonia, 8 Bennewitz.Ninja, 6 Microsoft/xUnit test-side, 1 Tmds | `dotnet list ClaudeForge.slnx package --outdated` on `main` at `7482f4b7`, with the GitHub feed credential handed to NuGet |
| Open PRs | #114, plus **ten Dependabot PRs** (#125, #127–#131, #133–#136), five of them already behind nuget.org | `gh pr list` |
| Dependabot is throttled | `open-pull-requests-limit: 10` and exactly ten are open, so **no new bump has been proposed since 2026-11-02**. That is why there is no Avalonia 12.1.6 PR | `.github/dependabot.yml` |
| ⛔ #114's branch no longer holds the redesign's test changes | `schema-refresh.yml` runs every Monday and recreates `chore/schema-refresh` from `main` through `peter-evans/create-pull-request` (`delete-branch: true`). On 2026-11-09 it reset the branch to a single bot commit, `4aeb1ead`, on top of `7482f4b7`. **That commit changes only `claude-code-settings.json` and `opencode-config.json`.** The test changes, `5134ef47` (the guards for the 48-property root, `ModelRefSites`, the synthetic root-`$ref` test and the `backup.json` golden), now exist **only in the local branch**. CI on #114 would therefore go red on the very tests that commit fixes | `git log origin/chore/schema-refresh`, `git diff --stat origin/main...origin/chore/schema-refresh`, `git branch -r --contains 5134ef47` (none), `.github/workflows/schema-refresh.yml` |
| Shared packages' Avalonia floor | `ScopedEditors.Avalonia` and `AppServices.Avalonia` 2026.4.1110 depend on **Avalonia ≥ 12.1.3**, DataGrid ≥ 12.1.2, Semi.Avalonia ≥ 12.1.0 | Their nuspecs on nuget.org |
| New transitive package | `AppServices.Avalonia` 2026.4.1110 depends on the new **`AppServices.EntryPoint`** | Same |
| XamlQuality usage | One test project (`ClaudeForge.Tests`), three test files, namespaces `Bennewitz.Ninja.XamlQuality` and `.Rules` | Search |
| Inspections releases | Per-family tags since 2026-10-29: `xamlquality/v2026.4.1109`, `assemblyquality/v2026.4.1031`, `codequality/v2026.4.1104` | `gh release list` |
| Model lineup vs the catalog | **Three new current models the catalog does not have**: `claude-fable-5-2`, `claude-sonnet-5-5`, `claude-haiku-5-5`. Each takes its family alias from a row that becomes legacy (Fable 5.1, Sonnet 5, Haiku 4.5). Opus 5.5 is current and already in the catalog. The page's legacy list also names **Opus 4.5**, which the catalog has never carried | The models overview (`platform.claude.com/docs/en/about-claude/models/overview.md`), read 2026-11-10, against `model-catalog.json` on `main` |

### The model rows that change

Read from the models overview on 2026-11-10. Every row is re-read on the day of the edit (decision 9).

| Family alias | Today resolves to | Becomes | New row's API facts | Outgoing row |
|---|---|---|---|---|
| `fable` | `claude-fable-5-1` | `claude-fable-5-2` | 1M context, 128K output, default effort `high`, adaptive thinking always on | `claude-fable-5-1` → `legacy: true`, `alias: null` |
| `sonnet` | `claude-sonnet-5` | `claude-sonnet-5-5` | 1M, 128K, default effort `high`, adaptive thinking | `claude-sonnet-5` → legacy |
| `haiku` | `claude-haiku-4-5` | `claude-haiku-5-5` | 1M, 128K, default effort **`medium`**, adaptive thinking | `claude-haiku-4-5` → legacy |
| `opus` | `claude-opus-5-5` | unchanged | — | — |

⚠ **Haiku crosses a generation, so its capabilities cannot be copied from the outgoing row.** The models page gives Haiku 5.5 a default effort, so it supports effort at least through the API. Whether Claude Code offers effort, auto mode and 1M for it has to be read from Claude Code's own documentation, which is the source `docs/MODEL-CATALOG.md` step 1 names for those three fields.

### The outdated packages

| Package | Now | Latest | Where | Rides the package release? |
|---|---|---|---|---|
| Avalonia, .Desktop, .Fonts.Inter, .Themes.Fluent, .Headless | 12.1.4 | **12.1.6** | Shell, ClaudeForge, ClaudeForge.Avalonia, ClaudeForge.Tests | **Yes**: `AgentForge.Avalonia.Shell` declares Avalonia, so its version becomes the consumers' floor |
| Bennewitz.Ninja.AppServices (+ .Abstractions, .Avalonia, .Logging) | 2026.4.1001 | **2026.4.1110** | Shell, Sdk, ClaudeForge, ClaudeForge.Avalonia | **Yes**: Shell and Sdk |
| Bennewitz.Ninja.ScopedEditors (.Abstractions, .ViewModels, .Avalonia) | 2026.3.924 | **2026.4.1110** | Shell, ClaudeForge | **Yes**: Shell. ⚠ Six weeks of upstream change, so it may break the API |
| Bennewitz.Ninja.JsonC | 2026.3.926 | 2026.4.1031 | Core | **Yes** |
| JsonSchema.Net | 9.4.1 | 9.6.0 | Core | **Yes**. ⚠ It parses every schema; `ARefCycle_IsRejectedWhenTheSchemaIsBuilt_NotWhenItIsWalked` pins its error text |
| Bennewitz.Ninja.AutoVersioning | 2026.3.928 | 2026.4.1031 | all 15 projects, from one `PackageReference` in the root `Directory.Build.props` | Yes. It stamps every package version, and `PackageVersionLockstepTests` guards that |
| Bennewitz.Ninja.XamlQuality | 2026.4.1014 | 2026.4.1109 | ClaudeForge.Tests | No (test-only). ⚠ The bump crosses the package's move into Inspections, so it is measured first alongside ScopedEditors (decision 4) |
| xunit.v3 | 4.0.2 | 4.0.3 | 6 test projects | No |
| Microsoft.Testing.Extensions.TrxReport | 2.5.1 | 2.5.2 | 6 test projects | No |
| Microsoft.Extensions.TimeProvider.Testing | 10.10.0 | 10.11.0 | 6 test projects | No |
| Tmds.DBus.Protocol | 0.95.1 | 0.95.2 | ClaudeForge (explicit pin over Avalonia's transitive copy) | No |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **One hand-made dependency PR replaces the ten Dependabot PRs.** It takes every row above to the latest version as of the day it is made. The ten Dependabot PRs are closed with a comment naming that PR | Five of the ten are already stale. Merging them one by one would mean ten CI runs and ten merge commits, and they would still miss Avalonia 12.1.6, which Dependabot never proposed |
| 2 | **The dependency PR, the model catalog PR and the schema PR go into the same package release.** Order: dependency PR → model catalog PR → schema PR (decision 12) → one `packages-v2026.4.MMDD` → pin bump. ⚠ **`main`'s `Published Version` may stay red from the first of these merges until the pin merges**, because that job builds in package mode against the published `2026.4.1019`. That is the same window `#108` went through; it is kept short by running Phases 1, 1b and 2 back to back | Most of the bumps change what the `AgentForge.*` packages declare or embed. Two package releases would spend two days for no gain, and immutability makes each one permanent |
| 3 | **Avalonia moves as a set, and the floor rule still holds.** Every `Avalonia*` id goes to one version (12.1.6 or newer), never below 12.1.3. DataGrid and Semi stay current, since none is outdated today | The 12.1.3 automation fix is the reason for the floor, and the shared packages now declare it too. Lockstep is what the Dependabot `avalonia` group already assumes |
| 4 | **ScopedEditors and AppServices are measured before they are bumped.** The dependency PR builds once at the new versions before anything else changes. Any API break is fixed in that PR, and the fix and its count are recorded there | A six-week jump in our own libraries is the likeliest place for a break. Finding it in a package-mode build after tagging would be too late |
| 5 | **`AppServices.EntryPoint` arrives transitively and is NOT adopted in this plan.** Phase 3 measures whether `Program.cs`'s own unhandled-exception plumbing duplicates it, and records the answer | Adopting it changes start-up behaviour. That belongs in its own reviewed change, not inside a dependency bump |
| 6 | **Dependabot gets room and a family group.** Raise `open-pull-requests-limit` (to 20) and add a `bennewitz` group over `Bennewitz.Ninja.*`, so a family release arrives as one PR | The limit is why the queue went silent. The group stops one upstream release from opening four PRs again (#127–#129 were one release) |
| 7 | **AssemblyQuality and CodeQuality are adopted rule by rule, each canaried, and no in-tree guard is deleted until the replacement has been shown to go red on the same violation.** The mapping is in Phase 3's table | An `AGENTS.md` row is backed by a named guard test. Swapping that test blind keeps the row's text while dropping its enforcement, which is the exact failure `AGENTS.md` §6 describes. The Inspections README makes the same point: assert on the count of what was inspected as well as on the findings |
| 8 | **CodeQuality starts at warning, not error, with a measured baseline.** Any rule whose baseline is non-zero is either fixed in the adoption PR or suppressed per site with a reason. Nothing is suppressed globally | A new analyzer turned on at error across 15 projects either blocks the build or invites a blanket `NoWarn`. A blanket suppression is a guard nobody can see |
| 9 | **The model catalog is updated in this plan, and rides the same package release.** It follows `docs/MODEL-CATALOG.md`'s *New model launch* checklist for each of the three families: the catalog row, the schema overlay's `model.examples` and `e.g.`, the enum-descriptions tooltips, and the alias asserts in `ModelCatalogTests`. Every fact is re-read from the models overview on the day of the edit, never copied from this plan or from recall | The catalog, both overlays and the descriptions are `EmbeddedResource`s in `AgentForge.Core`. A catalog PR merged after the package release would wait for another one, and the Opus 5.5 release measured that the app ships whatever catalog the pinned package carries, not the working tree's |
| 10 | **Opus 4.5 is not added** (maintainer, 2026-11-10). The catalog has never carried it, and adding a legacy row nobody asked for is scope creep. Revisit only if a user needs to pin it | It is on the page's legacy list, but the catalog never claimed to mirror that list |
| 11 | **The bundled-schema refresh, `claude-code-settings.json` included, lands in the schema PR (decision 12), not in the model catalog PR.** The bot's latest commit already carries a `claude-code-settings.json` diff. Refreshing it again in the catalog PR would touch the same file twice and hash it into the `backup.json` golden twice | The standing rule is that every change batch refreshes the bundled schemas. One PR owning the refresh means the golden is re-minted once |
| 12 | **The OpenCode redesign lands from a human-owned branch, `fix/opencode-schema-redesign`, and #114 is closed in its favour.** The branch starts from `main` after Phase 1b. It re-runs `scripts/refresh-schema.ps1` for today's upstream rather than trusting the bot's Monday copy, then carries `5134ef47`'s test changes forward | Anything pushed to `chore/schema-refresh` is deleted by the next Monday run. That has already happened once, and a merge of `main` into that branch would be deleted the same way |
| 13 | **`schema-refresh.yml` stops destroying human commits, in this plan** (maintainer, 2026-11-10). Before it opens or updates its PR, the run checks whether `chore/schema-refresh` exists and carries any commit not authored by `github-actions[bot]`. If it does, the run leaves the branch alone and comments on the open PR that upstream has drifted again. Otherwise it behaves as today. AGENTS.md gains a row on why | The workflow's force-recreate erased `5134ef47` from the remote with no signal. Keeping one fixed branch keeps one drift PR at a time. Per-run dated branches would also have stopped the loss, but at the cost of one stale PR per drifting week |
| 14 | **Plan 00008 pauses until Phase 2's pin merges; after that, 00008 Phase B and this plan's Phase 3 interleave** (maintainer, 2026-11-10). Whichever lands second rebases over the other's edits to `AssemblyLayeringTests` and `SharedFriendGrantsTests` | Phases 1–2 are short and mechanical. Phase 3 and 00008 Phase B are each long, and neither depends on the other's outcome |
| 15 | **The app release is cut straight after Phase 2** (maintainer, 2026-11-10). It does not wait for Phase 3 | It gets the three new models to users within a day of the pin, as Opus 5.5 was. Phase 3's fixes ride a later release |

## Phase 1 — the dependency PR

1. Branch `build/deps-2026-11` from `main`.
2. Bump every row of the outdated table to the latest version on that day. Re-run the outdated listing first, since that day may be later than this measurement.
3. Build in project mode and fix any ScopedEditors or AppServices breaks (decision 4).
4. Run the full suite and count all six test assemblies before reading the total. Baseline: main's last green run.
5. Run `pwsh -NoProfile -File scripts/package-canary.ps1` with `NuGetPackageSourceCredentials_github` set (the token has `read:packages`; NuGet needs to be handed it). This is the only local proof that package mode still restores and builds with the new floors.
6. Run one `win-x64` Release publish and check for no new IL warnings against `TRIMMING.md`'s baselines. (Avalonia and JsonSchema.Net both sit on the trim path.)
7. Update `.github/dependabot.yml` (decision 6).
8. Update `PROGRESS.md`, and `AGENTS.md` if the Avalonia version is quoted anywhere.
9. Open the PR. Every job green except, possibly, `Published Version`. ⛔ **If `Published Version` is red, read its log.** Merge only if the cause is the stale pin, meaning the package-mode build is consuming `AgentForge.*` `2026.4.1019` against the new dependency versions, and step 5's canary passed. A red with any other cause is a real failure. Merge with `--admin`.
10. Close #125, #127–#131 and #133–#136, each with a comment naming the PR.

## Phase 1b — the model catalog PR

Branch `feat/models-2026-11` from `main` after Phase 1 merges, so both are in the package release.

1. Re-read the models overview and Claude Code's model, effort and auto-mode documentation. Record the source and the date in the PR description.
2. For each of Fable, Sonnet and Haiku, in that order, run the checklist in `docs/MODEL-CATALOG.md` § *New model launch*:
   - add the new row and move the family alias onto it;
   - repoint `aliases`;
   - demote the outgoing row to `legacy: true`, `alias: null`;
   - swap the pinned snapshot id in `claude-code-settings.overlay.json`'s `model.examples` and `e.g.`;
   - mirror it in `claude-code-settings.enumdescriptions.json`, replacing in place;
   - update the alias asserts in `ModelCatalogTests`.
3. Run `pwsh -NoProfile -File scripts/validate-model-catalog.ps1`, then the `ModelCatalog|ModelPropertyPromotion` filter, then the full suite.
   - Three other test files use `claude-haiku-4-5` as their example of a model **without** effort or auto mode: `EssentialsModelEffortConstraintTests`, `PermissionsAutoModeGatingTests` and `ModelCatalogAccessorTests`. Demotion keeps that row's capabilities, so they should pass unchanged.
   - If any of them reaches the model through the `haiku` alias instead of the pinned id, it now gets a model with effort. Fix the test to pin the id. Do not change the catalog to keep the test green.
4. Update the release-facing `CHANGELOG.md` `## [Unreleased]` with the three models. Then merge, under Phase 1 step 9's `Published Version` rule.

## Phase 2 — the schema PR, the package release, and the pin

1. Branch `fix/opencode-schema-redesign` from `main` (decision 12). Run `scripts/refresh-schema.ps1`.
2. Bring over `5134ef47`'s work, which is tagged locally as `backup/opencode-redesign-tests`:
   - **Its two test files** come over whole: `git checkout 5134ef47 -- <path>`, never a merge of that branch.
   - **Its `PROGRESS.md` section** ("When OpenCodeForge rejoins") is re-applied **by hand**. Checking the whole file out would overwrite what Phases 1 and 1b wrote there.
   - **Its `backup.json`** is not taken; step 3 re-mints it. If upstream has moved again since 2026-10-19, re-measure `ModelRefSites`, the 48-property count and the `model` union against the refreshed file. Never adjust a number just to make a test pass.
3. Re-mint the `backup.json` golden with the `WriteGoldens` explicit test, and review its diff: only schema hashes may change. Run the full suite.
4. In the same PR, change `schema-refresh.yml` as decision 13 describes, and add its `AGENTS.md` row.
   - Verify by experiment after merge. Push one commit by hand to `chore/schema-refresh`, run the workflow with `workflow_dispatch`, and confirm the commit survives and the run says why it skipped. Then delete the branch.
   - ⚠ If upstream has not drifted that day, the guard is never reached. Order the check **before** the drift detection so the experiment can exercise it.
5. Open the PR. Close #114 with a comment naming the new PR and explaining the Monday reset. Merge under step 9 of Phase 1's `Published Version` rule.
6. `scripts/Publish-Packages.ps1 -PackageVersion 2026.4.MMDD -FeedUser JanusMael -PreflightOnly`, then push the `packages-v2026.4.MMDD` tag.
7. Verify each of the five ids on the feed through the service index's flat container, never a guessed URL. Then check the downloaded `AgentForge.Core`:
   - it embeds the 48-property `opencode-config.json`;
   - its `model-catalog.json` carries `claude-fable-5-2`, `claude-sonnet-5-5` and `claude-haiku-5-5`;
   - its nuspec names the new JsonC, JsonSchema.Net and AppServices versions.
8. Pin PR: move `SharedPackageVersion` and update `PROGRESS.md`'s CI row. `Published Version` must be **green** on it. Merge.

⚠ **The new models reach users only through an app release.** That release is the existing four-step process: packages, verify, pin (all done above), then the `v*.*.*` tag. The release PR must be the last merge before the tag, because its description becomes the release notes. Signing and the winget submission are the maintainer's. It is tagged straight after this phase (decision 15). Confirming the models in the shipped binary means running it, not byte-scanning it.

## Phase 3 — the Inspections quality packages

### Mapping, to be confirmed by measurement in step 1

| Rule | Reads | In-tree counterpart | Proposed outcome |
|---|---|---|---|
| BNAQ1001 | A public `CancellationToken` with a default value | None | **Add** |
| BNAQ1002 | Leak-prone namespaces in public signatures | `PublicSurfaceBaselineTests`, `PublicSurfaceContractTests` (a baseline of the whole surface, which is broader) | **Add alongside**; the baseline stays |
| BNAQ1003 | Forbidden references by layer | `AssemblyLayeringTests` (reflection **and** csproj XML, over `src/` and `tests/`) | **Supplement only.** BNAQ1003 reads compiled references. The XML half catches a declared-but-unused reference, which the compiler drops, so it stays |
| BNAQ1004 | A namespace segment shadowing a referenced root namespace | None | **Add** |
| BNAQ1005 | `InternalsVisibleTo` grants against an allow-list | `SharedFriendGrantsTests` (also checks that no project declares a grant of its own, in either spelling) | **Replace the allow-list half if it canaries; keep the no-local-grant half** |
| BNAQ1006 | References that no longer resolve, and internals no longer granted | None | **Add** |
| BNCQ1001/1002/1004 | Compile-time counterparts of BNAQ1001/1002/1004 | — | **Enable** (1001 is off by default upstream; decide on the baseline) |
| BNCQ1005–1009 | Rx scheduler, async-into-Rx, `Observable.Create`, gate release, uncaught callbacks | None | **Enable; baseline decides warning vs fix** |
| BNCQ3001 | Avalonia `DispatcherTimer` `Tick` not inside try/catch | None | **Enable** |
| XamlQuality.ThemeAudit / .ThemeRuntime | Theme-token and resource-key audit, CI tool, plus a runtime check | `XamlQualityRuleGuardsTests` and the raw-hex guard in the root targets | **Measure only in this plan**; adopt in a follow-up if the audit finds something the raw-hex guard misses |

### Steps

1. **Measure.** In a throwaway branch, add `Bennewitz.Ninja.AssemblyQuality` to `ClaudeForge.Tests`, and `Bennewitz.Ninja.CodeQuality` to every `src` project (as a `PrivateAssets="all"` development dependency, centrally). For each rule, record findings and the count inspected. Record the result in `PROGRESS.md` and keep it out of this plan.
2. **AssemblyQuality PR.** One test per rule, each asserting zero findings **and** a non-zero inspected count, with the layer and allow-lists taken from what the in-tree guards already encode. For each row marked *replace*, canary: plant the violation and watch both the old guard and the new one go red. Only then remove the old half, and repoint the `AGENTS.md` row at the new test.
3. **CodeQuality PR.** Central `PackageReference` for every project under `src/`, at warning severity (decision 8). Fix or suppress the baseline per site. ⚠ The root `Directory.Build.props` sets `TreatWarningsAsErrors` for every build, local and CI alike, so "warning" severity still fails the build, and the baseline must be zero in the same PR. Decision 8's choice of warning only matters for a site suppressed in place.
4. **EntryPoint.** Measure `Program.cs`'s exception plumbing against `AppServices.EntryPoint`, and record adopt or decline with the reason in `PROGRESS.md` (decision 5).
5. **Neither analyzer package ships, but the fixes they force can.** A finding fixed inside `src/AgentForge.*` changes a published package, and it reaches the app only through the next package release and pin. A BNAQ1001 or BNAQ1002 fix also changes the public API: it moves the `PublicSurface/*.txt` baselines and breaks the parked `feat/agentforge-opencodeforge` branch. Such fixes are listed in the PR and released through the normal process; this plan does not add a second package release for them.

## Done when

- [ ] No Dependabot PR is older than the dependency PR. The `bennewitz` group and the new limit are in `dependabot.yml`.
- [ ] Every id in the outdated table is at the version current on the day of Phase 1. `dotnet list package --outdated` on `main` lists none of them, except anything released after that day.
- [ ] The catalog's `fable`, `sonnet` and `haiku` aliases resolve to the models overview's current models on the day of the edit, the outgoing rows are legacy, and both overlays and the tooltips match.
- [ ] The OpenCode redesign is merged from `fix/opencode-schema-redesign`, with its guards, and #114 is closed. `packages-v2026.4.MMDD` is published and verified id by id. `main` pins it, and `Published Version` is green.
- [ ] `schema-refresh.yml` leaves a branch carrying a human commit untouched, shown by the step 4 experiment.
- [ ] The app release carrying the new models is tagged, and the models are confirmed in the shipped binary by running it.
- [ ] Every BNAQ rule has a test asserting zero findings and a non-zero inspected count. Every replaced guard was canaried first.
- [ ] CodeQuality runs on every `src` project with a zero baseline in CI.
- [ ] The `AppServices.EntryPoint` decision is recorded in `PROGRESS.md`.
