# PROGRESS — work state

> **What this file is.** The resume anchor: where the work stands *right now*, what comes next, and
> which decisions are already settled. It is replaced, not appended — a status table that lags
> reality is worse than none.
>
> **Ground truth beats this file.** Treat every claim here as a hypothesis and reconcile before
> acting: `git log`, `git status`, the test suite. Let reality win on any conflict.
>
> For the *why* behind the architecture see [`CLAUDE.md`](./CLAUDE.md); for enforceable rules see
> [`AGENTS.md`](./AGENTS.md). ⛔ For phase-by-phase detail see `docs/OPENCODEFORGE-PLAN.md`, which
> is **deliberately absent from this branch** — [`plans/00003`](./plans/00003-release-built-from-shared-packages.md)
> step 0e deleted it here because it is entirely about OpenCodeForge. It survives on the parked
> `feat/agentforge-opencodeforge` branch: `git show feat/agentforge-opencodeforge:docs/OPENCODEFORGE-PLAN.md`.

---

## Where things stand — 2026-09-21

| | |
|---|---|
| Branch | ✅ **`main` is the trunk again — PR #68 merged 2026-09-21 (`3de807d`), and `release/claudeforge-on-packages` is DELETED** (`delete_branch_on_merge: true`); its content is entirely in `main`, verified by an empty tree diff before deletion. ⛔ **`feat/agentforge-opencodeforge` is still PARKED: not deleted, not merged.** It is the only copy of OpenCodeForge and is needed again at **Phase C0** to evidence that the neutral layer is neutral by *use* |
| HEAD | ⓘ **`git log -1` is the answer.** A hash cannot be written into the commit that produces it, and every attempt to name one here has needed a follow-up commit to correct it — including the one that added this very warning and then named a hash anyway, which is why the hash is now gone rather than merely deprecated |
| Working tree | ✅ **CLEAN, level with `origin`, and the solution BUILDS — 0 errors, 0 warnings.** ⓘ This cell said the opposite for two sessions; Phase E step 3's test slice is finished. See *RESUME HERE* |
| Pushed | ✅ **Level with `origin/feat/agentforge-opencodeforge`** as of 2026-09-17. ⚠ **The branch was FORCE-PUSHED on 2026-09-16** — every commit after `v2026.3.901` has a new SHA. Recovery ref: `backup/pre-trailer-rewrite-20260916`. ⚠ There is still **no PR**, and that is the open question, not the push. ⓘ This cell twice carried a wrong claim — first *"nothing has been pushed"* while the remote branch had existed for four days, then a commit count that was stale the moment anything followed it. `git status -sb` is the answer; what belongs here is whether a PR exists |
| Merged from `main` | ✅ **Integrated by hand on 2026-09-16, up to `origin/main` `52f604d`** — record the SHA, because neither counting nor patch-ids can tell you again. `main` released **`v2026.3.916`** that day. ⛔⛔ **Both automatic answers are WRONG here, in opposite directions.** `git rev-list --count HEAD..origin/main` over-reports (the 2026-09-16 history rewrite renamed every already-merged commit, so ~27 look unmerged); `git cherry` saw through that and found the 8 genuinely new — but now over-reports too, because a hand-port produces a different patch-id than the commit it ports. **The only reliable record is this cell.** Compare `52f604d..origin/main` to find what is new since. ⓘ The CHANGELOG has now been rebased on `main`'s **twice** in one day — `main` owns the released history and this branch owns only what has not shipped, so keeping a local copy of the released sections just makes the next reconciliation bigger. ⛔ **`git merge origin/main` remains the wrong tool**: the merge base is `3c7aaab` (2026-09-01), `main`'s paths no longer exist here (`ClaudeForge.Sdk`→`AgentForge.Sdk`, the backup VM moved into `AgentForge.Avalonia.Shell`), and this repo has shipped two duplicate-attribute defects from *clean* auto-merges. Ports go through `AGENTS.md` §*Hand-porting a fix*. ⓘ What the 8 were: **4 ported** (YAML front-matter ×2, winget rename, version-probe test), **1 ported as a union** (deep links), **1 already present by a different mechanism** (tab a11y — this branch names containers via `ToString()`, `main` via a style; porting would have added a redundant second mechanism for a defect already fixed and guarded), **1 packaging** (`sign-release.ps1` is now committed rather than ignored), **1 reconciled** (CHANGELOG). ⓘ **`main` is +4 since `52f604d`, reconciled 2026-09-20**: the
`Microsoft.Extensions.TimeProvider.Testing` 9.10.0→10.10.0 bump is **ported** (suite unchanged at
3,552 / 0 / 13); the `Microsoft.Maui.Essentials` bump is **not applicable**, because this branch
has no MAUI dependency at all — sharing was reimplemented as `IShareService` / `DefaultShareService`,
which is what the *Sharing worked on no platform* CHANGELOG entry is; the other two are an
`AGENTS.md` docs refresh and its merge commit. **No user-facing gap against `main`'s
`v2026.3.916`.** |
| Suite | ✅ **3,586 passed · 0 failed · 14 skipped — TOTAL 3,600**, Debug, re-measured 2026-09-21 at `6a5c3c5` across all **nine** test projects. ⓘ +34 over the 3,552 of 2026-09-20: the relocation, descriptor-equality and home-bypass guards. ⚠ **Compare the TOTAL (3,600), not the passed count** — the skipped figure is machine-dependent, because the package-mode guards return `Assert.Inconclusive` rather than passing vacuously when `artifacts/localfeed` is empty. ⛔ **This row said `13 skipped · total 3,599` and the arithmetic never closed** (3,586 + 13 = 3,599 only if the skip count is the wrong one of the two). It is **14 / 3,600**; the resume row had the skips right and the total wrong. ⛔⛔ **Counting the summary lines is how the re-measure nearly reported a 200-test REGRESSION**: `dotnet test … \| tail -25` keeps only the last **six** of the nine per-assembly lines, so the three that scroll off (`AgentForge.Artifacts` 35, `JsonC` 73, `LayeredEditors.Avalonia.Diagnostics` 92 — **exactly 200**) look absent rather than truncated. **Count the assemblies against `find tests -name '*.csproj'` before believing a total** |
| **CI on `main`** | ✅ **7/7 GREEN** — all three `Build & Test` platforms (windows, ubuntu, macos), the package canary, the feed restore, the trim check **and `Published Version`**, measured on run `35674982685`. ⭐ **That is what makes the local green trustworthy** — a green suite on ONE platform is not the gate. ⛔⛔ CI had once been RED for roughly twenty consecutive commits with no row here to say so, while every local gate stayed green. Check `gh run list` rather than trusting the rows above this one. ⓘ **`Published Version` was red by construction for the whole of Phase E** and is now green: this branch added `ClaudeEnvironment` to the shared libraries, the published packages did not contain it, so package mode could not build `src/` or `tests/` at all (`CS0246`, `CS0115`). ⛔ **This row used to end *"`2026.3.921` cures it; nothing else can"* — that was WRONG, and cost nothing only because it was caught immediately.** Publishing the tag does not cure it on its own: the job builds against `SharedPackageVersion`, which stayed pinned at `2026.3.920` until `5062f5a` moved it. **A publish and the pin that consumes it are one release**; see *RESUME HERE*. |
| Suite (parked branch) | **4,453 passed · 0 failed · 11 skipped**, Debug — verified 2026-09-19 at `0de0f4e`, after the shared libraries were resynced from the release branch (`+19` over 4,434: the `F7`/`F8` and template-part guards that arrived with the sync). ⛔ The 4,434 and 4,429 figures below are **superseded** — both ran against an older `AgentForge.Core` than the release branch's; see the C0 correction under *RESUME HERE*. 2026-09-16 added `+16` (`F3` share outcomes), `+12` (its two sibling surfaces), `+1` (package surface baseline), `+36` (the YAML front-matter union ported from `main`) and `+13` (deep links). ⚠ **The skipped count is machine-dependent, and 11 is the LUCKY reading.** One of the three package-mode guards is inconclusive rather than green when `artifacts/localfeed` holds no packages, so a clone that has never run the canary reports **12**. That is the guard refusing to claim a measurement it did not take |
| Trim check | ✅ **6/6 six-RID ONE-app matrix, zero IL diagnostics — RE-RUN 2026-09-20 in PACKAGE MODE at `2026.3.918`** (linux-x64/arm64, win-x64/arm64, osx-x64/arm64). ⭐ **This is the first run that measured the PACKAGES rather than project-built code.** The 2026-09-18 run predates `D1`, so its evidence was project-mode however green it looked — the publish path only began selecting package mode when `Publish-Rid.ps1` gained the flag. ⭐ **The mode is READ, not assumed**: every one of the six logs carries `PACKAGE MODE: … at 2026.3.918` and none carries `ESCAPE HATCH`, and each log restores only the **three** product-specific projects, because the eleven arrived as packages. ⭐ **Zero was proven to mean "looked and found nothing"**: the detector was canaried 2026-09-18 against planted `IL2026` and `NETSDK1144` lines and matched 2 of 2, and each RID really trimmed — ILLink's *"Optimizing assemblies for size"* appears once per log; the six archives are 19.6–22.2 MB compressed. ⛔ **This row once claimed A5 would re-cover the matrix. It does not** — A5 publishes **win-x64 only**. ⚠ **Cross-published from Windows**, whereas `release.yml` builds each RID on its native host so the dylibs match; this gate therefore measures **trim analysis**, not native-payload correctness — `D6` is where a shipped artifact is measured. ⚠ **6/6, not 12/12**, because the release branch ships one app; the twelve-publish figure below is the parked branch's |
| Trim check (parked branch) | ✅ **12/12 six-RID two-app matrix, zero IL diagnostics, 2026-09-14** — and for the first time on a trim mode Avalonia actually supports. Both apps moved `link` → **`partial`**; the move needs `<TrimmableAssembly Include="Avalonia.DesignerSupport"/>` or the publish dies on `NETSDK1144`. ⓘ The old warning on this row — that a green matrix meant nothing because `link` silently removed the accessibility tree — **was based on a measurement that does not reproduce; see `F5`** |
| Accessibility of the shipped app | ✅ **168 UIA descendants on the published, trimmed, single-file build**, under both `link` and `partial`. Measured with `scripts/Audit-Accessibility.ps1`, which now settles before it walks |
| Trim analyser | ⭐ **`EnableTrimAnalyzer` is on for everything under `src/`**, so the Roslyn half runs on **every build, Debug included**. ⚠ ILLink's whole-program pass, which is what the matrix above measures, still runs only on a publish |
| Packaging | ⭐ `dotnet pack ClaudeForge.slnx -c Release` produces **exactly eleven** `.nupkg`, zero warnings, ids prefixed `Bennewitz.Ninja.`, one version. ✅ **Latest published: `2026.3.922`**, all eleven on the feed, and `SharedPackageVersion` consumes it. ⓘ This row named `2026.3.914` for three releases — the figure is not worth restating here, because `git ls-remote --tags origin 'packages-v*'` and `user/packages/nuget/<id>/versions` both answer it and neither goes stale |
| Package canary | ✅ **PASSED** end to end on 2026-09-13. Run it with `pwsh -NoProfile -File scripts/package-canary.ps1` |
| Package surface | ⭐ **Baselined 2026-09-16, and it was UNGUARDED until then.** `PublicSurfaceBaselineTests` pins all eleven packable assemblies' exported API against checked-in files under `tests/ClaudeForge.Tests/Architecture/PublicSurface/`. ⛔ The gap was measured, not supposed: `F3`'s breaking change to `IShareService` passed a 4,367-test green suite unnoticed, because `PublicSurfaceContractTests` covers `AgentForge.Sdk` only and checks house style, not API shape. ⚠ Established now because **nothing is on the feed yet** — after the first publish a baseline would have to be reconciled against immutable released versions |
| ⚠ Awaiting | **Nothing from the maintainer — the remaining work is the agent's.** ⓘ This cell twice carried a stale blocker. It said the maintainer was the only thing left while CI was red; then it said `packages-v2026.3.918` was local-only at `b373226` with red CI and had to be deleted and recreated. **Both are now false**: the tag is on `origin` at `19f3885`, `release-packages.yml` succeeded, and all eleven are on the feed at `2026.3.918`. ⛔ **The lesson is that a blocker cell outlives its blocker** — reconcile it against `git ls-remote --tags origin` and `gh run list`, never read it forward. ⛔ **Superseded 2026-09-20: D4 and D5 are DONE, and the only thing left in Phase D is `D6`, which IS the maintainer's** — cutting a `v*.*.*` tag is irreversible and outside the agent's standing permission. So this cell now reads the other way round: **the remaining Phase D work is the maintainer's, and Phase E is the agent's once the tag exists.** Scope for that decision is [`docs/RELEASE-SCOPE.md`](docs/RELEASE-SCOPE.md). ⓘ `F12` FIXED on both branches; `F11` stays open by an earlier locked decision |

---

## ⛔⛔ Two findings from 2026-09-17, both silent, both blocking the package release

Neither was reported by anything. Both are written up in
[`00002`](plans/00002-claude-code-real-config-locations.md) and
[`00003`](plans/00003-release-built-from-shared-packages.md), **drafts awaiting approval**.

### 1 · The release has NEVER been built from the shared packages

