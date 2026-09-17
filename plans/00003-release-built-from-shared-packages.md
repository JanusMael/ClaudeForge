# 00003 — A ClaudeForge release actually built from the shared packages

> Status: **draft, awaiting approval**. Supersedes nothing. Sequences [`00002`](00002-claude-code-real-config-locations.md).

---

## The finding this plan exists for

**The release has never published from package mode, and two places in the repository say it does.**

> *"Development mode is what every human runs, so PACKAGE mode is the one that rots unobserved —
> and it is the mode the release publishes from."*
> — `.github/workflows/ci.yml:132`

> `UseSharedPackages=true  ->  PackageReference   (this canary, and the release)`
> — `scripts/package-canary.ps1:10`

Neither is true. The release path resolves like this:

| Link | What it does |
|---|---|
| `.github/workflows/release.yml:192, 226, 257` | calls `./src/publish/publish.ps1 -App ClaudeForge -All -Rids …` |
| `src/publish/publish.ps1` | per-RID loop into `Publish-Rid.ps1` |
| `src/publish/Publish-Rid.ps1:160-167` | the **only** `dotnet publish` in the chain. It passes `-p:IncrementalBuild=false`, `-p:BuildInParallel=false`, `-p:RunResxKeyGuard=false` — and nothing else |
| `Directory.Build.targets:67` | the reference switch fires on `'$(UseSharedPackages)' == 'true'`, which is never set |

So `UseSharedPackages` is unset, the switch does not fire, and every RID of every ClaudeForge release
ever cut was built from **`ProjectReference`**. The eleven packages are not in the release at all.

⛔ **Even asking for package mode would fail today.** `.github/workflows/release.yml:21-23` grants
`contents: write` and `pull-requests: read` and **not `packages: read`**, so a restore of
`Bennewitz.Ninja.*` from the private GitHub feed would 401. Both halves are missing, independently.

⚠ **Nothing would ever have reported this.** The package canary proves package mode *works*; it does
not make the release *use* it. `PublicSurfaceBaselineTests`, `PackageVersionLockstepTests` and the
canary can all stay green forever while the shipped artifact is built from project references. The
claim lived in a comment, and a comment is not a guard — the same shape as the schema registry that
`ProductionSchemaRegistryTests` now scans source for.

---

## Why this plan orders everything else

A published package version is **permanent**, and the CalVer is day-resolution — one package release
per calendar day, recovery is tomorrow.

⚠ **An earlier draft of this plan overstated what follows from that, and the correction matters.**
It said a shared-library change *"must land before the first `packages-v*` tag, or it costs another
version and another day"*, and concluded it was therefore **a prerequisite of the tag**. That does
not follow. Only **re-pushing an existing version** is impossible; publishing `2026.3.920` and then
`2026.3.925` is ordinary. "Would cost a second version" is a *preference*, not a constraint, and
nothing in the immutability of the feed orders 00002 ahead of the release.

✅ **Settled: 00002 lands AFTER the first package release, as the second package version.** The
release then carries the pipeline change and nothing else, so a failure in Phase D is unambiguously
the pipeline rather than one of two candidates. 00002 ships against a pipeline already proven. The
cost is accepted: two package releases on two different days, and `E1` stays a **manual** retest
item for the first release.

⭐ **That choice simplifies three things, which is a fair sign it is the right one.**

| | |
|---|---|
| Phase A | No shared-library change lands before the tag, so there is **no public-surface change to review** — Phase A becomes verification of the surface that already exists, not a freeze of a new one |
| C0 | The parked branch already holds **identical** shared-library code, so evidencing neutrality is running its suite as-is. ⭐ The cross-branch port — the step this repo's own history says is treacherous, because after the rewrite neither counts nor patch-ids can say what has been carried — **disappears entirely** |
| Attribution | One variable changes at a time |

✅ **Settled: the WHOLE retest gates the package tag**, not just the write-path items. Phase B
therefore runs **before** Phase C rather than alongside it. ⛔ This is the conservative reading and
it was chosen deliberately over gating on `E1`–`E5` alone: the five that exercise one shared library
each are the ones that *could* expose a defect the feed cannot take back, but a tag that cannot be
undone is not the place to be clever about which checks matter.

⚠ **The consequence is that all eight manual items sit on the critical path to the tag**, `E1`
included, and `E1` is manual because 00002 moved. Nothing proceeds to Phase C until a human has
driven the app.

---

## Decisions

