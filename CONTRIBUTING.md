# Contributing to ClaudeForge

Thanks for your interest. This doc walks you through the dev environment, the rules of the road, and what kinds of contributions we welcome.

For a behaviour overview from a user's perspective, see [README.md](./README.md). For deep technical context (architecture, decisions, gotchas), see [CLAUDE.md](./CLAUDE.md) — it was originally written for AI agents but is the most thorough internal reference.

---

## Before you begin

- For bug reports and feature requests, please [open an issue](../../issues) first.
- For non-trivial changes, discuss the approach in an issue before writing code.
- Small fixes (typos, obvious bugs) can go straight to a pull request.

---

## Quick start

**Prerequisites:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview).
- Git.
- An IDE that handles Avalonia AXAML — Visual Studio, JetBrains Rider, or VS Code with the C# Dev Kit.

```bash
git clone https://github.com/JanusMael/ClaudeForge.git
cd ClaudeForge
dotnet build
dotnet test
dotnet run --project src/ClaudeForge
```

The app uses `~/.claude/` (and any open project's `.claude/`) for its real working data. If you'd like an isolated sandbox for development, the `PlatformPaths.TestUserProfileOverride` static is the seam most tests use — but for manual smoke runs, point at a throwaway `~/.claude/` setup or back yours up first.

---

## Project structure

| Project | Description |
|---------|-------------|
| `src/ClaudeForge.Core` | Config model, file I/O, schema registry — no Avalonia dependencies |
| `src/ClaudeForge.Sdk` | Typed accessors over Core (the public consumer surface — `IClaudeConfigClient` + accessors) |
| `src/ClaudeForge` | Avalonia UI application — views, view-models, converters |
| `src/LayeredEditors.*` | Reusable layered-config editor library (used by ClaudeForge but designed to stand alone) |
| `tests/ClaudeForge.Core.Tests` | Domain logic |
| `tests/ClaudeForge.Sdk.Tests` | SDK accessor contracts + regression tests |
| `tests/ClaudeForge.Tests` | View-model + headless integration tests |
| `tests/LayeredEditors.*.Tests` | Library tests |

When in doubt about which project a file belongs in: if it has Avalonia / Semi.Avalonia references it's `ClaudeForge`; if it's pure JSON / file-IO / domain logic it's `ClaudeForge.Core` or `ClaudeForge.Sdk`.

---

## What we welcome

- **Bug fixes** — reproduce on your machine first, include a regression test where it fits.
- **New compound editors** for schema properties not yet covered — see `AGENTS.md` § "X = Adding a new compound editor" for the checklist.
- **New top-level navigation pages** — checklist in `AGENTS.md` § "X = Adding a new top-level navigation page".
- **Localization** — `Strings.zh-CN.resx` is currently placeholder English; native-speaker translations are appreciated. Other locales follow the same satellite pattern.
- **Documentation** — particularly platform-specific gotchas, install procedures for distros we don't have first-class coverage for, and clearer error messages in user-facing dialogs.
- **Tests** — coverage on the GUI layer is lighter than on the SDK / Core. Headless-Avalonia test recipes welcome.

## What needs discussion before you start

Open an issue first if you're planning:

- **Architectural changes** (swapping a package, restructuring SDK / Core / GUI separation, adding a new top-level project).
- **New bundled schema files** — the schema-loading priority is documented in `CLAUDE.md`; choosing where to embed something has follow-on consequences. To **refresh** an existing bundled schema from its upstream source, see `scripts/refresh-schema.{sh,ps1}` — they download from `json.schemastore.org`, validate, show a diff, and write atomically. The PowerShell variant is also invoked weekly by `.github/workflows/schema-refresh.yml`, which opens a `chore/schema-refresh` PR if upstream has drifted; reviewers inspect the diff before merging. Any ClaudeForge hand-curated additions (notably the `default` + `examples` on the top-level `model` property, which drive the UI's AutoCompleteBox) live in the sibling `claude-code-settings.overlay.json` and are never touched by a refresh — the runtime merges them via RFC 7396 JSON Merge Patch. Guard tests under `tests/ClaudeForge.Core.Tests/Schema/` lock that contract.
- **Breaking changes to `IClaudeConfigClient`** — that's a public API; we want intentional churn.
- **Anything that touches the Backup / Restore engine.** Sharp safety invariants there (path-traversal protection, partial-write rollback, B4Forge-suffix files); subtle changes can corrupt user data.
- **Anything that touches the Profile import path.** See commit `854ed7e` for the path-traversal CRITICAL fix that's now pinned by 7 regression tests; don't regress it.

---

## Rules of the road

### SDK-first separation

- `ClaudeForge.Core` — domain model. No Avalonia. No UI dependencies.
- `ClaudeForge.Sdk` — typed accessors over Core. `IClaudeConfigClient` is the public contract; out-of-tree callers (a future MCP server, CLI, third-party apps) consume this.
- `ClaudeForge` — Avalonia UI. View-models bind to SDK clients, never directly to Core.

If you're adding a feature that touches data, ask yourself: "would this make sense in a CLI version of the app?" If yes, the logic belongs in Core or the SDK, not in a view-model.

### Trim safety (`PublishTrimmed=true`)

The Release build is trimmed for size. Every PR that adds code MUST publish cleanly:

```bash
dotnet publish src/ClaudeForge -c Release -r win-x64
```

Common foot-guns documented in [TRIMMING.md](./TRIMMING.md) and [docs/AVALONIA-GOTCHAS.md](./docs/AVALONIA-GOTCHAS.md). Highlights:

- New `JsonSerializer.Serialize/Deserialize` calls must use a source-generated context (`AppJsonContext` / `CoreJsonContext`).
- `JsonArray.Add(...)` calls cast to `(JsonNode?)` to force the safe overload.
- Every `DataTemplate` and `UserControl` in AXAML sets `x:DataType` for compiled bindings.
- Suppressing IL2xxx warnings on a 3rd-party assembly is NOT safe by itself when the package does string-typename reflection on its own internal types — pair with `<TrimmerRootAssembly>` (Markdown.Avalonia is the case study).

### Localization

User-visible strings ALWAYS go through `src/ClaudeForge/Localization/Strings.resx`. Adding a new key requires touching three files in lockstep:

1. `Strings.resx` — add the key + English value + a `<comment>` describing the context.
2. `Strings.zh-CN.resx` — add the same key with the same English value and `<comment>TODO zh-CN translation</comment>`. Don't translate yourself unless you're a native speaker.
3. `Strings.Designer.cs` — add the strongly-typed accessor by hand (we maintain it manually rather than relying on the IDE designer).

AXAML binds via `{x:Static loc:Strings.YourKeyName}`. See [LOCALIZATION.md](./LOCALIZATION.md) for the full workflow including platform-aware variants (`PlatformLabels.Reveal` etc.).

### Coding style

- Follow the existing code style (4-space indentation, C# file-scoped namespaces).
- **Many small files > few large files** — 200–400 lines typical, 800 max.
- **Immutable data** — prefer `with` expressions on records, `DeepClone()` on `JsonNode`. Don't mutate inputs.
- New public API must have XML doc comments.
- Errors handled explicitly — never bare `catch { }`. Filter exceptions (`when (ex is IOException or ...)`) and log or re-throw.
- No hardcoded user paths — route through `PlatformPaths` so the test override seam works.
- Prefer source-gen MVVM (`[ObservableProperty]`, `[RelayCommand]`) over hand-rolled `INotifyPropertyChanged`.
- AXAML: compiled bindings via `x:DataType` on every `DataTemplate` / `UserControl`.

### PII / security awareness

- Secret-bearing keys are auto-redacted by `SensitiveKeys.IsSensitive` before logging — see `src/ClaudeForge.Sdk/Diagnostics/SensitiveKeys.cs`. The classifier checks PATH SEGMENTS, so anything under `env`, `headers`, `credentials`, `auth` is auto-redacted regardless of nesting.
- If you add a new code path that emits user values, check whether they could contain secrets before logging. Trust `SensitiveKeys.IsSensitive` rather than ad-hoc regex.
- New file paths in logs are usually fine (they tend to contain the user's username only — accepted PII for a tool the user is running on their own machine), but flag for review if a path could contain values not normally in `~/.claude/` or `~/.config/`.

### Testing

- `dotnet test` should pass on every PR. A handful of platform-specific tests are gated on the host OS and skip on others; the green baseline is whatever the most recent successful run on `main` reported, and the CI status badge in the README reflects it. Don't hardcode the count anywhere — it drifts.
- New features should land with regression tests where it makes sense — particularly: SDK accessors (round-trip + edge cases), parsing / validation logic, anything security-sensitive.
- Avalonia-headless tests live under `tests/ClaudeForge.Tests/Headless/`. Pattern: `HeadlessUnitTestSession.GetOrStartForAssembly` + `Session.Dispatch(async () => { … })`. Two existing examples: `TransactionalReloadTests`, `ReloadHardeningTests`.
- Trim safety: if you add code that lights up new IL2026 paths, fix them rather than suppressing.

---

## Commit / PR conventions

### Commit messages

```
type(scope): short imperative description

Optional longer body explaining the WHY (not the what — diff shows the what).
Multi-line bodies are normal for non-trivial changes.
```

**Types:** `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`. Optional `scope` (parenthesised area name) recommended.

The codebase has dense commit messages — that's intentional. Future maintainers (and AI sessions) read commits to understand decisions; one-line commits like "fix bug" leave them no clues.

### One concern per commit

When feasible, mixing a refactor and a feature in one commit makes the diff hard to review. Split when you can.

### AI-assisted commits

Existing commits show `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` lines — those mark AI-assisted commits. Don't add the AI co-author line to your own human commits. If you used AI tooling and want to attribute, use a different co-author line that names your tool, or don't.

---

## Pre-PR checklist

- [ ] `dotnet build -warnaserror` passes — zero warnings.
- [ ] `dotnet test --no-build` passes — 0 failed, no new skips. Compare against the most recent green run on `main`.
- [ ] `dotnet publish src/ClaudeForge -c Release -r win-x64` succeeds with zero ILLink warnings.
- [ ] **If you touched the GUI:** launched the published binary and clicked through the affected pages (lifetime / asset-bundling regressions are easy to ship and only show up at runtime).
- [ ] **If you touched the SDK accessor public surface:** ran the contract tests in `tests/ClaudeForge.Sdk.Tests/` to confirm no breaks.
- [ ] **If you added a new resx key:** all three files (`Strings.resx`, `Strings.zh-CN.resx`, `Strings.Designer.cs`) are in sync.
- [ ] **If you touched privacy-sensitive code** (logging, file paths, env vars): no new PII or secret leaks.
- [ ] Commit messages explain the WHY, not just the what.
- [ ] No hardcoded secrets or credentials anywhere in the diff.

---

## Code of conduct

Be respectful. Disagree without being personal. Assume good faith from reviewers and contributors alike. We're all volunteers; nobody owes anyone their time.

---

## Where to find the deep docs

- **`CLAUDE.md`** — architecture decisions, build / run, key conventions, full list of subtle gotchas. The most thorough single document.
- **`AGENTS.md`** — hard invariants table, "if you do X also touch Y" checklists, test seams, anti-patterns.
- **`TRIMMING.md`** — trim safety policy and per-package safety stories.
- **`PLATFORM.md`** — `PlatformInfo` decision tree, debug flags table, redirectable-vs-platform-intrinsic API guide.
- **`docs/`** — feature-specific deep-dives (`ESSENTIALS-PAGE.md`, `LINUX-DESKTOP-INTEGRATION.md`, `AVALONIA-GOTCHAS.md`, `CLAUDECTX-COMPATIBILITY.md`).

If you find an inaccuracy in any of these as you work, please fix it. Stale docs are worse than missing docs.

Thanks for contributing.
