# 00004 — ClaudeForge consumes ScopedEditors and AppServices from nuget.org

> Status: **approved 2026-09-23**. Supersedes nothing. Stage two of
> `Bennewitz.Ninja.Templates` [`plans/00002`](https://github.com/JanusMael/Bennewitz.Ninja.Templates/blob/main/plans/00002-layerededitors-becomes-two-package-repositories.md).

The four **ClaudeForge** projects stop importing `Bennewitz.Ninja.LayeredEditors.*` and consume the
seven published ids instead — `Bennewitz.Ninja.ScopedEditors.{Abstractions,ViewModels,Avalonia}` and
`Bennewitz.Ninja.AppServices{,.Abstractions,.Logging,.Avalonia}`, all at `2026.3.923` on nuget.org.

⛔ **This plan does NOT delete the `LayeredEditors` projects, and cannot.** That is the single most
likely thing to be assumed from stage two's name. `00002`'s *After this plan* section says stage two
is *"delete the moved projects, consume the published packages"*; **the first half is wrong**, and
the session that wrote it retracted it after measuring. `AgentForge.*` still imports those
namespaces, and `AgentForge.*` is out of scope by the maintainer's decision:

| Still importing `Bennewitz.Ninja.LayeredEditors.*` | Files |
|---|---|
| `src/AgentForge.Avalonia.Shell` | **20** |
| `src/AgentForge.Sdk` | 1 |
| `tests/AgentForge.Sdk.Tests` | 1 |

Deleting the projects would break the build. So this plan **repoints ClaudeForge and leaves the
source standing**, which is a deliberate, temporary duplication — see *The coexistence this creates*.

## What is in scope, measured

| Project | Files importing a `LayeredEditors` namespace |
|---|---|
| `src/ClaudeForge` | 49 |
| `tests/ClaudeForge.Tests` | 61 |
| `src/ClaudeForge.Avalonia` | 1 |
| `tests/ClaudeForge.Avalonia.Tests` | 1 |
| | **112** |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **ClaudeForge only.** `AgentForge.*` is untouched and keeps its `ProjectReference`s | The maintainer's scope. `AgentForge` is revisited after, and widening now would make this change un-reviewable |
| 2 | **The `LayeredEditors` projects stay in `src/`**, unmodified | 22 files outside ClaudeForge still need them. Deleting is stage three |
| 3 | **ClaudeForge keeps the LOCAL `LayeredEditors.Avalonia.Diagnostics` for the F12 windows**, and consumes `AppServices.*` for everything else | The package ships `AvaloniaDiagnostics` **reduced**; the members ClaudeForge calls are gone from it. See *What cannot move* |
| 4 | **Migrate the breaking service APIs properly, not with adapters** | A shim preserving the old `void`/`bool` shape would discard exactly the information the reshape exists to add, and would have to be unpicked in stage three |
| 5 | **`nuget.config` gains explicit patterns** for `Bennewitz.Ninja.AppServices*` and `Bennewitz.Ninja.ScopedEditors.*` on `nuget.org` | They would resolve anyway via the catch-all `*`, but relying on a catch-all for seven ids that MUST come from nuget.org leaves the graph silent about intent. ⚠ Patterns are matched **most-specific-wins**, so these must not be added to `github` |
| 6 | **The five `LayeredEditors` PublicSurface baselines stay** | They pin projects this plan does not change. Deleting them would drop coverage while the code is still shipped |
| 7 | **The five `LayeredEditors` projects keep `IsPackable=true`, and a guard makes that temporary** | Stopping the packing would publish an `AgentForge.Avalonia.Shell` whose dependencies do not exist. The duplication is accepted and a tripwire ends it — see below |

⛔ **Decision 4 is where the real work is, and it is not mechanical.** `IShellLauncher`'s four
members become `ValueTask<LaunchResult>` taking a `CancellationToken`, over
`LaunchStatus { Succeeded, Unsupported, NotFound, Denied, Cancelled, Failed }`. A call site that
only branches on success reads `result.Succeeded` and stays one line. ⚠ **The ones that need
judgment are the sites that called `RevealInFileManager` or `LaunchUrl` and ignored a `void`:**
`Unsupported` means *hide the affordance*, not *report an error*, and a migration that maps every
non-success to an error message ships a worse UI than the `void` it replaced. `IShareService` is
likewise `ValueTask` + token, and `ShareOutcome` gains `Cancelled`. Diagnostics arrive through a
package-defined `DiagnosticSink` delegate supplied at construction — **there is no logger dependency
and it must not become one.**

### Alternatives dismissed

- **Adapter shims preserving the old signatures.** One file to change instead of dozens — but it
  throws away `Unsupported`, which is the whole reason the API changed, and stage three would have
  to remove it anyway.
- **Do AgentForge in the same pass.** Removes the coexistence entirely and is the cleaner end state
  — but it is explicitly out of scope, and it would put ~134 files and two families in one change.
- **Delete the `LayeredEditors` projects and let AgentForge consume the packages transitively.**
  That is not "out of scope", it does not compile: the packages are a *rename*, so AgentForge's
  imports resolve to nothing until they are rewritten too.

## The namespace map

The editors renamed wholesale. ⛔ **The services SPLIT by dependency, so one old namespace lands in
three places** — a blind find-and-replace produces code that does not compile, and worse, a
plausible-looking import that resolves to the wrong assembly.

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

## ⚠ Four `avares://` URIs that break SILENTLY

The assembly name changed with the rename, so every `avares://LayeredEditors.Avalonia/…` in
ClaudeForge must become `avares://ScopedEditors.Avalonia/…`.

| Site | What |
|---|---|
| `src/ClaudeForge/App.axaml:15` | `StyleInclude` → `SemiBundle.axaml` |
| `src/ClaudeForge/App.axaml:163` | `AppMonoFontFamily` → `#JetBrains Mono NL` |
| `src/ClaudeForge/App.axaml:176` | `AppMonoLigaturesFontFamily` → `#JetBrains Mono` |
| `src/ClaudeForge.Avalonia/Behaviors/CodeInline.cs:37` | `#JetBrains Mono NL` |
| `src/ClaudeForge/App.axaml:12` | a stale **comment** naming the old path |

⛔ **A bad `avares://` path falls back to a default face rather than throwing.** Nothing fails: no
exception, no build error, no test. The app renders in the wrong font and every gate stays green.
This is the item most likely to be missed and the least likely to be noticed afterwards, which is
why it gets its own verification step. ⓘ The fonts **did** travel — the published
`ScopedEditors.Avalonia.dll` is 1,320,448 bytes and carries the `!AvaloniaResources` blob with every
face and `OFL.txt`, confirmed by the session that published it.

## ⛔ What cannot move: the F12 diagnostics windows

`LiveLogWindow`, `LiveTailWindow`, `HeaderLink` and `LiveLogWindowSink` are **held back** from the
first release and stay in this repository. `AvaloniaDiagnostics` therefore ships **reduced**:
`ToggleLiveLogWindow`, `ToggleEventTailWindow`, the sink wiring, and the `EnableLiveLogWindow` /
`LiveLogWindowTitle` / `EnableEventTailWindow` / `EventTailWindowTitle` / `EventTailLaunchLabel`
options are all gone from the package.

**ClaudeForge calls them today**, as live shipped code — not hypothetically:

| Site | Uses |
|---|---|
| `src/ClaudeForge/Program.cs:124–126` | `EnableEventTailWindow`, `EventTailWindowTitle`, `EventTailLaunchLabel` |
| `src/ClaudeForge/Views/MainWindow.axaml.cs:287` | `AvaloniaDiagnostics.ToggleEventTailWindow()` |
| `src/ClaudeForge/Views/MainWindow.axaml.cs:297` | `AvaloniaDiagnostics.ToggleLiveLogWindow()` |

That is the F12 live-log window and the Shift+F12 config-event tail — a real debug affordance. So
ClaudeForge keeps its **local** `LayeredEditors.Avalonia.Diagnostics` reference for these, and takes
`AppServices.Logging` / `AppServices.Avalonia` for everything else. ⚠ **Both must not be referenced
in a way that puts two `AvaloniaDiagnostics` types in one graph** — step 4 is where that is proven,
not assumed.

## The coexistence this creates

This pass leaves **three** copies of this code reachable in one repository, not two:

1. ClaudeForge → the **published packages**, for editors and services.
2. ClaudeForge → the **local** `LayeredEditors.Avalonia.Diagnostics`, for the F12 windows.
3. `AgentForge.*` → the **local** `LayeredEditors.*` projects, entirely.

⚠ **This is tolerable briefly and rots if it lasts** — two copies of the same types drift the moment
either side is edited, and nothing here fails when they do. Stage three (repoint `AgentForge`, delete
the projects) is what ends it, and it should follow closely rather than being left open-ended.

## The five `LayeredEditors` projects keep packing — and a guard makes that temporary

**Decided.** All five stay `IsPackable=true`, so the next `packages-v*` tag keeps publishing
`Bennewitz.Ninja.LayeredEditors.*` to GitHub Packages alongside the same code on nuget.org as
`ScopedEditors` / `AppServices`. That is two homes for one codebase, which `00002`'s decision 2
exists to prevent — accepted here deliberately, because the alternative is worse.

⛔ **Stopping the packing would ship an UNRESTORABLE package.** `AgentForge.Avalonia.Shell` is itself
`IsPackable=true` and `ProjectReference`s three of them, so those are **hard package dependencies**.
Read out of the `2026.3.921` artifact's own `.nuspec`, not inferred:

```
Bennewitz.Ninja.AgentForge.Avalonia.Shell 2026.3.921
  -> Bennewitz.Ninja.LayeredEditors.Avalonia.Services  2026.3.921
  -> Bennewitz.Ninja.LayeredEditors.Avalonia           2026.3.921
  -> Bennewitz.Ninja.LayeredEditors.ViewModels         2026.3.921
```

`IsPackable=false` does not remove that edge — it leaves it pointing at a version nobody publishes,
on a feed where a version can never be corrected. ⓘ `IsPackable` is the only lever available:
`scripts/Publish-Packages.ps1` pushes **every** `.nupkg` it packs
(`Get-ChildItem -Filter '*.nupkg'`), with no id list to exclude one from.

⭐ **So the duplication is accepted, and a guard stops it outliving its reason.** A new test asserts
that at least one project outside ClaudeForge still imports a `Bennewitz.Ninja.LayeredEditors`
namespace — the condition that *justifies* keeping these five. The moment `AgentForge` is repointed
the justification is gone, the test **fails**, and its message says to delete the five projects,
drop them from the `_PackableSharedProject` selector and remove their `nuget.config` mappings.

⛔ **It is a tripwire, not a ratchet, and it must be canaried in BOTH directions** — this repository
has shipped a check whose success condition was unreachable, and several that could never fail:

- **It passes today.** 22 files import those namespaces (`AgentForge.Avalonia.Shell` 20,
  `AgentForge.Sdk` 1, `AgentForge.Sdk.Tests` 1).
- **It fails when the reason expires** — prove it by temporarily rewriting those imports and watching
  it redden, not by reasoning about the assertion.
- ⚠ **It must assert its own premise**, like `NeutralLayerDefaultsTests` does: a scan that finds zero
  files to examine passes vacuously and would report the duplication as justified forever.
- ⚠ **Strip comments before matching.** A comment naming `LayeredEditors` would satisfy the scan
  while no code imported anything — the exact failure this repository has already hit, where a text
  search matched the comment documenting a thing rather than the thing.

⚠ **When the guard fires, the fix is to DELETE the projects — never to weaken the guard**, and never
to add an exemption. It has one job and it only does it once.

## Scope

**In:** the 112 ClaudeForge files; the namespace map; the four `avares://` URIs and the stale
comment; the `IShellLauncher` / `IShareService` / `ShareOutcome` migration; the `DiagnosticSink`
wiring; `nuget.config` patterns; the `ClaudeForge` and `ClaudeForge.Avalonia` csproj references; the duplication guard;
whatever the suite needs to stay green.

**Out:** `AgentForge.*` and `JsonC` — untouched. Deleting the `LayeredEditors` projects. Repointing
DiffView. Any change to the F12 windows themselves. Any change to what the seven packages contain —
they are published and immutable.

## Steps

1. **Add the seven `PackageReference`s and the `nuget.config` patterns**, and repoint the two `src/`
   csproj. *Proves it:* a restore from a clean `NUGET_PACKAGES` resolves all seven from nuget.org
   with no PAT, and `dotnet list package` shows them at `2026.3.923`.
2. **Apply the namespace map**, three-way split included. *Proves it:* `src/ClaudeForge` and
   `src/ClaudeForge.Avalonia` compile. ⚠ **Review the diff, not the build** — a bulk replace
   over-fires silently where the result still compiles, and one pattern can corrupt another's
   replacement. Report counts **per pattern** against the 112 measured above.
3. **Migrate the service call sites.** *Proves it:* the suite is green, and every site that ignored
   a `void` is listed in the commit message with the branch taken — `Unsupported` hides its
   affordance rather than reporting an error.
4. **Wire the F12 windows against the local Diagnostics copy.** *Proves it:* F12 and Shift+F12 still
   open their windows in a real run, and exactly one `AvaloniaDiagnostics` type is in the graph — read
   from the build, since two would be an ambiguity the compiler may resolve silently in favour of the
   project.
5. **Fix the four `avares://` URIs and the stale comment.** *Proves it:* ⛔ **not a grep.** Launch the
   app and confirm the mono font actually renders — the failure mode is a silent fallback, so only
   observing the glyphs proves it. `grep -rn 'avares://LayeredEditors' src/ClaudeForge*` returning
   nothing is necessary and not sufficient.
6. **Regenerate the PublicSurface baselines that legitimately changed**, and diff before promoting.
   *Proves it:* the five `LayeredEditors.*.txt` baselines are **unchanged** — this plan does not
   touch those projects, so a change there means something moved that should not have.
7. **Add the duplication guard** per decision 7. *Proves it:* green today with its premise asserted (files scanned > 0, comments stripped), **and red** when the 22 `AgentForge` imports are temporarily rewritten — both observed, then the rewrite reverted.
8. **Full gate.** *Proves it:* `dotnet build` 0 warnings; the suite green with the **total** compared,
   not the passed count, and the reporting assemblies counted against `find tests -name '*.csproj'`;
   a Release `win-x64` trimmed publish with zero IL diagnostics; CI green on all three platforms.
