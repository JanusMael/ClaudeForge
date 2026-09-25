# 00007 — JsonC moves to its own repository and nuget.org

> Status: **approved 2026-09-25**. Supersedes nothing.

Decided 2026-09-25, library by library: `JsonC` leaves this repository for its own, published to
nuget.org by trusted publishing, and `AgentForge.Core` consumes it from there. The five `AgentForge.*`
packages stay here on the GitHub feed. The model is how ScopedEditors and AppServices left
([`plans/00005`](00005-claudeforge-and-agentforge-consume-scopededitors.md)).

## Why JsonC, and why it can move cleanly

Measured 2026-09-25, before drafting:

| Fact | Evidence |
|---|---|
| No dependencies at all | `src/JsonC/JsonC.csproj` has no package or project references |
| Stable | 2 commits touching it since 2026-08-26 (Core: 37) |
| Small public surface | `PublicSurface/JsonC.txt`, 98 lines |
| Only `AgentForge.Core` references it | project references across `src/` and `tests/`; on the parked OpenCodeForge branch too |
| Core uses **only its public API** | the one Core file using it, `JsoncEditWriter.cs`, touches `JsoncDocument`, `JsoncEditor`, `JsoncValueKind`; none of JsonC's three `internal` members (`Quote`, `AddItem`, `AddMember`) is used outside JsonC |
| No JsonC type leaks into another library's public surface | no `Bennewitz.Ninja.JsonC.` in any `PublicSurface/AgentForge.*.txt` |
| The nuget.org id is free | `api.nuget.org/v3-flatcontainer/bennewitz.ninja.jsonc/index.json` → 404 |
| Its naming already fits the family template | `bbpkg`: assembly names unprefixed (`JsonC`), package id prefixed (`Bennewitz.Ninja.JsonC`), `net10.0` |
| It is already trimmable | here it builds with `IsTrimmable` and `EnableTrimAnalyzer` from `src/Directory.Build.props`, and ClaudeForge's trimmed Release publish covers it; the template's `src/Directory.Build.props` sets the same two |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | Repository **`JanusMael/Bennewitz.Ninja.JsonC`**, generated with `dotnet new bbpkg -n JsonC --RepoOwner JanusMael` from **Templates `2026.3.925`**, installed from nuget.org | The family's way to start a repository (Templates `docs/repository-conventions.md`); it carries the conventions check, CI and the trusted-publishing release workflow. `2026.3.925` (released 2026-09-25) is the first version whose `bbpkg` ships `"trimming": "required"` and whose `check` evaluates the family's build properties, so the repository starts at the current bar rather than being raised to it later |
| 2 | **Fresh history**; the first commit names this repository's commit it was taken from | ScopedEditors' precedent (its first commit is "Initial commit", 2026-09-23). JsonC's history stays readable here. `git-filter-repo` is not installed, and would be the only reason to install it |
| 3 | Package id **unchanged**: `Bennewitz.Ninja.JsonC`; assembly `JsonC`; namespace `Bennewitz.Ninja.JsonC`; `net10.0` | Consumers change nothing but the source. Matches the template's convention |
| 4 | The first nuget.org version is **later than `2026.3.925`**, the last one on the GitHub feed | No version number may exist on two feeds with different bytes. CalVer makes this automatic on any later day; releasing it the same day is out |
| 5 | Published by **nuget.org trusted publishing** (OIDC), per the template's `docs/publishing.md` | No long-lived API key. Nothing publishes to nuget.org from THIS repository (locked) — it publishes from JsonC's own |
| 6 | `AgentForge.Core` takes a **`PackageReference`** to it in every mode, at a `JsonCVersion` property in the root `Directory.Build.props` | The same "never inert" pattern as `ScopedEditorsVersion` / `AppServicesVersion`: it is no longer a shared project of this repository, so the `UseSharedPackages` switch must not see it |
| 7 | **JsonC is routed to BOTH nuget.org and the GitHub feed until the next AgentForge release**, then to nuget.org only | The published AgentForge `2026.3.925` depends on `Bennewitz.Ninja.JsonC 2026.3.925`, which exists only on the GitHub feed. Routed to nuget.org alone, `Feed Restore` breaks until AgentForge is re-released against the nuget.org JsonC. With both, restore resolves the lowest version that satisfies each request, so old pins still find `2026.3.925` |
| 8 | The GitHub feed's existing JsonC versions are **left as they are** | Immutable history, still needed by decision 7 until the next AgentForge release |
| 9 | The **source** moves byte for byte; the **csproj** is the template's, carrying this one's `Description` rewritten for a standalone repository | The template's csproj and `Directory.Build.props` are what its build-property check measures. The current `Description` names `AgentForge`, `ClaudeForge.*`, `OpenCode.*` and `AssemblyLayeringTests`, none of which exists in the new repository; the first two paragraphs carry over, the layering ones do not |
| 10 | The template's **packaging tests stay**; only its sample (`Greeting.cs`, `GreetingTests.cs`) goes | `PackagingTests` and `TrimmableTests` guard the package itself, including that the shipped assembly carries the `IsTrimmable` mark that `"trimming": "required"` now demands |

**Dismissed:**

