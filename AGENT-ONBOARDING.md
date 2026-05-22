# Agent-onboarding guide for ClaudeForge

> Audience: humans (and agents) auditing the methodology behind the agent
> docs.
> Reading order: this file describes *why* the `AGENTS.md` files exist and
> the discipline that keeps them honest. For the actual operational rules,
> start at [`AGENTS.md`](./AGENTS.md).

## Why a separate agent-targeted doc set

ClaudeForge has thorough human-targeted documentation: `README.md`,
`CLAUDE.md`, `CONTRIBUTING.md`, `LOCALIZATION.md`, `TRIMMING.md`, `PLATFORM.md`,
`DISCLAIMER.md`, plus the `docs/` deep-dives. Those are well-maintained but
**shaped for human readers** — narrative, motivation, examples, top-to-bottom
flow.

The risk for a fresh-session agent (this Claude or any other LLM) isn't
*not knowing the codebase* — `grep` and `Read` solve discovery. The risk is
**violating cross-file contracts that aren't visible from a single-file
read.** Concrete recurring examples in this project:

1. The `IsModified` force-fire pattern (every compound editor must implement
   the same dance; bare `IsModified = true` is silently broken when the flag
   was already true at load time).
2. `ConfigScope` enum value order is structurally coupled to
   `ClaudeScope._cache` array order — swap one, you must swap the other.
3. `_suppressStateSave` latch must be set before `Shutdown()` so
   `OnClosed → SaveWindowState` doesn't recreate the file Clear-App-Data
   just deleted.
4. `PlatformInfo.Current` is the right call for UI / display branches;
   `OperatingSystem.IsWindows()` is the right call for platform-intrinsic
   APIs (registry, MSIX) — and the line drawn between them is non-obvious.
5. `WindowStateService.StatePath` is computed lazily (not a `static readonly`)
   so tests that mutate `PlatformPaths.TestUserProfileOverride` resolve the
   right path.
6. `_userEditedPaths` (not `IsModified`) is the correct gate for the
   group-editor flush, because every loaded compound editor carries
   `IsModified=true` at scope-non-empty load time and an IsModified-only
   gate clobbers concurrent out-of-band writes.

Each is a cross-file contract. None are visible from any single file an
agent might Read. The `AGENTS.md` set surfaces those contracts up front.

## What exists today

| Path | Scope |
|------|-------|
| [`AGENTS.md`](./AGENTS.md) (repo root) | LLM-shaped index: hard-invariants table, cross-cutting checklists, test-seam quick reference, anti-patterns, verify-before-shipping checklist, pointer index. |
| [`src/ClaudeForge/ViewModels/AGENTS.md`](./src/ClaudeForge/ViewModels/AGENTS.md) | ViewModel layer: MainWindowViewModel integration hub, navigation tree structure, `SearchViewModel` contract, JsonPath→NavNode mapping, test seams. |
| [`src/ClaudeForge/ViewModels/Editors/AGENTS.md`](./src/ClaudeForge/ViewModels/Editors/AGENTS.md) | Compound-editor contract: the force-fire `MarkModified` pattern, the `_isLoading` guard, `OnResetToInherited` semantics, child-subscription bookkeeping, the parity table, test-pattern templates. |
| [`src/ClaudeForge.Core/Settings/AGENTS.md`](./src/ClaudeForge.Core/Settings/AGENTS.md) | Workspace / scope / dirty-tracking semantics: `ConfigScope` ↔ `ClaudeScope._cache` coupling, `IsDirty` vs `HasActualChanges()`, merge semantics, `_selfWriting` guard. |
| [`src/ClaudeForge.Sdk/AGENTS.md`](./src/ClaudeForge.Sdk/AGENTS.md) | SDK architecture: `IClaudeConfigClient` surface, `_suppressForwarder` + `_selfWriting` dual guard, `_cachedSchemaNodes`, `Changed` threading model. |

The structure is **hybrid**: a root index plus per-folder sidecars where
local-invariant density is high enough to justify them. A flat directory of
per-component agent docs would be high-maintenance and quickly stale; a
single mega-file at the root would be too long to scan. The current
per-folder sidecars cover the highest-leverage concerns without sprinkling
docs everywhere.

## Discipline: the rules that keep these docs honest

Every claim in an `AGENTS.md` file should be **fact-shaped**: a file path, a
class name, a member name, a regression-test name. Drift then surfaces as a
wrong identifier when an agent uses the doc — not as silently-stale prose.

Concrete rules, enforced by the doc text itself (the root `AGENTS.md` opens
with these as explicit anti-patterns):

- **No hardcoded source-line numbers** (`Foo.cs:245`). They drift on every
  refactor. Cite the file, the type, the method, or `nameof()` and let
  `grep` do the locating.
- **No timestamps in prose** ("Reported 2026-05-13", "shipped 2026-05-07").
  `git log` and `git blame` are the authoritative source for when a thing
  happened; date-stamping prose adds maintenance debt with no corresponding
  benefit.
- **No row IDs that have to be renumbered** when invariants are added or
  removed. The invariants table is keyed by the invariant itself, not by
  an index column.
- **No hardcoded test counts**. The green baseline is whatever the most
  recent successful CI run on `main` reported.

If a sentence in an `AGENTS.md` file can't be expressed as "file `Foo.cs`,
type `Bar`, method `Baz`, contract `Qux`", it doesn't belong there — it
belongs in `CLAUDE.md`'s narrative or one of the `docs/` deep-dives.

## What the agent docs deliberately don't cover

Listed so an audit doesn't add work that the design rejected:

- **API reference / class shapes** — the source IS the reference; a doc
  would just drift.
- **Avalonia / .NET fundamentals** — baseline competence is assumed.
- **Refactor history** — `git log` and `git blame` own that.
- **General coding standards** — `CONTRIBUTING.md` owns that.
- **Per-control or per-view sidecars** — local logic is small enough that
  sidecars at that granularity are a maintenance liability.
- **Anything already in `CLAUDE.md`** — pointer-only.

## Maintenance contract

- Each invariant entry includes the canonical source file path. Touch the
  source file → the doc entry's path either still resolves (good) or it
  doesn't (caught at review).
- Each "if you do X also touch Y" checklist names specific files. Move a
  file in a refactor → checklist visibly wrong on the next review.
- Each anti-pattern includes a regression-test name that locks the
  contract. Test deleted → doc entry caught next time someone reads it.
- New cross-file contract → add a row to the invariants table. Old
  invariant fully retired → remove the row (no renumbering needed,
  because there are no row IDs).

## How to keep this file useful

This file is the design rationale, not a changelog. It changes when the
structure of the agent docs themselves changes — adding a new sidecar,
retiring one, or revising the discipline. Day-to-day churn in the
contents of the `AGENTS.md` files does NOT require an update here.

Pointers from the rest of the repo:

- `CLAUDE.md` references `AGENTS.md` in its companion-doc section.
- `README.md` lists `AGENTS.md` in its Documentation table.
- The root `AGENTS.md` cross-references the per-folder sidecars in its
  pointer index.
