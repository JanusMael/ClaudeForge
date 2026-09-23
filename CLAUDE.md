# CLAUDE.md — narrative architecture and prose context

> Audience: a human or agent who needs the *shape* of this repo and the reasoning behind it.
> [`AGENTS.md`](./AGENTS.md) is the companion: operational rules, cross-file invariants, and
> per-task checklists. When the two disagree, `AGENTS.md` is the enforceable one — its rows are
> backed by guard tests. This file carries the narrative and the three reference tables
> `AGENTS.md` cites: **Key conventions**, **Schema loading priority**, and **Debug flags**.

---

## What this repository is

Two desktop applications that edit the configuration of two different coding agents, built over
one set of shared libraries:

| App | Edits | Entry point |
|---|---|---|
| **ClaudeForge** | Claude Code (`settings.json`) and Claude Desktop (`claude_desktop_config.json`) | `src/ClaudeForge` |
| **OpenCodeForge** | OpenCode (`opencode.json` / `opencode.jsonc`) and its TUI (`tui.json`) | ⛔ **not on this branch** |

Both are Avalonia apps on .NET 10, published self-contained and trimmed per RID.

⛔ **This is the RELEASE branch and it holds ONE app.** OpenCodeForge and its two libraries were
removed here by plans/00003 Phase 0, so the ClaudeForge release carries no OpenCodeForge code; they
live on the parked `feat/agentforge-opencodeforge` branch and rejoin after the release, once the
shared packages exist and are published. ⚠ **The two-app narrative below is still the architecture
and still load-bearing** — it is why `AgentForge.*` is product-neutral, and that neutrality is what
the published packages sell. Read it as the design, not as an inventory of this tree.

The repository keeps its original name. That is deliberate — ClaudeForge shipped first and is
the published product; the second app grew out of it rather than replacing it.

## How the assemblies layer

Three families and one standalone in this tree, one family consumed as packages, and the prefix
tells you the layer:

- **`Bennewitz.Ninja.ScopedEditors.*`** — the schema-driven editor library, consumed from
  nuget.org with its services in `Bennewitz.Ninja.AppServices.*`. It was `LayeredEditors.*` in
  this tree until plans/00005 moved it to its own repository. Knows about JSON Schema, property
  editors, scopes and layered values. Knows nothing about Claude or OpenCode.
- **`AgentForge.*`** — product-neutral agent-configuration machinery: the SDK, the settings
  core, backup/restore, artifact resolution, and the Avalonia shell (nav, search, save,
  Essentials cards). Anything here must make sense for *both* products.
- **`JsonC`** — a comment- and formatting-preserving JSONC reader plus an edit-based writer.
  Zero dependencies, not even the BCL beyond the framework. ⭐ **A family of one, and named
  outside `AgentForge.*` deliberately:** nothing in it knows what an agent is, so an
  `AgentForge` prefix would have shipped a package id that overclaims. It was renamed before
  the first publish, because a package id is immutable once pushed.
- **`ClaudeForge.*` / `OpenCode.*`** — the product-specific halves: schema tables, danger
  tables, page layouts, product-shaped editors.
- **`ClaudeForge` / `OpenCodeForge`** — the two app assemblies. Each is an ordinary consumer of
  everything above.

⚠ **Being a family of one costs FOUR edits, in four uncoupled places.** The package-mode reference
switch in the root `Directory.Build.targets` selects the shared projects by family prefix, so
`JsonC` is named there explicitly; `PackageMetadataTests` asserts that selector and the packable
set agree in both directions; `AssemblyLayeringTests` keeps its *own* selector, which had to be
widened or `JsonC` would have silently left the layering scan; and **`nuget.config`'s
`packageSourceMapping`** has to route the id to the private feeds.

