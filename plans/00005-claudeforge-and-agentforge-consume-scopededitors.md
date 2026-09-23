# 00005 — ClaudeForge and AgentForge consume ScopedEditors and AppServices; LayeredEditors leaves

> Status: **approved 2026-09-23**. Supersedes [`00004`](00004-claudeforge-consumes-scopededitors-and-appservices.md).
> Stage two of `Bennewitz.Ninja.Templates`
> [`plans/00002`](https://github.com/JanusMael/Bennewitz.Ninja.Templates/blob/main/plans/00002-layerededitors-becomes-two-package-repositories.md).

Every consumer of `Bennewitz.Ninja.LayeredEditors.*` in this repository moves to the seven ids
published on nuget.org at `2026.3.923` — `Bennewitz.Ninja.ScopedEditors.{Abstractions,ViewModels,Avalonia}`
and `Bennewitz.Ninja.AppServices{,.Abstractions,.Logging,.Avalonia}` — and the five `LayeredEditors`
projects and their two test projects are **deleted** in the same change.

## Why `00004` is superseded

`00004` scoped this to the four ClaudeForge projects and left `AgentForge.*` on the local
`LayeredEditors` projects. **That cannot compile.** `AgentForge.Avalonia.Shell` is not a bystander
that happens to import the same namespaces: it **is** the adapter layer that turns AgentForge's
settings model into the editor abstractions, so its public API is built from `LayeredEditors`
types — **47** lines of its public-surface baseline, plus **4** `DialogMessage` factories in
`AgentForge.Sdk`'s. ClaudeForge passes those adapters straight into the editor view-models:

```csharp
// src/ClaudeForge/ViewModels/Editors/DefaultEditorFactory.cs:133
new LibVm.StringPropertyEditorViewModel(new SchemaNodeAdapter(schema), ConfigScopeAdapter.For(editingScope));
```

Repoint only ClaudeForge and `StringPropertyEditorViewModel` wants `ScopedEditors…IEditorSchema`
while `SchemaNodeAdapter` implements `LayeredEditors…IEditorSchema` — same name, **different
type**, a compile error on that line and every one like it.

⛔ **The mistake was measuring IMPORTS, not COUPLING.** `00004` counted files that *use* a
namespace; that says nothing about whether the renamed types *cross the boundary* to a sibling that
stays behind. The public-surface baseline answers that in one grep, and was not consulted until
implementation began. ⭐ This plan was re-measured the other way round — boundary first — and that
is what surfaced the F12 and guard findings below. It was caught before any source changed; the
cost was one plan number.

## What is in scope, measured

**Repointed — seven consumers, 134 files:**

| Project | Files importing a `LayeredEditors` namespace |
|---|---|
| `src/ClaudeForge` | 49 |
| `src/ClaudeForge.Avalonia` | 1 |
| `src/AgentForge.Avalonia.Shell` | 20 |
| `src/AgentForge.Sdk` | 1 |
| `tests/ClaudeForge.Tests` | 61 |
| `tests/ClaudeForge.Avalonia.Tests` | 1 |
| `tests/AgentForge.Sdk.Tests` | 1 |

**Deleted — the family and its tests:** `src/LayeredEditors.{Abstractions,ViewModels,Avalonia,Avalonia.Services,Avalonia.Diagnostics}`
and `tests/LayeredEditors.{Avalonia,Avalonia.Diagnostics}.Tests`. ⛔ **Except three test files, which
MOVE** — they cover the F12 cluster, which stays; see step 5. ⓘ `samples/` imports none of it.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **ClaudeForge and AgentForge move together**, in one change | They share the editor types at their boundary; neither can move alone. Supersedes `00004`'s ClaudeForge-only scope, by the maintainer's decision |
| 2 | **The five `LayeredEditors` projects and their two test projects are DELETED** | Once both families are repointed nothing imports them — except the held-back F12 code, which decision 3 relocates. `00004`'s duplication, and the tripwire it needed, both disappear |
| 3 | **The F12 cluster moves into `src/ClaudeForge`** and runs on the packages | See *The F12 windows*. It is ClaudeForge-only debug UI; the app is not packable, so it stays unpublished |
| 4 | **Migrate the breaking service APIs properly, not with adapters** | As in `00004`: a shim preserving `void`/`bool` discards `Unsupported`, the reason the API changed |
| 5 | **`nuget.config` gains explicit patterns** for `Bennewitz.Ninja.AppServices*` and `Bennewitz.Ninja.ScopedEditors.*` on `nuget.org`, and **loses** `Bennewitz.Ninja.LayeredEditors.*` from `github` and `localfeed` | Explicit over a catch-all for ids that must come from nuget.org. ⚠ Most-specific-wins, so the new patterns must not appear under `github` |
| 6 | **The eleven become SIX** — `AgentForge.*` ×5 and `JsonC` | A consequence, not a choice, and it touches prose everywhere. Recorded so the count is changed deliberately rather than left wrong in a dozen documents |
| 7 | **The next `packages-v*` release is BREAKING** for `AgentForge.Avalonia.Shell` and `AgentForge.Sdk` | 51 lines of their public-surface baselines change type. Their package dependencies also move from `Bennewitz.Ninja.LayeredEditors.*` on GitHub Packages to the nuget.org ids |
| 8 | **F12 and Shift+F12 are rewired LOCALLY** — a Serilog sub-logger around `Log.Logger`, and a forwarding wrapper for events | The package has no sink hook. Adding one upstream is cleaner but waits on another repository's release |
| 9 | **Build on `2026.3.923`; claim the trim gate only on `2026.3.924`** | `.923` shipped without the `IsTrimmable` mark, so ILLink never analyses the seven and a clean trim result is vacuous for them. See *The trim gate is vacuous on `2026.3.923`* |

### Alternatives dismissed

- **ClaudeForge only** (`00004`). Does not compile — see above.
- **Bridge the seam with adapters** converting every editor interface both ways. Large, fragile, and
  entirely thrown away once AgentForge moves.
- **Defer until later.** Leaves seven published packages unused and the extraction half-finished.

## The namespace map

The editors renamed wholesale. ⛔ **The services SPLIT by dependency, so one old namespace lands in
three places** — a blind find-and-replace produces code that does not compile, or a plausible import
that resolves to the wrong assembly.

| From | To |
|---|---|
| `…LayeredEditors.Abstractions` | `…ScopedEditors.Abstractions` |
| ⤷ except `Dialogs/DialogMessage` | `…AppServices.Abstractions.Dialogs` |
| `…LayeredEditors.ViewModels` | `…ScopedEditors.ViewModels` |
| `…LayeredEditors.Avalonia` | `…ScopedEditors.Avalonia` |
| `…LayeredEditors.Avalonia.Services` → the `I*` contracts, `ShareOutcome` | `…AppServices.Abstractions` |
| ⤷ `ShellLauncher`, `DefaultEnvironmentProvider`, `DefaultShareService` | `…AppServices` |
| ⤷ `AvaloniaDialogService` | `…AppServices.Avalonia` |
| `…Diagnostics` | `…AppServices.Avalonia` |
| `…Diagnostics.Logging` (`BucketedRollingFileSink` only) | `…AppServices.Logging` |
| `…Diagnostics.Dialogs.NativeErrorDialog` | `…AppServices.Dialogs` |

## The breaking service APIs

`IShellLauncher`'s four members become `ValueTask<LaunchResult>` taking a `CancellationToken`, over
`LaunchStatus { Succeeded, Unsupported, NotFound, Denied, Cancelled, Failed }`. A site that only
branches on success reads `result.Succeeded` and stays one line. ⚠ **The sites that need judgment are
the ones that called `RevealInFileManager` or `LaunchUrl` and ignored a `void`:** `Unsupported` means
*hide the affordance*, not *report an error*, and mapping every non-success to an error message ships
a worse UI than the `void` it replaced. `IShareService` is likewise `ValueTask` + token, and
`ShareOutcome` gains `Cancelled`. Diagnostics arrive through a package-defined `DiagnosticSink`
delegate supplied at construction — **there is no logger dependency and it must not become one.**

⚠ `AgentForge.Avalonia.Shell`'s public constructor takes `IDialogService` and `IShareService`, so this
migration reaches its **public** surface, not only ClaudeForge's call sites.

## ⚠ Four `avares://` URIs that break SILENTLY

The assembly name changed with the rename, so every `avares://LayeredEditors.Avalonia/…` must become
`avares://ScopedEditors.Avalonia/…`.

| Site | What |
|---|---|
| `src/ClaudeForge/App.axaml:15` | `StyleInclude` → `SemiBundle.axaml` |
| `src/ClaudeForge/App.axaml:163` | `AppMonoFontFamily` → `#JetBrains Mono NL` |
| `src/ClaudeForge/App.axaml:176` | `AppMonoLigaturesFontFamily` → `#JetBrains Mono` |
| `src/ClaudeForge.Avalonia/Behaviors/CodeInline.cs:37` | `#JetBrains Mono NL` |
| `src/ClaudeForge/App.axaml:12` | a stale **comment** naming the old path |

⛔ **A bad `avares://` path falls back to a default face rather than throwing.** No exception, no
build error, no failing test — the app renders in the wrong font and every gate stays green. ⓘ The
fonts did travel: the published `ScopedEditors.Avalonia.dll` is 1,320,448 bytes and carries the
`!AvaloniaResources` blob with every face and `OFL.txt`.

## The F12 windows: relocated, and rewired by hand

`LiveLogWindow`, `LiveTailWindow`, `HeaderLink` and `LiveLogWindowSink` are **held back** from the
packages, and ClaudeForge calls them as live shipped code — F12 (live log) and Shift+F12
(config-file event tail):

| Site | Uses |
|---|---|
| `src/ClaudeForge/Program.cs:124–126` | `EnableEventTailWindow`, `EventTailWindowTitle`, `EventTailLaunchLabel` |
| `src/ClaudeForge/Views/MainWindow.axaml.cs:287` | `AvaloniaDiagnostics.ToggleEventTailWindow()` |
| `src/ClaudeForge/Views/MainWindow.axaml.cs:297` | `AvaloniaDiagnostics.ToggleLiveLogWindow()` |

The package's `AvaloniaDiagnostics` ships **reduced**: those toggles, the five options, the window
construction and — decisively — **the wiring that fed both windows** are gone. Measured against the
**published** `AppServices.Avalonia` `2026.3.923`, not the local source, which is the old full
version:

- ✅ **Everything the windows READ survived.** `BucketedRollingFileSink.CurrentFilePath` and
  `AvaloniaDiagnostics.CurrentLogFilePath` / `CurrentEventLogFilePath` are public. `LiveLogWindow`'s
  only reach outside the cluster is that property; `LiveLogWindowSink`'s references to
  `AvaloniaDiagnostics` are doc-comment `cref`s, not code.
- ⛔ **Nothing FEEDS them any more.** Both windows are push-fed — `LiveLogWindowSink` pushes each log
  event, `LiveTailWindow.Enqueue(line)` each config event — and `AvaloniaDiagnosticsOptions` has
  **no hook for extra sinks**: no `AdditionalSinks`, no configure callback. Moved as-is, both windows
  open and stay empty. **Nothing fails.**

So the cluster moves into `src/ClaudeForge` and ClaudeForge supplies the wiring itself:

- **F12:** immediately after `AvaloniaDiagnostics.ConfigureLogging(...)`, wrap the global logger in a
  Serilog sub-logger that also writes to `LiveLogWindowSink`. This relies on `ConfigureLogging`
  assigning `Serilog.Log.Logger` — the local source does (`AvaloniaDiagnostics.cs:163`), and the
  package's IL references `set_Logger`. ⚠ **That is strong evidence, not proof**: a metadata string
  shows a reference, not an observed call. Step 5 proves it at runtime.
- **Shift+F12:** a ClaudeForge wrapper that forwards each event to the package's
  `AvaloniaDiagnostics.EnqueueEvent` (which still writes the event **file**) **and** to the tail
  window.
- **Toggles and the three removed options** become ClaudeForge's own.

⚠ **Two risks in the F12 wrap, both silent if missed:**

- **Shutdown flush.** An outer logger composed with `WriteTo.Logger(inner)` does not own `inner`, so
  `Log.CloseAndFlush()` may flush only the outer — **losing the tail of the rolling log file**, which
  is precisely the part a crash report needs. The inner logger must be flushed explicitly.
- **Order.** Anything that captured `Log.Logger` before the wrap — a `static readonly` `ForContext`
  field initialised early — keeps the inner logger, and its events **bypass F12**. The wrap goes
  directly after `ConfigureLogging`, before anything else can capture it.

⭐ **Decided: the wrap stays local.** It works around a hook the package lacks, and an upstream
`AvaloniaDiagnosticsOptions` sink or configure hook in `Bennewitz.Ninja.AppServices` would be
cleaner — but that is another repository's release, and this change would wait on it. The local
wrap needs nothing from anyone, and the two risks above are what it costs.

## ⛔ Guards keyed on the family — the riskiest part of this change

Deleting a family this repository guards heavily is not just deleting projects. Each of these keys
on it by name, and each must be handled **deliberately**:

| Where | Measured | What happens on delete |
|---|---|---|
| `ClaudeForge.slnx` | 7 entries | `BuildFilePathIntegrityTests` requires disk and solution to agree |
| `AssemblyInfo.InternalsVisibleTo.cs` | 7 grants | Dead grants; `SharedFriendGrantsTests` guards the file's shape |
| `Directory.Build.targets:116` | `_PackableSharedProject` selector | **Remove the line.** Left in place it matches nothing today and silently switches any future `LayeredEditors.*` project into package mode |
| `PackageMetadataTests` | 3 mentions | Asserts the selector and the packable set agree **in both directions** |
| `AssemblyLayeringTests` | 4 mentions, incl. **two globs** (lines 79, 202) | **Remove both globs** — and add the files-scanned > 0 assertion it lacks, or the neutral-layer scan can empty without anyone noticing |
| `nuget.config` | 2 mappings | Per decision 5 |
| PublicSurface baselines | 5 files | Delete; regenerate `AgentForge.Avalonia.Shell.txt` and `AgentForge.Sdk.txt` |

⛔⛔ **And NINE architecture tests name the family.** They were measured one by one, and they are
**not** one hazard. An earlier draft of this plan asserted that several "almost certainly" scan the
theme files and would pass vacuously — reasoning, not measurement, and it pointed at the wrong
failure mode. Measured:

| Kind | Tests | The hazard |
|---|---|---|
| **Type consumers** — they `using` the severity enum and converters | `AppSeverityTokenCoverageTests`, `SeverityTintStaysLegibleTests` | None beyond the namespace map, which repoints them like any other test file |
| **A hard-coded path or an expected count** — these fail **loudly** | `DangerSurfaceMarkupTests` (its expected wrapper count includes the `LayeredEditors.Avalonia` copy), `NoDeadBrushTokensTests` (lists `src/LayeredEditors.Avalonia/Converters/AppSeverityToBrushConverter.cs`) | ⛔ **The fix that goes green is to weaken the guard** — lower the count, drop the path |
| **A false failure** | `ThemeResourceIntegrityTests` — resources declared in `EditorColors.axaml` move into the package, so ClaudeForge's references to them look unresolved while resolving fine at runtime through `SemiBundle.axaml` | ⛔ **The lazy fix is a hand-maintained allow-list**, which only ever grows |
| **A dead glob with no premise check** — the one genuinely silent case | `AssemblyLayeringTests` (`LayeredEditors.*.csproj` at line 79, `LayeredEditors.*.dll` at line 202; no files-scanned assertion) | The family's share of the scan becomes empty and nothing says so |
| **Named only in comments** | `NoLiteralMonospaceFontStackTests`, `PublicSurfaceBaselineTests`, `SingleTargetFrameworkTests` | Low — confirm each still scans a non-empty set |

Three rules govern step 7:

1. ⛔ **When a count or path guard reddens, lowering the expectation to match is never the first move.**
   Establish where the invariant is now enforced — here, or in the `ScopedEditors` repository — and
   only then adjust. If it is enforced nowhere, record the gap rather than hide it.
2. ⛔ **`ThemeResourceIntegrityTests` learns the package's resource keys from the consumed package**, not
   from a hand-maintained list. If that proves infeasible, stop and ask — an allow-list is a decision,
   not a fix.
3. **Every retained scan asserts files scanned > 0**, and every retained guard is **canaried**: plant
   the violation it exists to catch and watch it redden, because a guard retargeted at the wrong files
   still passes.

## Package mode, and why CI will be red on one job

`Published Version` builds against the published packages at `SharedPackageVersion`. The published
`AgentForge.*` packages still depend on `Bennewitz.Ninja.LayeredEditors.*` — the old types — so in
package mode ClaudeForge's new source meets AgentForge's old types, and the job **fails to compile by
construction** until a new `packages-v*` is cut from the merged result **and** the pin is bumped.

⭐ **The release is two halves** — publish, then the pin that consumes it — as `2026.3.921` proved.
Expect this one red on the PR; do not read it as a defect. It clears only after merge, so the PR
body says so.

## ⛔ The trim gate is vacuous on `2026.3.923`

All seven `2026.3.923` assemblies shipped **without** `[AssemblyMetadata("IsTrimmable","True")]`.
Measured by reading the DLLs out of the packages, with a control: the local `AgentForge.Core.dll`,
built under `src/Directory.Build.props`, carries the mark, so the probe can say yes — and for the
packages it says no. The extraction never read that props file, which is where this repository sets
`IsTrimmable` and `EnableTrimAnalyzer` for everything under `src/`.

ClaudeForge publishes with `TrimMode=partial` (`src/ClaudeForge/ClaudeForge.csproj:132`), which trims
**only** assemblies carrying that mark. On `.923` the seven are kept whole, so:

- ⛔ **A trimmed publish reporting zero IL diagnostics proves nothing about them.** ILLink is not
  trimming them, so a clean result cannot tell "analysed and clean" from "never looked". That is
  `00004`'s step 9 and this plan's first draft, both of which would have passed.
- ⛔ **This change would REMOVE trim coverage the code has today.** Under `src/` it is marked and
  analysed on every publish; consumed from `.923` it is neither — and nothing fails.
- ⓘ It is not a runtime risk. Keeping an assembly whole is the safe direction, which is why `.923` is
  fine to build against in the meantime.

**A fixed `2026.3.924`**, carrying the mark, is due 2026-09-24 from both package repositories. So
steps 1–8 run on `.923`, and step 9 moves the pin to `.924` before the gate is claimed. ⚠ **If `.924`
has not been published when the rest is done, stop and ask** — merging on `.923` would ship the
coverage loss, and waiting is a decision, not a fix.

⚠ **`dotnet msbuild -getProperty:IsTrimmable` is the wrong check** — it reads this project, not the
package. Read the mark off the DLL in the package cache.

## Scope

**In:** the 134 files; the namespace map; the four `avares://` URIs and the stale comment; the
service API migration, including `AgentForge.Avalonia.Shell`'s public constructor; relocating and
rewiring the F12 cluster; deleting the five projects and two test projects; every guard and config
row in the table above; the nine architecture tests; the "eleven" → "six" prose; moving the pin to `2026.3.924`.

**Out:** `JsonC` — untouched. Repointing DiffView. Changing what the seven packages contain — they
are published and immutable. The parked `feat/agentforge-opencodeforge` branch — OpenCodeForge also
consumes this family and will diverge further; recorded, not fixed. Cutting the breaking
`packages-v*` release itself, which follows merge.

## Steps

1. **Add the seven `PackageReference`s at `2026.3.923` and the `nuget.config` patterns.** *Proves it:*
   a restore from a clean `NUGET_PACKAGES` resolves all seven from nuget.org with no PAT.
2. **Apply the namespace map across all seven consumers**, three-way split included, and remove
   their `LayeredEditors` `ProjectReference`s. *Proves it:* the **solution** compiles, tests included.
   ⚠ **Review the diff, not the build** — a bulk replace over-fires silently where the result still
   compiles. Report counts **per pattern** against the 134 measured above.
3. **Migrate the service call sites and `AgentForge.Avalonia.Shell`'s public constructor.**
   *Proves it:* every site that ignored a `void` is listed in the commit message with the branch it
   now takes.
4. **Fix the four `avares://` URIs and the stale comment.** *Proves it:* ⛔ **not a grep, and not a
   screenshot.** A fallback lands on another monospace face that looks almost identical, so the check
   is the **family name of the typeface actually resolved** for `AppMonoFontFamily` and
   `AppMonoLigaturesFontFamily` at runtime — `JetBrains Mono NL` and `JetBrains Mono`. ⭐ **Control
   first:** point one URI at a path that does not exist and confirm the check reports a different
   family — a check that says `JetBrains Mono` either way proves nothing. ⚠ In the real app, not the
   headless rig: `UseHeadlessDrawing=true` is a stub font stack, so the answer there would be vacuous.
5. **Relocate the F12 cluster and rewire it.** *Proves it:* in a real run, F12 shows live log events
   and Shift+F12 shows config-file events **as they happen** — windows that open is not the check,
   windows that **fill** is. ⚠ And the rolling log file's final lines survive a clean shutdown.
   ⛔ **The cluster's tests move with it.** Three files in `tests/LayeredEditors.Avalonia.Diagnostics.Tests`
   cover code that stays — `LiveLogWindowTests` (8 references to the cluster), `LiveLogWindowKeyTests` (5)
   and `AccessibilityCoverageTests` (3) — so they move into `tests/ClaudeForge.Tests` rather than being
   deleted with their project. Deleting them would drop the live-log window's behaviour, keyboard and
   **accessibility** coverage, and nothing would go red. *Proves it:* all three pass in their new home.
6. **Delete the five projects and two test projects**, with every row of the guard table.
   *Proves it:* `git grep -n LayeredEditors` over the **whole tracked tree** finds nothing outside an
   explicit, reasoned exclusion list. ⚠ `src/` and `tests/` are not enough — **11** tracked files
   outside them name `src/LayeredEditors` paths, including `AGENTS.md` (the enforceable document),
   `CONTRIBUTING.md` and `Directory.Build.props`. Live documents are updated. ⛔ **Historical records
   are NOT rewritten** — `plans/`, the history in `PROGRESS.md`, and dated reports such as
   `docs/RETEST-FINDINGS.md` and `docs/theme-audit-report.md` describe the tree as it was, and a
   "fixed" path there would make them false. Each exclusion is listed with its reason.
   ⚠ **The remaining test files are deleted only once their subject is confirmed covered elsewhere** —
   `BucketedRollingFileSinkTests`, `NativeErrorDialogTests` and `SerilogAvaloniaSinkTests` test code now in
   `AppServices`, and `tests/LayeredEditors.Avalonia.Tests` tests code now in `ScopedEditors`; each needs an
   equivalent in that repository first (rule 1). ⭐ *Proves it:* the suite's **total** before and after
   reconciles — every test that disappears is accounted for, by name, as covered in a package repository.
7. **Resolve the nine architecture tests** per the three rules above. *Proves it:* a written outcome per
   test; each retained scan asserts files scanned > 0; ⭐ **each retained guard canaried**.
8. **Regenerate the PublicSurface baselines** and read the diff before promoting. *Proves it:* only
   `AgentForge.Avalonia.Shell.txt` and `AgentForge.Sdk.txt` change, and every changed line is a
   `LayeredEditors` → `ScopedEditors` / `AppServices` type.
9. **Move the pin to `2026.3.924`** once published — see *The trim gate is vacuous on `2026.3.923`*.
   *Proves it:* the `IsTrimmable` mark read **off each of the seven DLLs in the package cache**; ⚠ purge
   `~/.nuget/packages/bennewitz.ninja.{appservices,scopededitors}*` first, since the cache never
   re-extracts a version it already holds.
10. **Full gate.** *Proves it:* `dotnet build` 0 warnings; the suite green with the **total** compared
   and reporting assemblies counted against `find tests -name '*.csproj'`; a Release `win-x64`
   trimmed publish with zero IL diagnostics — ⭐ **and each of the seven DLLs in
   `src/ClaudeForge/obj/Release/net10.0/win-x64/linked/` is SMALLER than its copy in the package
   cache**, which is the only observation that proves ILLink actually trimmed, and therefore
   analysed, them. Byte-identical means kept whole, and the zero says nothing. ⚠ **Not the publish
   output**: `PublishSingleFile=true` (`ClaudeForge.csproj:100`) bundles every assembly into one
   executable, so there are no DLLs there to compare — a check written against it could never be
   carried out. ⭐ **Control first**, so the observation is known to be able to say yes: a marked
   in-repo assembly shrinks there — measured 2026-09-23, `AgentForge.Core.dll` 1,979,392 →
   1,915,904 bytes. CI green on every job **except** `Published Version`, red by construction until
   the post-merge release.
