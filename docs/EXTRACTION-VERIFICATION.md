# Extraction verification — is the split complete, and did ClaudeForge regress?

> **What this document is.** A measured answer to two questions asked on 2026-09-12: whether the
> extraction of ClaudeForge's guts into `AgentForge.*` / `LayeredEditors.*` is **complete**, and
> whether it **regressed the shipping app**. Every claim here cites what was run or read. Where
> something could not be verified, it says so instead of rounding up.
>
> Baseline throughout is **`v2026.3.901`** (`3c7aaab`, 2026-09-01) — the latest tagged public
> release, which is what users actually have. It predates every `AgentForge.*` assembly.

---

## Answers, up front

| Question | Answer |
|---|---|
| Did ClaudeForge regress? | **No regression found.** Across ten pages driven in both builds, the log event sequence is identical, and the trim matrix is clean on all six RIDs for both apps. |
| Is the split complete? | **The Core/SDK half is; the app/shell half is much less far along than planned.** See §2. Seven Claude-named types also remain in the neutral layer — stranded, but unreferenced by OpenCode. |
| Is the extracted code actually *used*? | **Yes — it is 48.7% of what powers ClaudeForge**, up from 11.6%, and it is provably executing at runtime. See §2.0. |
| Was the permissions work finished? | **Yes, and as *redesigned*.** Both halves shipped. The roadmap simply never marked it, which is why it reads as open. |
| What is the real risk? | **Not the Claude-named code — the Claude-shaped *defaults*.** One of them was a live defect until this session. Name-based searching cannot find that class. |

---

## 1 · Regression check

### What was run

| Check | Result |
|---|---|
| Full suite, Debug | **4,266 passed · 0 failed · 11 skipped** |
| Release publish, **both apps × all six RIDs** | **12 of 12 clean — zero ILLink warnings** |
| Live app, baseline vs current, 10 pages each | Log event sequence **identical** except the one flag deliberately passed |

⭐ **The six-RID matrix had never been run for OpenCodeForge.** `TRIMMING.md`'s headline claim is
zero ILLink warnings across `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and
`osx-arm64`, and the document does not mention the second app at all. It now holds for both.

### The live comparison, and the number NOT to trust

Both builds were published Release `win-x64` and driven to the same ten navigation nodes with
`--deep-link`, then compared.

⛔ **The pixel comparison was attempted and is INVALID. Discard it.** The captures contain a Snagit
window, the desktop, and an antivirus popup sitting on top of the app, so the reported differences
(up to 61% of pixels) measure the desktop, not the refactoring. The first harness asserted the app
was the foreground window at capture time, which would have caught this; local antivirus blocked
that script as an infostealer signature — a script that both screenshots the screen and inspects
windows matches the heuristic — and the fallback harness had no such assertion. **The safeguard was
traded away to get past the blocker, which is exactly when a safeguard matters.**

✅ **The log comparison is the sound one, and it is a better signal anyway** — it is immune to
occlusion and it compares what the app *did* rather than how it looked.

Each build ran ten times. Both logs are **272 lines**. Timestamps, paths, version strings and
numbers were normalised away, and the remaining event shapes were counted and diffed:

```
The ONLY difference across 20 runs:
  baseline: [DebugFlags] active: --deep-link <node>
  current:  [DebugFlags] active: --deep-link <node>, --schema-source bundled