- *Publish to nuget.org from this repository* — reverses a locked decision, and ties a stable library to an app's release cadence.
- *Keep the history with `git filter-repo`* — needs a new tool, and the precedent chose a fresh start.
- *Multi-target `netstandard2.0`* — no consumer asks for it; the template targets `net10.0`. A later release can add a target without breaking anyone.
- *Rename the assembly to `Bennewitz.Ninja.JsonC`* — contradicts the family convention and changes every binary for nothing.
- *Generate from an earlier Templates version* — it would ship without `"trimming": "required"`, and the first `check` after upgrading would fail on what the repository already satisfies.

## Scope

**In:** the new repository and its first nuget.org release; this repository consuming it; removing `src/JsonC` and `tests/JsonC.Tests` from here; the guards, scripts and documents that name JsonC.

**Out:** any change to JsonC's API or behaviour (the source moves byte for byte); deleting the GitHub feed's JsonC versions; the parked `feat/agentforge-opencodeforge` branch, whose `AgentForge.Core` still references JsonC by project and takes this change when it rejoins.

## Steps

Steps marked **(maintainer)** need your account: creating a repository, nuget.org policy, and releasing.

### Phase A — the new repository

1. **Install the template.** `dotnet new install Bennewitz.Ninja.Templates::2026.3.925 --add-source https://api.nuget.org/v3/index.json`
   — no `bb*` template is installed on this machine today.
   *Verify:* `dotnet new list bbpkg` shows it, and the generated `.github/repository.json` carries
   `"trimming": "required"`.
2. **Generate and fill it.** `dotnet new bbpkg -n JsonC --RepoOwner JanusMael` in
   `C:\c\cl\Bennewitz.Ninja.JsonC`; replace the template's sample (`Greeting.cs`, `GreetingTests.cs`)
   with `src/JsonC/*.cs` and `tests/JsonC.Tests/*.cs` from this repository, keeping the template's
   `Packaging/` tests; carry the `Description` per decision 9; grant `JsonC.Tests` the internals it
   uses (`JsoncEditor.Quote`).
   *Verify:* build with 0 warnings; **every moved source file byte-identical** to this repository's
   at the provenance commit (hash compare); the test NAME set is exactly `JsonC.Tests`' 73 here plus
   the template's `Packaging/` tests, compared from both TRX files, not by count;
   `dotnet run --file scripts/repo-conventions.cs -- check --offline` passes, which is where the
   build properties and trimming are checked before the repository exists.
3. **(maintainer)** Create `JanusMael/Bennewitz.Ninja.JsonC` and push `main`. I can run
   `gh repo create` on your go-ahead.
4. **Conventions.** `.github/repository.json` description and topics (`nuget` stays: `packages.push`
   names an id); replace every `bbpkg:` marker in `README.md`, `AGENTS.md`, `PROGRESS.md`;
   **(maintainer)** `apply`, then `check --admin`.
   *Verify:* the `conventions` job goes green.
5. **(maintainer)** A nuget.org trusted-publishing policy for the repository's release workflow, per
   the generated `docs/publishing.md`.
6. **(maintainer)** The first release, on a day after `2026.3.925` (decision 4).
   *Verify:* against **nuget.org itself** (its flat container lists the version; the `.nupkg` holds
   `JsonC.dll` at the same stamp, carrying `[AssemblyMetadata("IsTrimmable", "True")]`), never
   against a green workflow.

### Phase B — this repository consumes it

7. **Consume.** `JsonCVersion` in the root `Directory.Build.props`; `AgentForge.Core` swaps its
   `ProjectReference` for `PackageReference Include="Bennewitz.Ninja.JsonC"`; `nuget.config` maps
   `Bennewitz.Ninja.JsonC` to nuget.org **and** keeps it on github (decision 7).
8. **Remove.** `src/JsonC`, `tests/JsonC.Tests`, their `ClaudeForge.slnx` entries,
   `PublicSurface/JsonC.txt`, and the two JsonC lines in `AssemblyInfo.InternalsVisibleTo.cs`.
9. **The coupled places** — `CLAUDE.md` names four, and nothing links them: the reference switch in the
   root `Directory.Build.targets` (JsonC named explicitly), `PackageMetadataTests` (six packable →
   five), `AssemblyLayeringTests`' selector, and `nuget.config`. Plus `scripts/verify-feed-restore.ps1`
   (six ids → five) and anything else that says "six".
10. **Documents.** `CLAUDE.md`'s "family of one" narrative becomes "consumed from its own repository";
    `AGENTS.md`, `CONTRIBUTING.md`, `docs/JSONC-WRITER.md`, `src/PACKAGE-README.md` point there.
    *Verify for 7–10:* build 0 warnings; the suite is exactly the old total minus `JsonC.Tests`'
    73, with no other name gone (TRX name sets); the package canary packs **five**; a trimmed Release
    publish has 0 IL warnings; CI all green, `Feed Restore` included. **Canary the guards:** add a
    `ProjectReference` to a deleted `src/JsonC` path and watch `BuildFilePathIntegrityTests` redden.
11. **(maintainer)** The next AgentForge release (`packages-v…`), built against the nuget.org JsonC.
    *Verify:* the published `AgentForge.Core` `.nuspec` names the nuget.org JsonC version.
12. **Drop the GitHub route** for JsonC in `nuget.config` and pin `SharedPackageVersion` to that
    release. *Verify:* CI all green; `Feed Restore` restores JsonC from nuget.org (read the restore
    log's source), not from the GitHub feed.
