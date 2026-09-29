# 00008 — AgentForge becomes product-neutral

> Status: **approved 2026-09-29**. Supersedes nothing. The move to its own repository is the
> next plan, `00009`, drafted from what this one measures.

Decided 2026-09-28: the five `AgentForge.*` libraries will leave this repository for their own and
publish to nuget.org, reversing the 2026-09-25 decision to keep them here. Before they can, they
have to be what their name and `CLAUDE.md` claim: libraries that know nothing about Claude. Measured
2026-09-28, they are not. Only `AgentForge.Abstractions` and `AgentForge.Artifacts` are. Moving
them as they are would make every Claude path, schema or model change an AgentForge release, one
per day at most, and would publish Claude's engine under a neutral name. The maintainer chose to
neutralize fully first, knowing the size below.

## Measured 2026-09-28

| What | Measurement | How |
|---|---|---|
| Claude knowledge in `src/AgentForge.*`, by word | **327 non-comment lines in 34 files** (122 with a Claude-bearing literal, 205 with a Claude-named identifier). **263 are in Core.** Abstractions and Artifacts: 0 | A scan that strips `//`, `///` and `/* */` while respecting strings, for `claude\|anthropic` |
| …by layout, without the word | About 20 literal sites: `managed-settings`, `mcp.json`, `settings.local`, `.credentials`, `sk-ant`, MSIX | A second pass over the same files |
| …by **shape**, without any literal | `ScopeLadder.Default` is Claude's ladder (`ScopeLadder.cs:38` documents that as deliberate). `ConfigScope` stores a **null** ladder for it (`Ladder => _ladder ?? ScopeLadder.Default`), and that null is the only reason `default(ConfigScope)` equals `Managed`; the `isDefault` constructor that makes one is private to Core. 13 `ConfigScope` fields are declared without an initialiser: 11 in ClaudeForge and ClaudeForge.Avalonia rely on that default, and Shell's two are assigned in their constructors. Shell itself falls back to Claude's `ConfigScope.User` (`SharedScopeContext.cs:30,37`, `SettingsGroupEditorViewModel.cs:163`). References: the `ConfigScope` statics 1,335 in 91 test files and 43 in product code; `ScopeLadder.Default` 13 in tests and 7 in `src`; `FootprintCatalog.Default` 14 and 10; the `FootprintCategory` statics 40 in tests. `FootprintCatalog.Default` (in **`AgentForge.Sdk/Memory/`**) is Claude's seven categories and on-disk layout (`FootprintCatalog.cs:55-100`), and `FootprintCategory` uses the same null encoding; one test pins `default(FootprintCategory)` (`FootprintCatalogTests.cs:28`) | Search for the statics; `ConfigScope.cs`, `ScopeLadder.cs`, `ConfigScopeTests.cs:49-50`, `ScopeLadderTests.cs:144,167` |
| By kind | **Paths and environment** ≈125 lines, mostly `PlatformPaths`; **behaviour in neutral engines** ≈110 (backup, restore, profiles, discovery, memory services, schema tree); **data** ≈45 lines plus 5 embedded resources; **text** ≈10; **dead** 8 members; plus the two shapes above | Classified site by site |
| Embedded resources | Core's three `EmbeddedResource` groups (`Schemas`, `ModelCatalog`, `Descriptions`) are all product data; no other AgentForge project embeds anything | `AgentForge.Core.csproj:48-50` |
| Public API that changes | About **100 members** before the two shapes: ~25 `PlatformPaths` Claude members, the `BackupEngine` and `RestoreEngine` constructors, 7 on `SchemaRegistry`, `ConfigFileType`'s two Claude values, `EnvVarKey`/`IEnvAccessor` (6), `AgentConfigClientCore.ArtifactPaths`, 6 memory-service overloads, 4 Shell MSIX members | `tests/ClaudeForge.Tests/Architecture/PublicSurface/AgentForge.*.txt` |
| Persisted wire strings that name Claude and stay in neutral code | `includesClaudeCode`, `includesClaudeDesktop` (`ExportManifest.cs:85,90`), `_claudeforge_sanitization_error` (`BackupJsonContext.cs:33`), and the legacy archive folders `ClaudeCode`/`ClaudeDesktop` (`ExportManifest.LegacyProductFolders`). `claude_md` leaves with `ExportedProfile` | Read |
| Neutral engines tested through Claude | **26 test files** in `AgentForge.Core.Tests` and `AgentForge.Sdk.Tests` build on `ClaudeCodeProductFor`, `ClaudeEnvironment` or `PlatformPaths.Claude*`, among them `BackupEngineTests`, `RestoreEngineTests`, `ProductBackupLayoutTests` and the frozen `BackupArchiveCompatibilityTests`, which restores into Claude paths | Search |
| Shell's test coverage | On test platform v1 with `Microsoft.Testing.Extensions.CodeCoverage` **17.14.2**: 82.1% of 2,833 lines from `ClaudeForge.Tests`, 19.4% from the 8 files that compile without ClaudeForge, **1,775 lines in 34 types** reached only through ClaudeForge, led by `SettingsGroupEditorViewModel` (554) and `BackupRestoreViewModel` (450). On platform v2 (since #101) with **18.11.2**, the same unchanged Shell measures **81.6% of 2,654 lines in 40 types**: the instrument counts differently, so numbers are compared only within one tool version | Cobertura per type, in a throwaway worktree. ⚠ On platform v2, `dotnet test … -- --coverage` writes nothing; the test executable run directly does |
| Consumers of the changing API | ClaudeForge only, and the parked `feat/agentforge-opencodeforge` branch | Repository and feed |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **The end state is enforced.** Two guards here. **(a) Code:** a scan of `src/AgentForge.*` with comments stripped fails on `claude`, `anthropic` and the measured layout literals, except a **permanent, named list of persisted wire strings** (decision 5). Until the plan is done, an allow-list holds today's sites **per file, as a count per normalized line text**, and no count may grow; the scan excludes generated `obj/` files. **(b) Resources:** every built `AgentForge.*.dll` carries **no manifest resources other than compiled `.resx` strings**. **(c) Shapes:** no `new ScopeLadder(` or `new FootprintCatalog(` anywhere under `src/AgentForge.*`, as a source scan — the literal scan cannot see Claude's ladder or catalog, which contain none of the measured words, and a shape that is never constructed there cannot come back through a public static, an internal one, a collection-typed one or a factory method | A goal without a guard erodes. A count per line text, not a set, because identical allowed lines already repeat (`ClaudeEnvironment env,` in three files, seven repeats in `ProfileEngine`), so a set would let a new copy in unseen, and a seam step's rewrite is then a removal the count can see. `.resx` stays allowed because a neutral library may need its own strings |
| 2 | **Every move is the last step of the item that frees it.** For each cluster, the pull request adds the seam, repoints the neutral callers to it, and only then moves the Claude type out | Moving a type before its neutral callers stop needing it cannot build: Core and Sdk may not reference `ClaudeForge.Sdk.Claude` (`AssemblyLayeringTests`). This is also what makes decision 11's "green on its own" possible |
| 3 | Code only ClaudeForge uses **moves whole** into `ClaudeForge.Sdk.Claude`: `ClaudeEnvironment`, `ClaudeDesktopVersionProbe`, `MsixPathProbe` (with `MsixStatus`, `MsixFixResult`), `ProfileEngine` (with `ProfileInfo`, `ExportedProfile`), `KnownProjectsDiscovery`, `ConfigFileDiscoverer`, `ModelCatalogLoader`/`ModelCatalog`, `ClaudeArtifactPaths` and the four internal `Claude*` artifact sources, and both Claude `ProductDescriptor`s as a `ClaudeProducts` type | A type with no neutral caller, once decision 2's seams exist, needs only a new home |
| 4 | **Seams, each sized to its measured call sites:** bundled data on `ProductDescriptor`; `ProductBackupLayout` gains per-project items, a known-projects provider and validatable-file globs; an `IArtifactLayout` the product implements for the memory services, and the footprint catalog supplied through it; `EnvVarKey.AllWellKnown` product-supplied; `GithubReleaseChecker` requires its URL. **Two sites need no seam:** `SchemaTreeBuilder` collects env-var candidates neutrally and ClaudeForge, its only consumer, filters them by prefix; and **the MSIX tab's view model is owned by ClaudeForge**, which Shell's backup page hosts as an extra tab supplied by the host. One command could not carry it: the view binds `ShowMsixTab`, `MsixStatus` paths and `FixMsixCommand`, and the tab index is hard-coded | Each seam replaces a measured Claude site. The two exceptions are cheaper than a seam and put the knowledge where it belongs |
| 5 | **Persisted wire strings keep their text.** The strings in the measurements table stay in neutral code, each on decision 1's permanent list with the archive or manifest version that needs it. Only C# names change | Renaming them would orphan every existing backup and profile archive. A reader of old archives has to know the old keys |
| 6 | **Tests follow what they test.** Tests of moved Claude types move to `ClaudeForge.Sdk.Claude.Tests`. Tests of neutral engines that build on Claude data switch to a **neutral test product** (a descriptor, layout and ladder defined in the test projects), so the engines keep their coverage in their own repository. `BackupArchiveCompatibilityTests` restores into Claude paths with Claude's descriptors, so it **moves to `ClaudeForge.Sdk.Claude.Tests`** with its frozen archive, where it still guards decision 5; step 3 mints a second frozen archive for the neutral test product, so Core keeps a compatibility test of its own | A test moved with its data leaves the engine untested; decision 6 exists for that gap |
| 7 | The 8 dead members (`PlatformPaths.ProfileClaudeMdPath`, `DefaultBackupDirectory`, `DesktopLogsPath`, `CredentialsPath`, `IsClaudeCodeOnPath`; `ConfigFileDiscoverer.DiscoverProfiles(env)`; the two `bool isClaudeCode` overloads on `SchemaRegistry`) are **deleted**, with any test that exists only for them | No caller in `src/`; moving dead code is work spent on nothing |
| 8 | The OpenCode schemas and `ConfigFileType`'s OpenCode values **leave Core** without a new home on this branch. `ConfigFileType`'s Claude values leave with `ConfigFileDiscoverer` (step 11), which is their only real user; its one other reader is a log line in ClaudeForge | The maintainer's choice 2026-09-28. Unused here; the parked branch keeps its copy |
| 9 | **`AgentForge.Avalonia.Shell.Tests` comes early, as the safety net for the refactor**, with a neutral test client. Its target is re-baselined after the engine changes: Shell's coverage from it alone reaches at least what `ClaudeForge.Tests` then gives Shell, per type, **both measured with `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2**, run through the test executables directly. **A new test must assert an observable outcome**; coverage from a test that asserts nothing does not count | Tests written first protect the rewrite of `BackupRestoreViewModel` and `SettingsGroupEditorViewModel`. A threshold alone can be met by tests that check nothing |
| 10 | **Neither Core nor Sdk keeps a default ladder or catalog.** `ScopeLadder.Default` (Core) and `FootprintCatalog.Default` (Sdk) move to the product with the `ConfigScope` and `FootprintCategory` statics (a Claude type in `ClaudeForge.Sdk.Claude`), after Shell takes its scopes from the client's ladder. **The `default(ConfigScope) == Managed` invariant is DROPPED, not restated**: a `ConfigScope` must name its ladder, and a default one throws from `Ladder`, `Id`, `DisplayName` and `IsReadOnly` instead of silently meaning Claude's `Managed`, while `ToString` returns a visible sentinel because logging and AXAML converters call it. The same for `FootprintCategory`. A source guard bans default-producing expressions of both types in `src/`, so the new failure is caught at review rather than at runtime (step 13) | The maintainer's choice 2026-09-29, made knowing the invariant cannot survive: the null ladder *is* Claude's ladder. The recorded design at `ScopeLadder.cs:38` and the guard at `ScopeLadderTests.cs:144,167` are reversed on purpose. It is the widest change in the plan: about 1,460 references to the statics and the two defaults, mechanically rewritten, which is why it runs last among the clusters |
| 11 | **Each cluster is its own pull request**, green on its own, with the public-surface baseline diff read in review | A ~100-member API change reviewed in one diff is not reviewed |

