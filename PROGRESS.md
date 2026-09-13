# PROGRESS — work state

> **What this file is.** The resume anchor: where the work stands *right now*, what comes next, and
> which decisions are already settled. It is replaced, not appended — a status table that lags
> reality is worse than none.
>
> **Ground truth beats this file.** Treat every claim here as a hypothesis and reconcile before
> acting: `git log`, `git status`, the test suite. Let reality win on any conflict.
>
> For the *why* behind the architecture see [`CLAUDE.md`](./CLAUDE.md); for enforceable rules see
> [`AGENTS.md`](./AGENTS.md); for phase-by-phase detail and every measurement see
> [`docs/OPENCODEFORGE-PLAN.md`](./docs/OPENCODEFORGE-PLAN.md).

---

## Where things stand — 2026-09-13

| | |
|---|---|
| Branch | `feat/agentforge-opencodeforge` |
| HEAD | `537493a` *docs: handoff — the AI-facing docs catch up with the packaging turn*, plus this cell's own correction on top. `git log -1` wins over this cell |
| Working tree | clean |
| Unpushed | **61 commits**, counting the commit that writes this cell (`git rev-list --count @{u}..HEAD` — trust that over this cell). Nothing pushed; **no PR** |
| Merged from `main` | ✅ `ea6d129`, 2026-09-12 — level with `main` (`git rev-list --count HEAD..main` = 0) |
| Suite | **4,291 passed · 0 failed · 11 skipped**, Debug |
| Trim check | Release publish clean for **both** apps across **all six RIDs** — 12/12, zero ILLink warnings, re-run 2026-09-13 after the Windows-TFM deletion |
| ⚠ Awaiting | **The maintainer's manual retest pass** — see below. Everything that was being batched for it is now done |

---

## ▶ RESUME HERE — the retest, then plan 00001

**Two things drive everything: cut a ClaudeForge release from the split, and get the shared
libraries out as private NuGet packages first.** The second is new as of 2026-09-13 and has an
approved plan: [`plans/00001-shared-libraries-as-private-nuget-packages.md`](plans/00001-shared-libraries-as-private-nuget-packages.md).

⭐ **The hard part of the release is already true: the ClaudeForge artifact has never contained
OpenCode.** Its Release build output is 14 assemblies — 6 `AgentForge.*`, 5 `LayeredEditors.*`,
3 `ClaudeForge*` — and **zero** OpenCode ones; `release.yml` passes `-App ClaudeForge` explicitly
on all three publish jobs and its tag pattern excludes `opencodeforge-v*`. Nothing needs carving out.

### ✅ The retest batch is finished — hand over now

Both items the previous session was holding work for are done, and one defect surfaced while doing
them. Ranked by what actually changed and what automation cannot reach:

1. **Theming, BOTH variants** — highest risk, unchanged from the previous handoff. Two theme files
   merged with colour tokens from both sides. Check the ✨ NEW badge (`AppAccent*`, new from
   `main`), the severity dots, and the save-dialog change pills. Automation proved the keys exist,
   never that they look right together.
2. **The keybinds editor's warning states** — `LE.DangerText` / `LE.DangerBorder` were referenced
   nine times and defined nowhere; the original bug was invisible because no conflicting keybind
   was ever seeded. Seed one and confirm red text inside a visible border.
3. **Save → reload a config, in both apps.** Still the one thing no verification has touched, and
   this branch changed the save dialog.
4. **Diagnostics windows (F12)** — `main`'s accessibility pass and live-log fix arrived untested.
5. ⚠ **Share, from the About and Effective-settings pages, on Windows.** New to this list.
   `DefaultShareService` lost six `#if` blocks and a constructor parameter in `23f7c4f`. The claim
   that this changed nothing rests on the TFM never having built — which is measured — plus 4,291
   tests and a clean 12/12 trim matrix. **Nobody has launched the app and pressed Share.** Expect
   Explorer to open with the file selected, or the default browser for a URI.

⚠ **Also worth one look: `src/publish/publish.ps1` lost its `maui-windows` workload preflight.**
Sixty lines that ran on every Windows release publish and could abort it. The script still parses,
but no release has been cut through it since.

### After the retest — the release path