| Decision | Why |
|---|---|
| The release **selects package mode explicitly**, and a guard asserts it | A comment asserted it and was wrong for the life of the feature. Whatever fixes this must be checkable, or it rots back to the same state with the same silence |
| The release restores from the **published feed**, not a locally-packed one | The claim being bought is that the app users run is built from the bytes a consumer of the feed would get. Packing locally in the release run proves the `PackageReference` mechanics and nothing about the feed |
| Provenance is a **build-time guard plus archived restore evidence** | ⛔ There is no artifact-level tell to find: `Directory.Build.targets:87` sets `CopyDocumentationFilesFromPackages` in package mode specifically to erase the one known difference, and the publish strip removes `*.xml` either way — *"Publish output is unaffected either way"*. So the guard fails the build when the mode is off, and the run archives which **source** and version each of the eleven resolved from. Only the second separates the published feed from a local folder, since a folder source is exactly as valid to NuGet as a remote one |
| `SharedPackageVersion` is a **committed pin**, in the **root** `Directory.Build.props` | An explicit value is not a default, so `Directory.Build.targets:148`'s refusal is satisfied. The release then needs no inputs and is reproducible from its tag alone; git history records every version move; the bump is a diff someone approves. ⚠ The **root** file, not `src/`'s — test projects consume the packages too and only the root reaches them. The canary's `-p:` on the command line is a global property and still overrides |
| `release.yml` gains `packages: read`, and forks lose the ability to cut a release | The private feed cannot be restored without it, and this is the narrowest grant that works. ⭐ The cost is **temporary**: the destination is public nuget.org, where no credential is needed — so it is not worth designing around |
| `LICENSE` and the package metadata both name **Brian Bennewitz** | They disagree today — `LICENSE` says *"ClaudeForge Contributors"*, `Directory.Build.props:43` says *Brian Bennewitz*, and the packages ship **no `<Copyright>` at all** because inventing one that contradicts `LICENSE` was judged worse than omitting it. A single named holder is unambiguous for MIT, already matches the shipped `<Authors>`, and survives the move to a repository that is not ClaudeForge |
| Package `<Description>`s lose their **internal** lines only | `AgentForge.Core`'s ends *"Invariant: AgentForge.\* must never reference ClaudeForge.\*. AssemblyLayeringTests enforces it"* — a test class no consumer can see, and already recorded in `CLAUDE.md`. The outward-facing paragraphs are accurate and specific; a rewrite would most likely make them blander |
| [`00002`](00002-claude-code-real-config-locations.md) lands **before** the packages tag | Immutable feed, day-resolution CalVer |
| ⛔ **The release is cut from a ClaudeForge-only branch** — no OpenCodeForge code in the tree | Maintainer's decision. ⓘ The shipped artifact already contained none: no `ProjectReference` from the app or from any of the eleven, no OpenCode assembly in the Release output, and `release.yml` deliberately excludes the `opencodeforge-v*` tag. The branch requirement is about the tree it is cut from |
| The branch is made by **subtracting from this HEAD**, and this branch is **parked** | A subtractive change on a known-good tree, verifiable by the suite and the trim gate. Rebuilding from `main` would mean hand-porting hundreds of commits past a history rewrite, after which — as this repo has measured — neither commit counts nor patch-ids can tell you when the port is complete. OpenCodeForge rejoins after the release, in the world where the packages exist |
| ⚠ **Neutrality is evidenced once on the parked branch before the tag** | Extraction removes ~941 tests and **50 files that exercise `AgentForge.*` from the OpenCode side** — the only non-Claude consumer of libraries about to become immutable. ⓘ The *guards* survive: `NeutralLayerDefaultsTests` is a source scan of the neutral layer itself, not a comparison between products, and `AssemblyLayeringTests` loses only a vacuous direction. What is lost is proof by use, so it is produced once, deliberately, rather than assumed |
| The guard fires on a **local publish too**, with a named escape hatch the evidence records | `publish.ps1` is *"the canonical entry point — same script developers run locally"*. Guarding CI alone rebuilds the local-versus-released divergence this plan exists to close; guarding with no exit means credentials for anyone publishing from a fresh clone. A hatch that the archived evidence names is the only shape that keeps one default without hiding a departure from it |
| The retest gates the **app** release, not the **package** tag | The packages are library code; the retest regresses the ClaudeForge UI. Blocking the tag on a UI pass would couple two things that fail for different reasons |

### Alternatives dismissed

