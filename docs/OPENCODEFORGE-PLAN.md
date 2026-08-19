# Plan — OpenCodeForge: a sibling app on a shared AgentForge foundation

> **APPROVED 2026-08-17.** Thirteen adversarial review passes applied. Fact-shaped per the
> repo's `AGENTS.md`: every claim cites a file, type, or member — never a line number.
> Counts measured against `main` @ `930eb41`.
>
> **Reading order for a fresh context:** Context → Roadmap at a glance → Scale → the eight
> hard problems → your phase. The review-pass log below records what was wrong in earlier
> drafts and why; it is kept deliberately, because several entries are traps a fresh reader
> would otherwise re-introduce (notably the schema load order and the union classification).
>
> **Trust calibration:** claims citing a specific file/type/member were verified against the
> implementation. Claims about OpenCode behaviour behind Spikes S1–S11 were **not** — treat
> those as hypotheses. See Risk 7 for the systematic optimism this plan had to correct for.
>
> **Spike progress (2026-08-17): 10 of 11 answered — only S5 (Desktop) remains.** Measured
> against **OpenCode v1.17.9** on the maintainer's machine plus the live schemas. Read the
> **Spikes** section before any phase; four earlier assumptions were wrong:
> **S11**'s mitigation (pre-resolving the models.dev ref) would have broken custom models ·
> **S1** was a false binary (array merge is **per-key**) · **S7** is a **deep merge**, not
> shadowing · **S3** surfaced two filesystem roots and a 60 MB backup mistake. The spikes
> also found that **a bad config bricks every OpenCode command** and that **config is not
> hot-reloaded** — both change product requirements, not just implementation.
>
> ⚠ **Everything was measured on an install that had never run a real session.** See the
> **Deferred re-checkpoint** section: 11 items must be re-validated against a used install
> before Phases 10 and 14 ship.
>
> ### Implementation status — 2026-08-17
>
> Branch **`feat/agentforge-opencodeforge`**, 22 commits, **not yet pushed** (`origin/main`
> still `930eb41`). Suite green throughout: **2,801 passed · 11 skipped · 0 failed ·
> 0 warnings**.
>
> | Phase | Status |
> |---|---|
> | 0 — Spikes | ✅ **10 of 11**; only **S5** (Desktop) open |
> | 1 — Rename + neutralize | ✅ **complete (1a–1h)** |
> | 2 — `AgentForge.Jsonc` | ✅ **complete** — library, wiring, `--writer legacy`, [`docs/JSONC-WRITER.md`](./JSONC-WRITER.md); smoke-tested against a real install |
> | 3 — Scope model | ✅ **complete** — `ConfigScope` is a struct, `ClaudeScope._cache` invariant retired. 4f then made the *ladder* the product's (`ScopeLadder`) and **kept the statics** — measured as 2 real edit sites, not 1,150 |
> | 4 — Product model | ✅ **complete (4a–4f)** — both `IsClaudeCode` booleans replaced, merge rules and the scope ladder are the product's own statements, the shell hosts a list of product sections, and an export names its products in a list at schema v2. One deferral stated explicitly: `ConfigFileDiscoverer` still knows only Claude's file layouts |
>
> **Phase 2 fixed a live data-loss bug the plan had only half-identified.**
> `ConfigFileLoader.LoadAsync` parsed with default `JsonDocumentOptions`, which **throw on a
> comment**; the throw was caught and turned into an *empty* `JsonObject`; the next save then
> serialized that emptiness over the file. **One comment, or one stray character, was enough
> to lose a config.** The plan predicted the OpenCode consequence but recorded "nothing is at
> risk today" — that was wrong for any Claude user who had ever hand-added a comment.
>
> ⚠ **Untested against a real install.** Every guarantee is covered by unit and end-to-end
> tests, all canaried, but nothing has exercised the new save path through the GUI against a
> real `~/.claude/settings.json`. **Do that before this branch goes near `main`.**
>
> ### ⚠⚠ Verification status — and what still REQUIRES a remote CI run
>
> The branch is deliberately **local and unpushed** (maintainer's decision, 2026-08-18), so
> **CI has never executed on any commit of Phases 1–4.** CI's gates were therefore reproduced
> locally. **One of them failed on its first-ever run**, which is the reason this section
> exists rather than being an abundance of caution:
>
> > `JsoncEditor.Quote` used the reflection-based `JsonSerializer.Serialize` overload, so the
> > Release publish failed with `IL2026` → `NETSDK1144`. It shipped in **Phase 2** and
> > survived Phases 3 and 4 **while 2,861 Debug tests passed over it** — Debug does not trim,
> > so no local test run could ever have seen it. Fixed in `807087c`.
>
> | Gate | Status |
> |---|---|
> | Debug build + full suite (Windows) | ✅ 2,884 passed · 0 failed · 11 skipped |
> | **Trim check** — `dotnet publish src/ClaudeForge -c Release -r linux-x64 --self-contained true` | ✅ **after `807087c`**; zero `IL2xxx` |
> | Release build + full suite (Windows) | ✅ 2,884 · 0 · 11 — identical to Debug |
> | `scripts/validate-model-catalog.ps1` + its 30 tests | ✅ |
> | **CI OS matrix — `ubuntu-latest`, `macos-latest`** | ❌ **NEVER RUN** |
> | **CodeQL** | ❌ **NEVER RUN** |
>
> **The two unrun gates are not reproducible locally and must run remotely before this branch
> merges.** What they would cover that nothing else does:
>
> - **The suite actually executing on Linux and macOS.** Only Windows has run it. Phases 1–4
>   touched serialization, archive layout, and per-product config paths — the three areas
>   where an OS assumption is most plausible. The relevant code *is* written OS-aware on
>   purpose (`BackupEngine` and `AdditionalDirectoriesResolver` both select
>   `OrdinalIgnoreCase` vs `Ordinal` per platform, with the Linux case commented), and the
>   JSONC tests build their input as inline strings with explicit `\r\n` escapes rather than
>   reading checked-in fixtures, so neither path-casing nor line endings are *known* to be at
>   risk. **That is an argument for expecting a pass, not evidence of one.**
> - **`Avalonia.Headless` on non-Windows.** Related: the 19 inert headless tests
>   (`Task<Task>`, outer awaited only) are still inert and are scheduled for Phase 5.
> - **CodeQL's C# analysis**, which has never seen the renamed assemblies or the new
>   `AgentForge.*` layering.
>
> **Do not report Phases 1–4 as CI-verified.** They are locally verified, which — as `807087c`
> demonstrated — is a strictly weaker claim.
>
> **Done in Phase 1:** resource prefix derived (not hardcoded) + guarded ·
> `AgentForge.Abstractions` created and the `LayeredEditors.Avalonia.Services → ClaudeForge.Sdk`
> violation removed · **all three assembly renames landed** (`AgentForge.Core`,
> `AgentForge.Sdk`, plus `IAgentConfigClient` / `AgentConfigClientCore`) · AI-facing docs
> repointed · **`ClaudeForge.Sdk.Claude` split out (1f)**, with `ClaudeForge.Sdk.Claude.Tests`
> alongside it · **1g** — the MCP sample retargeted to both SDK assemblies, plumbing moved to
> `IClaudeConfigClient`, README corrected; **no rename needed** (directory, `PackageId`, and
> namespaces already agree, and `ClaudeForge.Samples.*` is accurate for a Claude-specific
> sample) · **1h** — the `SchemaRegistry` load-order rot fixed in all four places and now
> **test-guarded**, `NAV-DEEP-LINKING-PLAN.md` re-headed as a shipped historical record.
>
> **1g's real finding was not the rename.** The sample's csproj comment asserted it
> "deliberately references ONLY AgentForge.Sdk" — which step 1f falsified the moment it added
> the second `ProjectReference` directly beneath that comment. Adding a reference silently
> turns the sentence above it into a lie, and nothing checks. Same class of rot as the four
> load-order comments: **after any structural edit, re-read the prose adjacent to what you
> changed** (Phase 1 trap 4).
>
> **1f in one paragraph, because the shape is not obvious from the instruction.** Moving the
> five accessors was the easy half; the dependency had to *invert*. `IAgentConfigClient` lost
> the five properties, `IClaudeConfigClient` re-declares them, and a new
> `ClaudeConfigClientBase` sits between the two concrete clients and `AgentConfigClientCore`
> so both share one copy of the accessor wiring — that middle class is what makes the split
> one-directional instead of circular. `SchemaHookEvents` / `SchemaHookCommandVariants` had to
> move with Hooks because they *return* Hooks types; they read the schema tree through a new
> `protected CachedSchemaNodes`. The accessors reach `internal` `JsonNode` members, so
> `AgentForge.Sdk` grants `InternalsVisibleTo("ClaudeForge.Sdk.Claude")` — an attribute, not a
> reference, so the layering rule is untouched. Three things were deliberately **not** folded
> in: `IsClaudeCode` stays on the neutral core (needs Phases 3–4's product descriptor), the
> Claude model-catalog *data* stays in `AgentForge.Core` (moving it drags an embedded resource
> and `BackupEngine`'s schema bundling along), and `Memory/`'s closed enums stay (Phase 10).
> `AssemblyLayeringTests` was extended to scan `tests/` — a shared *test* project referencing
> a product was an uncovered hole, and it was briefly occupied during this step.
>
> **Three guards were added and canaried** — read them before changing the layering or moving
> files: `ResourceNamePrefixTests`, `AssemblyLayeringTests`, `BuildFilePathIntegrityTests`.
> The Phase 1 risk table below was **corrected by measurement**; two of its four verdicts
> were wrong. Trust the corrected table, not the original claim.
>
> **Review pass 1 found and fixed:** the schema load order stated **backwards twice** — the
> exact stale comment this plan flags as wrong elsewhere · `samples/ClaudeForge.Samples.McpServer`
> breaks on the rename and was never mentioned · `--writer legacy` was architecturally
> impossible as specified (Core cannot read an app-level static) · `ClaudeForge.Avalonia`'s
> fate and its second English-only resx were unstated · no TUI nav grouping · the solution
> file was never mentioned · spike count given three ways · no sense of scale, no
> abandonment point.
>
> **Review pass 2 found and fixed:** the claim that `SchemaTreeBuilder` "already collapses
> `anyOf`/`oneOf` unions" was **wrong and load-bearing** — it classifies them `Complex`, and
> only all-string unions are rescued, so `formatter` · `lsp` · `autoupdate` would have
> shipped as raw JSON · no dependency graph among the `AgentForge.*` assemblies · Gate E
> tested two Phase-11.5 features that don't exist yet at Gate E, and 11.5 had no gate
> despite modifying shipped ClaudeForge UI · Phase 6's "tests unchanged" proof was
> overstated (Phase 3 touches them first) · the no-new-tests rule wrongly included Phase 5 ·
> no test project for the shell.
>
> **Review pass 3 found and fixed — the most serious yet:** **Phase 1 is not a safe
> mechanical rename.** Four sites hardcode `Bennewitz.Ninja.ClaudeForge` as a *string
> literal* backing embedded-resource lookup, so a rename breaks them at **runtime with no
> compile error** — worst case `BackupEngine.BundleSchemas` bundles zero schemas, after
> which `RestoreEngine` **silently skips** restore validation. Risk 2's "correct by
> construction" claim is retracted. Also: bundling OpenCode schemas in Phase 7 silently
> changes **ClaudeForge's** archive contents · `ExportManifest` is a versioned persisted
> format with product booleans and no migration plan (while its sibling `BackupManifest`
> already uses a list) — ✅ **migrated in 4e (`636fb34`)** · a **second** `IsClaudeCode`
> boolean in `RestoreEngine` ·
> `ProfileEngine` has a doubled Code/Desktop surface that makes "root-parameterize it" a
> real job, not a footnote.
>
> **Review pass 4 (deployment) found and fixed:** the monorepo decision **breaks the update
> checker** — `GithubReleaseChecker` hits `/releases/latest`, which is repo-wide, so each app
> would read the other's tag; `AssetPattern` cannot fix it. Now moved to list-and-filter by
> tag prefix, in **Phase 8** rather than 15. Also: 5 of the 10 publish scripts hardcode app
> identity (including the smoke gate, which asserts the log says `"Starting ClaudeForge"`) ·
> per-app Linux/macOS assets and icons · `release.yml`'s matrix needs an app dimension ·
> `AssemblyProduct` is global in `Directory.Build.props` · the signing script is **not in the
> repo** and now has to cover two apps · and the release/tag strategy is an unmade decision
> that gates Phase 8.
>
> **Review pass 5 found and fixed:** the **save stamp breaks the byte-stability claim** —
> `MakeHeaderComment()` embeds `DateTime.Now` to the second, so "save with no edit →
> identical bytes" was impossible as written in both the test plan and Gate A, and the
> git-diff benefit is one line larger than advertised; now a stated Phase-2 decision with
> three options. Also: **`AgentForge.Abstractions` had no creation phase** despite three
> Problems depending on it · **`OpenCode.Avalonia` likewise** · the 60/40 shell split was an
> eyeball estimate presented as measurement · the screenshot-gallery cost is PNGs, not
> markdown · Gate B referenced a `--simulate-*` flag that doesn't exist.
>
> **Review pass 6 found and fixed — a design error plus two wrong claims:** the danger
> design put severity on **`IEditorSchema.Metadata`**, which is (a) **read by nothing today**
> — zero consumers in `src/`, so the "existing extensibility bag" had no plumbing behind it —
> and (b) **per-property and scope-independent**, so it structurally cannot express
> scope-escalating or value-dependent danger; replaced with an `IDangerClassifier` service.
> `AdditionalDirectoriesResolver` is **not** a generic extra-dirs facility — it parses
> Claude's `additionalDirectories` key specifically, so OpenCode backup is new
> `BackupEngine` work, not configuration. And the artifact extraction is a
> **static→instance conversion across 5 services** (4 of them `static class` with baked-in
> roots, ~10 external call sites), not "extract a directory walk".
>
> **Review pass 7 found and fixed — and refuted pass 6's closing claim** that the remaining
> risk was spike-gated: **`GuidedRuleBuilderViewModel` is not shareable.** Its 530 lines
> branch per-tool (`Bash`/`PowerShell` command input, `WebFetch` domain input), encode
> Claude's path-anchor syntax, and string-build `WebFetch(domain:…)` / `mcp__server__tool`.
> Draft 1 called permissions Claude-only (wrong); draft 10 over-corrected to "shared UI"
> (also wrong) **and contradicted Phase 9**, which already specified a purpose-built grid for
> OpenCode. Shared UI is the ~340-line tester + interfaces, not the 945-line folder. Also:
> the two-product model is **baked into the SDK's public backup API** (`BackupClient`
> constructor, `BackupRequest`), the Backup page's bound checkboxes, and `ExportManifest` —
> Problem 3 is the second-largest refactor in the plan, not a `MainWindowViewModel` cleanup.
> ✅ **All four are now done: 4d-2 (`886494d`), 4d-3 (`a56fad7`) and 4e (`636fb34`).**
>
> **Review pass 8 — I repeated the pass-7 mistake while diagnosing it.** Pass 7 asserted
> `PermissionTesterViewModel` was "genuinely shared" *without reading it*. It is not: it
> **constructs** Claude candidates (`BuildCandidate()` switches per Claude tool) and calls
> `BashCommandSplitter`. Reading every body, **`PermissionCandidate` is itself Claude's tool
> taxonomy** (`CommandText`/`Path`/`Url`/`IsMcp`/`McpServer`/`AgentName` + per-tool
> factories), and `PermissionCollisionDetector` takes Claude's three buckets and parses
> Claude syntax. So **~none of the permission code is shareable** — only a ~50-line
> vocabulary and the view templates. **Phase 6 collapses** from an extraction to a
> vocabulary definition; **Phase 9 grows** by OpenCode's own resolver/tester/collisions.
> Three revisions of this one claim across three drafts — see the note in Risks.
>
> **Review pass 9 — systematic sweep of every remaining reuse claim.** Bad:
> `FootprintCategory` is another **closed Claude enum** (7 category names OpenCode doesn't
> share) with `ClaudeHome` baked in, so "reuses `FootprintService`" fails the same way;
> `BackupEngine` writes archive entries as **`ClaudeCode/claude-dir/{name}`**, so the
> **archive format itself is product-named** and needs versioning plus a pre-change fixture
> test; `GithubReleaseChecker` also hardcodes its **User-Agent**, which the pass-4 fix
> missed. Good: **`ConfigFileWatcher` verified genuinely reusable** — the only such claim to
> survive unchanged — and **`NavigationTreeBuilder` is *more* reusable than drafted**, its
> mechanism already neutral with only two static tables to lift into parameters.
>
> **Review pass 10 — applied the enum heuristic properly and swept all 19 enums.** It finds
> **six**, not four: `BackupMode` and `EditableMemoryScope` were missed. `BackupMode` is the
> nastier one — its values survive but their meanings are defined in Claude paths, **and it
> is serialised into `manifest.json`**, making it a *third* persisted-format migration
> alongside the archive layout and `ExportManifest`. Also: `LayeredEditors.Avalonia` is not
> quite "reuse as-is" — `WrapperStrings` carries a hardcoded English **Claude** fallback
> (*"…not in official Claude documentation"*) that OpenCodeForge would ship on its own 🕵
> badge unless it wires its own strings, which is invisible until someone hovers.
>
> **Review pass 11 — the permissions claim revised a *fourth* time, and it converges.** The
> three "narrow interfaces, no syntax" were never read: `IPermissionRuleSink` declares
> `AddAllow`/`AddDeny`/`AddAsk` over `PermissionRule` returning `PermissionCollision` —
> Claude's three buckets as an interface — and `IPermissionRuleSource` returns the
> allow/deny/ask triple. Only `IPermissionPathPicker` (22 lines, a file picker) is neutral.
> **Both permission assemblies are cut from the map**; Phase 6 becomes hours of work.
> Good news: the `LayeredEditors.Avalonia.Services` layering violation is a **five-minute
> fix** — one `using` of `Sdk.Dialogs` in two files, and those dialog primitives are simply
> filed in the wrong assembly; move them to `Abstractions` and it's gone. Draft 10 implied
> an interface was needed; it isn't.
>
> **Review pass 12 — the first large reuse claim that verified TRUE.**
> `AgentsSkillsEditorViewModel`, which the whole of Phase 11 rests on, is only **23
> product-coupled references across 1,692 lines (~1.4%)**, all in a thin data-access seam.
> It genuinely transfers — *conditional on Phase 10 converting the enums and static services
> properly*. Also: the "~15 two-product branch sites" asserted since draft 2 is **31
> references to `ClaudeDesktopSdk` alone**, so Problem 3's in-file scope roughly doubles; and
> `EssentialsCardViewModel`'s constructor is already at **14 parameters** before this plan
> adds three more — switch it to an options record while the signature is open.
>
> **Review pass 13 — checked the claims made in pass 12's own additions.** The
> `AxamlAccessibilityCoverageTests` precedent I cited is **better than I described**: not a
> flat allow-list but a four-property **ratchet** (at-or-below per-file baseline · new files
> capped at 0 · fixes decrement the entry · a missing baselined file fails loudly). The
> parameter guard now copies that shape, and the rename-detection property matters here
> because this plan renames so much. Two corrections: `InstallCommandViewModel` is
> **already compliant** — private ctor behind `ForClaudeCode`/`ForClaudeDesktop`, so pass 12's
> "make the ctor private" was wrong; and `MarketplaceListEditorViewModel` **verified** as the
> right union template for a specific reason — it preserves per-variant fields across a
> switch and round-trips unknown variants, both of which `mcp` and `plugin[]` need.
>
> **Changed since draft 1**, all at the maintainer's challenge:
> JSONC is now comment- **and** formatting-preserving, not warn-on-loss ·
> permissions are **substantially shared**, not "Claude-specific" (draft 1 was wrong) ·
> OpenCode gets **multiple product sections** (Core + TUI + Desktop), mirroring the
> Claude Code / Claude Desktop split · **artifact locations are config-declared, not
> convention** (draft 2 under-modelled this — new Problem 6 + a resolution engine) ·
> **rules exist in both products** with different semantics (new Problem 7) ·
> added an **OpenCode Essentials** card set, a **Rules & access** feature spec, a
> **schema-update** story for app + CI, a **test plan** sized against the existing suite,
> a **detect / install-banner / update-check** section, an explicit inventory of the
> **search / nav / filter / deep-link** surfaces the refactor touches, **six sparing human
> regression gates**, a **debug-flag** split (shared core + per-app), and — correcting
> draft 3 — **providers are readable from the config, so a model picker is in scope**
> (which also surfaced Spike S11, an external network `$ref` in the schema).
> Draft 5 adds a **coverage check** for hooks / agents / MCP / plugins: OpenCode has **no
> hooks** (Claude's stay Claude-only), and **plugins** and **inline agent/command JSON**
> were under-covered — both now have editors and tests. Draft 6 adds the **plugin editing
> pattern** (artifact-shaped, scaffold-not-rewrite), **profile-readiness rules** so a future
> profiles feature is additive, and brings the **diagnostics windows** in scope — including
> wiring `LiveTailWindow`, which is built but currently has no consumer. Draft 7 folds in
> the maintainer's rulings on all **15 open questions and deferrals** (table below): notably
> the inert headless tests move *into* Phase 5, the keybinds editor and a read-only
> credential-status view are *in* v1, and the JSONC writer keeps a one-release escape hatch.
> Draft 8 applies the **danger-indication tenant systematically**: a full severity
> assessment of all 36 config + 13 TUI keys, promoted from an Essentials-only concept to a
> scope-aware schema annotation shown on every surface — which also fixes the fact that
> ClaudeForge's own settings tree shows no severity today. Draft 9 adds **five enforcement
> guards** so an unmarked dangerous setting cannot ship, and a **disposition for all 25
> existing guides** (shared / adapt / duplicate / Claude-only) plus the 5 new docs required.
> Per the maintainer: the hardcoded severity hexes are migrated to tokens **in both apps**
> as part of this plan, and **`subagent_depth` is ruled amber**.

---

## Context

ClaudeForge is feature-complete and stable for community use. The maintainer is adopting
OpenCode alongside Claude Code (not as a replacement) and wants a directly analogous tool
for OpenCode's config surface.

Much of ClaudeForge was deliberately built generic — `LayeredEditors.*` is already
product-agnostic. The goal is a second app reusing as much code and UI as is practical,
with renames/moves where that unlocks reuse, plus first-class support for what only
OpenCode has.

**Decisions locked with the maintainer:**

| Decision | Choice |
|---|---|
| Topology | **Two apps, shared libraries** — `ClaudeForge.exe` + `OpenCodeForge.exe` |
| Repo | **Monorepo** — both apps in `JanusMael/ClaudeForge` |
| Renaming | **Rename Core/Sdk to neutral** — `AgentForge.Core` / `AgentForge.Sdk` |
| Localization | **One shared resx set**, per-app product strings layered on top |
| JSONC | **Full comment-preserving round-trip** (revised — see Problem 4) |
| Permissions | **Shared model + shared UI**, per-product serializer and matchers (revised) |
| Products | **Multi-section per app**, mirroring Claude Code / Claude Desktop (revised) |
| Artifacts | **Source-based resolution engine** with shadowing, not fixed directory walks (revised) |
| Rules | **Supported for both**, with an OpenCode resolution view (revised) |
| Essentials | **17-card OpenCode set** across the same four severity tiers (new) |
| Schema updates | **Opt-in in-app refresh with provenance badge** + multi-schema CI drift PR (new) |
| Detect / install / update | **Three distinct mechanisms**, kept distinct: install banner · managed-product version · app update (new) |
| Models | **Config-sourced model picker is in scope** — providers are readable from the config (corrected) |
| Debug flags | **Shared core in the shell + per-app registration**, 5 new OpenCode flags (new) |
| Hooks | **No OpenCode analogue** — Claude's hooks stay Claude-only; plugin events are the substitute (new) |
| Plugins | **Full page** — npm array + local files + event scan + **scaffold-a-plugin**; never rewrite user code (new) |
| Profiles | Not in v1, but **code stays profile-ready** — 5 cheap rules so it's additive later (new) |
| Diagnostics | **Both windows in scope** — F12 log, and `LiveTailWindow` finally wired as a live config-activity view (new) |
| Danger | **Systematised + enforced** — schema-level, scope-aware, every surface, 5 guards, and the hardcoded severity hexes migrated to tokens **in both apps** (new) |
| Guides | **All 25 docs dispositioned** — shared / adapt / duplicate / Claude-only, plus 5 new docs (new) |
| Parameter counts | **Max 6 positional** — 12 current violations inventoried, 10 fixed opportunistically, guarded by an allow-list test (new) |
| Deployment | **Publish scripts parameterized, matrix ×2 apps, per-app assets/winget** — and the update checker fixed for the monorepo (new) |
| Tests | **≈965–1,385 new tests** mirroring existing per-area layout; extraction phases add ≈0 (new) |

### Review decisions — folded in

Every open question, deferral, and out-of-scope item was reviewed individually. Resolutions:

| # | Item | Decision |
|---|---|---|
| 1 | OpenCode Desktop section | **Pending Spike S5** — decide after probing, not before |
| 2 | TUI keybinds editor | **In v1** — purpose-built searchable editor, not the raw fallback |
| 3 | Remote artifact sources (`skills.urls`, remote `instructions`, git `references`) | **List and explain, do not fetch** |
| 4 | models.dev catalog | **Offline tier only** — picker sourced from `provider.*.models` in config |
| 5 | Schema freshness | **Opt-in promotion, bundled-first default** |
| 6 | OpenCode profiles | **Ready-only, ship none** — the 5 rules, plus 2 guard tests |
| 7 | Provider credentials | **Both** — exclude + redact **and** a read-only credential *status* view (never values) |
| 8 | Plugin source editor | **Plain text**, no TS highlighting |
| 9 | 19 inert headless tests | **Fix during Phase 5** — they cover exactly what the shell extraction risks |
| 10 | JSONC writer rollback | **Keep the legacy writer behind a flag for one release** |
| 11 | Essentials card count | **Ship all 17** |
| 12 | AGENTS.md / docs | **Per-phase deliverable**, part of each phase's definition of done |
| 13 | Localization | **Full 9-locale parity**, machine-translated then spot-checked; gate stays as-is |
| 14 | Repo name | **Keep `JanusMael/ClaudeForge`** — published winget manifests can't be retroactively repointed |
| 15 | OpenCode rules v1 vs v2 | **Support both**, gated on `ProductVersionProbe`, labelled in the UI |
| Human testing | **7 gates, ~10 min each**, at the risky phase boundaries only (new) |
| v1 scope | Essentials (17 cards) · Settings + Effective view (config **and** TUI sections) · compound editors (mcp · permission · agent · command · plugin · keybinds · references) · Agents/Commands/Skills/**Rules**/Plugins · danger indication everywhere · install banner + update checks · diagnostics windows · Backup/Restore + data footprint |

### Roadmap at a glance

| # | Phase | Ships | ClaudeForge risk | Human gate |
|---|---|---|---|---|
| 0 | Spikes S1–S11 — **10/11 done; only S5 open** | answers, no code | none | — |
| 1 | Rename → `AgentForge.*` | nothing user-visible | mechanical only | — |
| 2 | `AgentForge.Jsonc` | **ClaudeForge stops normalizing your formatting** | ⚠ save path | **A** |
| 3 | Generalize scope model | nothing user-visible | ⚠ merge semantics | — |
| 4 | Generalize product model | nothing user-visible | ⚠ multi-product wiring | **B** |
| 5 | Extract shell + split resx | nothing user-visible | ⚠⚠ **highest** | **C** |
| 6 | Extract permission core | nothing user-visible | ⚠ | — |
| 7 | `OpenCode.Sdk` | nothing user-visible | none | — |
| 8 | **OpenCodeForge v0** | settings · effective view · install banner · update check — *first runnable* | none | **D** |
| 9 | Compound editors | mcp · permission · agent · command · plugin · keybinds · references | none | — |
| 10 | `AgentForge.Artifacts` | nothing user-visible | ⚠ Memory page | — |
| 11 | Agents / Commands / Skills / **Rules** / Plugins | the headline feature | none | **E** |
| 11.5 | **Danger indication systematised** | severity everywhere incl. save-preview · 5 guards · **hex→token migration in both apps** | ⚠ touches shipped Essentials | **E2** |
| 12 | OpenCode Essentials | 17 pinned cards | none | — |
| 13 | Schema refresh (app + CI) | in-app update + provenance badge | benefits both | — |
| 14 | Backup / Restore + footprint | archive + prune, `auth.json` excluded | none | — |
| 15 | Packaging | winget `Bennewitz.Ninja.OpenCodeForge` | ⚠ release workflow | **F** |

**Phase 8 is the first point anything is usable.** Phases 1–7 are all foundation, and every
one of them can regress ClaudeForge — which is why each ends on a fully green suite plus,
where marked, a ~10-minute human gate.

### Scale — read this before committing

This plan is **large**. Stated plainly so the size is a decision rather than a discovery:

| Dimension | Magnitude |
|---|---|
| Phases | 16 (0–15) |
| New assemblies | ~9, plus ~10 renamed |
| Files touched by the rename alone | 300+ |
| New tests | ~945–1,355, taking the suite past 3,400 |
| Translations | ~1,600–2,400 (200–300 keys × 8 locales) — re-derive after the shared-resx inventory |
| New docs | 5, plus dispositions for all 25 existing |
| Spikes before any code | 11 |

**Phases 1–7 deliver no user-visible value** and carry all of the regression risk. That is
the honest shape of the trade: a substantial foundation investment before the first
runnable OpenCode build. It is the right shape given the two-app decision — but if the
appetite is smaller, the cheapest alternative remains adding OpenCode as a third product
section inside ClaudeForge (a fraction of this work, at the cost of the app's name reading
oddly for OpenCode-only users).

**Deliberately no time estimates.** Phase durations depend entirely on how much of the week
this gets, and a fabricated month-count would be worse than none. Use the phase count and
the test/translation volumes as the sizing signal.

### If a phase goes badly — the exit

"ClaudeForge stays shippable at every boundary" is the safety property, and it needs a
stated exit or it is just optimism:

- **Phases 1–4** are individually revertible — each is a self-contained commit range with
  no user-visible change, so `git revert` is a real option.
- **Phase 5 is the one that can fail.** If the shell extraction proves unworkable mid-way,
  stop at the last green slice and **ship from there**: a partially-extracted shell is
  still a working ClaudeForge, and Phases 6–7 do not depend on the extraction *completing*
  — only on the pieces they use having moved. The fallback is to keep OpenCodeForge as a
  thicker app that duplicates the un-extracted shell parts, which is worse code but not a
  dead end.
- **Phase 2 has a runtime escape hatch** (`--writer legacy`), which is the only phase where
  a defect reaches users' files rather than their screen.
- **Abandonment point.** If Phase 5 cannot reach green after a bounded effort, the honest
  outcome is to stop and reconsider the two-app topology — not to push through. Phases 1–4
  retain standalone value (neutral names, generalized scopes, N-product model, the JSONC
  writer) even if OpenCodeForge never ships.

---

## What's already reusable — the inventory

| Project | Lines | Verdict |
|---|---|---|
| `LayeredEditors.Abstractions` | 331 | **Reuse as-is.** `IEditorSchema` / `IEditorScope` / `IEditorValue` / `IEditorWorkspace` are fully product-agnostic with an explicit value-currency contract. Zero `ConfigScope` references. |
| `LayeredEditors.ViewModels` | 1,197 | **Reuse as-is.** Zero `ConfigScope` references. |
| `LayeredEditors.Avalonia` | 1,761 | **Reuse after one real fix.** Zero `ConfigScope` references, but `Localization/WrapperStrings.cs` holds **hardcoded English Claude text** — `"Undocumented setting — not in official Claude documentation"` — as the fallback when a consumer doesn't supply its own strings. ClaudeForge overrides it via `Program.WireWrapperLocalization`; **OpenCodeForge must do the same or it ships a Claude-branded tooltip.** Better: make the library fallback product-neutral. The other `Claude` hits in this project are doc comments only. |
| `LayeredEditors.Avalonia.Diagnostics` | 2,763 | **Reuse as-is.** |
| `LayeredEditors.Avalonia.Services` | 2,183 | **Reuse after one fix** — references `ClaudeForge.Sdk`, a layering violation. |
| `ClaudeForge.Core` | 11,719 | **Mostly reusable mechanism.** Schema pipeline, merge engine, file IO, backup/restore, platform paths, updates are generic in shape. |
| `ClaudeForge.Sdk` | 11,007 | **Two-way split** — reusable base (`ClaudeConfigClientCore`, env, memory scanning, diagnostics) · everything else Claude-only, including **the whole ~1,600-line permission subsystem** (candidate, resolver, collision detector, matchers) and hooks / marketplaces / plugins / model catalog. Draft 11's "~530 shared lines" did not survive reading the bodies. |
| `ClaudeForge.Avalonia` | 1,581 | **Mostly Claude-only.** Both permission VMs (780 lines) stay; only three narrow interfaces (87) and the AXAML view templates are shareable. Draft 1 said Claude-only (right, for the wrong reason); drafts 10–11 said shared (wrong). |
| `src/ClaudeForge` (app) | 39,615 | **Split.** Roughly 60/40 generic shell vs Claude-specific pages — **an eyeball estimate from the file list, not a measurement.** The Phase 5 slicing should re-derive it; if the shell share is materially smaller, the extraction is less valuable than this plan assumes and the topology decision deserves a second look. |

### Structural facts that make this cheap

1. **The app already ships two products in one shell.** `MainWindowViewModel` builds
   header nodes for `NavTitleClaudeCode` / `NavTitleClaudeDesktop`, each backed by a
   `ClaudeConfigClientCore` subclass. `ClaudeDesktopClient` overrides only
   `DiscoverFiles(projectRoot)`, `Product` (`IsClaudeCode` until 4a), `CreateBackupClient()`,
   and `SnapshotUserMemoryFiles()`. **Every OpenCode section is another subclass.**

2. **`ClaudeDesktopClient` is the precedent for a product with a different scope set** —
   User-scope-only, not project-aware.

3. **OpenCode publishes two real JSON Schemas** (both draft 2020-12, both declaring
   `allowComments` + `allowTrailingCommas`):
   - `https://opencode.ai/config.json` — 38 KB, **36** top-level properties under
     `$defs/Config`, 19 `$defs`.
   - `https://opencode.ai/tui.json` — **1.1 MB**, 13 top-level properties, no `$defs`.
     `keybinds` alone is 326 KB / **184 actions** (see Risk 3).

   `SchemaRegistry` already does **memory → bundled (+overlay) → disk → HTTPS**. *(Bundled
   outranks disk and network — see Schema updates. The class's own doc comment states this
   backwards and is wrong; do not trust it.)*

   ⚠ **Correction to draft 9, which claimed `SchemaTreeBuilder` "already collapses
   `anyOf`/`oneOf` unions". It does not.** `ClassifyValueType` maps any `anyOf`/`oneOf`
   with more than one non-null branch to **`SchemaValueType.Complex`**. The one rescue,
   `TryGetStringUnionEnum`, fires only when **every** non-null branch is a string and at
   least one carries an `enum` (the `theme` shape). OpenCode's unions are mostly not that
   shape, so they land in `Complex` → `DefaultEditorFactory.CreateComplexFallback` → a
   typed VM if dispatched by name, otherwise **raw JSON**.

   That mechanism is fine — it is the documented extension point — but the settings editor
   is **not** as free as draft 9 implied. Concretely, four top-level `Config` keys are
   unions: `permission` (action | object), `formatter` (bool | object), `lsp` (bool |
   object), `autoupdate` (bool | "notify"). Draft 9 accounted for `permission` only;
   **`formatter`, `lsp`, and `autoupdate` would have rendered as raw JSON** with nobody
   noticing until a user complained. Now on the Phase 9 editor list. Nested unions
   (`mcp.*`, `plugin[]` items, `oauth`, keybind values, `agent.*.color`, `scroll_speed`)
   need the same treatment.

---

## Target assembly map

```
AgentForge.Abstractions       product identity · scope model · merge policy · permission
                              vocabulary · dialog primitives (moved from Sdk/Dialogs)
AgentForge.Core               schema · merge · file IO · backup · platform · updates
AgentForge.Jsonc              comment/format-preserving JSONC reader + edit-based writer   ← NEW
AgentForge.Sdk                AgentConfigClientCore · env · diagnostics
AgentForge.Artifacts          artifact source model · resolution engine · shadowing         ← NEW
AgentForge.Permissions        normalized rule model · resolver · collision detector         ← NEW
AgentForge.Avalonia.Shell     MainWindow shell · nav · deep links · status · search · save · Essentials cards
    (AgentForge.Permissions / .Avalonia.Permissions — DELETED after pass 8+11 found
     nothing shareable; vocabulary → Abstractions, path picker → Services, templates → Shell)
AgentForge.Localization       the product-neutral resx keys × 9 locales

LayeredEditors.*              unchanged — editor VMs · wrapper · converters · diagnostics · services

ClaudeForge.Sdk.Claude        Claude rule syntax + matchers · hooks · marketplaces · plugins · model catalog
ClaudeForge                   Claude pages + product registration

OpenCode.Sdk                  OpenCodeClient · OpenCodeTuiClient · mcp/permission/agent accessors
OpenCode.Avalonia             OpenCode-specific editors + views (incl. keybinds)
OpenCodeForge                 OpenCode pages + product registration
```

**Enforced rule:** `AgentForge.*` may never reference `ClaudeForge.*` or `OpenCode.*`.
Add a test inspecting `Assembly.GetReferencedAssemblies()` so this can't silently
regress the way `LayeredEditors.Avalonia.Services` already did.

**The intended dependency graph** — a flat list is not enough to implement against, and
without this the layering guard can only catch the crudest violations:

```
Abstractions ← (everything)
Jsonc        ← Core
Core         ← Sdk · Artifacts
Abstractions ← Permissions            (needs the scope model — see below)
Sdk          ← Avalonia.Shell
Artifacts    ← Avalonia.Shell
Permissions  ← Avalonia.Permissions ← Avalonia.Shell
Localization ← Avalonia.Shell · Avalonia.Permissions
```

Two rules the graph encodes that are easy to get wrong:
- **`AgentForge.Permissions` must NOT reference `AgentForge.Sdk`.** It needs only the
  scope model and the rule types. Letting it reach the SDK invites a cycle once the SDK
  wants to expose a permissions accessor.
- **`AgentForge.Jsonc` depends on nothing but the BCL.** It is a text-editing component;
  keeping it dependency-free is what makes it property-testable in isolation, which the
  risk profile demands.

Extend the layering test to assert these edges positively, not just the negative
`AgentForge.* → ClaudeForge.*` rule.

**Which phase creates each assembly.** Draft 10 left this implicit and scattered across
phase headings, which is exactly how `AgentForge.Abstractions` and `OpenCode.Avalonia` ended
up with *no* creating phase despite three Problems depending on the first. Explicit now:

| Assembly | Created in | Note |
|---|---|---|
| `AgentForge.Abstractions` | **Phase 1** ✅ | Must precede Phase 2 (`IConfigWriter`). Grows a contract per later phase. |
| `AgentForge.Core` · `AgentForge.Sdk` | Phase 1 ✅ | Renames of the existing projects |
| `ClaudeForge.Sdk.Claude` | Phase 1 ✅ | Claude-domain accessors split out. Also created `ClaudeForge.Sdk.Claude.Tests` — the shared test project must stay buildable without a product, which `AssemblyLayeringTests` now enforces for `tests/` too. |
| `AgentForge.Jsonc` | Phase 2 ✅ | Framework-only, no package references. `AgentForge.Jsonc.Tests` alongside it. **`AgentForge.Core` now references it and `AgentForge.Abstractions`** — both shared, so layering is unaffected. |
| `AgentForge.Avalonia.Shell` · `AgentForge.Localization` | Phase 5 | The shell extraction + resx split |
| ~~`AgentForge.Permissions` · `AgentForge.Avalonia.Permissions`~~ | — | **Cut.** Passes 8 and 11 found nothing to put in them. |
| `OpenCode.Sdk` | Phase 7 | |
| `OpenCodeForge` | Phase 8 | First runnable |
| `OpenCode.Avalonia` | **Phase 9** | Home of the OpenCode editors incl. keybinds |
| `AgentForge.Artifacts` | Phase 10 | |

Every row also implies a `ClaudeForge.slnx` edit and, where listed in the test plan, a
matching test project.

---

## The eight hard problems

### Problem 1 — `ConfigScope` is a closed 4-value enum **(the big one)**

`ConfigScope` (`src/ClaudeForge.Core/Settings/ConfigScope.cs`) is referenced **314 times
across 69 files**, and used as an ordinal, not just a tag:

- `LayeredValue` sorts with `OrderBy(e => (int)e.Scope)`.
- `ClaudeScope._cache` is an **array indexed by `(int)scope`** — a documented `AGENTS.md`
  hard invariant.
- `MergeResult` returns `ConfigScope?`; `LayeredValue.IsManagedLocked` hardcodes
  `== ConfigScope.Managed`.

OpenCode's ladder is different and longer: global → custom (`OPENCODE_CONFIG`) → project
→ inline (`OPENCODE_CONFIG_CONTENT`) → managed → macOS MDM.

**Solution.** A readonly record struct in `AgentForge.Abstractions`:

```csharp
public readonly record struct ConfigScopeId(string Id, int Priority, string DisplayName, bool IsReadOnly);
```

Each product declares an ordered `ScopeSet`. `ClaudeScope` collapses to a lookup over that
set — the `_cache` array invariant disappears entirely, a net simplification.
`IEditorScope` already models this exact shape, so the adapter boundary barely moves.

**Reviewable migration:** two commits. First make `ConfigScope` a struct with the same four
static instances and identical `(int)` values (everything still compiles). Second, thread
the product's `ScopeSet` through `SettingsWorkspace` / `MergeEngine` / `LayeredValue` and
delete the statics.

### Problem 2 — Merge semantics are Claude's, hardcoded

`MergeEngine`'s doc comment stated Claude's rules verbatim: arrays UNION, non-arrays
highest-scope-wins, objects deep-merge. OpenCode documents only that configs "merge rather
than replace" — **array behaviour unverified** at the time this was written; S1 has since
measured it. The problem statement missed one site: **Claude's list of union-merged paths
was a private static field on `SettingsWorkspace`**, so the rules were not only stated in
the core, they were *owned* by it.

**Solution.** `IMergePolicy` in `AgentForge.Abstractions`, with `ClaudeMergePolicy`
(today's exact behaviour, locked by existing `MergeEngine` tests) and
`OpenCodeMergePolicy`. `MergeEngine` takes the policy as a parameter; the `arrayPaths`
hint already threaded through `MergeCore` is the seam.

> ✅ **Done in 4c (`4255c12`) for the interface and `ClaudeMergePolicy`**; `OpenCodeMergePolicy`
> stays with Phase 7. ⚠ "**locked by existing `MergeEngine` tests**" was **wrong** — they
> locked the engine's *execution*, not Claude's *rules*: emptying Claude's whole path list
> failed exactly one test, the one written in that commit. See the canary table under
> Phase 4.

### Problem 3 — Product wiring is hardcoded for exactly two

`MainWindowViewModel` (4,797 lines) holds `ClaudeCodeSdk` and `ClaudeDesktopSdk` as named
fields. Drafts 2–12 said "~15 places"; **the actual count is 31 references to
`ClaudeDesktopSdk` alone**, before counting `ClaudeCodeSdk` — save, validate, effective
snapshot, search providers, change subscribe/unsubscribe, dispose, backup, export, and the
install-banner logic. `ClaudeConfigClientCore` carried
`protected abstract bool IsClaudeCode`, used in 8 places for schema selection — **4a
replaced it with `protected abstract ProductDescriptor Product`**; the field count below is
still current.

> ⚠ **Draft 10 scoped this to `MainWindowViewModel`. It is threaded through the SDK's
> public API and a persisted format as well** — a two-boolean product model, not two fields:
>
> | Site | Shape |
> |---|---|
> | `BackupClient(engine, includeClaudeCode, includeClaudeDesktop)` | **public SDK constructor** |
> | `BackupRequest.IncludeClaudeCode` / `.IncludeClaudeDesktop` | request record |
> | `ClaudeCodeClient.CreateBackupClient()` → `(true, false)`; Desktop → `(false, true)` | per-product wiring |
> | `BackupRestoreViewModel._includeClaudeCode` / `_includeClaudeDesktop` | `[ObservableProperty]` **bound to UI checkboxes** |
> | `ExportManifest.IncludesClaudeCode` / `.IncludesClaudeDesktop` | **persisted, versioned** (see below) — ✅ **4e (`636fb34`)**, now `clients` at schema v2 |
>
> Consequences the plan must budget for: the backup API becomes a **product set** rather
> than two flags (a public-surface change, caught by `PublicSurfaceContractTests`); the
> Backup page's two fixed checkboxes become a **dynamic per-product list**; and the manifest
> needs the migration described below. This is why Problem 3 is the second-largest refactor
> after the shell extraction, not a `MainWindowViewModel` cleanup.

This matters more now: **OpenCodeForge needs 2–3 sections of its own** (Core, TUI,
Desktop), so the app must handle N products, not 3.

**Solution.**
- Replace the two named fields with `IReadOnlyList<ProductSection>` carrying
  `{ Id, DisplayName, NavIcon, Client, ScopeSet, SchemaSource, MergePolicy, PermissionModel }`.
  Every `if (ClaudeDesktopSdk is not null)` becomes a `foreach`.
- Replace `bool IsClaudeCode` with a `ProductDescriptor` naming the schema key — deletes
  the boolean rather than adding a third case. **There are two such booleans, not one:**
  `ClaudeConfigClientCore.IsClaudeCode` *and* `RestoreEngine.FindConfigFilesToValidate`,
  which returned `(string FilePath, bool IsClaudeCode)` in Core. Draft 10 named only the
  first, so the restore-validation path would have kept a two-product assumption.
  ✅ **Both done — 4a (`101554b`) and 4b (`629bca7`).**
- Break `LayeredEditors.Avalonia.Services` → `ClaudeForge.Sdk`.
- **Persisted manifest formats need a versioning decision — draft 10 never mentioned them.**
  `ExportManifest` carries `includesClaudeCode` / `includesClaudeDesktop` **booleans** with
  `CurrentSchemaVersion = 1`, written into exported profiles that other builds read back.
  N products means bumping to schema v2 or replacing the booleans with a list — and
  **`BackupManifest` already does it the right way** (`clients: List<string>`), so two
  adjacent files in the same folder contradict each other. Recommended: mirror
  `BackupManifest`'s shape, bump `ExportManifest.CurrentSchemaVersion` to 2, and keep a v1
  read path mapping the two booleans onto the list. Round-trip tests for **both** versions —
  a silently unreadable v1 export is data loss for anyone who exported a profile before
  this change, and profile export/import is a shipped, documented feature
  (`docs/CLAUDECTX-COMPATIBILITY.md`).

Largest refactor in the plan and the most likely to regress `MainWindowViewModel`. Re-read
`AGENTS.md` §1 first — especially `_suppressProfileChangeReload`, `_suppressStateSave`,
and the `_lastDeepPath` capture rule.

### Problem 4 — JSONC round-trip **[REVISED: full preservation]**

**First, a correction to a premise.** ClaudeForge does **not** currently write JSONC
comments. `ConfigFileLoader.SaveAsync` writes a top-level `"//"` **JSON key** (see also
`EffectiveConfigBuilder.Stamp` and `ExportManifest.HeaderComment`), stripped again on load
by `ConfigFileLoader.LoadAsync` and ignored by `SettingsDocument.HasActualChanges` /
`JsonDiff.Compute`. It is valid strict JSON that merely *looks* like a comment. So nothing
is at risk today — but the maintainer's instinct is right, because **OpenCode files are
genuinely JSONC and users will have real comments in them.**

**Current behaviour is destructive for OpenCode.** `LoadAsync` uses plain
`JsonNode.ParseAsync` (no comment handling — it would *throw* on a commented file, caught
and silently treated as empty, which is worse than dropping comments: it would look like
an empty config and then overwrite the user's file). `SaveAsync` re-serializes the entire
document with `WriteIndented = true`, discarding key order nuances, blank lines, and
indentation style.

**Solution — `AgentForge.Jsonc`, an edit-based writer.**

The proven design is the one `microsoft/node-jsonc-parser` uses: a scanner producing
tokens with offsets, a parse tree carrying spans, and a `modify()` that returns **text
edits against the original string** rather than re-serializing. Comments, whitespace, key
order, and line endings survive because they are simply never rewritten.

This fits ClaudeForge's existing architecture unusually well: the app **already computes a
structural, path-level diff** (`JsonDiff.Compute`) to drive the save-changes dialog. That
diff is exactly the input an edit-based writer needs.

```
JsoncDocument.Parse(text)        → tree with { path → span } plus trivia
JsoncEditor.Apply(text, changes) → new text, minimal spans replaced
```

Scope is bounded — set-at-path, remove-at-path, and formatting of newly inserted values,
matching the document's detected indent. A few hundred lines, fully unit-testable.

**Why build rather than take a dependency:**
- `System.Text.Json` cannot do it. `dotnet/runtime#98865` proposes
  `JsonCommentHandling.Allow` for `JsonNode` — still a proposal, not shipped.
- `microsoft/JsonPlus` does preserve trivia through decode/mutate/encode, but has **no
  published releases and no NuGet packages**, and decodes ~2× slower than STJ. Vendoring
  an unreleased repo into a project that ships signed binaries is a supply-chain and
  maintenance risk that outweighs the saved effort.
- Most "JSONC for .NET" packages are strip-only — lossy by construction.

**Bonus, and the reason to do this properly:** an edit-based writer preserves **user
formatting for both products**. ClaudeForge today normalizes whitespace on every save.
After this, a hand-formatted `settings.json` survives a ClaudeForge save untouched except
for the keys that actually changed. That is a quality win independent of OpenCode, and it
shrinks every save diff a user sees in `git`.

**Keep the `"//"` key for Claude** (Claude Code's schema tolerates it and the round-trip is
already proven). For OpenCode, emit a **real leading `//` comment** instead, since the
format supports it — and make the stamp idempotent so repeated saves don't accumulate.

> ### ⚠ The save stamp undercuts the byte-stability claim — reconcile before building
>
> `MainWindowViewModel.MakeHeaderComment()` embeds **`DateTime.Now` to the second**:
> *"ClaudeForge v… last saved this file on MM-dd-yyyy hh:mm:ss tt…"*. Every save therefore
> rewrites that line with a new value.
>
> Two statements elsewhere in this plan were **impossible as written** and are corrected:
> the test-plan's *"load → save with no edit → identical bytes"* and Gate A's *"save with no
> change pending → content unchanged"*. Neither can hold while the stamp is timestamped.
>
> It also softens the headline benefit. "Only the keys you changed appear in `git diff`"
> is really **"the stamp line, plus the keys you changed."** Still a large improvement over
> today's whole-file reserialization, but state it honestly rather than overselling it.
>
> **Decide during Phase 2** (this is a maintainer call, not an implementation detail):
> 1. **Exclude the stamp from byte-stability assertions** and accept a permanent one-line
>    diff on every save — smallest change, keeps the stamp's forensic value.
> 2. **Write the stamp only when something else changed** — makes a no-op save genuinely
>    byte-identical. Requires a real no-op-save path, which the Save button can reach.
> 3. **Make the stamp opt-out** (a setting or debug flag) for users who keep config in git
>    and want truly minimal diffs. Most user-friendly, most work.
>
> Option 1 is the default assumption in the rest of this plan; the byte-stability test is
> specified as *"every byte outside the changed spans **and the stamp line** is identical."*

### Problem 5 — Permissions are far more alike than draft 1 claimed **[REVISED]**

Draft 1 asserted the permission UI was Claude-specific. Re-examined against the schema,
that was wrong. **Both products express the same underlying model:
`(tool, optional pattern) → {allow, ask, deny}`, plus a default.** Only the serialization
and the matching semantics differ.

| Concept | Claude Code | OpenCode |
|---|---|---|
| Storage | `permissions.{allow,deny,ask}` — three arrays of rule strings | `permission` — an action, **or** a map `tool → (action \| {pattern: action})` |
| Rule identity | `Tool` · `Tool(specifier)` · `mcp__server__tool` | tool key + glob pattern key |
| Outcomes | allow / ask / deny (the bucket *is* the outcome) | allow / ask / deny (the value *is* the outcome) |
| Default | `permissions.defaultMode` | bare-string `permission`; `--auto` CLI flag |
| Tool taxonomy | 30 names in `PermissionTools.Names` | 15 named **+ arbitrary** — `PermissionConfig` sets `additionalProperties: PermissionRuleConfig`, so MCP tool names fit natively |
| Bash matching | `BashCommandSplitter` (250 ln) splits `&&` / `\|` chains; prefix rules | glob against the parsed command (`git *`, `git commit *`) |
| Path matching | `PathRuleMatcher` (298 ln): `//abs`, `~/home`, `/project`, `./cwd`, gitignore bare-name semantics | glob with `~` / `$HOME` expansion |
| Per-agent override | `AgentRuleMatcher` | `AgentConfig.permission` (same `PermissionConfig` shape) |

### ⛔ Third revision — almost none of the permission *code* is shareable

Draft 1 said permissions were Claude-only. Draft 10 over-corrected to "shared model + shared
UI". Draft 11 walked back the guided builder but kept the model layer and the tester.
**Reading every body, draft 11 was still wrong.** The Claude taxonomy is baked into the
types themselves, not layered on top:

| Type | Lines | Claude coupling found in the body |
|---|---|---|
| `PermissionCandidate` | 93 | The record **is** Claude's tool taxonomy — `CommandText` · `Path` · `Url` · `IsMcp` · `McpServer` · `McpTool` · `AgentName`, with static factories `Bash()` · `PowerShell()` · `Read()` · `Edit()` · `Write()` · `WebFetch()` · `Mcp()` · `Agent()` |
| `PermissionCollisionDetector` | 186 | Takes Claude's **three buckets** (`allow` / `deny` / `ask`) and parses via `ParsedPermissionRule.TryParse` — Claude rule syntax |
| `PermissionResolver` | 183 | Resolves Claude candidates against Claude rules |
| `PermissionTesterViewModel` | 250 | **Not neutral after all** — draft 11 claimed it only *consumes* decisions; it *constructs* them: `BuildCandidate()` switches per Claude tool, and `BuildReadOnlyNote` / `BuildSubcommandWarning` call into `BashCommandSplitter` |
| `GuidedRuleBuilderViewModel` | 530 | Claude rule-syntax generator (draft 11 got this one right) |

**What is actually shared is a vocabulary and a pattern, not ~530 lines of code:**

- `PermissionOutcome` — `Allow` / `Ask` / `Deny` / `Default`. Identical concept, ~10 lines.
- A **generic decision shape** — `Decision<TRule>(Outcome, MatchedRule, MatchedScope, Explanation)`.
  Genuinely reusable once parameterized on the rule type.
- The **UI pattern** — a tester panel (tool selector → input → explained verdict) and a
  rule editor. The *view templates* can be shared; the view-models cannot.

**Plan consequence — Phase 6 shrinks dramatically.** It is no longer "extract a shared
permission core"; it is "define a ~50-line shared vocabulary and leave two parallel
implementations." Forcing the rest into a common abstraction would produce something worse
than duplication: an `IPermissionModel` general enough to express both Claude's
`Tool(specifier)` + gitignore path semantics + bash chain-splitting *and* OpenCode's flat
tool→glob map would be an abstraction over two things that merely rhyme.

**Correspondingly, Phase 9 grows.** OpenCode needs its own candidate model, resolver,
collision detection, tester VM, and grid editor — perhaps 400–600 lines, not the "binding
exercise" draft 10 described. The good news: OpenCode's semantics are far simpler than
Claude's (glob matching with `~` expansion vs. `BashCommandSplitter` + `PathRuleMatcher`),
so it is a much smaller implementation than the ~1,600 lines Claude carries.

**What becomes shared UI — narrower than draft 10 claimed.**

> ⚠ **Draft 10 said `GuidedRuleBuilderViewModel` becomes "product-parameterized". Read the
> implementation: it should not.** Its 530 lines are a **Claude rule-syntax generator**, and
> the coupling is structural rather than a taxonomy list:
> `PermissionBuilderTool` branches on `Bash` / `PowerShell` / `WebFetch` / MCP;
> `ShowCommandInput => SelectedTool is Bash or PowerShell` and
> `ShowDomainInput => SelectedTool is WebFetch` are per-tool input affordances;
> `BuildPathSpecifier()` encodes Claude's `//abs` · `~/home` · `/project` · `./cwd` anchors;
> and it string-builds `$"WebFetch(domain:{d})"` and `$"mcp__{server}__{tool}"`, validating
> the result through `PermissionRule.TryParse`. OpenCode emits a `{tool: {glob: action}}`
> map entry instead — a different artifact, not the same one with different data.
>
> **This also contradicted Phase 9**, which already (correctly) specified a purpose-built
> two-level tool × pattern grid for OpenCode. Phase 9 was right; this section was wrong.

Corrected split — see the third revision above for why the tester moved too:

| Component | Lines | Verdict |
|---|---|---|
| `GuidedRuleBuilderViewModel` | 530 | **Claude-only** — rule-syntax generator |
| `PermissionTesterViewModel` | 250 | **Claude-only** — constructs Claude candidates, calls `BashCommandSplitter` |
| `PermissionRuleEducationPanel` | 16 | Claude-only; OpenCode gets its own explaining globs |
| `IPermissionRuleSource` / `IPermissionRuleSink` | 65 | **Claude-only after all** — draft 12 called these "narrow interfaces, no syntax" without reading them. `IPermissionRuleSink` declares `AddAllow` / `AddDeny` / `AddAsk`, i.e. **Claude's three buckets as three methods**, taking `PermissionRule` and returning `PermissionCollision`. `IPermissionRuleSource` returns `ScopedPermissionRules` — the allow/deny/ask triple. Both are Claude's shape in interface form. |
| `IPermissionPathPicker` | 22 | **Shared** — `PickFileAsync()` / `PickFolderAsync()`. And note it isn't really about permissions at all: it belongs in the shell's dialog services, not a permissions assembly. |
| View templates (AXAML layout of tester + builder panels) | — | **Shareable as templates**, bound to per-product VMs |

**Fourth revision, and it converges.** Of the 945-line permission folder, the genuinely
shareable code is **22 lines that aren't permission-specific**. `ClaudeForge.Avalonia` keeps
~920 of them. Combined with the model-layer finding above, the honest summary is: *nothing
in the permission subsystem is shared except an outcome enum and a decision shape.*

**Therefore delete `AgentForge.Avalonia.Permissions` from the assembly map.** There is
nothing left to put in it — the path picker goes to the shell's services, the outcome
vocabulary to `AgentForge.Abstractions`, the templates to the shell.

**What stays product-specific (`IPermissionModel`):**
```csharp
IReadOnlyList<NormalizedRule> Parse(JsonNode config);   // 3 arrays  |  nested map
JsonNode Format(IReadOnlyList<NormalizedRule> rules);
IRuleMatcher MatcherFor(string tool);                    // Claude's 8 matchers | OpenCode's glob
IReadOnlyList<string> Tools { get; }                     // 30 fixed | 15 + arbitrary
```

Claude keeps its ~1,070 lines of sophisticated matchers (`BashCommandSplitter`,
`PathRuleMatcher`, `WebFetchRuleMatcher`, `McpRuleMatcher`, `BareToolMatcher`);
OpenCode's matcher is one glob implementation with `~`/`$HOME` expansion, roughly 150
lines. `PermissionTools` becomes `IPermissionModel.Tools`.

**Net effect:** the permission *editor, tester, and collision detection* — the expensive,
well-tested parts — serve both products. Only parse/format and per-tool matching are
written twice, and OpenCode's half is small.

### Problem 6 — Artifact locations are **config-declared**, not just convention **[NEW]**

Draft 2 listed OpenCode's artifact directories as if they were a fixed set like Claude's.
They are not, and this is the deepest structural difference between the two products.

**Claude is convention-driven.** `UserMemoryCategory` is a *closed* enum
(`src/ClaudeForge.Sdk/Memory/UserMemoryCategory.cs` says so explicitly) mapping each
category to one fixed on-disk location: `~/.claude/{agents,commands,hooks,plans,rules,skills}/`
plus `.claude/…` and read-only plugin copies under `~/.claude/plugins/`. Scanning is a
directory walk. *(Note: `CrossToolMemory` already scans `.opencode/*.md` — ClaudeForge
knows OpenCode exists.)*

**OpenCode is partly declared in the config it is editing.** Five keys move artifact
locations at runtime:

| Key | Effect |
|---|---|
| `skills.paths[]` | Extra skill folders — **arbitrary paths from config** |
| `skills.urls[]` | Skills fetched from **remote URLs** (`…/.well-known/skills/`) |
| `instructions[]` | Extra rule files by **glob** *and* **remote URL** |
| `references{}` | Named **git repos** (`repository` + `branch`) or local dirs, cloned under `<data>/repos/<host>/<path>` |
| `plugin[]` | npm package specs, or `[name, options]` tuples; plus `.opencode/plugin/*.ts` |

On top of that:
- **Three global roots**, not one: `~/.config/opencode/`, `~/.claude/`, `~/.agents/` —
  and `OPENCODE_CONFIG_DIR` relocates the first.
- **Project skills traverse upward** from cwd to the git worktree root. Claude uses the
  project root only.
- **Agents and commands exist in two forms at once** — markdown files *and* inline JSON
  (`Config.agent{}` / `Config.command{}`, both `additionalProperties`-open). A single
  logical agent may be defined either way.
- **Seven built-in agents are overridable by name** — `Config.agent` names `plan`,
  `build`, `general`, `explore`, `title`, `summary`, `compaction` explicitly.

**Solution — `AgentForge.Artifacts`, a resolution engine.** Replace "walk these
directories" with "resolve the effective artifact set from an ordered list of *sources*":

```csharp
interface IArtifactSource { ArtifactKind Kind; IArtifactScope Scope; IEnumerable<ArtifactRef> Enumerate(); }
// convention dir · config-declared path · glob · inline-JSON map · remote URL · git reference
```

The resolver returns, per artifact name, **the winner plus everything it shadowed** —
structurally identical to what `LayeredValue` already does for settings. That means the
existing scope-badge UI transfers directly: an agent named `build` can show
*built-in → global JSON → global markdown → project JSON → project markdown*, with the
winner marked, exactly as a setting shows which scope provides its value.

Claude's implementation is the degenerate case: fixed convention sources only, so
`UserMemoryService` becomes one `IArtifactSource` set and behaviour is unchanged.

**Deliberate v1 limits:** remote sources (`skills.urls`, remote `instructions`,
git `references`) are **listed and explained but not fetched**. Showing "this config pulls
skills from `https://…`" is the honest, useful 90%; a fetching cache is post-v1.

### Problem 7 — Rules exist in both, with different semantics **[NEW]**

The maintainer asked whether OpenCode supports rules. **It does** — and the shape
difference matters more than the presence.

| | Claude Code | OpenCode |
|---|---|---|
| Primary file | `~/.claude/CLAUDE.md` · `<project>/CLAUDE.md` | `~/.config/opencode/AGENTS.md` · `<project>/AGENTS.md` |
| Extra rules | `~/.claude/rules/**/*.md` — **directory, load all**, recursive | `instructions[]` — **config array**, globs + remote URLs |
| Discovery | fixed locations | **traverse upward** from cwd; **first match wins per category** |
| Fallback | — | `~/.claude/CLAUDE.md` when the global `AGENTS.md` is absent (v1 behaviour) |
| Combination | all files load | global + project combined; project wins on conflict; `instructions` files concatenate **in order** and *add to* rather than replace the AGENTS.md stack |

ClaudeForge already models the Claude half — `UserMemoryCategory.Rule` covers
`~/.claude/rules/**/*.md` and `PrimaryMemory` covers `CLAUDE.md`/`AGENTS.md`.

**What OpenCode needs that Claude does not:** a **rule-resolution view**, because
first-match-wins plus glob expansion plus ordering means *the file list is not the answer*.
The page must show which files actually load, in what order, and **which were shadowed** —
the same resolver from Problem 6, applied to `ArtifactKind.Rule`.

**Two known gotchas worth surfacing in the UI** — precisely the class of thing this tool
exists for:
1. **`OPENCODE_CONFIG_DIR`'s `AGENTS.md` is silently ignored** when
   `~/.config/opencode/AGENTS.md` also exists (upstream issue: the global-files loop breaks
   after the first hit). A user who relocated their config dir loses their global rules with
   no error.
2. **`@file` references inside `AGENTS.md` are not auto-expanded** — unlike Claude. Flag
   them and point at `instructions[]` as the supported mechanism.

**Version-dependence is real.** OpenCode v2 docs state the `CLAUDE.md` fallback no longer
applies and that nested `AGENTS.md` files are discovered lazily by the read tool and
injected nearest-first, once per session. So resolution semantics differ by version —
Spike S9. ClaudeForge already has `ProductVersionProbe` to detect the installed version;
gate the resolver on it rather than assuming.

### Problem 8 — Localization **[LOCKED: shared resx set]**

`src/ClaudeForge/Localization/Strings.resx` holds **789 keys × 9 locales**;
`ClaudeForge.Avalonia` has a second, English-only resx.

Two `Directory.Build.targets` guards make this delicate: the **dead-string guard** (fails
the build on an unreferenced key) and the **dynamic-access tripwire** (fails on
`Strings.ResourceManager` / `typeof(Strings)`). `LocalizationParityTests` additionally
forbids `TODO` markers and near-copies of English.

**Solution.** Split into `AgentForge.Localization` (neutral: buttons, scope badges,
dialogs, status, nav chrome) plus per-app product resx. Both guards must become
project-aware — they currently assume one resx set per app. `Strings.Designer.cs` stays
hand-maintained (deliberately not source-generated).

**The volume is real and it is the plan's largest non-code cost [decision 13].** OpenCode
needs an estimated **200–300 new keys** — nav titles and descriptions, 17 Essentials cards
each with a "why this matters" body, rules-resolution explanations, the ~28 plugin-event
descriptions, gotcha warnings, credential-status labels, and the new editors' chrome.
Against 8 non-English locales that is **≈1,600–2,400 translations**.

**Full parity ships; the gate stays as-is.** `LocalizationParityTests` forbids `TODO`
markers and rejects near-copies of English, and weakening it is what would let parity rot.
Process: machine-translate in bulk, then spot-check — the same route the existing 789 keys
took. Practical notes:

- **Write the English keys first and freeze them** before translating. Retranslating churned
  strings is where the cost actually blows up.
- **Batch translation once, at the end of each feature phase**, not per-commit. The dead-string
  guard means a key can't be added before it's referenced, so batching is natural.
- **Reuse aggressively.** Most chrome (buttons, badges, dialogs, status) is already
  translated and moves to the shared set — a large share of the 789 existing keys should
  land there rather than being duplicated. Do that inventory *before* estimating the new
  key count; the real number may be well under 200.

---

## Product sections **[REVISED]**

Mirroring ClaudeForge's Claude Code / Claude Desktop split, OpenCodeForge ships multiple
sections over the same shell.

| Section | Config | Schema | Notes |
|---|---|---|---|
| **OpenCode** (core/server) | `~/.config/opencode/opencode.json`, project `opencode.json` | `config.json` — 36 keys | The primary section. Full scope ladder. |
| **OpenCode TUI** | `~/.config/opencode/tui.json` | `tui.json` — 13 keys, **1.1 MB** | `theme` · `keybinds` (184 actions) · `cursor` · `mouse` · `scroll_*` · `diff_style` · `attention` · `prompt`. Zero key overlap with the core schema. |
| **OpenCode Desktop** | TBD — Spike S5 | TBD | The desktop app is real (`brew install --cask opencode-desktop`, Scoop on Windows; beta). Reporting suggests it reads the same `config.json`; if so it is a *presence indicator + shared section*, not a third config surface. **Do not build it until S5 confirms.** |

OpenCode uses a **client/server architecture** — one agent, many frontends (TUI, desktop,
mobile, IDE). That is precisely why the config splits across `config.json` (agent/server)
and `tui.json` (one client), and it is a genuinely better fit for the section model than
Claude's split is.

---

## OpenCode Essentials — yes, there is a strong set **[NEW]**

ClaudeForge's Essentials page pins 11 hand-curated cards across four severity tiers
(red security / red cost / amber quality / blue behaviour), each carrying a title, a
"why this matters" body, a reactive `IsDangerPredicate` that raises a standing red banner
on a known-unsafe value, and a "View in *group*" deep link. Curation lives in
`EssentialsViewModel.BuildCards`; the card kinds are `Bool` / `Int` / `EnumString` /
`StringList`.

OpenCode has an equally meaningful — arguably sharper — set, because several of its
highest-impact knobs are single booleans or single enums with no Claude analogue.

| # | Card | Key | Tier | Why it matters | Danger state |
|---|---|---|---|---|---|
| 1 | **Global approval mode** | `permission` (bare string, or `"*"`) | 🔴 security | A bare `"allow"` auto-approves *every* tool. The direct analogue of Claude's bypass-permissions knob. | resolves to `allow` |
| 2 | **Shell command approval** | `permission.bash` | 🔴 security | `allow` = unattended arbitrary shell. | `allow` |
| 3 | **File edit approval** | `permission.edit` | 🔴 security | `allow` = unattended writes. | `allow` |
| 4 | **Outside-project access** | `permission.external_directory` | 🔴 security | `allow` = reads/writes beyond the worktree. | `allow` |
| 5 | **Network tools** | `permission.webfetch` · `permission.websearch` | 🟠 security | The exfiltration and prompt-injection surface. | both `allow` |
| 6 | **Session sharing** | `share` | 🔴 privacy | `"auto"` uploads every session to a shareable link. **No Claude analogue and very high impact** — arguably the single most important card. | `auto` |
| 7 | **Snapshot tracking** | `snapshot` | 🔴 safety | `false` disables filesystem snapshots — no undo after a bad edit run. | `false` |
| 8 | **Plugins** | `plugin[]` | 🔴 security | npm packages loaded into the agent process. No marketplace-trust layer like Claude's. | non-empty (informational, not alarming) |
| 9 | **Model** | `model` | 🔴 cost | Same reasoning as Claude's card. Picker sourced from `provider.*.models` minus the gating arrays — see Providers and models. | pinned model's provider is disabled |
| 10 | **Small model** | `small_model` | 🟠 cost | Drives title/summary/compaction traffic; a wrong pin here is a quiet recurring cost. Same picker. | same |
| 11 | **Subagent depth** | `subagent_depth` | 🟠 cost | Nesting multiplier on every delegated task. **Amber, ruled by the maintainer** — not behaviour. | > 2 |
| 12 | **Auto-compaction** | `compaction.auto` | 🟠 quality | `false` means sessions hit the context wall instead of compacting. | `false` |
| 13 | **Tool output limits** | `tool_output.max_lines` · `max_bytes` | 🟠 quality | Truncation thresholds — the closest analogue to Claude's token-budget env cards. | — |
| 14 | **Auto-update** | `autoupdate` | 🔵 behaviour | Direct analogue of `autoUpdatesChannel`; tri-state (`true` / `false` / `"notify"`). | — |
| 15 | **Default agent** | `default_agent` | 🔵 behaviour | Which agent you land in — `plan` vs `build` changes default tool access. | — |
| 16 | **Rules in effect** | *derived* | 🔵 quality | "No global `AGENTS.md` found", or "your `OPENCODE_CONFIG_DIR` `AGENTS.md` is being ignored" (Problem 7 gotcha #1). Read-only card linking to the Rules tab. | ignored-rules detected |
| 17 | **Active config file** | *derived* from `OPENCODE_CONFIG` / `OPENCODE_CONFIG_DIR` / `OPENCODE_CONFIG_CONTENT` | 🔵 diagnostic | *Which file am I actually editing?* This is the structural analogue of Claude's "effective source" sub-row, and it directly defuses the config-dir confusion. | inline-config override active |

**Two card kinds are new:** a *derived / read-only* kind (#16, #17) that reports resolver
state rather than editing a key, and a *tri-state enum* for `autoupdate`'s
`true | false | "notify"` union. Everything else reuses the existing
Bool / Int / EnumString / StringList kinds unchanged.

**Reuse note.** `EssentialsCardViewModel` already takes read/write delegate closures so
the card is agnostic about which accessor it talks to — that indirection is exactly what
lets the same card type serve a JSON path, an env var, or (new) a derived resolver value.
The page itself is generic; only `BuildCards` is per-product. Move
`EssentialsCardViewModel` / `EssentialsCardKind*` / `EssentialsView` into
`AgentForge.Avalonia.Shell` during Phase 5 and give each app its own `BuildCards`.

**All 17 ship [decision 11].** Seventeen is more than Claude's eleven because OpenCode
genuinely has more single-knob, high-impact settings — `share: auto`, `snapshot: false`, a
bare `permission: allow`, and an executable `plugin[]` each turn one value into a
security or safety decision. Curation lives in one method (`BuildCards`), so trimming after
seeing it rendered at Gate D is a one-line change.

---

## Danger indication — a core tenant, applied systematically **[NEW]**

Being explicit about which settings are dangerous is one of ClaudeForge's defining
tenants. OpenCode needs the same treatment, and doing it properly means fixing two gaps
in how the concept is currently implemented.

### How it works today, and where it stops

`EssentialsCardViewModel` carries a `severityColor` hex → `SeverityBrush` dot, plus an
`IsDangerPredicate` recomputed on every read/write (`RecomputeIsDanger`) that raises a
standing red banner when the **current value** is unsafe. Four tiers, hardcoded in
`EssentialsViewModel.BuildCards`:

| Tier | Hex | Meaning |
|---|---|---|
| 🔴 | `#D32F2F` | security · cost |
| 🟠 | `#F4B400` | quality |
| 🔵 | `#1976D2` | behaviour |
| ⚪ | `#9E9E9E` | fallback |

**Gap 1 — danger is Essentials-only.** Grep for `IsDanger`/`Severity` and the only
surfaces are `EssentialsCardViewModel`/`EssentialsViewModel`, `PermissionsEditorViewModel`,
and search. **The general settings tree has no danger surface at all** —
`PropertyEditorWrapper` carries only an advisory amber structure warning. So a user who
reaches `sandbox.enabled` through the settings tree instead of the Essentials page sees no
severity signal whatsoever. That is already true for Claude and would be worse for
OpenCode, whose dangerous keys are more numerous and more scattered.

**Gap 2 — the severity colours are raw hex literals**, not design tokens, bypassing the
`UI-STYLE-GUIDE.md` §2 token policy and the light/dark brush system. They happen to work
today; with two apps sharing one shell and both needing correct light/dark rendering, they
should become `AppSeverity{Critical|Caution|Info}Brush` tokens alongside the existing
`AppCautionBrush`.

### What this plan does

**Promote danger to a per-product classifier service.**

> ⚠ **Draft 10 got the carrier wrong.** It said danger would ride on
> `IEditorSchema.Metadata`, "the interface already has an open extensibility bag for exactly
> this". Two problems:
>
> 1. **`Metadata` is write-only today.** `ClaudeSchemaAdapter.BuildMetadata` populates it,
>    but grepping `.Metadata[` across `src/` returns **zero** consumers. There is no
>    existing plumbing to follow — draft 10 implied there was.
> 2. **More fundamentally, `IEditorSchema` is per-property and scope-independent.** Danger
>    is not. `provider.*.options.apiKey` is *caution* at global scope and *critical* at
>    project scope, and `share` is only dangerous when its value is `auto`. A static
>    per-schema annotation cannot express either.
>
> **Correct shape:** an `IDangerClassifier` on the product descriptor —
> `Classify(path, scope, currentValue) → { Severity, IsDangerNow, Explanation }` — backed by
> the bundled per-product table. `IEditorSchema.Metadata` may optionally carry the *static
> tier* so the wrapper can render a dot without a service call, but the **scope escalation
> and the value predicate must be evaluated, not annotated**. If `Metadata` is used at all,
> note this plan would be its first consumer.

A single per-product table then drives **every** surface a setting appears on:

| Surface | Today | After |
|---|---|---|
| Essentials cards | severity dot + danger banner | unchanged (now token-driven) |
| **Settings tree** | **nothing** | severity dot beside the property name; danger banner on the row when the current value is unsafe |
| **Effective-settings view** | nothing | severity column, so "what's dangerous right now, across all scopes" is one screen |
| **Search results** | partial | severity dot on hits, so a search lands on a knob already labelled |
| **Save-preview dialog** | nothing | flag when a pending change **raises** danger — the last honest moment to stop |

The last row is the most valuable and the cheapest: `JsonDiff.Compute` already produces
the per-property change list the dialog renders, so evaluating the danger predicate against
the *new* value is a lookup, not new machinery.

**Danger is scope-aware.** Several settings are far more dangerous at project scope than at
user scope, because a project config is committed to git. `provider.*.options.apiKey` in
`~/.config/opencode/opencode.json` is a local secret; the same key in a project
`opencode.json` is a **secret published to everyone with repo access**. The danger
predicate therefore takes the writing scope, and the same knob can render blue at one scope
and red at another. This applies to Claude too (`.claude/settings.json` is likewise
committed) — a genuine improvement for both products.

### Danger assessment — OpenCode `config.json` (all 36 keys)

🔴 **Critical — security, privacy, or unrecoverable data**

| Key | Why | Danger state |
|---|---|---|
| `permission` (bare string or `"*"`) | Auto-approves **every** tool. The bypass-permissions analogue. | resolves to `allow` |
| `permission.bash` | Unattended arbitrary shell execution. | `allow`, or an over-broad glob (`*`, `git *` is fine; `rm *` is not) |
| `permission.edit` | Unattended file writes. | `allow` |
| `permission.external_directory` | Reads/writes outside the worktree. | `allow` |
| `permission.webfetch` · `websearch` | Exfiltration and prompt-injection surface. | both `allow` |
| `share` | `"auto"` uploads **every** session to a shareable link. No Claude analogue. | `auto` |
| `snapshot` | `false` disables filesystem snapshots — no undo after a bad edit run. | `false` |
| `plugin[]` | npm packages loaded into the agent process; no marketplace-trust layer. | non-empty (informational) |
| `mcp.*` | Each server is executable code or a network endpoint. | any `enabled: true` server; remote servers without `oauth` |
| `provider.*.options.apiKey` | **Plaintext secret in the edited file.** | set at all — and **critical** at project scope (git-committed) |
| `provider.*.options.baseURL` | Repoints a provider at an arbitrary endpoint — credential and prompt exfiltration. | non-default host |
| `enterprise.url` | Routes the agent through an enterprise backend. | set |
| `instructions[]` | **Remote URLs inject attacker-controllable text into every session's prompt.** Easy to miss and genuinely dangerous. | any `http(s)://` entry |
| `skills.urls[]` | Remote skills — remote instructions, fetched and trusted. | non-empty |
| `server.hostname` · `server.cors` | Binding beyond loopback, or wide CORS, **exposes the agent to the network**. | not `127.0.0.1`/`localhost`; `cors` containing `*` |
| `server.mdns` | Advertises the agent on the local network. | `true` |

🟠 **Caution — cost or quality**

`model` · `small_model` (cost) · **`subagent_depth`** — **ruled amber** [maintainer]: each
level multiplies delegated work, so it is a genuine cost lever, not merely behaviour;
danger state `> 2` · `compaction.auto` (`false` → context overflow) · `compaction.prune` ·
`tool_output.max_lines`/`max_bytes` (truncation → quality) · `attachment.image.*` ·
`tools{}` (disabling core tools) · `formatter`/`lsp` (`false` → quality) ·
`disabled_providers`/`enabled_providers` (can silently break model resolution) ·
`watcher.ignore` · `references{}` git entries (clones a repo) · `experimental.*` (unstable
by declaration) · `agent.*.temperature`/`top_p`/`steps`

🔵 **Behaviour** — `autoupdate` · `default_agent` · `shell` · `username` · `logLevel` ·
`server.port` · `server.mdnsDomain` · `command{}` · local `references{}` ·
deprecated `mode`/`autoshare`/`reference`/`layout`

**Per-agent inheritance:** `agent.*.permission` re-uses the whole permission tier table,
scoped to that agent — so an agent granted `bash: allow` is red even when the global
`permission` is safe. The agent editor must show that.

### Danger assessment — OpenCode `tui.json` (13 keys)

🔴 `plugin[]` · `plugin_enabled{}` — same executable-code reasoning as the config section.
🔵 Everything else — `theme` · `keybinds` · `cursor` · `mouse` · `scroll_speed` ·
`scroll_acceleration` · `diff_style` · `attention` · `prompt` · `leader_timeout`.
Keybinds are behaviour-only; rebinding cannot grant capability.

### Implementation notes

- The danger table is **bundled data per product**, alongside the nav grouping map — not
  hardcoded in view-models. That keeps it reviewable as a single artifact and lets a schema
  refresh flag keys that gained or lost a danger classification.
- **Dual-code every indicator** (colour *and* glyph), matching the status-pill principle
  already documented in `UI-STYLE-GUIDE.md` — colour alone fails colour-blind users.
- Contrast: severity dots and banners must clear the same two budgets the status pills do
  (≥1.3:1 against the surface, ≥4.5:1 for glyph/text against the fill).
- **Tests:** every table entry has a predicate test (safe value → not dangerous, unsafe
  value → dangerous); scope-sensitive entries assert *both* scopes; a coverage test asserts
  every schema key appears in the danger table exactly once, so a schema refresh that adds
  a key fails until it is classified. That last test is what stops the table silently
  rotting behind upstream.

---

## Parameter-count violations — max 6 positional **[NEW, maintainer standard]**

**Standard: more than 6 positional parameters is too many to read.** A full scan of `src/`
finds **12 declarations** over the line. Ten of them sit in code this plan already touches,
so fixing them is near-free if done opportunistically and expensive as a separate sweep.

Two categories, and they need different prescriptions:

### Non-record classes and methods — the real problem (6)

| Declaration | Params | Touched by | Fix |
|---|---|---|---|
| `EssentialsCardViewModel` | **14** | Phase 11.5 (severity enum) | **Options record.** Worst offender, and `BuildCards` calls it ~12 times. |
| `SettingsGroupEditorViewModel` | 9 | Phase 5 (shell extraction) | Options record while it moves. |
| `SearchResultViewModel` | 7 | Phase 5 | Options record. |
| `InstallCommandViewModel` | 7 | Phase 8 | **Already compliant in spirit — verified.** The 7-param ctor is `private`, behind `ForClaudeCode()` / `ForClaudeDesktop()` static factories. Just add `ForOpenCode()` / `ForOpenCodeDesktop()`; **do not widen the ctor**. *(Draft 13 prescribed making it private — it already is.)* |
| `NavigationTreeBuilder.BuildGroup` | 7 | Phase 8 (moves to shell, takes a grouping table) | Parameter object — it is about to gain the table argument. |
| `ParsedPermissionRule` | 8 | untouched | **Already mitigated** — private ctor behind `TryParse`. Leave. |

### Positional records — idiomatic C#, judged by call site (6)

`BackupManifest` (13) · `ArtifactEditSnapshot` (8) · `EditableMemoryEntry` (8) ·
`McpServer` (8) · `ModelInfo` (8) · `PermissionCandidate` (8).

Positional records are the language's intended shape for DTOs, and `with`-expressions work
on init-only properties too, so conversion is cheap but not always warranted. **Rule:
require named arguments at construction sites, or convert to init-only properties if the
type is constructed in more than a couple of places.** `PermissionCandidate` is already
mitigated (private ctor + per-tool factories). `BackupManifest` at **13** is over the line
regardless of idiom — convert it.

> **Incidental finding while verifying this:** there are **two `BackupManifest` types** —
> a class in `AgentForge.Core/Backup` with `[JsonPropertyName]` attributes, and a `sealed
> record` in `Sdk/Backup` that exists so *"no JSON-serialization attributes leak into the
> public surface"*. Deliberate, but it means the persisted-format migration (archive layout +
> `ExportManifest` + `BackupMode`) must update **both**, and a change to one without the
> other is a silent divergence between what is written and what the SDK exposes.

### New code must comply

This matters more than the retrofit: Phase 9 builds OpenCode's own `McpServer`-analogue,
permission candidate, and agent/command records. **Do not reproduce the 8-param shape.**
Same for the `IDangerClassifier` and `IArtifactSource` contracts.

### Guard — copy the **ratchet**, not a flat allow-list

I cited `AxamlAccessibilityCoverageTests` as precedent; reading it, the pattern is better
than "allow-list" and worth copying exactly. It holds a
`IReadOnlyDictionary<string,int>` **baseline of per-file counts** with four properties:

1. A file's count must be **at or below** its baseline entry — so things can only improve.
2. A **new** file has no entry → expected **0** → the strictest rule applies to new code.
3. A PR that fixes violations is expected to **decrement the entry**, locking the new floor.
4. Renaming or deleting a baselined file **fails with "Baseline entry X no longer exists"**,
   so the dictionary cannot rot.

Apply the same shape to parameter counts: a reflection test over public constructors and
methods keyed by declaring type, seeded with the 12 current violations, where new
declarations get an implicit ceiling of 6 and fixes ratchet the baseline down. That
combination — green on day one, strict for new code, self-cleaning under renames — is what
makes the standard enforceable rather than aspirational, and it is why the accessibility
backfill has actually progressed rather than stalling as a TODO.

No analyzer package needed. Property 4 matters most here, because this plan renames a great
many types.

---

## Making the danger tenant stricter — low-friction enforcement **[NEW]**

The repo already has the right precedent: `Directory.Build.targets` carries a
`GuardUnusedResxKeys` target that **fails the build** on an unreferenced resx key, plus a
dynamic-access tripwire. It runs `AfterTargets="Build"`, skips design-time builds, and is
opt-out-able via `RunResxKeyGuard` — and it is deliberately **disabled during publish**,
because inline `RoslynCodeTaskFactory` tasks intermittently fail under concurrent builds.
Any new guard must follow that exact shape.

Five candidates, ordered by friction:

| # | Guard | Friction | Effect |
|---|---|---|---|
| 1 | **Severity is non-nullable** in the danger-table record | ~zero | "No ruling" becomes unrepresentable. A key gets a tier or the table doesn't compile. |
| 2 | **Coverage test** — every schema key classified exactly once | ~zero | A schema refresh adding a key fails until someone classifies it. Stops the table rotting behind upstream. Already in the test plan; listed here because it *is* the enforcement. |
| 3 | **Save-preview assertion** — a pending change that raises danger must be flagged | ~zero | One test. Closes the gap where a user can write a dangerous value without ever seeing a warning. |
| 4 | **Dual-coding guard** — any severity indicator in AXAML binds a glyph, not only a brush | low | Extends the existing `AxamlAccessibilityCoverageTests` scanner rather than adding a mechanism. Enforces the colour-blind rule `UI-STYLE-GUIDE.md` already states as principle. |
| 5 | **No raw hex colours in view-models** — build-time tripwire | one-time migration | Forces the hex→token migration below. After it lands, friction is zero forever. |

**Recommendation: take all five.** 1–3 are free, 4 extends an existing scanner, and 5's
cost is a migration that Phase 11.5 already requires. Together they turn "we try to mark
dangerous settings" into "a dangerous setting cannot ship unmarked" — which is what a
tenant should mean.

### The hex→token migration — **both apps, not just the new one**

The severity colours are raw hex literals today, bypassing the `UI-STYLE-GUIDE.md` §2 token
policy and the light/dark brush system. **ClaudeForge's are migrated as part of this plan**,
in the same commit as OpenCodeForge's are written — not deferred, and not left as a
"ClaudeForge does it the old way" exception. Two apps sharing one shell cannot carry two
colour conventions.

Exact sites, all four:

| Literal | Where | Becomes |
|---|---|---|
| `#D32F2F` | `EssentialsViewModel.BuildCards` — security + cost cards | `AppSeverityCriticalBrush` |
| `#F4B400` | `EssentialsViewModel.BuildCards` — quality cards | `AppSeverityCautionBrush` |
| `#1976D2` | `EssentialsViewModel.BuildCards` — behaviour cards | `AppSeverityInfoBrush` |
| `#9E9E9E` | `EssentialsCardViewModel` constructor — parse-failure fallback | `AppSeverityNeutralBrush` |

Note the fourth: `EssentialsCardViewModel` currently takes a **string** `severityColor` and
`Color.TryParse`s it, falling back to grey when parsing fails. Under the token model the
card should take a **severity enum**, not a colour string — which deletes the parse, the
failure path, and the fallback literal outright. That is strictly less code than today.

> While in there: the constructor takes **14 positional parameters** — the worst violation
> of the max-6 standard in the codebase — and this plan adds three more (two card kinds plus
> a scope argument for the danger predicate). **Convert it to an options record here**,
> where the signature is already being changed. See Parameter-count violations.

Both apps' severity brushes then resolve through `App.axaml` / `Resources/`, are verified in
light **and** dark against the two contrast budgets the status pills already document, and
are covered by the guard so no future card can reintroduce a literal.

**One deliberate non-guard:** do not try to enforce *correctness* of a tier. Whether a given
knob is caution or behaviour is a judgement call, and a guard that pretends otherwise would
just get suppressed. Enforce that a ruling **exists**, is **visible**, and is
**dual-coded**; leave the ruling itself to review — as with `subagent_depth`, ruled
**amber** below.

---

## Guides and docs — disposition for two apps **[NEW]**

The repo carries **6,528 lines across 25 documents** (11 root, 10 `docs/`, 4 sidecar
`AGENTS.md`). Every one needs a disposition, because a guide that silently describes only
one of two apps is worse than no guide — the whole methodology rests on these being
fact-shaped and current.

### Root documents

| Doc | Lines | Disposition |
|---|---|---|
| `AGENTS.md` | 531 | **Split.** The largest doc job. Invariants and "if you're doing X" checklists divide into shared (`AgentForge.*`) and per-app. Several entries die outright (`ClaudeScope._cache` ordering); one splits in two ("adding a debug flag" → shared vs per-app). |
| `PLATFORM.md` | 423 | **Shared as-is.** The `PlatformInfo.Current` vs `OperatingSystem.IsWindows()` decision tree is product-neutral. |
| `LOCALIZATION.md` | 375 | **Shared, adapt.** Document the split resx sets and the project-aware guards. |
| `README.md` | 336 | **Duplicate.** One per app. ClaudeForge's notes that the repo also hosts OpenCodeForge (decision 14). |
| `TRIMMING.md` | 517 | **Shared, adapt.** Baseline IL-warning counts become per-app. |
| `CONTRIBUTING.md` | 195 | **Shared, adapt.** Add the two-app layout and the `AgentForge.* → never ClaudeForge.*/OpenCode.*` layering rule. |
| `CHANGELOG.md` | 124 | **Adapt.** One changelog with per-app tags — releases are cut from one repo, and two files would drift. |
| `AGENT-ONBOARDING.md` | 125 | **Shared, adapt.** Methodology rationale is neutral; reframe "returning to ClaudeForge cold" to cover both. |
| `SECURITY.md` | 90 | **Shared, adapt.** Add the new disclosure surface: `auth.json`, `provider.*.options.apiKey`, executable plugins, and `server.hostname`/`cors` exposure. |
| `DISCLAIMER.md` | 53 | **Adapt.** Currently disclaims affiliation with Anthropic; needs the equivalent for OpenCode. |
| `CODE_OF_CONDUCT.md` | 127 | **Shared, unchanged.** |

### `docs/`

| Doc | Lines | Disposition |
|---|---|---|
| `UI-STYLE-GUIDE.md` | 566 | **Shared, adapt.** The token system belongs to the shell. Add `AppSeverity{Critical,Caution,Info}Brush`, the no-raw-hex rule, and the dual-coding requirement as normative rather than advisory. |
| `AVALONIA-GOTCHAS.md` | 432 | **Shared as-is.** Framework-level; both apps hit the same traps. Add new ones found during the extraction. |
| `ESSENTIALS-PAGE.md` | 121 | **Adapt + duplicate.** Architecture section becomes shared; the card table is per-app. |
| `LINUX-DESKTOP-INTEGRATION.md` | 194 | **Shared, adapt.** `.desktop` / `.svg` per app. |
| `MODEL-CATALOG.md` | 170 | **Claude-only.** Add a pointer noting OpenCode's model story is config-sourced and lives elsewhere (decision 4). |
| `CLAUDECTX-COMPATIBILITY.md` | 292 | **Claude-only, unchanged.** |
| `NAV-DEEP-LINKING-PLAN.md` | 636 | **Historical record.** Leave — it documents a shipped Claude feature. *(Its header is stale and says "uncommitted"; fix in Phase 1.)* |
| `NAV-DEEP-LINKING-TEST-PLAN.md` | 196 | **Template.** Becomes the model for the OpenCodeForge manual plan. Its unverified **G1 virtualization** scenario is closed at Gate C. |
| `screenshots-{light,dark}.md` | 62 | **Duplicate.** OpenCodeForge needs its own galleries. Note the real cost is not the two `.md` files but the **PNGs under `docs/screenshots/`** — a second app × every page × two themes, captured by hand and re-captured whenever the UI moves. Budget for it, or scope the gallery down to the handful of pages that actually sell the app. |

### Sidecar `AGENTS.md`

| Sidecar | Lines | Disposition |
|---|---|---|
| `src/ClaudeForge/ViewModels/Editors/AGENTS.md` | 341 | **Move to shared.** The compound-editor contract (force-fire `MarkModified`, `_isLoading` guard, `ToJsonValue()` null-when-empty, transient-field filtering) is product-neutral and both apps' editors must obey it. |
| `src/ClaudeForge/ViewModels/AGENTS.md` | 236 | **Split.** Shell view-model rules shared; Claude page rules stay. |
| `src/ClaudeForge.Core/Settings/AGENTS.md` | 250 | **Move** to `AgentForge.Core/Settings/`. Rewrite the scope section for the generalized model. |
| `src/ClaudeForge.Sdk/AGENTS.md` | 136 | **Split** across `AgentForge.Sdk` and `ClaudeForge.Sdk.Claude`. |

### New documents this plan requires

| Doc | Why |
|---|---|
| `docs/DANGER-TAXONOMY.md` | The tenant, the tiers, both products' tables, the scope-sensitivity rule, and the five guards. Without a written taxonomy the tables drift apart. |
| `docs/OPENCODE-CONFIG.md` | Scope ladder, env-var overrides, JSONC, the config-declared-artifact model. The OpenCode counterpart to what `AGENTS.md` assumes about Claude. |
| `docs/ARTIFACT-RESOLUTION.md` | The `IArtifactSource` model, shadowing, upward traversal, version-gated rule semantics. Shared. |
| `docs/JSONC-WRITER.md` | The edit-based writer's contract and byte-stability guarantees — the highest-consequence component in the plan deserves its own page. |
| `docs/OPENCODE-TEST-PLAN.md` | Manual plan, modelled on the nav-deep-linking one. |

**Sequencing:** per decision 12 these land phase-by-phase as part of definition-of-done, not
as a final documentation sprint. `DANGER-TAXONOMY.md` lands with Phase 11.5;
`JSONC-WRITER.md` with Phase 2; the `AGENTS.md` split with Phase 5.

---

## Rules and access — the headline feature **[EXPANDED]**

The maintainer flags rules/access as one of ClaudeForge's most important features, so
OpenCode parity here is a v1 gate, not a nice-to-have. Problems 5 and 7 cover the models;
this is what actually ships.

### Access (permissions) — Phases 6 + 9

Shared normalized model, shared guided builder, shared dry-run tester (Problem 5).
**Idiomatic-for-OpenCode surfaces layered on top:**

- **Tool × pattern grid**, not Claude's three rule lists. Rows are the 15 named tools plus
  any arbitrary key (MCP tool names fit natively via `additionalProperties`); each row is
  either a single action or an expandable set of glob → action rules.
- **A `*` wildcard row pinned first**, since `{"*": "ask"}` is the idiomatic OpenCode base.
- **Bare-string mode** — the whole `permission` value can be one action. Offer it as a
  mode toggle ("apply one rule to all tools" ↔ "per-tool rules") rather than making users
  hand-edit JSON to reach it.
- **Per-agent overrides.** `AgentConfig.permission` is the same shape, so the same editor
  binds to it from the Agents tab — an agent's effective permissions shown as
  *global → agent override*, reusing the scope-badge affordance.
- **Dry-run tester** answers "would `git push --force` be allowed, and which rule decided
  it?" — the shared `PermissionDecision` already carries matched-rule/bucket/scope for the
  explanation.
- **Collision detection** — the shared `PermissionCollisionDetector` flags e.g.
  `"git *": "allow"` shadowed by `"*": "deny"`.

### Rules (instructions) — Phase 11

- **Resolution view, not a file list** — load order, glob expansion, shadowed entries.
- **Editable in place** — the same markdown editor the Agents/Skills tabs use, so
  `AGENTS.md` and any `instructions[]` match are editable without leaving the app.
- **`instructions[]` array editor** with live glob resolution: type `packages/*/AGENTS.md`
  and see the matches immediately. Remote URLs listed, not fetched (v1).
- **Gotcha surfacing** — the `OPENCODE_CONFIG_DIR` shadowing bug and unexpanded `@file`
  references, both as inline warnings.
- **Cross-tool badge** — `~/.claude/CLAUDE.md` reached via OpenCode's fallback is marked
  *"shared with Claude Code — editing affects both"*.
- **Version-gated semantics** (Spike S9) — v1 and v2 resolve differently; the resolver
  branches on `ProductVersionProbe` and the page says which ruleset it applied.

---

## Detection, install banner, and update checks **[NEW]**

ClaudeForge has three distinct mechanisms here that are easy to conflate. All three need
OpenCode equivalents.

### 1. Is the managed product installed? → install banner

`PlatformPaths.IsClaudeCodeInstalled` layers three probes: anywhere on `PATH` (via
`TryFindClaudeCodeBinary`), then canonical disk locations (catches "installed but PATH not
updated", including the Windows-ARM64-npm-global case), then `~/.claude/settings.json`
exists as a belt-and-braces fallback. `IsDesktopInstalled` checks the config file first,
then *application* install directories — deliberately **not** the config's parent
directory, because uninstallers leave that behind and it produced false positives.
`IsClaudeCodeOnPath` is tracked separately so the About page can say "installed but not on
PATH" and offer **Add to PATH**.

When nothing is detected, `MainWindowViewModel` raises the install banner
(`neitherInstalled = !IsClaudeCodeInstalled && !IsDesktopInstalled`), and
`InstallCommandPanel` + `InstallCommandViewModel` render a monospace command with **Run**
(launches a terminal pre-filled) and **Copy**. `ForClaudeCode` is the shell-command flow;
`ForClaudeDesktop` is the URL-and-browser flow. `DebugFlags.ShowInstallBanner` forces it
on for testing.

**OpenCode equivalents:**

| | Probe order |
|---|---|
| **OpenCode CLI/TUI** | `opencode` on `PATH` → canonical locations (`~/.opencode/bin/`, `~/.local/bin/`, `/usr/local/bin/`, Homebrew prefix, Scoop shims) → `~/.config/opencode/opencode.json` exists → `~/.local/share/opencode/` exists |
| **OpenCode Desktop** | application install dirs (Homebrew Cask / Scoop / `%LOCALAPPDATA%`) — **not** the config dir, which the CLI also creates and which would false-positive on every CLI-only install |

Two OpenCode-specific wrinkles Claude does not have:
- **The config dir is not proof of the CLI.** `~/.config/opencode/` can be created by hand
  or by the desktop app. Keep it as a *weak* signal, ranked below the binary probe, and
  never as the sole basis for "installed".
- **`OPENCODE_CONFIG_DIR` / `OPENCODE_DATA_DIR` move the evidence.** Probes must consult
  those env vars before falling back to the defaults, or a relocated install reads as
  absent. This is the same root cause as the Problem 7 rules gotcha.

**Install commands are Spike S10** — do not guess them. A wrong install command in a
prominent banner is a bad, high-visibility bug. Confirm the current CLI installer and the
per-platform desktop commands from `opencode.ai/download` at implementation time, and
model them per-platform through `InstallCommandViewModel` exactly as
`ForClaudeCode`/`ForClaudeDesktop` do.

**Add to PATH** transfers directly — the existing Windows `HKCU\Environment\Path` and
macOS/Linux shell-rc-append implementation is product-agnostic apart from the binary name.
Move it into the shell in Phase 5 and parameterize the name.

### 2. How old is the managed product? → version display

`ProductVersionProbe.TryGetClaudeCodeVersionAsync` shells `claude --version`, with the
Windows `.cmd`/`.bat`/`.ps1` shim-wrapping logic in `ResolveCommand` (those shims cannot be
launched with `UseShellExecute=false`). `TryGetClaudeDesktopVersion` is the Desktop probe.

**OpenCode:** `opencode --version` reuses `ResolveCommand` unchanged — the shim problem is
identical on Windows. Add `TryGetOpenCodeVersionAsync`. **Also surface OpenCode's own
`autoupdate` config value next to the detected version**, since a user seeing "v1.15.11"
alongside `autoupdate: false` immediately understands why they're behind. That pairing has
no Claude analogue and is a small, genuinely useful addition.

### 3. Is *this app* out of date? → update banner

`AppUpdateService` (`CheckManualAsync` / `CheckOncePerLaunchAsync` / `CheckPeriodicAsync`)
over `GithubReleaseChecker` in Core, rendered by `UpdateBannerViewModel` +
`UpdateBanner.axaml`, with a `checkForUpdatesOnLaunch` Essentials card.

**OpenCodeForge:** the same machinery, parameterized rather than duplicated.

> ⚠ **`GithubReleaseChecker` is not product-agnostic** — draft 10 said it was. Three
> hardcodings: `DefaultReleasesLatestUrl` pins the ClaudeForge repo (found in pass 4, and
> it must move to list-and-filter anyway), the class doc names ClaudeForge, and
> `client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeForge/{appVersion}")` sends a
> **hardcoded User-Agent** — missed by the pass-4 fix. GitHub's API keys rate limits and
> abuse heuristics off the UA, so two apps reporting as `ClaudeForge` is wrong even though
> nothing visibly breaks. Parameterize all three.

`AppUpdateService` is `internal static` in the app assembly and hardcodes the current
version string — move it into the shell and inject
`{ Owner, Repo, TagPrefix, AssetPattern, UserAgent, CurrentVersion }`. Both apps then share
one code path, one set of tests, and one banner.

> **Do not let these three collapse into one.** "OpenCode is not installed", "your OpenCode
> is old", and "your OpenCodeForge is old" are three different banners with three different
> calls to action. ClaudeForge keeps them distinct and OpenCodeForge must too.

---

## Search, nav, filter, and deep-link surfaces touched **[NEW]**

Every one of these is shared machinery that the multi-product change reaches into. Called
out explicitly because they are cross-cutting and easy to miss in a phase-by-phase read.

| Surface | Current state | What changes |
|---|---|---|
| **Schema search providers** | `MainWindowViewModel.BuildSchemaSearchProviders` builds one `SchemaSearchProvider` per product, hardcoding Claude Code and Claude Desktop | Becomes a loop over `ProductSection`. Result rows already carry the provider's display name, so multi-product grouping in the results list works unchanged. |
| **Synthetic search hits** | `SearchViewModel.EssentialsTriggers` — a hardcoded trigger table with Claude phrasing (`--dangerouslySkipPermissions`, `bypassPermissions`) plus `TryAddEssentialsSyntheticHits` | Trigger tables become **per-product**, supplied by the app. OpenCode's set should include `share`/`auto-share`, `snapshot`, `permission allow`, `plugin`, `subagent depth`, and — importantly — the *gotcha* phrasings (`OPENCODE_CONFIG_DIR`, `AGENTS.md not loading`) so users searching a symptom land on the explanation. |
| **`SearchViewModel`'s header-title const** | Holds the Claude Code header node title for synthetic nav targeting | Becomes a per-product id, resolved through the section list. |
| **Nav node ids** | `NavIdClaudeCode` / `NavIdClaudeDesktop` / … consts on `MainWindowViewModel`; uniqueness is **per-parent, not tree-wide**; guarded by `NavigationNodeIdTests` | Each app owns its own id set. **`NavigationNodeIdTests` must run against both apps' trees** — extend the test's tree source rather than copying the test. |
| **`NavDeepPath`** | Grammar `<page>/<tab>/<item>`; `Slug()`; `FormatItemKey(name, source)` splitting on the LAST `@`; item keys must never contain `/` | Grammar is product-agnostic and moves to the shell unchanged. **The constraint bites harder for OpenCode**: skills are directory-named and `references{}` keys are user-chosen, so both need `Slug()`/`FormatItemKey` discipline and a test asserting no separator leaks in. |
| **`IDeepNavigable`** | Implemented only by `AgentsSkillsEditorViewModel` | Every new OpenCode page that has tabs/items should implement it — Settings groups, Agents/Commands/Skills/**Rules**, Permissions (deep-link to a tool row), Essentials (deep-link to a card). Follow the `AGENTS.md` checklist, especially: select the tab **first**, await an in-flight load via the `LastRefresh` seam rather than starting a competing one, honour `DeepRestoreMode.Locate`, and return `false` instead of throwing. |
| **`ApplyNavigationFilter`** | Two implementations (`SettingsGroupEditorViewModel`, `AgentsSkillsEditorViewModel`); the `_applyingNavFilter` latch is what raises `FilterFromNavigation` and draws the orange "navigated" frame | **Hard invariant** — a deep-link handler must never assign `FilterText` directly. Every new OpenCode page that reveals an item by filtering must use this, or the user sees a mysteriously narrowed list with no explanation. |
| **Computed filtered projections** | Binding `ItemsSource` to a computed `Filtered*` property means the source collection's `Clear()`/`Add()` no longer reaches the UI; the rebuild must raise `PropertyChanged` by hand | Same trap applies to every new OpenCode list. `AgentsSkillsEditorViewModel.NotifyFilteredListsChanged()` is the template. |
| **`WindowStateService.StatePath`** | `~/.claude/cache/ClaudeForge-gui-state.json`; must stay a **property** (`=>`) not `static readonly`, or tests bypass the sandbox | ⚠ **OpenCodeForge must not write into `~/.claude/`.** Give each app its own state path — OpenCodeForge's belongs under its own config/cache root. Getting this wrong means two apps fighting over one state file, and an OpenCode-only user acquiring a `~/.claude/` directory they never asked for. |
| **`--deep-link` + Copy-deep-link** | CLI arg parsed in `Program.cs`; unresolvable **persisted** paths stay silent, only explicit `--deep-link` warns via `RaiseDeepLinkWarning` | Moves to the shell; each app registers its own page-id resolver. Keep the silent-vs-warn asymmetry — it is a deliberate locked decision. |
| **Status bar** | Typed `SetStatusActive/Success/Warning/Failure/State` helpers; the legacy `StatusMessage` setter silently degrades to gray `State` | New OpenCode code must use the typed helpers. This is a documented invariant with a dedicated test file (`StatusControllerTests`). |

---

## Coverage check — hooks · agents · MCP servers · plugins **[NEW]**

Asked directly, and worth stating explicitly because two of the four were under-covered in
draft 4 and one has no OpenCode analogue at all.

| Claude concept | OpenCode analogue | Where it lands |
|---|---|---|
| `hooks` settings key + `~/.claude/hooks/*` scripts | **None in config** | See below — plugin events are the closest thing |
| `agents` (markdown) | markdown **and** inline `Config.agent{}` | Phase 11 (files) **+ Phase 9 (inline editor — was missing)** |
| `mcpServers` | `mcp{}` | Phase 9 ✓ already covered |
| `enabledPlugins` + `extraKnownMarketplaces` | `plugin[]` + local plugin files + TUI `plugin`/`plugin_enabled` | **Phase 9 + a Plugins page — was only an Essentials card** |

### Hooks — OpenCode has no config-declared hooks

The string `hook` appears **zero times** in either OpenCode schema. There is no
`hooks` key, no event/matcher/command shape, nothing analogous to Claude's
`PreToolUse`/`PostToolUse` config surface.

**Consequence for the plan:** `HooksEditorViewModel`, `HookEntry`, `HookEventGroup`,
`HookEventCatalog`, `HookCommandVariantInfo`, and `IHooksAccessor` are **Claude-only** and
stay in `ClaudeForge.Sdk.Claude` / `src/ClaudeForge`. Do not try to generalize them —
draft 1 already put them on the Claude-only side and that was right.

**OpenCode's closest equivalent is code, not config.** Plugins export hook implementations
subscribing to ~28 named events:

`tool.execute.before` · `tool.execute.after` · `permission.asked` · `permission.replied` ·
`session.created` · `session.idle` · `session.updated` · `session.compacted` ·
`session.deleted` · `session.diff` · `session.error` · `session.status` ·
`message.updated` · `message.removed` · `message.part.updated` · `message.part.removed` ·
`file.edited` · `file.watcher.updated` · `command.executed` · `todo.updated` ·
`lsp.updated` · `lsp.client.diagnostics` · `server.connected` · `installation.updated` ·
`shell.env` · `tui.prompt.append` · `tui.command.execute` · `tui.toast.show` ·
`experimental.session.compacting`

**What OpenCodeForge can offer instead — and it is genuinely useful:** the Plugins page
lists each installed plugin file and **which events it subscribes to**, obtained by a
shallow static scan of the exported hook names in the source. Read-only, no execution, no
sandbox needed. "What is hooking into my agent, and where?" is the same question Claude's
Hooks page answers, reached by a different route. A regex/CST scan over `.ts`/`.js`
exports is enough; if a file can't be parsed, say so rather than guessing.

### The editing pattern for OpenCode's hook equivalent

The reason Claude's Hooks page is a **compound editor** is that Claude's hooks are
*config data* — event, matcher, command, all JSON. OpenCode's are *code*. So the right
pattern is not "port the Hooks editor"; it is **the artifact-editing pattern the
Agents/Skills page already uses**, with one twist.

On the Agents/Skills page, an artifact is *front matter* (structured, form-edited) plus a
*body* (markdown, free-edited). A plugin is the same two-part shape, except **the
structured half is derived from the body rather than authored**: the event set comes from
the exported hook names in the source. So:

| Affordance | Risk | What it does |
|---|---|---|
| **Subscribed-events panel** (read-only) | none | The derived "front matter". Recomputed on save. Shows the events this plugin hooks, or *"could not parse"* — never a guess. |
| **Enable / disable** | none | TUI `plugin_enabled{}` is a genuine `name → bool` toggle. **`Config.plugin[]` has no toggle** — removal is the only off switch, so present it as *Remove* with a confirm, and state the asymmetry in the UI rather than faking symmetry. |
| **Scaffold a new plugin** | none | The highest-value affordance and the closest analogue to Claude's guided hook builder: name it, pick events from the known ~28 as a checklist, choose global or project, and **write a new `.ts` stub** with typed handler skeletons. Creating a file is safe; this is the same user intent as "build me a hook", in the medium OpenCode actually uses. |
| **Edit source** | low | Plain-text editor over the `.ts`/`.js`, exactly mirroring the raw-YAML escape hatch on the Agents/Skills page. No syntax highlighting in v1 (`JsonHighlightBlock` is JSON-only; a TS highlighter is not worth it yet). Save writes back verbatim. |
| **Append a handler stub** | low | From the events checklist on an *existing* plugin, append a new handler skeleton at end of file. |
| ~~Rewrite an existing plugin's event set~~ | **excluded** | **Hard rule: never restructure code the user wrote.** Adding may append; removing an event is the user's job in the source editor. A config tool that silently reformats or reorders someone's TypeScript loses trust permanently — the same principle behind the comment-preserving JSONC writer. |

The event catalogue itself is bundled data, mirroring `HookEventCatalog` on the Claude
side: a static list of the ~28 names with one-line descriptions, so the checklist has
tooltips and the read-only panel can label unknown exports as *"not a recognised event"*
(useful when upstream adds one before we do).

### Plugins — a page, not just a card

Three distinct surfaces, all needed:

1. **`Config.plugin[]`** — items are `string` **or** a 2-tuple `[string, object]`
   (package + options). **The published docs say no tuple form exists; the schema declares
   one.** Trust the schema, support both, and default new entries to the string form.
   Same discriminated-union editor shape as `mcp`.
2. **Local plugin files** — `~/.config/opencode/plugins/` and `.opencode/plugins/`, `.ts`
   or `.js`, auto-loaded at startup. These are artifacts: they flow through the Phase 10
   resolution engine as `ArtifactKind.Plugin`, get the shadowing treatment, and carry the
   subscribed-event list described above.
3. **TUI has its own** `plugin[]` **plus** `plugin_enabled{}` (a `name → bool` map with no
   config-section counterpart). So the TUI section gets its own plugin editor, and
   `plugin_enabled` is the one place a plugin can be toggled off without removing it.

**Security framing matters here.** npm plugin packages load into the agent process with no
marketplace-trust layer equivalent to Claude's `extraKnownMarketplaces` /
`strictKnownMarketplaces`. Essentials card #8 flags a non-empty `plugin[]`
informationally; the Plugins page should state plainly what each entry is and where it
came from. Do **not** offer an install/add-package button — surfacing and removing is the
right scope for a config editor.

### Agents — the inline-JSON half was missing

Phase 11 covers agent *markdown files*. `Config.agent{}` is the other half: an object keyed
by agent name, `additionalProperties: AgentConfig`, with seven overridable built-ins
(`plan` · `build` · `general` · `explore` · `title` · `summary` · `compaction`) named
explicitly in the schema.

`AgentConfig` carries `model` · `variant` · `temperature` · `top_p` · `prompt` · `tools`
*(deprecated)* · `disable` · `description` · `mode` · `hidden` · `options` · `color`
(hex **or** theme-name union) · `steps` · `maxSteps` *(deprecated)* · **`permission`
(a full nested `PermissionConfig`)**.

That nested `permission` is the reason this belongs in Phase 9 rather than being hand-waved:
it binds the **shared** permission editor from Phase 6 as a child of the agent editor, and
the effective view for an agent must show *global permission → agent override* using the
same scope-badge affordance. Same story for `Config.command{}` (`template` required,
plus `description` · `agent` · `model` · `variant` · `subtask`).

---

## Profile-readiness — don't build the door shut **[NEW]**

OpenCode has no profile equivalent today, and v1 ships none. But profiles are a plausible
future addition, and the difference between "cheap to add later" and "prohibitive" is a
handful of decisions taken now, all of which cost nothing.

**The seam already exists on the Claude side.** `ConfigFileDiscoverer.DiscoverClaudeCodeSettings`,
`DiscoverDesktopConfig`, and `DiscoverMcpFiles` all take `string? profileName = null`, and
`PlatformPaths` has `ProfileSettingsPath(name)` / `ProfileMcpPath(name)` /
`ProfileClaudeMdPath(name)` / `DesktopProfileConfigPath(name)`. `ConfigFileType` already
enumerates `ProfileSettings` and `ProfileMcp`. The SDK clients pass `profileName: null`
today with a comment saying profile-aware loading is post-v1 work. **A profile is just
"the same file set, rooted somewhere else."**

Five rules for the new code:

1. **Thread `string? profileName` through `OpenCodeClient.DiscoverFiles` from day one**,
   even though nothing supplies it. One unused parameter now versus a signature change
   through the whole client hierarchy, every test fixture, and both apps later.
2. **No static or singleton config-root path.** Every root resolves through a
   `ConfigRoot` value. This is not speculative work — `OPENCODE_CONFIG_DIR` /
   `OPENCODE_CONFIG` / `OPENCODE_CONFIG_CONTENT` already demand exactly this, so the
   profile-shaped seam falls out for free. A profile would simply be another resolved root.
3. **`AgentForge.Artifacts` sources take a root, not a hardcoded path.** Then a profile
   registers the same source set against a different root, and agents/commands/skills/
   rules/plugins all become profile-aware in one move rather than five.
4. **Never cache a resolved path in a `static readonly` field.** Use expression-bodied
   properties. The repo already enforces this for `WindowStateService.StatePath` as a hard
   invariant — a cached path captures the *first* root ever seen and silently ignores every
   later switch, which is precisely how a profile feature breaks.
5. **Make `ProfileEngine` root-parameterized when Core is renamed in Phase 1**, rather than
   keeping `~/.claude/profiles/` baked in. Its knowledge of *where profiles live* belongs
   on the product descriptor alongside the scope set.

   ⚠ **This is bigger than draft 10 implied.** `ProfileEngine` is not one code path — it
   carries a **doubled surface**, with parallel Claude Code and Claude Desktop variants of
   nearly every operation (`DiscoverProfiles` / `DiscoverDesktopProfiles`,
   `ReadCurrentProfileName` / `ReadCurrentDesktopProfileName`, `CreateFromLiveAsync` /
   `CreateDesktopProfileFromLiveAsync`, `ApplyProfileToLiveAsync` /
   `ApplyDesktopProfileToLiveAsync`, `SyncFromLiveAsync` / `SyncDesktopFromLiveAsync`), plus
   export/import and `ResolveProfileDirSecurely`. That duplication is itself the
   two-product hardcoding this plan removes elsewhere — so the right move is to collapse
   the pairs onto the `ProductSection` model rather than add a third variant. Treat it as
   real work in Phase 4, not a Phase 1 footnote.

Also worth noting: under Problem 1's generalized scope model, `ConfigFileType` stops being
a closed enum of Claude file kinds and becomes per-product data — so adding an OpenCode
profile file type later is additive, not a breaking change to a shared enum.

**Test-wise:** one guard is enough — assert `DiscoverFiles(projectRoot, profileName: "x")`
produces paths rooted under the profile rather than the live root, even though no UI
supplies a profile name yet. That single test is what stops the parameter from quietly
rotting into a no-op.

---

## Diagnostics windows — logs and live config changes **[NEW, in scope]**

Both apps get the full diagnostics surface. Most of it is already product-agnostic in
`LayeredEditors.Avalonia.Diagnostics` (2,763 lines) and needs wiring, not writing.

### What exists

| Component | Lines | State |
|---|---|---|
| `LiveLogWindow` | 638 | **Wired.** F12 toggle via `AvaloniaDiagnostics.ToggleLiveLogWindow()` from `MainWindow.axaml.cs`. Ships in Debug *and* Release, hidden until F12, so steady-state cost is one `Channel.Writer.TryWrite` per event. Virtualized `ListBox` (O(visible rows)); header strip shows the on-disk log path, click-to-open, plus *Open folder*. |
| `LiveTailWindow` | 270 | **Built but has no consumer in `src/` outside the diagnostics library.** Designed for *"LOW-VOLUME, EPHEMERAL streams (e.g. debounced file-watcher hits)"* — a `SelectableTextBlock` tail with free selection and Ctrl+C, capped at `MaxLines`, bursts coalesced to ≤5 UI updates/sec. |
| `BucketedRollingFileSink` · `LiveLogWindowSink` · `SerilogAvaloniaSink` | 610 | Wired. |
| `ConfigFileWatcher` | — | **Verified genuinely product-agnostic** — `Watch(string filePath)` / `Unwatch(string)` / `FileChanged`, no Claude paths, no `PlatformPaths`. *The only "reusable" claim in this plan that survived reading the implementation unchanged.* Debounced, and raises `FileChanged` **from a thread-pool thread** — subscribers must `Dispatcher.UIThread.Post`; Core has no Avalonia dependency, so it cannot marshal for you. |
| Live-write audit trail | — | `[Editor.UserEdit]` / `[Editor.Flush]` lines routed through `SettingsGroupEditorViewModel.FormatValueForAuditLog` (sensitive paths → `[redacted]`, compound values → shape summary). `WorkspaceDiagnostics.LogDiffs` adds per-leaf redacted diffs at save. |

### What this plan adds

1. **Wire both apps to the F12 log window.** Moves to the shell in Phase 5 along with
   `MainWindow`; each app registers its own `AvaloniaDiagnosticsOptions`. Logs already land
   next to the executable (`<exe dir>/logs`), so two apps separate naturally — but keep the
   existing caveat in the docs: `src/publish/publish.ps1` wipes every `bin/`+`obj/` under
   `src/` with `Remove-Item -Force`, taking the log folder with it and bypassing the recycle
   bin. Copy logs out before publishing.
2. **Give `LiveTailWindow` its intended job: a live config-activity window.** It was built
   for exactly this and never connected. Wire it to a second toggle (Shift+F12) streaming
   the *semantic* event flow rather than raw Serilog:
   - `ConfigFileWatcher` hits — *which* file changed on disk, and whether it triggered a reload
   - `[Editor.UserEdit]` — the live-write path, already redacted at source
   - `[Editor.Flush]` — the save-time safety-net flush
   - save-diff summaries from `WorkspaceDiagnostics.LogDiffs`
   - **OpenCode-specific:** artifact-resolution invalidations (a `skills.paths[]` edit
     changing the resolved artifact set) and JSONC comment-preservation notices

   This is genuinely more valuable for OpenCode than for Claude, because OpenCode's config
   *changes what the config means* — editing `skills.paths` or `instructions` re-resolves
   the artifact and rule sets, and watching that happen live is the fastest way to
   understand it. It also makes the Problem 7 `OPENCODE_CONFIG_DIR` gotcha directly
   observable instead of theoretical.
3. **Redaction is inherited, not re-implemented.** Everything reaching either window is
   already redacted at emission by `FormatValueForAuditLog` / `SensitiveKeys` /
   `JsonRedactor`. Add one test asserting `provider.*.options.apiKey` and `auth.json`
   contents never reach the tail window — a live-log window is exactly where a
   screen-shared secret leaks.
4. **Thread-affinity rule.** Anything feeding the tail window from `ConfigFileWatcher` must
   marshal — `Enqueue` is thread-safe, but any view-model state updated alongside it is
   not. This is a documented Core-side contract and a real crash source in DataGrid.

**Tests:** ingest ordering and coalescing under burst; `MaxLines` cap holds; enqueue from a
non-UI thread does not throw; redaction assertions above; window toggles do not leak on app
shutdown (`App.axaml.cs` already closes ownerless helper windows explicitly — extend that
to the second window). Note `LayeredEditors.Avalonia.Diagnostics.Tests` already has 47
tests to extend rather than start from scratch.

---

## Providers and models — correcting draft 3 **[NEW]**

Draft 3 said OpenCode's `model` is "a free-form `provider/model-id` string with providers
resolved at runtime", and concluded a model picker was out of scope. **That was imprecise
and the conclusion was wrong.** The model space has three inputs, and two of them are
readable straight out of the file being edited:

1. **Remote catalog.** `Config.model`, `Config.small_model`, `AgentConfig.model`, and
   `Config.command.<n>.model` all carry
   `"$ref": "https://models.dev/model-schema.json#/$defs/Model"` — an **external** schema
   reference to models.dev. That is the default id space.
2. **Config-declared gating.** `enabled_providers[]` (allowlist — when set, *only* these)
   and `disabled_providers[]` (blocklist) narrow it. Per-provider `whitelist[]` /
   `blacklist[]` narrow it further.
3. **Config-declared providers and models.** `Config.provider{}` maps provider id →
   `ProviderConfig { api · name · id · npm · env[] · whitelist[] · blacklist[] · options{} ·
   models{} }`. Custom providers and their `models{}` are declared **right there in the
   config**, so they are fully knowable offline.

**So yes — a real model picker is feasible**, and better than free text:

- **Offline tier (v1):** build suggestions from the config alone — every key under
  `provider.<id>.models`, formatted `provider/model`, minus anything excluded by the
  gating arrays. Zero network, always correct for custom/self-hosted setups, and it makes
  `enabled_providers` mistakes visible ("you pinned a model whose provider is disabled").
- **Catalog tier (optional):** fetch models.dev through the same
  memory → bundled → disk → HTTPS chain and opt-in-promotion machinery as the schemas
  (Phase 13). Same provenance badge, same offline fallback.

This upgrades Essentials cards #9 (**Model**) and #10 (**Small model**) from free text to
the existing free-form-with-suggestions control — the same
`FuzzyModelAutoCompleteBox` / `ModelPropertyEditorViewModel` shape ClaudeForge already
uses, minus the effort-level coupling (OpenCode has no `effortLevel` analogue; it has
`temperature` / `top_p` / `steps` per agent instead). A **validation hint** rather than a
hard constraint: warn on a model whose provider is disabled, never block.

> ✅ **Spike S11 is answered — see the Spikes section.** Short version: parse and
> tree-build never touch the network, and `model` builds fine as a `String` node. But
> **`Evaluate()` throws `RefResolutionException`** the moment an instance sets `model`,
> which crashes the shared save path — and the ref target turns out to be a **6,688-value
> `enum`**, so resolving it would reject every custom or self-hosted model. The draft's
> "pre-resolve into the overlay" mitigation is therefore **wrong** and has been replaced
> by **strip the `$ref` at refresh time**. This section's offline-first model-picker
> decision is unaffected — S11 reinforces it.

**Security note.** `provider.<id>.options.apiKey` is a **plaintext API key inside the
config file the editor is editing**. It is already caught by the existing substring pass
in both classifiers (`apikey` / `api_key` / `api-key`), so audit logs and sanitized
backups redact it — but add an explicit parity test for the
`provider.*.options.apiKey` path rather than assuming, and make sure the editor does not
render it in a tooltip or the save-preview diff. `provider.*.env[]` holds env var *names*,
not values, and is not sensitive.

### Credential *status* view — read-only, values never displayed **[decision 7]**

Alongside the exclude-and-redact rules, ship a read-only panel answering *"why is this
provider not working?"* — the single most common OpenCode setup question, and one the user
currently has to answer by opening `auth.json` in a text editor.

Per configured provider, show **presence and origin only**:

| Column | Source | Shown |
|---|---|---|
| Provider | `provider{}` keys + auto-loaded set | id + display name |
| Credential present | `auth.json` · `provider.*.options.apiKey` · each name in `provider.*.env[]` | ✓ / ✗ **only** |
| Origin | which of the three supplied it | `auth.json` / config / `MY_KEY` (the *variable name*) |
| Gated | `enabled_providers` / `disabled_providers` | *enabled* / *disabled by allowlist* / *blocklisted* |

**Hard rules for this view:**
- **Never render a credential value** — not truncated, not masked-with-last-4, not in a
  tooltip, not in the save-preview diff, not in either diagnostics window. Presence is a
  boolean; that is the whole feature.
- **Read-only.** No edit, no add, no "paste your key here". Entering API keys into a form
  is out of scope by design, and it is what would make this app a credential-theft target.
- **Read `auth.json` for existence and key names only** — never load values into memory
  beyond what the presence check needs, and never log the read.
- **Tests:** presence detection across all three origins; a provider whose key exists but
  is blocklisted reports *disabled*, not *missing*; and a guard asserting no credential
  value reaches any UI surface, log sink, or backup.

---

## Debug flags — shared core, per-app extensions **[NEW]**

`DebugFlags` is a static class in the app assembly with 11 flags today:
`--showinstallbanner` · `--windows` / `--macos` / `--linux` (platform emulation) ·
`--showallnew` · `--culture <v>` · `--simulate-update` · `--deep-link <v>` ·
`--debug-help` / `--help-debug`. Separately there are **CLI-bypass tools** (e.g.
`--cleanup-restore-sidecars`) which run a task and exit rather than tweaking GUI state —
`AGENTS.md` keeps those conceptually distinct and so should this plan.

Two apps sharing one shell means the flag surface has to split.

**Shared, moves to `AgentForge.Avalonia.Shell` unchanged in Phase 5:**
`--windows` / `--macos` / `--linux` · `--culture` · `--showallnew` · `--simulate-update` ·
`--deep-link` · `--debug-help`. All are product-neutral.

**Product-parameterized:** `--showinstallbanner` forces the banner; with multiple sections
it should take an optional target (`--showinstallbanner=opencode`), defaulting to all.

**Extension mechanism.** Keep the ergonomics that make the current design pleasant — static
properties, one `Initialize(args)` switch, `ListActive()`, `ResetForTesting()`,
`_deferredWarnings` (because `Initialize` runs *before* Serilog is configured, so it must
never call `Log.*`). Add a registration seam: each app contributes an
`IDebugFlagSet` before `Initialize` runs, and the shell folds those into the same parse
loop, the same `ListActive()` line, the same `--debug-help` output, and the same
`ResetForTesting()`. One parser, one help text, per-app flags.

**New OpenCode flags worth shipping in v1:**

| Flag | Purpose |
|---|---|
| `--simulate-no-opencode` | Force the not-installed path. *(`AGENTS.md`'s own checklist uses `--simulate-no-claude` as its worked example — this is the sibling.)* |
| `--schema-source bundled\|fetched` | Exercise both sides of the Phase 13 opt-in promotion. Two-token, so use the `args[++i]` pattern and validate before assigning. |
| `--opencode-config-dir <path>` | Point at a scratch config root without mutating the environment — makes the `OPENCODE_CONFIG_DIR` gotcha demoable and testable. |
| `--simulate-opencode-version <v>` | Drive the "your OpenCode is out of date" surface without downgrading a real install. |
| `--rules-semantics v1\|v2` | Force the version-gated resolver branch (Spike S9) so both paths are reachable in Gate E. **Both are implemented** (decision 15) — this flag overrides the `ProductVersionProbe` detection. |
| `--writer legacy\|jsonc` | **Shared flag**, both apps (decision 10). Restores the pre-Phase-2 re-serializing writer as a one-release escape hatch. Remove after one clean release. |

Each follows the `AGENTS.md` checklist verbatim: lowercase `case` label, no `Log.*` inside
`Initialize`, added to `ListActive()`, reset in `ResetForTesting()`, documented in the
debug-flags table and (if user-visible) `README.md`, and a test in the per-app
`DebugFlagsTests` covering set / default / — for two-token flags — missing-value,
invalid-value, and value-then-next-flag.

**Extend the `AGENTS.md` checklist itself** when the split lands: "adding a debug flag" now
has two answers depending on whether the flag is shared or per-app, and a future
contributor will get it wrong if the doc still describes one static class.

---

## Deployment — publish scripts, workflows, artifacts **[NEW — draft 10 badly understated this]**

Draft 10 gave this one paragraph in Phase 15. It is materially larger, and it contains one
defect that would have shipped as a **user-visible bug**.

### ⛔ The monorepo decision breaks the update checker

`GithubReleaseChecker.DefaultReleasesLatestUrl` is
`https://api.github.com/repos/JanusMael/ClaudeForge/releases/latest`. That endpoint returns
**the single most recent non-draft release for the whole repository** — it cannot be
filtered by asset name.

With two apps releasing from one repo: ClaudeForge ships `v2026.3.900`, OpenCodeForge then
ships `v2026.1.100`, and ClaudeForge's next update check reads OpenCodeForge's tag. It will
either claim an update exists and link the wrong download, or claim you are current when
you are not. **Draft 10's fix — passing `{ Owner, Repo, AssetPattern, CurrentVersion }` —
does not help**, because `AssetPattern` filters assets *within* a release that was already
chosen wrongly.

**Required:** move to `/repos/{owner}/{repo}/releases` (the list endpoint) and select the
newest release whose **tag matches this app's prefix**. That forces the tag decision below,
and it must land in **Phase 8**, when OpenCodeForge first gets an update check — not
Phase 15.

### Decision required: release and tag strategy

GitHub releases are repo-level, so tags must disambiguate. Three options, and this needs a
ruling before Phase 8:

| Option | Tags | Trade |
|---|---|---|
| **Prefixed tags** (recommended) | `claudeforge/v2026.3.900` · `opencodeforge/v2026.1.100` | Independent cadences, clean filtering, each app's release notes are its own. Costs: existing ClaudeForge tags are unprefixed, so the checker needs a legacy path for old tags. |
| Combined releases | one tag, both apps' artifacts | One release to write. Costs: forces lockstep versioning of two apps with different maturity — OpenCodeForge v0.1 riding a ClaudeForge 2026.3 tag is confusing. |
| Separate repo for OpenCodeForge | — | Cleanest releases, but contradicts the locked monorepo decision. |

### Publish scripts — 5 of 10 hardcode app identity

`src/publish/` holds ten scripts. The orchestration shape is
`publish.ps1` → `Publish-Rid.ps1`, with six thin per-RID wrappers.

| Script | Hardcoding |
|---|---|
| `Publish-Rid.ps1` | `$projectName = "./ClaudeForge/ClaudeForge.csproj"`; the Linux asset list names `claudeforge.desktop`, `claudeforge.svg`, and `../ClaudeForge/Resources/ClaudeForge.svg` |
| `Analyze-XamlClosures.ps1` | `src/ClaudeForge/obj/Release/net10.0` and `ClaudeForge.dll` |
| **`Smoke-PublishedBinary.ps1`** | publish dir, exe name, **and asserts the log contains `"Starting ClaudeForge"`** — for OpenCodeForge that assertion is simply wrong, and it is the post-publish gate |
| `publish.ps1` | orchestrates one app; also the script whose `bin`/`obj` wipe deletes the app's `logs/` folder |
| 6 × `publish-<rid>.ps1` | doc headers only, but each targets one app |

**Approach:** parameterize on an app descriptor (`{ ProjectPath, AssemblyName, IconSvg,
DesktopFile, StartupLogToken }`) rather than duplicating ten scripts. The per-RID wrappers
become `publish-<rid>.ps1 -App <name>`. Duplicating would guarantee drift — these scripts
already encode hard-won knowledge (the trim-warning analyzer, the smoke gate).

### Assets — per app

`assets/linux/claudeforge.desktop` · `assets/linux/linux-setup.sh` (references the binary
name) · `assets/macos/allow-app-to-run.sh` · `src/ClaudeForge/Resources/ClaudeForge.svg`
plus the 256px/64px app icons behind `AppIcon.Instance` / `AppIcon.SmallInstance`.
OpenCodeForge needs its own of each. The Linux `.desktop` `Exec=`/`Icon=` and the setup
script's install paths are name-derived, so they cannot be shared.

### Workflows

| Workflow | Change |
|---|---|
| `release.yml` | `APP_NAME` is already an env var — good, but the publish matrix gains an **app dimension** (6 RIDs × 2 apps = 12 jobs), the download table (6 rows), the install instructions, and the explicit `gh release create` artifact list all become per-app. Plus the tag strategy above. |
| `ci.yml` | Builds the `.slnx`, so new projects come along free — but the RID-qualified restore note applies to both app csprojs, and the smoke gate needs a per-app run. |
| `winget-submit.yml` | Per-app; carries the `40c3ebf` lessons (submit builds only from `packaging/winget/*.yaml`; pin `ManifestVersion`; set `[Console]::OutputEncoding`; duplicate guard; signing precondition). |
| `codeql.yml` | Likely unchanged. |
| `model-catalog-refresh.yml` | Claude-only; leave. |
| `schema-refresh.yml` | Already covered — multi-schema drift (see Schema updates). |

`packaging/` needs a second manifest set (`Bennewitz.Ninja.OpenCodeForge.{yaml,installer,locale}`)
and `Submit-Winget.ps1` parameterized on package identity.

> **Signing note:** the release flow publishes *unsigned* archives and a developer-machine
> script signs, re-uploads in place, then submits — CI cannot sign because the certificate
> is developer-machine-only. That script is **not in the repo** (`scripts/` holds only the
> schema and model-catalog helpers), so it is out-of-band knowledge that now has to cover
> two apps. Worth committing a redacted version, or at minimum documenting the two-app
> procedure in `packaging/winget/README.md` — an unsigned-SHA256 mix-up permanently pins
> the wrong binary in the manifest.

### Versioning

`Directory.Build.props` sets `AssemblyProduct = ClaudeForge` **globally**, and the
auto-version generator stamps `2026.3.<MMDD>.<HHmm>` from build time. Both apps would
otherwise share a product name and a version line. Move `AssemblyProduct` to the per-app
csproj, and decide whether the two apps share the date-derived version (simple, but implies
lockstep) or version independently (needs the prefixed tags above).

---

## Schema updates — in-app and in CI **[NEW]**

### How it works today, and one stale doc

The runtime chain in `SchemaRegistry.GetSchemaAsync` is
**memory cache → bundled resource (+ overlay) → disk cache → HTTPS fetch → empty fallback.**
Bundled deliberately outranks disk and network so hand-curated overlay content always wins.

> ✅ **Fixed in Phase 1 (1h), and it was worse than recorded here.** The order was stated
> backwards in **four** places, not one: `SchemaRegistry`'s class summary, its
> `GetClaudeCodeSettingsNodeAsync` summary, and the `<remarks>` of **two** promotion tests
> (`ModelPropertyPromotionTests`, `OutputStylePropertyPromotionTests`) — where "SchemaRegistry
> prefers the on-disk cache" was given as the *reason for the tests' design*. Only the
> `GetSchemaAsync` method comment was right.
>
> **The deeper problem was that nothing asserted the ordering at all**, so prose was its only
> record. New `SchemaLoadPrecedenceTests` (3 tests) now locks it behaviourally: bundled beats a
> populated disk cache, the overlay-only `model` enum promotion survives the whole chain, and
> — the other direction — a schema with no bundled resource still falls through to disk, so
> "bundled-first" cannot silently become "bundled-only". **Canaried** by guarding the bundled
> branch with `!File.Exists(diskPath)` (making the code match the wrong comment): the two
> precedence tests failed with the authored diagnostics, the fall-through test correctly stayed
> green.

Hand-curated additions live in a sibling `*.overlay.json` applied via RFC 7396 JSON Merge
Patch, fail-open on malformed overlay. `scripts/refresh-schema.ps1` refreshes only the base
file, never the overlay; `.github/workflows/schema-refresh.yml` runs it weekly (Mondays
09:00 UTC) plus `workflow_dispatch`, and opens/updates a `chore/schema-refresh` PR on drift
via `peter-evans/create-pull-request@v8`. `SchemaSnapshotService` diffs against the last
snapshot to drive the "✨ NEW" property chips.

### What OpenCode needs

**Bundled-first is the wrong default for a fast-moving upstream.** Claude's schema comes
from schemastore.org and changes slowly; OpenCode's is first-party and young. Under
bundled-first, a new OpenCode key is invisible until ClaudeForge ships a release.

**Add an explicit, user-visible refresh — and let it apply to both products.**

- **In-app:** a *Check for schema updates* action on the About / Version page (next to the
  existing update check). It fetches each registered schema URL, writes to the disk cache,
  and records a `SchemaProvenance { Source, FetchedUtc, Sha256 }`. A **provenance badge**
  on each product section shows `bundled v… ` or `fetched <date>`.
- **Opt-in promotion.** Fetched schemas outrank bundled **only after the user opts in**
  (per product, persisted in `WindowState`). Default stays bundled-first, so the offline
  and reproducible behaviour ClaudeForge has today is unchanged unless asked for. The
  overlay is merged onto whichever base wins, so hand-curated content is never lost.
- **`SchemaSnapshotService` gets this for free** — a fetched schema with new properties
  lights up the ✨ NEW chips, which is exactly the desired signal.
- **New debug flag** `--schema-source bundled|fetched` for testing both paths, following
  the two-token flag pattern in `DebugFlags.Initialize` (validate, `_deferredWarnings`,
  `ListActive()`, `ResetForTesting()`, `--debug-help`).

### CI changes

`scripts/refresh-schema.ps1` currently hardcodes one URL and one target path. Generalize it
to a table:

| Schema | Upstream | Bundled path | Overlay |
|---|---|---|---|
| Claude Code settings | `json.schemastore.org/claude-code-settings.json` | `Assets/Schemas/claude-code-settings.json` | yes (existing) |
| Claude Desktop config | *(none — `$id` is a bare token)* | hand-maintained | — |
| **OpenCode config** | `opencode.ai/config.json` | `Assets/Schemas/opencode-config.json` | **yes** — for the `@deprecated` keyword normalization and any nav hints |
| **OpenCode TUI** | `opencode.ai/tui.json` | `Assets/Schemas/opencode-tui.json` | yes |

`schema-refresh.yml`'s drift step currently diffs exactly one path. Change it to diff the
whole `Assets/Schemas/` directory and name the changed files in the PR body. Keep the rest
of the design — idempotent, never auto-merge, `dependencies` label excluded from release
notes via `.github/release.yml`.

**One extra CI guard, because OpenCode's schema is young:** a scheduled job that fails
loudly if a refreshed schema's **top-level property count** changes by more than a
threshold, or if any key the nav grouping map references disappears. A silent upstream
restructure would otherwise land the whole page in the `JsonRaw` fallback with no test
failure.

---

## Phased implementation

Each phase ends green: build 0 warnings, full suite passing, trim publish 0 IL warnings.
**ClaudeForge stays shippable at every phase boundary.**

**Docs are part of every phase's definition of done [decision 12].** The repo's whole
methodology rests on `AGENTS.md` being fact-shaped and current — a stale invariant table
actively misleads the next agent, and one entry is *already* wrong today. Per phase, update
whichever of these the change invalidates: `AGENTS.md` §1 invariants and §2 checklists, the
editor sidecar (`src/ClaudeForge/ViewModels/Editors/AGENTS.md`), `PLATFORM.md`,
`LOCALIZATION.md`, `TRIMMING.md`, `README.md`, `CHANGELOG.md`.

Full disposition for all 25 existing docs is in **Guides and docs**. Per-phase debts:

| Phase | Doc change |
|---|---|
| 1 | **Fix the stale `SchemaRegistry` class doc comment** (load order stated backwards — wrong *today*). Fix the `NAV-DEEP-LINKING-PLAN.md` header, still claiming "uncommitted". |
| 2 | New `docs/JSONC-WRITER.md`. Record the `--writer legacy` removal intent so the hatch doesn't become permanent. |
| 3 | **Delete the `ClaudeScope._cache` array-ordering invariant** — it stops existing. Rewrite the `Core/Settings` sidecar's scope section. |
| 4 | Replace the two-product wiring notes with the `ProductSection` model. |
| 5 | **The `AGENTS.md` split** (the big one) + the four sidecars. Nav-page and deep-link checklists for the shell split. Retire the "19 inert tests" note once they're fixed. `LOCALIZATION.md` for the split resx sets. |
| 6, 9 | Extend the compound-editor sidecar — now shared — for the permission model and the new OpenCode editors. |
| 7 | New `docs/OPENCODE-CONFIG.md`. |
| 10, 11 | New `docs/ARTIFACT-RESOLUTION.md`; new `docs/OPENCODE-TEST-PLAN.md`. |
| 5, 8, 9, 11.5 | **Max-6-positional-parameters** convention added to `CONTRIBUTING.md` and the coding conventions in `AGENTS.md`, with the allow-list guard noted so contributors know it shrinks rather than grows. |
| 11.5 | New `docs/DANGER-TAXONOMY.md`. `UI-STYLE-GUIDE.md` gains the four `AppSeverity*` tokens with light/dark values and contrast figures, the no-raw-hex rule, and dual-coding as **normative** rather than advisory. `ESSENTIALS-PAGE.md` updated for the enum-not-string card signature. |
| 13 | **Split the "adding a debug flag" checklist into shared vs per-app** — a contributor following today's single-static-class version will get it wrong. |
| 15 | Per-app `README.md` + screenshot galleries; `SECURITY.md` and `DISCLAIMER.md` for the new surface; `TRIMMING.md` per-app baselines. |

### Phase 0 — Spikes

**Effectively complete — 10 of 11 answered.** Results, with measurements, are in the
**Spikes** section. **Only S5 (OpenCode Desktop) is open**, and it is the one spike that
needs software installed that isn't already here; it gates nothing before Phase 8.

**Everything else is answered but provisional in one specific way** — see the **Deferred
re-checkpoint** section. Nothing derived from *accumulated* state can be trusted yet.

Method, worth repeating:

1. **Schema questions** (S4, S6, S11) — a throwaway `[TestClass]` in
   `tests/ClaudeForge.Core.Tests/Schema/` run against the live
   `https://opencode.ai/{config,tui}.json`, then **deleted**. Phase 0 leaves no code behind.
   Run with `--logger "console;verbosity=detailed"`; passing tests' stdout is hidden otherwise.
2. **Behaviour questions** (S1, S2, S7, S8) — a scratch git repo with a real
   `opencode.json`, layered via `OPENCODE_CONFIG` / `OPENCODE_CONFIG_CONTENT`, read back
   through `opencode debug config`. **Never guess a merge rule; measure it.**
3. **Spec questions** (S2, S8, and most of the product model) — `opencode debug skill`
   exposes the built-in **`customize-opencode`** skill, the vendor's own version-matched
   spec. Read it first; it is better than the web docs.
4. **Version-sensitive questions** (S9, S10) — fetch the docs **at the installed tag**
   (`gh api repos/anomalyco/opencode/contents/<path>?ref=v1.17.9`), never `main`.

### Phase 1 — Rename and neutralize (mechanical, zero behaviour change)

- **Create `AgentForge.Abstractions`** — draft 10 referenced it from Problems 1, 2, and 4
  (Phases 3, 4, and 2) and listed it in the assembly map, but **no phase created it**.
  It must exist by Phase 2, which needs `IConfigWriter` there. Start it empty-ish here;
  each later phase adds its contract (`ConfigScopeId`, `IMergePolicy`, `IPermissionModel`,
  `ProductDescriptor`).
- `ClaudeForge.Core` → `AgentForge.Core`; `ClaudeForge.Sdk` → `AgentForge.Sdk`;
  namespaces likewise.
- `IClaudeConfigClient` → `IAgentConfigClient`; `ClaudeConfigClientCore` → `AgentConfigClientCore`.
- Move Claude-domain accessors (hooks, marketplaces, plugins, model catalog, Claude
  permission *syntax*) into `ClaudeForge.Sdk.Claude`.
- **Break `LayeredEditors.Avalonia.Services` → `ClaudeForge.Sdk` — a five-minute fix, not an
  abstraction exercise.** Re-verified 2026-08-17: the reference exists for exactly one
  `using`, `Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs`, in two files
  (`AvaloniaDialogService.cs`, `IDialogService.cs`). Those types are **generic dialog
  primitives filed in the wrong assembly**, so moving them removes the violation with no new
  indirection. *(Draft 10 implied an interface would be needed; it isn't.)*

  > ⚠ **Move `Sdk/Dialogs/DialogMessage.cs` only — NOT `Sdk/Dialogs/*`.** The directory
  > holds **two files of different natures**, and the coarse instruction would drag SDK
  > domain logic into the abstractions assembly:
  >
  > | File | Contents | Disposition |
  > |---|---|---|
  > | `DialogMessage.cs` | `DialogCategory`, `DialogSegmentKind`, `DialogSegment`, `DialogMessage` — pure primitives, no Claude coupling | **→ `AgentForge.Abstractions`** |
  > | `SdkDialogs.cs` | `SdkDialogs` — factories like `SaveSucceeded(writtenPaths)` that encode *SDK domain knowledge* and wording | **stays in the Sdk** |
  >
  > Measured: `LayeredEditors.Avalonia.Services` uses exactly `DialogCategory`,
  > `DialogMessage`, `DialogSegment`, `DialogSegmentKind` — **all four in `DialogMessage.cs`**
  > — and **never `SdkDialogs`**. So moving the one file is sufficient *and* minimal.
  > Another instance of Risk 7: the directory *name* suggested one thing, the *bodies* said
  > another.
  >
  > **Two doc-comment crefs in `DialogMessage.cs` break on the move** and are
  > compiler-checked, so they surface as build errors rather than silently:
  > `<see cref="Bennewitz.Ninja.ClaudeForge.Sdk"/>` and `<see cref="IClaudeConfigClient"/>`.
  > Rewrite them as prose — an abstractions assembly should not name the SDK.
  >
  > **Blast radius of the namespace change: 14 files** reference `Sdk.Dialogs` (7 app
  > ViewModels/Views, 2 in the Sdk itself, the 2 Services files, 3 test files). All are
  > compile-time `using` updates.

  Note `ClaudeForge.Tests` also references the Services project, so re-point it too.
- **Rename the test projects** to match their subjects.
- **`samples/ClaudeForge.Samples.McpServer`** references `ClaudeForge.Sdk` and breaks on the
  rename. It is also the public-facing example of SDK usage, so retarget it to
  `AgentForge.Sdk` + `ClaudeForge.Sdk.Claude`, rename it, and update its README — an
  out-of-date sample is worse than none. Consider adding an OpenCode sibling sample later;
  not v1.
- **`ClaudeForge.slnx`** gains ~9 new projects and ~10 renames. It is hand-maintained, so
  every phase that adds a project must edit it or the project silently never builds in CI.

Nothing is published to NuGet (no `dotnet pack` / `nuget push` in any workflow; only
`LayeredEditors.Avalonia.Diagnostics` declares a `PackageId`), so there is **no public API
contract to break**. Cheapest this will ever be.

> ### ⛔ Phase 1 is NOT a safe mechanical rename — this was draft 10's worst error
>
> Draft 10 claimed: *"if the suite passes and the diff is exclusively identifier
> substitution, it is correct by construction."* **That is false.** Embedded-resource
> logical names derive from `<RootNamespace>` (set explicitly in every csproj), and several
> sites hardcode that namespace as a **string literal**. The compiler cannot see them, so
> they break at *runtime*, silently:
>
> | Site | What breaks | Caught by tests? *(**measured** 2026-08-17)* |
> |---|---|---|
> | `ResourceHelper.ResourcePrefix` = `"Bennewitz.Ninja.ClaudeForge"` | **Every** bundled resource — schemas, model catalog, enum descriptions | ✅ **Yes — 52 failures** (30 Core, 8 Sdk, 14 app). Draft said "probably"; correct. |
> | `BackupEngine.BundleSchemas` prefix `"…ClaudeForge.Core.Assets.Schemas."` | Archives bundle **zero** schemas → `RestoreEngine` then **silently skips validation** (it treats a missing `Schemas/` folder as "archive predates bundling") | ✅ **Yes — 2 failures.** ⚠ Draft said "**probably not** — the dangerous one". **That was wrong.** |
> | `Strings.Designer.cs` → `new ResourceManager("Bennewitz.Ninja.ClaudeForge.Localization.Strings", …)` | All localized strings fail at runtime | ✅ **Yes — 361 failures.** ⚠ Draft said "maybe not". **Wrong.** |
> | Same, in `ClaudeForge.Avalonia/Localization/Strings.Designer.cs` | Same, second resx set | ✅ **Yes — 40 failures.** ⚠ Same, **wrong**. |
> | `InternalsVisibleTo` — csproj items and one `AssemblyAttribute` | Test seams stop being visible | Yes, at compile time |
>
> #### ⚠ The risk table above was corrected by measurement — read this before trusting it
>
> Each site was broken **one at a time** with a `CANARY.` prefix and the full suite run.
> **All four are caught, loudly.** Two of the draft's four "probably/maybe not" verdicts
> were wrong, and one of those was the entry labelled *"the dangerous one"*.
>
> **This does not make Phase 1 a safe mechanical rename** — the compiler still cannot see
> these dependencies, so the diff looks clean while the app breaks. What it *does* mean is
> that the existing suite is a sufficient net, so the phase is **materially lower-risk than
> the draft assumed**. Do not skip the full-suite run; do not dread it either.
>
> **Also already true today, contrary to the draft:**
> - A test asserting the archive contains a `Schemas/` folder **already exists** —
>   `CreateAsync_NoOnDiskData_ProducesArchiveWithJustManifestAndSchemas`. The draft called
>   this a gap. It is not.
> - Both resx sets are **already** exercised through the real `ResourceManager`, incidentally
>   but thoroughly, by the 401 tests that read localized strings. No new test needed.
>
> **Required additions — revised to what is actually missing:**
> - ✅ **DONE.** Grep for the literal in **strings**, not identifiers. Doc-comment
>   `<see cref="…">` and `.axaml` `x:Class`/`using:` references are compiler- or
>   XAML-compiler-checked and safe; only the four sites above are not.
> - ✅ **DONE.** `ResourceHelper` now **derives** the prefix from
>   `typeof(ResourceHelper).Namespace` and exposes `SchemasPrefix` + `AssetName(sub, file)`.
>   `BundledResource`, `BackupEngine.BundleSchemas`, and five test call sites were
>   re-pointed at it, so **the literal exists in exactly one place and moves with the
>   namespace automatically**. This removes the trap rather than relocating it.
> - ✅ **DONE.** New `ResourceNamePrefixTests` (3 tests) assert the derived prefix still
>   matches real manifest resources — the one thing deriving can't guarantee, since the C#
>   namespace and MSBuild `RootNamespace` are set in different files. **Canaried:** all
>   three fail when `RootNamespace` is changed without the namespace.
> - The two `Strings.Designer.cs` literals are **left hardcoded on purpose.** They are
>   generated by `ResXFileCodeGenerator` from `RootNamespace` + path, so hand-deriving them
>   would be clobbered on the next regeneration — and the correct post-rename value is
>   exactly what the generator would emit. Update the literal with the rename; the 401
>   tests confirm it.

**Risk 2 is corrected accordingly** — see Risks.

**`ClaudeForge.Avalonia`'s fate**, unstated until now: it keeps its name and holds the
Claude-only remainder — `PermissionRuleEducationPanel` (teaches Claude's rule syntax) and
the Claude-specific converters. `GuidedRuleBuilder*` and `PermissionTester*` leave for
the shell in Phase 6 (both permission assemblies were cut). Its **English-only
`Localization/Strings.resx`** is a second, separate resx set that today escapes the
9-locale parity gate; folding it into the Phase 5 split is the moment to decide whether
those keys become shared, per-app, or stay an English-only exception. Decide explicitly —
do not let it drift through the refactor unexamined.

### Phase 2 — `AgentForge.Jsonc` (Problem 4)

Standalone and independently valuable — land it early and let ClaudeForge benefit first.
Switch `ConfigFileLoader` onto the edit-based writer for Claude, prove byte-stability on
an unchanged save, then move on. Doing it here means OpenCode never has a lossy path.

**Ship the legacy writer behind `--writer legacy` for one release [decision 10].** This is
the single highest-consequence code path in the plan — a bug corrupts user config for both
products — so keep a one-command escape hatch. Remove it after one clean release; record
that intent in `AGENTS.md` so it doesn't become permanent.

> ⚠ **This cannot be a plain `DebugFlags` read.** `ConfigFileLoader` lives in
> `AgentForge.Core`, which has no Avalonia and no app reference; `DebugFlags` is a static
> class in the **app** assembly. Core cannot read it, and making Core reference the app
> would invert the layering the whole plan depends on.
>
> Correct shape: define `IConfigWriter` in `AgentForge.Abstractions` with two
> implementations (`JsoncEditWriter`, `LegacySerializingWriter`); `ConfigFileLoader` takes
> one by injection, defaulting to the JSONC writer. The **app** parses `--writer` and
> selects the implementation at composition time. This is the same shape the plan already
> uses for `IMergePolicy` and `IPermissionModel`, so it costs nothing extra — but getting
> it wrong here would be discovered only at compile time, after the writer is built.

> ### ✅ Phase 2 shipped — what the plan got right, and the four things it did not say
>
> The prescribed shape was exactly right and was followed unchanged: `IConfigWriter` in
> `AgentForge.Abstractions`, two implementations, injection into `ConfigFileLoader`, app-side
> flag resolution. Full contract in **[`docs/JSONC-WRITER.md`](./JSONC-WRITER.md)**.
>
> 1. **The read side was the real bug.** See the status header — a commented file loaded as
>    *empty* and the next save overwrote it. The plan said "nothing is at risk today". Wrong.
> 2. **There is exactly ONE save call site** in the product (`AgentConfigClientCore`), so the
>    flag threads through constructors and needs no global mutable state. The plan implied
>    broader plumbing.
> 3. **No new change-tracking was needed** — `SettingsDocument.BaselineRoot` already existed,
>    and baseline-vs-current *is* the change set. Only `OriginalText` had to be added.
>    ⚠ **It must be refreshed after every save**, or every save after the first silently falls
>    back to re-serializing against stale text.
> 4. **Apply changes one at a time, re-parsing between each.** Batching edits from a single
>    parse yields two insertions at the same offset, which `TextEdit.Apply` correctly rejects
>    as overlapping.
>
> **Stamp decision: option 2** (write only when something else changed), so a no-op save is
> genuinely byte-identical. That obsoleted two existing tests which had correctly encoded the
> old unconditional contract; both were updated with in-place notes rather than quietly
> rewritten.
>
> ⚠ **`--writer legacy` and `LegacySerializingWriter` are now a live removal debt**, recorded
> as a hard invariant in `AGENTS.md`. Delete the flag, `SelectedConfigWriter`, and the legacy
> writer together after one clean release.

### Phase 3 — Generalize the scope model (Problem 1) — ✅ **complete**

Two commits, as described. The `ClaudeScope._cache` invariant is gone from `AGENTS.md`,
the root invariant table, `AGENT-ONBOARDING.md`, and the `Core/Settings` sidecar.

> **Four things this section got wrong — worth reading before Phase 4 repeats them.**
>
> 1. **"Everything still compiles" after commit 1 is false.** Six defaulted parameters
>    (`= ConfigScope.User` is not a compile-time constant), sixteen constant patterns
>    across four converters, four `Enum.GetValues<ConfigScope>()`, and five `(int)` casts
>    all break. All loud, all cheap — but it is not a free first step.
>
> 2. **The reference count was 4.5× low** — 314 across 69 files became **1,412 across
>    147**. It did not matter: only **18 files** needed edits, because most references are
>    ordinary uses that compile unchanged against a struct. Count edit sites, not
>    references.
>
> 3. ⚠ **The proposed shape would have shipped a silent bug.**
>    `ConfigScopeId(string Id, int Priority, string DisplayName, bool IsReadOnly)` gives an
>    all-zero `default` whose `Id` is null, and a dozen editors declare
>    `private ConfigScope _lastScope;` with no initialiser, relying on it being `Managed`.
>    That shape was built and run against the full suite: **exactly one failure out of
>    2,792**, and only because commit 1 adds the test that catches it. All 1,391
>    `ClaudeForge.Tests` stayed green. `ConfigScope` is therefore backed by a **single int
>    ordinal**; the richer shape belongs in Phase 4 where a `ProductDescriptor` supplies it.
>
> 4. **`MergeEngine` needed no change.** It never names a scope — it relies purely on
>    entries arriving highest-priority-first. Its Claude-specific *merge rules* are
>    Problem 2, i.e. Phase 4. The real Claude assumptions were three:
>    `LayeredValue.IsManagedLocked`, `AgentConfigClientCore.EditableScopes` (both
>    `== ConfigScope.Managed`, now `Scope.IsReadOnly`), and `ClaudeScope` itself.
>
> **"Delete the statics" was deliberately not done.** `ConfigScope.User` and friends have
> **58 uses in `src/` but 1,074 in tests**, and nothing supplies a `ScopeSet` until
> Phase 4. Deleting them now would be a diff dominated by mechanical test churn, against an
> abstraction that does not exist yet, and Phase 4 would likely churn it again. The statics
> survive as Claude's canonical set; `ConfigScope.All` **is** the ordered scope set that
> `ClaudeScope` and the id-resolution fallback now build themselves from. **Phase 4 owns
> retiring them**, once a product descriptor can supply scopes.
>
> **The invariant was replaced by a test, not just deleted.** The old `AGENTS.md` entry
> said in as many words that a mis-ordered cache "produces the wrong wrapper silently" and
> that there was **no runtime check**. `ClaudeScopeTests.For_ReturnsTheWrapperForTheScopeItWasAsked`
> is now that check, and it fails 33 tests when the mapping is mirrored.
>
> **Not covered by any test:** the scope-chiclet `DataTemplate` in
> `SettingsGroupEditorView.axaml` binds a `ConfigScope` through three converters. It
> compiles and the converters are unit-tested, but the template's runtime binding is only
> provable by running the app.

### Phase 4 — Generalize the product model (Problems 2 + 3) — 🔶 **4a done**

`ProductSection` list replaces the named SDK fields; `ProductDescriptor` replaces
`IsClaudeCode`; `IMergePolicy` replaces the hardcoded rules. Re-register Claude Code and
Claude Desktop through the new path and prove behavioural identity.

**This phase is five separable commits, not one.** Splitting it:

| | Piece | Status |
|---|---|---|
| **4a** | `ProductDescriptor` replaces `AgentConfigClientCore.IsClaudeCode` | ✅ `101554b` |
| **4b** | The **second** `IsClaudeCode` — `RestoreEngine.FindConfigFilesToValidate` returned `(string FilePath, bool IsClaudeCode)` in Core. Draft 10 named only the first. | ✅ `629bca7` |
| **4c** | `IMergePolicy` (Problem 2) | ✅ `4255c12` |
| **4d** | `ProductSection` list replaces `MainWindowViewModel`'s two named SDK fields — **31 `ClaudeDesktopSdk` + 40 `ClaudeCodeSdk` references**, plus `BackupClient`'s public `(includeClaudeCode, includeClaudeDesktop)` constructor and the Backup page's two fixed checkboxes | ✅ **3 commits** — `c9eecfe` (shell lifecycle) · `886494d` (backup product set) · `a56fad7` (per-product checkboxes) |
| **4e** | `ExportManifest` v1 → v2 (booleans → `Clients` list), **with a v1 read path** | ✅ `636fb34` |
| **4f** | Retire the `ConfigScope` statics (deferred from Phase 3) | ✅ `1bbbe4b` — **as a `ScopeLadder` seam; the statics stay, see below** |

4d and 4e are each comparable in size to the whole of Phase 3. **4d took three commits**,
split on where the risk changed: the shell's lifecycle, the backup API + persisted archive
identity, then the view.

**What 4d actually did, and what it left.**

- **`c9eecfe` — the shell's lifecycle.** `ProductSection` (descriptor · nav title · workspace
  display name · export entry path · live client) became the storage; save, validate,
  snapshot, subscribe/unsubscribe, dirty check, export and disposal iterate it. **66
  references in `MainWindowViewModel` → 27.** `WorkspaceDiagnostics.LogPendingChanges` and
  `SaveDialogBuilder.Build` took one parameter per product and now take a sequence of
  (client, display name) pairs — neither ever needed to know how many products exist.
- **`886494d` — the backup API, and the two id vocabularies.** `BackupRequest.Products`
  (`required`, no default — the pair it replaced both defaulted to `true`, so omitting them
  quietly backed up everything) and `BackupClient(engine, products)`. Each Claude client
  passes `[Product]`, the descriptor it already declares.
  **`ProductDescriptor` gained `ArchiveFolder`**, resolving the fact that
  `ProductDescriptor.Id` (`claude-code`) and the archive side (`ClaudeCode` — folder names
  *and* the manifest's persisted `clients` entries) were two vocabularies for the same
  products. That property is now the single source for 7 folder literals in `BackupEngine`
  and the 4-row layout table in `RestoreEngine` — **the duplication 4b explicitly flagged
  and could not fix.**
- **`a56fad7` — the view.** An `ItemsControl` over `SelectableProducts` replaces two fixed
  checkboxes. Labels stay resource-backed (nine locales) via the item view-model, with a
  fallback to the descriptor's display name for an untranslated product.

**Deliberately NOT collapsed — do not "fix" these incidentally:**

- **The navigation tree stays per-product.** Different icons, node ids and descriptions, and
  Claude Code has pages (Essentials, Environment, Effective settings, Permissions, Hooks)
  Claude Desktop has none of. That is two page compositions sharing a header shape, not one
  applied twice. **Phase 5 owns it.**
- **`UpdateScopeContextScopes`** — its Desktop branch carries a documented workaround for a
  binding artefact; the asymmetry is intentional.
- **`BackupEngine`'s two bundling bodies are still hardcoded Claude path-walkers**
  (`ClaudeHome`, `DesktopConfigPath`, profiles, the worktree probe). The product set decides
  *whether* each block runs; it does not describe *what* to collect. That needs a per-product
  footprint description, and `FootprintCategory` is one of the six closed enums **Phase 10**
  owns.
- **`ProductSection.Client` is a `ClaudeConfigClientBase`**, not the neutral core, because
  the editor view-models take `IClaudeConfigClient`. Correct for *this* app; Phase 5
  parameterises it.

> **⚠ 4d's canaries — the breadth record, and a new failure mode.**
>
> | Canary | Result |
> |---|---|
> | Every shell lifecycle loop covers only the FIRST open section | **passed all 2,814 tests** |
> | Transposing the two products behind the named accessors | 6 existing tests fail |
> | Renaming Claude Code's `ArchiveFolder` | 10 tests fail — but every one only *incidentally*, via hardcoded path strings; **nothing asserted the value written into `manifest.clients`** |
> | `BuildClientList` ignoring the request and always listing both | 1 test (the one written for it) |
> | Renaming a bound member in the item view-model | **build error** (`AVLN2000`), thanks to `x:DataType` on the template |
>
> **The one-product canary is the worst hole found in Phase 4**, and the cause is
> structural: *every other test in the suite exercises one product at a time*, so a
> silently one-product save, validate, subscribe, dispose and export looked perfectly
> healthy. Same root cause as 4c's finding that almost every test workspace holds one
> document. **Anything asserting multi-product or multi-scope behaviour has to construct
> two of them deliberately.**
>
> ⚠ **New failure mode introduced by centralising `ArchiveFolder`:** the writer and the
> reader now read the same property, so changing it moves both sides at once and stays
> self-consistent — new archives work perfectly while every archive already on a user's
> disk quietly stops matching. Guarded by three tests that pin the persisted strings and the
> manifest the engine writes.

**What 4f actually did (`1bbbe4b`), and why "retire the statics" was the wrong frame.**

The ladder — not the statics — was the product coupling. `ConfigScope` held it as two
private arrays, `["Managed", "Local", "Project", "User"]` and `[true, false, false, false]`,
inside product-neutral `AgentForge.Core`. **Given a longer ladder that fails silently
rather than loudly:** rungs past the fourth report `IsReadOnly` as `false`, so
policy-locked settings become editable, and their name comes back as the bare ordinal,
breaking the name-keyed brush and tooltip lookups. OpenCode has six rungs and two
read-only ones, so both were already waiting.

`ScopeLadder` (ordered `ScopeRung(Name, IsReadOnly)`, highest-priority first) is now
supplied by the product through `AgentConfigClientCore.Scopes`, **exactly as
`IMergePolicy` is** — 4c's seam shape, reused. `ConfigScope` keeps the int ordinal as its
identity and derives `Ordinal`, `Id`, `DisplayName`, `IsReadOnly`, `Ladder`.

⚠⚠ **The scoping measurement is the transferable part.** "Retire the statics" reads as
1,150 references across 96 files. Counting **edit sites** rather than references — Phase
3's lesson — the real number is **2**: of 15 references in neutral assemblies, 10 are in
`ConfigFileDiscoverer` (Claude-layout code already in neutral Core as a documented
deferral), 3 are doc comments, and 2 are genuine neutral behaviour, both in
`EditableScopes`' hardcoded `[ConfigScope.User]` fallback. The other 33 src references are
Claude code naming Claude's scopes, which is correct. **The statics therefore stay** — the
maintainer's call, and the opposite of the plan's literal wording.

They stay affordably because of one encoding: **`ScopeLadder.Default` IS Claude's ladder,
and a scope built from it stores `null` for its ladder field.** That preserves
`default(ConfigScope) == Managed` under plain struct equality (Phase 3's invariant) and
keeps the four statics equal to the scopes a Claude client hands out. Without it, all
~1,100 test sites naming `ConfigScope.User` would compare unequal to the client's own
`User`, and 4f would have looked like a thousand unrelated failures.

> **⚠ 4f's two traps, both worth carrying forward.**
>
> 1. **Static initialisation order.** `ScopeAt` first decided "am I the default ladder" with
>    `ReferenceEquals(this, Default)` — but `Default`'s own constructor builds its scope
>    list, and the `Default` property is still `null` at that moment. The test was false
>    during exactly the one construction that needed it true, so `ConfigScope.All`'s scopes
>    carried a non-null ladder while `ConfigScope.Managed`, built later, carried null, and
>    they compared **unequal**. Fixed with an explicit `_isDefault` field. **A lazily
>    initialised static that its own constructor consults is a trap wherever it appears.**
> 2. ⚠ **`ClaudeScopeTests` stayed green through that whole failure**, because
>    `ClaudeScope._cache` is built from `ConfigScope.All` and was therefore
>    *self-consistent while wrong*. Third instance of this shape — the other two are
>    4d-2's `ArchiveFolder` (writer and reader read the same property) and 4c's
>    single-document workspaces. **A test whose fixture derives from the thing under test
>    cannot detect that thing moving.**
>
> | Canary | Result |
> |---|---|
> | `ReferenceEquals` instead of `_isDefault` | **2 red** — both pre-existing Phase 3 tests |
> | `EditableScopes` fallback back to `ConfigScope.User` | 2 red, message names the regression |
> | `ConfigScope.Id` no longer lower-cased | **15 red** |
> | Default ladder's rungs reversed | **26 red** |
>
> Ordering and the `Id` contract are well guarded; the neutral-code fallback was **not**
> guarded at all before 4f, which is why `ProductScopeLadderTests` exists.

**What 4e actually did (`636fb34`), and the premise it corrected.**

`ExportManifest` now carries `clients: List<string>` at `CurrentSchemaVersion = 2`, written
from the open-section list — which also retired the last
`ClaudeCodeSdk is not null` / `ClaudeDesktopSdk is not null` pair in the shell, the two lines
4d deliberately left here.

- **`ProductSection` gained a fourth archive-folder site.** Its export paths were the whole
  strings `"ClaudeCode/.claude/settings.json"` and
  `"ClaudeDesktop/claude_desktop_config.json"`. It now takes only the part *inside* the
  product's folder and composes the rest from `ProductDescriptor.ArchiveFolder` — the same
  duplication 4d-2 centralised in `BackupEngine` and `RestoreEngine` but did not reach. Not
  tidiness: a reader takes `clients` as the list of folders to look in, so the manifest and
  the entry paths must agree, and deriving both from one property makes that structural.
- **`ExportManifest.TryRead` maps v1's booleans onto the list** and rejects a non-export
  kind, an unknown future version, and malformed JSON. **It has no caller** — nothing reads
  an export back. Kept anyway because the format is on users' disks, and because without a
  read path the written shape cannot be round-tripped in a test at all. When v1 archives are
  old enough to abandon, delete the two legacy properties and the v1 branch **together**.
- ⚠ **A missing `schemaVersion` does not deserialise to `0`.** The property has an
  initialiser, and System.Text.Json leaves an initialised value untouched when the field is
  absent — so such a manifest arrives claiming to be v2, and a purely version-gated migration
  silently ignores its booleans and reports an export covering nothing. A test written for
  that case failed and is why `TryRead` also falls back when the list came out empty while a
  legacy field was present. **Any future `schemaVersion`-gated migration in this repo has the
  same trap.**
- ✅ **`ExportManifest` has no SDK twin**, unlike `BackupManifest`. The "must update **both**"
  warning recorded under *Incidental finding* above applies to `BackupManifest` and
  `BackupMode`, not to this file — there is exactly one `ExportManifest` type.

> ⚠⚠ **The plan's stated reason for the v1 read path was wrong, and this is the correction.**
> It said these booleans are *"written into exported profiles that other builds read back"*,
> citing profile export/import as a shipped, documented feature. That conflates two unrelated
> artefacts. **Profile export is `ExportedProfile`** — snake_case, `version: "1.0.0"`,
> claudectx-compatible, and carrying **no product booleans at all**. `ExportManifest` is the
> metadata inside a `claude-export-*.zip` written by the Export command, and it is
> **never deserialised anywhere in `src` or `tests`**. So there was no v1 read path to
> preserve and no reader that could break. The migration is worth doing on its own terms —
> two adjacent persisted formats contradicting each other on the same question — not to avert
> data loss.

> **⚠ 4e's coverage finding, and its canaries.**
>
> **Nothing had ever tested this surface.** No test referenced `ExportManifest`,
> `ZipArchiveWriter.SerialiseExportManifest` or `MainWindowViewModel.ExportAsync`, so which
> products an export claimed to cover was unguarded end to end. 16 tests close it:
> `ExportManifestTests` (14) on the DTO and both schema versions, and `ExportArchiveTests`
> (2) on the archive the GUI actually writes — **with two sections open deliberately.**
>
> | Canary | Result |
> |---|---|
> | `Assert.Fail` inside the dispatched lambda | both GUI tests red — the dispatch shape observes assertions |
> | Manifest built from the **first** open section only | both GUI tests red (one on the product list, one on its precondition) |
> | Export entry paths hardcoded to one product's folder | the folder test red, naming the disagreement and listing the archive's real entries |
> | v1 boolean mapping disabled | 4 red; the `false,false` row correctly stayed green |
>
> ⚠ `Assert.AreEqual(2, ExportManifest.CurrentSchemaVersion)` **fails the build** —
> `MSTEST0032`, two compile-time constants folded into a tautology. The version is pinned
> through the serialised bytes instead, which also covers the JSON property name. Same family
> as the `if (false)` canary that would not compile: **use a comparison the compiler cannot
> fold.**

> **⚠ 4a's canary found a hole that applies to every remaining piece — read this before 4b.**
>
> Transposing the two products — pointing Claude Desktop's descriptor at Claude Code's
> schema, which is exactly the mistake a product refactor introduces — **passed all 2,798
> tests.** Desktop's schema selection and its no-hooks behaviour were completely unguarded.
> Desktop configs would have validated against Claude Code's schema, and the Hooks editor
> would have been offered for a product that has none, with a green suite.
>
> Closed by `tests/AgentForge.Core.Tests/Schema/ProductDescriptorSchemaTests.cs`; the
> transposition now fails two of its three tests. **Assume the same hole exists for 4b–4f.
> Canary each by transposing the two products, not merely by running the suite** — "green
> after the refactor" demonstrates almost nothing here, because so little of the suite
> distinguishes the two products in the first place.
>
> **4b confirmed the prediction.** The restore-validation path had the same hole: every
> test that reached it seeded Claude Code data only, so the Desktop routing and two of the
> four archive locations were unguarded. The refactor was green before the guard existed.
> **Keep predicting the hole for 4c–4f.**

**What 4a actually did**, since the plan's one-line description understates it: every use
of the boolean was choosing a schema, so the descriptor names the schema
(`{ Id, DisplayName, SchemaUrl, SchemaFileName }`) rather than the product. The five
ternaries that each restated "Claude Code's URL and file name, else Desktop's" collapse
into two descriptors declared once on `SchemaRegistry`. The `bool` overloads and the two
Claude-named node accessors **stay** as thin wrappers — the GUI and a good number of tests
call them, and retiring them is a separate public-surface change.

The hooks gate in `ClaudeConfigClientBase` was **deleted rather than translated**:
`GetHookEvents` / `GetHookCommandVariants` already return empty for a schema with no hooks
section, which is precisely what Desktop's is, so passing `Product.SchemaFileName`
unconditionally reads the fact instead of hardcoding it.

**What 4b actually did.** The boolean was again only ever a schema file name, so the fix is
the same shape — but the interesting part was the *other* product knowledge in the same
method. `FindConfigFilesToValidate` also hardcoded, per product, which archive-relative
directory to look in, which file names to look for, and whether to recurse: four
`yield return` blocks. Those became a **table** of
`(ProductDescriptor, ArchiveDir, FileNames, Depth)` rows, so adding OpenCode's config
locations is a row rather than a fifth block. Two details worth keeping:

- **Enumeration order is load-bearing enough to preserve deliberately.** Warnings
  accumulate in file order, so the loop goes one file *name* at a time rather than one
  directory at a time, reproducing the old "every `settings.json`, then every
  `settings.local.json`" sequence.
- **The archive layout is still duplicated with `BackupEngine`**, literals on both sides
  (`"ClaudeCode"`, `"ClaudeDesktop"`, `claude-dir`, `profiles`). Centralizing it belongs
  with **4d**. ⚠ Nothing fails loudly if only one side moves: a file that stops being
  found simply stops being validated, and validation is informational. The new test pins
  all four locations by archive-relative path, which is the closest available guard.

**What 4c actually did.** `IMergePolicy` carries exactly two decisions —
`UnionsAt(path, everyValueIsArray)` and `UnionOrder` — because those are the only two the
two products disagree on. Objects deep-merge and non-unioned values go to the
highest-priority scope in both, so the engine keeps them. Claude's list of union-merged
paths moved from a private static on `SettingsWorkspace` into `ClaudeMergePolicy` in
`ClaudeForge.Sdk.Claude`; a client supplies its own through the new
`AgentConfigClientCore.MergePolicy`.

- **The plan said the `arrayPaths` hint "is the seam", and that was right** — the whole
  refactor is that hint becoming a question asked of a policy. What the plan did not
  mention is that the hint's *inference* rule (an undeclared all-array path unions) is
  itself a Claude behaviour that OpenCode must not inherit, which is why the predicate
  takes `everyValueIsArray` instead of just a path.
- **`UnionOrder` is not in the plan's description but is required by its own S1 findings.**
  Claude concatenates highest-priority first, OpenCode lowest-first. Both orders are
  tested now, via a test policy, so the branch is covered before Phase 7 exists to use it.
- **No overload omits the policy** — not on the engine, not on `SettingsWorkspace`. All
  throw on null. A defaulted policy is exactly how a new product silently inherits
  Claude's rules. Cost: 52 call sites across four test projects. That churn is the point.
- `OpenCodeMergePolicy` is deliberately **not** here; Phase 7 owns it, with one test per
  key of S1's table against a client that can exercise it.

> **⚠ 4c's canaries found two more unguarded rules — the same shape as 4a's finding.**
>
> | Canary | Result |
> |---|---|
> | Empty Claude's declared union list **entirely** | **1 failure** — only the new `ClaudeMergePolicyTests`. 2,813 others green. |
> | Flip Claude's `UnionOrder` | **1 failure** — again only the new test. |
> | `UnionsAt` returns `true` unconditionally | **~22 failures** across the SDK and Claude test projects. |
> | Engine stops consulting the policy | **3 failures** — exactly the new seam tests. |
>
> So the "don't union scalars and objects" direction was well covered end-to-end, while
> **which paths union, and in what order, was not covered at all.** The reason is
> structural and worth remembering for 4d–4f: nearly every workspace built in tests holds
> **one document**, and a single scope has nothing to merge with. Multi-scope behaviour is
> therefore under-tested across the board — assume it, and construct two scopes explicitly
> when asserting anything about merging.

### Phase 5 — Extract the shell

Move the product-neutral half of `src/ClaudeForge` into `AgentForge.Avalonia.Shell`:
`MainWindow` chrome, `NavigationNodeViewModel`, `NavDeepPath`, `IDeepNavigable`,
`Status/*`, `SearchViewModel`, `SaveChangesDialog`, `WindowStateService`, `DebugFlags`,
`AppUpdateService`, `UpdateBanner`, `InstallCommandPanel` + `InstallCommandViewModel`,
Add-to-PATH, `EssentialsCardViewModel` + `EssentialsCardKind*` + `EssentialsView`,
`BackupRestoreViewModel`, `AboutEditorViewModel`, `WelcomeView`.
Split `Strings.resx` (Problem 8) in the same phase.

Diagnostics come along too: `AvaloniaDiagnostics` wiring, the F12 `LiveLogWindow` toggle,
and the new Shift+F12 config-activity `LiveTailWindow` — plus the ownerless-helper-window
cleanup in `App.axaml.cs`, extended to cover the second window.

**Parameterize while moving — don't just relocate.** `WindowStateService.StatePath` becomes
per-app (keeping the `=>` property form; the `static readonly` version bypasses the test
sandbox). `AppUpdateService` takes `{ Owner, Repo, AssetPattern, CurrentVersion }`.
`SearchViewModel`'s synthetic-trigger table and header-title const become per-product
inputs. Add-to-PATH takes the binary name. `NavigationNodeIdTests` is **extended** to scan
both apps' trees, not copied.

Claude residue stays in `src/ClaudeForge`: `EssentialsViewModel.BuildCards`,
`AgentsSkills*`, `Memory*`, `Profiles*`, `Environment*`, `Editors/*`, `Adapters/*`,
`NavigationTreeBuilder`, `ModelSuggestion*`, `Catalog/*`.

**Un-inert the 19 headless tests here [decision 9].** ✅ **DONE, ahead of the extraction —
`a0895f2` (un-inert) · `d8389e6` + `cf49c6c` (the two defects it exposed) · `5f53c4f` (a
misattribution it corrected).** `NavigationTreeWelcomeNodeTests` (9), `ReloadHardeningTests`
(7), and `TransactionalReloadTests` (3) all used `return Session.Dispatch(async …)`, which
binds `Dispatch<T>(Func<T>)` with `T = Task` and yields `Task<Task>`; MSTest awaited only the
outer task, so no assertion could fail. All 19 now return a value from the lambda and were
canaried with a deliberate `Assert.Fail`.

> **The population was exactly 19 — the count was right.** `SampleHeadlessTests` also calls
> `Session.Dispatch` twice but with a **non-async** lambda, which binds `Dispatch(Action, ct)`
> and is correctly awaited. The trap is specific to `async` lambdas returning `Task`.
>
> **15 passed. The 4 that failed were two pre-existing defects, neither a Phase 1–4
> regression** — both predate Phase 1, because these tests were inert from the day they were
> written and so never verified anything.
>
> | Defect | Root cause | Fix |
> |---|---|---|
> | Transactional reload never held (3 tests) | `ConfigFileLoader.LoadAsync` catches `JsonException` and returns an empty `JsonObject`, so PHASE 1's try/catch never fires and the "no throw points past here" swap installs a placeholder. The next save writes that emptiness over the user's real settings — **the loader's own comment predicted exactly this.** | `SettingsDocument.LoadFailure` + `SettingsWorkspace.FailedDocuments`, consulted before PHASE 2 (`d8389e6`) |
> | Use-after-dispose on concurrent reload (1 test) | One reload reaches `ClaudeCodeSdk?.Dispose()` while another is inside `BuildNavigationTreeAsync`. Reachable in the app: `OpenProjectAsync` sets `IsLoading` but never **checks** it, and awaits a folder dialog first. | Serialise overlapping calls inside `LoadAllWorkspacesAsync` (`cf49c6c`) |
>
> ⚠⚠ **A pinned contract contradicted the one these tests asserted, and only one side was
> enforceable.** `ConfigFileLoaderTests:95` pins the opposite guarantee — a corrupt file must
> degrade to an empty-root document rather than crash. The conflict turned out to be **only
> about throwing**, so neither side had to lose: the loader still never throws, and the flag
> makes the failure visible to the one caller that must be transactional.
>
> ⚠⚠ **Two comments asserted guarantees that did not exist**, and both are why these defects
> survived. `_reloadPending`'s comment said it "prevents concurrent calls to
> `LoadAllWorkspacesAsync`" — it guards `ReloadCoreAsync`, one of three callers. And the
> concurrency test named that field while calling a method it does not protect. **A test that
> cannot fail plus a comment that overstates its guard is how a race lives for years.**
>
> **Both fixes are canaried.** Bypassing the serialisation restores `ObjectDisposedException`
> in 2 tests; disabling the parse-failure bail turns 3 red. Two rejected alternatives are
> recorded in `LoadAllWorkspacesAsync`'s remarks, because both are the tidier-looking choice:
> **coalescing** overlapping loads into one shared `Task` would mean `OpenProjectAsync` (which
> mutates `ProjectRoot` first) silently never opens the new project, and a **non-reentrant
> lock** would deadlock if the load path re-enters — which it can, via the notifications
> `_suppressProfileChangeReload` exists to suppress.

**Most dangerous phase.** Extract in slices (status → search → deep-link → nav → save),
not one move.

**Slice progress.**

| Slice | Status |
|---|---|
| **status** | ✅ `5fa6f54` — `AgentForge.Avalonia.Shell` created; `StatusController` + `StatusKind` moved |
| search | ⬜ |
| deep-link | ⬜ |
| nav | ⬜ — the hard one; see the two-page-compositions note below |
| save | ⬜ |

**What slice 1 established, and two things it corrected.**

Status went first because it is the cleanest slice available, which was confirmed rather
than assumed: `StatusController` imports only `Avalonia.Threading` and
`CommunityToolkit.Mvvm` and has **zero `Strings.` references**, so it carries none of the
`Strings.resx` split Problem 8 still owes. `MainWindow.axaml` mentions the types only in
comments — no markup type references at all.

`AssemblyLayeringTests` covers the new assembly **automatically**: it works from the
`AgentForge` / `ClaudeForge` name prefixes and checks both the ProjectReference graph and
compiled assembly references, so no registration is needed for the slices that follow.
Canaried — pointing the shell at `ClaudeForge.Sdk.Claude` fails
`SharedProjectsNeverDeclareAProductReference`.

> ⚠ **Five empty directories under `src/` read as an extraction already half-done.**
> `ClaudeForge.Adapters`, `ClaudeForge.Localization`, `ClaudeForge.Avalonia.Localization`,
> `ClaudeForge.Avalonia.ViewModels` and `ClaudeForge.Editors.ViewModels` held no files and
> were untracked — git does not track empty directories, so they survived the
> MainWindow-extract discarded un-merged on 2026-08-06. Removed in `5fa6f54`. **Anything
> resembling partial progress in this phase should be checked against `git ls-files` before
> being believed.**
>
> ⚠ **Expect `BuildFilePathIntegrityTests` to fail on most slices, and treat that as the
> guard working.** Slice 1 turned the suite red because root `AGENTS.md` cited
> `src/ClaudeForge/ViewModels/Status/StatusController.cs`. Prose falsified by a move is the
> failure mode this phase produces repeatedly, and the compiler cannot see it — that test is
> the only thing that can.

### Phase 6 — Shared permission *vocabulary* (Problem 5) — **much smaller than drafts 10–11**

Not an extraction. Define `PermissionOutcome` and a generic
`Decision<TRule>(Outcome, MatchedRule, MatchedScope, Explanation)` in
`AgentForge.Abstractions`, move the three narrow UI interfaces, and share the tester/builder
**view templates**. Everything else stays Claude-side; OpenCode gets its own implementation
in Phase 9.

**Both permission assemblies are cut** — pass 11 confirmed even the three "narrow
interfaces" are Claude-shaped (`AddAllow`/`AddDeny`/`AddAsk` over `PermissionRule`). What
survives: the outcome vocabulary → `AgentForge.Abstractions`; `IPermissionPathPicker` →
`LayeredEditors.Avalonia.Services` (it's a file picker, not a permissions type); the AXAML
templates → the shell. Everything else stays in `ClaudeForge.Avalonia` / `ClaudeForge.Sdk.Claude`.

This phase is now **hours, not days** — which is the right outcome. Do not manufacture an
abstraction to justify the phase.

**Precision on the "tests pass unchanged" proof** — draft 9 overstated it.
`PermissionDecision` (`MatchedScope`) and `PermissionResolver` both reference
`ConfigScope`, so **Phase 3 has already touched these tests** by the time Phase 6 runs.
The honest claim: after Phase 3, the permission tests are green against the generalized
scope model; **Phase 6 must then change nothing but namespaces**. A behavioural diff at
Phase 6 means the extraction was unfaithful; a behavioural diff at Phase 3 means the scope
generalization was. Keeping those two attributions separate is the whole reason the phases
are ordered this way — do not merge them.

### Phase 7 — `OpenCode.Sdk`

- Bundle both schemas under `AgentForge.Core/Assets/Schemas/` (+ an overlay each) and
  register the live URLs. The refresh tooling and in-app update path land in Phase 13 —
  here, just make bundled-first work.

  > ⚠ **This silently changes ClaudeForge's backups.** `BackupEngine.BundleSchemas` copies
  > **every** embedded resource under `Assets/Schemas/` into each archive, and
  > `RestoreEngine` parses **every** file it finds in the archive's `Schemas/` folder.
  > Dropping two OpenCode schemas there therefore alters ClaudeForge archive contents and
  > its restore-time validation path — from a phase that is nominally OpenCode-only.
  > Decide deliberately: either give OpenCode its own resource folder and make
  > `BundleSchemas` product-aware, or accept the shared folder and confirm `RestoreEngine`
  > tolerates schemas irrelevant to the archive it is validating. **Do not discover this
  > from a user's failed restore.** Add a test asserting archive contents for each product.
- **Root `$ref` — confirmed broken, fix is known (Spike S4, answered).** `BuildTopLevel`
  returns **0** nodes for `config.json` today. Teach `SchemaTreeBuilder.GetPropertySubschemas`
  to fall back to a single-subschema `$ref` when no `properties` keyword exists — the
  resolved target is already sitting in `KeywordData.Subschemas`. **Follow `$ref` only in
  the absence of `properties`**, never unconditionally. Regression test: 36 top-level
  nodes for `config.json`, 13 for `tui.json`.
- **Strip the external models.dev `$ref` from the bundled copy (Spike S11, answered).**
  Four sites. Leaving it in makes `Evaluate()` throw `RefResolutionException` through
  `ValidateWorkspaceAsync` → `SaveAsync` for any config that sets `model`; resolving it
  instead imposes a 6,688-entry allowlist that rejects custom models. Stripping leaves
  `"type": "string"` intact, which is the behaviour we want. Test that no
  `"$ref": "http…"` survives into the bundled schema.
- `OpenCodeClient` + `OpenCodeTuiClient : AgentConfigClientCore`.
- `OpenCodeScopeSet` — **precedence measured in S1, not assumed**: `global`
  (`~/.config/opencode/opencode.json`, and note OpenCode writes **`opencode.jsonc`** there
  by default) → `custom` (`$OPENCODE_CONFIG`) → `project` → `inline`
  (`$OPENCODE_CONFIG_CONTENT`, read-only) → `managed` (read-only). Honour
  `OPENCODE_CONFIG_DIR`.
  - ⚠ **`project` is three filenames plus an upward walk**, not one file:
    `./opencode.json`, `./opencode.jsonc`, or `.opencode/opencode.json`, resolved by
    walking **up from the cwd to the worktree root**. Getting this wrong shows the user the
    wrong authoritative file.
  - Also honour `OPENCODE_DISABLE_PROJECT_CONFIG=1` — it removes the project layer, and the
    Effective view must reflect that or it will disagree with the running agent.
- `OpenCodeMergePolicy` — ⛔ **per-key, not a single array rule (S1).** `instructions` and
  `plugin` union (lowest layer first); `disabled_providers`, `enabled_providers`,
  `skills.paths`, `skills.urls`, `experimental.primary_tools` replace; objects deep-merge;
  scalars last-wins. One test per key.
  - The interface it implements **already exists** (4c): `UnionsAt(path, everyValueIsArray)`
    returns true only for the two union keys — **do not infer from the values**, or a
    replace-key silently unions and resurrects a provider the user disabled — and
    `UnionOrder` is `LowestPriorityFirst`. Both engine branches are already tested via a
    test policy, so this is implementing a covered seam, not proving a new one.
- `OpenCodePermissionModel` (parse/format the nested map; glob matcher) — ⛔ **key order is
  semantically load-bearing and the LAST match wins.** Never re-serialize the map in a
  different order. See the merge-inversion hazard under S1.

### Phase 8 — `OpenCodeForge` app: settings + effective view — **first runnable build**

Thin shell registering the OpenCode + OpenCode TUI sections. New icon, `AssemblyProduct`,
winget identity, **and its own `WindowStateService.StatePath`** — not under `~/.claude/`.

**Must also call the equivalent of `Program.WireWrapperLocalization`.** `LayeredEditors.Avalonia`'s
`WrapperStrings` fallback is hardcoded English **Claude** text (*"…not in official Claude
documentation"*); without wiring, OpenCodeForge renders a Claude-branded tooltip on its own
🕵 badge. Add a test asserting no Claude-branded fallback string is reachable from
OpenCodeForge — this is invisible until a user hovers.

Also lands the detection / banner / update trio (see that section): OpenCode + Desktop
install probes honouring `OPENCODE_CONFIG_DIR` / `OPENCODE_DATA_DIR`, per-platform
`InstallCommandViewModel` factories (Spike S10), `TryGetOpenCodeVersionAsync`, and
Add-to-PATH parameterized on the binary name.

**The update checker must be fixed here, not at Phase 15.** `GithubReleaseChecker` hits
`/releases/latest`, which returns the newest release for the **whole repo** — so in a
monorepo each app would read the other's tag. Move it to the `/releases` list endpoint,
filter by this app's **tag prefix**, and settle the tag strategy (see Deployment). Shipping
Phase 8 without this means OpenCodeForge's very first release makes ClaudeForge's update
banner wrong. Tests: each app resolves its own latest across a mixed release list, and old
unprefixed ClaudeForge tags still resolve.

Per-product search wiring too: `BuildSchemaSearchProviders` looping over sections, and an
OpenCode synthetic-trigger table (including the gotcha phrasings).

**Good news for once: `NavigationTreeBuilder` is more reusable than earlier drafts implied.**
`BuildGroups(IReadOnlyList<SchemaNode>, …)` buckets nodes by a lookup dictionary and orders
by a list — the **mechanism is already product-neutral**; only `PropertyToGroup` and
`GroupOrder` are Claude data. So it moves to the shell and takes the grouping table as a
parameter. No duplication, no generalization work beyond lifting two static fields into
arguments.

Navigation grouping for the 36 core keys:

| Group | Keys |
|---|---|
| General | `shell` · `username` · `logLevel` · `snapshot` · `autoupdate` · `share` |
| Model & Agents | `model` · `small_model` · `default_agent` · `subagent_depth` · `agent` · `provider` · `disabled_providers` · `enabled_providers` |
| Permissions | `permission` · `tools` |
| MCP | `mcp` |
| Commands & Skills | `command` · `skills` · `instructions` · `references` |
| Tooling | `formatter` · `lsp` · `watcher` · `plugin` |
| Context | `compaction` · `tool_output` · `attachment` |
| Server | `server` · `enterprise` |
| Advanced | `experimental` · `$schema` |
| *(deprecated — hidden unless set)* | `mode` · `autoshare` · `reference` · `layout` |

And for the **TUI section's 13 keys** — omitted from draft 9, which registered the section
without saying how it was organised:

| Group | Keys |
|---|---|
| Appearance | `theme` · `diff_style` · `cursor` |
| Input | `keybinds` · `leader_timeout` · `mouse` |
| Scrolling | `scroll_speed` · `scroll_acceleration` |
| Notifications | `attention` · `prompt` |
| Plugins | `plugin` · `plugin_enabled` |
| Advanced | `$schema` |

Those four config keys carry `@deprecated` in their schema **description**, but
`IEditorSchema.IsDeprecated` reads a `deprecated` **keyword**. `SchemaTreeBuilder` needs a
small rule to recognize the `@deprecated` convention, or they render as ordinary settings.

### Phase 9 — OpenCode compound editors

**Creates `OpenCode.Avalonia`** — listed in the assembly map and the test-project list since
draft 6, but no phase created it. This is its home: the OpenCode-specific editors and views,
including the keybinds editor.

- **`mcp`** — union on `type`. `McpLocalConfig` (`command[]` · `cwd` · `environment` ·
  `enabled` · `timeout`) vs `McpRemoteConfig` (`url` · `headers` · `oauth` · `enabled` ·
  `timeout`), where `oauth` is `McpOAuthConfig | false`. Copy the shape of
  `MarketplaceListEditorViewModel` (682 lines, 8 source variants), **not**
  `McpServersEditorViewModel` — Claude's transport model differs.

  > ✅ **Verified, and two of its behaviours are the reason to copy it rather than start
  > fresh.** It (a) **preserves per-variant non-discriminator fields across a variant
  > switch**, so flipping a server local↔remote doesn't silently destroy the fields the
  > other arm didn't use, and (b) **echoes an unknown variant back unchanged** rather than
  > dropping it — essential when the upstream schema adds a variant before the editor knows
  > about it. Both apply directly to `mcp` (local↔remote) and to `plugin[]`
  > (string↔`[name, options]`). Reproduce both, and test both.
- **`permission`** — a purpose-built **two-level tool × pattern grid** plus the bare-string
  ("apply to all tools") mode. The shared **tester** from Phase 6 binds over
  `OpenCodePermissionModel`; the **guided builder does not** — Claude's is a rule-syntax
  generator and stays Claude-side (see Problem 5). Budget this as a real editor, not a
  binding exercise.
- **`agent{}`** — object keyed by agent name with 7 named built-ins plus arbitrary keys.
  15 fields, including a **nested `PermissionConfig`** that binds the shared permission
  editor from Phase 6 as a child, and a `color` field that is a hex-or-theme-name union.
  The effective view must show *global permission → agent override*.
- **`command{}`** — object keyed by command name; `template` required, plus `description` ·
  `agent` · `model` · `variant` · `subtask`.
- **`plugin[]`** — `string | [string, object]` discriminated union (the schema has the
  tuple form even though the docs say otherwise). TUI section additionally gets its own
  `plugin[]` **and** `plugin_enabled{}` name→bool toggle map.
- **`formatter`** · **`lsp`** — `bool | object-of-configs`. A mode toggle (*off / on /
  configured*) over a per-language map (`disabled` · `command[]` · `environment` ·
  `extensions`). **Missed by draft 9**; without this they render as raw JSON.
- **`autoupdate`** — `true | false | "notify"`. Small, but it is Essentials card #14 and a
  three-state union is not a checkbox. Shares the tri-state control with that card.
- **`keybinds`** (TUI section) — **in v1, purpose-built** [decision 2]: a searchable action
  list over the 184 actions with a key-capture control, lazily realized, **not** 184 generic
  wrappers and **not** the raw-JSON fallback. This is the largest single new editor in the
  plan and the main reason people edit `tui.json` at all. Each action's value is an `anyOf`
  over `false | "none" | string | {name,ctrl,shift,meta,super,hyper} | …`, so the capture
  control writes the object form and the editor renders the others read-through. Gate the
  realized-row count with the `[PropView.Realized]` trace; Spike S6 measures it first.
- **`theme`** (TUI section) — schema declares it a bare `string` with no enum, so offer a
  picker sourced from installed themes on disk (`~/.config/opencode/themes/*.json`) plus
  free text. Same shape as the existing free-form-with-suggestions `model` field.

All compound editors must follow `src/ClaudeForge/ViewModels/Editors/AGENTS.md` — force-fire
`MarkModified()`, `_isLoading` guard, `ToJsonValue()` returning `null` when empty,
transient-field filtering — plus the
`EditingXxxAfterLoad_FiresIsModifiedPropertyChanged` / `RemovingXxxAfterLoad_…` pair.

### Phase 10 — `AgentForge.Artifacts` resolution engine (Problem 6)

Extract the directory walk behind `IArtifactSource` + a resolver that returns
*winner + shadowed*. Claude re-registers as convention-only sources; its existing Memory /
Agents-&-Skills tests must pass unchanged, which is the proof the extraction was faithful.
Reuse `LayeredValue`'s shadowing vocabulary so the existing scope-badge UI binds with no
new controls.

> ⚠ **Bigger than draft 10's "extract `UserMemoryService`'s directory walk".** The artifact
> surface is **five services, four of them `static class`** — `UserMemoryService`,
> `EditableMemoryService`, `MemoryArtifactDeleter`, `MemoryFileWriter` (static) and
> `FootprintService` (instance) — each resolving roots internally from
> `PlatformPaths.ClaudeHome`, with ~10 call sites outside the Memory folder. So this phase
> is a **static→instance conversion with root injection**, not a single extraction. It is
> also where profile-readiness rules 2 and 3 actually get paid for: a static service with a
> baked-in root is precisely what makes profiles (and `OPENCODE_CONFIG_DIR`) impossible
> later. Convert them all, or the seam is fiction.

### Phase 11 — OpenCode Agents / Commands / Skills / Rules / Plugins page

Reuses `AgentsSkillsEditorViewModel` (1,692 lines) heavily — same tabbed shape,
front-matter card, raw-YAML escape hatch, rendered markdown body, filter/deep-link/
`IDeepNavigable` machinery.

> ✅ **Verified — and this is the first large reuse claim in the plan that held up.**
> Product coupling is **23 references across 1,692 lines (~1.4%)**, concentrated in a thin,
> well-defined data-access seam: `UserMemoryCategory.Subagent`/`.SlashCommand`/`.Skill`
> (12, the three tabs), `EditableMemoryService.Snapshot`/`.ReadAsync`/`.LoadDescription`
> (4), `EditableMemoryScope.Plugin` (2), `MemoryFileWriter.WriteAsync` (1). Everything else —
> filtering, grouping, shadow/plugin row handling, deep-link capture and restore, the
> front-matter editor, the markdown renderer — is product-neutral UI logic.
>
> **The dependency is on Phase 10 doing its job.** Once `UserMemoryCategory`,
> `EditableMemoryScope`, and the four static services become per-product data and instances,
> this VM transfers with a per-product category set and a service injection. If Phase 10 is
> skipped or half-done, this claim collapses — which is the argument for doing the
> static→instance conversion properly rather than shimming it. Two tabs are added: **Rules** (Problem 7) and **Plugins**
(coverage check) — the latter read-only, listing each plugin file with the events it
subscribes to, from a shallow static scan of exported hook names. No execution.

**Sources per artifact kind**, all resolved through Phase 10's engine:

| Kind | Convention sources | Config-declared sources | Inline JSON |
|---|---|---|---|
| Agents | `~/.config/opencode/agent(s)/*.md` · `.opencode/agent(s)/*.md` | — | `Config.agent{}` — incl. 7 overridable built-ins (`plan` `build` `general` `explore` `title` `summary` `compaction`) |
| Commands | `~/.config/opencode/command(s)/*.md` · `.opencode/command(s)/*.md` | — | `Config.command{}` (`template` required · `description` · `agent` · `model` · `variant` · `subtask`) |
| Skills | `~/.config/opencode/skills/<n>/SKILL.md` · `.opencode/skills/<n>/SKILL.md` · **`~/.claude/skills/`** · **`.claude/skills/`** · **`~/.agents/skills/`**; project paths traverse **upward to the git worktree root** | `skills.paths[]` · `skills.urls[]` *(listed, not fetched in v1)* | — |
| Rules | `AGENTS.md` traversing upward · `~/.config/opencode/AGENTS.md` · `~/.claude/CLAUDE.md` fallback | `instructions[]` — globs + remote URLs | — |
| **Plugins** | `~/.config/opencode/plugins/*.{ts,js}` · `.opencode/plugins/*.{ts,js}` | — | `Config.plugin[]` (npm specs) · TUI `plugin[]` + `plugin_enabled{}` |

Front-matter per kind:
- **Agents** — `description` · `mode` (`primary`\|`subagent`\|`all`) · `model` · `variant` ·
  `temperature` · `top_p` · `prompt` · `permission` · `disable` · `hidden` · `color` · `steps`
- **Commands** — `description` · `agent` · `model` · `subtask`; body supports
  `$ARGUMENTS`, `$1..$n`, `` !`cmd` ``, `@file`
- **Skills** — `name` (`^[a-z0-9]+(-[a-z0-9]+)*$`, must match the directory) ·
  `description` · `license` · `compatibility` · `metadata`

**Three things this page must do that ClaudeForge's does not:**

1. **Show shadowing.** An agent named `build` may be defined as a built-in, in global JSON,
   in a global markdown file, in project JSON, and in a project markdown file
   simultaneously. Show the winner and let the user expand the chain — same affordance the
   settings editor already uses for scopes. Which form wins (JSON vs markdown) is **Spike S7**.
2. **Show rule resolution, not a file list.** First-match-wins + glob expansion + ordering
   means the file list is not the answer. Render the actual load order, mark shadowed files,
   and surface the two gotchas from Problem 7 (`OPENCODE_CONFIG_DIR` global `AGENTS.md`
   silently ignored; `@file` not auto-expanded).
3. **Explain remote and reference sources without fetching them** — `skills.urls[]`,
   remote `instructions[]`, and `references{}` git entries are listed with their origin and
   a "not fetched by this tool" note.

**Cross-tool overlap is a first-class feature.** OpenCode genuinely reads
`~/.claude/skills/`, `.claude/skills/`, `~/.agents/skills/`, and falls back to
`~/.claude/CLAUDE.md`. `EditableMemoryScope` is one of the six product-varying closed enums
(see Risk 7) — `Plugin` means `~/.claude/plugins/` specifically — so it becomes a
per-product scope set rather than gaining a value. Badge shared rows
*"also visible to Claude Code"* — editing one artifact affects both tools, and users need
to know that before they edit. Only possible because both products live in one codebase.

**`references{}` gets a small dedicated editor** — named entries that are a bare string,
a git ref (`repository` + optional `branch`), or a local path, each with `description` and
`hidden`. Closest in-tree template is `MarketplaceListEditorViewModel`.

### Phase 11.5 — Danger indication, systematised

Promote danger from an Essentials-only concept to a schema-level annotation on
`IEditorSchema.Metadata`, driven by a per-product bundled danger table. Lands the four
missing surfaces (settings tree, effective view, search hits, **save-preview**), makes the
predicate scope-aware, and replaces the hardcoded severity hexes with
`AppSeverity{Critical|Caution|Info}Brush` tokens so both apps theme correctly.

**Benefits ClaudeForge immediately** — its own dangerous keys (`sandbox.enabled`,
`enableAllProjectMcpServers`, `permissions.disableBypassPermissionsMode`) currently show no
severity anywhere except the Essentials page. Write the Claude danger table in the same
pass; it is the proof the mechanism is genuinely product-neutral.

**Lands all five enforcement guards** (see Making the danger tenant stricter): non-nullable
severity · coverage test · save-preview assertion · dual-coding scanner rule · the
no-raw-hex build tripwire. Model the tripwire on `GuardUnusedResxKeys` in
`Directory.Build.targets` — `AfterTargets="Build"`, skip design-time builds, opt-out
property, and **not run during publish** (inline `RoslynCodeTaskFactory` tasks fail
intermittently under concurrent builds; that lesson is already recorded there).

**Migrates ClaudeForge's four hardcoded severity hexes to tokens in this phase** —
`#D32F2F` / `#F4B400` / `#1976D2` in `EssentialsViewModel.BuildCards` and the `#9E9E9E`
parse-failure fallback in `EssentialsCardViewModel`. Both apps end on
`AppSeverity{Critical,Caution,Info,Neutral}Brush`; neither keeps a literal. Changing
`EssentialsCardViewModel` to take a **severity enum instead of a colour string** deletes
the `Color.TryParse` call and its fallback path entirely — net less code.

Ships `docs/DANGER-TAXONOMY.md`.

Ordering note: this sits after Phase 11 because the OpenCode table references artifact and
rule resolution (`instructions[]` remote URLs, `skills.urls[]`), but the *mechanism* only
depends on Phase 5, so it can be pulled earlier if the shell extraction lands cleanly.

### Phase 12 — OpenCode Essentials page

The card infrastructure already moved to the shell in Phase 5. Here: add the two new card
kinds (derived/read-only, tri-state enum) and write `OpenCodeEssentialsViewModel.BuildCards`
for the 17 cards above. Depends on Phase 11 for cards #16/#17, which report resolver state.

Re-assert the `IsLoading`-must-not-span-`await` guard here
(`IntValueWrite_NotSuppressed_WhileReadIsInAsyncPhase`) — that bug class is not
Claude-specific and will recur in any new Essentials VM.

### Phase 13 — Schema refresh: in-app + CI

Generalize `scripts/refresh-schema.ps1` to the four-schema table; widen
`schema-refresh.yml`'s drift check to all of `Assets/Schemas/`; add the property-count /
missing-key CI guard. Add the in-app *Check for schema updates* action, `SchemaProvenance`,
the per-product opt-in promotion, the provenance badge, and the `--schema-source` debug
flag. ~~**Fix the stale `SchemaRegistry` class doc comment**~~ — ✅ **done in Phase 1 (1h)**,
along with three more instances of the same inverted claim, plus a new
`SchemaLoadPrecedenceTests` guard. See the *Schema updates* section. **This phase must not
regress it:** the per-product opt-in promotion deliberately lets a *fetched* schema outrank
bundled, so it changes the very ordering those tests lock. Update them as part of the
promotion work rather than deleting them — they are what will tell you the opt-in wired the
precedence the way you meant.

### Phase 14 — Backup / Restore + data footprint

- **Backup** archives `~/.config/opencode/` — ⛔ **but not verbatim.**

  > ⛔ **A naïve archive of that directory is ~60 MB of regenerable dependencies.**
  > Measured: OpenCode materializes `node_modules/` (~60 MB, 24 packages),
  > `package.json`, and `package-lock.json` there to resolve plugin imports, alongside the
  > `opencode.jsonc` it auto-creates. **Exclude `node_modules/`, `package-lock.json`, and
  > `bun.lock`** — OpenCode maintains a `.gitignore` in that directory listing exactly
  > those, so honouring it is both correct and self-maintaining. Same list drives the
  > Phase 14 footprint page's largest prune candidate.
  >
  > ⚠ **And `~/.config/opencode/` is no longer the whole story.** State lives in
  > `~/.local/share/opencode/opencode.db` (SQLite + `-wal`/`-shm`), with additional roots at
  > `~/.local/state/opencode/` and `~/.cache/opencode/`. Decide explicitly what a backup
  > means for a live SQLite database — a naïve file copy of a `-wal` database can restore
  > corrupt. **Re-checkpoint item 2 gates this.**

  > ⚠ **The archive format embeds product names in entry paths.** `BackupEngine` writes
  > entries as `"ClaudeCode/claude-dir/{name}"` — so the archive's internal layout is
  > product-specific, and `RestoreEngine` reads it back by those prefixes. N products means:
  > a per-product prefix supplied by the product descriptor; `RestoreEngine` dispatching on
  > it; and **existing ClaudeForge archives with `ClaudeCode/` paths must keep restoring**.
  > Combined with the `ExportManifest` boolean fields (Problem 3), **both persisted formats
  > change** — archive layout *and* manifest. Version them together, and add restore tests
  > against a **pre-change archive fixture** committed to the repo; a format change that only
  > round-trips with itself is how backup tools lose people's data.
  >
  > ⚠ **Half of that is now spent: 4e (`636fb34`) already took `ExportManifest` to schema v2.**
  > So Phase 10 changes the archive layout against a manifest that is *already* at v2 — bump
  > it again and extend `ExportManifest.TryRead` in the same commit. 4e's own tests cover both
  > of its versions but there is **still no committed pre-change archive fixture**; that part
  > of this recommendation is unspent and belongs with the layout change.

  > ⚠ **Draft 10 claimed `AdditionalDirectoriesResolver` and `BackupEngine` "already model
  > extra dirs — configuration, not new mechanism". That is wrong.**
  > `AdditionalDirectoriesResolver` is a parser for **Claude Code's `additionalDirectories`
  > setting** specifically — two accepted shapes (root-level and `permissions`-nested),
  > entries as string or `{path}`, relative paths resolved against the settings file, `~`
  > expansion. OpenCode has no such key. Backing up an arbitrary product root is **new
  > `BackupEngine` work**, not configuration: a product-supplied root set, per-product skip
  > rules (`ShouldSkipHomeSubdir` is Claude-shaped), and a per-product exclusion list for
  > `auth.json`. Budget it accordingly.
- **Redaction is mandatory.** `~/.local/share/opencode/auth.json` holds **plaintext API
  keys and OAuth tokens**. Exclude it by default and add `auth` to the sensitive-key
  classifiers. Per the `AGENTS.md` parity invariant that means editing **both**
  `SensitiveKeys._segmentExact` (Sdk) and `JsonRedactor.SegmentExact` (Core) with an
  identical `RedactedMarker` — `SensitiveKeysParityTests` enforces it.
- **Footprint page** mirrors the Memory page's Tier-2 view over
  `~/.local/share/opencode/` (override `OPENCODE_DATA_DIR`, which accepts a
  comma-separated list): `storage/` (`message` · `part` · `project` · `session` ·
  `session_diff`) · `log/` · `snapshot/` · `tool-output/` · `bin/` · `repos/`.
  Treat the documented layout as unverified (Spike S3).

  > ⚠ **"Reuses `FootprintService` + `MemoryArtifactDeleter`" was another name-level claim.**
  > `FootprintService.GetStatsAsync` iterates `Enum.GetValues<FootprintCategory>()` — a
  > **closed enum of Claude categories** (`SessionTranscripts` · `SessionMetadata` ·
  > `PromptHistory` · `BashCommandLog` · `CostTrackerLog` · `Todos` · `FileEditHistory`) —
  > and bakes `PlatformPaths.ClaudeHome` into its per-category paths and its
  > `~/.claude/projects/` transcript logic. OpenCode's categories share **none** of those
  > names. `MemoryArtifactDeleter` is a `static class`.
  >
  > So `FootprintCategory` needs the same treatment as `ConfigScope` and
  > `UserMemoryCategory`: **a closed enum becomes per-product data** (id, display name, root,
  > glob, in-standard-backup flag). The *shape* — walk categories, compute size and count,
  > delete per category — transfers; the code does not. Fold this into the Phase 10
  > static→instance conversion, which is already doing exactly this to the sibling services.
  >
  > **`BackupMode` is the same problem with a persistence twist.** Its three values survive,
  > but their *meanings* are written in Claude paths — `SettingsOnly` is defined as
  > "`~/.claude.json`, settings/hooks/agents/commands, per-project `.claude` folders,
  > worktrees, Desktop config, **excluding** `~/.claude/projects/`". Each product must supply
  > what each mode includes. And `BackupMode` is **serialised as a string into
  > `manifest.json`**, so this joins the archive-layout and `ExportManifest` changes as a
  > **third** persisted-format concern — version them as one migration, not three.

### Phase 15 — Packaging and release

Full detail in **Deployment** above — it is considerably more than draft 10's one
paragraph. Summary of what lands here:

- **Publish scripts parameterized** on an app descriptor (5 of the 10 hardcode app
  identity, including `Smoke-PublishedBinary.ps1`, which asserts the startup log says
  `"Starting ClaudeForge"` — wrong for the new app, and it is the post-publish gate).
- **Per-app assets**: `.desktop`, `linux-setup.sh`, `.svg`, the 256/64px icons, macOS script.
- **`release.yml` matrix gains an app dimension** — 12 publish jobs — plus per-app download
  tables, install instructions, and `gh release create` artifact lists.
- **Second winget manifest set** + `Submit-Winget.ps1` parameterized; `winget-submit.yml`
  per app; carry the `40c3ebf` lessons.
- **`AssemblyProduct` moves out of `Directory.Build.props`** into the per-app csproj.
- Document the **two-app signing procedure** — the signing script is not in the repo.

**Already landed in Phase 8, not here:** the tag-prefix decision and the
`GithubReleaseChecker` move from `/releases/latest` to list-and-filter. Deferring those to
Phase 15 would mean OpenCodeForge shipped a broken update check for seven phases.

**The repo keeps its name [decision 14].** `JanusMael/ClaudeForge` will host two apps and a
neutrally-named library family, which reads oddly — but three winget versions are already
published with `PackageUrl` values pointing at it, and published manifests cannot be
retroactively repointed. GitHub would redirect a rename, but every raw badge URL, workflow
reference, and published manifest would need attention for a purely cosmetic gain. Note the
mismatch in `README.md` instead.

**Carry forward the winget lessons in `40c3ebf`** — `wingetcreate submit` builds only from
`packaging/winget/*.yaml` and does not carry published fields forward; pin
`ManifestVersion`; set `[Console]::OutputEncoding`; respect the duplicate guard and the
signing precondition (CI publishes unsigned; `sign-release.ps1` signs then submits).

---

## Spikes

| # | Question | How to answer |
|---|---|---|
| ~~**S4**~~ | ✅ **ANSWERED 2026-08-17 — see below.** Does `SchemaTreeBuilder` follow a **root-level `$ref`**? | **No — it returns 0 nodes.** Fix identified and it is small. |
| ~~**S11**~~ | ✅ **ANSWERED 2026-08-17 — see below.** Does the external models.dev `$ref` hit the network mid-parse? | **No network at parse or tree-build. But `Evaluate()` throws, and the ref target is a 6,688-value enum.** |
| ~~**S1**~~ | ✅ **ANSWERED 2026-08-17 — see below.** Does OpenCode **union** arrays across layers or replace? | ⛔ **Neither — it is PER-KEY.** `instructions` and `plugin` union; `disabled_providers`, `enabled_providers`, `skills.paths/urls`, `experimental.primary_tools` replace. The spike's binary framing was wrong. |
| ~~**S2**~~ | ✅ **ANSWERED 2026-08-17 — see below.** `agent/` vs `agents/`, `command/` vs `commands/`. | **Both spellings work, simultaneously.** Vendor-documented *and* measured. Scan both. |
| ~~**S3**~~ | ⚠️ **PARTLY ANSWERED 2026-08-17 — see below.** Actual `~/.local/share/opencode/` layout. | **Path *resolution* answered authoritatively via `opencode debug paths`. Populated layout still unknown** — the probed install has never been used. **Two roots the plan never mentions.** |
| **S5** | Does **OpenCode Desktop** have its own config surface, or does it read `config.json`? Decides whether it is a third section or a presence indicator. | Install the desktop beta; diff `~/.config/opencode/` before/after; check for an Electron/Tauri settings store. |
| ~~**S6**~~ | ✅ **ANSWERED 2026-08-17 — see below.** How big is the **TUI schema's** realized editor tree, and does `keybinds` blow the page open? | **Yes, and worse than feared: 184 raw-JSON editors.** `keybinds` is 86% of the tree and 99% of the file. |
| ~~**S7**~~ | ✅ **ANSWERED 2026-08-17 — see below.** Inline JSON vs a markdown file of the same name — which wins? | **Neither shadows the other: they DEEP-MERGE, file wins per field.** Inline-only fields survive. |
| ~~**S8**~~ | ✅ **ANSWERED 2026-08-17 — see below.** Do the skill roots merge, or does first hit win? | **Both: roots union by name; on a name collision the higher-precedence root wins, one entry, no duplicate.** Also: skills are discovered **recursively** and nested ones flatten into the global namespace. |
| ~~**S10**~~ | ⚠️ **SOURCED 2026-08-17, NOT VERIFIED — see below.** Exact install commands per platform. | Taken from the **v1.17.9-tagged** docs, not guessed. **Still must be run on a clean machine before shipping.** |
| ~~**S9**~~ | ✅ **ANSWERED 2026-08-17 for v1.17.9 — see below.** Which rule-resolution semantics? | **v1 semantics: first-match-wins per category, and the `~/.claude/CLAUDE.md` fallback is STILL PRESENT.** Version-gate as planned. |

### S4 — ANSWERED: no, and the fix is ~5 lines *(2026-08-17, measured)*

Ran against the live `https://opencode.ai/config.json` (38,773 bytes) through a throwaway
harness in `ClaudeForge.Core.Tests`, since deleted. Schema shape confirmed exactly as
predicted: root is `{"$schema", "$ref": "#/$defs/Config", "$defs", "allowComments",
"allowTrailingCommas"}`, **zero** top-level `properties`, and `$defs.Config.properties`
holds **36** keys.

| Measurement | Result |
|---|---|
| `SchemaTreeBuilder.BuildTopLevel(root)` | **0 nodes** — root `$ref` is not followed |
| `BuildTopLevel($defs.Config node)` | **36 nodes**, names matching the schema exactly |

**Root cause is one method.** `GetPropertySubschemas` looks only for a `properties`
keyword and returns empty when there isn't one. **The fix is cheap because JsonSchema.Net
already did the resolution for us:** `$ref` is exposed as an ordinary `KeywordData` with
`Handler.Name == "$ref"` and **`Subschemas.Length == 1`** holding the *resolved target
node* (`RelativePath == "/Config"`, keywords `[type, properties, additionalProperties]`).
So `GetPropertySubschemas` gains a fallback: no `properties` → follow a single-subschema
`$ref` and retry. No pointer parsing, no manual `$defs` walk, no registry work.

**Two traps that shape how the fix must be written:**

1. **An unresolvable external `$ref` returns `Subschemas.Length == 0` and does NOT throw**
   — verified directly at the `model` node. So the fallback degrades safely: it finds
   nothing to follow and drops through to the sibling keywords. Do *not* wrap it in a
   try/catch and assume a throw; do handle the 0 case.
2. **Follow `$ref` only when there is no `properties` keyword** — never unconditionally.
   Draft 2020-12 lets `$ref` sit *alongside* siblings (OpenCode's `model` is
   `{type, description, $ref}`), and unconditional following would splice the ref target's
   properties into every such node. See the S11 coupling below for why this specific one
   matters.

**Unexpected bonus finding:** JsonSchema.Net did **not** reject the unknown root keywords
`allowComments` / `allowTrailingCommas` — they surfaced as hash-named handlers
(`4e9c21a6…`) and parsed fine. That narrows [[the earlier "strict-rejects unknown
keywords" note]] to *custom keywords injected into a schema we author*, not any unknown
keyword anywhere. It also means **OpenCode's schema self-declares JSONC** — direct
corroboration for Phase 2.

### S11 — ANSWERED: parse is safe, `Evaluate()` is the blocker *(2026-08-17, measured)*

| Question | Answer |
|---|---|
| Network hit at parse? | **No.** Parse = 8–11 ms. Null-routing the host to `https://10.255.255.1/…` completed in ~20 ms instead of stalling on a connect timeout — proof no socket is opened. |
| Network hit at tree-build? | **No.** `SchemaTreeBuilder` never follows `$ref` today (see S4). |
| Does `model` still build a usable node? | **Yes.** `ValueType = String`, description intact — because the `$ref` sits *alongside* `"type": "string"` and `"description"`. `small_model` likewise. (`AgentConfig.model` and `command.*.model` carry `type` but no description.) |
| Throw / hang / silent untype? | **None of the three at parse.** `Json.Schema.SchemaRegistry.Global.Fetch` is non-null but is a *throwing stub* — it never dials out. |
| **`Evaluate()`?** | ⛔ **Throws `RefResolutionException`** — *"Could not resolve `https://models.dev/model-schema.json#/$defs/Model`"* — in ~10 ms. **Lazy and instance-driven:** `{"logLevel":"INFO"}` evaluates fine; `{"model":"…"}` throws. |

⛔ **This is a real crash path, and it is in shared code.** `SchemaRegistry.CollectSchemaErrors`
calls `schema.Evaluate(...)` with no exception handling, so a `RefResolutionException`
propagates through `ValidateWorkspaceAsync` → `SaveAsync` — **the user hits it by saving
any OpenCode config that sets `model`**, i.e. nearly all of them. Phase 7 must not bundle
the schema verbatim.

**The ref target is far worse than "an external reference".** `models.dev/model-schema.json`
is **281 KB**, and `$defs/Model` is a single `string` `enum` of **6,688 model IDs across
189 providers**. Consequences:

- **Registering it locally "fixes" `Evaluate` and breaks users.** Verified: with the real
  document registered in the local `Json.Schema.SchemaRegistry`, `anthropic/claude-opus-5`
  → valid, but `my-selfhosted/llama-42` → **invalid**. It is a hard allowlist, so every
  custom, self-hosted, or newer-than-the-bundle model becomes a save-blocking error. This
  directly contradicts the locked *"warn on a disabled provider, never block"* decision.
- **It is coupled to the S4 fix.** If models.dev were ever registered *and* `$ref`-following
  were unconditional, `model` would build as an **Enum node with 6,688 values** — measured,
  not hypothesized. Trap 2 in S4 above exists precisely to prevent this.
- Bundling 281 KB of weekly-churning model IDs into an offline-first app is a poor trade
  regardless.

**Decision — strip the external `$ref` at refresh time** (four sites: `Config.model`,
`Config.small_model`, `AgentConfig.model`, `command.*.model`). Verified: with the `$ref`
removed the sibling `"type": "string"` survives, `Evaluate` returns valid for any string,
and the node still builds with its description. This supersedes the draft's *"pre-resolve
the reference into the bundled overlay"* fallback — pre-resolving is what *causes* the
6,688-value allowlist. The refresh script must strip rather than inline, and needs a test
asserting no `"$ref": "http…"` survives into the bundled copy.

This **confirms the already-locked model-picker decision** rather than changing it:
suggestions come from `provider.<id>.models` in the user's own config, offline. The
models.dev list stays available as an *optional, opt-in suggestion source* — never a
validator.

### S6 — ANSWERED: `keybinds` is the whole problem *(2026-08-17, measured)*

Ran `https://opencode.ai/tui.json` (1,156,030 chars) through `SchemaTreeBuilder`.
Both of the plan's predictions were exactly right — **13 top-level properties, 184
keybind actions** — and the consequence is worse than "a big page".

| Measurement | Result |
|---|---|
| Top-level nodes | **13** (no `$ref` anywhere in `tui.json` — the S4 fix is not needed here) |
| **Total nodes in the realized tree** | **215** |
| …of which under `keybinds` | **185 (86%)** |
| `keybinds` serialized share of the file | **99.1%** (325,685 of 328,738 compact chars) |
| Parse time | **137 ms** (vs 8–11 ms for `config.json` — 17×) |
| `BuildTopLevel` | 12 ms |

⛔ **All 184 keybind children classify as `Complex` with no children and no enum**, so every
one of them lands on the **raw-JSON fallback editor**. This is review pass 2's finding
made concrete: each keybind is
`anyOf[ boolean(false) | "none" | anyOf[ string | object{name,ctrl,shift,meta,super,…} ] ]`,
which is *not* an all-string union, so `TryGetStringUnionEnum` correctly declines to rescue
it. The page would render **184 raw-JSON text boxes**, not 184 rows of sensible controls.

*(Measured on the node tree rather than the `[PropView.Realized] group=… wrappers=N` trace
the draft suggested — that trace needs the running GUI, and the node count is the
underlying quantity anyway.)*

**Consequences for the plan:**

- **The `keybinds` compound editor in Phase 9 is mandatory, not nice-to-have.** It needs a
  key-chord capture control plus search/filter over 184 actions, and it must **not** be
  realized as generic property rows. Budget it as a first-class editor.
- Everything *else* in `tui.json` is genuinely small: 30 nodes total across the other 12
  properties, with the largest (`attention`) at 13. Excluding `keybinds`, the TUI section
  is exactly the "screenful" the draft hoped for.
- 1.1 MB bundled + 137 ms parse argues for **lazy-loading the TUI schema** rather than
  parsing it at startup alongside `config.json`.
- `tui.json` also self-declares `allowComments` / `allowTrailingCommas`, same as
  `config.json` — more Phase 2 corroboration.

### S3 — PARTLY ANSWERED: ask OpenCode, don't guess — and there are two roots the plan missed *(2026-08-17)*

`opencode debug paths` exists and prints the resolved roots authoritatively. On the
maintainer's machine (**OpenCode v1.17.9, Windows**):

| Key | Path |
|---|---|
| `home` | `C:\Users\brian` |
| `config` | `~/.config/opencode` |
| `data` | `~/.local/share/opencode` |
| `log` | `~/.local/share/opencode/log` |
| `repos` | `~/.local/share/opencode/repos` |
| **`state`** | **`~/.local/state/opencode`** ⚠ **not in this plan** |
| **`cache`** | **`~/.cache/opencode`** (with `bin/`) ⚠ **not in this plan** |
| `tmp` | `%LOCALAPPDATA%\Temp\opencode` |

1. **OpenCode uses XDG-style paths on Windows** — `~/.config`, `~/.local/share`,
   `~/.local/state`, `~/.cache` — **not** `%APPDATA%` / `%LOCALAPPDATA%`. Exactly the kind
   of thing that gets guessed wrong. `PlatformPaths` must not assume Windows conventions.
2. **`state` and `cache` are two footprint roots the plan never accounted for.** Phase 14
   (backup / restore / footprint / prune) and the Phase 8 install probe both need them.
   `cache/bin` is where OpenCode stores downloaded binaries — likely the largest artifact
   on disk and a prime prune candidate.
3. **The `project/` → `storage/` question is moot at 1.17.9:** `data` contains only `log/`
   and `repos/`. Neither `project/` nor `storage/` exists.
4. ⚠ **What is NOT answered:** the probed install has **never been meaningfully used** —
   `data`, `state`, and `cache` are all 0 bytes, and `config` holds only
   `plugins/gk-hooks.js`. The *populated* layout (what appears after real sessions) is
   still unknown. Re-probe on a used install before building the footprint page.

### ⭐ The single most valuable discovery — OpenCode ships its own spec

`opencode debug skill` lists a **built-in skill named `customize-opencode`** (~16 KB,
`location: <built-in>`, registered at `packages/core/src/plugin/skill.ts`). It is the
vendor's own, **version-matched**, authoritative description of the entire configuration
surface: file locations, merge rules, every artifact type's frontmatter, the permission
model, plugin hook names, and the env-var escape hatches.

**Read it before writing any OpenCode-facing code, and re-read it on every version bump.**
It is strictly better than the web docs because it ships *inside the binary being probed*,
so it can never be a version ahead or behind. It answered or corrected S2, S8, and large
parts of the product model in one pass. Extract it with:

```bash
opencode debug skill
```

### S1 — ANSWERED: array merge is **per-key**, not global *(2026-08-17, measured)*

The spike asked "union or replace?" — a **false binary**. Measured with three real layers
(`OPENCODE_CONFIG` → project `opencode.json` → `OPENCODE_CONFIG_CONTENT`):

| Key | Behaviour | Evidence |
|---|---|---|
| `instructions` | ⬆ **UNION**, lowest-precedence layer **first** | `["global-x.md", "proj-a.md", "proj-b.md", "inline-z.md"]` |
| `plugin` | ⬆ **UNION**, auto-discovered entries appended last | `["extra-plugin", "proj-plugin", "file:///…/plugins/gk-hooks.js"]` |
| `disabled_providers` | ⛔ **REPLACE** | project's `["openai"]` won; lower layer's `["anthropic"]` discarded |
| `enabled_providers` | ⛔ **REPLACE** | project's `["proj-prov"]` won outright |
| `skills.paths` / `skills.urls` | ⛔ **REPLACE** | `/extra/skills` vanished entirely |
| `experimental.primary_tools` | ⛔ **REPLACE** | `["proj-tool"]` only |
| objects (`permission`, `experimental`, `agent`, `command`) | 🔀 **DEEP MERGE** | see the hazard below |
| scalars (`logLevel`, `username`) | last layer wins | `ERROR` / `from-inline` |

**`OpenCodeMergePolicy` therefore needs a per-key table, not a single array rule.** A
policy that unions everything silently resurrects providers the user disabled; one that
replaces everything silently drops the global `AGENTS.md` from `instructions`. Both are
data-losing. **Test each key in the table explicitly.**

Layer precedence confirmed: `OPENCODE_CONFIG` **<** project `opencode.json` **<**
`OPENCODE_CONFIG_CONTENT`. The plan's scope ladder was right.

> ### ⛔ Permission merging can silently invert the user's intent
>
> The vendor spec states: *within a permission object, **insertion order matters** —
> opencode evaluates the **LAST** matching rule, so put broad rules first and narrow rules
> last.* Combine that with deep-merge-by-key-order and you get a trap. Measured:
>
> | Layer | `permission.bash` |
> |---|---|
> | lower (`OPENCODE_CONFIG`) | `{"npm *": "deny"}` |
> | higher (project) | `{"*": "ask", "git *": "allow"}` |
> | **merged result** | `{"npm *": "deny", "*": "ask", "git *": "allow"}` |
>
> The lower layer's keys land **first**. Because the **last** match wins, the project's
> broad `"*": "ask"` now sits *after* the narrow `"npm *": "deny"` — so **`npm install`
> resolves to `ask`, not `deny`.** The user's deny was silently defeated by merge ordering
> alone, with no edit to either file.
>
> **Consequences, all mandatory:**
> - The permission model is a **JSON object whose key order is semantically load-bearing.**
>   Any editor that re-serializes it alphabetically, or does `Remove` + re-add, **inverts
>   the user's rules.** This is the strongest possible argument for Phase 2's
>   order-preserving writer — and note `SchemaRegistry.ApplyMergePatch` already carries a
>   fixed bug of exactly this shape (Remove + re-add pushed keys to the end).
> - The Effective view for permissions must show **final key order** and evaluate
>   **last-match-wins**, not first.
> - **Add a warning** when a broad pattern from a higher layer shadows a narrower rule from
>   a lower one. This is a danger-tenant surface, not a nicety.
> - Add a merge-order regression test using exactly the table above.

### S2 — ANSWERED: both spellings, simultaneously *(2026-08-17, vendor-documented + measured)*

The `customize-opencode` spec lists `agent(s)`, `command(s)`, `skill(s)`, and
`plugin(s)` — **both singular and plural are supported everywhere.** Measured directly:
`.opencode/agent/dup.md` and `.opencode/agents/dup2.md` **both** resolved, as did
`.opencode/command/cmd1.md` and `.opencode/commands/cmd2.md`.

**The artifact resolver must scan both spellings and union the results** — not pick one.
Choosing one would silently find nothing for half of all users.

### S7 — ANSWERED: deep merge, file wins per field *(2026-08-17, measured)*

Not "which wins" — **both contribute.** Defined `agent.dup` inline in `opencode.json`
*and* as `.opencode/agent/dup.md`:

| Field | Inline JSON | Markdown file | **Resolved** |
|---|---|---|---|
| `description` | `INLINE-JSON-VARIANT` | `MARKDOWN-SINGULAR-VARIANT` | **markdown** |
| `prompt` | `inline prompt body` | `markdown prompt body` | **markdown** |
| `temperature` | `0.11` | *(absent)* | **`0.11` — inline survives** |
| `color` | `warning` | *(absent)* | **`warning` — inline survives** |

**Phase 11's shadowing chain must be per-field merge, not whole-artifact replacement.**
A UI that says "the file shadows the inline definition" would be lying: an inline-only
`temperature` is still live. Show *field-level* provenance.

Resolution also **normalizes**: the merged agent gained `name`, `options: {}`,
`permission: {}`. The editor must not treat those synthesized keys as user edits.

### S8 — ANSWERED: union by name, precedence on collision, recursive discovery *(2026-08-17, measured)*

124 skills resolved simultaneously from **`<built-in>` + `~/.claude/skills/` +
`~/.agents/skills/`** — so the roots **union**. Then, seeding a colliding `handoff` skill:

| Seeded at | Winner | Entries named `handoff` |
|---|---|---|
| `<project>/.opencode/skills/handoff/` | **project** | **1** — no duplicate |
| `~/.config/opencode/skills/handoff/` (project one removed) | **`~/.config/opencode`**, beating `~/.claude` | **1** |

**Precedence: project `.opencode/skill(s)` > global `~/.config/opencode/skill(s)` >
external (`~/.claude/skills`, `~/.agents/skills`).** Name-keyed, single winner, shadowed
copies are not surfaced at all — so **the Skills page must compute the shadowing itself**
if it wants to show "this one is being overridden".

⚠ **Discovery is recursive, and nested skills flatten into the global namespace.**
`~/.agents/skills/microsoft-foundry/models/deploy-model/preset/SKILL.md` registers as a
top-level skill simply named **`preset`** — alongside `customize`, `capacity`,
`finetuning`, `deploy-model` from the same tree. The spec confirms: skill loaders scan
`**/SKILL.md`. So a "one folder = one skill" assumption is wrong, and generic nested names
are collision-prone by construction.

### S9 — ANSWERED for v1.17.9: v1 semantics, fallback still present *(2026-08-17)*

From `packages/web/src/content/docs/rules.mdx` at **tag `v1.17.9`** (not `main`):

1. **Local** — traverse **up** from cwd for `AGENTS.md`, then `CLAUDE.md`
2. **Global** — `~/.config/opencode/AGENTS.md`
3. **Claude Code** — `~/.claude/CLAUDE.md` — ⚠ **still present at 1.17.9**

*"The first matching file wins in each category"*, and `~/.config/opencode/AGENTS.md`
takes precedence over `~/.claude/CLAUDE.md`. So the effective result is **one local file +
one global file**, and `instructions[]` entries are **combined with** them, not instead of.

**The v2 "fallback is gone" reading does not apply to 1.17.9.** The plan's instinct to
version-gate the resolver was correct — keep it.

Also captured:
- **Three** Claude-compat kill switches, not two: `OPENCODE_DISABLE_CLAUDE_CODE=1`,
  `OPENCODE_DISABLE_CLAUDE_CODE_PROMPT=1`, `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1`.
- `instructions[]` supports globs **and remote URLs, fetched with a 5-second timeout** —
  a network dependency inside rule resolution. Surface it; never block the UI on it.

### S10 — SOURCED (not verified) from the v1.17.9 docs *(2026-08-17)*

⚠ **Do not ship these until each has been run on a clean machine** — that was the spike's
own condition and it still stands. Sourced from the tagged docs rather than guessed:

| Platform | Commands |
|---|---|
| Any | `curl -fsSL https://opencode.ai/install \| bash` *(the recommended path)* |
| Node | `npm install -g opencode-ai` · `bun` · `pnpm` · `yarn global add opencode-ai` |
| macOS / Linux | `brew install anomalyco/tap/opencode` — ⚠ **the tap, not the plain `opencode` formula**, which the docs say lags |
| Arch | `sudo pacman -S opencode` (stable) · `paru -S opencode-bin` (AUR) |
| **Windows** | `choco install opencode` · `scoop install opencode` · `npm install -g opencode-ai` · `mise use -g github:anomalyco/opencode` · `docker run -it --rm ghcr.io/anomalyco/opencode` |

- **Windows note the banner should carry:** the docs *recommend WSL* on Windows for full
  feature compatibility. A Windows-native banner that omits that is misleading.
- **No `winget` package** is listed — notable, since OpenCodeForge itself ships via winget.
- The maintainer's own install is at `C:\Program Files\nodejs\opencode` — i.e. **npm-global**,
  a location the plan's probe list does not include. Add it.

> ⚠ **The upstream repo moved: `sst/opencode` → `anomalyco/opencode`** (default branch
> `dev`). `raw.githubusercontent.com` does **not** follow the rename, so any hardcoded raw
> URL to `sst/opencode` 404s. Homebrew tap and Docker image are `anomalyco/*` too.

### Found while spiking — facts that change the plan *(2026-08-17)*

Not answers to any spike; discovered in passing and each one invalidates something.

#### 1. ⛔ A bad config does not degrade — it **bricks every OpenCode command**

Accidentally wrote `"color": "magenta"` into a project `opencode.json`. The result:

```
Error: Configuration is invalid at …\opencode.json
↳ Expected a string matching the RegExp ^#[0-9a-fA-F]{6}$, got "magenta" agent.dup.color
↳ Expected "primary"|"secondary"|"accent"|"success"|"warning"|"error"|"info", got "magenta" agent.dup.color
```

…and **`opencode debug skill` failed too.** It is not just startup: *every* command
refuses to run. The spec is explicit — *"opencode validates its own config strictly and
refuses to start when a field is wrong"*, and *"unknown top-level keys are rejected with
`ConfigInvalidError`"*.

**This is a different risk class than Claude Code, which is forgiving.** A bad save from
OpenCodeForge takes the user's entire agent offline until they hand-edit a file — and they
cannot use OpenCode to fix it. Therefore:

- **Save validation is not advisory for OpenCode; it is a hard gate.** The `force: true`
  bypass that exists for Claude must be reconsidered or loudly re-labelled here.
- **Surface the escape hatches in the error dialog** — `OPENCODE_DISABLE_PROJECT_CONFIG=1`
  lets the user start OpenCode and repair the file from inside it. That turns a brick into
  an inconvenience, and it is the vendor's own recommended recovery.
- Good news for implementation: the error format is `↳ <message> <dot.path>`, one line per
  failing branch — a near-exact match for `SchemaValidationError`'s
  `(InstancePath, Message)` shape, including the same all-branches-of-an-anyOf verbosity
  that `CollapseFailedAnyOfErrors` already handles.
- Concrete enum captured: `agent.*.color` = `#RRGGBB` **or** one of
  `primary|secondary|accent|success|warning|error|info` — an all-string union with an enum
  branch, i.e. exactly the shape `TryGetStringUnionEnum` rescues into a free-form picker.

#### 2. ⛔ Config is **not hot-reloaded** — a saved change does nothing until restart

*"Config is loaded once when opencode starts and is not hot-reloaded… tell the user to
quit and restart opencode."*

ClaudeForge's mental model — edit, save, done — **is wrong for OpenCode.** The Diagnostics
"live config changes" section assumes a watcher is enough; here a successful save has *no
effect on the running agent*. **Every successful OpenCode save needs a "restart OpenCode
for this to take effect" affordance**, and the diagnostics view should show
loaded-at-startup vs on-disk as two distinct states. This applies to agent files, skills,
plugins, and `opencode.json` alike.

#### 3. ⛔ `~/.config/opencode/` contains **60 MB of `node_modules`** — backup must exclude it

Running the debug commands materialized the global config dir. It now holds:

| Item | Note |
|---|---|
| `opencode.jsonc` | ⭐ **auto-created by OpenCode itself, and it is `.jsonc`** — not `.json` |
| `package.json` | pins `@opencode-ai/plugin` to the exact CLI version (`1.17.9`) |
| `package-lock.json` | 13.8 KB |
| **`node_modules/`** | **~60 MB**, 24 packages — for plugin dependency resolution |
| `.gitignore` | auto-managed; ignores all of the above |

**The plan says "Backup archives `~/.config/opencode/`". That would produce a 60 MB
archive of regenerable dependencies.** Exclude `node_modules/`, `package-lock.json`, and
`bun.lock` — OpenCode's own `.gitignore` in that directory is a ready-made exclusion list,
and honouring it is both correct and self-maintaining. `node_modules` is also the single
largest prune candidate for the Phase 14 footprint page.

⭐ **`opencode.jsonc` being the file OpenCode writes for itself is decisive for Phase 2.**
The default global config is JSONC, both published schemas declare `allowComments` and
`allowTrailingCommas`, and the vendor lists `./opencode.jsonc` as a project location. A
lossy JSON writer is not an acceptable fallback for this product.

#### 4. The project config scope is wider than the plan states

Vendor spec: project config is `./opencode.json`, `./opencode.jsonc`, **or**
`.opencode/opencode.json` — and **opencode walks up from the cwd to the worktree root**.
The plan's Phase 7 ladder says only "`opencode.json` at root". Three filenames and an
upward walk; the scope resolver must match, or it will show the wrong file as authoritative.

#### 5. `debug config` already computes plugin provenance — reuse it

The resolved config carries a `plugin_origins` array (`spec`, `source`, `scope`) that
OpenCode computes itself, e.g.
`{"spec":"file:///…/gk-hooks.js","source":"C:\\Users\\brian\\.config\\opencode","scope":"global"}`.
The Plugins page wants exactly this. Note it is **output-only** — not one of the 36 schema
keys — so it must not leak into anything written back to disk.

Auto-discovery confirmed: any `*.ts`/`*.js` in `.opencode/plugin(s)/` or
`~/.config/opencode/plugins/` loads with **no config entry**, and merges into project scope.

#### 6. Smaller facts worth not rediscovering

- **`{env:VAR}` and `{file:path}` interpolate** inside string values (e.g. MCP headers).
  Shell-style `${VAR}` does **not**. Affects the MCP editor and credential redaction.
- **Unknown agent frontmatter fields are silently routed into `options`** — no error, no
  warning. The agent editor should say so rather than let a typo vanish.
- Built-in agents: `build`, `plan`, `general`, `explore`; **hidden**: `compaction`,
  `title`, `summary`. `default_agent` must point to a **non-hidden, primary-mode** agent —
  a validation rule the editor can enforce locally.
- Permission keys that accept **only a flat action**, never a per-pattern object:
  `todowrite`, `question`, `webfetch`, `websearch`, `doom_loop`. The rest of
  (`read, edit, glob, grep, list, bash, task, external_directory, lsp, skill`) take either.
- `opencode debug scrap` exposes a project registry with a **`sandboxes`** concept the plan
  has no model for. Worth a look before Phase 10.
- State now lives in **`~/.local/share/opencode/opencode.db`** (SQLite + `-wal`/`-shm`),
  and **`~/.cache/opencode/models.json`** caches the models.dev catalog — i.e. the
  catalog the plan wants for the model picker is *already on disk*, no fetch needed.

---

## ⏱ Deferred re-checkpoint — validate against a *used* install **[NEW, maintainer request]**

Everything above was measured against an install that had **never run a real session**.
That is enough to build against, but not enough to trust for anything derived from
*accumulated* state. The maintainer is building up genuine OpenCode usage over the coming
days specifically so this can be re-validated.

**Gate: this checkpoint must clear before Phase 10 (`AgentForge.Artifacts`) ships, and
again before Phase 14 (Backup / Restore / footprint) ships.** Phases 1–9 do not depend on
it. Re-run the probes below against the used install and diff against what is recorded here.

| # | Re-check | Why it can't be trusted yet | Blocks |
|---|---|---|---|
| 1 | **`opencode debug paths` + a full `find` of `data`, `state`, `cache`** | All three were **0 bytes**. The populated layout is unknown. | Phase 14 |
| 2 | **`opencode.db` size, growth, and whether anything in it is user-authored** | It appeared only *after* the debug commands ran. If sessions live in SQLite, "backup `~/.config/opencode/`" misses them entirely. | Phase 14 |
| 3 | **`~/.local/state/opencode/locks/` — what takes locks, and does a running OpenCode block our writes?** | Empty. A lock held during our save is a real failure mode on Windows. | Phase 8 |
| 4 | **`~/.cache/opencode/` growth, esp. `bin/`** | Empty. This is the likeliest prune target and its size is unmeasured. | Phase 14 |
| 5 | **`node_modules` size on a real install** | 60 MB with **one** plugin. Users with several plugins will be larger. | Phase 14 |
| 6 | **`opencode debug scrap` with real projects — and what `sandboxes` holds** | Only the synthetic `global` entry existed. | Phase 10 |
| 7 | **A real multi-layer `permission` merge** | The inversion hazard was proven on synthetic layers. Confirm on real config, and confirm **last-match-wins** by actually triggering a rule. | Phase 6 / 9 |
| 8 | **`opencode debug agent <name>` output** | Could not run — *"No providers found"*. Needs a configured provider. This is the direct probe for resolved rules and per-agent permissions. | Phase 11 |
| 9 | **Rule resolution end-to-end (S9)** | Established from **docs at the tag**, not observed. Confirm the `~/.claude/CLAUDE.md` fallback actually fires, and re-check the version — `ProductVersionProbe` must gate this. | Phase 11 |
| 10 | **S10 install commands** | **Sourced, never executed.** Each must run on a clean machine/VM. | Phase 8 |
| 11 | **Re-extract `customize-opencode`** | It is version-matched; a version bump may silently change the spec this plan is built on. | every phase |

**Cheap insurance:** script these as `scripts/probe-opencode.ps1` emitting a JSON snapshot,
and commit the snapshot. Then a re-check is a diff, not a re-investigation — and the same
script becomes the field-diagnostic when a user reports something the app got wrong.

---

## Test plan — comparable coverage for every new OpenCode path **[NEW]**

The existing suite is **2,512 test methods**. Anything new must be tested to the same
standard, mirroring the existing per-area layout (`tests/ClaudeForge.Core.Tests/{Backup,
Catalog,FileIO,Platform,Profile,Schema,Settings,Updates}`, `tests/ClaudeForge.Sdk.Tests/
{Env,Hooks,Memory,Models,Permissions,Diagnostics}`, `tests/ClaudeForge.Tests/{Adapters,
Converters,Headless,Localization,Services,ViewModels}`).

### New test projects

```
tests/AgentForge.Jsonc.Tests            tests/AgentForge.Artifacts.Tests
tests/AgentForge.Permissions.Tests      tests/AgentForge.Avalonia.Shell.Tests   ← was missing
tests/OpenCode.Sdk.Tests                tests/OpenCode.Avalonia.Tests
tests/OpenCodeForge.Tests
```

**`AgentForge.Avalonia.Shell.Tests` was omitted from draft 9** — the largest extraction in
the plan had no named test home. It receives the product-neutral half of today's
`ClaudeForge.Tests`: `Headless/*`, `Services/WindowStateServiceTests`, `DebugFlagsTests`,
`Status/*`, `NavDeepPathTests`, `NavigationNodeIdTests`, `Accessibility/*`. Getting this
wrong means shell tests stay in an app-specific project and silently only ever exercise
one app's wiring — which is exactly the failure mode the extraction is meant to prevent.

Each gets `GlobalUsings.cs` + `Parallelization.cs` copied from the nearest existing peer.
**`Parallelization.cs` matters** — per the repo's own hard-won lesson, `Sdk.Tests` runs
method-level parallel and relies on `PlatformPaths.TestUserProfileOverride` being
`AsyncLocal`; a new project that opts into parallelism without that isolation will produce
intermittent cross-test failures.

### Coverage by area

| Area | Must-have tests | Notes |
|---|---|---|
| **`AgentForge.Jsonc`** — highest risk in the plan | **Byte-stability**: load → save with no edit → assert every byte identical **outside the `"//"` stamp line** (the stamp embeds `DateTime.Now` — see Problem 4; assert the whole file only if option 2 or 3 is chosen). Corpus of real config files: commented, tab-indented, CRLF, BOM, trailing commas, deeply nested. **Single-edit minimality**: change one scalar → assert only that span and the stamp differ. **Comment survival** at every position (leading, trailing, between keys, inside arrays). **Insert/remove at path** with correct indent inference. **Malformed input** → no throw, no data loss. Property-based round-trip over generated documents. | This code sits on the save path for **both** products. Target the densest coverage in the repo. Include a fixture corpus under `tests/AgentForge.Jsonc.Tests/Fixtures/`. |
| **Shared permission vocabulary** | `PermissionOutcome` and `Decision<TRule>` compile against both products' rule types. Claude's ~200 existing permission tests stay **exactly where they are and unchanged** — there is no extraction to prove faithful, because there is no extraction. | Draft 11 specified an extraction-parity suite here; with Phase 6 reduced to a vocabulary, that suite has nothing to test. |
| **`OpenCodePermissionModel`** | Parse/format for: bare-string form · `"*"` wildcard · per-tool action · per-tool `{pattern: action}` · arbitrary (MCP) tool keys · the four action-only tools (`todowrite` `question` `webfetch` `websearch`) rejecting the object form. Glob matching: `*`, `?`, `~`/`$HOME` expansion, `git *` vs `git commit *` specificity. Per-agent override precedence. Deny-wins ordering. | Mirror `tests/ClaudeForge.Sdk.Tests/Permissions/` structure. |
| **`AgentForge.Artifacts`** | Claude's **existing** Memory / Agents-&-Skills tests pass unchanged. Plus: resolver returns winner **+ full shadowed chain**; same-name across all five source kinds (built-in / global-JSON / global-md / project-JSON / project-md) resolves per S7; three global roots per S8; upward traversal stops at the git worktree root; `skills.paths[]` glob expansion; remote sources listed-not-fetched. | The shadowing tests are the ones that catch real bugs — seed conflicts deliberately. |
| **`OpenCode.Sdk`** | `DiscoverFiles` for every scope-ladder permutation (no project · project · `OPENCODE_CONFIG` set · `OPENCODE_CONFIG_CONTENT` set · `OPENCODE_CONFIG_DIR` relocated · managed present). Read-only scopes reject writes. `OpenCodeMergePolicy` per S1. JSONC load. Save round-trip through `AgentForge.Jsonc`. Schema validation surfaces real errors. Every test sandboxed via `PlatformPaths.TestUserProfileOverride`. | Mirror `ClaudeCodeClientLifecycleTests` / `ClaudeConfigClientAsyncTests` / `…CoreReentrancyTests` — the thread-safety and reentrancy contracts on `AgentConfigClientCore` apply to `OpenCodeClient` too and must be re-asserted, not assumed. |
| **Rules resolution** | Load order with project + global + `instructions[]`; first-match-wins per category; glob expansion order; **the `OPENCODE_CONFIG_DIR` gotcha is reported, not reproduced**; `@file` references flagged; v1-vs-v2 semantics both covered and version-gated (S9). | This is the feature the maintainer called out as most important — treat its test count as a proxy for whether it is really done. |
| **Union classification** | Each of the four top-level unions (`permission` · `formatter` · `lsp` · `autoupdate`) builds a **typed** editor, not `JsonRawPropertyEditorViewModel` — assert the dispatched VM type, mirroring `PropertyEditorFactoryTests`. Same for the nested unions (`mcp.*` local/remote, `plugin[]` string-vs-tuple, `oauth` config-vs-false, `agent.*.color` hex-vs-theme, `scroll_speed`). **A regression here is silent** — the raw-JSON fallback works, it just looks terrible, and no existing test would notice. | This whole row exists because draft 9 wrongly assumed `SchemaTreeBuilder` collapsed unions. It classifies them `Complex`; only all-string unions get rescued. |
| **Schema handling** | Root-`$ref` follow (S4) → **36** top-level nodes for `config.json`, **13** for `tui.json`. Overlay merge applies and survives a simulated refresh. `@deprecated`-in-description normalization. Provenance/opt-in promotion: bundled wins by default, fetched wins after opt-in, overlay merges onto whichever base won. `--schema-source` flag flips it. | Mirror `SchemaRegistryOverlayTests` + `ModelCatalogSchemaParityTests`. |
| **Compound editors** (`mcp`, `permission`, `agent`, `command`, `plugin`, `keybinds`, `references`) | The mandated pair per `src/ClaudeForge/ViewModels/Editors/AGENTS.md`: `EditingXxxAfterLoad_FiresIsModifiedPropertyChanged` and `RemovingXxxAfterLoad_FiresIsModifiedPropertyChanged`. Plus `ToJsonValue()` returns `null` when empty; transient input fields don't mark modified; `OnResetToInherited` restores on-disk state; union-variant switching **preserves per-variant non-discriminator fields**, and an **unknown variant round-trips unchanged** rather than being dropped. | The two `MarkModified` tests are non-negotiable — that bug is the repo's most-repeated defect. The two union behaviours are lifted from `MarketplaceListEditorViewModel`, which already implements both; reproducing the editor without them is how a variant switch silently eats a user's config. |
| **Essentials (OpenCode)** | One test per card: read reflects disk, write reaches the SDK, danger predicate fires on the unsafe value. Plus the guard the repo already learned: `IntValueWrite_NotSuppressed_WhileReadIsInAsyncPhase` — re-assert it for the OpenCode VM, since the `IsLoading`-spanning-`await` bug is a whole-class trap. Derived cards (#16/#17) assert they report resolver state and never write. | Mirror `EssentialsViewModelTests`. |
| **Detection / install / update** | Install probes: binary-on-PATH · canonical-location · config-exists · data-dir-exists, each in isolation, **plus** the negative case (nothing present → banner shown). `OPENCODE_CONFIG_DIR` / `OPENCODE_DATA_DIR` relocation honoured by every probe. Desktop probe does **not** false-positive on a CLI-only install. `InstallCommandViewModel` per platform emits the right command text and the right launcher kind (terminal vs browser). `ResolveCommand` shim-wrapping for `opencode.cmd` on Windows. **Update checker against a mixed release list**: each app resolves *its own* newest release by tag prefix, ignores the other app's newer release, and still resolves legacy unprefixed ClaudeForge tags. | Mirror `tests/ClaudeForge.Tests/ViewModels/UpdateBannerViewModelTests.cs` and the `AboutEditorViewModel` install-panel tests. The false-positive cases are the ones that matter — ClaudeForge already shipped and fixed one (`%APPDATA%\Claude\` left behind by the uninstaller). |
| **Search / nav / deep links** | `BuildSchemaSearchProviders` yields one provider per registered section. OpenCode synthetic triggers fire on the expected phrases (including the gotcha phrasings). `NavigationNodeIdTests` passes for **both** apps' trees. `NavDeepPath` round-trip through the STRING form for every new page; captured item keys contain **no** path separator. `ApplyNavigationFilter` raises `FilterFromNavigation` (orange frame) while a direct `FilterText` write does not. Each new `IDeepNavigable` page: tab selected first · `Locate` doesn't enter edit mode · missing item returns `false` but still selects the tab. Per-app `WindowStateService.StatePath` — assert OpenCodeForge never writes under `~/.claude/`. | Templates: `AgentsSkillsDeepPathTests`, `NavDeepPathTests`, `AgentsSkillsFilterTests`, `DeepPathReloadTests`. The `StatePath` assertion is new and worth having — it is the kind of leak nobody notices until a user complains. |
| **Plugins** | `Config.plugin[]` round-trips **both** union arms (bare string and `[name, options]` tuple) without coercing one to the other. Local plugin discovery across both roots, with shadowing. **Event-scan**: a plugin exporting `tool.execute.before` + `session.idle` reports exactly those; an unparseable file reports "could not parse", never a wrong list or a throw; an unrecognised export is labelled *not a recognised event* rather than dropped. **Scaffold**: generated stub compiles-shaped (valid TS syntax), lands in the chosen root, and its events round-trip back through the scanner. **Append-handler** adds at end of file and leaves every pre-existing byte untouched — byte-compare the prefix. | The event scan is static-only — add an explicit test that no plugin file is ever executed or imported. The prefix byte-compare is the guard for the never-restructure-user-code rule. |
| **Profile-readiness** | `DiscoverFiles(projectRoot, profileName: "x")` produces paths rooted under the profile, not the live root — even with no UI supplying a name. No resolved config-root path is cached in a `static readonly` field (reflection scan, mirroring the `WindowStateService.StatePath` invariant). | One test each. They exist to stop the seam rotting into a no-op. |
| **Danger indication** | `IDangerClassifier.Classify(path, scope, value)` — a test per danger-table entry (safe value → not dangerous, unsafe → dangerous). **Scope-sensitive entries assert both scopes** — `provider.*.options.apiKey` is caution at global and critical at project. **Coverage test: every schema key appears in the danger table exactly once** (guard 2), so a schema refresh that adds a key fails until it is classified. **Save-preview flags a change that *raises* danger** (guard 3). **Dual-coding scanner** rejects a severity indicator bound to a brush with no glyph (guard 4). **No-raw-hex build tripwire** (guard 5) — fails on all four current literals until migrated. **Both apps** resolve severity through `AppSeverity*` tokens, verified in light and dark against the two contrast budgets; a regression test asserts no severity literal exists in either app's view-models. | Guard 1 (non-nullable severity) needs no test — it's a compile error. The coverage test is what keeps the table honest as OpenCode's schema moves. Write the **Claude** danger table in the same pass: it proves the mechanism is product-neutral, and ClaudeForge's own settings tree has no severity today. |
| **Docs** | The dead-string guard and dynamic-access tripwire pass with the split resx sets. `AxamlAccessibilityCoverageTests` scans both apps. No doc references a symbol that no longer exists — a light grep-based check over `AGENTS.md`'s cited identifiers would catch the class of staleness the repo has already hit twice. | Docs are per-phase definition-of-done (decision 12), so there is no end-of-project doc sprint to slip. |
| **Diagnostics windows** | Ingest ordering and burst coalescing; `MaxLines` cap holds; enqueue from a non-UI thread does not throw; **`provider.*.options.apiKey` and `auth.json` contents never reach the tail window**; both windows close cleanly on shutdown without leaking. | Extend the existing 47 tests in `LayeredEditors.Avalonia.Diagnostics.Tests` rather than starting fresh. The redaction assertion matters most — a live-log window is exactly where a screen-shared secret leaks. |
| **Agents / commands inline JSON** | `Config.agent{}` round-trip for all 15 fields incl. the `color` hex-or-theme union and the deprecated `tools`/`maxSteps`. Nested `permission` binds the shared editor and produces *global → agent override* in the effective view. The 7 built-in names resolve even when absent from config. `Config.command{}` requires `template`. | The nested-permission case is the one most likely to break — it crosses Phase 6 and Phase 9. |
| **Providers / model picker** | Suggestions built from `provider.<id>.models` alone (no network) · `enabled_providers` allowlist narrows, `disabled_providers` blocklist removes, per-provider `whitelist`/`blacklist` narrow further · a model pinned to a disabled provider produces a **warning, not a block** · `provider.*.options.apiKey` is redacted by **both** classifiers (explicit parity test for that exact path) and never appears in a tooltip or the save-preview diff · external `$ref` handling per S11, including the offline case. | The apiKey path is caught today only by the substring pass — assert it rather than assume it. |
| **Debug flags** | Per-app `DebugFlagsTests` for every new flag: set / default / `ResetForTesting` clears it / appears in `ListActive()` / appears in `--debug-help`. Two-token flags (`--schema-source`, `--opencode-config-dir`, `--simulate-opencode-version`, `--rules-semantics`) additionally cover missing-value, invalid-value, and value-then-next-flag. Shared-vs-per-app registration: a shell flag and an app flag parse in one pass and both show up in help. `Initialize` emits **no** `Log.*` calls (it runs before Serilog is configured) — assert warnings land in `_deferredWarnings`. | Mirror `tests/ClaudeForge.Tests/Services/DebugFlagsTests.cs`. |
| **Accessibility** | `AxamlAccessibilityCoverageTests` must scan **OpenCodeForge's** `Views/*.axaml` too — every interactive control needs `AutomationProperties.Name`. Extend the scanner's project list rather than copying it. | Otherwise the new app ships with zero screen-reader coverage and no failing test to say so. |
| **Localization** | `LocalizationParityTests` extended across the split resx sets; dead-string guard and dynamic-access tripwire made project-aware; full 9-locale parity for every new key. | |
| **Layering** | `Assembly.GetReferencedAssemblies()` guard: no `AgentForge.*` references `ClaudeForge.*` or `OpenCode.*`, plus the positive edges in the dependency graph. | New gate; would have caught the existing `LayeredEditors.Avalonia.Services` violation. |
| **Parameter count** | Reflection guard over public constructors and methods: **fail above 6 positional**, as a **ratchet baseline** — seeded from the 12 current violations, new declarations implicitly capped at 6, fixes decrement the entry, and a missing baselined type fails loudly ("no longer exists"). | Copies `AxamlAccessibilityCoverageTests`' four-property ratchet exactly. The rename-detection property matters most here, since this plan renames a great many types. |
| **Public surface** | Extend the `PublicSurfaceContractTests` pattern to `AgentForge.Sdk` / `.Permissions` / `.Artifacts` — these are now genuinely shared libraries and accidental surface changes should fail. | |

### Two harness rules that apply to every new test

1. **Never `return Session.Dispatch(async () => …)`** — it binds `TResult = Task`, the
   framework awaits only the outer task, and the test **cannot fail**. 19 pre-existing
   tests are inert for this reason. Write plain `async Task` tests constructing the
   view-model directly (`NavigationHeaderClickTests`, `DeepPathReloadTests` are the good
   templates). **Canary every new headless test with a temporary `Assert.Fail` at the top
   of the body** — if it still reports Passed, it is inert.
2. **Sandbox every path-touching test** with the `PlatformPaths.TestUserProfileOverride`
   `[TestInitialize]` / `[TestCleanup]` pair from `AGENTS.md` §3, and add an
   `OPENCODE_CONFIG_DIR` / `OPENCODE_DATA_DIR` equivalent override so OpenCode tests never
   read the developer's real install.

### Rough sizing

Scaled from comparable existing areas (Claude's permissions ≈ 200 tests, Env ≈ 23,
Backup ≈ 150, editors ≈ 2 × per compound editor plus shape tests):

| Area | Estimate |
|---|---|
| `AgentForge.Jsonc` | 120–180 (fixture-heavy) |
| `OpenCode.Sdk` (discovery, merge, lifecycle, JSONC, validation) | 150–200 |
| OpenCode permissions (own candidate · resolver · collisions · tester · grid) + rules resolution | 170–220 |
| `AgentForge.Artifacts` (static→instance conversion + new behaviour) | 100–150 |
| Compound editors + Essentials + views | 100–140 |
| Schema handling / provenance / guards | 40–60 |
| Detection / install / update + search / nav / deep links | 70–100 |
| Providers / model picker + debug flags | 50–70 |
| Plugins (union + discovery + event scan + scaffold) + inline agent/command editors | 70–110 |
| Diagnostics windows + profile-readiness guards | 25–40 |
| Keybinds editor + credential status view | 40–60 |
| Danger tables + predicates + surfaces + guards (both products) | 80–115 |
| **Total new** | **≈ 1,015–1,445**, taking the suite to roughly **3,530–3,960** |

Plus **~19 rewritten** (the inert headless tests, decision 9) — those don't add to the
count, they make an existing part of it real.

Extraction phases (1, 3–6, 10) should add **near zero** — their correctness proof is that
the existing count passes unchanged.

---

## Human regression testing — sparing and targeted **[NEW]**

Automation covers most of this. These checkpoints exist for the things it provably cannot:
real rendering, real theme tokens, real filesystem scale, terminal launching, and the
reload/relaunch experience. **Seven gates, 3–8 steps each — roughly 10 minutes per gate.**
Each targets invariants the repo has *actually* broken before, not hypotheticals.

### Gate A — after Phase 2 (`AgentForge.Jsonc` on the save path)
The single highest-consequence change in the plan. Runs against **ClaudeForge**.
1. Open a real `~/.claude/settings.json`, change one value, save. `git diff` (or a copy
   comparison) shows **the changed line plus the `"//"` stamp line** — no reflow, no
   reordering, nothing else. *(The stamp is timestamped; see Problem 4. If option 2 or 3
   was chosen there, expect exactly one changed line.)*
2. Save with **no** change pending → only the stamp line differs (or nothing, under
   options 2/3).
3. Hand-add odd formatting (tabs, blank lines between keys, CRLF) → edit → confirm all of it survives.
4. Confirm the `"//"` header stamp still appears once, not twice.
5. Save-changes dialog still lists the right files with the right per-property diff.

### Gate B — after Phase 4 (product model generalized)
Runs against **ClaudeForge**, with **both** Claude Code and Claude Desktop present.
1. Edit one setting in each product, save once → both files written, dialog lists both.
2. Search a term present in both schemas → results grouped under both product names.
3. Switch profiles → **no reload loop** (the `_suppressProfileChangeReload` invariant; watch
   for a repeating `[Profiles] After load` + `[Schema] Post-reload validation` pair in the log).
4. Simulate Desktop being absent — **rename its config directory**; there is no
   `--simulate-no-desktop` flag today (`--showinstallbanner` only forces the banner *on*).
   App loads cleanly with one section. *(If this gate proves useful, add the flag alongside
   `--simulate-no-opencode` in Phase 13.)*

### Gate C — after Phase 5 (shell extraction — highest risk)
Runs against **ClaudeForge**. This is the gate worth doing slowly.
1. Full nav walk: every top-level page opens, renders, and has its accent pill and icons.
2. `F5` **Reload Window** from a deep position with an **unsaved edit in progress** → returns
   to the same place *with the edit buffer intact*; file on disk unchanged.
3. Quit from an open item → relaunch → lands on the item, **not** in edit mode.
4. Trigger each status kind — success pill auto-clears, failure sticks until dismissed.
5. Deep-link filter shows the **orange navigated frame**; clearing it removes the frame.
6. Toggle light/dark; confirm no `SystemControl*Brush` regressions (missing/incorrect colours).
7. Screen-reader spot check on one page (Narrator/NVDA) — buttons announce real names, not emoji.

### Gate D — after Phase 8 (OpenCodeForge first runnable)
1. Launch with OpenCode **absent** → install banner appears, command text is correct,
   **Run** actually opens a terminal, **Copy** puts the right text on the clipboard.
2. Launch with OpenCode **present** → no banner; About page shows the detected
   `opencode --version` and the `autoupdate` value.
3. Launch with `OPENCODE_CONFIG_DIR` set to a scratch dir → the app edits *that* config,
   and the "Active config file" Essentials card names it.
4. Edit a setting at global scope, save, verify on disk.
5. Confirm OpenCodeForge wrote **no files under `~/.claude/`**.

### Gate E — after Phase 11 (Rules & access — the headline feature)
1. Seed a project `AGENTS.md`, a global `AGENTS.md`, and `"instructions": ["docs/*.md"]` →
   Rules tab shows real load order with globs expanded and shadowed files marked.
2. Reproduce the `OPENCODE_CONFIG_DIR` gotcha → the page **reports it as ignored** rather
   than silently reproducing the bug.
3. Define the same agent name in inline JSON *and* a markdown file → shadowing chain shows
   both, winner marked per S7.
4. Permissions: set `{"*": "ask", "bash": {"git *": "allow"}}` → dry-run `git push` and
   `rm -rf /` and confirm each explanation names the deciding rule.
5. Edit a skill that lives in `~/.claude/skills/` → confirm the *"shared with Claude Code"*
   badge, then confirm ClaudeForge sees the same edit.
6. Drop a plugin `.ts` exporting two known hooks into `~/.config/opencode/plugins/` →
   Plugins tab lists it with exactly those two events; corrupt the file → it says
   "could not parse" rather than throwing or listing a wrong set.
7. Scaffold a new plugin from the events checklist → file lands in the chosen root and
   round-trips back through the scanner with the events you picked.
8. Open the config-activity window (Shift+F12), then edit `skills.paths[]` → watch the
   artifact set re-resolve live. Confirm no secret values appear in either window.

### Gate E2 — after Phase 11.5 (danger indication)

Separated from Gate E because 11.5 is the **only late phase that modifies a shipped
ClaudeForge surface** — draft 9 wrongly folded these two checks into Gate E, which runs
before the feature exists.

1. **OpenCodeForge:** set `share: auto` → red severity + standing banner on the Essentials
   card, **and** on the settings-tree row, **and** in the effective view.
2. Set `provider.x.options.apiKey` at **project** scope → escalates to critical with a
   "this file is committed to git" explanation, and the save-preview flags it *before*
   writing. Repeat at global scope → caution, not critical.
3. Confirm every indicator carries a **glyph**, not colour alone, in light and dark.
4. **ClaudeForge regression:** re-check its Essentials page after the hex→token migration —
   all four severity tiers render identically to before, light *and* dark. This is the one
   place the migration can damage a shipped app, and no automated test covers "looks the
   same".

### Gate F — after Phase 15 (packaging), before any release
1. Install each app from its own artifact on a clean VM; both run side by side.
2. Confirm two distinct winget identities, icons, and Start-menu entries.
3. **Release the *other* app, then check the first app's update banner** — with prefixed
   tags it must ignore the sibling's newer release. This is the monorepo trap; it cannot be
   caught by installing one app alone.
4. Linux: extract the tarball, run `linux-setup.sh`, confirm the `.desktop` entry and icon
   resolve **for each app independently** and neither overwrites the other's.
5. Confirm each app's smoke gate asserts *its own* startup log token, not `"Starting ClaudeForge"`.
6. Backup from OpenCodeForge → open the archive and confirm `auth.json` is **absent**.
7. Verify each published archive contains a non-empty `Schemas/` folder (the Phase 1
   rename trap, re-checked at the last possible moment).

**Also run the existing manual plan.** `docs/NAV-DEEP-LINKING-TEST-PLAN.md` is the
established format and its **G1 virtualization** scenario is still unverified by hand —
Gate C is the natural moment to finally close it, and Spike S6 makes the TUI keybinds page
the more demanding version of the same check.

---

## Verification

**Per phase:**
```bash
dotnet build ClaudeForge.slnx -c Debug --no-restore
```
Zero warnings (`TreatWarningsAsErrors` is solution-wide).

```bash
dotnet test ClaudeForge.slnx --no-build -c Debug
```
Baseline **2,512 test methods** across six projects — 1,279 `ClaudeForge.Tests`,
574 `Core.Tests`, 421 `Sdk.Tests`, 149 + 47 + 42 elsewhere.

**Phases 1, 3, 4, and 6 must not change this count** except by renames — a behavioural
change in an extraction phase means the extraction was unfaithful.

**Phase 5 is the stated exception** (draft 9 wrongly included it in the no-change rule).
Un-inerting the 19 headless tests will surface real failures, and fixing them legitimately
adds tests. Expect the count to *rise* there. What must not change is the count of
**passing** tests going down, or any existing test being weakened to accommodate the move.

```bash
pwsh src/publish/publish.ps1 -Rids win-x64
```
Zero IL2026 / trim warnings — required because the shell extraction moves AXAML across
assembly boundaries, exactly where `x:DataType` and source-generated-JSON invariants break.

**New gates this plan introduces:**
- **Layering guard** — no `AgentForge.*` assembly references `ClaudeForge.*` / `OpenCode.*`.
- **JSONC byte-stability** — load a commented, hand-formatted file, change one scalar,
  save; assert every byte outside the changed span is identical. Run against
  ClaudeForge's own `settings.json` fixtures too.
- **Schema-shape guards** — `config.json` builds 36 top-level nodes; `tui.json` builds 13.
  Fails loudly when upstream restructures (mirrors `ModelCatalogSchemaParityTests`).
- **Merge-policy parity** — today's `MergeEngine` tests re-run through `ClaudeMergePolicy`
  unchanged, proving Phases 3–4 changed nothing for Claude.
- **Permission-extraction parity** — Claude's existing matcher/resolver tests pass
  unchanged against the extracted `AgentForge.Permissions`.
- **Artifact-extraction parity** — Claude's existing Memory / Agents-&-Skills tests pass
  unchanged against the extracted `AgentForge.Artifacts`.
- **Shadowing correctness** — seed one agent name across built-in / global-JSON /
  global-markdown / project-JSON / project-markdown and assert the resolver reports the
  right winner and the full shadowed chain, per S7.
- **Rule resolution** — assert the `OPENCODE_CONFIG_DIR` gotcha is *detected and
  reported*, not silently reproduced: with both an alternate-dir `AGENTS.md` and
  `~/.config/opencode/AGENTS.md` present, the page must show the alternate one as ignored.
- **Localization parity** extended across split resx sets, with both
  `Directory.Build.targets` guards made project-aware.

**Manual, end-to-end:**
1. Launch `OpenCodeForge` with OpenCode absent → clean empty state, no crash.
2. Edit `model` at global scope, save, confirm on disk.
3. Open a project with `opencode.json` → project badge overrides global.
4. **Hand-write a commented, oddly-indented `opencode.jsonc` → edit one value → save →
   `git diff` shows exactly one changed line.** This is the headline check for Phase 2.
5. `--deep-link opencode/permissions/bash` resolves (proves shell reuse end to end).
6. Backup → confirm `auth.json` is absent from the archive.
7. Open the TUI section's keybinds page → confirm it opens in well under a second.
8. **Artifacts:** add `"skills": { "paths": ["~/my-skills"] }` to the config → confirm the
   Skills tab picks up that folder **without a restart of the resolver**, and that a
   same-named skill in `~/.claude/skills/` is badged *"also visible to Claude Code"*.
9. **Rules:** with a project `AGENTS.md`, a global `AGENTS.md`, and
   `"instructions": ["docs/*.md"]`, confirm the Rules tab shows the real load order,
   the expanded glob matches, and any shadowed file marked as such.
10. **Re-run ClaudeForge's own smoke path** — the regression risk lives here, not in the
    new app.

**Beware the harness trap:** per `AGENTS.md`, `return Session.Dispatch(async …)` makes a
headless test pass unconditionally; 19 pre-existing tests are inert for this reason. Canary
every new headless test with a temporary `Assert.Fail`.

---

## Out of scope for v1

- OpenCode **profiles** — no analogue exists upstream. **But the code stays profile-ready**
  (see Profile-readiness) so adding them later is additive, not a refactor.
- **Rewriting existing plugin source** — scaffolding and appending only; never restructure
  code the user wrote.
- **TypeScript syntax highlighting** in the plugin source editor. Plain text for v1.
- **Credential *management*** — editing, adding, or storing provider keys. Declined by
  design. A read-only credential **status** view *is* in scope (decision 7): presence and
  origin only, values never rendered anywhere.
- **Fetching remote artifact sources** — `skills.urls[]`, remote `instructions[]` URLs, and
  git `references{}` are listed with their origin and explained, never retrieved (decision 3).
- ~~Un-inerting the 19 known-inert headless tests~~ — **moved into Phase 5** (decision 9).
- **Porting ClaudeForge's `model-catalog.json` to OpenCode.** Its model↔effort↔auto-mode
  relationships have no OpenCode equivalent — OpenCode has no `effortLevel`; it has
  per-agent `temperature` / `top_p` / `steps`. **A model *picker* is in scope** (see
  Providers and models) — it is the *relationship catalog* that is not.
- **Fetching models.dev.** The offline, config-sourced suggestion tier ships in v1; the
  remote catalog tier reuses the Phase 13 provenance machinery afterwards.
  *(Context to verify, not to rely on: reporting says Anthropic blocked OpenCode's access
  to Claude models in early 2026 and OpenCode removed Anthropic references — if so, a
  Claude-centric catalog would be actively wrong there anyway.)*

---

## Risks

1. **Phase 5 (shell extraction) destabilizes ClaudeForge.** 4,797 lines, eight-plus
   documented cross-file invariants. Mitigation: slice the extraction; full suite green
   between slices; never bundle behaviour changes with moves.
2. **Rename churn hides a real bug — and the obvious mitigation does not work.** A 300+ file
   mechanical diff is unreviewable by eye, and "the suite passes, so a pure rename is
   correct by construction" is **false here**: embedded-resource logical names derive from
   `<RootNamespace>`, and four sites hardcode that namespace as a *string literal* the
   compiler cannot check. The worst, `BackupEngine.BundleSchemas`, would bundle zero
   schemas — after which `RestoreEngine` **silently skips** restore-time validation, with no
   error and (today) no test to catch it. Mitigation: the explicit string-literal checklist
   and three new tests in Phase 1; grep strings, not identifiers; prefer
   `typeof(X).Namespace` over re-hardcoding.
3. **The TUI schema is a performance trap.** `keybinds` declares **184 actions**, each an
   `anyOf` over `false | "none" | string | {name,ctrl,shift,meta,super,hyper} | …`. This is
   the exact shape of the documented `env` incident — ~305 declared vars built 306
   `PropertyEditorWrapper`s and took ~4.4 s. Mitigation: apply the documented lazy gate
   (`ObjectPropertyEditorViewModel.VisibleChildren` + `PropertyCategoryViewModel`) and
   build a purpose-built searchable keybind editor rather than 184 generic wrappers.
   Spike S6 measures it before any UI is written.
4. **`AgentForge.Jsonc` is new code on the save path — the highest-consequence code in the
   app.** A bug corrupts user config files for *both* products. Mitigation: land it in its
   own phase behind the byte-stability test; property-test round-trips over a corpus of
   real config files; keep the existing re-serializing writer available behind a debug flag
   for one release.
5. **OpenCode's schemas are young and move.** Mitigation: schema-shape guard tests, plus
   **bundled-first loading** (memory → bundled+overlay → disk → HTTPS) means an upstream
   change cannot break a shipped build at all — the bundled copy is authoritative until a
   refresh PR lands or the user opts into a fetched schema. The trade is staleness, not
   fragility, which is why Phase 13 adds the explicit refresh.
6. **Double maintenance, permanently.** Two apps, two winget identities, two release
   cadences. This is the real cost of the two-app choice and it does not go away.
7. **Reuse estimates in this plan are systematically optimistic — treat every unverified
   "shareable" claim as a hypothesis.** The permissions assessment was revised **three
   times** across drafts 1, 10, 11, and 12, each revision triggered by actually reading an
   implementation rather than its name, interface, or doc comment. The same failure produced
   the `SchemaTreeBuilder` union claim, the `AdditionalDirectoriesResolver` claim, and the
   `IEditorSchema.Metadata` carrier. **Mitigation: before committing to any phase that
   claims code is shared, read the bodies.** The pattern is that types named for a general
   concept (`PermissionCandidate`, `AdditionalDirectoriesResolver`) frequently encode a
   specific product's taxonomy — this codebase's naming is aspirational in places, and the
   plan's cost estimates inherited that optimism. Expect the true shareable fraction to be
   lower than any figure here that isn't backed by a cited member.

   **A recurring shape worth naming, because it now accounts for six separate findings.**
   This codebase repeatedly models a product-varying dimension as a **closed C# enum**. The
   full sweep of `Core` + `Sdk` (19 enums) yields six that must become per-product data:

   | Enum | Values | Why it varies | Persisted? |
   |---|---|---|---|
   | `ConfigScope` | 4 | OpenCode's ladder is longer and differently named | indirectly |
   | `ConfigFileType` | 5 | Claude file kinds | no |
   | `UserMemoryCategory` | 10 | doc says *"The set of categories is closed"* | no |
   | `FootprintCategory` | 7 | zero overlap with OpenCode's data dirs | no |
   | **`BackupMode`** | 3 | values survive but their *meaning* is defined in Claude paths (`~/.claude.json`, `~/.claude/projects/`) — each product must supply what each mode includes | **yes — string in `manifest.json`** |
   | **`EditableMemoryScope`** | 3 | `Plugin` means `~/.claude/plugins/`; OpenCode's plugin notion differs, and a `Shared` value is needed | no |

   The remaining 13 are genuinely generic (`SchemaValueType`, `EditorValueType`,
   `ClientChangeKind`, `ChangeKind`, `DialogCategory`…) or legitimately Claude-only
   (`HookCommandType`, `MarketplaceSourceKind`, `PermissionDefaultMode`…).

   **Scoping heuristic: grep for `enum` in an area before estimating it.** An enum is the
   reliable tell that a seam was never intended, and it converts "parameterize this" into
   "change the data model and every consumer." Two of the six are also **persisted**, which
   converts it further into "and migrate existing files."
8. **The plan's own size is the largest schedule risk.** 16 phases, ~9 new assemblies, and
   ~1,000 new tests, with **no user-visible value until Phase 8**. A long value-free stretch
   is where side projects die. Mitigation: Phases 1–4 each have standalone value even if the
   effort stops (neutral names, generalized scopes, N-product model, a formatting-preserving
   writer that improves ClaudeForge on its own); the stated abandonment point is Phase 5;
   and if appetite is smaller, the third-product-section alternative is a fraction of the
   work.
9. **OpenCode's artifact and rule semantics are version-dependent and partly undocumented.**
   v1 and v2 docs actively contradict each other on rule resolution, and at least one
   documented behaviour (the `OPENCODE_CONFIG_DIR` global-`AGENTS.md` skip) is an upstream
   bug rather than a design. Mitigation: S7–S9 resolve semantics against the *installed*
   version via `ProductVersionProbe`; the resolver is version-gated; anything unverified is
   *displayed as unverified* rather than asserted. A config editor that confidently states
   the wrong resolution order is worse than one that says it isn't sure.