⛔ **The rename missed the fourth and only the package canary caught it.** A normal build never
asks for these package ids, so nothing local fails. The symptom is `NU1101 "no packages exist with
this id"` listing only nuget.org, with the real feeds under *"were not considered"* — which reads
like a missing package rather than a mapping gap. ⚠ The broad `Bennewitz.Ninja.*` pattern that
would make this automatic is unavailable: it would also capture the public
`Bennewitz.Ninja.AutoVersioning`, and every credential-free clone would then get a 401 from the
private feed on a package every project references. **Prefer a family prefix for anything new.**

**The rule that matters: the two products never reference each other, and nothing
product-specific is referenced by `AgentForge.*`.** `AssemblyLayeringTests` enforces it over
both `src/` and `tests/`, by scanning csproj XML as well as by reflection — the compiler omits
unused references from the assembly reference table, so a declared-but-unused bad reference is
invisible to reflection alone, which is exactly the state a violation is in just before someone
depends on it.

Where a product genuinely needs privileged access to neutral internals, the seam is an
`InternalsVisibleTo` grant rather than a reference. An attribute is not a dependency; layering
holds.

⭐ **Those grants are no longer per-project.** `AssemblyInfo.InternalsVisibleTo.cs` sits beside
`ClaudeForge.slnx` and is **linked** — never copied — by every project as
`../../AssemblyInfo.InternalsVisibleTo.cs`, relative and forward-slashed so Linux and macOS resolve
it as Windows does. One list that is obviously complete, in place of a dozen that were individually
precise and collectively unknowable.

⛔ **The cost is that `internal` now means SOLUTION-internal.** The file compiles into every linking
assembly, so every grant applies to every assembly. If a member must not be reachable from another
assembly, `internal` no longer expresses that — make it private. ⚠ The names in it are **assembly**
names (`AgentForge.Core`), not the `Bennewitz.Ninja.*` root namespaces; a grant naming the namespace
form compiles, ships, and grants nothing. `SharedFriendGrantsTests` guards the shape, including that
no project declares a grant of its own in **either** spelling — the SDK `<InternalsVisibleTo>` item
or a raw `<AssemblyAttribute>`, both of which were in use before the consolidation.

## The package layer

The six shared projects — the `AgentForge.*` family plus `JsonC` —
are also published as NuGet packages, ids prefixed `Bennewitz.Ninja.`, to this repository's
GitHub Packages feed. See [`plans/00001`](plans/00001-shared-libraries-as-private-nuget-packages.md)
for the reasoning and the measurements.

⛔ **They are PUBLIC, and this document called them "private" until the first publish measured
otherwise.** GitHub Packages inherit the linked repository's visibility, and `JanusMael/ClaudeForge`
is public, so all eleven report `visibility: public` — verified 2026-09-19 against
`user/packages/nuget/…` immediately after `packages-v2026.3.918`. ⓘ Nothing is exposed that
`git clone` does not already give away, which is why the answer was to correct the wording rather
than to restrict the packages.

⚠ **"Private feed" elsewhere in this repo means AUTHENTICATED, and that part is true.** GitHub's
NuGet registry requires a token for reads even on public packages, which is why a credential-free
restore 401s and why `packageSourceMapping` matters. ⛔ **Do not read those passages as a claim
about visibility** — the two properties are independent, and conflating them is what produced the
wrong sentence here. `plans/00001` carries the same wording in its title and is **frozen**; the
correction lives here and in `PROGRESS.md`, never in the plan.

⭐ **There are two reference MODES, and no csproj declares both.** `UseSharedPackages` unset or
`false` — the default, and what every human runs — means `ProjectReference`. `true` means
`PackageReference` at `SharedPackageVersion`, which is what the per-PR canary and the release
consume. One conditional, applied centrally in the **root** `Directory.Build.targets`, so the two
cannot be mixed.

⛔ **Mixing them is the failure the switch exists to make unrepresentable.** A test project that
referenced one shared library by project and another by package would hold two
`AgentForge.Core.dll` in one graph, and MSBuild prefers the project output — so a job named
"package canary" would report success over a suite still exercising project-built code.