**Dismissed:** *move first, neutralize later*, *move only the two neutral libraries*, and *don't
move*. All three were offered with these numbers, and the maintainer chose this plan. *Rename the
persisted keys* — see decision 5. *Leave the MSIX tab in Shell behind a flag* — a flag still ships
Claude's MSIX knowledge in the neutral package.

## Scope

**In:** everything in the measurements table; the guards of decision 1; the Shell test project;
the neutral test product; the tests that move with their code.

**Out:** the move itself (`00009`); any behaviour change visible to a user; the parked
`feat/agentforge-opencodeforge` branch, which will have to adopt these seams when it rejoins, and
whose OpenCode code is expected to break against them; XamlQuality's new opt-in rules.

## Steps

Every pull request verifies the same four things, besides its own:
- the Debug build has 0 warnings, and the full suite passes;
- test names are compared against `main` with `scripts/Compare-TestNames.ps1` in its pairing mode
  (step 2), and every difference is a test that moved, one decision 7 deleted, or one the pull
  request's own named work adds;
- a trimmed Release publish has 0 IL diagnostics, and **the published app boots**:
  `src/publish/Smoke-PublishedBinary.ps1` launches it and it stays up. Nothing in CI runs this, and
  the seams rewire what the composition root hands the libraries, which unit tests build by hand;