- ⚠ **`CHANGELOG.md` is stale**: its top entry is `[2026.2.528] - TBD`, older than the shipped
  `v2026.3.901`, with no Unreleased section. **13+ `feat` commits** touching ClaudeForge's UI need
  writing up — a feature release, not a plumbing one (severity indication across rows / search /
  effective view / save dialog, the schema provenance badge and in-app check, `--schema-source`,
  the no-raw-hex tripwire, and now the host-supplied backup wording).
- Free, no retest cost: a guard for Claude-shaped defaults in the neutral layer (see
  [`docs/EXTRACTION-VERIFICATION.md`](docs/EXTRACTION-VERIFICATION.md) §5), roadmap phase markers
  1–9, and `TRIMMING.md` never mentioning the second app.

### Plan 00001 — where it stands

Item 1 is done (`23f7c4f`). ⛔ **Item 3 is blocked and it is the long pole:** the package version
must come from an MSBuild property AutoVersioning is taught to emit, and **that package's source is
not in this repository** — ask the maintainer where it lives. The plan records an interim
(`PackageVersion` computed from `BuildTimestamp`, which AutoVersioning already defines and the
generator provably consumes) if that release slips.

Items 2, 4, 5, 6 and 7 are unblocked and independent of it.

### 🔭 Open strategic question — do not start without the maintainer

Whether the shared libraries eventually leave this repository altogether. Plan 00001 is a staging
post toward that and says so; nothing has been decided, and the plan's "not in scope" section is
the current answer.

### ⭐ Capability worth knowing about before anything else

**A page in these apps can be looked at.** `scripts/capture-page.ps1` launches a *published* app,
deep-links to one node, photographs the window and shuts it down:

```powershell
pwsh -NoProfile -File scripts/capture-page.ps1 `
    -ExePath src/OpenCodeForge/bin/Release/net10.0/win-x64/publish/OpenCodeForge.exe `
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

## Done — 2026-09-13

Five commits. The retest batch, then a strategic turn.

| Commit | What |
|---|---|
| `98b74d1` | ⛔ **OpenCodeForge built THREE `SchemaRegistry` instances** — its own inside `InitializeAsync`, plus a private one inside each client, because `AgentConfigClientCore` makes its own when handed `null`. Every schema fetched twice per launch; an offline launch paid the 3s timeout per registry. ⭐ The correctness half is the provenance badge: with three registries it could report `Fetched` for the pages while the registry validating saves had fallen back to bundled. Both clients now take an optional registry; a three-rung constructor chain builds one environment and one registry and hands each to both. `SharedSchemaRegistryTests` pins both halves, canaried by stashing the fix |
| `4ac7844` | **The Clients column's short names come from the host.** `AbbreviateClient` knew `"claudecode"` and `"claudedesktop"`, hardcoded in the neutral shell, so OpenCode's two fell through its unknown-product passthrough and the cell rendered `OpenCode+OpenCodeTui` in 110 px. `BackupPageText.ClientAbbreviations` is `required`, keyed by `ArchiveFolder` — which is what `BackupEngine.BuildClientList` writes into `manifest.clients`, so a map keyed by `Id` would compile and abbreviate nothing. Now `OpenCode+TUI`; ClaudeForge unchanged |
| `131a38c` | ⭐ **All fifteen progress-bar phrases come from resx**, translated into all nine locales. The seam is an id beside the English fallback: `BackupProgress.ItemId`, and `ProductArchiveSection.ProgressLabelId` **derived from `SubPath`** rather than declared, because the sub-path already is the section id. Each app has a guard taking ids from the *descriptors* that asserts both coverage and that each label says what the engine says — presence alone passes two keys swapped between sections. **TWINS:** `BackupEngine`'s `"Discovering projects…"`, the backup side's only phrase, fixed here too |
| `5f1a0aa` | ⭐ **[`plans/00001`](plans/00001-shared-libraries-as-private-nuget-packages.md)** — the approved packaging plan, committed before implementation per the plan workflow |
| `23f7c4f` | ⛔⛔ **A Windows TFM that was never built, in three projects.** See below |

### ⛔⛔ The Windows TFM, and why nothing ever failed

`src/ClaudeForge`, `src/OpenCodeForge` and `src/LayeredEditors.Avalonia.Services` each declared
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
existed — its five tests passed against `src/OpenCode.Sdk` on its own. `6e5352b` is the state the
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
