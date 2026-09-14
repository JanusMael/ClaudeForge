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

## Where things stand — 2026-09-14

| | |
|---|---|
| Branch | `feat/agentforge-opencodeforge` |
| HEAD | The commit that writes this cell, sitting on `2ba224e`. ⓘ A hash cannot be written into the commit that produces it, and two earlier attempts each needed a follow-up commit to correct this row — so it names no hash: **`git log -1` is the answer** |
| Working tree | clean |
| Unpushed | **75 commits**, counting this one (`git rev-list --count @{u}..HEAD` — trust that over this cell). Nothing pushed; **no PR** |
| Merged from `main` | ✅ `ea6d129`, 2026-09-12 — level with `main` (`git rev-list --count HEAD..main` = 0) |
| Suite | **4,302 passed · 0 failed · 11 skipped**, Debug — two new `ReleaseWorkflowTests` on the **4,300** the packaging batch left. ⚠ **The skipped count is machine-dependent now, and 11 is the LUCKY reading.** One of the three is inconclusive rather than green when `artifacts/localfeed` holds no packages, so a clone that has never run the canary reports **4,299 · 12**. That is the guard refusing to claim a measurement it did not take, not a regression |
| Trim check | ⓘ The **12/12** six-RID two-app matrix is from 2026-09-13, **before** the two packaging commits, and has **not** been re-run. Neither adds code. What has been re-run since: a full Release solution build (zero IL diagnostics) and two `ClaudeForge win-x64` Release publishes, both clean, with no `.md` in the output. Say "12/12 plus spot-checks", not "12/12" |
| Trim analyser | ⭐ **`EnableTrimAnalyzer` is now on for everything under `src/`**, so the Roslyn half runs on **every build, Debug included** — not only inside an app's trimmed publish. ⚠ ILLink's whole-program pass, which is what the matrix above measures, still runs only on a publish |
| Packaging | ⭐ `dotnet pack ClaudeForge.slnx -c Release` produces **exactly eleven** `.nupkg`, zero warnings, ids prefixed `Bennewitz.Ninja.`, inter-package dependencies resolving to those ids — **now all at `2026.3.914`**, the same three parts the built DLLs stamp (`2026.3.914.1303`). The `1.0.0` this cell reported for two weeks is gone; plan 00001 item 3 is done |
| Package canary | ✅ **PASSED** end to end on 2026-09-13, `0.0.0-local-20260913161300`: eleven packages → isolated `NUGET_PACKAGES` → Release build → **4,295 passed · 0 failed · 11 skipped** → both apps published `win-x64`. Zero warnings, zero IL diagnostics. Run it with `pwsh -NoProfile -File scripts/package-canary.ps1` |
| ⚠ Awaiting | **The rest of the manual retest.** The first pass ran 2026-09-14 and found six defects, all now fixed (`e9859b9`, `790ce63`) — but the fixes themselves are **unverified in the running app**. See *RESUME HERE* |

---

## ▶ RESUME HERE — the retest, then plan 00001

**Two things drive everything: cut a ClaudeForge release from the split, and get the shared
libraries out as private NuGet packages first.** The second is new as of 2026-09-13 and has an
approved plan: [`plans/00001-shared-libraries-as-private-nuget-packages.md`](plans/00001-shared-libraries-as-private-nuget-packages.md).

⭐ **The hard part of the release is already true: the ClaudeForge artifact has never contained
OpenCode.** Its Release build output is 14 assemblies — 6 `AgentForge.*`, 5 `LayeredEditors.*`,
3 `ClaudeForge*` — and **zero** OpenCode ones; `release.yml` passes `-App ClaudeForge` explicitly
on all three publish jobs and its tag pattern excludes `opencodeforge-v*`. Nothing needs carving out.

### ▶ The manual retest — first pass done, fixes UNVERIFIED

The maintainer drove ClaudeForge (Release `win-x64`) on 2026-09-14 and reported findings one at a
time; fixes were batched and landed in `e9859b9` + `790ce63`. **Every fix below is unverified in a
running app** — that is the next thing to do.

**Passed, do not retest:** save → reload a config (the one path nothing had ever exercised),
the F12 live-log window, and About page → *Share Log*.

**Fixed, needs looking at:**

| | What to check |
|---|---|
| Theme brushes | Toggle light ↔ dark repeatedly on a settings page. Severity glyphs must change palette **immediately**, on every row, with no mixture. This was the worst defect found |
| Severity glyphs | Critical is now `⊗`, Caution `⚠`, at FontSize 14. ⛔ **`⚠` rendering is NOT confirmed on any platform** — if it shows as colour emoji or tofu, say so |
| Light palette | Critical `#CE2029` (fire engine), Caution `#D97706` (amber, not brown) |
| Share config | Effective settings → *Share config* now copies the JSON to the clipboard on Windows. Paste somewhere to confirm |
| Config watcher | Create `settings.local.json` or `.mcp.json` **while the app is running** — it should now be picked up. `logs/events-*.txt` should say `armed for 6 of 6` |
| F12 keyboard | Tab should now reach the header links (*Log file*, *Open folder*, *Config-file events*) with a visible focus ring, and Enter/Space should activate them |
| About dialog | The stray vertical line is gone |

**Still never tested at all:**

1. **Theming across both variants, systematically** — the ✨ NEW badge (`AppAccent*`, from the
   `main` merge), severity dots, save-dialog change pills. Automation proved the keys exist,
   never that they look right together.
2. **OpenCodeForge — nothing has been driven.** Its keybinds editor's warning states
   (`LE.DangerText` / `LE.DangerBorder`) need a **conflicting keybind seeded** or they never
   render. Also its own theming and save → reload.
3. **F12 diagnostics accessibility** beyond the tab trap — `main`'s accessibility pass arrived
   through the merge untested.

⚠ **Close any other ClaudeForge before testing.** A second instance from `C:\c\cl\ClaudeForge`
(Debug build) was running against the same `~/.claude` throughout the first pass; each app sees
the other's writes as external changes, which is exactly the signal the watcher test measures.

ⓘ **Two live surfaces worth knowing:** **Shift+F12** opens *Live Config-File Events*, and
`logs/events-*.txt` beside the executable now persists the same stream with scope.

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

Items **1** (`23f7c4f`), **2**, **3**, **4** and **5** are done. ▶ **Item 6 — the publish pipeline —
is the next one, and it is now unblocked**: it needed a real version to push, and it has one.

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

⚠ **Two things item 5 deliberately left for item 6**, both recorded so they are not rediscovered:

- **`src/publish/publish.ps1` still knows nothing about `artifacts/`.** It wipes every `bin/` and
  `obj/` under `src/` and all of `dist/`, so it does not delete the local feed — but a stale
  local-feed package **outranks** GitHub Packages in `nuget.config`'s source mapping, so a release
  cut on a machine that has run the canary could consume a local build at the release version.
- **Nothing pre-flights the feed for an already-published version**, which is item 6's core.

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