- the public-surface baseline diff contains only the members that pull request names.

### Phase A — guards, fixtures and tooling, before any code moves

1. **Decision 1's two guards**, with the allow-list holding today's sites. *Verify:* green as
   measured; red when a new `claude` literal is added in an allowed file, red when one is added in
   a new file, red when a layout literal is added; the resource guard is red today (Core embeds
   three groups) and so starts with Core on its allow-list; the shapes guard starts with the two
   constructions of Claude's defaults (`ScopeLadder.cs`, `FootprintCatalog.cs`) on its allow-list,
   which 13c empties.
2. **A pairing mode for `scripts/Compare-TestNames.ps1`**, keyed on the short class name plus the
   method, so a moved test reads as moved rather than as one removal and one addition. *Verify:*
   canaried by moving one test class between two test projects in a scratch copy.
3. **Mint the golden fixtures from `main`**, committed as test fixtures: a backup of a fixture home
   and a profile export, each with a normalized-JSON comparison whose ignore-list is written down
   (`ExportedAt`, `appVersion`, `projects[].projectRoot`, `sizeBytes`, archive timestamps); a
   frozen archive of the neutral test product, for Core's own compatibility test (decision 6); and
   the scopes each settings page offers, read from the view models in order, for step 13. Restore
   of a Claude archive stays compared against the existing frozen fixture in
   `BackupArchiveCompatibilityTests`. *Verify:* each comparison is red when one non-ignored field
   is changed.

