# 00002 — ClaudeForge resolves Claude Code's real config locations

> Status: **draft, awaiting approval**. Supersedes nothing.
> ⛔ **Sequenced as Phase E of [`00003`](00003-release-built-from-shared-packages.md) — AFTER the
> first package release, as the second package version.** An earlier draft claimed this had to land
> before the first `packages-v*` tag. It does not: only re-pushing an existing version is
> impossible, so a second version is a preference rather than a constraint — and landing a rewrite
> of path resolution in front of the release that exists to prove the package pipeline would leave
> a Phase D failure with two candidate causes.

---

## Two defects, one accessor family

Both live in `PlatformPaths` and its duplicate, and both are silent. They change packable assemblies,
so they land as **one** package version — but that version is the **second**, not the first; see the
status line above.

### ⛔ 1 · Managed settings are read from the wrong directory — the larger one

Claude Code reads managed (enterprise / MDM) policy from a **system** directory:

| OS | Path |
|---|---|
| macOS | `/Library/Application Support/ClaudeCode/` |
| Linux and WSL | `/etc/claude-code/` |
| Windows | `C:\Program Files\ClaudeCode\` |

holding `managed-settings.json`, an optional `managed-settings.d/`, and `managed-mcp.json`. The
legacy Windows path `C:\ProgramData\ClaudeCode\managed-settings.json` is explicitly **not** read.

ClaudeForge reads `~/.claude/managed-settings.json` and `~/.claude/managed-settings.d/`
(`PlatformPaths.cs:60-65`). The real system directory appears **nowhere in the product**, and
`managed-mcp.json` is not handled at all.

**It fails in both directions, silently.**

- On a machine with real policy deployed, ClaudeForge shows **no managed layer**. Its
  effective-settings view then tells the user their own value wins where policy actually overrides
  it — the exact failure `OpenCodeEnvironment` names for the OpenCode side: *"The effective view
  must honour it or it will disagree with the running agent about which settings apply."*
- A file at `~/.claude/managed-settings.json` displays as **enforced policy** in ClaudeForge and
  does nothing in Claude Code.

⭐ **The discovery code was written for the right thing and pointed at the wrong place.**
`ConfigFileDiscoverer.cs:32-56` already marks these entries `readOnly: true`, already reads a
drop-in directory, and already catches `UnauthorizedAccessException` with the comment *"skip
gracefully if unreadable (e.g. enterprise policy dir)"*. A privileged directory was anticipated.
`~/.claude/` is never privileged. Only the path is wrong.

### 2 · `CLAUDE_CONFIG_DIR` is documented to the user and ignored

The variable is in the bundled schema and the app ships a description for it —

> *"Override the configuration directory (default: ~/.claude)."*
> — `src/ClaudeForge/Converters/EnvVarTooltipConverter.cs:39`

— live as a tooltip in `src/ClaudeForge/Views/EnvironmentEditorView.axaml` (lines 178 and 280) and
app-wide via `LinkifiedTextBlock.EnvVarDescriptionProvider` (`src/ClaudeForge/App.axaml.cs:27`).

Claude Code's own wording is *"If you set `CLAUDE_CONFIG_DIR`, every `~/.claude` path lives under
that directory instead."* `PlatformPaths.ClaudeHome` is hardcoded to `Path.Combine(UserProfile,
".claude")` and nothing reads the variable. A user who sets it gets an editor that loads, validates,
diffs and writes a file the agent is not reading, with every surface reporting success.

⭐ **The two are independent.** `CLAUDE_CONFIG_DIR` cannot relocate managed settings, because
managed settings were never in the config directory. Fixing either alone leaves the other live.

## The precedent — OpenCodeForge already solved this shape

```csharp
// src/OpenCode.Sdk/OpenCodeEnvironment.cs
public sealed record OpenCodeEnvironment(
    string? ConfigDir = null, string? ConfigPath = null,
    string? InlineContent = null, bool ProjectConfigDisabled = false);