```

That flag is one **this verification passed deliberately**: the baseline predates
`--schema-source`, so the current build had to be pinned to bundled schemas or the two would be
rendering different schema copies and every settings page would differ for an unrelated reason.

Severity counts are identical on both sides:

| | FTL | ERR | WRN | exceptions |
|---|---|---|---|---|
| Baseline `v2026.3.901` | 0 | 0 | 10 | 0 |
| Current `HEAD` | 0 | 0 | 10 | 0 |

⚠ **The ten warnings are one per run and pre-existing** — the schema notice listing settings with
no structured editor (`allowedChannelPlugins`, `sandbox.*`, `skillOverrides`, …). Identical in both
builds, so it is not a regression. ⓘ An earlier count in this session reported *zero* warnings; that
was wrong — it matched `" WRN "` where the log writes `"[WRN]"`.

**Coverage this actually gives.** Per run the log records schema→editor construction for every
settings group (`[Editor.Rebuild] group=… editors=N`), Essentials card evaluation, profile
discovery, deep-link resolution and shutdown. That is the whole read-and-render path. ⛔ **It
exercises no save, no edit, no backup and no restore** — see *Not verified*.

### Structural check

`MainWindowViewModel`'s eleven `NavId*` constants are **byte-identical** between the tag and HEAD
(`welcome`, `essentials`, `claude-code`, `claude-desktop`, `version-info`, `effective-settings`,
`profiles`, `backup-restore`, `environment`, `memory`, `agents-skills`). The navigation surface
survived the extraction unchanged.

---

## 2 · Completeness

### 2.0 · How much of ClaudeForge the shared layer actually powers

> ⓘ **Corrected 2026-09-12.** An earlier revision of this document described the leftovers in §2.1
> as *"inert"*. That word was scoped to **the seven Claude-named types, with respect to OpenCode
> only** — it never described the shared layer, and read alone it wrongly suggests the extraction
> is dead code. It is not: it is nearly half of ClaudeForge. Measured below rather than asserted.

Hand-written `.cs` + `.axaml` lines per assembly, excluding build output and generated designer
files, read out of git at the tag and off disk at HEAD:

| | Baseline `v2026.3.901` | HEAD |
|---|---|---|
| Shared (`AgentForge.*` + `LayeredEditors.*`) | 8,629 | **42,217** |
| Claude-specific (`ClaudeForge`, `.Avalonia`, `.Sdk.Claude`) | 65,716 | 44,430 |
| **ClaudeForge's whole stack** | 74,345 | 86,647 |
| **Shared share** | **11.6%** | **48.7%** |

✅ **And it runs.** `[Editor.Rebuild] group=… editors=N`, which appears twelve times per run in
*both* builds' logs, is emitted by
`AgentForge.Avalonia.Shell/Settings/SettingsGroupEditorViewModel.cs`. Every settings group in
ClaudeForge is built by the shared shell. `src/ClaudeForge.Core` and `src/ClaudeForge.Sdk` no
longer exist; `ClaudeForge.csproj` references `AgentForge.Core`, `AgentForge.Sdk`,
`AgentForge.Avalonia.Shell`, `ClaudeForge.Sdk.Claude` and four `LayeredEditors.*` projects.

### 2.1 · ⚠ The app/shell half is much smaller than the plan assumes

The Core/SDK split is thorough: `ClaudeForge.Core` (11,719) and `ClaudeForge.Sdk` (11,007) are
gone, re-partitioned into five neutral assemblies plus a Claude-specific SDK.

The **app** barely moved:

| | Baseline | HEAD | Change |
|---|---|---|---|
| `src/ClaudeForge` | 41,371 | 38,301 | **−3,070 (−7%)** |
| `AgentForge.Avalonia.Shell` | — | 7,693 | new |

⛔ The plan carries an unmeasured estimate that *"`src/ClaudeForge` … Roughly 60/40 generic shell
vs Claude-specific — **an eyeball estimate from the file list, not a measurement**"*, which implies
roughly 24,000 extractable lines. The extracted shell is 7,693. The plan states the consequence
itself: *"if the shell share is materially smaller, the extraction is less valuable than this plan
assumes and the topology decision deserves a second look."*

⚠ **That −3,070 is NET, and gross extraction was larger** — the app also gained features in the
same window (Essentials, artifacts, schema refresh, backup). Separating the two needs per-file
history, which this measurement does not do. The direction is solid; the exact gross figure is not
claimed. `src/ClaudeForge` remains the largest assembly in the repository.

### 2.2 · What is still Claude-shaped inside the neutral layer

`CLAUDE.md` states the rule: *"`AgentForge.*` — product-neutral agent-configuration machinery.
Anything here must make sense for **both** products."*

### Seven Claude-named types declared in `AgentForge.*`

| Type | Where | Visibility |
|---|---|---|
| `ClaudeArtifactPaths` | `AgentForge.Sdk/Memory/ClaudeArtifactPaths.cs:39` | **public** |
| `ClaudeCodeLocation` | `AgentForge.Core/Platform/PlatformPaths.cs:288` | **public** |
| `ClaudeDesktopVersionProbe` | `AgentForge.Core/Platform/ClaudeDesktopVersionProbe.cs:30` | **public** |
| `ClaudeArtifactSources` | `AgentForge.Sdk/Memory/ClaudeArtifactSources.cs:42` | internal |
| `ClaudeEditableArtifactSources` | `AgentForge.Sdk/Memory/ClaudeEditableArtifactSources.cs:32` | internal |
| `ClaudePluginArtifactSource` | `AgentForge.Sdk/Memory/ClaudePluginArtifactSource.cs:33` | internal |
| `ClaudeScopes` | `AgentForge.Sdk/Memory/ClaudeScopes.cs:24` | internal |

Plus Claude-specific **members on neutral types**: `PlatformPaths.ClaudeHome` / `.ClaudeJsonPath` /
`.ClaudeMdPath` / `.ClaudeCodeLocation`, `ConfigFileType.ClaudeCodeSettings` /
`.ClaudeDesktopConfig`, `SchemaRegistry.ClaudeCodeProduct` / `.ClaudeDesktopProduct`, and
`EnvVarKey.MaxOutputTokens = "CLAUDE_CODE_MAX_OUTPUT_TOKENS"`.

✅ **Every one of these is live for ClaudeForge and unreachable from OpenCode** — they are residue
of the extraction, not dead code. A search of `src/OpenCode.Sdk`, `src/OpenCode.Avalonia` and
`src/OpenCodeForge` for every one of those seven type names returns **zero matches**. This is
incomplete extraction, not an active bug.

### ⛔ The part that DOES reach OpenCode — Claude-shaped defaults

**This is the finding that matters, and it is invisible to every search above.** A neutral type
whose *default* resolves to Claude data couples a second product to Claude without naming Claude
anywhere near the call site.

That is not hypothetical. `AgentConfigClientCore.FootprintService` returned
`new FootprintService()`, whose catalog defaults to Claude's seven `~/.claude` categories, and
neither OpenCode client overrode it — so both reported **Claude's** footprint as their own, and a
delete would have removed the other agent's data. Fixed this session in `765648a`.

⭐ **The plan predicted it exactly, including the trigger** (`docs/OPENCODEFORGE-PLAN.md`):

> `AgentConfigClientCore.FootprintService` also still does `new FootprintService()` — the neutral
> core defaulting to Claude's catalog … **Harmless while only ClaudeForge reads footprints; it
> becomes wrong the moment OpenCodeForge does.**

This session is the moment it described. The prediction sat in the document and the defect shipped
anyway, because nothing mechanical was watching for it.

Two more of the same shape remain, both currently harmless because only ClaudeForge reaches them:

| Site | Shape |
|---|---|
| `AgentForge.Core/Schema/SchemaSnapshotService.cs:23` | Parameterless ctor defaults its cache directory to `PlatformPaths.ClaudeHome` |
| `AgentForge.Core/Backup/RestoreSidecarCleanup.cs:59` | `claudeHome ?? PlatformPaths.ClaudeHome` |

### ⚠ Why the existing guard cannot catch any of this

`AssemblyLayeringTests` has three test methods, and all three are about **assembly references** —
that a shared project declares no product reference, and that no shared assembly references a
product. Claude-shaped code *inside* a neutral assembly declares no reference at all, so the guard
is silent on every row above. That is a gap in coverage, not a failure of the guard: it enforces
what it claims to.

---

## 3 · Permissions — complete, as redesigned

The plan's **third revision** walked back two earlier drafts after reading every implementation
body, and shrank Phase 6 from *"extract a shared permission core"* to *"define a ~50-line shared
vocabulary and leave two parallel implementations."* It also **rejected** the generic
`Decision<TRule>` shape on measurement.

Reality matches that decision:

| Piece | Expected by the revision | Actual |
|---|---|---|
| Shared vocabulary | ~50 lines | `AgentForge.Abstractions/Permissions/PermissionOutcome.cs` — **55 lines**, shipped in `a453063` |
| Claude's implementation | stays Claude-only | 13 files in `ClaudeForge.Sdk.Claude`, 6 + 2 in `ClaudeForge.Avalonia`, 8 + 5 in `ClaudeForge` |
| OpenCode's own implementation | 400–600 lines | **~1,790 lines** — `OpenCodePermissionModel` (500), `…EditorViewModel` (639), `…ToolViewModel` (295), `…RowViewModel` (96), view (238 + 22) |

**Nothing is outstanding.** The uncertainty is a bookkeeping artifact: the roadmap's at-a-glance
table marks ✅ only on phases 10, 13 and 14, leaving phases 1–9 unmarked even though phase 8
(*first runnable app*) demonstrably shipped and everything after it depends on them.

---

## 4 · Not verified

Stated plainly, because a verification document that implies more coverage than it has is worse
than none.

- ⛔ **No save, edit, backup or restore was exercised in either build.** The live comparison is
  read-and-render only. A regression in the write path would not appear in it.
- ⛔ **The visual comparison was not completed** — see the occlusion note in §1. Redoing it needs
  the desktop cleared of other windows, or a foreground assertion that local antivirus permits.
- ⚠ **Only `win-x64` was run *live*.** The other five RIDs were verified to publish cleanly, not to
  run.
- ⚠ **The size measurement in §2.0–§2.1 is lines, not semantics.** It shows how much code sits in
  which assembly; it does not show whether what remains in `src/ClaudeForge` is *genuinely*
  Claude-specific or merely un-extracted. Deciding that needs a read of the app's view-models, not
  a line count.
- ⚠ **The branch is 23 commits behind `main`**, including theming, accessibility and schema-race
  fixes. None of this verification says anything about those.

---

## 5 · What would close the gaps

1. **A guard for Claude-shaped defaults in the neutral layer.** The one class of incompleteness
   that has already produced a real defect, and the one nothing watches for. A source scan in the
   spirit of `ProductionSchemaRegistryTests` — which exists precisely because a registry cannot
   report whether it holds an `HttpClient` — would fit the repo's existing idiom.
2. **Extend the live comparison to the write path**, on a sandbox profile, so save/backup/restore
   are covered rather than assumed.
3. **Mark phases 1–9 in the roadmap**, so "was the permissions work finished?" has an answer at a
   glance instead of requiring a read of three revisions.
4. **Decide what the app/shell number means** — §2.1 measures it for the first time, and it is well
   short of the plan's estimate. Either more of `src/ClaudeForge` should move into
   `AgentForge.Avalonia.Shell`, or the estimate should be replaced with the measured figure and the
   topology re-argued from it. What should not happen is the eyeball estimate continuing to sit in
   the plan now that a real number exists.