### Phase B — the safety nets

4. **`tests/AgentForge.Avalonia.Shell.Tests`** with the 8 measured files and a neutral test client
   (after `AgentForge.Sdk.Tests`' `TestConfigClient`). The `TestSupport/` helpers are copied, not
   moved: 17 files here use them, and their own tests stay here. Then write the tests that cover
   `BackupRestoreViewModel` and `SettingsGroupEditorViewModel` against the neutral client, before
   Phase D rewrites them. *Verify:* coverage of those two types from the new project, per
   `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2, at least what `ClaudeForge.Tests` gives them
   today under the same tool.
5. **The neutral test product** (decision 6), which uses `ScopeLadder.Default` until 13c (until 13a,
   Shell still falls back to the `ConfigScope` statics, so a ladder of its own would test the
   fallback, not the product), and Core's and Sdk's coverage measured the same way
   as Shell's, from their own test projects, before and after switching the 26 files to it.
   *Verify:* no type in Core or Sdk loses coverage by the switch.

### Phase C — data

6. **Bundled data through the descriptor**: Claude's schemas, overlay, enum descriptions and model
   catalog move to `ClaudeForge.Sdk.Claude` as embedded resources, with `ClaudeProducts` and
   `ModelCatalogLoader`, and the neutral callers of the descriptors (`RestoreEngine`,
   `ExportManifest`) repointed first (decision 2). Repoint what writes into or watches the data:
   `scripts/refresh-schema.ps1` and `.sh`, `scripts/validate-model-catalog.ps1`,
   `.github/workflows/schema-refresh.yml`, `.github/workflows/model-catalog-refresh.yml`,
   `.github/WORKFLOWS.md`, `AGENTS.md`, `CLAUDE.md`. *Verify:*
   - a search for `src/AgentForge.Core/Assets` finds nothing outside `plans/`;
   - the refresh script with no upstream change leaves `git diff` empty;
   - step 3's backup fixture comparison passes, including the bundled schemas by name and SHA-256.
     `BackupEngine` bundles by resource prefix, and a prefix matching nothing produces an archive
     with ZERO schemas that `RestoreEngine` then restores WITHOUT validating;
   - with `--schema-source bundled`, a saved `settings.json` validates against the same schema
     digest as on `main`. The default is network-first and would pass whether or not the data moved.
7. **OpenCode data and `ConfigFileType`'s OpenCode values** (decision 8); its Claude values stay
   until step 11, where they leave with `ConfigFileDiscoverer`. *Verify:* the resource guard passes
   for every `AgentForge.*.dll` and its allow-list is removed.

### Phase D — the clusters, each seam then move (decision 2)

8. **Backup and restore**: `ProductBackupLayout`'s additions; `restorableProducts` required,
   removing `ClaudeEnvironment` from both constructors; `KnownProjectsDiscovery` then moves.
   *Verify:* step 3's fixtures and `BackupArchiveCompatibilityTests` pass.
9. **Memory and footprint**: `IArtifactLayout`, the footprint catalog through it; then
   `ClaudeArtifactPaths` and the four artifact sources move. *Verify:* the artifact sources
   resolve the same artifacts, by path, for a fixture home as on `main`; the footprint page's
   categories and sizes match for the same home.
10. **Paths, environment and probes**: `ClaudePaths` (decision 4's `PlatformPaths` split),
    `ClaudeEnvironment` once nothing neutral takes it, both version probes, `MsixPathProbe`, and
    the Claude env keys. *Verify:* `ClaudeCodeDetectionTests` and the Essentials env tests pass
    unchanged apart from namespaces.
11. **Profiles and discovery**: `ProfileEngine` and `ConfigFileDiscoverer` move whole, and
    `ConfigFileType`'s Claude values with them (as a Claude-side type, or `DiscoveredFile` carries a
    string). *Verify:* step 3's profile fixture comparison passes; discovery over a fixture home
    finds the same files.
12. **The schema tree's env candidates** filtered in ClaudeForge, and **the MSIX tab** owned by
    ClaudeForge (decision 4). *Verify:* the MSIX tab appears on Windows for a Claude backup and
    nowhere else, as on `main`.
13. **Scopes and footprint categories** (decision 10), in three pull requests. The order matters:
    the defaults leave only after nothing depends on the null encoding, so the move itself changes
    no behaviour.
    - **13a — Shell stops falling back to Claude's scopes.** Shell takes its initial and available
      scopes from the client's ladder (`ScopeLadder.DefaultEditableScope` exists for this) instead
      of `ConfigScope.User` (`SharedScopeContext.cs:30,37`, `SettingsGroupEditorViewModel.cs:163`),
      and the parameterless `SharedScopeContext()` is deleted. *Verify:* step 3's fixture of scopes
      per settings page matches exactly, in order; no `ConfigScope.` static remains in
      `src/AgentForge.Avalonia.Shell`.
    - **13b — the null encoding goes, while the defaults are still in Core and Sdk.**
      `ScopeLadder.Default`'s scopes carry their ladder instance, and `FootprintCatalog.Default`'s
      categories their catalog. A default `ConfigScope` or `FootprintCategory` throws from `Ladder`,
      `Id`, `DisplayName` and `IsReadOnly` (and the category equivalents). `ToString` returns a
      visible sentinel instead of throwing, because it runs in logging and in AXAML converters
      (`ScopeLadder.cs:151`), including hidden ones (`PermissionTesterView.axaml:103-104`).
      `Equals` and `GetHashCode` stay field-based, so a default used as a dictionary key is still
      safe. The 13 uninitialised fields get explicit initialisers: the 11 in ClaudeForge and
      ClaudeForge.Avalonia; Shell's two are already constructor-assigned. A source guard bans
      default-producing expressions of both types in `src/`: `default`, `default(T)`, `new T[`,
      `GetValueOrDefault()`, and fields neither initialised nor assigned in the constructor. The
      guards that pin the old invariant are replaced by ones asserting a default instance throws
      from `Ladder`: `ConfigScopeTests.cs:49-50`, `ScopeLadderTests.cs:144,167`,
      `FootprintCatalogTests.cs:28`. `PermissionOutcome.cs:34`'s advice to copy the old choice is
      rewritten. *Verify:* each replaced guard is red if a null ladder or catalog is read as
      Claude's again; the source guard is red with one initialiser removed and with one planted
      `default(ConfigScope)`; a test builds the permission tester's initial view model and runs its
      scope converters without throwing. The rest of the suite is not evidence here: the default
      already meant `Managed`, and Phase 3 measured that the suite cannot see default drift.
    - **13c — the defaults and statics move, and nothing else changes.** `ScopeLadder.Default`,
      `FootprintCatalog.Default`, the `ConfigScope` statics and the `FootprintCategory` statics move
      to a Claude type in `ClaudeForge.Sdk.Claude`. The references are rewritten mechanically by a
      script whose output is the whole diff: `ConfigScope` statics (1,335 in 91 test files, 43 in
      product code), `ScopeLadder.Default` (13 in tests, 7 in `src`), `FootprintCatalog.Default` (14
      and 10) and the `FootprintCategory` statics (40 in tests). The neutral test product (step 5)
      gets a ladder and catalog of its own in the same change. *Verify:* step 3's scopes-per-page
      fixture matches; decision 1's shapes guard passes with its allow-list empty and removed; the
      suite is otherwise unchanged, which
      here is evidence, because 13b removed the only behaviour a move could change.

### Phase E — text, dead code, and the end state

14. Generalise the remaining text sites (`"ClaudeRestore-"`, `claudeHome`,
    `CheckForRunningClaudeProcesses`, the MSIX messages, which also move into resx), make
    `GithubReleaseChecker`'s URL required, and delete decision 7's members. *Verify:* the code guard
    passes with an **empty allow-list** and only decision 5's permanent list; the allow-list
    mechanism is removed.
15. **Shell's coverage re-baselined**: `ClaudeForge.Tests`' coverage of Shell measured again now
    that Shell has changed, and the new project reaches it per type (decision 9). Any type still
    short is listed in `PROGRESS.md` with its reason.
16. **Drive the app once, end to end**, with the UI Automation harnesses in `scripts/retest/`
    against a scratch home (`Test-ScratchHomeIsolation.ps1`), on a build of `main` from before
    Phase A and on the finished branch: back up and restore, export and import a profile, switch
    the editing scope on a settings page, and open the footprint and memory pages. Everything this
    plan rewires is in those flows. *Verify:* both builds produce the same archive entries,
    profiles, scopes and page contents, compared by the step 3 fixtures' normalization; any
    difference is a defect or is listed in `PROGRESS.md` with its reason. The harness README's
    lessons apply, so an unchanged result is measured by its effect, never by a fixed sleep.

### Phase F — the inputs `00009` needs

17. **Re-measure** what the move depends on: the per-consumer `InternalsVisibleTo` probe, the
    co-change rate, and the guards and scripts that name `src/AgentForge.*`. The results go in
    `PROGRESS.md`, and `00009` is drafted from them.