```

Four variables captured as a **value**, read from the real environment in exactly one place
(`FromProcess`), passed into discovery. Its remarks carry the reasoning this plan adopts:

> *"process environment is global mutable state and this suite runs many tests in one process …
> `TestUserProfileOverride` solves the same problem for the home directory by being `AsyncLocal`;
> passing a value is simpler still and needs no ambient state at all."*

The mechanism is not invented here. ClaudeForge is the half that never got it.

---

## Decisions

| Decision | Why |
|---|---|
| A `ClaudeEnvironment` record mirroring `OpenCodeEnvironment` — **not** a static, **not** an `AsyncLocal` | Ambient state is what makes the current code hard to test. A value read once in one place and passed down has neither a flow problem nor a reset problem, and the sibling product already proved the shape |
| Managed settings resolve from the **per-OS system directory** | That is where Claude Code reads them. An editor that disagrees with the agent about where policy lives is worse than one that shows no policy, because it shows a *wrong* answer confidently |
| **Both** path implementations take the resolved values | There are two and they are independent: `PlatformPaths.ClaudeHome` (static) and `ClaudeArtifactPaths.ClaudeHome` (instance, `src/AgentForge.Sdk/Memory/ClaudeArtifactPaths.cs:56`), the second deriving from `UserProfile` rather than the first. Fixing one ships an app whose settings pages read one tree while Agents & Skills and Memory read another |
| `DefaultBackupDirectory` becomes a sibling of the **resolved** home | Its rationale is that backups sit *next to* `.claude/` so losing that directory does not lose them (`PlatformPaths.cs:76-84`). "Next to" survives the move; a fixed `~/claude-backups` breaks both the rationale and any isolated `E3` |
| `TestUserProfileOverride` stays `AsyncLocal`, untouched, and keeps priority | Measured, not assumed — its own comment records *"a plain static races there — ~48 failures/run"*. Tests keep full control |
| The environment is read **once**, at composition | A path accessor that can change its answer mid-session is a corrupted save in flight |
| `~/.claude.json` does **not** move | The documented wording is *"every `~/.claude` path"*, and `~/.claude.json` is not one. ⚠ Not stated outright by the docs; see *Open questions* |
| Test isolation is a **consequence**, not the goal | A seam added so a test can pass is the class of change that ends up vouching for the thing it was meant to check. Both fixes stand on user-facing defects alone |

### Alternatives dismissed

| Rejected | Why |
|---|---|
| A `--user-root <path>` debug flag assigning `TestUserProfileOverride` | `AsyncLocal` flows with `ExecutionContext`, so a value set in `Main` reaches most but not provably all of startup. A **partially** applied override is worse than none: it writes some files to the sandbox and some to the real profile while reporting isolation |
| A plain process-wide static | Still ambient, still needs reset discipline in a single-process suite, and `OpenCodeEnvironment` demonstrates the better answer |
| Reading the variable per call inside the accessor | The process environment is mutable; the answer could change between a save's validation and its write |
| Two separate plans, one per defect | Same file, same accessor family, one public-surface change, one package version — against one immutable-feed deadline |

---

## Scope

**In**

- Managed settings, the drop-in directory, and `managed-mcp.json` resolved from the per-OS system
  directory.
- `CLAUDE_CONFIG_DIR` honoured for the user scope and everything derived from it: `settings.json`,
  `mcp.json`, `profiles/`, `.credentials.json`, `CLAUDE.md`, `.claudectx-current`, `local/`, and the
  `cache/` tree (schema cache, schema snapshots, `ClaudeForge-gui-state.json`).
- Both path implementations, held in agreement by a guard.
- `DefaultBackupDirectory`.
- A `CHANGELOG.md` entry under `## [Unreleased]`. ⚠ The managed-settings fix is **behaviour users
  can see**: a machine with deployed policy gains a managed layer that was previously invisible, and
  a hand-placed `~/.claude/managed-settings.json` stops being shown as enforced.

**Out**

- **OpenCodeForge.** It already honours its four variables correctly.
- **Claude Desktop config.** `%APPDATA%\Claude\claude_desktop_config.json` and its equivalents belong
  to a different product and are not under `.claude/`.
- **Project and Local scope**, resolved from the picked project root, not the user home.
- **The project-root picker**, still UI-only and persisted.

---

## Open questions

1. **Does `CLAUDE_CONFIG_DIR` move `~/.claude.json`?** The docs say *"every `~/.claude` path"*, and
   that file is `~/.claude.json` — a sibling, not a path under it. The plan plans for "does not
   move"; the docs do not state it outright, so it is worth one check against the real agent.
2. **Relative or non-existent `CLAUDE_CONFIG_DIR`.** Undocumented. No external constraint, so this is
   an ordinary defensive-design choice: resolve, or refuse, or create — pick one and say so at the
   point of use.