⚠ **The switch has to live in the ROOT targets file.** It runs after each csproj declares its
`ProjectReference`s, which rules out `Directory.Build.props`; and the obvious alternative — a
`Directory.Build.targets` under `src/` — is a trap, because MSBuild imports only the CLOSEST one.
Adding it would silently detach every `src` project from the root file, taking the publish strip,
the dead-resx guard and the raw-hex guard with it, and nothing would fail.

**One version, from one source.** `PackageVersion` is assigned from `$(AutoPackageVersion)` in
that same root file — the CalVer stamp AutoVersioning writes into the assemblies — so a package
and the assembly inside it agree by construction rather than by two conventions that happen to
line up. A release pins the whole thing to its **tag** by exporting `BuildTimestamp`; see
[`AGENTS.md`](./AGENTS.md) for why that is the only knob of the three that works.

| Job | How |
|---|---|
| Prove the repo still works in package mode | `pwsh -NoProfile -File scripts/package-canary.ps1` — packs at a throwaway version into `artifacts/localfeed`, restores through a job-local `NUGET_PACKAGES`, then builds, tests and publishes against the packages |
| Publish for real | Push a `packages-v2026.3.914` tag. `release-packages.yml` gates on the suite, resolves the version, and runs `scripts/Publish-Packages.ps1` |
| Check before publishing | `scripts/Publish-Packages.ps1 -PackageVersion <v> -PreflightOnly` |

⛔ **A published package version can never be replaced or re-pushed**, which is why the publish
script refuses on three grounds before the first upload and pushes one package at a time. ⚠ Under
a day-resolution CalVer that also means **one package release per calendar day**: a second on the
same day collides with an immutable version, and the recovery is tomorrow.

⚠ **The tag prefix is deliberately neither app's.** The six serve both products, so riding
ClaudeForge's `v*.*.*` would leave OpenCodeForge unable to publish shared code and tie a library
fix to cutting a full app release.

## Build, run, test

The solution file `ClaudeForge.slnx` is **hand-maintained**. A project missing from it silently
never builds in CI, so `BuildFilePathIntegrityTests` asserts every project on disk is listed.

```bash
dotnet build ClaudeForge.slnx -c Debug
dotnet test  ClaudeForge.slnx -c Debug --no-build
```

Tests are **MSTest**, not xUnit. Passing tests' stdout is hidden unless you pass
`--logger "console;verbosity=detailed"`. The suite is sequential by design — see
`AGENTS.md` on Avalonia.Headless and the global-static seams.

**A green Debug suite does not mean the apps ship.** An `IL2026` in a JSON helper once broke the
Release publish for three phases while thousands of Debug tests passed over it. Every shipping app
needs its own publish:

```bash
dotnet publish src/ClaudeForge    -c Release -r linux-x64 --self-contained true
```

ⓘ There was a second line here for OpenCodeForge; on this branch there is one app to publish.

`src/Directory.Build.props` sets `IsTrimmable` for everything under `src/`, which is what gives
ILLink eyesight into the shared libraries. Without it, trim warnings in a shared project are
simply not reported.

> ⓘ **Corrected 2026-09-13.** This said *"trim analysis only runs on a Release publish"*. It no
> longer does. `src/Directory.Build.props` now also sets `EnableTrimAnalyzer`, so the **Roslyn**
> trim analyser runs on every build of every project under `src/`, in **Debug as well as
> Release** — verified by removing the `McpServersAccessor` cast and watching a plain
> `dotnet build` redden with `IL2026` in both configurations. ⚠ **The warning above still
> stands**, because the two analyses are not the same thing: Roslyn sees one project's own
> source, while **ILLink's whole-program pass — the one that decides what is actually
> removed — still runs only on a publish**, and it is that pass the six-RID matrix measures.
> The property exists because packaging would otherwise delete the Roslyn half entirely: it
> reaches shared code today only because `dotnet publish -p:PublishTrimmed=true` sets a
> **global** property that flows through the app's build graph, and a packaged library is not
> in that graph. See `plans/00001`, work item 4.

