# 00006 — The test suite moves from MSTest to xUnit v3

> Status: **approved 2026-09-23**. Supersedes nothing. Starts after `plans/00005` has merged.

## Why

Every other repository in this family tests with **`xunit.v3` on Microsoft.Testing.Platform (MTP)** —
`Bennewitz.Ninja.AppServices` and `Bennewitz.Ninja.ScopedEditors` both do, and both ported suites
that began life here. OpenForge2k is now the one MSTest suite left. Tests move between these
repositories (plans/00005 ported about 33 files out, in both directions of the same idioms), so
every move today is a translation, and each translation is a chance to drop an assertion's meaning.
One framework removes that tax.

## What is being moved — measured 2026-09-23 at `3447783`

| Project | Classes | Tests (total) | Headless session |
|---|---:|---:|---|
| `JsonC.Tests` | 6 | 73 | — |
| `AgentForge.Artifacts.Tests` | 2 | 35 | — |
| `ClaudeForge.Avalonia.Tests` | 2 | 42 | — |
| `AgentForge.Core.Tests` | 74 | 810 (8 skipped) | — |
| `AgentForge.Sdk.Tests` | 29 | 370 | 1 file |
| `ClaudeForge.Sdk.Claude.Tests` | 19 | 208 | 1 file |
| `ClaudeForge.Tests` | 154 | 1,759 (3 skipped) | 17 files, plus the linked root bootstrap |
| **Total** | **286** | **3,297 (11 skipped)** | |

MSTest surface in use, by count of occurrences under `tests/` (plus the two linked root files):

| MSTest | Uses | xUnit v3 |
|---|---:|---|
| `[TestClass]` / `[TestMethod]` | 293 / 3,066 | *(none)* / `[Fact]` |
| `[DataRow]` (26 files) | 260 | `[Theory]` + `[InlineData]` |
| `[DynamicData]` | 1 | `[MemberData]` |
| `[TestInitialize]` / `[TestCleanup]` | 77 / 85 | constructor / `IDisposable` or `IAsyncLifetime` |
| `[AssemblyInitialize]` (root `HeadlessSessionBootstrap.cs`) | 1 | `[assembly: AssemblyFixture]` — the peers' port of this exact file |
| `TestContext.CancellationToken(Source)` | 54 | `TestContext.Current.CancellationToken` |
| `[DoNotParallelize]` / `[Parallelize]` | 16 / 8 | `CollectionBehavior` / `[Collection(..., DisableParallelization = true)]` |
| `[Timeout]` | 4 | `[Fact(Timeout = …)]` — ⚠ xUnit v3 honours it for **async** tests only |
| `Assert.Inconclusive` (14 files) | 30 | `Assert.Skip` |
| `[Ignore]` | 3 | `[Fact(Skip = …)]` |
| `Assert.*` | 6,228 | `Assert.*`, and `MessageAssert.*` where a message is carried |
| `StringAssert.*` / `CollectionAssert.*` | 334 / 309 | `Assert.Contains` / `StartsWith` / `Equal` / `Equivalent` … |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **`xunit.v3` on MTP**, `Microsoft.Testing.Extensions.TrxReport`, `global.json` `test.runner = Microsoft.Testing.Platform` | Exactly the peers' shape, so a test moves between repositories as a copy, not a translation |
| 2 | **The runner moves first, on its own.** MSTest goes onto MTP (step 1) with no test changed, and must report the identical name set | Separates "the runner changed" from "the framework changed" — a count that drops in step 1 cannot be blamed on a converted assertion |
| 3 | **Every message survives.** A ported `AreEqual(e, a, msg)`-shaped assertion becomes `MessageAssert.Equal(e, a, msg)` — a helper the rewriter emits (`--emit-helpers`), derived from the peers' hand-written one and extended for the rules they never needed | Messages are this repository's convention — they name the culprit. xUnit gives `Equal`/`Null`/`Same`/`Contains` no message parameter; `Assert.True(Equals(…), msg)` loses the diff |
| 4 | **Conversion is done by a syntax-aware (Roslyn) rewriter, not by text substitution — and the rewriter is kept, in `Bennewitz.Ninja.Templates`** as `scripts/mstest-to-xunit.cs` | 6,871 call sites whose argument COUNT decides the target (`AreEqual` with and without a message, with a delta). A regex cannot see argument boundaries in nested calls and interpolated strings. Templates defines the family's test shape (`bbpkg` ships `xunit.v3` on MTP) and already keeps file-based C# tools in `scripts/`; the tool is not specific to this repository, so it does not live in it |
| 5 | **No test is renamed, merged, split, rewritten or added during the move** | Identity is the proof (step 0). A conversion that also "improves" a test cannot be shown to have lost nothing |
| 6 | **Parallelism does not change.** Every assembly serial today stays serial | The suite is sequential by design (headless dispatcher, global-static seams, four measured flakes). Re-enabling parallelism is a separate decision with its own evidence |
| 7 | **One branch, one PR, one commit per project**, each commit green | MTP runs both frameworks side by side (verified in step 1 before relying on it), so a half-converted solution is a valid state to review and to bisect |
| 8 | **`Inconclusive` becomes `Skip`**, and the reconciliation counts it | xUnit v3 has no inconclusive outcome; those 30 sites already mean "cannot run here" |