3. ⚠ **Does reading a system directory need elevation anywhere?** `ConfigFileDiscoverer` already
   catches `UnauthorizedAccessException`, so the failure mode is handled — but whether it fires
   routinely on `C:\Program Files\ClaudeCode\` decides whether "no managed layer" stays a plausible
   display or needs its own explanation in the UI.

⛔ **Settled — do not re-open.** `CLAUDE_CONFIG_DIR` does **not** relocate managed settings, because
managed settings live outside the config directory entirely. The policy-escape concern raised while
drafting this plan does not exist.

---

## Steps

Each step names the verification that shows it worked. No step is complete on a green build alone.

### 1 · `ClaudeEnvironment`, mirroring `OpenCodeEnvironment`

A record in `AgentForge.Core` with a single `FromProcess()` reading the real environment.

**Verification:** tests construct the record directly for each case — unset, absolute path, blank —
with no process-environment mutation anywhere. `FromProcess` is the one line no test exercises,
exactly as `OpenCodeEnvironment` documents.

### 2 · Managed settings move to the system directory

Per-OS paths, plus `managed-settings.d/` and `managed-mcp.json`.

**Verification**, in two parts, because the obvious one cannot run:

⛔ **A test cannot write to `C:\Program Files\ClaudeCode\` or `/etc/claude-code/`** — both need
elevation, so "place a policy file in the system directory and see it discovered" is not a runnable
check on a normal CI agent or developer machine. Writing the plan as though it were would produce a
step that is quietly skipped or quietly run as admin, and neither is evidence.

1. **Path resolution** — assert the computed path per platform against the three literals, with the
   platform simulated rather than the file created. This is the half that can be wrong in a way
   nobody notices.
2. **Discovery behaviour** — exercise `ConfigFileDiscoverer` against an **injected** managed root, a
   scratch directory, asserting the entries are found and marked `readOnly: true`.

⚠ **Assert the negative in both parts**: a file at `~/.claude/managed-settings.json` is **not**
discovered. Adding the new location while still reading the old one leaves the confidently-wrong
display in place for exactly the users who already have such a file, and a test that only checks the
new path passes either way.

ⓘ **That injected root is a new seam**, and it is the same shape as the `ClaudeEnvironment` value
above rather than a second mechanism — which is the point.

### 3 · One resolved home, threaded through both implementations

```
TestUserProfileOverride        // AsyncLocal, tests only, unchanged
ClaudeEnvironment.ConfigDir    // CLAUDE_CONFIG_DIR, read once at composition
Path.Combine(UserProfile, ".claude")
```

**Verification:** a **parity** test asserting both implementations return the same home for the same
inputs. ⚠ This is the guard that would have caught the duplication, so it must fail when only one
side changes — prove that by changing one side and watching it redden, before wiring the second.

### 4 · `DefaultBackupDirectory` follows the resolved home

**Verification:** with the variable set to a scratch path, a backup writes beside the scratch home
and **nothing appears in the real profile**. Assert the absence, not only the presence.

### 5 · A guard against the next bypass

A source scan for production code reaching `Environment.SpecialFolder.UserProfile`, or composing a
literal `".claude"` or a managed-settings path outside the sanctioned accessors.

⚠ **A source scan, not reflection** — reflection cannot see which accessor a call site chose, and
this repository has shipped that exact blind spot once; `ProductionSchemaRegistryTests` exists for
the same reason. It must match target-typed `new` and every spelling of the literal.

**Verification:** the canary. Write down the expected offending sites **before** running it; fewer
reds than predicted is as much a finding as more.

### 6 · Public surface, changelog, and the harness dividend

Regenerate the eleven public-surface baselines and **read the diff**. Add the `## [Unreleased]`
entry. Then the write-path retest items become runnable against a scratch home by setting one
environment variable on the child process.

**Verification:** `E1` driven end to end against a scratch home, with the real `~/.claude` proven
untouched by a before/after listing — not by reasoning about which code paths ran.

---

## What this plan does not claim

It does not make `E2`–`E5` automatable. Those need a project root, still chosen through a UI folder
picker and persisted to `cache/ClaudeForge-gui-state.json`. With this change that file lives inside
the relocated home, so seeding it stops being a write to the user's real profile — but seeding it is
separate work and no part of it is promised here.