Release artifacts for real distribution go through `src/publish/publish.ps1`, which takes the app
by name — `-App ClaudeForge` (the default) or `-App OpenCodeForge`, from the table in
`PublishApps.ps1`. Each app also has its own release workflow: `release.yml` and
`release-opencodeforge.yml`.

> ⓘ **Corrected 2026-09-12.** This paragraph said the script "builds ClaudeForge only" and that
> "OpenCodeForge has no release pipeline yet". Phase 15 shipped both, and the line outlived it —
> verified against the script's own `-App` parameter and `.github/workflows/` rather than
> re-asserted.

⚠ **That script deletes every `bin/` and `obj/` under `src/`**, and the apps write their logs
next to their executable — so run any local diagnosis *before* invoking it.

## Key conventions

Referenced from `AGENTS.md`; the enforceable statements live there, the reasoning here.

- **Compiled bindings everywhere.** Every `DataTemplate` and `UserControl` sets `x:DataType`.
  This is not style: reflection bindings raise `IL2026` under `PublishTrimmed`, and — more
  usefully — with `x:DataType` a renamed bound member becomes a *build error* rather than a
  control that silently renders nothing. It is the strongest correctness guard available for
  AXAML here, because the headless test app is deliberately stripped of the App's resource
  dictionaries and so cannot instantiate views.
- **No ancestor bindings** (`$parent[...]`) in the shared libraries — they resolve by
  reflection and trip the same trim analysis.
- **Theme tokens, never literal colours.** Both apps declare their own palette; a hex literal in
  a shared view ships one theme's colour into the other variant.
- **All user-visible text comes from resx**, including every `AutomationProperties.Name`.
- **Never swallow an exception to make a test pass.** Real bugs become invisible. The sanctioned
  pattern is a narrow `catch` over the exception types a caller can actually act on — see
  `WindowStateService.Load/Save/Delete`.
- **Max six positional parameters**, ratchet-guarded with an allow-list that shrinks rather
  than grows. Past that, take an options record.
- **Immutable-first**: `record` for value-like models, `init` setters, `with` for updates.

## Schema loading priority

Referenced from `AGENTS.md` and from both refresh scripts.

⭐ **The disk cache is the MATERIALISED RESULT, not a tier in a precedence chain.** A launch
resolves once and writes what it resolved; memory is then loaded from that one file:

```
memory cache
  ->  conditional GET          304 -> the cached artifact is already current
                               200 -> strip + overlay -> write artifact + sidecar -> memory
                              fail -> the cached artifact, else extract bundled -> write -> memory
```

So there is exactly **one** path into the memory cache and exactly **one** file that says what the
app validates against — which a user or a bug report can read without re-deriving anything. The
artifact on disk is the resolved document: already stripped, already overlaid, so loading it is a
plain parse.

⛔ **A cached FETCHED artifact is never overwritten by bundled because a launch is offline.** That
would hand the user an *older* schema than the one already on their machine, silently — no error,
no badge change, just different validation rules. A cached **bundled** artifact *is* refreshed when
the build ships different bundled bytes, or upgrading the app would strand the user on whatever the
old build extracted. The sidecar's `source` and `bundledSourceSha256` are what make that
distinction expressible; `overlaySha256` is what notices an overlay edit, since the overlay is
baked into the artifact.

⚠ **A `null` cache directory means NO DISK, and that is the constructor's default** — the same
shape, and the same measured reason, as the `HttpClient` default being OFFLINE. 34 test sites build
a bare registry; a writing default would put every one of them into a real user profile. Production
names a directory **per app**: there is no neutral default, because `~/.claude/cache/schemas` is
Claude's answer and OpenCode's schemas do not belong beneath it. OpenCodeForge's sits beside its
window-state file, deliberately *outside* the roots the disk-footprint page measures, so the app's
own cache is never reported to the user as OpenCode's disk usage.

