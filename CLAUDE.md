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
| **OpenCodeForge** | OpenCode (`opencode.json` / `opencode.jsonc`) and its TUI (`tui.json`) | `src/OpenCodeForge` |

Both are Avalonia apps on .NET 10, published self-contained and trimmed per RID.

The repository keeps its original name. That is deliberate — ClaudeForge shipped first and is
the published product; the second app grew out of it rather than replacing it.

## How the assemblies layer

Four families, and the prefix tells you the layer:

- **`LayeredEditors.*`** — the schema-driven editor library. Knows about JSON Schema, property
  editors, scopes and layered values. Knows nothing about Claude or OpenCode.
- **`AgentForge.*`** — product-neutral agent-configuration machinery: the SDK, the settings
  core, backup/restore, artifact resolution, and the Avalonia shell (nav, search, save,
  Essentials cards). Anything here must make sense for *both* products.
- **`ClaudeForge.*` / `OpenCode.*`** — the product-specific halves: schema tables, danger
  tables, page layouts, product-shaped editors.
- **`ClaudeForge` / `OpenCodeForge`** — the two app assemblies. Each is an ordinary consumer of
  everything above.

**The rule that matters: the two products never reference each other, and nothing
product-specific is referenced by `AgentForge.*`.** `AssemblyLayeringTests` enforces it over
both `src/` and `tests/`, by scanning csproj XML as well as by reflection — the compiler omits
unused references from the assembly reference table, so a declared-but-unused bad reference is
invisible to reflection alone, which is exactly the state a violation is in just before someone
depends on it.

Where a product genuinely needs privileged access to neutral internals, the seam is an
`InternalsVisibleTo` grant rather than a reference. An attribute is not a dependency; layering
holds.

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

**A green Debug suite does not mean the apps ship.** Trim analysis only runs on a Release
publish, and an `IL2026` in a JSON helper once broke the Release publish for three phases while
thousands of Debug tests passed over it. Every shipping app needs its own publish:

```bash
dotnet publish src/ClaudeForge    -c Release -r linux-x64 --self-contained true
dotnet publish src/OpenCodeForge  -c Release -r linux-x64 --self-contained true
```

`src/Directory.Build.props` sets `IsTrimmable` for everything under `src/`, which is what gives
ILLink eyesight into the shared libraries. Without it, trim warnings in a shared project are
simply not reported.

Release artifacts for real distribution go through `src/publish/publish.ps1` — which today
builds **ClaudeForge only**. OpenCodeForge has no release pipeline yet; its only Release publish
is the CI trim check above, which passes the RID and `--self-contained` on the command line.
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

Referenced from `AGENTS.md` and from both refresh scripts. **This is the order:**

```
memory cache  ->  HTTPS fetch (+ strip, + overlay)  ->  bundled resource (+ strip, + overlay)
```

**There is no disk cache and no empty fallback.** If neither source answers, the load throws
`SchemaUnavailableException` — because an empty JSON Schema permits *everything*, so returning
one would not degrade validation, it would remove it while every surface kept reporting success.

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

⚠ **Startup blocks on this chain** — it is awaited from `AgentConfigClientCore.OpenAsync`. A
`FetchTimeout` of 3s bounds it (the `HttpClient`'s own timeout is 15s, five times too long to
sit in front of a launch), and a per-instance latch stops probing after one connectivity
failure. ⓘ Measured: a launch builds **two** registries — the window's and each client's — so
each schema is fetched twice and an offline launch pays two timeouts, not one.

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
| Trim-warning baselines and what they mean | [`TRIMMING.md`](./TRIMMING.md) |
| Localization and the resx parity contracts | [`LOCALIZATION.md`](./LOCALIZATION.md) |
| Platform conditionals and path handling | [`PLATFORM.md`](./PLATFORM.md) |
| The two-app plan, phase status, and every spike measurement | [`docs/OPENCODEFORGE-PLAN.md`](./docs/OPENCODEFORGE-PLAN.md) |

Area-specific `AGENTS.md` sidecars sit next to the code they describe — the editor one under
`src/ClaudeForge/ViewModels/Editors/` is the largest.