| Rejected | Why |
|---|---|
| Leave the release on `ProjectReference`; treat the packages as publish-only artifacts | Then the canary is the only consumer, the split is decorative, and the two comments above stay wrong. It also means the code users run has never been built the way the packages are consumed |
| Make package mode the **default** | Every developer's build would then need feed credentials. `nuget.config`'s entire source-mapping design exists to prevent exactly that, and says so |
| Pin `SharedPackageVersion` in a props file | It drifts from the tag silently, which is the failure the targets file already refuses to allow |
| Fix the two comments and stop | The comments are a symptom. Correcting them leaves the release built from project references, which is the thing being asked for |

---

## Scope

**In**

- The publish path selecting package mode, and where the version comes from.
- `release.yml`: the mode, the version, and `packages: read`.
- A guard that fails when the release stops consuming packages.
- Sequencing [`00002`](00002-claude-code-real-config-locations.md) ahead of the tag.
- Correcting `ci.yml:132` and `package-canary.ps1:10` **to whatever ends up true**.
- `CHANGELOG.md` — how the app is built is not a user-visible change, so this is a note in the
  work-state document rather than a changelog entry, unless the package build differs observably.

**Out**

- ⚠ **OpenCodeForge's release has the same gap** — `release-opencodeforge.yml` also never sets the
  mode. Deliberately out of scope: that app is parked, and fixing both at once means neither
  fix is verified against a real release. It is recorded here so it is not rediscovered.
- Whether the shared libraries leave this repository (the standing strategic question).
- The retest items themselves; this plan sequences them, it does not perform them.
- The `LICENSE` / `CopyrightHolder` and nuspec `<Description>` questions already open against 00001.

---

## Open questions — answer before the tag, not after

ⓘ **All of this plan's open questions have been answered; they are recorded in *Decisions* above.**
Whether forks must be able to release (no — and the cost is temporary, because the destination is
public). Where `SharedPackageVersion` comes from (a committed pin in the root props). What proves
provenance (a build-time guard plus archived restore evidence, because `Directory.Build.targets:87`
deliberately erased the one artifact-level difference that existed). Whether the guard fires
locally (yes, with a named escape hatch the evidence records).

⚠ **One thing to hold onto while implementing.** The escape hatch must be **named and recorded**,
never silent. A publish that quietly took the other path while the release claimed package mode is
this plan's own founding defect pointed the other way.

---

## The sequence

Six phases, in order: **0 → A → B → C → D → E**. Each step names what shows it worked; no step is
complete on a green build alone. ⛔ Nothing here runs in parallel — Phase B gates Phase C because a
published version cannot be taken back, and Phase E follows Phase D because its whole value is
landing on a pipeline that has already been proven.

### Phase 0 — extract the ClaudeForge-only branch

Everything else happens on that branch. Doing it first means the suite, the trim gate and the canary
all measure the tree that will actually be released.

| Step | Verification |
|---|---|
| 0a · Branch from this HEAD; delete the six OpenCodeForge projects | `src/OpenCode.Avalonia`, `src/OpenCode.Sdk`, `src/OpenCodeForge`, and `tests/OpenCode.Sdk.Tests`, `tests/OpenCode.Avalonia.Tests`, `tests/OpenCodeForge.Tests` are gone |
| 0b · Remove their six `ClaudeForge.slnx` entries, `release-opencodeforge.yml`, the CI steps, the six OpenCode grants in `AssemblyInfo.InternalsVisibleTo.cs` (lines 60-62 and 78-80), and the **OpenCodeForge entry in `src/publish/PublishApps.ps1`** | `BuildFilePathIntegrityTests` green **both ways** — every project on disk listed, and every listed project present; it guards both directions and a deletion can break either. ⭐ It also scans `src/publish/`, `scripts/` and `packaging/`, so the `PublishApps.ps1` row's `ProjectPath` reddens there rather than surviving to the release cut. Plus `SharedFriendGrantsTests` for the grants file |
| 0c · ⛔ **`src/PACKAGE-README.md` first — it is packed into all eleven packages** | `src/Directory.Build.props` packs it as `PackageReadmeFile` for every project without its own README, and lines 3 and 18 name OpenCodeForge. Left alone, the **immutable** published packages describe a product that is not in the tree they were built from. ⚠ **No existing guard covers this file**: `BuildFilePathIntegrityTests` scans root `*.md` and area `AGENTS.md` only. Verify by reading the `README.md` inside a packed `.nupkg`, not the source file |
| 0d · Purge references in `AGENTS.md`, `CONTRIBUTING.md`, `docs/AVALONIA-GOTCHAS.md`, `src/ClaudeForge/ViewModels/AGENTS.md` and `docs/MANUAL-RETEST-PLAN.md` | `BuildFilePathIntegrityTests` green — it scans root `*.md` and every area `AGENTS.md`, so a path naming a deleted project fails there. ⓘ `MANUAL-RETEST-PLAN.md` is included because **Phase B reads it** and it currently describes OpenCodeForge surfaces |
| 0d2 · Delete `scripts/probe-opencode.ps1` and `scripts/refresh-opencode-db-schema.ps1`, and the OpenCode half of `.github/workflows/schema-refresh.yml` | ⚠ **No guard catches these.** They are scripts, not references, so `BuildFilePathIntegrityTests` has nothing to flag — they would simply sit there dead, and the schema-refresh workflow would keep fetching schemas for a product that is not in the tree. Verify by grepping the whole tree for `opencode` case-insensitively and reading every remaining hit |
| 0e · **Delete** `docs/OPENCODEFORGE-PLAN.md` on the release branch | It is not a document with OpenCodeForge references in it — it is entirely about OpenCodeForge, so purging references from it is incoherent. ⛔ Delete rather than edit, and **only on the release branch**: the parked branch keeps it, and it is the record needed when OpenCodeForge is revisited |
| 0f · Full suite and trim gate on the new branch | Green at the **reduced** count. ⚠ Write the expected number down first: the drop from ~4,429 should be about **941**, and a much smaller drop means projects are still being built. ⓘ The trim gate is now a **two-RID-set, one-app** matrix rather than twelve publishes — six, for ClaudeForge alone |