Two places in the repository say it is — `.github/workflows/ci.yml:132` (*"it is the mode the
release publishes from"*) and `scripts/package-canary.ps1:10` (*"this canary, and the release"*).
Neither is true. `src/publish/Publish-Rid.ps1:160-167` is the **only** `dotnet publish` in the
release chain and its complete flag list is `IncrementalBuild`, `BuildInParallel` and
`RunResxKeyGuard`. The switch at `Directory.Build.targets:67` fires on `UseSharedPackages == 'true'`,
which nothing sets, so **every RID of every release ever cut used `ProjectReference`**.

⛔ Even asking would fail: `release.yml:21-23` grants `contents: write` and `pull-requests: read`,
**not `packages: read`**, so the private feed would 401. Two independent halves, both missing.

⚠ **The canary proves package mode WORKS; nothing makes the release USE it.** The canary,
`PublicSurfaceBaselineTests` and `PackageVersionLockstepTests` all stay green forever while the
shipped artifact is built from project references. The claim lived in a comment, and a comment is
not a guard — the same shape as the schema registry that `ProductionSchemaRegistryTests` now
source-scans for.

### 2 · Managed settings are read from the wrong directory

Claude Code reads enterprise policy from a **system** directory —
`/Library/Application Support/ClaudeCode/` (macOS), `/etc/claude-code/` (Linux and WSL),
`C:\Program Files\ClaudeCode\` (Windows) — holding `managed-settings.json`, `managed-settings.d/`
and `managed-mcp.json`. ClaudeForge reads `~/.claude/managed-settings.json`
(`PlatformPaths.cs:60-65`); the real system directory appears **nowhere in the product**, and
`managed-mcp.json` is unhandled.

It fails **both** ways, silently: a machine with real policy shows **no managed layer**, so the
effective view tells the user their own value wins where policy overrides it; and a file the user
places at `~/.claude/managed-settings.json` displays as enforced while doing nothing.

⭐ **The discovery code was written for the right thing and pointed at the wrong place.**
`ConfigFileDiscoverer.cs:32-56` already marks the entries `readOnly: true`, already reads a drop-in
directory, and already catches `UnauthorizedAccessException` — *"skip gracefully if unreadable
(e.g. enterprise policy dir)"*. A privileged directory was anticipated; `~/.claude/` is never one.

ⓘ The same file also documents `CLAUDE_CONFIG_DIR` to the user
(`EnvVarTooltipConverter.cs:39`, live on two surfaces) and ignores it. Independent of the above,
and fixed by the same plan.

---

## ✅✅ PHASE C IS COMPLETE — the eleven packages are PUBLISHED

**`packages-v2026.3.918` was pushed on 2026-09-19 and `release-packages.yml` succeeded**: the Test
job green, then Publish with gates 1–3 passed and `Published 11`.

⭐ **Verified against the feed, not against the workflow's own report.** All eleven appear under
`user/packages/nuget/` at exactly one version, `2026.3.918`; the flat-container index returns
`{"versions":["2026.3.918"]}`; and a package was downloaded and opened — 37,323 bytes, a valid
NuGet archive containing `lib/net10.0/JsonC.dll` and a nuspec reading `<version>2026.3.918</version>`.

⛔ **The packages are PUBLIC, and every document here said "private" until this publish measured
it.** GitHub Packages inherit the linked repository's visibility and this repository is public, so
all eleven report `visibility: public`. **Decision taken 2026-09-19: accept public and correct the
wording**, because the source is public already — private packages would buy a restriction without
buying secrecy, while wrong documentation costs the next reader real time. Corrected in `CLAUDE.md`,
`AGENTS.md`, `nuget.config` and `ci.yml`.

⚠ **"Private feed" survives in several places and is still TRUE in the sense that matters there** —
GitHub's NuGet registry demands a token for every read, even of a public package. Requiring auth and
being private are independent properties; conflating them is what produced the wrong sentence.

⛔ **`plans/00001` is titled *"Shared libraries as private NuGet packages"* and is FROZEN.** The
title is now inaccurate and stays that way; this is the drift record, per the never-edit-an-approved-plan
rule.

---

## ⛔ CI IS RED ON ONE JOB, BY CONSTRUCTION — read this before "fixing" it

**`Published Version` fails, and every other job is green (6/7).** It builds the suite against the
**published** packages at `2026.3.920`, and this branch has since changed packable libraries:
`ClaudeEnvironment`, `PlatformPaths.ManagedSettingsRoot` / `ManagedMcpPath`, and the `DialogMessage`
family moving from `AgentForge.Abstractions` to `LayeredEditors.Abstractions`. The feed cannot know
about any of it.

⛔ **There is no source fix.** The job goes green when a **new package version is published**, and
not before. ⚠ Day-resolution CalVer means `2026.3.920` is spent, so the earliest is **`2026.3.921`**
— a second tag on the same date collides with an immutable version.

⭐ **This is the pipeline working, not failing.** The whole point of Phase D is that the release
consumes published bytes; a job that stayed green while shared source drifted from the feed would be
the defect.

---

## ▶ RESUME HERE — `plans/00005` is MERGED (#77, 2026-09-24); next: the AgentForge package release, then `plans/00006`

| | |
|---|---|
| `main` | `e719f3e` — #77 (stage two) and #78 (leaked-timer test fix) both merged 2026-09-24, admin, on the maintainer's instruction. ⓘ `git log -1` is the answer for HEAD |
| Suite | **3,287 passed · 0 failed · 11 skipped — TOTAL 3,298**, across **seven** test assemblies (= seven test csproj), Debug. ⛔ A `Passed!` line with a SHORT total is a crashed test host (drift 15) — compare the total |
| ⛔ CI on `main` | `Feed Restore` and `Published Version` are **RED by construction** and stay red until step 1 below: they restore the published AgentForge `2026.3.922`, whose `LayeredEditors.*` dependencies no longer exist here (NU1101 only). Every other job is green |
| Packages consumed | ScopedEditors / AppServices **`2026.3.924`** (nuget.org), Avalonia **12.1.3**, DataGrid **12.1.2**, XamlQuality **`2026.3.924`** |

**Next, in order:**

1. ⛔ **The maintainer cuts a `packages-v*` release of the six AgentForge/JsonC packages — BREAKING**
   (their public surface now speaks ScopedEditors/AppServices types; see the `AgentForge.Avalonia.Shell`
   and `AgentForge.Sdk` baselines). Releasing is the maintainer's, always. Then the `SharedPackageVersion`
   bump to that version, in its own change — publish and pin are ONE release, and only both clear the
   two red jobs.
2. ▶ **`plans/00006` — MSTest → xUnit v3**, IN PROGRESS on `feat/tests-xunit-v3` (see *Where `00006`
   stands* below). Converter: `Bennewitz.Ninja.Templates` `scripts/mstest-to-xunit.cs` at `17e6bd8`.
   ⚠ Its step 5 carries the headless bootstrap as proof of set-up ordering — that premise is FALSE (see
   the headless section), so the bootstrap is not ported; record that as 00006 drift, never in the plan.
3. ⏳ **Headless flakes still open:** the cross-thread `VerifyAccess` failure (cause unknown; `PerAssembly`
   tried and parked on local-only branch `fix/headless-perassembly` `d5c660a` — it breaks
   `MainWindowViewModel`'s `Application.Current is null` seam); the 2026-09-19 `IOException` on a temp
   `settings.json`; and one `GuiSave_WritesEveryProductsChanges…` failure seen once locally, message not
   captured. #78 removed the leaked-timer test-host crash (13 post-test timers → 0, measured).
4. ⓘ Coverage that left with the library and is not yet restored in the ScopedEditors repo: drift 12.

### ▶ Where [`plans/00006`](plans/00006-tests-move-to-xunit-v3.md) stands — branch `feat/tests-xunit-v3`

| Step | State |
|---|---|
| 0 · baseline | ✅ `artifacts/xunit-move/baseline/` (gitignored): **7 assemblies, 3,068 methods, 3,298 results, 11 skipped**. Compared by `scripts/Compare-TestNames.ps1` — identity is assembly + class + method from each TRX's definitions, data rows by COUNT. Canaried: one test and one `[DataRow]` removed from a copy → it named exactly those two (`REMOVED …`, `ROWS … 21 -> 20`) |
| 1 · MSTest onto MTP | ✅ `global.json` runner, `EnableMSTestRunner`, test projects `Exe`; every `dotnet test` in workflows and the canary uses `--solution`, CI uses `--report-trx`. Name set vs step 0: **0 differences**. Package canary green in package mode. ✅ CI green on three OSes (`46c73f8`). ✅ **Premise for decision 7 holds**: a throwaway xUnit v3 project ran beside the seven MSTest ones under one `dotnet test --solution` (3,301 = 3,298 + 3), wrote its own TRX, then was deleted. `tests/Directory.Build.props` now picks the framework per project — `<UseXunitV3>true</UseXunitV3>` in a converted csproj |
| 2 · rewriter + helpers | ✅ Templates `17e6bd8`, unchanged. Helpers emitted ONCE to `tests/Shared/MessageAssert.cs`, linked into each converted project by `tests/Directory.Build.props` with a global `using Bennewitz.Ninja.Testing` |
| 3 · pilot `JsonC.Tests` | ✅ 73 of 73, same identities, build 0 warnings; the only unmapped site was the assembly `[Parallelize]`, converted by hand to `CollectionPerClass`. Canaried: a message-bearing `MessageAssert.Equal` and ONE `[MemberData]` row (`name: "with space"`, 1 of 21) each fail with the original message above xUnit's diff. ⚠ `JsonC.Tests` has no `CollectionAssert` — that canary moves to `AgentForge.Artifacts.Tests` |
| 4a · `AgentForge.Artifacts.Tests` | ✅ 35 of 35, 0 differences, build 0 warnings. Its build found **xUnit2012** (`IsFalse(xs.Any(p))`) → a converter rule, Templates **`b4d3c4a`** on `feat/mstest-to-xunit-rules`, ⏳ **PR JanusMael/Bennewitz.Ninja.Templates#1**, not merged. ⚠ `5d06d84`'s message cites `95aaed2`, the same commit before a rebase: the converter file is blob `54189b6` in both, so the conversion reproduces from either. And the project was re-converted from its MSTest state. Canaried: `CollectionAssert.AreEqual` → `Assert.Equal` still fails on ORDER; `AreEquivalent` → `SameElements` fails on multiplicity |
| 4b · `ClaudeForge.Avalonia.Tests` | ✅ 42 of 42, nothing unmapped, 0 differences, build 0 warnings. Its `[assembly: DoNotParallelize]` became `CollectionBehavior(DisableTestParallelization = true)` — still serial. Converter from a PRIVATE worktree of the Templates branch: ⛔ never switch branches in the shared `Bennewitz.Ninja.Templates` checkout — a peer session works there, and its commit landed on this branch once (cleaned up with it; its patch is on Templates `main` as `159425b`) |
| 4c · `AgentForge.Core.Tests` | ✅ 810, 8 skipped, 0 differences, build 0 warnings. Converter at Templates **`27c0ec6`** (PR #1, pushed): its first build found 401 errors, and every one except xUnit1051 became a rule there — `[Description]` → `[Trait("Description", …)]` (maintainer's choice, 2026-09-24), boolean `Contains`/`StartsWith`/`EndsWith` checks (xUnit2009/2017), `TestContext.WriteLine`, `AssertFailedException`, `nameof()` messages, named `delta:`/`ignoreCase:`/`message:`, and the 4-argument/message-bearing collection forms for the projects still to come. Re-converted from the MSTest state. By hand: `[Parallelize]` → `CollectionPerClass`; the one synchronous `[Timeout]` test made async (`await Task.Run(…)`) — canaried: a 60 s hang under a 2 s budget is cut off at **2.009 s**. `Inconclusive` → `Skip`: 22 sites, the same 8 skip here by name as under MSTest. ⚠ **xUnit1051 is suppressed** in `tests/Directory.Build.props` (383 sites here): passing `TestContext.Current.CancellationToken` everywhere would rewrite tests mid-move — decision 5. Its own change afterwards |
| 4d · ordinal fix | ✅ JsonC, Artifacts, Avalonia and Core re-converted from `46c73f8` with Templates **`d12e28a`**, which keeps MSTest's ORDINAL string comparison (drift 8); hand edits reapplied (`GitignoreReaderTests` came out byte-identical to its committed form). 0 differences, build 0 warnings |
| 5a · `AgentForge.Sdk.Tests` | ✅ 370, 0 differences, build 0 warnings, Templates `d12e28a`. It has NO headless session any more (the plan's "1 file" predates `00005`). By hand: `[Parallelize]` → `CollectionPerClass`; **xUnit1031** suppressed by `#pragma` in the two files whose tests block ON PURPOSE (bounded `Wait()`s proving the reentrant lock and `ConfigureAwait(false)` hold) — awaiting would delete what they test |
| 5b · `ClaudeForge.Sdk.Claude.Tests` | ✅ 208, 0 differences, build 0 warnings, Templates `d12e28a`; no headless session either. By hand: `[Parallelize]` → `CollectionPerClass` |
| 5c · `ClaudeForge.Tests` | ✅ 1,757 (was 1,760), build 0 warnings, Templates `d12e28a`, nothing unmapped. **The headless bootstrap is NOT ported** (the recorded decision: under `PerTest` isolation every `Dispatch` rebuilds the app, so the warm-up changed nothing and its guard was true by construction): `HeadlessSessionBootstrap.cs` and `HeadlessSessionBootstrapTests.cs` deleted with their links — the **3 intended `REMOVED`** in the name set. By hand, each with its reason in the file: `#pragma` for **xUnit1030** (`ConfigureAwait(false)` in `ClaudeEditorDangerWiringTests` — removing it would move continuations onto xUnit's context) and **xUnit1031** (`LiveLogWindowTests`, `McpServersEditorViewModelTests` — deliberate blocking). ⛔ **One real ORDER bug, exposed and fixed** — drift 10. Three whole-suite runs: 3,295 / 0 failed, differences = exactly the 3 removals |
| 6 · drop MSTest, correct the prose | ✅ `tests/Directory.Build.props` references `xunit.v3` + TrxReport unconditionally; `MSTest`, `EnableMSTestRunner`, the per-project `UseXunitV3` switch and the dead `Microsoft.NET.Test.Sdk` / `coverlet.collector` entries are gone. Live docs rewritten to xUnit: `CLAUDE.md` (now also says order is RANDOMISED and how to reproduce with `--seed`), `PLATFORM.md`, root `AGENTS.md`, and the area `AGENTS.md` files under `Settings/`, `AgentForge.Sdk/`, `ViewModels/` and `ViewModels/Editors/`, plus the template in `SampleHeadlessTests`. No MSTest code remains anywhere; what still says "MSTest" is history in comments, `CHANGELOG.md`, `PROGRESS.md` history and approved plans. All seven TRX report executor `xunit 3.2.2`; build 0 warnings; the only differences are the 3 removed bootstrap tests |
| ▶ RESUME | **Step 7 — the gate**: the reconciliation list (the 3 removed bootstrap tests, the `Inconclusive` → `Skip` accounting of drift 8's 22 sites, theory-row display names), CI green on three OSes, the package canary's own output, and a trimmed Release publish. Drift 8's audit is measured; its one open item (collection-typed `AreEqual`) is a follow-up needing a compilation-backed scan, not a blocker. Templates PR #1 is not merged. Converter: Templates `d12e28a` from a PRIVATE worktree. ⚠ Pushing Templates from here needs `-c credential.helper= -c "credential.helper=!gh auth git-credential"` |
| 4 – 7 (rest) | ⏳ Rewriter dry-run over all seven: `ClaudeForge.Avalonia.Tests` maps cleanly; the other six list **40 UNMAPPED** sites — 5× assembly `[Parallelize]`, 1 sync `[Timeout]`, 5× `[Description]`, 16× 3-argument `AreEqual` in `ClaudeArtifactPathsTests`, 2× `AllItemsAreUnique(msg)`, 2× `CollectionAssert.AreNotEqual(msg)`, 2× named-argument `AreEqual`, 7× 4-argument `StartsWith`/`Contains`/`AreNotEqual`. Each is a rule for the TOOL first, never a hand patch |

⚠ **Drift from the frozen plan** — recorded here, because `00006` is never edited:

1. **The baseline is 3,298, not 3,297** — one test landed in `ClaudeForge.Tests` after the plan measured at `3447783`.
2. ⛔ **`--nologo` on `dotnet test` breaks every test app under MTP**: it is forwarded to each executable,
   which rejects it with exit 5, "Zero tests ran", and **nothing names the option**. The package canary
   passed it; removed there.
3. ⛔ **An assembly whose `--filter` selects nothing exits 8 under MTP** — VSTest never cared. The one
   filtered workflow (`model-catalog-refresh.yml`) now runs per project: 22 + 4 + 4 = the 30 the old
   solution-wide filter selected, measured against the baseline. ⏳ Re-expressed again when those three
   projects move to xUnit, whose filter options differ.
4. ⛔ **NETSDK1151 in Release only**: `ClaudeForge.Tests` references the app, which is `SelfContained`
   in Release, and an `Exe` may not reference a self-contained `Exe`. Debug never sees it, so only the
   package canary caught it. `ValidateExecutableReferencesMatchSelfContained=false` on that one project.
5. **The converter's commit is now on Templates `main`** (the plan says `feat/mstest-to-xunit`); the file
   is unchanged from `17e6bd8` through `6d83523`.
6. ⚠ **xUnit's TRX spells a method `Namespace.Class.Method(arg: value)`**, MSTest's the bare name — so
   `Compare-TestNames.ps1` normalises both, or every converted test would read as removed-and-added.
   Proven on the probe: its theory's two rows grouped as one method, count 2.
7. ⚠ **Compare in the baseline's ENVIRONMENT.** `PackageVersionLockstepTests` is Inconclusive unless
   `artifacts/localfeed` exists, and the package canary creates it — so a comparison after a canary run
   shows one `NotExecuted -> Passed`. Delete `artifacts/localfeed` before comparing.
8. ⛔⛔ **The first four conversions WEAKENED every string assertion, and every gate was green.**
   MSTest's `StringAssert.*` and string `Assert.Contains/StartsWith/EndsWith` are ORDINAL; xUnit's
   default to the CURRENT CULTURE, which ignores e.g. a soft hyphen — measured on both frameworks:
   `Contains("coop", "co­op")` fails in MSTest (all five forms) and passes in xUnit. A weaker
   assertion that still passes is invisible to a name-set comparison, which is exactly why the
   gate cannot catch this class. Fixed in the converter (Templates `d12e28a`: an emitted
   `OrdinalAssert`, ordinal `MessageAssert` string helpers), and all four projects re-converted
   from `46c73f8`. Proven both ways: a search for bare xUnit string assertions finds **12** in the
   committed JsonC tests and **0** after. ⭐ The lesson generalises: a conversion can keep every
   name and every outcome and still change what a test PROVES — check the semantics of each
   mapping on both frameworks, not only that the suite stays green.
   ✅ **The other two suspects, measured on both frameworks (2026-09-24):**
   `AreEqual(string, string, ignoreCase: true)` — MSTest compares under the invariant CULTURE, so it
   passes on the soft-hyphen case where xUnit's `Equal(…, ignoreCase: true)` fails: xUnit is
   STRICTER there, which can only surface as a visible failure (none — the suite is green); `ß`/`SS`
   and `i`/`I` behave the same on both. ⏳ **`AreEqual(a, b)` on a COLLECTION-typed value is LOOSER**:
   MSTest's `Equals` is reference equality for an array or `List<T>` (an equal copy FAILS), xUnit's
   `Equal` compares structurally (an equal copy passes). A converted site therefore still passes, but
   would no longer catch code that starts returning a copy where it returned the same instance.
   Unlike the ordinal case this cannot be settled syntactically — it needs each argument's TYPE —
   so it is a follow-up for a semantic (compilation-backed) scan, not a converter rule. Nested
   `CollectionAssert.AreEqual` agrees on both sides.
9. ⛔ **xUnit v3 RANDOMISES test order per run** (`--seed`); MSTest always ran one fixed order. A suite
   built around process-wide static seams can therefore meet orders it never met before. Measured on
   `ClaudeForge.Tests` after the fix below: **20 orders** (seeds 1–8, 101–110, and seed 3 twice),
   **no order-dependent failure**. Randomisation is KEPT — it is what found drift 10 — and a failure
   is reproduced with the seed: `tests/ClaudeForge.Tests/bin/Debug/net10.0/ClaudeForge.Tests.exe --seed N`.
   ⏳ The one failure in those 20 runs was `ReloadHardeningTests.LoadAllWorkspacesAsync_ConcurrentCalls_ConvergeWithoutDeadlock`
   under seed 3, which then passed twice under the same seed — TIMING, not order: the open
   `VerifyAccess` flake, and its message was lost again (the runner script kept names only).
10. ⛔ **`ConfigScopeAdapterTests.ToConfigScope_ResolvesRealWrappersAndForeignScopesAlike` only ever
    passed because of MSTest's declaration order.** `ConfigScopeAdapter`'s cache is process-wide, and
    `ToConfigScope`'s id fallback searches every wrapped scope first; classmates wrap an
    `other-product` ladder that also has a `Project` rung, so once one of them ran first a foreign
    `"project"` resolved to THAT ladder (`Expected: Project / Actual: Project` — same name, different
    ladder). Fixed, on the maintainer's choice (2026-09-24), by isolating the test:
    `ConfigScopeAdapter.ForgetNonDefaultLaddersForTesting()` (internal) forgets only non-default-ladder
    entries — default-ladder singletons are KEPT, because `For` promises one instance per scope and
    production holds them — called from the class constructor. Canaried under 8 fixed seeds: without
    it the test fails 8/8, with it passes 8/8. ⏳ **The production ambiguity remains**: once two
    ladders share an id, resolving a foreign scope by id alone cannot say which ladder is meant. It
    matters when OpenCodeForge rejoins; a follow-up, not part of the move.

### ✅ DONE — stage two: [`plans/00005`](plans/00005-claudeforge-and-agentforge-consume-scopededitors.md), approved 2026-09-23, merged 2026-09-24 as #77

ClaudeForge **and** AgentForge move from the local `LayeredEditors.*` projects to the seven ids
published on nuget.org at `2026.3.923` — `Bennewitz.Ninja.ScopedEditors.{Abstractions,ViewModels,Avalonia}`
and `Bennewitz.Ninja.AppServices{,.Abstractions,.Logging,.Avalonia}` — and the `LayeredEditors` family
is deleted. 134 files across seven consumers. Work happens on a feature branch and lands by PR.

⛔ **[`plans/00004`](plans/00004-claudeforge-consumes-scopededitors-and-appservices.md) is SUPERSEDED,
and it was approved on a false premise.** It scoped this to ClaudeForge alone. That cannot compile:
`AgentForge.Avalonia.Shell` is the adapter layer between AgentForge's settings model and the editor
abstractions, so its public API is built from `LayeredEditors` types, and ClaudeForge hands those
adapters straight to the editor view-models
(`src/ClaudeForge/ViewModels/Editors/DefaultEditorFactory.cs:133`). Repoint one side and the two ends
are the same names over **different types**. ⭐ **`00004` measured namespace IMPORTS; the question was
COUPLING** — whether renamed types cross a boundary to something that stays behind. The public-surface
baseline answers that in one grep, and it was not consulted until the first implementation step.
Caught before any source changed; the cost was one plan number. `00004` stays frozen as the record.

⚠ **Things `00005` knows that are easy to lose:**

- **The trim gate is vacuous on `2026.3.923`.** All seven assemblies shipped without
  `[AssemblyMetadata("IsTrimmable","True")]` — read off the DLLs, against a control that has it — and
  ClaudeForge publishes `TrimMode=partial`, which analyses only marked assemblies. Build on `.923`;
  claim the gate only on **`2026.3.924`**, due 2026-09-24, proven by per-assembly size in
  `obj/…/linked/` (the publish output is single-file, so it holds no DLLs to compare).
- **The F12 and Shift+F12 windows would ship EMPTY.** They are held back from the packages, and the
  package removed the wiring that fed them. `00005` rewires them locally.
- **`Published Version` is red by construction** on the PR: the published `AgentForge.*` still
  depend on the old types. It clears only after a post-merge `packages-v*` release — which is
  **breaking** — and the pin bump. Publish and pin are one release.

✅ **`v2026.3.922` is published with six assets, built from the shared packages at `2026.3.922`.**
It carries **Claude Opus 5.5** plus the two fixes that had been sitting unreleased (managed
settings read from the real per-OS system directory, and `CLAUDE_CONFIG_DIR` honoured).

⭐ **Opus 5.5 was confirmed IN THE SHIPPED BINARY, not inferred.** The single-file byte scan was
**inconclusive** — the bundle compresses its payload, so a missing substring proves nothing — so
the published `win-x64` asset was downloaded, run against a scratch `CLAUDE_CONFIG_DIR`, and driven
to *Model & Effort*, which rendered `claude-opus-5-5` from the overlay resource inside the package.
The UI also reported `v2026.3.922.0`.

| | |
|---|---|
| Working tree | ✅ **CLEAN on `main`, level with `origin/main`.** ⓘ `git log -1` is the answer for HEAD |
| Packages | ✅ **All ELEVEN on the feed at `2026.3.922`**, verified id by id against the flat container — **not** by reading a green workflow. ⛔ **Discover the flat-container URL from the service index** (`PackageBaseAddress/3.0.0` → `.../download`); `<base>/<id>/index.json` 404s, and that 404 is indistinguishable from *"never published"*. A verification script guessing the path reported **0 of 11** against a feed that held all eleven |
| Pin | ✅ **`SharedPackageVersion` = `2026.3.922`** in the root `Directory.Build.props` (PR #70) |
| CI | ✅ green on `main`, **including `Published Version`** |
| Suite | ✅ **3,595 passed · 0 failed · 14 skipped — TOTAL 3,609**, Debug, across all **nine** test projects, measured 2026-09-23. ⓘ It read 3,600 until PR #74 linked `HeadlessSessionBootstrapTests.cs` (3 tests) into three projects |
| Release | ✅ **`v2026.3.922`, six assets**, run `35770630333`. ⭐ **The mode was READ, not assumed**: all six provenance logs carry `PACKAGE MODE … at 2026.3.922`, **zero** `ESCAPE HATCH` lines and **zero** IL warnings. Notes came from PR #72's body — `release.yml` resolves them from the PR whose merge commit is the tagged SHA, so the release PR must be the LAST thing merged before the tag |
| ⚠ Remaining | **Signing, then the winget submission — the maintainer's, always.** `packaging/sign-release.ps1` needs a PIN; it signs, re-uploads **and submits**. ⓘ winget users are on **`2026.3.916`** because `.920` was deliberately skipped, so this submission jumps them across both. Fallback is `Resubmit-Winget.ps1`, which needs `-Force` |


**Where `00005` stands:**

| Step | State |
|---|---|
| 1 · package references + `nuget.config` | ✅ Done — proven from an EMPTY cache with no credential |
| 2 · namespace map | ✅ Done — 134 files, derived map, 0 unresolved, plus 1 base class and 15 `cref`s in the partially-qualified form |
| 3 · service APIs | ✅ Done in code — every ignored `void`/`bool` now takes the command's token and a result; `Cancelled` answered explicitly |
| 4 · `avares://` | ✅ Done — the runtime font check is a test now, with a control (drift 13) |
| 5 · F12 windows | ✅ Done in code, on the `.924` hook — ⏳ "windows that FILL" in a real run is still owed |
| 6 · delete the family | ✅ Done — `75d8961`: 5 `src` + 2 test projects, their baselines, slnx entries, friend grants and the selector line |
| 7 · guards that named the family | ✅ Done — every narrowed guard canaried BOTH ways (drift 12); prose swept eleven → six |
| 8 · `PublicSurface` baselines | ✅ Done — remapped through the derived 81-type map, the diff leaves exactly three lines, all step 3's: `OpenFileLocationCommand` becomes `IAsyncRelayCommand`, `IsRevealInFileManagerSupported` is new. `AgentForge.Sdk` is the namespace move and nothing else |
| 9 · real `.924` | ✅ Done 2026-09-24 — all seven ids verified on nuget.org's flat container **here**, not taken from the relay; tags `v2026.3.924` = AppServices `d1c5c9c`, ScopedEditors `4e44d8b`, the commits the local packs came from. Pins → `2026.3.924`; the `prerelease924` source, its mapping and `artifacts/prerelease-924` removed; every `-local.*` and both never-published `.avaloniaui` ids purged from the cache. Each restored package's `.nupkg.metadata` names `api.nuget.org` as its source |
| 10 · full gate | ⏳ Local half done — 7 of 7 test assemblies (= 7 test csproj), 1,760 / 1,757 / 3 in `ClaudeForge.Tests`, build 0/0. ✅ **The trim gate is real now:** all seven `.924` DLLs carry `IsTrimmable` (all seven `.923` controls do not), a trimmed win-x64 Release publish reports 0 IL warnings, and every one of the seven is SMALLER in `obj/…/linked/` than in its package (e.g. `ScopedEditors.ViewModels` 66,048 → 54,784) — trimmed, not kept whole. ✅ **CI, `7ba127c`:** Build & Test green on Windows, Ubuntu and macOS; Trim Check and Package Canary green. `Feed Restore` and `Published Version` red **by construction only** — every error in both is NU1101 for the four `LayeredEditors.*` ids the published `.922` AgentForge depends on (drift 16) |

⚠ **Drift from the frozen plan** — recorded here, because `00005` is never edited:

1. ⛔ **AQ1004 renames two package IDS in `.924`, not just namespaces.** `ScopedEditors.Avalonia` →
   `ScopedEditors.AvaloniaUI` and `AppServices.Avalonia` → `AppServices.AvaloniaUI` — id, assembly,
   namespace **and** `avares://` path all change. So the plan's namespace map and `avares://` targets
   name the `.923` spellings; **step 9 is a rename, not a one-line pin bump**; and decision 9's "build
   on `.923`" is moot, because steps 2 onward target the `.924` names directly — which also means the
   trim gate is claimed on the marked version from the start. Two of step 1's references change when
   `.924` lands; the other five, and both `nuget.config` patterns, already cover the new names.
   ⓘ The two `.Avalonia` ids are deprecated on nuget.org, not removed: installable at `.923`, never
   published to again.
2. **`nuget.config` removals move to step 6.** Step 1 only **adds** the nuget.org patterns. While the
   `LayeredEditors` projects exist, package mode still resolves them from `github` / `localfeed`, and
   dropping that mapping early would route them to nuget.org, which has none.
3. **The namespace map is DERIVED, not relayed.** All 87 old types were matched by name against the
   published packages' own XML documentation: 81 matched, 0 ambiguous, and the 6 unmatched are the F12
   cluster plus two internal JSON tokeniser types. The relayed map **missed** `LayeredEditors.Messages`
   (→ `ScopedEditors.Messages`, shipped in the **ViewModels** package), `…Diagnostics.Binding`, and one
   side each of the `…Diagnostics.Logging` and `…Diagnostics.Dialogs` splits. ⚠ **Re-derive it against
   `.924`** rather than assume the rename is uniform.
4. **Neither package repository carries tests for the moved code** — the move carried source only.
   Maintainer, 2026-09-23: **port first; step 6 waits.** About 33 files, including a fourth category
   the plan's list missed: `ShellLauncherWindowsTerminalTests.cs` (ported to `AppServices.Tests/Shell/`, deleted here),
   which exercises `ShellLauncher` internals whose `InternalsVisibleTo` grant did not travel.
5. **The baseline is 3,609, not 3,600.** PR #74 links `HeadlessSessionBootstrapTests.cs` (3 tests) into
   three test projects. ⚠ Two of those are the `LayeredEditors` test projects step 6 deletes, so the
   total falls by **6 there with no loss of coverage** — the same three tests still run in
   `ClaudeForge.Tests`. Account for them by name when the totals are reconciled.


6. ⏳ **Building against LOCAL builds of `.924` until it is published** (maintainer, 2026-09-23: "work
   around it"). Both families are packed from their committed source — ScopedEditors `56ff954`,
   AppServices `4815c74` — at **prerelease** versions (`2026.3.924-local.1` / `-local.2`), because
   the NuGet cache never re-extracts a version, so a local pack under the real `2026.3.924` would
   silently shadow the published bits on this machine. They live in their **own** gitignored feed,
   `artifacts/prerelease-924`, with its own temporary source — ⛔ NOT `artifacts/localfeed`, which is
   the package canary's and which `EveryPackageInTheLocalFeedNamesOneVersion` holds to one version.
   **The swap, once `.924` is indexed:** both versions → `2026.3.924`; delete the `prerelease924`
   source and mapping; purge `~/.nuget/packages/bennewitz.ninja.{appservices,scopededitors}*`.
7. **Decision 8 is implemented through the package's hook, not the local wrap** (maintainer,
   2026-09-23). The hook shipped in the same `.924` —
   `AvaloniaDiagnosticsOptions.ConfigureLogger` / `.EventListener` (AppServices `4815c74`) — so the
   wait that made the wrap preferable no longer exists. Gone with it: flushing a pipeline Serilog
   did not own, early `Log.Logger` captures bypassing F12, and a "call ours, not
   `AvaloniaDiagnostics.EnqueueEvent`" trap. `ClaudeForgeDiagnostics` now only builds the windows.
8. **Step 2's proof — "the solution compiles" — is only reachable together with step 3**, because
   the service API is breaking. Step 2 landed as a non-compiling checkpoint on the branch.
9. **Step 4: one of the four `avares://` sites fails LOUDLY, not silently.** A `StyleInclude` is
   resolved by the XAML compiler (AVLN2000). The plan's "no build error" is true of the three
   **font** sites only.
10. **A fifth test category the plan's list missed:** `ClaudeForge.Tests` files that test package
    code through its **public** API — `ShareOutcomeTests` / `ShareServiceTests` exercise
    `DefaultShareService`, and two of `AccessibilityCoverageTests`' four tests guard
    `FatalErrorDialog` / `NonFatalNoticeDialog`. They need no internals, so the internals scan could
    not find them. They STAY: neither package repository has equivalents, so they are the only
    coverage that code has.
11. **`NoLiteralMonospaceFontStackTests` was misclassified** in the plan's measured table as "named
    only in comments". It resolves `avares://<assembly>/Assets/Fonts` to `src/<assembly>/`, so it is
    coupled to the family through the URI's **data**, which a grep for the literal cannot see. It is
    step 7's work, and the one guard that validates step 4's font URIs statically.
12. **Step 7 — what each guard became, and the coverage that left with the library.** Rule 1 held:
    nothing was deleted without naming where its subject is now checked.

    | Guard | Now | Canary |
    |---|---|---|
    | `DangerSurfaceMarkupTests` | app wrapper only; file floor 5 | ✅ floor and `IsDangerNow` banner both redden |
    | `SeverityGlyphFontSizeMarkupTests` | 6 sites (was 8), 5 files | ✅ |
    | `AxamlAccessibilityCoverageTests` | library project dropped, 6 directories | ✅ ratchet reddens on ONE removed attribute (a line drop that broke the XML "passed" the first time for the wrong reason) |
    | `ThemeResourceIntegrityTests` | the two `LE.*` tests left; NEW guard reads the package's user-string heap for the `App*` tokens it asks the app for | ✅ both ways; found 2 keys + the `AppSeverity` fragment |
    | `NoDeadBrushTokensTests` | severity family re-earned by calling the package's `KeyFor`; the `LE.Danger*` exemption retired — its named exit (the tokens leaving) was reached | ✅ both tests redden, and the exemption is shown load-bearing |
    | `NoLiteralMonospaceFontStackTests` | a packaged URI is resolved by Avalonia's `AssetLoader` on the headless session | ✅ old assembly name and wrong folder each redden with the URI named |
    | `PackageMetadataTests`, `AssemblyLayeringTests` | `LayeredEditors.` prefix / globs removed — a stale DLL in a test `bin/` would otherwise be scanned as shared code | — |
    | `scripts/verify-feed-restore.ps1` | 11 ids → 6 — it would have failed CI's `feed-restore` | — |

    ⛔ **GAPS — no test anywhere covers these now; restore them IN the ScopedEditors repo:** the
    shared `PropertyEditorWrapper`'s danger banner and glyph sizing, the `LE.*` token
    reference/declaration consistency, and the AXAML accessibility scan of the package's own
    controls. (`TemplatePartAutomationNameTests` and `ThemedBrushTrackingTests` DID move — they are in
    `ScopedEditors.Tests`.) ⓘ The F12 windows kept their English literals when they moved into the
    app, so they sit outside the resx rule; that is inherited, not new.
13. ⛔ **Drift 9's word "silent" is WRONG for the font sites — measured here, 2026-09-23.** A
    single-family `FontFamily` whose name does not resolve THROWS at first layout (*"Could not create
    glyphTypeface"*); only a fallback list degrades silently. Step 4's runtime check is now a test,
    `EveryBundledFontUri_LaysOutText`: it lays out text in every full `avares://…#Family` URI in
    `src/` on the headless session, behind a nonexistent-family control that must fail. All three
    sites resolve; canaried with `#JetBrains Mona`, which reddens with Avalonia's own message. The
    remark that said "does not throw" is corrected. ✅ **Weight is asserted too** (maintainer,
    2026-09-23): a missing weight throws nothing and borrows the nearest face (the peer measured
    600 → 700), so `EveryWeightTheMarkupAsksOfAMonospaceToken_HasItsOwnFace` takes every token/weight
    pair the markup binds (Normal, Bold and SemiBold today) and asserts the shaping face's weight,
    behind a control that must substitute. Canaried: the SemiBold site set to Light reddens with
    *"drawn with a weight-400 face"*.
14. ⛔ **Drift 1 is half-REVERSED: at `.924` the two package IDS are `.Avalonia` again** (developer's
    decision, 2026-09-23, before anything was published — AppServices `a853070`, ScopedEditors
    `6525d87`). AQ1004 governs namespaces and a package id is not one. So
    `Bennewitz.Ninja.{AppServices,ScopedEditors}.Avalonia` ship `…AvaloniaUI.dll`; assemblies,
    namespaces and `avares://ScopedEditors.AvaloniaUI/…` URIs stay `.AvaloniaUI`. ⚠ **Package and
    assembly are now named differently** — an `avares://` URI written from the package name points at
    nothing. The `.AvaloniaUI` ids will never exist on nuget.org, nothing is deprecated, and the real
    `.924` is a new version of the ids pinned at `.923`. Here: three `PackageReference`s and the
    comments naming the package changed; the local prerelease moved to **`-local.3`**, packed from
    those two commits, with both `.nupkg`s checked to carry the `.AvaloniaUI` DLL.
15. ⛔ **Avalonia 12.1.3 is the family floor** (developer's decision, 2026-09-23, relayed by the
    ScopedEditors session and checked here: AppServices `d1c5c9c` and ScopedEditors `64769ae` both
    set it, and every id exists on nuget.org). At `.924` both Avalonia packages depend on
    `Avalonia >= 12.1.3`, so this repo's direct `12.1.0` pins would fail restore with NU1605 (the
    peer measured that; not re-measured here). Moved to 12.1.3: `Avalonia` ×3 projects,
    `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, and `Avalonia.Headless`
    (from 12.1.2, kept at the framework's version). ⛔ **`Avalonia.Controls.DataGrid` is a floor too,
    at 12.1.2** — its newest; it has no 12.1.3. This first read "stays 12.1.0, as ScopedEditors
    keeps it", which was true of `64769ae` and stopped being true the same night: ScopedEditors
    `4e44d8b` (merged, CI green) makes 12.1.2 a floor of `ScopedEditors.Avalonia`. MEASURED here
    against a pack of `4e44d8b`: a direct 12.1.0 reference fails restore with *"Detected package
    downgrade: Avalonia.Controls.DataGrid from 12.1.2 to 12.1.0"*; 12.1.2 restores clean. The local
    prereleases are now AppServices **`-local.4`** (`d1c5c9c`) and ScopedEditors **`-local.5`**
    (`4e44d8b`). Build 0/0; suite green at the usual totals; a trimmed win-x64 Release publish
    reports 0 IL warnings.
    ⚠ **One unexplained run:** the first full-suite run after the bump reported `ClaudeForge.Tests`
    at **1,606 tests, 0 skipped** — 154 short of 1,760, with the 3 skips missing too, and nothing
    failed. Four runs since (one isolated, three full-suite) each report 1,760. It left no TRX, so
    the cause is unknown. If a total ever reads short again, capture the TRX before rerunning.
    ✅ **Explained 2026-09-24: a short `Passed!` total is a CRASHED TEST HOST.** Reproduced by
    accident — a timer callback that threw on a pool thread aborted the run, and the summary still
    printed `Passed!` at **1,587**. CI then caught the real instance (PR #77, Windows push run):
    `StatusController`'s auto-clear timer, left armed by an undisposed `MainWindowViewModel`, fired
    after its test and posted into a dispatcher the `PerTest` teardown had reset — a
    `NullReferenceException` inside `Dispatcher.Post`, unhandled, taking the host down. ⛔ So a
    `Passed!` line proves nothing about completeness; compare the TOTAL against the known count.
    The fix — disposing every `MainWindowViewModel` a test creates — is on `fix/headless-leaked-timers`,
    judged by repeated CI before it becomes a PR.
16. **Step 10's first CI run (`859cf41`) — two jobs red by construction, one real guard defect.**
    - ⛔ **`Feed Restore` is red by construction too**, not only `Published Version` as the plan
      named: both restore the PUBLISHED AgentForge packages at `SharedPackageVersion` `2026.3.922`,
      which depend on `LayeredEditors.*` ids this branch no longer maps (step 6), so NU1101. Re-adding
      the mapping would not help — `.922`'s AgentForge is built against the old types. Both clear only
      with the post-merge, BREAKING `packages-v*` release plus the pin bump.
    - ✅ **`PackageVersionLockstepTests` treated the prefix as the family.** It called every
      `Bennewitz.Ninja.*` dependency a sibling that must share the pack's version, so AgentForge's
      dependencies on ScopedEditors/AppServices `2026.3.924` read as six drifts and failed the package
      canary. A sibling is now a package IN the packed feed; a premise asserts at least one sibling
      dependency was checked; canaried by pointing AgentForge.Sdk's AgentForge.Core dependency at
      `9.9.9` inside the feed. ⓘ Locally it had only ever been **Inconclusive** (no feed) — one of
      the "3 skipped". The full canary now passes locally, with that test measuring.

### ▶ NEXT — [`plans/00006`](plans/00006-tests-move-to-xunit-v3.md), approved 2026-09-23: MSTest → xUnit v3 (unblocked: 00005 merged)

Starts **after `00005` merges**, on its own branch and PR. The converter is
`Bennewitz.Ninja.Templates` `scripts/mstest-to-xunit.cs`, on that repository's `main` at **`17e6bd8`**
— the commit to pin and name in each conversion commit. Already piloted on a scratch clone:
`JsonC.Tests` converted with nothing unmapped, 0 warnings, 73 of 73. ⚠ Step 1 selects the MTP runner
explicitly: an `xunit.v3` executable otherwise runs xUnit's native runner.

⭐ **Decided, 2026-09-23 — `[assembly: Parallelize(Scope = MethodLevel)]` becomes xUnit's DEFAULT.**
Six projects set it and xUnit has no method-level mode; the converter reports it UNMAPPED on purpose.
Each conversion commit deletes the attribute: xUnit then runs classes in parallel and a class's
methods serially — strictly less parallel than today, so no new interleaving can appear. Assemblies
serial today (`DoNotParallelize`) stay serial under decision 6 of the plan.

### ⛔⛔ A DATA EDIT UNDER `src/` DOES NOT REACH A RELEASE ON ITS OWN

`model-catalog.json`, `claude-code-settings.overlay.json` and the enum-descriptions file are
**`EmbeddedResource`s inside `AgentForge.Core`**, which is one of the eleven. The release publishes
with `-p:UseSharedPackages=true`, so the app consumes the **package** at `SharedPackageVersion`,
never the working tree.

⭐ **Caught before the tag, and proven three independent ways.** Byte-scanning the cached packages
showed `2026.3.917`, `.918` and `.921` all carry `claude-opus-5` and **none** carries
`claude-opus-5-5`. CI then reproduced it from the other direction —
`expected: "claude-opus-5-5"` / `actual: "claude-opus-5"` in `published-version`. Finally the
published `2026.3.922` package was **downloaded and opened**, and its embedded catalog maps `opus`
to `claude-opus-5-5`.

⛔ **Tagging on a green local suite would have shipped a release with no Opus 5.5 in it** — six
assets, green workflow, nothing failing. This generalises beyond models: **any** bundled asset under
a packable library ships through the package, so the three-step order applies to all of them.

### ⛔ Correction: the `C1` / "no `read:packages`" claim was FALSE

This document said package-mode restore 401s here for want of a `read:packages` grant. **It does
not.** `gh auth status` shows `GH_TOKEN` carries `read:packages`; what was missing is handing the
credential to NuGet:

```powershell
$env:NuGetPackageSourceCredentials_github = 'Username=<user>;Password=' + $env:GH_TOKEN
```

With that set, `Publish-Rid.ps1 -Rid win-x64` completed **in package mode at `2026.3.921`** on this
machine — exit 0, 0 IL warnings, 0 `ESCAPE HATCH` lines, and only the three product-specific
projects restored. ⭐ Permission and credential are independent halves — the same shape as the D6b
release failure, which had the permission and set no credentials.

### Next, in order

1. ✅ **`v2026.3.922` is cut** — six assets, all six RIDs in package mode at `2026.3.922`.
   ⛔ The release notes do **not** come from `CHANGELOG.md`: `release.yml` reads the body of the PR
   whose **merge commit is the tagged SHA**, else the tagged commit's message body. So the release
   PR has to be the last thing merged before the tag — tagging after a docs refresh publishes that
   refresh as the notes. Scope is [`docs/RELEASE-SCOPE.md`](docs/RELEASE-SCOPE.md).
2. ⚠ **Sign, then submit to winget — the maintainer's, always.** `packaging/sign-release.ps1` needs
   a PIN; it signs, re-uploads **and submits**. ⓘ winget users are on **`2026.3.916`** because
   `.920` was deliberately skipped, so this submission jumps them across both. Fallback is
   `Resubmit-Winget.ps1`, which needs `-Force`.
3. **Re-run `E1` in package mode** — `pwsh -NoProfile -File scripts/retest/Test-ScratchHomeIsolation.ps1`.
   ⓘ No longer blocked: a package-mode published build now exists locally (see the correction
   above), which is what E1 was waiting for. The **mechanism** was already proven 7/7 with 0 touches
   to the real `~/.claude`; what is outstanding is the same measurement against a package-mode build.
   ⚠ **A whole-home diff cannot be used** — a control with no app running showed **8 changes in 25s**.
4. ⓘ **Nothing else is outstanding in this repository.** `AgentForge.*` and `JsonC` keep publishing
   as they do today; the `LayeredEditors` extraction is another repo's work and changes nothing here
   until its packages are live and verified — see the 2026-09-21 decisions.

### ⓘ New model launches are a catalog chore, not a schema refresh

`scripts/refresh-schema.ps1` reported **all three schemas already up to date** for Opus 5.5:
schemastore.org omits model names entirely, so a refresh can never be what adds a model. The work is
`model-catalog.json` plus the two hand-curated overlays, per the checklist in
[`docs/MODEL-CATALOG.md`](docs/MODEL-CATALOG.md). ⛔ **Never fill a model row from recall** — read the
published lineup. Opus 5.5's default effort is **`medium`, not `high`**, the only model in the lineup
whose default differs, and that would have been copied wrongly from Opus 5's row by anyone assuming.

## Done — plans 00003, Phase D: make the release actually consume them

⭐ **Phase D is the point of the entire plan.** The founding defect is that the release has never
once been built from these packages while two comments claimed it was. They now exist, so every
step below is finally verifiable.

| Step | State | What proves it |
|---|---|---|
| **D1** — publish path selects package mode at `2026.3.918` | ✅ **DONE** | `source` in every `.nupkg.metadata` names the feed. Verified in CI: **11/11 from `nuget.pkg.github.com`** |
| **D2** — `packages: read` in `release.yml` | ✅ **DONE** | The `feed-restore` job restores from the feed with exactly that scope, on every push |
| **D3** — build-time guard + recorded escape hatch | ✅ **DONE** | `GuardShippingPublishUsesPackages` fails a bare Release publish (exit 1, observed); the hatch publishes clean and prints `ESCAPE HATCH USED`; package mode prints `PACKAGE MODE: … at 2026.3.918`; a Release **build** stays silent. 4/4 predicted |
| **D4** — full suite and trim gate at the published version | ✅ **DONE** | CI job `published-version`: suite **3,541 · 0 · 23** in package mode plus a trimmed publish, empty local feed, no cache. Locally **6/6 RIDs, zero IL diagnostics**, every log naming `PACKAGE MODE … 2026.3.918` and none naming the hatch |
| **D5** — correct `ci.yml:132` and `package-canary.ps1:10` | ✅ **DONE** | Both now say the clause was false when written, name the guard that defends it, and state that neither touches the feed — the `published-version` job does |
| ⛔ **D6a** — the release publish creates `artifacts/localfeed` **empty** | ✅ **DONE** | **Release attempt 1 died here, on all three publish hosts at once.** A configured local NuGet source that does not exist is a hard `NU1301`, not a skipped one. `verify-feed-restore.ps1` has carried the identical block since D1, and its comment even says *other jobs never see this*: development mode never requests these ids, and the canary creates the folder by packing into it. The release publish did neither — and it is the only path whose **first clean-runner execution IS the release**. Fixed in `Publish-Rid.ps1` rather than `release.yml`, so a local publish on a fresh clone behaves the same |
| ⛔⛔ **D6b** — the publish hands NuGet the feed **credentials** | ✅ **DONE** | **Release attempt 2 died here.** `packages: read` only makes the token *allowed* to read the feed; NuGet still has to be handed it. `ci.yml` sets `NuGetPackageSourceCredentials_github` on every step that touches the feed; `release.yml` set it on none, so the restore reached the feed and **401'd**. ⭐ **The founding defect's own shape, repeating**: plan 00003 opens by naming *"two independent halves, both missing"* — D2 added the permission half, verified it, and left the credential half unwritten |
| ⓘ **winget — DELIBERATELY SKIPPED for `2026.3.920`** | ✅ decided 2026-09-20 | ⛔ **Not an oversight, and not to be "fixed" later by a late submission.** The maintainer skipped it on the judgement that too little is new to be worth a manifest. ⓘ The consequence, stated: winget users stay on **`2026.3.916`** (last submitted 2026-09-16) while the GitHub release is `2026.3.920`, so the next submission jumps them across both. Nothing breaks — a skipped submission just means no new manifest |
| **D6** — cut the app release | ✅ **DONE — `v2026.3.920`, 2026-09-20** | ⭐⭐ **The first ClaudeForge release ever built from the published packages**, which is the whole point of plan 00003. Six assets on the GitHub Release, and the mode was **read from the archived provenance** rather than taken from the workflow: every RID log carries `PACKAGE MODE … at 2026.3.920` and **zero** `ESCAPE HATCH` lines. ⛔ **It took THREE attempts, and the first two failed for two independent halves of the same missing thing** — see the two rows below. Previously: The shipped artifact passes D3. ✅ **A release-path binary has now been BUILT AND RUN — 2026-09-20.** `Publish-Rid.ps1 -Rid win-x64` produced a single-file 26.5 MB self-contained build, exit 0, **0 warnings**, log carrying `PACKAGE MODE … at 2026.3.918` and **zero** `ESCAPE HATCH` lines. ⭐ **Provenance was read, not assumed**: all eleven `.nupkg.metadata` name `nuget.pkg.github.com`, so this consumed the **published** bytes rather than `artifacts/localfeed` or a stale cache. It launches clean — the log's own `Starting ClaudeForge v2026.3.920.1059` matches the binary just built, schemas **fetched** from schemastore at runtime, no `[ERR]` or `[FTL]`, and the single `[WRN]` is the app's designed heads-up that the fetched schema carries settings newer than its structured editors. ⚠ **The earlier hand-driven retest (2026-09-18, `v2026.3.918.839`) was package mode from LOCALLY-PACKED bytes and predates `6910ffc`**, the commit that made the release path itself select package mode — so this is the first time the actual release chain's output has been run. ⭐ **Scope is written down**: [`docs/RELEASE-SCOPE.md`](docs/RELEASE-SCOPE.md) states what the tag produces, what ships, what is deliberately left out, and the three decisions that are the maintainer's. ⛔ **The release notes do NOT come from `CHANGELOG.md`** — `release.yml` reads the merged PR's body, else the tagged commit's message body, else auto-generated notes. This branch has no PR, so the curated `## [Unreleased]` section reaches the release only if it is put into the tagged commit's body or a PR |

ⓘ **The pin has MOVED since: it is `2026.3.920` today, and `2026.3.920` is the newest on the feed** (verified against `user/packages/nuget/` — the eleven hold `2026.3.918` and `2026.3.920`, nothing newer). The row below records what D1/D2 shipped at the time and is left as written.

**What D1/D2 shipped:** `SharedPackageVersion` pinned at `2026.3.918` in the **root**
`Directory.Build.props`; `-p:UseSharedPackages=true` on the only `dotnet publish` in the release
chain (`Publish-Rid.ps1`); `packages: read` in `release.yml`; and a `feed-restore` CI job running
`scripts/verify-feed-restore.ps1`.

⛔⛔ **D1 caught a false success on its first run, which is why the job exists in that shape.** A
restore at the pinned version reported success with **all eleven resolving from
`artifacts/localfeed`** — locally-packed bytes from an earlier commit, out of a folder that no
longer even held that version. The global cache is keyed id+version and **never re-extracts**.
Purging and retrying returned **401**, the honest answer; with credentials, all eleven came from the
feed. ⚠ **So D4's "no local feed present" means the CACHE too** — moving the folder is not enough.

⛔ **A configured local source that does not EXIST is a hard `NU1301`, not a skipped one.** That
killed the job's first CI run before it reached the feed. Development mode never meets it, because
source mapping means those ids are never requested; the canary never meets it, because it creates
the folder by packing. The script now creates it **empty**, which satisfies NuGet, can supply
nothing, and makes the provenance conclusion stronger — and it refuses outright if that folder
holds any package at the pinned version.

**What D5 corrected, and one thing it found on the way.** Both comments now say the clause was
**false when it was written**, name `GuardShippingPublishUsesPackages` as what defends it, and add
the limit neither of them stated: **neither the canary nor its CI job ever contacts the feed**, so
`published-version` is the one consuming what a release actually ships against. ⚠ Keeping the claim
and the guard in the same breath is deliberate — a claim about the release that only a comment
defends is the precise defect this plan exists to close.

⛔ **`package-canary.ps1` still called the packages "private", and Phase C's sweep missed it.** That
correction landed in `CLAUDE.md`, `AGENTS.md`, `nuget.config` and `ci.yml`; the script was not on the
list and nothing would ever have failed over it. ⓘ It is the last non-frozen place carrying the
wrong word — `plans/00001` keeps it in a **frozen** title, by the never-edit-an-approved-plan rule.

✅ **The three stale untracked directories under `tests/` are GONE — verified 2026-09-20.** The
Phase 0 subtraction left `OpenCode.Avalonia.Tests`, `OpenCode.Sdk.Tests` and `OpenCodeForge.Tests`
holding only `bin/` and `obj/`, and this note used to say they were worth clearing. Nine
directories remain and all nine are real test projects, so the "nine test projects" figure in the
canary header now matches the disk as well as the solution. ⚠ **Nothing recorded when they went**,
which is the reason to check a cleanup item before doing it rather than after — a `bin`/`obj` wipe
removes an untracked directory silently, and `publish.ps1` performs one.

**What D4 proved, and why it took two halves.** The CI job `published-version` runs the whole suite
and a real trimmed publish in package mode at the pinned version, restoring from the feed with
`packages: read`. ⛔ **It is deliberately NOT the package canary**, and treating the two as
equivalent is the easy mistake: the canary packs *this commit* at a throwaway version into a local
folder, which proves the `PackageReference` mechanics and says nothing whatever about the bytes on
the feed. Packages published from an older commit are exactly the case no local gate would notice,
and a published version can never be replaced. ⭐ **Two of D4's conditions come FREE on a hosted
runner and cannot be had locally at all** — no local feed, and no global NuGet cache. The cache is
keyed id+version and never re-extracts, so a developer "proving" this locally is at the mercy of
whatever it already holds.

⭐ **The premise was checked rather than assumed**: no shared-library source has changed since
`19f3885`, the commit the packages were built from, so the published bytes still correspond to this
tree's shared sources. Everything since is build files, docs, scripts and tests. Had that not held,
a red D4 would have been drift rather than a defect, and the two are worth telling apart *before*
the run.

⚠ **The suite reads 3,541 · 0 · 23 in that job against 3,551 · 0 · 13 locally, and the TOTAL is
3,564 both ways.** Ten tests move from passed to skipped, not out of existence: the local-feed
guards return `Assert.Inconclusive` rather than pass vacuously when `artifacts/localfeed` is empty,
which is the correct answer on a runner and the reason the totals are the number to compare.

**What D3 shipped:** `GuardShippingPublishUsesPackages` in the **root** `Directory.Build.targets`,
hooked `BeforeTargets="PrepareForPublish"` and scoped by `OutputType != Library` plus
`IsPackable != true` — which selects exactly the shipping apps and picks up a second app without
anyone remembering to opt it in. The hatch is `-p:AllowProjectReferencePublish=true`, declared by
the two publishes that legitimately want project references (`ci.yml`'s trim gate and
`Smoke-PublishedBinary.ps1`, neither of which has feed credentials, and the trim gate must stay
green on a fork). `release.yml` now uploads `src/dist/logs/*.log` as `provenance-<host>`,
`if: always()` and 90-day retention, so the record outlives the run that made it.

⛔ **PACKAGE MODE ANNOUNCES ITSELF, and that line is load-bearing rather than decorative.** The
first draft was silent on the good path, which meant an archived log proved the release's mode
only by the **absence** of the hatch line — and that is exactly what a log looks like when the
target was renamed, when its condition stopped selecting the project, or when the guard never ran
at all. Absence is not evidence. Every shipping publish now states its mode positively, from any
entry point including a bare `dotnet publish`.

⚠ **A `Message`, not a `Warning`.** `Directory.Build.props` sets `TreatWarningsAsErrors`; a hatch
whose own record can be escalated into the failure it exists to avoid is not a hatch.

⛔⛔ **CI CAUGHT WHAT THE LOCAL SUITE COULD NOT, twice in a row on the same mechanism.** `a6fd749`
went in with a green 3,551 local suite and reddened **all four** CI test jobs — the package canary
and every `Build & Test` platform — on `EveryHardcodedRepoPathInBuildFilesExists`. A comment in
`release.yml` named the staging folder as a bare directory. It is a **build output**, and since git
does not track it, it exists on any machine that has ever published and on no CI runner. ⛔⛔ **The
fix commit then failed the same test again, **four jobs again** — because this section named the same directories while
explaining them**, and the write-up of a trap is not exempt from it. `dc418bd` closed it.

✅ **The guard no longer has the blind spot: it asks GIT, not the filesystem.**
`EveryHardcodedRepoPathInBuildFilesExists` reads one `git ls-files -z`, derives from it the set of
directory prefixes git implies, and requires every candidate to be **both** present on disk **and**
tracked. The conjunction is deliberate: it is strictly stronger than the filesystem check it
replaces, so a path that is in the index but deleted locally cannot start passing. It also closes a
Windows-only hole for free, because `Directory.Exists` is case-insensitive there and git's index
never is.

⭐ **That retires the procedural workaround.** The gate recorded above was to *park the staging
folder first and leave it parked until the test is green*, and it existed only because a dev machine
could not reproduce the failure at all. It reproduces directly now: with the folder **present and
full on disk** and nothing moved aside, the old guard passed 4/4 while CI reddened on the identical
tree, and the strengthened guard failed on exactly the two predicted names, each reported as
*present here, but git tracks nothing at or under it*.

⛔ **"Empty" was never the property that mattered**, which is why the mitigation on record never
fired. The root `AGENTS.md` said to sweep with `find src tests -type d -empty`, and the folder is
**full of files** locally, merely untracked. That checklist step is replaced by a note that the
guard now covers this without a manual sweep.

⛔ **The exact boundaries, measured rather than recalled.** Writing `src/<dir>` below as a
placeholder, because the literal spellings are themselves the bug and the regex cannot match a `<`:

| Spelling | Outcome |
|---|---|
| `src/<dir>/*.zip` | ✅ skipped — `*` is inside the regex char class, so the whole token matches and anything containing `*` is skipped |
| `src/<dir>/` | ⛔ checked as bare `src/<dir>` — a **trailing slash is not consumed** |
| `src/<dir>/*` | ⛔ also bare `src/<dir>` — a trailing `/*` is **trimmed back** before the `*` test |

**A glob is safe only with a suffix after the star**, and prose about an untracked directory should
drop the `src/` prefix entirely, which is what the regex anchors on. All three are now carried in
the test's own remarks, where the next reader of the guard will find them.

⛔⛔ **The guard test was VACUOUS on its most important site and only the canary found it.** Both
non-release publishes explain the hatch in a comment directly above the flag, so a whole-file
search matched the **prose** — the assertion passed with the live flag deleted from `ci.yml`'s
publish command, which is the one thing it exists to catch. Predicted 4 reds, got 3; the missing
one was the tell. Comment lines are stripped now, and the re-run produced 4/4 with the right
names. ⓘ Suite **3,551 · 0 · 13**, predicted before the run as 3,545 + 6.

⚠ **The frozen plan contradicts itself on the pin** — a committed pin in a props file is accepted
in *Decisions* (line 87) and rejected in *Alternatives dismissed* (line 104). The pin is operative:
`E4` depends on it, the open-questions list records it as answered, and
`Directory.Build.targets:148` refuses an empty value, so "no pin" is not a reachable state.

---

## Done — Phase C, and how it nearly shipped broken

⛔⛔ **CORRECTED 2026-09-19. This section claimed everything was green and only the maintainer
remained. CI was RED at the time, on ubuntu and macOS, and had been for ~20 commits.** Two guards
that held only on Windows were shipping in packages about to become immutable. See the **CI on this
branch** row. ⚠ **The lesson is not "CI was red" — it is that four local gates were green while the
gate that mattered was not consulted at all.**

⭐ **Status now: CI green at `8692b20`; `C0` re-passed for real at `0de0f4e`; `C1` gates 1–2
re-proven; six-RID trim 6/6.** ⛔ **`A5` is stale and the tag must be re-pointed** — both are agent
work, and neither is done.

⚠ **The intended tag is still `packages-v2026.3.918`, and the day rolling over did not change
that** — the version follows the tag, not the calendar; see the correction below before concluding
otherwise. ⓘ Written 2026-09-18, still current on the 19th; the evidence is pinned to a **commit**,
so it does not expire with the date.

| Next | Who |
|---|---|
| **C2** — push `packages-v2026.3.918`. ⛔⛔ **Irreversible, and the maintainer's to run** | maintainer |
| ⓘ **`F12` is FIXED** — decided 2026-09-18, fix taken over ship-it-known | done |
| ⓘ **`F11`** stays open by an earlier locked decision and does not gate the tag | — |
| **Phase D**, then **Phase E** | agent |

### ✅ A5 PASSED — 2026-09-18, re-run on the final pre-tag commit

⛔ **This heading deliberately names no hash, and that is not laziness.** A5 is locked to *"the
commit actually being tagged, never inherited"*; writing its result into this file **changes that
commit**, so any hash recorded here is falsified by the act of recording it. Naming one cost three
canary runs in a row before the regress was visible. The rule that terminates it: **A5 runs last,
after the final commit**, and this row says only that it did.

Packed at a fresh `0.0.0-local-<timestamp>`; eleven packages; full **Release** suite in package mode
at **3,545 · 0 · 13** across all 9 test projects; real win-x64 self-contained publish, 0 warnings.
ⓘ That figure is the Debug suite's, measured before this text was committed; the post-commit A5 run
confirms it in Release under package mode, and a mismatch is reported as drift rather than edited away.
⭐ **The figure was predicted before the run**, so a smaller count would have been as legible as a
larger one. ⭐ **Package consumption was checked, not inferred** — a green canary is also what a
mixed graph reports, and MSBuild prefers the project output. The app's own `project.assets.json`
resolves all eleven as `Bennewitz.Ninja.*` **packages** at that run's canary version; the only
`"type": "project"` entries are the two product-specific libraries, which is correct.

### ⛔⛔ C0's PREMISE CHECK WAS VACUOUS — corrected 2026-09-19, and the plan's own command is the culprit

⛔ **Plan `00003` line 244 tells you to prove the trees identical with a command that cannot do it**,
and the plan is frozen, so the correction lives here:

```
# ✅ RIGHT — real directory names, wildcarded per family.
git diff <release> feat/agentforge-opencodeforge -- 'src/AgentForge.*' 'src/LayeredEditors.*' src/JsonC
```

⛔ **The WRONG form cannot be written here, and that is itself the point.** The plan's version names
the two families as **bare prefixes with no suffix** — the `AgentForge` and `LayeredEditors` stems on
their own, without `.Core`, `.Sdk`, `.ViewModels`. Spelling those out under `src/` in this file
**reds `EveryHardcodedRepoPathInBuildFilesExists`**, because they are not directories; that guard
fired on this very paragraph's first draft. ⭐ **The guard that refuses to let the mistake be written
down is the same guard that had already been telling us the mistake existed.**

**Git pathspecs match path COMPONENTS, not string prefixes.** A bare family stem selects no files, so
the diff came back empty and empty read exactly like "identical". ⚠ The repo had already said so
twice before anyone noticed: that guard reddened CI on those two strings, and the fix was applied to
the prose without anyone registering that it falsified the verification standing on them.

⛔ **The canary was broken in the same way, which is why it did not catch it.** The empty result was
"canaried" by re-running over `src/ClaudeForge` and getting output — but that is a **different,
real** pathspec, so it proved the command works, never that the pathspec under test matched
anything. This is `scripts/retest/README.md` lesson 7 exactly: a zero that is indistinguishable from
"fixed", confirmed by a needle that was never the needle in question. ⭐ **A pathspec canary must
use the SAME pathspec form against a path that must differ.**

**What was actually diverged:** the parked branch had **no `F7`, no `F8`** — `RestoreJournal.cs` did
not exist there — plus no a11y expander-header names and none of the cross-platform guard fixes. The
2026-09-18 run of **4,434** was real but exercised an *older* `AgentForge.Core` than the one being
published, so it evidenced the wrong code.

### ✅ C0 PASSED FOR REAL — 2026-09-19, parked branch at `0de0f4e`

**4,453 · 0 · 11** across all twelve projects, predicted at 4,453 before the run, against shared
libraries now **verified** identical (empty diff on the corrected pathspec, canaried against
`src/ClaudeForge`, which is non-empty).

⭐ **Two findings that strengthen the neutrality evidence rather than weaken it.** The parked
branch's app and its `OpenCode.*` product libraries compiled **clean** against the new shared surface — all 14 errors in the first
attempt were in `tests/AgentForge.Core.Tests` calling the old *internal* API. And the surface change
itself is `BackupEngine.RestoreAsync` gaining `openProjectRoots = null`: **optional and appended**,
so source-compatible for existing callers.

⭐ **`PublicSurfaceBaselineTests` caught what a hand-sync missed.** Six library files and three test
files were synced by hand and declared identical; the baseline guard then failed on the one exported
line nobody thought about. It has now paid for itself twice — the first was `F3`'s `IShareService`
break passing a 4,367-test green suite.

⚠ **The sync was surgical, not wholesale.** A `SolutionFilterTests.cs` — 177 lines, under the parked
branch's `AgentForge.Core` test project — exists **only** there: it guards the two-app solution
filters, which are correct on that branch and absent from this one-app one. A directory-level
`git checkout <branch> -- <dir>` would have deleted it silently.

ⓘ **Its full path cannot be written in this file either**, for the same reason as the pathspec
above: `EveryHardcodedRepoPathInBuildFilesExists` scans root `*.md` and requires every `src/…` and
`tests/…` it finds to exist **in this tree**, and a parked-only file does not. ⭐ Worth knowing before
documenting cross-branch work here — the anchor can describe the other branch, but it cannot cite
its paths.

### ⓘ Superseded: the 2026-09-18 C0 run

**4,434 · 0 · 11** across 12 test projects, matching the prediction exactly. The OpenCode side
contributes 906 (`OpenCode.Sdk` 332, `OpenCode.Avalonia` 355, `OpenCodeForge.Tests` 219).

⛔⛔ **Fixing a shared library invalidates `C0`, not only `A5` — and it does so SILENTLY.** `C0`
evidences neutrality by running the parked branch's suite, and its stated premise is that the two
branches' shared-library trees are **identical**. `F12`'s fix changed `LayeredEditors.ViewModels` on
the release branch alone, and at that moment the earlier `C0` result stopped describing the code
about to be published — with nothing failing to say so. The first run (**4,429 · 0 · 11**, at
`030ec9e`) was real but is superseded. The fix was cherry-picked to the parked branch and `C0`
re-run; **the number above is the one that counts.**
⚠ **The shared-library trees were proven identical rather than taken from the plan**, as `00003`
requires — `git diff <both branches> -- src/AgentForge.* src/LayeredEditors.* src/JsonC` is empty,
and the same command over `src/ClaudeForge` is **not**, so the empty result is a measurement and not
a bad pathspec. ⚠ `00003` spells the first two as bare family prefixes, which git resolves the same
way; **spelled that way in this file they red `EveryHardcodedRepoPathInBuildFilesExists`**, because
neither is a directory that exists. ⓘ All 9 commits on the parked branch since its last verified run are docs-only.

⭐ **The stronger evidence is not the total.** The two cross-app guards that return
`Assert.Inconclusive` on the one-app release tree — `EveryAppTokenAsharedLibraryNeeds_IsDeclaredByEveryApp`
and `TheTwoApps_DoNotDriftApartOnTrimSettings` — are **absent from the parked skip list**, so they
ran and passed. All 11 skips there are platform-conditional or environment-gated; not one is a
cross-app guard declining to measure. That is neutrality evidenced *by use*.

### ✅ `F12` — found by C0's own prerequisite check, FIXED before the tag

`F9`'s fix (`1077e95`) landed in the **app's** object editor only. There are two classes by that
name and the repo already documents why (`IChildEditorHost.cs`): neither derives from the other, so
a **type test** against either covers half the object editors in play — and so does a **fix**. The
library's copy, the one inside `Bennewitz.Ninja.LayeredEditors.ViewModels`, still rebuilt objects
from schema-derived children.

⭐ **Traced, then reproduced, then fixed.** The shared shell reads `editor.ToValue()` and writes it
at the editor's path via `SetValue`, which **replaces** rather than merges; the library editor's own
`OnChildPropertyChanged` force-fires *"so the hosting group editor always re-invokes `ToValue()` and
writes the complete updated object"*. ⛔ **That plumbing is the sharp edge** — the mechanism that
makes the host notice an edit is what turns a rebuild into a deletion, and it reads as change
propagation.

⭐ **Two tests written before the fix redden by name with the re-emit absent; three more pass either
way and are NOT counted as the catch.** Predicting which two would go red is what makes them
evidence. ⚠ **Verified by test, not in a running app** — `F9` was additionally driven through the
UI; this was not.

⛔⛔ **The cost nobody schedules: it invalidated `C0`.** See the `C0` row above — a shared-library
change breaks C0's identical-trees premise silently. Fix cherry-picked to the parked branch, `C0`
re-run at **4,434 · 0 · 11**.

ⓘ **`F12` never affected the shipped ClaudeForge artifact** — the app uses its own fixed editor. The
defective copy was reachable only through `DefaultPropertyEditorFactory`, i.e. OpenCodeForge and
external package consumers. See [`docs/RETEST-FINDINGS.md`](./docs/RETEST-FINDINGS.md) `F12`.

⛔⛔ **CORRECTED 2026-09-19 — this said the calendar day forces a new version. IT DOES NOT, and
believing it costs a day of rework for nothing.** The previous text read: *"The CalVer is baked into
a passed C1. Tagging on a later day means re-packing at that day's version and re-running both C1
and A5."* The first sentence is true; the second confuses **the day you tag on** with **the version
you tag**.

⭐ **The version comes from the TAG, not from the clock.** `Resolve-ReleaseVersion.ps1` emits
`BuildTimestamp = yyyyMMdd000000` — *midnight local on the tag's own date* — precisely so *"the same
tag built twice produces the same version"*. Pushing `packages-v2026.3.918` on the 19th, or the
25th, still publishes **2026.3.918**.

**Measured on 2026-09-19, not reasoned:** `Resolve-ReleaseVersion.ps1 -Tag packages-v2026.3.918`
emitted `BuildTimestamp=20260918000000`, and a preflight pack at that stamp produced all eleven at
`2026.3.918` with assembly `2026.3.918.0` — **gates 1 and 2 PASSED a day later**. Gate 3 asks only
whether the feed already holds the version; nothing has ever been published, so it is unaffected.

⭐ **`packages-v2026.3.918` is therefore the RIGHT tag even now**, because 2026.3.918 is the exact
version Phase B's retest was driven against. Tagging `packages-v2026.3.919` instead would publish a
version nothing was retested at, and *that* is what would cost a re-pack and a fresh C1 gate 3.

ⓘ **A5 is indifferent to all of this** — the canary packs at a throwaway `0.0.0-local-<timestamp>`,
so no CalVer enters it. It is pinned to the **commit**, not the day.

ⓘ **`C1` PASSED 2026-09-18 for `2026.3.918`**, all three gates. Gate 3 ran for the **first time**
and reported *"none of the 11 ids holds 2026.3.918"*; no `packages-v*` tag has ever existed. It
needed `read:packages`, added to the existing classic PAT **in place** so the token value did not
change. ⓘ Learned while getting there: `release-packages.yml:110` already runs all three gates with
the built-in `GITHUB_TOKEN` before its first upload, so **C1 moves that check earlier rather than
performing one that otherwise never happens**.

---

## Done — the 2026-09-17 state this replaces

⛔ **[`plans/00002`](plans/00002-claude-code-real-config-locations.md) and
[`plans/00003`](plans/00003-release-built-from-shared-packages.md) are APPROVED (2026-09-17) and
FROZEN.** Never edit them. Everything below is drift, which is what this file is for.

**Phase 0 is DONE** — `2c84f47` (removal) and `1e41f87` (guard reconciliation).

**Phase A is DONE** — 2026-09-17, all four steps, at HEAD `904df7d`.

| Step | Result |
|---|---|
| A2 · eleven baselines unchanged | ✅ **No regeneration.** ⚠ The guard's premise assertion is only `Count > 0`, which would pass green if discovery found *one* of eleven — so the count was measured independently rather than trusted: 11 projects declare `<IsPackable>true</IsPackable>`, all 11 assemblies were beside the test, all 11 baselines matched. ⭐ The real evidence of no-regeneration is that **no `.txt.actual` was written** — that guard writes one on every mismatch, so a clean tree after the run is the measurement |
| A3 · full Debug suite | ✅ **3,509 · 0 · 13** — the figure was **predicted before the run** so a smaller count would have been as legible as a larger one, and all **9** test projects reported (a project that silently fails to run is the failure this cross-check exists to catch) |
| A4 · six-RID trim gate | ✅ **6/6, zero IL diagnostics.** ⚠ Zero is the same reading a broken detector gives, so the detector was canaried against synthetic `IL2026` and `NETSDK1144` lines and fired on both. `-c Release` takes the `PublishTrimmed=true` / `TrimMode=partial` branch and each RID produced a real ~27.7 MB single-file exe, so the gate measured a trimmed build rather than an untrimmed one |
| A5 · package canary | ✅ **PASSED** at `0.0.0-local-20260917182607` — packed, restored through an isolated cache, full **Release** suite under `-p:UseSharedPackages=true` at the same **3,509 · 0 · 13**, then a real win-x64 self-contained publish |

**Phase B is DONE** — 2026-09-17, all eight items driven against the package-mode build
`v2026.3.917.1839`, packed at the pinned CalVer `2026.3.917`.

| Item | Result |
|---|---|
| E1 save preserves comments/formatting | ✅ PASS |
| E2 save lands in the right scope | ✅ PASS |
| E3 backup, then restore | ✅ **PASS on the 2026-09-18 re-drive** (was ⛔ FAIL — `F7`, `F8`) |
| E4 secrets stay redacted | ✅ PASS — and it surfaced `F9` |
| E5 artifact resolution | ✅ PASS (source attribution; exact counts recorded as unverified) |
| C3 `--cleanup-restore-sidecars` | ✅ **PASS** — output captured 2026-09-18; it writes to **stderr**, which is why redirecting stdout saw nothing |
| F3 share config + two siblings | ✅ PASS |
| B2 diagnostics-window accessibility | ✅ PASS |

### ✅ Phase C's retest gate is SATISFIED — 2026-09-18

Plan `00003` is explicit: *"All eight items complete before Phase C. A published version cannot be
taken back, so nothing proceeds on a partial retest."* ⭐ **All eight are now complete**: the six
that passed on 2026-09-17, plus `E3` and `E4` re-driven and passed on 2026-09-18 against the
package-mode build `v2026.3.918.839`.

⛔⛔ **Phase C is still blocked, on ONE thing and it is not the retest**: `C1` needs a
`read:packages` PAT this machine does not have (see the Phase A drift section).

✅ **`C3` is now fully green too** (2026-09-18) — and the reason its output "could not be captured"
was wrong: the tool writes to **`Console.Error`**, so redirecting stdout alone sees nothing while
merging stderr sees everything. No terminal was needed. Driven against **three planted** sidecars
so the counts are a measurement rather than a format string over zeros; the app log carried the
identical summary and no process survived the run.

✅ **`F10` is also fixed and verified** (2026-09-18) — expander headers announced
`Avalonia.Controls.Grid`. ⭐ The name was on the **wrong element**, not missing: views already
named the Expander, but focus goes to its `ExpanderHeader` part. ⛔ Two measurements corrected the
plan — a plain string `Header` announces the type name too (so **all twelve** Expanders were
affected, not the reported seven, and there was no fallback to protect), and the selector needs
**`ToggleButton`**, since UIA's `Button` is the peer's answer and a selector from it matches
nothing silently. ⛔⛔ **`F10`'s own diagnosis was wrong**: `Audit-Accessibility.ps1` has always
had the type-name rule and it fires correctly — the audit simply only walks **pages that are on
screen**, and Environment was not open during `B2`.

ⓘ The three defects that blocked this gate, and how each was closed:

- ✅ **`F9` — editing any env value DELETES every env key the app does not model. FIXED
  2026-09-17**, and it was **not an `env` bug**: `ObjectPropertyEditorViewModel.ToJsonValue`
  rebuilt every object from its schema children, so the writer read any unmodelled key as a
  removal — `env` is simply the object where users keep keys the schema never named. The
  editor now carries the editing scope's unmodelled keys across a save and re-emits them
  verbatim. ✅ **Verified in the running app 2026-09-18** — the save diff lists ONE change and no removals; both unmodelled keys and both comments survive on disk.
- ✅ **`F7` — backup captures project files; restore silently ignores them. FIXED 2026-09-18.**
  ⭐ **`RestoreProjects` was never missing** — it refused on `IsUnderUserProfile`, and the retest
  project lived at `C:\c\cl\retest-2026.3.917`. **One check was doing two jobs**: a real defence
  against a crafted manifest, and an unwritten scope limit that excluded every repository kept
  outside `~`. Restore now also authorises paths **this machine's own `~/.claude.json` lists as
  projects** — a source the archive cannot forge — and the skip message no longer calls a refusal
  a missing path. ⛔ Worktrees are the same shape and are **not** fixed; see `F11`.
  ⛔⛔ **The first attempt covered ONE of backup's THREE project sources and would not have closed
  the finding.** ClaudeForge does not write `~/.claude.json` — Claude Code does — so a project
  opened only in ClaudeForge is absent from it, which is exactly what a *Settings only* backup
  captures. Caught by checking the premise: the previous fixture was missing from a **62-entry**
  list. Restore now calls **backup's own discovery** (`CollectSettingsFilesForDiscovery` is
  `internal` for this) and the host hands over its open project. ⚠ `BackupEngine.RestoreAsync`
  gained an optional `openProjectRoots`; the public-surface baseline moved in the same commit.
- ✅ **`F8` — a successful restore leaves every `.pre-restore-*.bak` sidecar behind. FIXED
  2026-09-18.** A `RestoreJournal` records each sidecar as it is written and a clean restore
  deletes exactly those, reporting the count. ⚠ Three bounds: **only on a run with zero file
  failures** (a partial restore is when the undo trail matters), **only this run's** paths, and
  **only** names matching the pre-restore pattern. `--cleanup-restore-sidecars` still owns
  everything older — including the 5,899 from the retest.
- ✅ ✅ ✅ **ALL THREE VERIFIED IN THE RUNNING APP, 2026-09-18** — re-driven through UIA against
  the package-mode build **`v2026.3.918.839`** (packed at `2026.3.918`, freshness proven by the
  consumed package carrying `BuildAuthorisedRoots`). `E3` and `E4` both **PASS**; the eight-item
  list is complete again. ⭐ **`F8` was measured, not assumed**: a final count of zero cannot tell
  *swept* from *never written*, so the trees were polled at 150 ms across a live restore —
  project **peak 2 → final 0**, `~/.claude` **peak 6,023 → final 0**. ⭐ That peak of 2 under the
  project is also independent `F7` evidence, because the original finding's tell was *zero*
  sidecars there. ⚠ The fixture project sat at `C:\c\cl\retest-2026.3.918`, **outside the home
  folder** — the condition the failure needed.

⭐ **`F6` was verified FIXED in passing** — it named exactly 26 unnamed chevrons and the running app
exposes exactly 26, all announcing *"Expand or collapse"*. The matching count is what makes it
conclusive. `F10` is new and is the same class.

### Next, in order

1. ✅ **`F9` is fixed** — see the row above. ⚠ Code only; the retest item is still open.
2. ✅ **`F7` and `F8` are decided and fixed** — 2026-09-18, both recommendations taken: restore
   project entries, and sweep the sidecars once a restore has committed. ⚠ Code only.
3. ✅ **`E3` and `E4` re-run and PASSED** on `v2026.3.918.839`, 2026-09-18. All eight items green.
4. **Then Phase C** — C0 neutrality on the parked branch (⭐ no port needed, the trees are
   identical), C1 preflight, C2 the tag. ⛔ C2 is irreversible. ⛔⛔ **C1 needs a `read:packages`
   PAT that this machine does not have** — provision it before Phase C starts.
5. **Phase D**, then **Phase E** (00002 as the second package version).

ⓘ **A5 re-runs immediately before the C2 tag**, on the commit actually tagged — a locked decision,
not an optional extra.

### ⭐ Decisions taken 2026-09-21 — locked, do not relitigate

1. ⛔ **The RETROFIT is declined: `LayeredEditors.*` is not published to nuget.org from inside this
   repository.** No trusted-publishing policy on `JanusMael/ClaudeForge`, no second release
   workflow here, no remap of this repo's `nuget.config`. The draft that proposed it —
   `plans/00004-layerededitors-goes-public-on-nuget.md` — was deleted while still untracked.
   ⭐ **`00004` is free for the next plan**: nothing was committed, so the number was never consumed.
   ⭐ **A plan that is only a draft is deleted, not superseded** — the *new number referencing the
   old* rule governs plans that were **approved**, and applying it to a draft would have permanently
   spent a number on a direction nobody took.
   ⛔⛔ **This decision is NARROWER than it first reads, and the first version of this entry got it
   wrong.** It said *"`LayeredEditors.*` does NOT go public on nuget.org. The direction is
   DISCARDED"*, which a future session would have read as settling public publication for good.
   **It does not.** What was declined is *publishing from here*; see decision 2.
2. ⭐ **Extraction proceeds separately: the five `LayeredEditors.*` ids move to a new dedicated
   repository `Bennewitz.Ninja.LayeredEditors` and publish to nuget.org from there.** Confirmed by
   the maintainer 2026-09-21. ⭐ **Staged, and the order is the point** — the new repo publishes and
   is **verified from the feed** before any change here removes the source. ⛔ **Nothing in this
   repository changes yet.** The second half — deleting those five projects from `src/` and
   consuming the packages instead — is a later, separate change, and is **not** approved by this
   entry. ⓘ The plan lives outside this repo (`Bennewitz.Ninja.Templates`, draft); repointing
   DiffView is a named follow-up owned by that project, not a gate.
3. ⓘ **`AgentForge.*` and `JsonC` are untouched** and keep publishing exactly as today: GitHub
   Packages, authenticated reads, `scripts/Publish-Packages.ps1`. The pending
   `packages-v2026.3.921` is unaffected.
4. ⭐ **Pushing a fast-forward commit to `main` is ordinary work, not a disturbance.** The standing
   bound is on **rewriting** `origin/main` — force-pushes and history surgery — not on landing an
   ordinary commit. ⚠ **`git push origin main` prints `remote: - Changes must be made through a pull
   request.` and then SUCCEEDS** for the repository owner. That line reads exactly like a rejection
   and is not one: read the ref update (`6a5c3c5..a987357  main -> main`) and `git rev-list --count
   @{u}..HEAD`, never the `remote:` prose. Same lesson as the 2-ref push cap — the `remote:` lines
   are advisory and the **effect** is what must be measured.

⚠ **How the wrong scope got recorded, because the mechanism will recur.** The question put to the
maintainer named the *draft* — and that draft was the retrofit — so *"discard it"* was a correct
answer to a narrower question than the entry then claimed. **A decision is only as wide as the
question that produced it.** When recording one, write down what was actually asked, not the
largest reading the answer permits.

ⓘ **Measured while reconciling, and it outlives the discarded draft.** 00001 gates public
publication on *"ClaudeForge, continuing OpenCodeForge work, and a third project"* having exercised
these libraries. The third project exists — **DiffView** (`C:/c/cl/Bennewitz.Ninja.DiffView`) — but it
consumes the **unprefixed** id `LayeredEditors.Avalonia.Diagnostics` at `1.0.1`, hand-packed from a
ClaudeForge checkout into the sibling folder feed `../nuget-local`. ⛔ **It has exercised the CODE and
never the `Bennewitz.Ninja.*` PACKAGES**, and its own `packageSourceMapping` matches `LayeredEditors.*`,
which does **not** match `Bennewitz.Ninja.LayeredEditors.*`. So publishing these ids would neither
break DiffView nor reach it — it would keep resolving `1.0.1` from a folder, and a throwaway-project
restore check would pass while the one real external consumer never moved. **Read 00001's gate as met
by the library and not by the packaging.** ⚠ That pin is a hand-packed copy with no link back to the
CalVer ids, so it drifts silently.

### ⭐ Decisions taken 2026-09-20, second batch — Phase E step 3, locked

Five decisions, all taken interactively. ⭐ **The through-line is COMPILE-ENFORCEMENT OVER GUARDS:**
where a wrong value could be supplied silently, the answer was to make the wrong call impossible to
write rather than to add a scan that catches it afterwards. A guard is a thing that can be written
too narrowly, and this repository has shipped exactly that failure twice.

- **The resolved home is threaded as a REQUIRED parameter, never a process-wide static.**
  `PlatformPaths.ClaudeHome` and thirteen members derived from it became methods taking
  `ClaudeEnvironment`. ⭐ Required is the entire mechanism: it turns every stale call site into a
  compile error, and an optional parameter would have found none of them. ⛔ The rejected
  alternative was a set-once static holder consulted after the `AsyncLocal` test override — cheaper
  by ~90 call sites, and rejected because a site that forgot it would resolve the *old* tree with
  no error and no failing test.

- **`BackupEngine` takes the environment as a REQUIRED constructor argument, and
  `BackupEngine.Default` is DELETED.** ⛔ The parameterless `Default` static was the one remaining
  way to obtain an engine pointed at the default home, so removing it is the point rather than a
  side effect. ⚠ The environment must not cross into `IBackupClient`, which is product-neutral and
  which OpenCode implements — binding it to the engine instance is what stops `ClaudeEnvironment`
  reaching that interface.

- **`SchemaRegistry.ClaudeCodeProduct` is de-statified; `ClaudeCodeProductFor(env)` is the only
  way to obtain the descriptor.** ⛔ **The cheaper split was measured and REFUSED.** Keeping a
  static for identity (~90 sites read only `Id` / `ArchiveFolder`) and env-binding just the layout
  looked free, until `ProductDescriptor.Backup` was read: it is
  `BackupLayout ?? ProductBackupLayout.Empty`, so a descriptor with no layout yields **zero
  sections** and an archive that writes nothing while reporting success. ⚠ A null layout is
  therefore not a loud failure and cannot be used as one. Equality was never the hazard —
  `BackupRequest.Includes` compares on `Id` precisely so a separately-constructed descriptor still
  matches.

- **The two direct `Environment.SpecialFolder.UserProfile` reads are routed through
  `PlatformPaths.UserProfile`** — `AdditionalDirectoriesResolver.ExpandTilde` and
  `PermissionMatchContext.FromEnvironment`. ⓘ **Not a `CLAUDE_CONFIG_DIR` defect**: the variable
  moves the config directory, not the profile, and both sites want the profile. They are fixed for
  a different reason — they bypassed the `AsyncLocal` sandbox, so a test that relocated the profile
  still resolved `~` to the developer's real home. ⭐ Production behaviour is unchanged by
  construction (the override is null outside tests), and the step-5 guard now ships with **zero**
  allow-list entries for these, which is what makes it strong.

- **The refactor lands as staged commits, each building, each with its own verification** — Core
  threading, then the descriptor and app composition, then tests green with the parity guard and
  its canary, then the step-5 bypass guard, then baselines and CHANGELOG. ⚠ Slices one and two are
  individually non-runnable: the suite does not compile until the third.

- **A PR is opened against `main` now and merged after Phase E lands.** ⭐ Two reasons, and the
  second is the one that is easy to miss: it stops `main` drifting further from what actually
  shipped, and **`release.yml` reads the merged PR's body for release notes** — with no PR the
  curated `## [Unreleased]` section cannot reach a release except by being pasted into the tag
  message. ⛔ Merging is still **not** a plain `git merge`: the merge base is `3c7aaab`
  (2026-09-01) and `main`'s paths no longer exist here, so the hand-port procedure in `AGENTS.md`
  governs, exactly as it did on 2026-09-16.

ⓘ **`F11` (external worktrees) was NOT among these and stays open**, by the earlier locked decision
that parked it until after the release rather than rejecting it.

### ⭐ Decisions taken 2026-09-20 — locked, do not relitigate

- **`D6` is prepared but NOT cut by the agent, and the CHANGELOG is the maintainer's too.** The
  agent verifies the gates and reports readiness; the `[Unreleased]` section and the `v*.*.*` tag
  are both written and pushed by a human. ⓘ Narrower than the earlier split, deliberately: the
  release notes are the maintainer's voice, not a generated artifact.
- **The four dangling references to `OPENCODEFORGE-PLAN.md` are corrected in place**, each saying
  the document is deliberately absent here and naming the parked branch that still has it.
  ⛔ **Nothing guards this class**: the path guard scans `src`/`tests` prefixes only, so a broken
  link under `docs/` or a bare filename reference is invisible to CI. Found by auditing a branch,
  not by a test.
- **Merged local branches are deleted once their content is located in the tree**, never on
  ancestry — squash merges make ancestry lie. ⭐ **`backup/pre-trailer-rewrite-20260916` STAYS**:
  it is the only copy of the pre-rewrite history and the one thing here that is not recoverable.
- **`delete_branch_on_merge` is ON** (set 2026-09-20), so this cleanup is the last manual one. It
  affects only branches merged via PR afterwards and can never catch `feat/agentforge-opencodeforge`,
  which has no PR.
- ⛔ **This remote refuses a push that updates more than TWO refs**, and says so only in the
  `remote:` lines. Recorded in `AGENTS.md` under *Pushing more than two refs at once*.
- **A dead Markdown link is a failing test now**, and the LINK rule covers `docs/` while the
  PROSE-PATH rule still does not. ⭐ The exclusion was reasoned for prose — a document may name a
  path that does not exist yet — and **that reasoning does not transfer to a link**, which promises
  navigability whenever it is written; one of the four dead references lived in `docs/`.
  ⛔ **`plans/` is never scanned**: an approved plan is frozen, its links are specification, and a
  guard reddening on them would force the edit the freeze forbids. Proven by canary, not asserted —
  a planted dead link in `docs/` reddened and one in `plans/` did not.

### ⭐ Decisions taken 2026-09-19 — locked, do not relitigate

- **C2 is clear to tag**, at `packages-v2026.3.918`. Every gate is green on the tagged commit, and
  the workflow re-runs its own three gates with `GITHUB_TOKEN` before the first upload. ⓘ The tag
  fires **only** `release-packages.yml` — `release.yml` filters `v*.*.*`, which a name starting
  `packages-v` cannot match, so no app release and no GitHub Release is created.
- **`F12` was NOT driven through the UI before the tag**, deliberately. It is verified by a
  class-boundary reproduction plus a read of the shared shell's write path; `F9` additionally got a
  UI drive. ⚠ Recorded so the asymmetry is a known choice rather than an oversight.
- **Publishing from `release/claudeforge-on-packages` rather than `main` stands.** Plan `00003` D6
  flags this as a decision to take *before* Phase C, and it was taken: the earlier locked decision
  holds, split work stays out of `main` until the maintainer approves. ⭐ A tag pins its commit
  permanently, so a later branch rewrite — one already happened on 2026-09-16 — cannot orphan it.
  ⛔ Merging first was rejected on cost, not principle: the merge base is `3c7aaab` (2026-09-01) and
  `main`'s paths no longer exist here, so it is `AGENTS.md`'s hand-port procedure, not `git merge`,
  and that is a session of work in front of an irreversible step it adds no evidence to.
- **`F11` stays open and is revisited after the release.** The sound fix spawns `git` once per known
  project, each with a timeout, immediately in front of a destructive operation — a real runtime cost
  and a real dependency on `git` being present. ⛔ Not folded into release week. ⓘ Nothing regresses
  by waiting: the refusal is reported honestly, and external worktrees are only captured in Full mode.
- **The six-RID native-host gap does not apply to C2** — settled by evidence, not judgment.
  `release-packages.yml` states it: *"These eleven are RID-neutral libraries; the cross-platform
  surface belongs to the apps."* The trim matrix is app-level evidence for Phase D, where
  `release.yml` builds each RID on its native host anyway.

### ⭐ Decisions taken 2026-09-18 — locked, do not relitigate

- **`F7`: restore the project entries** rather than stop capturing them. Both UI texts already
  claimed this behaviour, so the archive, the Backup tab and the Restore tab all become true at
  once — the alternative would have made the product honest by removing a capability.
- **`F7` authorisation comes from `~/.claude.json`, not from the manifest.** ⛔ The security check
  is NOT removed. A path is written to when the running user's home contains it, or when this
  machine's own project list names it; both are things a crafted archive cannot forge.
- **`F8`: sweep after the restore commits**, not "keep them and say so". ⚠ The sweep is bounded to
  a zero-failure run, to this run's own sidecar paths, and to the pre-restore filename pattern.
- **`F11` (external worktrees) is NOT fixed by the same move, on purpose.** Authorising them
  soundly means running `git worktree list` per project in front of a destructive operation. That
  cost gets decided on its own, not smuggled in beside `F7`.

### ⭐ Decisions taken 2026-09-17 — locked, do not relitigate

- **The release CalVer is chosen at the START of Phase B, not in advance.** Phase B's build must be
  packed at the exact version the tag will carry, and the retest is driven by hand through the UI, so
  pinning a day before the retest is ready just means re-packing. ⭐ Plan `00003` (line 51) already
  settles the apparent risk: publishing `2026.3.920` and then `2026.3.925` is **ordinary**, and
  "would cost a second version" is a *preference*, not a constraint. Only re-pushing an existing
  version is impossible.
- **A5 is re-run immediately before the C2 tag**, on the commit actually being tagged — not inherited
  from this Phase A run. ⛔ C2 is irreversible and a published version can never be replaced, so the
  five minutes buys away any argument about what was certified. ⚠ This is deliberately *stricter*
  than "the delta looks docs-only": that judgment is exactly what should not be trusted at an
  irreversible step.
- **PR #65 was admin-merged into `main`** as `c149b82` (`AGENTS.md` only, +10/−4, all checks green).
  ⓘ It does **not** touch the release path; `main` and this branch diverged long ago.

### ⛔ FOUR headless flakes — the trigger fired on 2026-09-19, and the suspect is confirmed

⭐ **This section's own standing instruction — *"if a third appears, treat the headless session's
shared state as the suspect rather than the individual tests"* — fired, and the evidence now
supports it rather than merely suggesting it.**

**2026-09-19 produced two, in CONSECUTIVE full runs, in the SAME class** — `ReloadHardeningTests`
in `ClaudeForge.Tests` — with two *different* tests and two *different* symptoms:

| Run | Test | Symptom |
|---|---|---|
| 1 | `LoadAllWorkspacesAsync_ConcurrentCalls_ConvergeWithoutDeadlock` | `IOException` — temp `settings.json` "used by another process" |
| 2 | `PersistentToolVms_ProfilesVm_SurvivesReload_SameInstance` | `Dispatcher.VerifyAccess` — "the calling thread cannot access this object" |

⭐ **Then the decisive measurement: the whole class runs 7/7 green, THREE times, in isolation.**
Stable alone, unstable inside the full run. So the variable is the **full-run context**, not the
tests — the suspect is the process-global headless session state that earlier tests leave behind,
exactly as predicted.

⚠ **`[assembly: DoNotParallelize]` does not exclude this.** It governs parallelism *within* an
assembly; `ClaudeForge.Tests` still runs 1,742 tests sequentially in **one process** against one
process-global Avalonia headless session, and VSTest runs separate test *assemblies* concurrently.
Both leave room for what is being seen. ⛔ Do not re-diagnose this as "a flaky test" — two different
tests with two different exceptions in one class, green in isolation, is not a property of either
test.

✅✅ **ROOT-CAUSED AND FIXED 2026-09-22 — it was lazy application set-up, not "contamination".**

⛔⛔ **CORRECTED 2026-09-24 — the heading above is FALSE. Not root-caused, not fixed.** Everything
below it is kept as the record of what was believed. Avalonia 12.1.3's own source
(`Headless/Avalonia.Headless/HeadlessUnitTestSession.cs` in the AvaloniaUI/Avalonia repository, read here): with no
`[AvaloniaTestIsolation]` on the assembly — this repository sets none — isolation defaults to
`PerTest`, and under `PerTest` EVERY `Dispatch` runs `EnsureIsolatedApplication()`:
`Dispatcher.ResetBeforeUnitTests()` then `AppBuilder.SetupUnsafe()`. The app is rebuilt for every
test; there is no first build to move, so the warm-up does nothing on the failing path, and the
stack below — `SetupUnsafe` inside a test's dispatch — is what EVERY test does, not evidence that
set-up was skipped. The guard reads `Application.Current` inside a dispatch, so it is true by
construction; the "canary" compared a read outside a dispatch. It passed on CI for PR #76
(Windows), the run in which the failure recurred. Handed over by the ClaudeForge session
(2026-09-24), which wrote PR #74; the Avalonia source was re-read here before correcting.
▶ **OPEN.** Candidate: `[assembly: AvaloniaTestIsolation(PerAssembly)]` (builds the app once; all
headless tests then share one `Application`), unverified, tried on its own branch and judged only by
repeated CI on Windows and Ubuntu. If it does not hold, delete the bootstrap rather than keep a
linked file that exists for a wrong reason. ⚠ `plans/00006` step 5 carries this bootstrap and its
guard into xUnit as proof of set-up ordering; that premise is false (drift, never edited into the
frozen plan), and ScopedEditors' xUnit port carries the same claims.

⛔ **The 2026-09-22 entry that stood here was wrong and is corrected rather than deleted.** It read
this as a third class of the same process-global contamination, on the strength of a matching
exception message. The message matched; the cause is more specific, and naming it "contamination"
is what kept it unfixed for three weeks.

`SchemaProvenanceBadgeTests.ClaudeCode_FallenBackToBundled_SaysTheFetchWasTried` threw
`"The calling thread cannot access this object because a different thread owns it"` on a PR whose
whole diff was a Markdown file, while the **same job passed in the duplicate CI run of the identical
commit**. Its stack is the answer:

```
HeadlessUnitTestSession.DispatchCore
  -> EnsureIsolatedApplication()      <- the app is built HERE, inside a TEST
    -> AppBuilder.SetupUnsafe()
      -> AvaloniaHeadlessPlatform.Initialize -> Compositor..ctor
        -> DefaultRenderLoop.Add -> Dispatcher.VerifyAccess   THROWS
```

⭐ **`HeadlessUnitTestSession.GetOrStartForAssembly` starts the session's THREAD; it does not build
the Avalonia application.** `EnsureIsolatedApplication()` runs lazily from `DispatchCore`, so
application set-up belonged to whichever test dispatched first — the exact ordering
`HeadlessSessionBootstrap` was added to remove, and which its own XML doc claimed it had removed.
**The bootstrap was load-bearing in name only.**

⭐ **Proven twice, once without CI.** The stack above shows `SetupUnsafe()` running inside a test,
which it can only do if set-up had not already happened. Then locally: strip the new warm-up
dispatch and `Application.Current` is **null** at `[AssemblyInitialize]`; restore it and it is
non-null. That is a direct measurement of the defect, not an inference from a flaky log.

⚠ **Why the thread is ever wrong.** `Dispatcher.UIThread` is a lazily-resolved process-global that
binds to whichever thread touches it first — the property `LiveLogWindow` already documents for the
shipping app, where constructing an `AvaloniaObject` too early "forces `Dispatcher.UIThread` to
resolve". In a full run a non-headless test can bind it to the MSTest thread; the session thread
then builds the compositor and `VerifyAccess` fails. Alone, nothing gets there first — which is
exactly the "green in isolation, flaky in the full run" signature, and why that signature pointed at
*ordering* rather than at any test.

**The fix** is one warm-up `Dispatch` in `[AssemblyInitialize]`, making the session thread the first
toucher deterministically, on the one path MSTest guarantees runs before every test.
`HeadlessSessionBootstrapTests` guards it and **was canaried**: removing the dispatch fails it with
the message that names the cause.

⚠ **What is NOT claimed.** The 2026-09-19 `IOException` (temp `settings.json` "used by another
process") is a different fault and is untouched by this. The 2026-09-19 `Dispatcher.VerifyAccess` in
`ReloadHardeningTests` is *probably* this same cause — same exception, same conditions — but its
stack was not captured, so that is a reasonable inference and not a measurement. ⛔ **The local
repro attempt failed: 0 of 6 full-assembly runs on Windows reproduced anything.** This was fixed
from the stack trace and the canary, never from a reproduction, so do not treat a green local loop
as evidence the fix works — the CI record is what will show that.

✅ **THE OTHER EXPOSED ASSEMBLIES NOW TAKE THE SAME FIX, AS A LINKED FILE.**
`HeadlessSessionBootstrap.cs` and `HeadlessSessionBootstrapTests.cs` sit at the repo root beside
`AssemblyInfo.InternalsVisibleTo.cs` and are **linked, never copied**, by every project that runs a
headless session — `ClaudeForge.Tests`, `LayeredEditors.Avalonia.Tests` and
`LayeredEditors.Avalonia.Diagnostics.Tests`. `Assembly.GetExecutingAssembly()` resolves per
compiled assembly, which is what makes one shared file correct: each warms up *its own* session.
⭐ The guard is linked with it, so a project cannot take the fix and silently lose it.

⛔ **The count was wrong when first recorded here, and the correction matters.** This said **four**
assemblies were exposed. It is **two** — `AgentForge.Sdk.Tests` and `ClaudeForge.Sdk.Claude.Tests`
match `HeadlessUnitTestSession` only in a **comment** inside their `Parallelization.cs`; neither has
an `[AvaloniaTestApplication]` and neither runs a session. ⚠ **Linking the bootstrap into them would
have broken them**: with no application to build, `[AssemblyInitialize]` becomes a hard failure for
the whole assembly. A `grep -l` for a type name counts mentions, not uses.

ⓘ Proof the link took effect rather than merely compiling: the two newly-linked assemblies went
**216 → 219** and **92 → 95** tests, and the suite total moved **3,600 → 3,609** (+9 = 3 guards ×
3 assemblies).

ⓘ **Not fixed, deliberately, and it did not block the Phase D work**: CI was green on all five jobs
at `19f3885`, and the local failures were re-run green. But a local full suite can no longer be
relied on first time, which is worth knowing before reading a single red run as a regression.

ⓘ The two earlier occurrences follow, kept because the pattern is the finding.

- **2026-09-18 · `GuiSave_WritesEveryProductsChanges_NotJustTheFirstSection`** — failed once in
  the full-solution run, passed in isolation immediately after and in the next full run.
- **2026-09-17 · `LoadAllWorkspacesAsync_ConcurrentCalls_ConvergeWithoutDeadlock`** — below.

Failed once in `ReloadHardeningTests`, on a run that was also building, then passed **five**
consecutive full-project runs after it. It fires three overlapping `LoadAllWorkspacesAsync`
calls while rewriting the settings file between them, so it is timing-sensitive by
construction and the lack of a deadlock is its only real assertion. ⓘ Recorded rather than
dismissed: the fix landing that day touches no part of that path. **If it recurs, it is a
real reload race, not noise** — do not re-run until green and move on.

### ⚠ Phase A drift — three things the plan did not name

- ⛔ **The `gh` token on this machine has NO `read:packages` scope** (`repo`, `workflow`, `read:org`,
  `read:user`…). **C1 cannot run until that exists**, and finding out at C1 means finding out one
  step before the irreversible one. Provision the PAT before Phase C starts, not during it.
- ✅ **`scripts/package-canary.ps1` line 6 was stale from Phase 0** — *"both apps, the four
  product-specific libraries … all twelve test projects"*. Corrected to the one-app tree: the app,
  its **two** product-specific libraries, the sample, **nine** test projects.
- ⛔ **Line 10 of that same file was deliberately LEFT WRONG.** It still reads *"this canary, and the
  release"*, which is finding **1** above verbatim — the release does not publish from package mode.
  ⚠ It is **evidence, not a typo**: Phase D changes the behaviour, and the comment is corrected there
  in the same change. Fixing the words now would erase the only in-repo trace of the defect while
  leaving the defect.

### ⚠ Phase 0 drift — the approved plan's list was incomplete

Recorded here rather than in the plan, which is frozen.

- **It named five deletions and sixteen were needed.** Beyond the six projects: `OpenCodeForge.Only.slnf`,
  three winget manifests, the winget-submit dropdown option, the linux desktop asset, the
  install-probe snapshot **and an orphaned OpenCode test living inside `ClaudeForge.Tests`**, two
  scripts, the CI trim-check step, and the `PublishApps.ps1` row.
- ⛔ **It did not anticipate 27 reddened guards.** None was a product defect; every one was a guard
  correctly noticing the tree holds one app. They needed **three** different answers — narrowed
  lists, lowered floors that still trip on a broken regex, and two guards that now return
  `Assert.Inconclusive` because a comparison with one subject cannot be made honestly.
  ⭐ **Widening them again is one grep: `TWO-APP GUARD NARROWED`.**
- **The solution-filter concept collapsed.** With one product a filter selects the whole solution and
  `FilterExcludesTheOtherProduct` has nothing to exclude, so the filter and its four tests were
  deleted. They return from the parked branch with the second app.
- **`LE.DangerBorder` / `LE.DangerText` are exempted, not deleted** — their only consumer was
  OpenCodeForge, and deleting them is a shared-library change Phase A forbids. ⛔ **The exemption is
  written in BOTH guards that ask the question** (`NoDeadBrushTokensTests` and
  `ThemeResourceIntegrityTests`); fixing one and believing it done lets the pair disagree silently.
  Delete tokens and both lists in Phase E.
- ⛔ **`BuildFilePathIntegrityTests` bit twice, both times on text written minutes earlier** — a
  comment in `package-canary.ps1` and six lines in this file. It scans root `*.md`, area
  `AGENTS.md`, `scripts/`, `src/publish/` and `packaging/`, and reads any `src/…` or `tests/…`
  spelling as a claim the path exists. **Write deleted projects as bare names.**

---

## Done — 2026-09-17, earlier: the ClaudeForge regression state

### The next steps, in order

1. **The eight outstanding retest items** — [`docs/MANUAL-RETEST-PLAN.md`](docs/MANUAL-RETEST-PLAN.md).
   **Every one needs a human driving the app; none is a code task.** `E1` (`JsonC` preserves
   comments on save) first: newest library, no release behind it, and its failure mode destroys
   user content silently. ⚠ **`E1` matters more than it did on 2026-09-15**, because 2026-09-16
   replaced the YAML front-matter parser wholesale with `main`'s, so the artifact write path moved
   underneath it.

   ✅ **The build under test is ready** — `2026.3.917.1244`, single-file, zero IL diagnostics, at
   `src/ClaudeForge/bin/Release/net10.0/win-x64/publish/ClaudeForge.exe`. ⛔ **It was missing until
   2026-09-17**, and the two Windows exes under `artifacts/` are **2026-09-14 leftovers** from the
   `F5` investigation — old enough to predate both the YAML parser replacement and `F3`, so
   retesting against one would exercise the very write path `E1` exists to check. That path is not
   in the repository and does not survive a clean; the retest plan now carries the publish command.

   ⓘ **`F1`, `F2`, `F3`, `F4` and `F6` are fixed, not open** — all five await a look at the running
   UI, which is why they join the retest list rather than leaving it. ⚠ The old note that `F2` and
   `F4` are one job is spent: they were, and the resolution was to split the caution palette by
   **role** rather than to find one colour satisfying both. No token is being pulled two ways any
   more.
2. **Contribute two changelog entries to `main`.** The `NumericUpDown`/`AutoCompleteBox` and
   spinner-button accessibility entries are branch-authored, `main`'s `CHANGELOG.md` has never
   listed them, and the work shipped in `v2026.3.916` through PR #53 — so users have it and `main`
   does not say so. They were hand-carried across **two** `main` rebases on 2026-09-16 and will
   need carrying again at the next one. ⭐ A small PR against `main` ends the recurrence; nothing
   done on this branch can.
3. **Decide on a PR for this branch.** Pushed and level with origin; whether it gets one is still
   open, and opening it against `main` is a locked decision the maintainer has not made.
4. **The first `packages-v*` tag.** ⚠ **The preflight that "passed" on 2026-09-14 exercised gates
   1–2 ONLY** — gate 3, *does the feed already hold this version*, is **SKIPPED when no token is
   set**, and this machine has none. The script says so in its own output. That run is evidence the
   eleven packages build and stamp correctly at `2026.3.914`, and **no evidence at all** that the
   feed is clear. Re-run with a real token before tagging:
   `scripts/Publish-Packages.ps1 -PackageVersion <v> -PreflightOnly`.
   ⛔ Nothing is on the feed yet, every version there is permanent, and the CalVer is
   day-resolution — one release per calendar day, recovery is tomorrow.

ⓘ **Locked decisions — do not relitigate.** The package tag is a dedicated `packages-v*` prefix,
not either app's. A same-day second package release fails at pre-flight by design. `PublicVersion`
is kept but emitted only by `Resolve-ReleaseVersion.ps1`. OpenCodeForge testing is parked
deliberately. The `maui-windows` preflight removal was correct and must not be restored.

**Settled 2026-09-16:**

- ✅ **`F3` is BUILT, and the settled design is what shipped** — enum outcome, changed signature,
  sentence per outcome through a new `OnTerminalStatus` hook wired to the centre pill. Six resx
  keys across all nine locale files; `ShareOutcomeTests` is the guard. ⭐ `TryStart` returns `bool`
  now, so `Failed` is read from a measurement rather than assumed — a canary that made it claim
  success reddened exactly the two tests that assert it, and no others.
  ✅ **The two siblings are closed too**, in a second pass on request: *Share log* and *Share
  backup archive* now report through the same pill. One mapper, `FileShareStatus.Describe`, for
  both — they live in different assemblies, so a second switch would drift unnoticed. Backup's
  three sentences come from the host's resx via `BackupPageText`, which means **OpenCodeForge
  supplies them too**: it wires no share service, so its button now says *unavailable* instead of
  doing nothing silently.
- **`F3` changes `IShareService`'s SIGNATURE**, rather than adding a second member.
  `ShareTextAsync` returns what it actually did; `ShareFileAsync` gets the same treatment.
  ⓘ Breaking in name only: **one** production call site, one implementation, two test fakes, and
  ⛔ **no surface-contract test pins it** — `PublicSurfaceContractTests` lives in
  `AgentForge.Sdk.Tests` and covers the SDK, not this package, contrary to what
  `RETEST-FINDINGS` says. Nothing is on the feed, so this is as cheap as it will ever be.
- **The outcome is an ENUM of what happened** — clipboard / browser / mail client / unavailable /
  failed — not a bool and not a record. ⚠ There is **no share sheet on any platform**: macOS would
  need `NSSharingService` behind a TFM that was never compiled. `Failed` and `Unavailable` stay
  distinct because the status pill treats them differently (failure sticks, success auto-clears).
- **The AI attribution trailers are gone from this branch**, and `main` was never touched — see
  the rewrite note below.
- **Still no PR, and the lock stands.** ⓘ `ci.yml` and `codeql.yml` both trigger on
  `branches: ['**']`, so a branch push already runs full CI and CodeQL; a PR would add review
  surface, not verification, on work still in motion.
- **The first `packages-v*` tag waits for the split.** Nothing consumes the packages today — both
  apps default to `ProjectReference` and the canary already proves package mode — so publishing an
  immutable version now spends a number that cannot be reused. ⓘ The local preflight gap is
  narrower than it looked: `release-packages.yml` runs `Publish-Packages.ps1` in Actions with
  `packages: write`, so **gate 3 does run on the real publish**; only a local `-PreflightOnly` is
  blind, and this machine's `gh` token carries no `read:packages`.

**Settled 2026-09-14:**

- **`F5` is refuted on this session's evidence**, rather than held open pending a second machine.
- **Both apps run `TrimMode=partial`.** Not to fix `F5` — there was nothing to fix — but because
  `link` is unsupported by Avalonia and upstream reports crashes for screen-reader users.
- **`Audit-Accessibility.ps1` settles by default.** `-NoSettle` is the opt-out and nothing else
  should use it.
- **`main` is merged into this branch on request only**, and was on 2026-09-14. Split work still
  does not flow the other way: no merge, no PR, no fast-forward into `main` until the split is
  done and the maintainer approves.
- **OpenCodeForge gets its own `CHANGELOG-OpenCodeForge.md`**, separate from ClaudeForge's,
  **created when it cuts its first release** — not before, because an empty changelog for a parked
  app is a document that lags reality.
- **Friend grants live in ONE linked file**, `AssemblyInfo.InternalsVisibleTo.cs` beside the
  `.slnx`, linked by all 29 projects as `../../AssemblyInfo.InternalsVisibleTo.cs`. ⛔ **`internal`
  now means SOLUTION-internal** — the file compiles into every assembly, so a grant applies to all
  of them. If something must not cross an assembly boundary, `internal` no longer says so; make it
  private. Guarded by `SharedFriendGrantsTests`; maintainer's stated preference, `c7643ab`.

### What landed on 2026-09-16 — the attribution trailers left this branch

⚠ **History was rewritten and the branch force-pushed.** Every commit on
`feat/agentforge-opencodeforge` after `v2026.3.901` has a new SHA. Anything that forked from the
old line — the five `claude/*` branches share ancestry — will read as diverged; their own commits
are untouched.

⛔ **`main` was NOT touched, and neither was any release tag.** The scoping question turned on a
fact worth keeping: of the **123** commits carrying a `Co-Authored-By: Claude` trailer, **all 12 on
`main` sit inside `v2026.3.901`**, a published release. Zero trailers on `main` are outside a tag.
So "clean the ones outside release tags" reduced to *this branch only* — no force-push to `main`,
no disturbance to dependabot **#58**/**#59**, and `v2026.3.810` and `v2026.3.901` keep the SHAs
their shipped binaries and the winget manifest were built from.

Done with `git filter-repo --partial --refs v2026.3.901..feat/agentforge-opencodeforge`, the
message callback in a file rather than a shell argument. Verified rather than assumed, on a scratch
clone first and then again on the real repo:

| | |
|---|---|
| Trailers in the rewritten range | 110 → **0** |
| Trailers still on the branch | 12 — **all** ancestors of the protected tag |
| `v2026.3.901` | `3c7aaab…` **unchanged** |
| Commit count | 295 → **295** |
| Tree at tip | `61139db…` → `61139db…` — **content byte-identical** |

ⓘ One trailer remains on a `claude/*` branch only; those branches were left alone.
ⓘ Recovery: `backup/pre-trailer-rewrite-20260916` points at the pre-rewrite tip (`8232a38`), and a
52-ref snapshot sits beside it in the session scratchpad.

### What landed on 2026-09-15 — `F1`, `F2`, `F4`

⚠ **Uncommitted, in the working tree, awaiting approval.** No hash to quote yet.
Suite **4,351 · 0 · 11**; trim gate green for both apps on linux-x64, zero IL diagnostics.

#### `F2` + `F4` — the caution palette splits by ROLE

**Every contrast figure in `RETEST-FINDINGS` was recomputed; all sixteen reproduce exactly.**

⛔ **`F4` was one instance of six.** The mechanism is `AppCautionBrush` — an accent chosen for the
**3.0:1** non-text floor — used as a **text foreground**, where the floor is **4.5:1**. Five sites
beyond the banner measured 3.07–3.19:1. ⭐ And the repo already contained the correct pattern:
`EssentialsView` draws its caution panel as tint + border + *ordinary* body text, while
`AboutEditorView` and `MemoryEditorView` built the same panel and coloured the header with the
border token.

| | |
|---|---|
| `F2` | `#EA580C` for the accent role — a **hue** shift, not a lightening. 3.56:1, up from 3.19:1 |
| `F4` ×5 | new `AppCautionTextBrush`: light `#9A3412` (7.31:1), dark unchanged at 7.76:1 |
| `F4` banner | `AppSeverityToTintBrushConverter` — the severity's own colour at 10%, body text **inherited** |

⭐ **The banner tint adds no colour to the palette.** A `AppSeverity*BackgroundBrush` family would
have been eight new tints across two variants and two apps, against this palette's own rule that
no unreviewed colour enters it. Deriving the tint from the existing severity brush is correct for
all four severities and both themes for free.

⛔ **The alpha ceiling is set by the border, not by taste** — the banner's border is the same
colour as its tint, so Caution's border falls to **2.96:1 against its own tint at 15%**.

⭐ **Both new guards COMPUTE contrast from the hexes in `App.axaml` rather than quoting it**, so a
future palette change is checked rather than merely recorded. Canaried four ways, each red exactly
as predicted — restoring `#D97706` reddens with *this finding's own numbers*, 3.19 and 3.07.

#### `F6` — the nav tree's 26 unnamed chevrons

Named from the theme, in the file that already exists for parts no view can reach:
`TreeViewItem /template/ ToggleButton#PART_ExpandCollapseChevron` →
`WrapperStrings.LabelExpandCollapse` → `AutoNameExpandCollapse` in all nine resx files.

⛔ **The finding's own preferred fix was refuted by measuring it.** `RETEST-FINDINGS` argued that
hiding the chevron was *"arguably more correct"* because the `TreeViewItem` already exposes an
ExpandCollapse pattern. It does not: `TreeViewItemAutomationPeer` carries `IScrollProvider` and
`ISelectionItemProvider` only, while the chevron's `ToggleButtonAutomationPeer` carries
`IToggleProvider`. The chevron is the **only** programmatic expand/collapse affordance in the
tree, so hiding it would have removed the affordance rather than a duplicate.

⚠ **The element is a `ToggleButton`; the audit's "Button" was the PEER's control type.** A
selector written from the UIA walk matches nothing and **builds with zero errors and zero
warnings** — canaried exactly that way, and the guard caught it announcing `''`.

⭐ `TreeChevronAutomationNameTests` asserts the *rationale* as well as the behaviour: if a later
Avalonia gives the item an ExpandCollapse provider, the premise test reddens and the name-vs-hide
choice is remade on the new facts.

ⓘ Translations for the eight locales are mine, not a translator's — short, standard UI wording
("Expandir o contraer", "展开或折叠"), worth a glance if you have a native speaker to hand.

#### Dead brush tokens — asked for after `F2`

⛔ **Two tokens were declared and referenced by nothing**, both now deleted:
`InstallBannerCodeBorderBrush` (its palette is theme-independent and nothing was missing a border)
and `SuggestionGroupHeaderBrush`, whose comment explained that Semi does not guarantee a Fluent key
*"so we own this token explicitly"* — a live-sounding constraint on a picker that had since moved
to inherited foregrounds.

`NoDeadBrushTokensTests` now holds the line. ⚠ Two exclusions, both measured rather than guessed:
the generated compat shims (**260 of 307** declared brush keys, whose consumers are templates
inside theme packages this scan cannot see), and **four runtime-built key families** —
`AppSeverity*`, `AppChangeKind*`, `common-action-brush-*`, `scope-brush-*`. ⭐ Each family
exemption must still be **earned**: the test checks that the source it names still constructs a key
of that shape, so deleting a converter makes its exemption lapse rather than hide real dead tokens
forever.

ⓘ **A test, not an MSBuild task, and not `theme-audit`.** `CheckUnusedResxKeys` is the closer local
precedent and had already solved both traps independently rediscovered here — comment stripping and
dynamically-built keys — but it is **per-project**, and brush tokens are declared in one app and
referenced from the other and from the shared library. `theme-audit` owns the mirror question
(referenced here, defined by no theme) and lives in another repository, runs on theme pin bumps,
and calls its own report a snapshot; see [`docs/THEME-AUDIT.md`](docs/THEME-AUDIT.md) for the
split and for what would belong upstream.

#### `F1` — severity glyph size

Severity glyph size becomes a function of severity: `AppSeverityToFontSizeConverter` replaces the
**nine** hardcoded literals across **seven** files in both apps, at **Critical ×1.55, Caution
×1.15, circles ×1.00** against each site's existing tier base (14 for a settings row, 11 for a nav
badge, search hit, effective-value cell or save-dialog line).

⛔ **The report understated the defect, and only measuring showed it.** Ink height at 14pt runs
`⚠` 10.70, `○` 10.14, `⊗` 8.52, `●` 6.02 — so Critical drew smaller than Caution *and* smaller
than the hollow "nothing to do" circle. ×1.255 buys Critical bare parity with Caution, which is why
the shipped scale is as large as it is; there is no cheap version of this fix.

⭐ **The guard asserts ink, not points**, because `SizeFor(Critical) > SizeFor(Caution)` goes green
at ×1.01 with the defect still on screen. Canaried three ways, each red exactly as predicted.

ⓘ Two stale claims in `AppSeverityToGlyphConverter`'s remarks were corrected in passing: it still
named the superseded `▲ ◆ ● ○` glyphs, and asserted a visual-weight escalation that the
measurement disproves.

### What landed on 2026-09-14, after the retest

| | |
|---|---|
| `1f4c87d` | Merge `origin/main` — 25 commits of drift, 19 conflicts. ⚠ Two duplicate-attribute defects came from **clean** auto-merges, not conflicts, and only the build caught them |
| `dcf6bd5` | `F5` withdrawn; `Audit-Accessibility.ps1` settles before it walks |
| `b6a0b78` | Both apps → `TrimMode=partial` + `TrimmableAssembly`, `TrimModeIntegrityTests` |
| `94c864c` | This file — blocker removed, six decisions recorded |
| `c7643ab` | Friend grants consolidated into one linked file, `SharedFriendGrantsTests` |

ⓘ **`E1` needs no automated work.** `tests/AgentForge.Core.Tests/FileIO/ConfigFileLoaderPreservationTests.cs`
already drives the real `ConfigFileLoader` path — hand-formatted commented file in, one value
changed, comments / blank lines / tabs / key order asserted intact, plus a legacy-writer contrast.
The layer above is fail-safe too: `SelectedConfigWriter()` returns `null` unless `--writer legacy`
is passed, and `null` means the comment-preserving writer — the **opposite** of the `SchemaRegistry`
trap, where bare meant offline. What remains for `E1` is a human driving the UI.

---

**Two things drive everything: cut a ClaudeForge release from the split, and get the shared
libraries out as private NuGet packages first.** The second has an approved plan:
[`plans/00001-shared-libraries-as-private-nuget-packages.md`](plans/00001-shared-libraries-as-private-nuget-packages.md).

⭐ **The hard part of the release is already true: the ClaudeForge artifact has never contained
OpenCode.** Its Release build output is 14 assemblies — 6 `AgentForge.*`, 5 `LayeredEditors.*`,
3 `ClaudeForge*` — and **zero** OpenCode ones; `release.yml` passes `-App ClaudeForge` explicitly
on all three publish jobs and its tag pattern excludes `opencodeforge-v*`. Nothing needs carving out.

### ▶ The retest — first pass DONE, and one of its findings has since been withdrawn

The maintainer drove ClaudeForge against `ba794c2` on 2026-09-14. **All seven fixes from the
earlier batch are verified**, plus four never-tested items. ⭐ **No defect is open any more.**
`F5` is refuted; `F1`, `F2`, `F3`, `F4` and `F6` are fixed and awaiting a look at the UI. `F3`
closed last, on 2026-09-16 — so **nothing on the retest list is a code task**, and every remaining
item needs a human driving the app.

Two live documents carry it, and they are the ones to read — not this summary:

| | |
|---|---|
| What is still to do | [`docs/MANUAL-RETEST-PLAN.md`](docs/MANUAL-RETEST-PLAN.md) — **outstanding items only**; the eleven verified ones sit in one table at its end |
| What came back | [`docs/RETEST-FINDINGS.md`](docs/RETEST-FINDINGS.md) — `F1`–`F6`, each with measurements and the shape of a fix |

⭐ **The plan's PURPOSE changed mid-pass, and that is the durable point.** It began as "verify the
fix batch". It is now **regressing ClaudeForge for the split-library release**, which asks a
different question — and `docs/EXTRACTION-VERIFICATION.md` §4 already names the gap:

> No save, edit, backup or restore was exercised in either build. The live comparison is
> read-and-render only. A regression in the write path would not appear in it.

Reading and rendering are covered. **Everything that writes is not.** Section E of the plan covers
it, one item per shared library, `JsonC` first because it replaced a serialize-and-overwrite writer
and its failure mode destroys user comments silently.

⏸ **OpenCodeForge is PARKED** — a deliberate call, not an oversight. ClaudeForge's release comes
first. Its sandbox keeps: `artifacts/retest-opencode-config` holds a copy of the real
`opencode.json` plus a `tui.json` seeded with two keybind conflicts, reached via
`OPENCODE_CONFIG_DIR`.

### ✅ `F5` is REFUTED — and how it happened is the part worth keeping

**The published app's accessibility tree was never missing.** `F5` recorded 1 UIA descendant on the
shipped build and became the top release blocker. Re-measured on the same configuration: **168**,
under both trim modes. The build was proven identical, not merely similar — its linked
`Avalonia.Win32.Automation.dll` is **92,672 bytes**, the exact figure `F5`'s own evidence table
quotes — so the divergence was in the instrument, not the artifact.

**The cause of the bad reading:** the tree builds incrementally. Cold launch, polling every 120 ms:
72 descendants at 5.3 s, a `COMException` mid-construction at 7.1 s, 168 from 8.0 s. A single early
sample under-reports; early enough, only the OS `TitleBar` exists — which counts as 1.

⚠ **This also explains the control that made `F5` look airtight.** `PublishTrimmed=false` → 227 vs
`true` → 1 looks like one variable, but a trimmed single-file **compressed** build self-extracts on
first run and is ready seconds later than an untrimmed multi-file one. One fixed delay, two
different readiness times.

⛔ **And the proposed cure could not have worked, which was checkable without running anything.**
`Microsoft.NET.ILLink.targets:45` defaults `BuiltInComInteropSupport` to false under
`Condition="'$(PublishTrimmed)' == 'true'"` — **`TrimMode` is not in that condition.**

⭐ **The lesson: a measurement tool whose default can silently under-report will eventually
manufacture a defect.** Everything downstream of the bad number was competent — control experiment,
upstream research, two candidate fixes rejected with evidence, a full write-up. None of it could
rescue a first number taken before the thing being measured existed. `Audit-Accessibility.ps1` now
settles before it walks, and treats a count of 1 as "not ready" rather than as an answer.

ⓘ **What is NOT refuted:** `TrimMode=link` really is unsupported by Avalonia, with upstream reports
of access violations for users running Magnifier or a screen reader. Both apps moved to `partial`
on that basis alone. Measured cost: **~76 KB** on win-x64, against the "size will grow" this
document once assumed.

ⓘ **`artifacts/a11y-untrimmed/` and `artifacts/a11y-trimmed-loose/`** were built for the `F5`
investigation. They are no longer needed to walk a tree — the **shipping** artifact has one.

⚠ **Close any other ClaudeForge before testing.** Two instances on one `~/.claude` each see the
other's writes as external changes — the exact signal the watcher test measures.

ⓘ **Two live surfaces worth knowing:** **Shift+F12** opens *Live Config-File Events*, and
`logs/events-*.txt` beside the executable persists the same stream with scope.

✅ **`src/publish/publish.ps1`'s missing `maui-windows` workload preflight — looked at, and it is
CORRECT.** Those sixty lines preflighted a workload for MAUI Essentials, which `23f7c4f` deleted
along with the Windows TFM it lived behind — a TFM that never built, so no shipped ClaudeForge ever
contained the MAUI share path. Verified now rather than re-asserted: no `Maui` reference survives in
any csproj, props or targets file, and the only three `TargetFrameworks` mentions under `src/` are
comments explaining what was removed. ⛔ **Do not restore it.** A preflight that installs a workload
elevated, and aborts the release when that install fails, is a live hazard on the release path with
nothing left to preflight for.

### After the retest — the release path

- ✅ **`CHANGELOG.md` has an `[Unreleased]` section**, written from the 108 `feat`/`fix` commits
  since `v2026.3.901` and filtered to what a ClaudeForge **user** sees. The stale `- TBD` is
  corrected to `- [2026.2.612]`, the file's own `[X] - [Y]` shape meaning "current until Y" —
  read off the tag dates rather than guessed. ⓘ The gap was far bigger than this cell said:
  **eight releases** shipped between `2026.2.612` and `2026.3.901` with no entries at all. Those
  are not back-filled — the file now says so in a blockquote and points at the auto-generated
  release notes, rather than leaving a three-month hole that reads like a quiet period.
  ⭐ **Leading the Changed section is a behaviour change users must read**: the `**/` permission
  fix makes matching stricter in BOTH directions, so a `deny` rule that relied on the over-match
  now covers less.
- 🔭 **Open, and structural: there are two apps and one changelog, whose first line says
  "changes to ClaudeForge".** OpenCodeForge has a release workflow and no changelog. Everything
  that app has ever done is unreleased, so nothing is lost yet — but its first release needs
  either a second file or a per-app section here, and that is a decision, not a chore.
- ✅ **The guard for Claude-shaped defaults exists** — `NeutralLayerDefaultsTests`, closing
  [`docs/EXTRACTION-VERIFICATION.md`](docs/EXTRACTION-VERIFICATION.md) §5 item 1. It found a third
  site on its first run; see the sixth batch below. What is left of this list: roadmap phase
  markers 1–9. ✅ *`TRIMMING.md` never mentioning the second app* was already false
  when this line was written — that file's 12-publish note dates from 2026-09-14. Its real gap,
  the trim analyser property, is now written up.

### Plan 00001 — where it stands

✅ **All seven items are done.** `23f7c4f` (1), then 2–6 across the batches below, and item 7 —
documentation — closed with `CLAUDE.md`'s **The package layer** section and `TRIMMING.md`'s
**`EnableTrimAnalyzer`** section. `AGENTS.md`'s enforceable rows and
`.github/WORKFLOWS.md`'s versioning section landed with the items they describe.

ⓘ **Item 7's own description was stale in one respect**, found by checking rather than by
following it: it said `TRIMMING.md` "never mentions the second app". It has since 2026-09-14 —
the 12-publish note at the top of that file. What was genuinely missing was the other half, the
analyser property, which is now written up with the distinction that makes it matter: the Roslyn
analyser runs on every build of every `src/` project, while ILLink's whole-program pass — the one
that decides what is actually removed — still runs only on a publish.

⛔ **Nothing has been published to the feed yet.** The pipeline exists and its preflight has been
run against the live feed — which confirmed none of the eleven ids holds `2026.3.914` — but no
`packages-v*` tag has been pushed. The first one is a decision, not a step: every version on that
feed is permanent.

[PR #1](https://github.com/JanusMael/Bennewitz.Ninja.AutoVersioning/pull/1) is **MERGED** and
**`2026.3.914` is published to nuget.org**. The root `Directory.Build.targets` assigns
`<PackageVersion>$(AutoPackageVersion)</PackageVersion>`, and all eleven packages now pack at
`2026.3.914` with every inter-package dependency pinned to it — measured by reading the nuspecs
out of the `.nupkg` files, not by asking MSBuild.

⚠ **The assignment is in the root targets file, and moving it is the silent mistake.**
`src/Directory.Build.props` — where the rest of the package identity lives, which is why it is
tempting — is imported BEFORE the NuGet-generated props that define `AutoPackageVersion`, so the
value would evaluate empty, the SDK's default would already have run, and eleven packages would
pack at `1.0.0` again with nothing reporting anything. `PackageVersionLockstepTests` asserts both
halves, and its stamp comparison reads the **built DLLs** rather than the property that produced
them. Canaried red on a wrong source property and on a mixed-version feed before being trusted.

ⓘ A `-p:PackageVersion=…` on the command line is a **global** property and still outranks the
assignment, so `package-canary.ps1` packs at its throwaway prerelease exactly as before.

✅ **The two things item 5 deliberately left for item 6 are both closed.**

- **`src/publish/publish.ps1` now wipes `artifacts/localfeed`** alongside `dist/` and every
  `bin/`+`obj/` under `src/`. It is the one build input that sat outside `src/` and so survived
  every existing wipe, and in package mode a local package at the release version is preferred
  over the published one — silently, a folder source being exactly as valid to NuGet as a remote
  one. A release cut on a developer's machine could have shipped whatever the working tree held
  when the canary last ran.
- **The feed pre-flight is item 6's gate 3**, and it does more than the plan asked — see the
  fifth batch below for why "404 means absent" was not good enough.

⚠ **Two questions for the maintainer, both surfaced by item 2 and neither blocking:**

1. **`LICENSE` and `CopyrightHolder` name different holders** — the file says *"Copyright (c)
   2024-2026 ClaudeForge Contributors"*, the root `Directory.Build.props` says *Brian Bennewitz*.
   The packages therefore carry `<Authors>Brian Bennewitz</Authors>` and **no `<Copyright>` at
   all**, because inventing one that contradicts `LICENSE` is worse than omitting it. One of the
   two is the answer; a package page showing neither is the current state.
2. **`<Description>` is now read by a second audience.** This repo writes it as internal design
   rationale — `AgentForge.Core`'s says *"Invariant: AgentForge.\* must never reference
   ClaudeForge.\*"* — and that text is now the nuspec description on the feed page. Fine while the
   feed is private; it is a decision to revisit before anything goes public.

### 🔭 Open strategic question — do not start without the maintainer

Whether the shared libraries eventually leave this repository altogether. Plan 00001 is a staging
post toward that and says so; nothing has been decided, and the plan's "not in scope" section is
the current answer.

### ⭐ Capability worth knowing about before anything else

**A page in these apps can be looked at.** `scripts/capture-page.ps1` launches a *published* app,
deep-links to one node, photographs the window and shuts it down:

```powershell
pwsh -NoProfile -File scripts/capture-page.ps1 `
    -ExePath src/ClaudeForge/bin/Release/net10.0/win-x64/publish/ClaudeForge.exe `
    -NodeId footprint -OutFile footprint.png
```

⛔ **Use it on every new page before calling one done.** The headless test app is stripped of the
App's resource dictionaries and cannot instantiate views, so a page can pass every test, publish
trimmed with zero warnings, and still be blank — the exact state the Backup page sat in for a phase.
⚠ Windows only, and the capture is a *screen* grab: an overlapping window lands in the PNG and reads
exactly like clipped layout, so check the window rectangle the script prints. `PrintWindow` is not
the fix and was already tried — see the script's own comment. ⚠ It navigates and photographs; it
**presses nothing**, which is why items 3 and 5 above are still a human's job.

⚠ **Python is installed but NOT on PATH** — use the `py` launcher (3.14.7).

⛔ **Phase 16's quantitative half still gates the footprint page's RATES, not the page.**
`usage.isUsedInstall` in `docs/opencode-install-probe.json` still reads `false`, so growth,
retention and prune rates remain unmeasurable. Current sizes are live and correct.

---

## Done — 2026-09-14, seventh batch — the retest ran, and automation found what people could not

`cdde83c`. The maintainer drove ClaudeForge against `ba794c2`. **Eleven items verified, six defects
open**, and the pass changed its own purpose: from checking a batch of fixes to regressing
ClaudeForge for the split-library release.

| | |
|---|---|
| `docs/MANUAL-RETEST-PLAN.md` | Outstanding items only — section E is the write path, one item per shared library |
| `docs/RETEST-FINDINGS.md` | `F1`–`F6` with measurements |
| `scripts/Audit-Accessibility.ps1` | Walks a running app's UIA tree — the API a screen reader uses |

⭐ **The audit exists because no test here can see a rendered control's accessible name.** The AXAML
guards check that the markup sets one; the headless test app cannot instantiate views. Markup
correctness and *exposed* correctness are different questions, and only the second one matters to a
user. It found `F5` and `F6` on its first run.

> ⓘ **Corrected 2026-09-14.** `F6` stands. **`F5` does not** — it was an artifact of sampling the
> automation tree before it had finished building, and the audit script now settles before it
> walks. See the `F5` section above. The instrument was right about the gap it was built to close
> and wrong about the first thing it reported through it.

⚠ **Two findings pull against each other and must be fixed together:** `F2` wants the light Caution
token lighter (it reads brown); `F4` needs it darker (its *text* use measures 3.19:1 against a
4.5:1 floor). No single value satisfies both — the resolution is to split the token by **role**,
since glyph and border sit under a 3.0:1 floor and body text does not.

ⓘ **The audit's own first run produced a false positive, fixed rather than tolerated:** it reported
the status bar's `v2026.3.914.1513` as a leaked type name, four dot-separated alphanumeric segments
being exactly what the rule matched.

---

## Done — 2026-09-14, sixth batch — a guard for Claude-shaped defaults, and what it found

`NeutralLayerDefaultsTests` closes `docs/EXTRACTION-VERIFICATION.md` §5 item 1 — the one class of
incompleteness that had already produced a real defect and that nothing watched for.
`AssemblyLayeringTests` cannot see it: all three of its methods are about assembly **references**,
and Claude-shaped code inside a neutral assembly declares no reference at all.

⭐ **The rule is about DEFAULTS, not names.** Claude-named symbols are legitimate throughout the
neutral layer — `SchemaRegistry`'s Claude product descriptor names Claude's paths because it
*describes* Claude. Forbidden is a caller reaching Claude data without having said so: a `??`
fallback, or a constructor chaining to one. There were 543 `Claude` references across the neutral
projects, so a blanket ban was never the shape of this.

**Two audited sites fixed**, both behaviour-identical — the Claude-ness moves from the neutral
layer to the app that means it:

| Site | Was | Now |
|---|---|---|
| `SchemaSnapshotService` | parameterless ctor → `{ClaudeHome}/cache` | ctor takes the directory; `MainWindowViewModel` names it |
| `RestoreSidecarCleanup.Run` | `claudeHome ?? PlatformPaths.ClaudeHome` | required parameter; `Program.cs` passes it |

ⓘ **`Program.cs` improved incidentally**: its console message named `PlatformPaths.ClaudeHome` while
the cleanup resolved its own home independently. Two lookups that happened to agree; now one value,
reported and walked.

⛔ **I said `SchemaSnapshotService`'s parameterless ctor had no callers, and that was wrong.**
`MainWindowViewModel` constructed it with a target-typed `new()` — no type name on the line, so no
search for the type found it. The compiler did. Worth holding: **a `new()` makes a call site
invisible to any grep for the type.**

### ⛔ What the guard found on its first run, that the audit believed was fixed

`AgentForge.Sdk/Memory/FootprintService.cs:89` — `_paths ?? ClaudeArtifactPaths.Default`.

The audit records the footprint defect as *"Fixed this session in `765648a`"*. That fix was at the
**call sites**: both OpenCode clients now pass their own paths and catalog. **The default itself
was never removed.** `AgentConfigClientCore` still reads
`_footprintService ??= new FootprintService()`, so a third client that forgets to override gets
Claude's footprint exactly as the first two did — and *that* line carries no `Claude` token at all,
so no scan will ever show it.

⚠ **Allowed rather than fixed, deliberately.** `FootprintService` is `ClaudeArtifactPaths`-typed
throughout, so making it neutral is a real refactor of the disk-footprint feature — which is in the
pending manual retest. It is the single entry in the guard's `KnownSites` ratchet, which **only
shrinks**: a stale entry fails the test, so an exemption cannot outlive its fix.

▶ **This is the next piece of neutral-layer work**, and it should follow the retest rather than
precede it.

ⓘ Canaried three ways: the exemption removed (the real `FootprintService` line trips it), a stale
exemption added (the ratchet's self-check fires), and the parameterless constructor restored (the
reflection half fires).

---

## Done — 2026-09-14, fifth batch — the package publish pipeline (plan 00001 item 6)

A `packages-v2026.3.914` tag now packs the eleven and pushes them to GitHub Packages.

| | |
|---|---|
| Tag | `packages-v*.*.*` — **neither app's**. The eleven serve both, so riding ClaudeForge's tag would leave OpenCodeForge unable to publish shared code and tie a library fix to a full app release. Also `workflow_dispatch`, taking a **tag** rather than a bare version, so a manual run passes the same validation |
| Workflow | [`.github/workflows/release-packages.yml`](.github/workflows/release-packages.yml) — suite gate, then resolve, then publish |
| Script | [`scripts/Publish-Packages.ps1`](scripts/Publish-Packages.ps1) — three gates, all before the first upload |
| No GitHub Release | The artifacts are packages on a feed, not downloads. This repo's Releases page is how users of two apps find binaries; an entry with nothing to download for either belongs elsewhere. Easily reversed if you disagree |

⛔ **The first version of gate 3 passed with the literal token `definitely-not-a-valid-token`, and
finding that is the whole value of this batch.** Measured against the live feed:

| Request | Result |
|---|---|
| bad token → `/<id>/index.json` | **404** — identical to an unpublished id |
| no auth → `/<id>/index.json` | 401 |
| bad token → `/index.json` (service index) | **200** — proves nothing |

So "404 means absent" hands a clean preflight to anyone whose token is missing, expired or
garbled. The credentials are now proved first against `api.github.com/rate_limit` — 401 for a dead
token, 200 for any live one, PAT or `GITHUB_TOKEN` — and only then is a 404 read as absent.

⚠ **One residual gap, covered by push ORDER rather than by the check.** A token with
`write:packages` but not `read:packages` is live, passes the probe, and still 404s on every read.
So the eleven go one at a time in a stable order and the run stops on the first failure: a re-run
over a partly-published set collides at package 1 with the other ten untouched. `--skip-duplicate`
is deliberately not passed.

ⓘ **Gate 2 is the one that ties this batch to the last.** Each package's contained assembly must
carry the package's own three-part stamp, so a release whose `BuildTimestamp` was not pinned from
the tag is refused rather than published as a permanent, silent lie. Canaried: packing
`2026.3.901` against today's assemblies is rejected.

ⓘ **`ReleaseTagScheme` cannot mistake a package tag for an app release** — verified, not assumed.
ClaudeForge uses `Unprefixed`, which strips at most a leading `v` then requires `Version.TryParse`;
`packages-v2026.3.914` fails that.

✅ **Closed in the same batch: `src/publish/publish.ps1` now wipes `artifacts/localfeed`.** It was
the one build input outside `src/`, so every existing wipe missed it, and in package mode a local
package at the release version is preferred over the published one. ⚠ **A side effect worth
knowing:** running `publish.ps1` locally now empties the feed, so the third
`PackageVersionLockstepTests` goes inconclusive until the canary re-packs it — the suite then
reads **4,302 · 12** rather than 4,303 · 11. That is the guard declining to claim a measurement it
did not take, not a regression.

ⓘ Also corrected here: `nuget.config` credited the local feed to `scripts/pack-local-feed.ps1`,
which does not exist — the same phantom script the `ValidateSharedPackageVersion` error named.

---

## Done — 2026-09-14, fourth batch — a release pins its version from its own tag

⛔ **Every release this repo has ever cut stamped the date CI ran, not the tag.** `release.yml` and
`release-opencodeforge.yml` both set `PublicVersion` from the tag and both described a version flow
that was not happening. Measured three ways on one packable project:

| What a release pins | `PackageVersion` | Assembly stamp | |
|---|---|---|---|
| `PublicVersion=2026.3.901` — what both workflows did | `2026.3.914` | `2026.3.914.1346` | sets no version |
| `AutoPackageVersion=2026.3.901` | `2026.3.901` | `2026.3.914.1347` | **skew** |
| `BuildTimestamp=20260901120000` | `2026.3.901` | `2026.3.901.1200` | ✅ both follow |

The reason is in AutoVersioning's own props: `BuildTimestamp` is a `CompilerVisibleProperty`, so
the generator sees it. `AutoVersion` and `AutoPackageVersion` are not in that list — MSBuild-side
only, so pinning them moves the package and leaves the assembly behind.

⚠ **`PublicVersion` is not inert, and the first read of this was wrong.** The generator does write
it — as `[AssemblyMetadata("PublicVersion", …)]`, never as the version. So the tag *was* in every
binary, one attribute away from the numbers that disagreed with it, which is exactly why nothing
ever looked wrong. ⭐ It is kept for one reason: `AssemblyVersion` and `FileVersion` are numeric, so
`v2026.3.914-rc.1` and `v2026.3.914` both stamp `2026.3.914.0` and `InformationalVersion` is a
fixed string — without that attribute an rc binary and its final release are indistinguishable from
the file alone.

| | |
|---|---|
| The derivation | [`scripts/Resolve-ReleaseVersion.ps1`](scripts/Resolve-ReleaseVersion.ps1) — strips the app's tag prefix, rejects an unreal date or a quarter that disagrees with its month, exports `BuildTimestamp` + `ReleaseVersion` + `PublicVersion` |
| Wired into | Every publish job of both release workflows. ⚠ Per job, not per workflow: `$GITHUB_ENV` does not cross a job boundary |
| Packages | Need no change — `dotnet pack` already derives from `AutoPackageVersion`, which follows `BuildTimestamp`. Verified end to end with the env var rather than `-p:`, since that is how a workflow passes it: `2026.3.914` package, `2026.3.914.0` assembly |
| Guards | `ReleaseWorkflowTests.EveryPublishingJobResolvesItsVersionFromTheTag` and `.NoWorkflowSetsPublicVersion`, both canaried red |

⛔ **The first version of the counting guard was fooled by a comment** — the workflow header names
the resolver script, so counting raw occurrences let that comment stand in for a missing step. The
canary is what found it; reading the test would not have. It counts non-comment lines now.

ⓘ **A tag release stamps a fourth part of `0`** (`2026.3.914.0`), because that part is `HHmm` and
the timestamp is pinned to midnight. That is what makes the same tag reproducible, which a package
feed requires.

⚠ **`$(Version)` is still `1.0.0` and still reaches nothing.** Unchanged by this batch, and still
the thing to check before item 6 names an artifact after it.

---

## Done — 2026-09-14, third batch — the packages stop being 1.0.0

Plan 00001 work item 3. One property assignment, one guard class, and one error message that had
been pointing at a script that does not exist.

| | |
|---|---|
| The assignment | `<PackageVersion>$(AutoPackageVersion)</PackageVersion>` in the ROOT `Directory.Build.targets`, beside the reference switch and for the same reason: it is the file imported last |
| Measured | Eleven `.nupkg` at `2026.3.914`, every inter-package dependency pinned to it, against DLLs stamped `2026.3.914.1303`. Read out of the nuspecs and out of `FileVersionInfo`, not out of MSBuild |
| Guard | `PackageVersionLockstepTests` — three tests. Canaried red twice before being trusted: the assignment swapped to `$(Version)`, and a stray `JsonC 9.9.9` packed into the local feed |

⭐ **The packages' INTERNAL versioning was already AutoVersioning's, and now it is pinned.** Read
out of the DLLs inside the eleven `.nupkg`: `AssemblyVersion` *and* `FileVersion` are both
`2026.3.914.1303` — identity included, which is the one a consumer binds to. Only the NuGet half
was ever missing. ⚠ **This assertion could not be canaried red**, and the two failed attempts are
the finding: `-p:AssemblyVersion=3.3.3.0` is ignored outright because the generator writes the
attribute, and `GenerateAutoVersionedAssemblyInfo=false` fails the build with `BAUTOVERSIONING00`
rather than falling back to the SDK's assembly info. The skew is unreachable from MSBuild today, so
the guard's subject is a future change to that package. The comparison itself was canaried against
the third-party assemblies in the same output directory — `HarfBuzzSharp` carries identity `1.0.0`
against file version `8.3.1`, and it separates them correctly.

ⓘ **One loose end, not a defect:** `$(Version)` is still `1.0.0`. It no longer reaches the package
(the assignment overrides it) and never reached the assemblies (the generator wins), but anything
that reads it — a future zip name, a workflow, a publish step — would get `1.0.0`. Worth knowing
before item 6 names an artifact after it.

ⓘ **`InformationalVersion` on all eleven is the literal string `Built with ♥`**, which is
AutoVersioning's own choice. That is the attribute crash reports and `--version`-style diagnostics
usually read, so a consumer of these packages cannot recover a version from it. A question for that
package's repo, not this one.

⛔ **The error text on `ValidateSharedPackageVersion` was wrong in two ways at once**, and it is the
one message whose entire job is to rescue someone who has just hit a confusing NuGet failure. It
named `scripts/pack-local-feed.ps1`, which **does not exist** — the real one is
`scripts/package-canary.ps1 -PackOnly` — and its literal `$(date)` was expanded by *MSBuild* as a
property, so the example version it printed was truncated to `1.0.0-local-`. Both fixed.

⚠ **`artifacts/localfeed` currently holds eleven packages at `0.0.0-local-20260914094410`.** That
is the hazard item 6 has to close: a stale local-feed package outranks GitHub Packages in
`nuget.config`'s source mapping.

---

## Done — 2026-09-14, second batch — the manual retest and its fixes

The maintainer drove the app; findings came in one at a time and fixes were batched.

| Commit | What |
|---|---|
| `e9859b9` | Six retest defects: theme brushes, Windows share, the config watcher, severity glyphs and palette, the F12 keyboard trap, the About separator |
| `790ce63` | The event-log file the maintainer asked for, plus the AutoVersioning bump to `2026.3.914` |

### ⛔⛔ The worst one: themed brushes were snapshots

`BrushHelper.ResolveThemed` read `ActualThemeVariant` at Convert time. An `IValueConverter` re-runs
only when its binding SOURCE changes, and a severity does not change because the theme did — so
every element kept whichever palette was live when it was last materialised. Rows rebuilt by
navigation picked up the new one, rows that were not kept the old one, and **one screen showed
both**. Reported as *"brighter on reopen, dark after switching to light, sometimes light then later
dark inside the same theme"*.

⭐ The fix is far smaller than either option first considered: return ONE shared `SolidColorBrush`
per key and re-colour it on variant change. `Color` is change-notifying, so every element already
holding it re-renders — **zero markup change**, and it covers all three converters and any future
caller rather than the 17 binding sites that exist today.

⚠ Three process lessons, all recorded because each nearly shipped something wrong:

- **The first canary was INVALID.** Stashing the fix turned the new test red — on a
  `NullReferenceException` in its own reflection scaffolding, not on its assertion. Scaffolding
  must never be what fails. Made tolerant; the re-run fails on the real assertion.
- **The existing suite caught a flaw in the fix.** `AppSeverityThemedLookupTests` passed in
  isolation and failed in the full run: the first version cached the COLOUR as well as the
  instance. The cache is identity-only now.
- ⛔ **I claimed "nothing today would catch this" and was wrong** — two themed-lookup test classes
  already existed. They assert a FRESH Convert respects the variant, which the old code did
  correctly. None covered a brush already handed out and never re-converted.

### The other five

- **Share config was a silent no-op on Windows** — `IsWindows() && !string.IsNullOrEmpty(uri)`, and
  the caller passes no URI. macOS falls back to `pbcopy`, Linux to `mailto:`, Windows to nothing.
  ⛔ Not a regression from `23f7c4f`: the MAUI path lived behind a TFM that never compiled, so **no
  shipped build ever had it**. Now uses `clip.exe`. Failure reporting moved off `Debug.WriteLine`,
  which is compiled OUT of Release while its comment claimed it was there "so developers can
  diagnose".
- **Only 3 of 6 config files were watched** — `SetupFileWatcher` filtered on `f.Exists`, but
  `ConfigFileWatcher.Watch` needs only the DIRECTORY and already raises `Created`. A config created
  while the app ran was invisible. Surfaced by the new event log printing `armed for 3 of 6`.
- ⛔ **The F12 "tab trap" was misdiagnosed twice.** I claimed Avalonia's `ListBox` defaults
  `TabNavigation` to `Continue` and wrote a fix plus a global style on it. **Measured: the default
  is already `Once`.** Both were no-ops asserting a falsehood; both reverted. The real cause:
  `HeaderLink.Create` returned a `TextBlock`, which is not focusable — so the window's only tab
  stop was the log list and Tab had nowhere to go. The actions were **mouse-only** while their
  automation properties made a screen reader announce them correctly. *Announcing a control is not
  the same as exposing it.* They are `Button`s now.
- **Severity glyphs and palette** — see *Locked decisions*.
- **The About dialog's separator** stretched to the grid height, set by the taller column, and ran
  ~150px past the last button into empty space.

### The instrument that answered the watcher question

`AvaloniaDiagnosticsOptions.EnableEventLogFile` writes `logs/events-*.txt` beside `app-*.txt`, with
scope, file type, profile, read-only — and, crucially, **the files that are NOT watched**. The
events had only ever existed in the Shift+F12 window, which is live-only, and both `[FileWatcher]`
log lines are `Log.Debug` while the shipping level is Information — so the question was
unanswerable from either surface.

⛔ **The prefix must not contain a hyphen and I shipped a build that died proving it.** The file
name is `{prefix}-{yyyyMMdd}-{HH}.txt`; `BucketedRollingFileSink` throws on a hyphen, from
`ConfigureLogging`, which runs BEFORE logging exists. `"config-events"` exited with no window and
no log line anywhere, and I handed it over without checking it started.

---

## Done — 2026-09-14

Three commits, all namespace/identity work, done **before** the first publish because a package id
is immutable the moment it is pushed.

| Commit | What |
|---|---|
| `dc43e00` | **Six `RootNamespace` values said `Bennewitz.Ninja.Layer.*` while every `namespace` under them said `LayeredEditors.*`.** `RootNamespace` only drives new-file defaults and generated code, so the two disagreed silently and the code was already right. Corrected, zero code churn. One namespace WAS wrong in code — `Layer.Avalonia.Services.Converters`, 4 files |
| `2bf0dfc` | **`LayeredEditors.ViewModels` stopped claiming an Avalonia it does not have.** Assembly and package say `LayeredEditors.ViewModels`; every namespace inside said `LayeredEditors.Avalonia.ViewModels`. ⭐ The deciding fact: the project references `CommunityToolkit.Mvvm` and `LayeredEditors.Abstractions` and **nothing else** — no Avalonia at all. So the namespace moved, not the assembly. 110 files |
| **HEAD** | ⭐ **`AgentForge.Jsonc` → `JsonC`.** See below |

### ⭐ JsonC left the family, and that had teeth

`AgentForge.Jsonc` is a comment- and formatting-preserving JSONC reader with **zero dependencies**
and no agent knowledge of any kind. Under `PackageId = Bennewitz.Ninja.$(AssemblyName)` it would
have published as `Bennewitz.Ninja.AgentForge.Jsonc` — a name that overclaims, forever, because a
pushed id cannot be replaced. Renamed to `Bennewitz.Ninja.JsonC` (the maintainer's casing) along
with the directory, csproj, assembly, namespaces and its test project.

⛔ **It falls outside both family prefixes, and FOUR separate selectors had to learn about it —
they do not share a list, and the canary found the one the rename missed.** The reference switch in the root `Directory.Build.targets`;
`PackageMetadataTests`, which asserts the switch's selector and the packable set agree in both
directions; and **`AssemblyLayeringTests`, which keeps its own.**

⛔⛔ **That third one is the dangerous one.** Its shared-project scan globbed `AgentForge.*.csproj`.
`JsonC` would have dropped out of the layering scan entirely, and its own vacuity guard — which
exists precisely to catch "renaming the shared projects turns every assertion into a no-op pass" —
would **not** have fired, because the remaining `AgentForge.*` projects still satisfy "at least
one". Widened to a glob list and canaried: an injected `JsonC -> ClaudeForge.Sdk.Claude` reference
now reddens and names the file.

ⓘ **`LayeredEditors.*` was never in that scan either.** Not a decision, a gap — those projects are
equally product-neutral. Added in the same change; it found no violations.

⚠ **The type names are still `Jsonc*`** — `JsoncDocument`, `JsoncEditor`, `JsoncScanner`,
`JsoncEditWriter` in `AgentForge.Core`. Only the namespace, assembly and package moved, because
that is what was asked. `Bennewitz.Ninja.JsonC` containing `JsoncDocument` is visibly half-done;
renaming the ~10 public types is a separate, purely mechanical change and a decision for the
maintainer.

**Measured:** 11 packages still pack, zero warnings, `Bennewitz.Ninja.JsonC` among them; suite
4,296 passed / 0 failed / 11 skipped across all 12 assemblies.

---

## Done — 2026-09-13

Eight commits of substance, plus docs-only ones not listed. The retest batch, a strategic turn,
then the packaging plan's three unblocked items.

| Commit | What |
|---|---|
| `98b74d1` | ⛔ **OpenCodeForge built THREE `SchemaRegistry` instances** — its own inside `InitializeAsync`, plus a private one inside each client, because `AgentConfigClientCore` makes its own when handed `null`. Every schema fetched twice per launch; an offline launch paid the 3s timeout per registry. ⭐ The correctness half is the provenance badge: with three registries it could report `Fetched` for the pages while the registry validating saves had fallen back to bundled. Both clients now take an optional registry; a three-rung constructor chain builds one environment and one registry and hands each to both. `SharedSchemaRegistryTests` pins both halves, canaried by stashing the fix |
| `4ac7844` | **The Clients column's short names come from the host.** `AbbreviateClient` knew `"claudecode"` and `"claudedesktop"`, hardcoded in the neutral shell, so OpenCode's two fell through its unknown-product passthrough and the cell rendered `OpenCode+OpenCodeTui` in 110 px. `BackupPageText.ClientAbbreviations` is `required`, keyed by `ArchiveFolder` — which is what `BackupEngine.BuildClientList` writes into `manifest.clients`, so a map keyed by `Id` would compile and abbreviate nothing. Now `OpenCode+TUI`; ClaudeForge unchanged |
| `131a38c` | ⭐ **All fifteen progress-bar phrases come from resx**, translated into all nine locales. The seam is an id beside the English fallback: `BackupProgress.ItemId`, and `ProductArchiveSection.ProgressLabelId` **derived from `SubPath`** rather than declared, because the sub-path already is the section id. Each app has a guard taking ids from the *descriptors* that asserts both coverage and that each label says what the engine says — presence alone passes two keys swapped between sections. **TWINS:** `BackupEngine`'s `"Discovering projects…"`, the backup side's only phrase, fixed here too |
| `5f1a0aa` | ⭐ **[`plans/00001`](plans/00001-shared-libraries-as-private-nuget-packages.md)** — the approved packaging plan, committed before implementation per the plan workflow |
| `23f7c4f` | ⛔⛔ **A Windows TFM that was never built, in three projects.** See below |
| **HEAD** | ⭐ **Plan 00001 item 5 — the reference switch, the local feed and the canary.** `UseSharedPackages` rewrites `ProjectReference` → `PackageReference` centrally; `nuget.config` maps the private feed so a credential-free clone still restores; `scripts/package-canary.ps1` and a `package-canary` CI job prove the package mode. **It found a real difference on its first run.** See below |
| `2627403` | ⭐ **Plan 00001 item 4 — the libraries analyse themselves.** `EnableTrimAnalyzer` in `src/Directory.Build.props`. See below |
| `2f4a7cb` | ⭐ **Plan 00001 item 2 — package metadata.** One block in `src/Directory.Build.props` gives all eleven their identity, licence, repository and readme; every one of the seventeen projects under `src/` now states `<IsPackable>` for itself. Three guards in `PackageMetadataTests`, each canaried. See below |

### ⛔⛔ The Windows TFM, and why nothing ever failed

`src/ClaudeForge`, `OpenCodeForge` and `LayeredEditors.Avalonia.Services` each declared
`net10.0-windows10.0.19041.0` so `DefaultShareService` could compile against MAUI Essentials — each
with a comment asserting the plural `<TargetFrameworks>` took precedence over the root's singular
`<TargetFramework>`. **It does not.** MSBuild cross-targets only when `TargetFramework` is EMPTY,
and `Directory.Build.props` sets it.

So the TFM never built, in any configuration, since the day it was added. **No shipped ClaudeForge
has ever contained the MAUI share path**; the non-Windows fallbacks are what users have always got.
The build succeeded, the suite stayed green, and `bin/Release/` quietly held one TFM directory
instead of two.

⭐ **It surfaced only because `dotnet pack` reads `TargetFrameworks` for the nuspec while the build
honours the singular** — NU5026, for a file no build had ever written. Packaging found a defect a
year of green builds did not.

What went with it: the three declarations plus `UseMauiEssentials` and the conditional
`Microsoft.Maui.Essentials` reference; six `#if` blocks and the `hwndProvider` parameter in
`DefaultShareService`; the `#if` in `App.axaml.cs` whose `#else` was the only branch that compiled;
`ILLink.Suppressions.Windows.xml`; `EnableWindowsTargeting`; and **sixty lines of `maui-windows`
workload preflight in `publish.ps1`** that ran on every Windows release publish, could install a
workload elevated, and aborted the release if that install failed.

`SingleTargetFrameworkTests` guards recurrence — a project may multi-target only by clearing the
inherited singular first, and the test **fails rather than passes** if the root ever stops setting
it, since that is its entire premise. Canaried by restoring OpenCodeForge's declaration.

### Package metadata — where it lives, and the placeholders it caught

`src/Directory.Build.props` carries the whole block: id, authors, company, repository, project URL,
MIT licence expression, readme. Eleven packages, one statement.

⭐ **`<PackageId>` is `Bennewitz.Ninja.$(MSBuildProjectName)`, and `$(AssemblyName)` would have
failed SILENTLY.** `Directory.Build.props` is imported at the *top* of every csproj, before that
file assigns `AssemblyName` — so the property evaluates empty and all eleven packages collide on
the id `Bennewitz.Ninja.`. Not an error; a collision.

⛔ **A `Directory.Build.targets` under `src/` — imported after the csproj body, where
`$(AssemblyName)` is real — was considered and rejected**, which is why no such file exists.
MSBuild imports only the CLOSEST `Directory.Build.targets`,
so adding one under `src/` detaches every `src` project from the ROOT one: the publish strip, the
dead-resx guard and the raw-hex guard all stop running, and nothing fails. That is the same shape
as the Windows TFM above, and a worse trap than the drift the file-name form accepts — which is
closed by a guard instead.

⛔ **Two sets of placeholders were one release away from being immutable.**

- `LayeredEditors.Avalonia.Diagnostics` already carried `<PackageId>LayeredEditors.Avalonia.Diagnostics</PackageId>`
  — **without the `Bennewitz.Ninja.` prefix** — and `<Authors>LayeredEditors contributors</Authors>`,
  under a comment reading *"placeholders, filled in before first publish"*. Both are gone; its own
  `README.md` and its tags stay.
- `AgentForge.Core` and `AgentForge.Sdk` had no `<Description>`, and the SDK's default is the
  literal string **`Package Description`** — measured in the nuspec, not inferred. Both now
  describe themselves.

ⓘ **The readme is shared but not mandatory.** `src/PACKAGE-README.md` packs for the ten projects
that have no readme of their own; the one that does keeps it. GitHub Packages will not let a
published version be replaced, which is why each of these is worth a guard rather than a review.

**Measured, not asserted:** `dotnet pack ClaudeForge.slnx -c Release` yields exactly eleven
`.nupkg`, zero warnings, each holding `lib/net10.0/<assembly>.dll`, `README.md` and a nuspec whose
inter-package dependencies already use the prefixed ids. **All four canaries fired:** a project
stating no `IsPackable`, a project whose `AssemblyName` diverges from its file name, a packable
project with no description, and — for both premise assertions at once — deleting `<PackageId>`
from the shared block.

### The trim analyser moves into the libraries — and one sentence in `CLAUDE.md` stopped being true

`EnableTrimAnalyzer`, one line beside `IsTrimmable` in `src/Directory.Build.props`.

⛔ **Packaging would otherwise have deleted the trim analysis on shared code entirely, silently.**
The analyser reaches those libraries today only because `dotnet publish -p:PublishTrimmed=true`
sets a **global** property that flows through the app's build graph — and a packaged library is
not in that graph. The property would reach nothing, the analyser would stop running on shared
code, and nothing would fail. ILLink would still analyse the packaged IL, since `IsTrimmable`
travels in the `.nupkg`, but after the fact and attributed to an assembly the app cannot edit.

✅ **Canaried in BOTH configurations, and the second one is why a doc changed.** Removing the
`(JsonNode?)` cast in `AgentForge.Sdk/McpServers/McpServersAccessor.cs` now reddens a plain
`dotnet build` with `IL2026` — in **Debug** as well as Release, on the library project alone, with
no publish and no app anywhere. `CLAUDE.md` said *"trim analysis only runs on a Release publish"*;
that is now false and carries a dated correction.

⚠ **The warning it sat inside still stands, and the correction says so.** The two analyses are
different: Roslyn sees one project's own source, while **ILLink's whole-program pass — the one
that decides what is actually removed — still runs only on a publish.** A green Debug suite still
does not mean the apps ship.

**Cost: zero, measured rather than carried over.** A Release build of the whole solution reports
zero IL diagnostics; so does Debug. ⓘ The plan predicted "224, all under `tests/`" — that figure
came from setting the property globally. Scoped to `src/`, where it belongs, `tests/` is untouched
and the number is nought. ⛔ `IsAotCompatible` is deliberately not set alongside it: it implies the
AOT and single-file analysers too, which is a claim about these libraries nothing has measured.

### The reference switch — one conditional, and three things that fail silently

`UseSharedPackages` unset or `false` means `ProjectReference` and is what every human runs;
`true` means `PackageReference` at `SharedPackageVersion`. The rewrite is **one ItemGroup in the
ROOT `Directory.Build.targets`**, so no csproj declares both and the mixed graph the plan warns
about is unrepresentable. New files: `nuget.config`, `scripts/package-canary.ps1`, and a
`package-canary` job in `ci.yml`.

⭐ **The plan said "both apps and all twelve test projects switch together". That list is
incomplete, and the omission would have re-created the exact bug the sentence exists to prevent.**
The four product-specific libraries switch too: leave `ClaudeForge.Avalonia` on a
`ProjectReference` to `AgentForge.Core` and the app that references it has two
`AgentForge.Core.dll` in its graph again. The condition is `IsPackable != true`, which says it
without naming anyone.

⛔ **Two MSBuild facts, both measured in an isolated probe after the first attempt silently
switched nothing:**

1. **`%(Metadata)` in a condition on an item OUTSIDE a target evaluates to EMPTY and does not
   error.** The obvious spelling — `Include="@(ProjectReference)"` with a
   `Condition="…StartsWith('AgentForge.')"` — matches nothing, and the build succeeds having done
   nothing at all.
2. **Item `Remove` is a string comparison, not a path comparison.** It does not normalise, `**`
   does not traverse `..`, and an absolute pattern does not match a relatively-spelled item. This
   repo spells these references two ways (`..\AgentForge.Core\…` from `src/`,
   `..\..\src\AgentForge.Core\…` from `tests/`), so a glob tuned to one would silently miss the
   other.

   The implementation normalises to full paths first, then uses `Remove` twice as a **set
   difference** — the one thing it does reliably. Plain evaluation, no target hooks, so restore
   and build cannot disagree.

⛔ **A package and a project do not put the same files in the output directory, and the canary is
how that was found.** A `ProjectReference` copies the referenced project's XML documentation file
next to its DLL; a `PackageReference` leaves it inside the `.nupkg`, because
`CopyDocumentationFilesFromPackages` defaults to `false`.
`PublicSurfaceContractTests.IAgentConfigClient_DocumentsThreadingContract` reads
`AgentForge.Sdk.xml` from the test output, and it was **the one test of 4,295 that failed the
first package-mode run**. Fixed in package mode only — repo-wide the property would drag every
third-party package's XML into every `bin/`.

✅ **The plan's `AssemblyLayeringTests` concern does not apply to this implementation, and that was
checked rather than argued.** The plan expected the switch to edit csproj files, which would have
left that guard reading `ProjectReference` elements that no longer existed — a vacuous pass. The
central transform edits no csproj, so the guard's input is byte-identical in both modes.
Demonstrated by injecting an `AgentForge.Artifacts -> ClaudeForge.Sdk.Claude` reference and
watching it redden **in both modes**, naming the file. ⓘ The injected edge is circular, so restore
fails before the test can run — the canary was driven against the pre-built test assembly, which
is legitimate precisely because the guard reads disk rather than the build.

⚠ **`PackageMetadataTests` gained a fourth guard, and it is the switch's premise.** The rewrite
matches on the file-name prefixes `AgentForge.` / `LayeredEditors.` because MSBuild cannot read
the referenced project's `IsPackable` from there. So the name and the packability must agree, and
`TheSwitchesNamePrefixSelectsExactlyThePackableProjects` is what makes them — asserting **both**
directions, since a packable project outside the prefixes silently stays a project reference and
lets the canary validate project output while reporting success.

ⓘ **No credentials anywhere, and a credential-free clone still restores.** `nuget.config` lists the
private GitHub feed but maps it — and the local folder feed — to `Bennewitz.Ninja.AgentForge.*` and
`Bennewitz.Ninja.LayeredEditors.*` only. Without that mapping NuGet queries every source for every
package and the unauthenticated feed 401s on `Avalonia`. ⚠ The patterns name the two families
rather than the whole `Bennewitz.Ninja.` prefix on purpose: `Bennewitz.Ninja.AutoVersioning` is
**public and comes from nuget.org**, verified from its `.nupkg.metadata` rather than assumed.

### What the plan's adversarial pass caught before any of it was built

Two faults in the first draft, both of which would have shipped a canary that could not fail:

- ⛔ `ClaudeForge.Tests` references `src/AgentForge.Core` **and** `src/ClaudeForge`. Switching only
  the apps to `PackageReference` would leave two `AgentForge.Core.dll`s in the test graph with
  MSBuild preferring the project one — the suite exercising project code under a job named for
  packages. Apps and all twelve test projects now switch together.
- ⛔ NuGet extracts by id+version and will not re-extract, so a local feed would have served the
  previous build's package. The canary now uses a unique per-run version **and** an isolated
  `NUGET_PACKAGES`.

Two risks the pass **retired** with measurement rather than argument: `AgentForge.Avalonia.Shell`
carries no AXAML at all and `LayeredEditors.Avalonia` carries its four files as one
`!AvaloniaResources` resource inside the DLL (and the repo already consumes `Semi.Avalonia` this
way); and `InternalsVisibleTo` survives packaging, because the assemblies are unsigned and the
grants compile into attributes that travel in the `.nupkg`.

---

## Done — 2026-09-12, third batch

| Commit | What |
|---|---|
| `5d74cd2` | docs: the plan said Phase 14 was open **in four places**, two of which predated this session's work and one of which contradicted a phase that had already shipped |
| `cc141b9` | ⭐ **[`docs/EXTRACTION-VERIFICATION.md`](docs/EXTRACTION-VERIFICATION.md)** — is the split complete, did ClaudeForge regress. Answers up front, then evidence, then an explicit list of what was NOT verified |
| `b7c202f` | docs: corrected the word *"inert"*, and **measured the app/shell split for the first time** |
| `ea6d129` | ⭐ **Merge `main`** — 23 commits, 7 conflicts. Five additive (both sides kept); `AGENTS.md` and `SchemaRegistry.cs` took judgment |
| `d9128a3` | ⭐ **The schema disk cache, redesigned to the maintainer's spec** — see *Locked decisions* |

**Regression verdict: none found.** Baseline was **`v2026.3.901`** (`3c7aaab`), the latest tagged
public release, which predates every `AgentForge.*` assembly. Both builds were published Release
`win-x64` and driven to the same ten nav nodes; across twenty runs the **log event sequence is
identical** except the one flag the verification deliberately passed (the baseline predates
`--schema-source`). Severity counts match exactly: 0 fatal, 0 error, 10 warnings each — one per run
of a pre-existing schema notice. The eleven `NavId` constants are byte-identical.

⛔ **A pixel comparison was attempted and is INVALID — do not resurrect its numbers.** The captures
contain other windows on top of the app, so its 39–61% "differences" measure the desktop. The first
harness asserted the app was foreground at capture time and would have caught it; local **antivirus
blocked that script** as an infostealer signature (screenshotting plus window inspection in one
file), and the fallback had no such assertion. If the visual half is ever redone, the desktop must
be clear, or the foreground assertion must survive AV.

⭐ **The extraction is 48.7% of what powers ClaudeForge**, up from 11.6% at the tag — 42,217 of
86,647 lines — and it demonstrably executes (`[Editor.Rebuild]`, twelve lines per run in *both*
builds' logs, comes from `AgentForge.Avalonia.Shell`). ⚠ But the two halves are in very different
states: the Core/SDK split is thorough, while `src/ClaudeForge` went 41,371 → 38,301 lines (**net
−7%**) against a plan estimate implying ~24,000 extractable. It remains the largest assembly in the
repo. That number had never been measured before.

✅ **The permissions work is complete, as REDESIGNED** — the plan's third revision shrank Phase 6 to
"a ~50-line shared vocabulary plus two parallel implementations" and rejected `Decision<TRule>` on
measurement. `PermissionOutcome` is 55 lines (`a453063`); OpenCode's own implementation is ~1,790.
Nothing outstanding; the roadmap simply marks ✅ only on phases 10, 13 and 14.

---

## Done — 2026-09-12, second batch

Phase 14's last item, plus the flag that made it verifiable and the defect that finding it exposed.
Three commits:

| Commit | What |
|---|---|
| `765648a` | ⛔⛔ **Defect:** `OpenCodeClient` and `OpenCodeTuiClient` never supplied their own `FootprintService`, so `AgentConfigClientCore`'s default gave them **Claude's seven `~/.claude` categories**. `GetFootprintStatsAsync` returned real rows over real directories belonging to the other agent, and `DeleteFootprintCategoryAsync` would have deleted them. Both constructors now pass `OpenCodeFootprint.Catalog` and the `Roots` **method group**. Guard: `OpenCodeClientFootprintTests` — 5 tests, 4 of which fail against the pre-fix tree, verified by stashing the fix rather than assumed |
| `6e5352b` | ⭐ **The footprint page** — `OpenCodeFootprintViewModel` + `OpenCodeFootprintRowViewModel`, `Views/FootprintView.axaml` (+ code-behind), an `App.axaml` template, a `footprint` nav node, 21 resx keys, 16 wiring tests. ⭐ **Plus `--deep-link <nodeId>`**, in the same commit because `MainWindowViewModel` carries both halves: applied *after* the landing page is set so an unresolvable id leaves a working window, searching top-level nodes and their children and expanding a matched child's parent |
| `a429603` | **The harness** — `scripts/capture-page.ps1`. See *RESUME HERE* |

⚠ **Bisectability.** `765648a` is SDK-only and was built and tested alone before any app change
existed — its five tests passed against `OpenCode.Sdk` on its own. `6e5352b` is the state the
full build and 4,266-test run were taken against. `a429603` adds no compiled code.

**Three deliberate divergences from ClaudeForge's memory page**, each asserted in markup because an
omission cannot be observed from a running view-model: **no `DataGrid`** (it brings click-to-sort,
and a size sort inverts the page's guidance), **no delete button**, **no ancestor bindings**.

⛔ **Why there is no delete button.** The catalog's order *is* prune guidance, so the temptation is
to let the user act on it in place. Against it: the regeneration and retention behaviour of every
category is explicitly unmeasured (Phase 16), one row is in no backup at all and has no undo, and
`node_modules` is live state for a running agent. Reveal hands the folder to the file manager, which
asks its own question. **Re-opening this needs Phase 16's measurements, not an argument.**

⛔ **A fourth instance of the session's one shape.** The three earlier defects were a *write* path
resolving through a different function than every read path. This one is the same shape on a **read**
path — which is why no archive and no measurement caught it, and why it survived a whole phase with
its catalog sitting there tested and unused. **A product's data existing is not evidence that
anything consumes it.**

---

## Done — 2026-09-12, first batch

Eight commits, all on `feat/agentforge-opencodeforge`, none pushed. In order:

| Commit | What |
|---|---|
| `6968ae1` | `TipCell` moves `ClaudeForge.Controls` → `LayeredEditors.Avalonia.Controls`; six ClaudeForge views follow it. The two products cannot reference each other, so the alternative was a second copy |
| `040d26b` | ⛔ **Defect:** the shared credentials prompt hardcoded `~/.claude/.credentials.json`. Now `BackupPageOptions.CredentialsPathDisplay`, `required` like its siblings, so a third host is a compile error until it answers |
| `1634a4d` | ⭐ **OpenCodeForge's Backup / Restore page** — host options record, view + code-behind, `App.axaml` template, nav node, persisted state, 104 resx keys, 12 wiring tests. `OpenCodeBackup` went `internal` → `public` |
| `24fc550` | docs: the anchor catches up with its own commits |
| `820a098` | docs: the redirected-config defect, measured with a throwaway canary |
| `86bee98` | ⛔⛔ **Defect:** a redirected config backed up to an EMPTY archive that reported success. Now `config/` ← `GlobalDirectory(env)` plus `config-default/` ← the default root when they differ, gated by a new `ProductArchiveSection.IncludeWhen` |
| `9883518` | docs: the twin survey — five `DefaultGlobalDirectory()` call sites, one of them a live bug |
| `15af56a` | ⛔ **Defect:** that twin — `OpenCodeFootprint.Roots()` measured the wrong config root. Fixed and guarded |

**Three defects, one shape.** All three were *a write path resolving through a different function
than every read path*. Worth holding as a pattern rather than three incidents: when a product
exposes `X()` and `DefaultX()`, anything that WRITES or MEASURES must justify which one it uses.

### Bisectability — checked, not assumed

`6968ae1`, `040d26b` and `1634a4d` were each rebuilt at their own commit in a detached worktree.
All three build alone; `040d26b` runs the pre-change suite green at 4,227, so the +12 arrive with
the page rather than with the defect fix underneath it.

### Verification — the gap is closed, and here is exactly how far

✅ **Both pages have been seen rendered**, in the published `win-x64` build, driven by
`--deep-link` through `scripts/capture-page.ps1`.

- **Footprint page:** six rows in catalog order against OpenCode's own four roots, measured on a
  real install — `node_modules` 3,667 files / 52.4 MB at the top, `opencode.db` 3 files / 277.5 KB
  at the bottom carrying its *Cannot be regenerated* badge, total line `58.8 MB across 6
  categories`. This is also the observation that proves the defect fix end to end: the rows are
  under `~/.config/opencode`, `~/.cache/opencode` and `~/.local/...`, not `~/.claude`.
- **Backup page:** renders with exactly two scope radios, no MSIX tab, both client checkboxes, and
  no unreadable light-mode banner — the three deliberate divergences, confirmed visually rather
  than only by markup scan.

⚠ **Still NOT observed, and unchanged by this session: no backup has been taken or restored through
the GUI.** The round trip is covered at the SDK level by `OpenCodeBackupRoundTripTests` and
`OpenCodeRedirectedConfigBackupTests`; what remains unproven is the path from a button to it. The
harness navigates and photographs — it presses nothing.

⚠ **One cosmetic thing seen and then ruled out.** The footprint page's *Re-measure* button reads as
disabled at a glance. It is not: `IsBusy` is false and `RefreshCommand.CanExecute` is true after a
completed walk, both now asserted in `TheTotalCountsEveryCategory_IncludingTheEmptyOnes`. It is the
Semi Dark secondary-button fill against the blue-texted Reveal buttons beside it. Worth a style
pass, not a fix.

---

## 🔒 Locked decisions — do not relitigate

**Decided 2026-09-21, in a choices pass — each was a question and is now settled:**

- ✅✅ **PR #68 IS MERGED — 2026-09-21, merge commit `3de807d`.** Merged with admin at the
  maintainer's instruction, over the two known reds. ⭐ **`main`'s tree is byte-identical to
  `release/claudeforge-on-packages`**, verified by an empty `git diff origin/main
  origin/release/claudeforge-on-packages` — not inferred from the merge succeeding.
  ⓘ A MERGE COMMIT, not a squash, though this repo squashes by default: squashing would have
  collapsed the deliberate `-s ours` lineage and discarded 100 commit messages carrying the
  measurements and corrections. ⛔ **The branch is NOT deleted** — it is the release line.

- ✅ **NO app release is cut for this work, and none is needed.** Decided 2026-09-21: the changes
  are entirely structural, and an app release would only be deleted again afterwards. ⭐ Nothing
  depends on one — the `Published Version` red is cured by the **package** release, and
  `## [Unreleased]` simply rides along whenever an app release is next cut.
  ⓘ **A package release was never going to appear in Releases anyway**, which is worth knowing
  before worrying about it: `release-packages.yml` states outright that it creates no GitHub
  Release, because *"an entry with nothing to download for either of them belongs somewhere
  else"*. So `packages-v2026.3.921` publishes eleven packages to the feed and leaves the Releases
  page showing `ClaudeForge 2026.3.920` as latest.
  ⚠ One consequence, stated so it is not a surprise later: `release.yml` takes an app release's
  notes from the **merged PR body**, so #68's body is not consumed by anything until an app
  release happens. It remains the PR's record either way.

- ✅ **`main` takes this branch's tree WHOLESALE**, via `git merge -s ours origin/main` on the
  branch so the PR becomes mergeable and **`main` is never force-pushed**. ⛔ `-s ours`, not
  `-X ours`: the `-X` attempt produced a duplicated `Avalonia.Headless` ItemGroup and **ten**
  duplicate members where main's deep-link work collided with this branch's ported union of it.
  Both were caught only because warnings are errors here.
- ✅ **`2026.3.921` packages: the agent PREFLIGHTS, the maintainer TAGS.** Preflight passed —
  11 packages at one version, each carrying the assembly it was stamped from (`2026.3.921.1653`).
  Gate 3 is skipped without a token, so it was verified by hand instead: no `2026.3.921` exists
  for any of the eleven, and the newest on the feed is `2026.3.920`.
- ✅ **The XamlQuality adoption folds into #68** rather than becoming its own PR — cherry-picked
  as `98fb732`. It is test-only, so it cannot widen the release's risk surface.
- ✅ **Dependabot #67 is CLOSED as superseded**, with the bump applied here instead at
  `2026.3.916` (`aa5a1c5`). ⚠ Closing it plainly would have been a silent DOWNGRADE: this branch
  pinned `2026.3.914`, older than what #67 proposed, and this branch's file is what survives.
- ✅ **1Password's SSH agent is left alone**; git goes over HTTPS via the `insteadOf` the
  maintainer set. The failure mode and the `gh`-token workaround are now **lesson 10** in
  [`scripts/retest/README.md`](scripts/retest/README.md). The `.bashrc` `ssh-agent` snippet is to
  be removed — **awaiting the maintainer's approval of the exact lines**, per the global rule.

**Settled 2026-09-17 — the package release path.** Plans [`00002`](plans/00002-claude-code-real-config-locations.md)
and [`00003`](plans/00003-release-built-from-shared-packages.md) are the drafts carrying these.

- ⭐ **The shared libraries go to a DEDICATED REPOSITORY, published to nuget.org** — gated on being
  fully tested first. The private GitHub feed is therefore a **test rig, not the end state**, which
  is what makes the fork-credential cost below acceptable: it disappears when the packages are
  public. The Claude-specific halves stay in this repository and are consumed through the published
  feed. ⚠ All eleven packable projects are product-neutral **today**; `ClaudeForge.Sdk.Claude` and
  `ClaudeForge.Avalonia` are not packable, so that arrangement means a move **and** two new packages.
- ⭐ **The XAML-quality audits eventually ship as their own package**, enforcing theme, styling and
  accessibility rules across every Avalonia project rather than living in one app's test suite. ⛔
  The payoff starts before any move: the generic guards sit in `tests/ClaudeForge.Tests/` and
  **OpenCodeForge is not covered by them**. It is a different *kind* of package — analyzer or
  MSBuild task, consumed with `PrivateAssets="all"` by every project including the eleven, so it is
  **not** switched by `UseSharedPackages`. See 00003's *Downstream* section.
- **The release restores from the PUBLISHED feed**, not a locally-packed one, and **forks lose the
  ability to cut a release**. Temporary, per the first item.
- **Provenance is a build-time guard plus archived restore evidence naming the SOURCE.** ⛔ There is
  no artifact-level tell: `Directory.Build.targets:87` erases the one known difference on purpose
  and the publish strip removes `*.xml` either way. "Package mode" and "package mode from the
  published feed" are different claims and only the restore source separates them.
- **`SharedPackageVersion` is a committed pin in the ROOT `Directory.Build.props`.** Not a default,
  so the targets file's refusal is satisfied; the release needs no inputs and git history records
  every version move. ⚠ Root, not `src/` — test projects consume the packages too.
- **`LICENSE` and the package metadata both name Brian Bennewitz**, and `<Copyright>` gets set.
  Survives the repository move; a project-named holder would not.
- **Package `<Description>`s lose their internal lines only** — the layering invariants are already
  in `CLAUDE.md`; the outward-facing paragraphs stay as written.
- **`main`'s CHANGELOG stays silent about the v2026.3.916 accessibility fix.** No PR for the two
  entries. ⓘ Consequence: this branch's copies sit in `## [Unreleased]` describing shipped work, so
  each rebase re-decides about them unless they are dropped here too.
- ⛔ **`CLAUDE_CONFIG_DIR` does NOT relocate managed settings** — they live outside the config
  directory entirely. The policy-escape concern raised while drafting 00002 does not exist.
- ⛔⛔ **This ClaudeForge release carries NO OpenCodeForge code, and is cut from a ClaudeForge-only
  branch.** OpenCodeForge is revisited *after* the release, in the world where the packages exist
  and are published. ⓘ The shipped artifact already contained none — verified: no `ProjectReference`
  from the app or from any of the eleven, no OpenCode assembly in the Release output, and
  `release.yml` deliberately excludes the `opencodeforge-v*` tag. The requirement is about the tree.
- **The branch is made by SUBTRACTING from `feat/agentforge-opencodeforge`'s HEAD**, which is then
  **parked, not deleted and not merged**. A subtractive change on a known-good tree is verifiable;
  rebuilding from `main` would mean hand-porting hundreds of commits past a history rewrite, after
  which neither counts nor patch-ids can say when the port is done. Six projects go:
  the three under `src/` — `OpenCode.Avalonia`, `OpenCode.Sdk`, `OpenCodeForge` — and their three
  siblings under `tests/`. ⓘ Written as bare names rather than repo-relative paths on purpose:
  `BuildFilePathIntegrityTests` scans this file and treats an `src/…` spelling as a claim that the
  directory exists.
- ⚠ **Neutrality is evidenced ONCE on the parked branch before the `packages-v*` tag.** Extraction
  removes **~941 tests** and **50 files exercising `AgentForge.*` from the OpenCode side** — the only
  non-Claude consumer of libraries about to become immutable. ⓘ The guards survive:
  `NeutralLayerDefaultsTests` is a source scan of the neutral layer itself, not a two-product
  comparison, and `AssemblyLayeringTests` loses only a vacuous direction. What is lost is proof by
  *use*, so it gets produced deliberately rather than assumed. See 00003 step C0.
- **The package-mode guard fires on a LOCAL publish too**, with a named escape hatch that the
  archived evidence records. ⛔ A silent hatch would be this whole effort's founding defect pointed
  the other way.
- **The two accessibility entries are dropped from this branch's `## [Unreleased]`.** They describe
  work shipped in `v2026.3.916`; `main` owns released history and stays silent. Keeping them would
  have the next release announce a fix users have had since September.
- ⭐ **00002 lands AFTER the first package release, as the SECOND package version.** ⛔ An earlier
  draft called it a prerequisite of the `packages-v*` tag on the grounds that a later fix "costs
  another version and another day". That does not follow — only **re-pushing** an existing version
  is impossible, so a second version is a preference. The real reason to defer it is risk
  attribution: 00002 rewrites path resolution across `AgentForge.Core` and `AgentForge.Sdk`, and
  landing that in front of the release meant to prove the package pipeline would leave a failure
  with two candidate causes. ⭐ Deferring also removes C0's cross-branch port entirely, because the
  parked branch then holds identical shared-library code.
- **The WHOLE retest gates the `packages-v*` tag**, not only the five write-path items. ⓘ Chosen
  deliberately over gating on `E1`–`E5` alone — those five exercise one shared library each and are
  the ones that *could* expose a defect the feed cannot take back, but a tag that cannot be undone
  is not the place to be clever about which checks matter. ⚠ Consequence: all eight manual items are
  on the critical path, `E1` included, and `E1` is **manual** for this release because 00002 moved.
- ⓘ **`RepositoryUrl` / `PackageProjectUrl` need no decision.** Package metadata is per-version, so
  a version built from `JanusMael/ClaudeForge` correctly names it; later versions from the new
  repository carry their own. ⓘ The ids are free: `Bennewitz.Ninja.JsonC` is a 404 on nuget.org, and
  `Bennewitz.Ninja.AutoVersioning` is already published there, so the account exists.

- ⭐ **Schema loading: the disk cache is the MATERIALISED RESULT, not a tier in a chain.** Decided
  by the maintainer on 2026-09-12, three answers given explicitly:
  1. **A fetched artifact is never overwritten by bundled because a launch is offline** — that
     would silently downgrade the user. But **a newer bundled copy SHOULD replace a
     bundled-sourced one**, or upgrading the app strands them on what the old build extracted.
  2. **Disk holds the RESOLVED and overlaid artifact**, so loading it is a plain parse. The
     overlay is therefore baked in, which is what `overlaySha256` exists to invalidate.
  3. **Launch blocks on the fetch, but cheaply** — a conditional GET, so a `304` is one header
     exchange. (HEAD was considered and rejected: it costs a second round trip whenever there IS
     an update.)
  ⛔ `--schema-source bundled` resolves in memory and does **not** touch disk. ⚠ A null cache
  directory means **no disk**, and is the default, for the same measured reason the `HttpClient`
  default is OFFLINE.
- **The footprint page renders CATALOG order and offers no sort.** The order is the page's only
  real guidance and it is the inverse of a size sort: the largest row regenerates from
  `package.json`, the smallest meaningful one is the only irreplaceable thing on the page. A
  `DataGrid` brings click-to-sort by default, which is why the page uses an `ItemsControl` and why
  `TheViewOffersNoDeleteAndNoSort` scans for both.
- **The footprint page does not delete.** See *Done* for the three reasons. Re-opening it needs
  Phase 16's measurements.
- **Every OpenCode client supplies its own `FootprintService`.** `AgentConfigClientCore`'s default
  is Claude's catalog, and a product client that does not override it reports the other agent's
  files as its own. Both OpenCode clients pass the same catalog on purpose — `tui.json` lives
  inside the config root, so the TUI's footprint *is* OpenCode's.
- **`tests/AgentForge.Core.Tests/Fixtures/*.zip` are frozen.** Never re-mint one to make a test
  pass. A change that cannot restore one needs a migration, or a *second* fixture beside it.
- **A host that backs up a product must also be able to restore it** — same set to
  `BackupRequest.Products` and `new BackupEngine(restorableProducts:)`. OpenCodeForge passes
  `OpenCodeBackup.Engine`; `BackupEngine.Default` restores Claude's two and nothing else.
- **`ProductArchiveSection.IncludeWhen` is consulted by the WRITER only.** What a restore may apply
  is decided by what is in the archive: the machine reading it need not have the environment of the
  machine that wrote it.
- **OpenCodeForge's Backup view offers two scope radios and no MSIX tab**, and uses no literal
  colours. All three are deliberate divergences from ClaudeForge's copy, each guarded by a markup
  scan in `OpenCodeBackupWiringTests` because an omission cannot be observed from a running VM.
- **The footprint's config root is ONE root**, unlike the backup's two — the backup carries both
  because both hold user-authored config, while the only footprint category against that root is a
  regenerable cache. Re-opening this needs a *measurement* of a redirected install, not an argument.
- **`BackupMode` stays an enum** and cannot move to `AgentForge.Abstractions` (BCL-only by design).
  That is why `ProductSkippedSubdir.IncludedInFullBackup` is a bool rather than a mode name.
- **No second resx in the shell.** Hosts supply wording via `BackupPageText`.
- **OpenCodeForge's resx is English-only and declared so** — `ResxLedger` in
  `LocalizationParityTests` carries `("OpenCodeForge", false, …)`, and contracts #1–#4 run only
  against `src/ClaudeForge/Localization`. ⚠ Declaring this project *localized* is what would force
  #1–#4 to be generalised first.
- **OpenCode's config root is archived whole**, letting OpenCode's own `.gitignore` exclude the
  52 MiB `node_modules` — never a hardcoded skip list. Two earlier plan drafts got that list wrong.
- **`opencode.db` is opt-in with an advisory, never redacted**, and excluded from `Sanitized`
  outright. `auth.json` is never archived at all.
- **No AI attribution trailers on commits**, per the global `CLAUDE.md`. That decision outranks a
  session instruction mandating one; every commit on this branch carries none.
- ⭐ **The shared libraries become private NuGet packages BEFORE the ClaudeForge release.** Decided
  2026-09-13; see [`plans/00001`](plans/00001-shared-libraries-as-private-nuget-packages.md) for the
  four sub-decisions (apps and all twelve test projects switch together, the version comes from an
  MSBuild property AutoVersioning is taught to emit, the Windows TFM is deleted, a release
  pre-flights the feed). ⛔ **`UseSharedPackages` is a MODE, not a rewrite** — `ProjectReference`
  for development, `PackageReference` for the per-PR canary and the release publish. Making it
  unconditional was considered and rejected: it costs the inner loop and makes a clean clone
  depend on feed credentials.
- ⛔ **The Caution glyph's `#D97706` is a DELIBERATE 3:1 trade — do not "fix" it.** Maintainer,
  2026-09-14: *"AA contrast for this particular glyph I can give up; accessibility is intended to
  accommodate screen readers as first class but low vision is less supported already and I am ok
  with that."* A contrast audit WILL flag it (3.19:1, below the 4.5:1 text floor) and the obvious
  remedy — darken it — is exactly the regression this prevents, because darkening amber is what
  makes it brown. It is triple-coded: shape, tooltip, and `AutomationProperties.HelpText`.
  ⚠ Distinct from the `#F57C00` incident on record, which was 2.70:1, below BOTH floors,
  unmeasured, and the only channel. Scope is the GLYPH only —
  `AppStatusWarning/FailureForegroundBrush` keep their AA-text values.
- ⭐ **Severity tiers are separated by SHAPE, not luminance, and that is forced rather than chosen.**
  Measured: all four tiers sat within 1.02–1.08:1 of each other in both themes. On white, 4.5:1
  pushes every tier below luminance ~0.175, so four tiers cannot be far apart inside that band.
  Colour can make them pop; only shape can make them distinguishable. Glyphs follow Windows'
  three-icon convention — `⊗` Critical, `⚠` Caution, `●` Info, `○` Neutral. ⛔ Two warning triangles
  differing only in hue was rejected: it puts Critical and Caution on the red-amber axis with no
  shape to separate them.
- ⛔ **`⚠` is BARE — no U+FE0E text-variation selector.**
  `EveryGlyphIsASingleBmpCharacterNotAnEmoji` rejects anything over one UTF-16 unit. `U+26A0+FE0E`
  is two BMP units, not the surrogate pair that guard was written for, so its REASON does not apply
  while its RULE still fires. U+26A0 already defaults to text presentation. ⚠ **Rendering is NOT
  confirmed on any platform** — if it shows as colour emoji, add FE0E and widen the guard
  deliberately.
- ⛔ **A themed brush is SHARED AND MUTABLE.** `BrushHelper.ResolveThemed` hands back one instance
  per key and re-colours it on variant change; treat what it returns as read-only. The cache is
  identity-only — the colour is re-read on every resolve — because caching the colour made it stale
  when resource dictionaries changed without the variant changing.
- ⭐ **A package id must not overclaim, and it is only free to fix before the first publish.**
  `JsonC` was renamed out of `AgentForge.*` on 2026-09-14 for exactly this reason: it is a
  dependency-free JSONC reader that knows nothing about agents. ⛔ Being a family of one costs
  four edits in four files that do **not** share a list — the switch in `Directory.Build.targets`,
  `PackageMetadataTests`, `AssemblyLayeringTests`, and **`nuget.config`'s `packageSourceMapping`**.
  ⛔ The fourth is invisible to every normal build and only the package canary fails on it. Prefer
  a family prefix for anything new.
- ⛔ **`AssemblyLayeringTests` keeps its OWN selector, and its vacuity guard cannot detect a
  family departure.** "At least one shared project exists" stays true while a renamed one silently
  leaves the scan. Widening it is part of any rename, not an afterthought.
- ⭐ **A namespace must not claim a dependency the project does not have.**
  `LayeredEditors.ViewModels` carried `.Avalonia` in every namespace while referencing no Avalonia
  at all; the namespace moved rather than the assembly, because the cheap fix would have cemented
  something untrue in the package id.
- ⚠ **`RootNamespace` is not the namespace.** Six of them disagreed with the code for a long time
  and nothing failed — it only drives new-file defaults and generated code. Read the `namespace`
  declarations before believing a csproj.
- ⭐ **Package identity is declared once, in `src/Directory.Build.props`, and derived from
  `$(MSBuildProjectName)`** — never from `$(AssemblyName)`, which is empty at that point, and never
  from a `Directory.Build.targets` placed under `src/`, which would silently detach every `src`
  project from the root targets file and its three build-time guards. Decided 2026-09-13 doing plan 00001
  item 2; the reasoning is written into the props file itself.
- ⭐ **`UseSharedPackages` is a MODE, and the switch is CENTRAL** — one ItemGroup in the root
  `Directory.Build.targets`, not a conditional in fourteen csproj files. Every project that is not
  itself one of the eleven switches together (`IsPackable != true`), which includes the four
  **product-specific** libraries the plan's own list omits: leaving one on a `ProjectReference`
  re-creates the duplicate-assembly graph through the app that references it. ⛔ A
  `Directory.Build.targets` under `src/` is the trap, not the answer — same finding as item 2.
- ⛔ **The switch matches a NAME PREFIX, and the name must therefore agree with packability.**
  MSBuild cannot read a referenced project's `IsPackable`, so the rewrite keys on `AgentForge.` /
  `LayeredEditors.` and `PackageMetadataTests.TheSwitchesNamePrefixSelectsExactlyThePackableProjects`
  asserts the two sets are the same set, **both directions**, because both fail quietly.
- ⛔ **Never re-use a canary version.** NuGet extracts by id+version and will not re-extract, so the
  second run of a repeated version validates the first run's packages and reports success. The
  script uses a per-second timestamp **and** a `NUGET_PACKAGES` directory named for it — both, and
  the directory is created fresh rather than emptied, because MSBuild's nodes hold DLLs open out of
  it and the delete fails on Windows *after* the work is done.
- ⭐ **The shared libraries run the trim analyser themselves** (`EnableTrimAnalyzer`, `src/` only),
  because packaging removes the only route it reaches them by today — a global property flowing
  through the app's build graph, which a packaged library is not in. Scoped to `src/` deliberately:
  the plan's "224 diagnostics" figure was a global measurement, and `tests/` is never published.
  ⛔ **`IsAotCompatible` is NOT set** — it implies the AOT and single-file analysers as well, which
  is a claim about these libraries nothing has measured. Adding it is a measurement, not a tidy-up.
- ⛔ **Every project under `src/` states `<IsPackable>` for itself, and no guard says WHICH eleven
  pack.** A test asserting the packable set is exactly a named eleven needs a copy of that list,
  and the copy is what drifts. Asserting that nothing *defaults* is the non-vacuous form — a
  library that says nothing defaults to packable and gets pushed to a feed that will not let the
  version be replaced. `PackageMetadataTests`, three methods, all four canaries fired.
- ⛔ **A plural `<TargetFrameworks>` does nothing unless the inherited singular is cleared first.**
  Three projects declared one and none ever built it. `SingleTargetFrameworkTests` enforces this,
  and fails rather than passes if the root stops setting the singular form.
- **The share service opens no share sheet, on any platform**, and reintroducing one is a new
  feature rather than a revert — its own TFM that actually builds, its own trim pass, its own
  retest. See `DefaultShareService`'s remarks.

---

## Known issues / debt

Newest first.

- ⚠ **`AGENTS.md`'s "adding a debug flag" checklist describes ClaudeForge's parser, and the two
  apps disagree on the one rule that matters.** That list says a two-token flag consumes its value
  with `args[++i]` unconditionally; OpenCodeForge's `DebugFlags` documents and implements the
  opposite — **flags PEEK, they do not consume**, so every argument still reaches Avalonia. Both are
  right for their own app, and the checklist names `src/ClaudeForge/Services/DebugFlags.cs`
  explicitly, so nothing is wrong today. ⚠ It is one careless copy away from being wrong: `--deep-link`
  was added here following this app's rule, not the checklist's. Left alone deliberately —
  `AGENTS.md` rows are guard-test-backed and generalising this one is its own change.
- ⛔ **The merge of `main` DROPPED `cafe89c` *fix(schema): make the fire-and-forget disk-cache sync
  awaitable*, and the debt is only partly repaid.** Not because the race was imagined but because
  the thing that raced was gone: taking `main`'s side did not even compile, since its code reads
  `PlatformPaths.SchemaCacheDirectory`, which this branch removed. ✅ The *production* half is now
  back and guarded — `SchemaDiskCache` has a per-path semaphore plus temp-then-rename. ⚠ **The
  TEST half is not.** `AvailableProfileEntriesTests` carries an explicit marker where its
  `await registry.WhenDiskCacheIdleAsync()` used to sit; a launch writes to disk again, so that
  teardown can race a write exactly as `main` found it. Reinstate a drain rather than
  rediscovering the flake.
- ⚠ **Seven Claude-named types still live in the "product-neutral" `AgentForge.*` layer**
  (`ClaudeArtifactPaths`, `ClaudeCodeLocation`, `ClaudeDesktopVersionProbe`, `ClaudeScopes`, and
  three artifact-source classes), plus Claude-specific members on neutral types. All are
  unreferenced by OpenCode, so they are residue rather than an active bug — and **not** a blocker
  for a ClaudeForge-only release, since every one of them is correct for Claude. ⛔ The dangerous
  class is the one no name search finds: a neutral type whose *default* resolves to Claude data.
  Two remain (`SchemaSnapshotService`, `RestoreSidecarCleanup`). Full inventory in
  [`docs/EXTRACTION-VERIFICATION.md`](docs/EXTRACTION-VERIFICATION.md) §2.
- ⓘ **`AssemblyLayeringTests` cannot see any of that** — all three of its methods check assembly
  *references*, and Claude-shaped code inside a neutral assembly declares none. Not a failure of
  the guard; it enforces what it claims.
- ⓘ **The footprint page's *Re-measure* button reads as disabled.** Cosmetic; proven enabled. See
  *Verification*.
- ⓘ **`FootprintService.GetProjectTranscriptStatsAsync` stays Claude-shaped on an OpenCode client.**
  It walks `~/.claude/projects` through `ClaudeArtifactPaths` regardless of catalog, so it is the
  one footprint member the catalog fix does NOT make product-correct. Nothing calls it from
  OpenCodeForge and the new page does not, but it is the same latent lie the catalog defect was —
  a read path that answers confidently for the wrong product. Fixing it means either a per-product
  seam or making it throw on a non-default catalog; both are decisions, not cleanups.
- ⚠ **The `OpenCode TUI` checkbox archives nothing of its own.** `OpenCodeProducts.Tui` carries no
  `BackupLayout`; `tui.json` travels anyway inside the config root. Ticking it changes only the
  archive's `manifest.clients` and which schema is bundled. Listing it is still right — it starts
  working the day the product gains a layout — but the checkbox promises more than it delivers.
- ⚠ **A restore does not reload the open documents.** ClaudeForge passes `OnRestoreCompleted`,
  `IsAnyWorkspaceDirty` and `SaveAllWorkspaces` into the shared page; OpenCodeForge's window has no
  save-all pipeline to wire them to, so they are unset. The editor keeps showing the pre-restore
  file until the app is restarted.
- ⓘ **The two-roots survey.** Five `DefaultGlobalDirectory()` call sites in `src/`: one is the
  fallback inside `GlobalDirectory` itself; two — `OpenCodeArtifactSources.AddGlobalSources` and
  `OpenCodeEssentialsViewModel.HasShadowedGlobalRules` — already handle both roots deliberately and
  are the precedent the backup fix followed; the fifth was the footprint bug, now fixed. ⚠ Both
  correct sites compare with an unconditional `OrdinalIgnoreCase` where the backup's new
  `SameDirectory` asks the real OS. On Linux two roots differing only in case would read as one
  there. Vanishingly unlikely; on the record rather than silently inconsistent.
- ✅ **RESOLVED 2026-09-13 (`4ac7844`)** — `DisplayClients` rendering `OpenCode+OpenCodeTui` in a
  110 px cell. The abbreviation map is host-supplied now (`BackupPageText.ClientAbbreviations`,
  `required`), so the neutral layer names no products and the cell reads `OpenCode+TUI`.
- ⓘ **Three accessible names on the Backup page are formatted in markup**, not resx —
  `{Binding DisplayName, StringFormat='{}{0} — Restore'}` and two siblings. Carried over from
  ClaudeForge, which has the same three, so the two apps at least agree.
- ⓘ **ClaudeForge's `BackupRestoreView.axaml` declares a `BytesToHumanReadableConverter` resource it
  never uses.** Noticed while porting; left alone.
- ⚠ **`RestoreResult.Message` is still English, and it is the last of the unlocalised strings.**
  ✅ The progress labels are done (`131a38c`) — keyed by section id exactly as this entry asked,
  with `"Restoring the default opencode config root…"` among them. ⛔ The result message was left
  out **deliberately, not forgotten**: localising it means decomposing a multi-sentence status line
  (item count, the `.pre-restore-*.bak` advisory, the skipped-paths list, the credentials-omitted
  note) into structured fields on `RestoreResult`, and it also changes two English string
  comparisons in the shell (`!= "Restore cancelled."`, `!= "Backup cancelled."`). That is a
  coherent change of its own, and worse if half-done — a status line mixing a translated first
  sentence with English remainder.
- ⚠ **`WithNoNetwork_BothSectionsSayBundled` flaked once** on 2026-09-11, then passed in isolation,
  in its own assembly, and in every full run since. Cause unknown; not reproducible on demand.
- ⛔ **Phase 16's quantitative half stays blocked.** `usage.isUsedInstall` is still `false`, so
  growth, retention and prune *rates* are unmeasurable.
- ⓘ **The local OpenCode install stays contaminated** from an earlier session: three
  `opencode debug v2` runs fetched `models.json` and ripgrep and left two empty
  `~/.cache/opencode/bin/ripgrep-*` temp dirs. Deleting them is a user decision.
- ✅ **RESOLVED 2026-09-12** — `CLAUDE.md`'s "builds ClaudeForge only" line was indeed stale.
  Verified against `publish.ps1`'s own `-App` parameter (which documents `-App OpenCodeForge`) and
  against `.github/workflows/`, which carries both `release.yml` and `release-opencodeforge.yml`.
  The paragraph is corrected and carries a dated note saying what it used to claim.