**There is still no empty fallback.** If nothing can supply the schema, the load throws
`SchemaUnavailableException` — because an empty JSON Schema permits *everything*, so returning one
would not degrade validation, it would remove it while every surface kept reporting success.

⭐ **The overlay and the external-`$ref` strip apply to whichever source wins.** That is what
makes network-first safe, and it is the whole design. `*.overlay.json` siblings carry
hand-curated additions upstream omits — `model.examples` and `model.default`, which promote
`model` to an enum and give the editor a real picker — merged via RFC 7396 JSON Merge Patch.
Only 2 of the 4 schemas have one.

⛔ **The strip is a runtime concern, not just the refresh script's.** Upstream
`opencode-config.json` types four `model` properties with a `models.dev` `$ref`; a copy carrying
one makes schema evaluation throw on save for any config that sets a model. Bundled files are
already stripped by the script, so re-applying is a no-op — one code path for every source is
what stops the two drifting. The strip is line-based, so an inline `$ref` survives it; the
loader verifies afterwards and refuses such a source rather than shipping it.

⚠ **A `null` `HttpClient` means OFFLINE**, and that is the constructor's default. Production
asks for the network by name via `SchemaRegistry.CreateWithNetwork()`. The default is inverted
deliberately: 34 test sites construct a registry without a client, and under network-first every
one of them would otherwise make live outbound calls and resolve schemas against whatever
upstream is serving that day. A production site that forgets simply behaves as the app did
before network-first, which is why this is the safe default.

⛔⛔ **ClaudeForge was that site, for the whole of Phase 13.** `App.axaml.cs` wrote
`SchemaRegistry schemaRegistry = new()` from the initial commit — correct while bundled won
anyway — and network-first updated OpenCodeForge's composition root without touching it. The
shipped app built its pages *and* validated its saves against bundled schemas while this
document described a fetch it never made. Nothing failed, because there was nothing to fail.
`ProductionSchemaRegistryTests` now scans both app assemblies' source for a bare registry;
it is a source scan because a registry deliberately does not expose whether it holds an
`HttpClient`, so reflection cannot tell the two apart.

⚠ **Startup blocks on this chain** — it is awaited from `AgentConfigClientCore.OpenAsync`. A
`FetchTimeout` of 3s bounds it (the `HttpClient`'s own timeout is 15s, five times too long to
sit in front of a launch), and a per-instance latch stops probing after one connectivity
failure. ⭐ **Both apps build exactly ONE registry and hand it to every consumer**, so each schema
is fetched once per launch and an offline launch pays one timeout per schema. Sharing is also
what makes the nav provenance badge speak for save-validation: with separate registries the badge
can report `Fetched` for the pages while the registry the save path validates against has fallen
back to bundled, and no surface anywhere disagrees.

> ⓘ **Corrected 2026-09-13.** This paragraph said OpenCodeForge built **three** — its own inside
> `InitializeAsync`, plus one inside each client it constructed without passing one — and that it
> "does not do it yet". That was accurate when written. `SharedSchemaRegistryTests` now pins both
> halves: that the two clients share an instance, and that `InitializeAsync` adds no third.
> ClaudeForge's composition root has shared one since network-first.

⚠ **This order was bundled-first until 2026-09-09, and the prose said so in four places** —
twice as the stated reason for a test's design, because nothing asserted it. The reversal is
deliberate. Before "fixing" a comment to match the old order, read `GetSchemaAsync`;
`SchemaLoadPrecedenceTests` pins the current behaviour, including that a fetched copy gets the
overlay.