⛔ **The parked branch is not deleted and not merged.** It is the OpenCodeForge continuation and it
is needed again at C0 below.

ⓘ **Checked, and clean:** no test project outside `tests/OpenCode.*` references OpenCode, and
neither the ClaudeForge app nor any of the eleven packable projects holds a `ProjectReference` to
one. The six deletions are therefore genuinely subtractive — this was verified rather than assumed,
because a single incoming reference would turn Phase 0 from a deletion into a refactor.

### Phase A — confirm the shared-library surface

⭐ **Nothing changes the libraries here.** 00002 is Phase E. This phase establishes that the surface
about to become permanent is the surface already proven, which is a much cheaper claim than freezing
a new one.

| Step | Verification |
|---|---|
| A2 · The eleven public-surface baselines are **unchanged** | `PublicSurfaceBaselineTests` green with **no regeneration**. ⛔ If a baseline needs regenerating, something changed a shared library and this phase's premise is false — stop and find out what, rather than accepting the new baseline |
| A3 · Full suite, Debug | 0 failed, at Phase 0's reduced count. ⚠ The skipped count is machine-dependent: 11 or 12 depending on whether `artifacts/localfeed` holds packages |
| A4 · Trim gate, six RIDs | ⚠ **6/6, not 12/12** — the release branch has one app. Zero IL diagnostics. A green Debug suite is not evidence here: an `IL2026` once broke the Release publish for three phases while thousands of Debug tests passed over it |
| A5 · Package canary on the exact commit that will be tagged | `pwsh -NoProfile -File scripts/package-canary.ps1` passes end to end |

⚠ **A5 runs on the commit being tagged, not an earlier one.** The canary validates whatever it packs;
run on a stale commit it certifies a build nobody is shipping.

### Phase B — the retest

| Step | Verification |
|---|---|
⛔ **This phase gates the package tag, not only the app release.** All eight items complete before
Phase C. A published version cannot be taken back, so nothing proceeds on a partial retest.

### ⛔⛔ The build under test must be PACKAGE MODE, or this phase validates the wrong artifact

Phase B runs before Phase C, so no packages exist on the feed yet — which means the obvious build to
retest is a `ProjectReference` one, while Phase D ships `PackageReference`. **The one phase gating an
irreversible step would then exercise an artifact nobody ships.**

⚠ **Assuming the two modes are equivalent is the precise move that created the defect this plan
exists to fix.** They are close by design and the publish output is stripped identically — but the
canary found a real difference (`Directory.Build.targets:70-87`) **by measurement**, and only then
was it erased on purpose. "Close enough" here is a belief, not a result.

The circularity resolves with a **locally-packed** package-mode build — the same reference mechanics
as the release, without needing the feed to exist:

```
# -CanaryVersion pins the pack to the version Phase C will publish, so the retested
# build and the shipped build differ ONLY in which feed answered the restore.
pwsh -NoProfile -File scripts/package-canary.ps1 -PackOnly -CanaryVersion <the intended CalVer>

dotnet publish src/ClaudeForge -c Release -r win-x64 --self-contained true `
  -p:UseSharedPackages=true -p:SharedPackageVersion=<the same value>
```

⭐ **Packing at the intended release version rather than the default throwaway is what makes this
worth doing.** It reduces the gap between what was retested and what ships to a single variable —
the restore source — and that variable is exactly what D1's `.nupkg.metadata` check reads.

⛔ **That publish cannot go through `src/publish/publish.ps1`**, which wipes `artifacts/localfeed` —
deliberately, so a release never silently consumes a stale local package. Here the local feed is the
point, so drive `dotnet publish` directly.

ⓘ `docs/MANUAL-RETEST-PLAN.md`'s *Build under test* row names a plain Release publish and must be
updated to this, or the next person retests the wrong thing for the documented reason.

| Step | Verification |
|---|---|
| B1 · `E1` — comments and formatting survive a save | ⚠ **Manual**, because 00002 is Phase E. A hand-written `settings.json` with comments, deliberate key order, blank lines and odd indentation diffs byte-identical except the edited value. ⭐ Also drive a skill `.md` with a `description: >-` folded block and one ending in a blank line — the artifact write path moved under this item when the YAML parser was replaced |
| B2 · `E2`–`E5` | Per `docs/MANUAL-RETEST-PLAN.md`. ⭐ These four plus `E1` exercise **one shared library each** — `JsonC`, `AgentForge.Core` + `LayeredEditors`, `AgentForge.Core.Backup`, `AgentForge.Sdk`, `AgentForge.Artifacts` — which is exactly the code about to become permanent. They are the highest-value items in the phase |
| B3 · `F1`, `F2`, `F4`, `F6` | Fixed, awaiting a look at a running UI. Colour and glyph judgements; these need eyes and cannot be automated |

### Phase C — publish the packages

| Step | Verification |
|---|---|
| **C0 · Neutrality, evidenced on the parked branch** | ⭐ **No port needed.** With 00002 deferred to Phase E, `feat/agentforge-opencodeforge` already holds **identical** shared-library code, so this is running its suite as it stands. ⛔ **Pass criterion: the ~941 OpenCode tests are green, and a failure BLOCKS C2** — it means the surface about to become permanent is not neutral, which is the one thing this step exists to find out. ⚠ Confirm the two branches' shared-library trees really are identical before relying on that — `git diff <release-branch> feat/agentforge-opencodeforge -- src/AgentForge src/LayeredEditors src/JsonC` should be empty. Do not infer it from the plan saying so |
| C1 · Preflight with a real `read:packages` token | ⛔ **Gate 3 is SKIPPED when no token is set**, and the 2026-09-14 run that "passed" exercised gates 1–2 only. The script says so in its own output — read it, do not infer it |
| C2 · Push `packages-v<CalVer>` | `release-packages.yml` green; all eleven ids resolvable from the feed at that exact version |

⛔ **C2 is irreversible.** One package release per calendar day; a mistake is recovered tomorrow, not
by re-pushing.

### Phase D — the release that consumes them

| Step | Verification |
|---|---|
| D1 · Publish path selects package mode at C2's version | ⭐ **The evidence mechanism exists and is named, rather than assumed:** `.nupkg.metadata`, written beside each restored package in the global packages folder, carries `{"version":…,"contentHash":…,"source":"https://…"}`. Reading `source` for each of the eleven is what distinguishes the **github** feed from `artifacts/localfeed`. ⚠ Verify the file is actually present for all eleven before relying on it — the restore writes it, a cache hit may not |
| D2 · `packages: read` in `release.yml` | A real workflow run restoring from the private feed |
| D3 · The build-time guard, and the recorded escape hatch | It **fails** when package mode is dropped — prove that by dropping it deliberately and watching it redden. And the archived evidence **names the hatch** when it was used: a publish that quietly took the other path while the release claimed package mode is this plan's founding defect pointed the other way |
| D4 · Full suite and trim gate in package mode | Green, at the published version, with no local feed present |
| D5 · Correct `ci.yml:132` and `package-canary.ps1:10` | They describe what D1–D3 made true |
| D6 · Cut the release | The shipped artifact passes D3. ⚠ **The tag points at the release branch, not `main`** — `release.yml` triggers on `v*.*.*` and is branch-agnostic, so this works mechanically, but it is a departure worth stating: a locked decision keeps split work out of `main` until the maintainer approves, so this release is cut from a feature branch. ⓘ If that is not acceptable, it is a decision to make **before** Phase C, not after the packages are permanent |

### Phase E — 00002, as the second package version

Only once the pipeline is proven. [`00002`](00002-claude-code-real-config-locations.md) carries its
own steps and verifications; what belongs here is the sequencing around them.

| Step | Verification |
|---|---|
| E1 · Implement 00002 on the release branch | That plan's own step verifications |
| E2 · Regenerate the eleven public-surface baselines, and **read the diff** | `PublicSurfaceBaselineTests` green. A baseline regenerated without reading it records whatever happened, including a mistake |
| E3 · Re-evidence neutrality on the parked branch | ⛔ **Now the port is real** — 00002's commits have to be carried across, and this repo has measured that after the history rewrite neither commit counts nor patch-ids can say what has been carried. Carry by explicit cherry-pick of a named list |
| E4 · Second `packages-v<CalVer>` tag, then bump the committed `SharedPackageVersion` | A different calendar day from the first. ⭐ The bump is a reviewable diff, which is the point of the pin |
| E5 · `E1` re-run **automated** against a scratch home | The dividend 00002 was originally folded in for: set `CLAUDE_CONFIG_DIR` on the child process, drive the app, diff the fixture, and prove the real `~/.claude` untouched by a before/after listing |

---

## Downstream — the dedicated repository, and a package this plan does not build

The destination for the product-neutral libraries is a **dedicated repository, published to
nuget.org**, gated on them being fully tested. The private GitHub feed is therefore a **test rig,
not the end state**, and the fork-credential cost this plan accepts is temporary — it disappears
when the packages are public. The Claude-specific halves stay in this repository and are consumed
through the published feed.

⚠ All eleven packable projects are product-neutral **today**; nothing Claude-specific is packaged
at all. `ClaudeForge.Sdk.Claude` and `ClaudeForge.Avalonia` are not packable, so that arrangement
implies both a move and two new packages.

### The XAML-quality audit belongs in that repository, as a package

The theme, styling and accessibility audits — and the equivalents being built against Avalonia
elsewhere — should eventually ship as a **proper NuGet package that enforces XAML quality**, rather
than as tests inside one app's test project.

⛔ **The payoff starts before any repository moves.** Roughly fifteen such guards exist, and the
generic ones — `ThemeResourceIntegrityTests`, `NoDeadBrushTokensTests`,
`AxamlAccessibilityCoverageTests`, `DangerSurfaceMarkupTests`, `SeverityGlyphFontSizeMarkupTests`,
`CautionBrushIsNotUsedAsTextTests` and the three `ItemsSourceBound*` suites — live under
`tests/ClaudeForge.Tests/`. **OpenCodeForge is not covered by them.** A guard that scans one app's
folders is not a quality bar; it is one app's quality bar, and the second app's markup drifts
unobserved. `TemplatePartAutomationNameTests` and `TreeChevronAutomationNameTests` already sit in
`LayeredEditors.Avalonia.Tests` and show the shape the rest should take.

⚠ **It is a different KIND of package, and that has consequences worth settling early.**

| | |
|---|---|
| Not switched by `UseSharedPackages` | Every project consumes it, including the eleven. It is an ordinary `PackageReference` with `PrivateAssets="all"` in **both** modes — it does not belong in the `Directory.Build.targets` switch, whose condition is `IsPackable != true` |
| Delivery is undecided | A Roslyn analyzer under `analyzers/dotnet/cs`, or an MSBuild task plus `build/*.targets`. The current guards are MSTest tests that read files from disk, which is neither |
| ⛔ Open question | A Roslyn analyzer sees **C#**, not AXAML. Reaching the markup means AXAML arriving as `AdditionalFiles`, which has to be established rather than assumed — if it does not, the MSBuild-task shape is the only one that works |
| Naming | Neither `AgentForge.*` nor `LayeredEditors.*` fits — it knows nothing about agents or editors. It needs its own family prefix, and `CLAUDE.md` records what a family of one costs: four uncoupled edits, one of which only the package canary catches |

Out of scope here. Recorded so the shape is known before the split plan is written, and so the
extraction from `tests/ClaudeForge.Tests/` can be done for its own sake in the meantime.

## What this plan does not claim

It does not decide whether the shared libraries leave this repository; 00001's "not in scope" section
remains the current answer. It does not fix OpenCodeForge's release. And it does not make `E2`–`E5`
automatable — those need a project root, which is still chosen through a UI folder picker.