Dismissed: **staying on MSTest** (the family has moved; every port pays the translation); **xUnit v2**
(not what the peers run, and no assembly fixtures); **TUnit** (not the family's choice); **converting
by hand** (6,871 sites; hand edits are where messages quietly drop); **one big-bang commit**
(unreviewable, and a regression could sit in any of 286 classes).

## Scope

In: the seven test projects, the two linked root files (`HeadlessSessionBootstrap.cs`,
`HeadlessSessionBootstrapTests.cs`), `tests/Directory.Build.props`, `global.json`, every `dotnet test`
invocation in `.github/workflows/` and `scripts/`, and the live docs that name MSTest or its attributes (`CLAUDE.md`,
`PLATFORM.md`, the root `AGENTS.md`, and the two area `AGENTS.md` files under
`src/AgentForge.Core/Settings/` and `src/ClaudeForge/ViewModels/Editors/`). `CHANGELOG.md`,
`PROGRESS.md` history and approved plans are records and stay as written.

Out: new tests; parallelism; the dead `coverlet.collector` `Update` entry beyond deleting it
(nothing collects coverage today, and MTP needs a different extension if anyone wants it); any change
under `src/`.

## Steps

**0 · Baseline, and prove the proof can fail.**
Record every test's fully-qualified name and outcome per assembly at the starting commit, into
`artifacts/xunit-move/baseline/` (gitignored). Measure the message-bearing assertions per project
with the rewriter's analysis pass. Then canary the comparison script: delete one test from a copy of
the list, and one `[DataRow]` — it must name both.
*Verify:* 7 assemblies, 3,297 names, 11 skipped; the canary names exactly what was removed.

**1 · MSTest onto Microsoft.Testing.Platform — no test touched.**
`global.json` test runner, MSTest's MTP runner enabled, test projects `OutputType=Exe`.
⚠ Measured in the pilot: an `xunit.v3` executable runs **xUnit's native console runner** unless MTP
is selected — `--report-trx` was an unknown option there — so the MTP selection is part of each
project's switch, and step 7's TRX comparison depends on it; CI moves to
`dotnet test --solution` and `--report-trx`; the one `--filter` in a workflow
(`model-catalog-refresh.yml`) is re-expressed and shown to select the same tests.
⚠ Premise to verify before step 3: **a solution with one MSTest-on-MTP project and one xUnit v3
project runs under one `dotnet test`** — measured with a throwaway xUnit project, then deleted. If it
does not, decision 7 falls back to converting all seven in one commit, and this plan comes back
first.
*Verify:* the name set equals step 0's, byte for byte; CI green on all three OSes.

**2 · The rewriter, and `MessageAssert`.**
The rewriter — `Bennewitz.Ninja.Templates` `scripts/mstest-to-xunit.cs` — maps the table above;
anything it cannot map it leaves untouched and lists. It is run at a **pinned Templates commit**,
named in every conversion commit here, so any conversion can be reproduced from that commit and the
baseline. A defect found mid-move is fixed there first, then the affected project is re-run from its
pre-conversion state — never patched by hand on top of a converted file.
`MessageAssert` and the `DoNotParallelize` collection definition are written by the same tool
(`--emit-helpers --namespace <ns>`), so helpers and rules always come from one commit.
✅ **Already built and piloted** — Templates `17e6bd8` on `feat/mstest-to-xunit`. On a scratch clone
of this repository at `3447783`, `JsonC.Tests` converted with nothing unmapped, built with 0
warnings and ran 73 of 73, the same identities by fully-qualified name; the one theory's 21 rows
match by count, since xUnit names data rows differently. That build added six rules the first
table lacked (xUnit1013, xUnit1016, xUnit2007, xUnit2013, xUnit2029, and CS0619 for a throw-lambda).
⚠ The step still stands for the other six projects: expect their builds to surface more analyzer
rules, each fixed in the tool first, never by hand on converted output.
*Verify:* on `JsonC.Tests` the "could not map" list is empty or every entry is converted by hand and
named in the commit.

**3 · Pilot: `JsonC.Tests` (73).**
*Verify:* same 73 names; three assertions canaried — one message-bearing `Equal`, one
`CollectionAssert`, one `[DataRow]` row — each fails, and the message still reads in the output.

**4 · The rest without a headless session:** `AgentForge.Artifacts.Tests`, `ClaudeForge.Avalonia.Tests`,
`AgentForge.Core.Tests` (the `[Timeout]`s and most `Inconclusive`s live here). One commit each.
*Verify per commit:* name set equal to baseline; `Inconclusive` → `Skip` accounted by name; each
`[Timeout]` test is async or is made async, and a canary that hangs is killed by it.

**5 · The headless projects:** `AgentForge.Sdk.Tests`, `ClaudeForge.Sdk.Claude.Tests`, then
`ClaudeForge.Tests` last. The bootstrap becomes an assembly fixture exactly as the peers ported it,
and `HeadlessSessionBootstrapTests` comes with it — it is what proves set-up still happens before the
first test rather than inside it.
*Verify:* name sets equal; `HeadlessSessionBootstrapTests` green **and** red when the warm-up
dispatch is removed; the full suite three times on Windows with no flake, and green on CI's three OSes.

**6 · Drop MSTest, and correct the prose.**
Remove the `MSTest` and dead `coverlet.collector` entries; `CLAUDE.md` loses "MSTest, not xUnit";
`PLATFORM.md` and the three `AGENTS.md` files updated where they describe `[TestMethod]`, `DataRow`
or MSTest isolation; `PROGRESS.md` records the move.
*Verify:* no `Microsoft.VisualStudio.TestTools` or `MSTest` reference anywhere under `tests/` or the
root; build 0 warnings.

**7 · Gate.**
Final name set against step 0: equal, apart from a written list of every intended difference
(`Inconclusive` → `Skip` outcomes; theory rows' display names), each reconciled by name. Package
canary green. A Release trimmed publish is unaffected by construction, and is run anyway.
*Verify:* the reconciliation list; CI green; the package canary's own output.