Bundled schemas live in `src/AgentForge.Core/Assets/Schemas/` and are refreshed by
`scripts/refresh-schema.ps1` (or its `.sh` twin, kept in byte parity). They are the offline
fallback, so refreshing them matters for every user who is offline, on a slow link, or behind
something that blocks the fetch — and for the first three seconds of every launch. Two things
that script does which are not obvious:

- **It strips external `$ref`s** — the same rule the loader applies, for the same reason.
- **It compares line-ending-normalised content.** `.gitattributes` sets `* text=auto`, so a
  Windows checkout is CRLF while every download is LF.

ⓘ **There is an in-app check, and it MUTATES.** Both apps' About dialogs carry *Check for schema
updates*; `SchemaRefresher` re-fetches every product whose `SchemaUrl` is `https://` and re-labels
the nav badges in place. Two things it deliberately does not do, both visible in its result line:

- **It does not rebuild the pages.** An `Updated` result means the badge and the tree disagree
  until a reload, because the tree was built from the previous copy. Reloading automatically
  would interrupt unsaved edits from a button whose label says *check*, and the next launch picks
  the new copy up anyway. ⚠ In ClaudeForge, where one registry is shared with both SDK clients,
  save-validation switches immediately even though the tree has not.
- ✅ **It can no longer move a session backwards.** `RefreshAsync` still drops the cached copy
  before re-fetching, but a failed retry now lands on the previously-**fetched** artifact on disk
  rather than on bundled, so the session keeps the newer schema.
- ⛔ **Which is exactly why the refresher asks `NetworkUnavailable` FIRST.** Falling back to a
  fetched artifact means provenance alone can no longer tell "checked, nothing new" from "never
  reached the server" — both end up reporting `Fetched` with an unchanged digest. Reading the
  digest first would report `Unchanged` for a check that never happened, which is the same
  dishonesty as claiming a fetch failed on a section where no request was made, pointing the other
  way. A failed retry is still `Unavailable`, never "up to date" — the distinction is the point.

A product with no upstream (Claude Desktop: `$id` is a bare token, so its descriptor URL is
`bundled://…`) is **omitted from the results**, not reported unchanged, and carries its own badge
tooltip. Saying "the app tried to fetch a newer copy and could not" about a section where no
request was ever made sends the reader after a network fault they do not have.

## Debug flags

Runtime switches parsed by `DebugFlags.Initialize` in `src/ClaudeForge/Services/DebugFlags.cs`.
They configure the app and then it starts normally.

| Flag | Effect |
|---|---|
| `--windows`, `--macos`, `--linux` | Simulate a host platform, for platform-conditional UI |
| `--showAllNew` | Treat every schema property as new, lighting all "✨ NEW" chips |
| `--showInstallBanner` | Force the install-detection banner |
| `--culture <code>` | Force a UI culture (strict: `GetCultureInfo(name, predefinedOnly: true)`) |
| `--simulate-update` | Pretend an update is available |
| `--deep-link <path>` | Navigate to a dotted settings path on launch |
| `--writer <legacy\|jsonc>` | Choose the config writer; `legacy` is a one-release escape hatch |
| `--schema-source <bundled\|fetched>` | Force one branch of the load chain. ⚠ `fetched` is FATAL if the fetch fails — it does not fall back, because a run that silently used bundled would prove nothing |
| `--debug-help`, `--help-debug` | Print the recognised flags and exit |

⚠ **Two-token flags** (`--culture`, `--deep-link`, `--writer`) must advance the loop index
explicitly and validate before assigning. Adding one means touching `Initialize`,
`_deferredWarnings`, `ListActive()`, `ResetForTesting()` and the `--debug-help` text — see the
checklist in `AGENTS.md`.

⚠ **Flag parsing runs BEFORE Serilog is initialised**, because `--culture` affects logging and
culture itself.

## CLI-bypass tools

Conceptually different from debug flags, and deliberately kept out of the table above: these
**do their work and exit without ever starting Avalonia**. They are dispatched in
`src/ClaudeForge/Program.cs` from a loop placed *above* `BuildAvaloniaApp()`.

| Tool | Purpose |
|---|---|
| `--cleanup-restore-sidecars` | Remove sidecar files left by an interrupted restore |

A new tool goes in that loop and returns immediately after dispatching. Starting the UI to run a
one-shot maintenance task would show a window nobody asked for and hold file locks the task needs.

## Where else to look

| Topic | Document |
|---|---|
| Operational rules, cross-file invariants, per-task checklists | [`AGENTS.md`](./AGENTS.md) |
| Avalonia behaviours that cost us time, with measurements | [`docs/AVALONIA-GOTCHAS.md`](./docs/AVALONIA-GOTCHAS.md) |
| **Driving the app through UI Automation for a retest** | [`scripts/retest/README.md`](./scripts/retest/README.md) — harnesses, not tests; nothing here runs in CI. ⛔ Read its **nine** lessons before hand-driving anything: a fixed sleep is not a measurement, `pwsh -File` does not parse PowerShell syntax so an array argument arrives as one string, `catch { continue }` without counting makes a degraded tree look empty, patterns never coordinate clicks, modals are `Window` elements **inside** the main window rather than siblings of it, a result announced only in a clearing status pill has to be measured by its effect, a needle matches the **name** and never the AutomationId, and `Invoke()` can return cleanly having done nothing, and **`~/.claude` is not quiescent** — a control window with no app running showed 8 changes in 25s, so a whole-home before/after diff cannot attribute anything |
| Where the app's automation surface is thin, and what to do about it | [`docs/UIA-AUTOMATION-GAPS.md`](./docs/UIA-AUTOMATION-GAPS.md) — nine gaps plus a proposal for a reusable automation-surface helper. ⚠ Several are **accessibility** gaps wearing automation clothes; read it before concluding a control is missing |
| Trim-warning baselines and what they mean | [`TRIMMING.md`](./TRIMMING.md) |
| Localization and the resx parity contracts | [`LOCALIZATION.md`](./LOCALIZATION.md) |
| Platform conditionals and path handling | [`PLATFORM.md`](./PLATFORM.md) |
| **Where the work stands right now, and what to do next** | [`PROGRESS.md`](./PROGRESS.md) — the resume anchor. Read it first in a fresh session, and reconcile it against `git` rather than trusting it |
| The two-app plan, phase status, and every spike measurement | ⛔ **`docs/OPENCODEFORGE-PLAN.md`, and it is NOT on this branch.** [`plans/00003`](./plans/00003-release-built-from-shared-packages.md) step 0e deleted it here on purpose: the document is entirely *about* OpenCodeForge, so purging references from it would have been incoherent. It lives on the parked `feat/agentforge-opencodeforge` branch and is the record needed when OpenCodeForge is revisited — read it with `git show feat/agentforge-opencodeforge:docs/OPENCODEFORGE-PLAN.md` |
| Why the shared libraries are packages, the two reference modes, and what an immutable feed costs | [`plans/00001`](./plans/00001-shared-libraries-as-private-nuget-packages.md) — approved and implemented; drift goes to `PROGRESS.md`, never into the plan |
| Which YAML front-matter tokens are supported, and which round-trip verbatim | [`docs/YAML-FRONT-MATTER.md`](./docs/YAML-FRONT-MATTER.md) — ⚠ paths in it differ from `main`'s, and there are **two** block-scalar test files on purpose: both sides wrote that coverage independently and the parser here is the union |
| What users see, release by release | [`CHANGELOG.md`](./CHANGELOG.md) — ⚠ ClaudeForge only. OpenCodeForge has a release workflow and no changelog yet. ⛔ **Rebased on `main`'s, not merged with it**: `main` owns the released sections, this branch's `## [Unreleased]` owns only what has not shipped |

Area-specific `AGENTS.md` sidecars sit next to the code they describe — the editor one under
`src/ClaudeForge/ViewModels/Editors/` is the largest.
