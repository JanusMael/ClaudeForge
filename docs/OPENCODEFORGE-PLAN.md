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
> ### Implementation status — 2026-08-27 (session 13)
>
> Branch **`feat/agentforge-opencodeforge`** @ **`d528997`**, working tree **clean**.
> **105 commits ahead of `origin/main` and 5 BEHIND it.** Suite: **3,731 passed · 11 skipped ·
> 0 failed · 0 warnings** across 12 test projects; the trim gate publishes clean for **both** apps.
> Every commit this session was checked into a throwaway `git worktree` and built in isolation.
>
> ⛔ **22 commits are LOCAL ONLY and NO PR HAS EVER BEEN OPENED for this branch** (confirmed via
> `gh pr list --head`). Pushing is two-party — the dev machine's credential is read-only on this
> repo — so **CI has seen none of this**; do not report the branch as CI-green at HEAD until a run
> says so.
>
> ⚠ **`origin/main` MOVED to `befedb0` and this branch is 5 behind.** Two dependabot bumps
> (`Microsoft.Maui.Essentials` 10.0.90→10.0.100, `Bennewitz.Ninja.AutoVersioning`
> 2026.3.701→2026.3.819) plus **`3c7aaab feat(models): add Claude Fable 5.1, demote Fable 5 to
> legacy`**. Those touch the model catalog, enum descriptions, the overlay schema and one csproj —
> **no overlap with this branch**, so no conflict is expected. Not rebased or merged: two-party call.
>
> ✅ **Phase 11c HAS NOW BEEN SEEN RUNNING**, and the per-kind inverted precedence sentence is
> verified on screen in **both** directions — agents and commands merge with their lower copies
> LIVE and win at *global*; skills override and win at *project*. Also confirmed live: the chain
> expander, all three skill issue sentences, cross-tool badges, the filter and its counts, both
> directory spellings, and that a 130-row skills tab scrolls to its last row. **A green,
> trim-clean page still had four defects** — see "What running the UI keeps finding" below.
>
> ✅ **Phase 11.5 — COMPLETE.** Four surfaces, six guards, `docs/DANGER-TAXONOMY.md`, and the
> no-raw-hex tripwire.
>
> **Slice 1 (`c1dbb4f`) — severity became a type.** The `AppSeverity` enum in
> `LayeredEditors.Abstractions`, four per-variant `AppSeverity*Brush` tokens in **both** apps,
> `AppSeverityToBrushConverter`, and ClaudeForge's Essentials cards migrated off
> `string severityColor` (`Color.TryParse` and its grey fallback deleted).
>
> **Slice 2 — the classifier and OpenCode's table.** `DangerAssessment` +
> `IDangerClassifier` (abstractions), the neutral `TableDangerClassifier` matcher (shell), and
> `OpenCodeDangerTable` (app) covering **all 36 `config.json` keys and all 13 `tui.json` keys**
> plus 30 nested refinements. Scope escalation is live: an `apiKey` is Caution at Global and
> **Critical at Project**, because a project file is committed to git. 85 tests, 7 canaries.
> ⛔ **The carrier is NOT `IEditorSchema.Metadata`** — see the correction below; `Metadata` had
> zero consumers, and neither the scope escalation nor the value predicates can be expressed as a
> static per-property annotation. `AppSeverity`'s own remark citing `Metadata` is corrected too.
> **Slice 3a — the settings tree, the first surface, SEEN RUNNING.** Every property row in
> OpenCodeForge now carries a dual-coded severity dot (`▲ ◆ ● ○` + colour) and, only when the
> value held is the unsafe one, a standing banner. `PropertyEditorViewModel` gained
> `Danger`/`HasDangerSeverity`/`IsDangerNow`/`DangerAccessibleText` recomputed on value change,
> scope change and reset; the classifier is attached at ONE choke point in
> `OpenCodeEditorFactory.Create`, and `HostedSection` carries it **per document** so `tui.json`
> cannot be labelled with `opencode.json`'s policy. 22 tests, 5 canaries.
>
> ⛔ **Two defects the screenshot found that the green suite could not.** (a) The banner used
> `LE.DangerText` — a flat theme-neutral `#C62828` that lands near **3:1 on the dark surface**,
> under the 4.5:1 this plan requires; it now uses the themed `AppSeverity*Brush` pair, which also
> makes a Caution-tier problem read amber instead of falsely red. (b) The banner stretched the
> full row width for one short sentence, reading as a page-wide alert.
>
> ⛔⛔ **`AutomationProperties.Name` is IGNORED on a `TextBlock` — its `Text` always wins.**
> Measured via UIA. So the severity dot announced "▲" and nothing else; `AutomationProperties.HelpText`
> is what carries the sentence (and is already this file's own convention for descriptions).
> A bare `Border` and a `ContentControl` both got **no peer at all**, which made the dot
> *invisible* to assistive tech — worse than the glyph. Note this also means the repo's 15
> `AutomationProperties.Name="{Binding DisplayName}"` on TextBlocks are no-ops that nobody noticed,
> because their `Text` already equals `DisplayName`.
>
> ⚠ **This surface is OpenCodeForge-only, by construction.** The two apps use DIFFERENT wrappers —
> OpenCodeForge takes the shared `LayeredEditors.Avalonia` one, ClaudeForge has its own copy under
> `src/ClaudeForge/Controls/`. ClaudeForge gets the dot when its own danger table lands.
>
> ⓘ **Nested children of an object editor show no dot** — they render through the object editor's
> own template rather than the wrapper header, so they never reach this code.
>
> **Slice 3b — search hits, the second surface.** A severity dot on every search result, in
> **both** apps' templates, so a search lands on a knob already labelled. 21 tests, 7 canaries.
>
> ⭐⭐ **The dot is not classified at the search site — the hit ASKS THE EDITOR.** New
> `IDangerAnnotatedEditor.AssessDanger(path)` (in `LayeredEditors.ViewModels`, beside
> `IChildEditorHost` and for the same reason: `PropertyEditorViewModel` implements it and the
> library cannot reference the shell). The obvious alternative — hand `SearchViewModel` a
> classifier — **quietly reclassifies**: search holds neither the editing scope nor the value, and
> the assessment is a function of both, so the hit would contradict the row it navigates to. The
> assessment returned is the row's own instance, so agreement is structural rather than tested-for.
> `SettingsGroupEditorViewModel` implements the same interface by delegating to its `Editors`.
>
> ⓘ **`null` and `Unremarkable` are different answers** and the distinction is load-bearing: `null`
> means "this editor does not render that path, keep looking", which is what lets the group editor
> stop at the first owner. Collapsing them makes the walk stop at editor #1 and call every later
> path safe. ⭐ The descent into nested paths goes through `IChildEditorHost`, **never a concrete
> object-editor type** — there are two `ObjectPropertyEditorViewModel` classes and the app's does
> not derive from the library's, so a type test covers half the object editors in play.
>
> ⛔⛔ **A REAL DEFECT FELL OUT OF SLICE 2, and it made the headline escalation inert.**
> `IsGitCommittedScope` compared a scope id ordinally against `OpenCodeScopes.Project`
> (`"Project"`), but what arrives is an `IEditorScope.Id`, which `ConfigScope.Id` produces by
> **lower-casing** the rung name (`"project"`). So a plaintext API key in a git-committed project
> file rendered **Caution amber instead of Critical red** in the running app. ⚠ **All 85
> slice-2 tests stayed green** because they build their own scope from that same constant —
> tautological with respect to casing. Fixed to `OrdinalIgnoreCase`; new
> `ScopeEscalationRealScopeTests` compares against `ConfigScopeAdapter`, the object the app
> actually hands the classifier. *Canary C4 restored `Ordinal` and reddened only the new test
> while every table-level test stayed green — the asymmetry is the proof.*
>
> ⚠ **And the escalation is still unobservable on every SURFACE**, for a different reason: its
> only escalating key is `provider.*.options.apiKey`, a wildcard segment under an open-ended
> provider dictionary, so no concrete schema node exists for it and it can never become a settings
> row or a search hit. The policy is now correct; nothing renders it. Worth a decision when
> Claude's table lands, since `.claude/settings.json` is committed too.
>
> **Slice 4 — Claude's own danger table, and ClaudeForge's settings tree.** `ClaudeDangerTable`
> (`4a`) plus the wiring and the dot in ClaudeForge's own wrapper (`4b`). 45 tests, 12 canaries.
> **Verified ON SCREEN**: amber ◆ on `autoMemoryEnabled` / `autoUpdatesChannel` /
> `cleanupPeriodDays` / `respectGitignore`, a dim hollow ○ on `verbose`, and — because the hit
> asks the editor — the *search* dot lit up in ClaudeForge at the same time, with
> *"Critical: Decides which tools Claude may run without asking you first."*
>
> ⚠ **142 top-level keys, not the "~25" this plan budgeted** — a 5.7× miss. Most of the growth is
> presentation, grouped under shared sentences rather than 70 variants of "this only changes what
> you see".
>
> ⭐ **Two conventions that keep the banner readable.** `Unsafe` fires only when the held value is
> SPECIFICALLY boundary-weakening (`bypassPermissions`, a sandbox off, a bare `*`, a secret-shaped
> name in `env`) — never merely because a powerful feature is configured, or every real
> installation carries a standing red banner on `hooks`. And a `disableX` / `allowManagedXOnly`
> key points the SAFE way, so it gets a tier and no predicate: its unsafe state is absence, which
> is also every untouched machine's default.
>
> ⛔ **Writing the escalation test exposed a no-op in this table's own first draft.**
> `EscalatesAt` was attached to three keys already pinned at Critical, where it escalates Critical
> to Critical and does nothing — invisible, and it still reads as a configured feature.
> **Escalation is only observable from a base tier BELOW Critical**, which the honest model wanted
> anyway: a secret in `~/.claude/settings.json` is plaintext on your own disk (Caution), the same
> secret in the committed `.claude/settings.json` is published to everyone with repo read
> (Critical). ⚠ **`Local` does NOT escalate** — `settings.local.json` is git-ignored, i.e. the
> file a machine-local secret is *supposed* to live in.
>
> ⛔ **`CreateDefault` deliberately does NOT default the table.** One factory type serves BOTH
> Claude products, and `NavigationTreeBuilder.BuildGroups` already builds one per section for
> exactly that reason — defaulting would hand **Claude Desktop** rows Claude Code's policy, silent
> for most keys and confidently wrong on any that collide (`env` is in both). Same hazard OpenCode
> avoided per-document. Desktop passes no table and shows no dots until it has one.
>
> ⚠ **`LooksSecret` matches word boundaries, not substrings**, because `MAX_OUTPUT_TOKENS` and
> `MAX_THINKING_TOKENS` are real Claude env vars the Essentials page writes itself. A substring
> test on "TOKEN" flags both, and a dot on a value the app set for you is the false positive that
> teaches people to ignore dots.
>
> ⓘ `mcpServers` is a **claude-desktop-config.json** key, not a settings key — the specialised MCP
> registration is live but for the other product's tree. Measured after a test asserted otherwise.
>
> **Slice 5 (`7222936`) — the effective view, DONE and seen running.** A Risk column on **both**
> of ClaudeForge's effective-value surfaces: the group editor's Effective tab
> (`GroupEffectiveView.axaml`) and the standalone Effective Settings page
> (`EffectiveSettingsView.axaml`). ⚠ **Two producers, not one** — the standalone page builds rows
> from `AllDefinedKeys()` through `EffectiveSettingsViewModel`, the group tab from `SchemaNodes`
> through the shell; they share the row type and nothing else, so a fix to one leaves the other
> silent. 16 tests, 10 canaries. Verified on screen: red ▲ on `enabledPlugins`/`hooks`/
> `permissions`, amber ◆ on `autoUpdatesChannel`/`cleanupPeriodDays`/`effortLevel`/`env`/
> `extraKnownMarketplaces`, dim ○ on `$schema`; glyph code points read back through UIA as
> **U+25B2 / U+25C6 / U+25CB**, so dual-coding is real and not three tofu boxes.
>
> ⭐⭐ **The effective row CLASSIFIES; it does not ask the editor — the opposite of slice 3b, for
> the reason that made 3b right.** 3b's rule was "do not classify with inputs you do not have":
> search holds neither the value nor an editing scope. An effective row *is* a (path, winning
> scope, winning value) triple — the three inputs classification takes — and they are **different
> inputs from the editor's**. A key that escalates in a git-committed file reads Caution while you
> edit it at User scope and Critical once a project file overrides it, so delegating would report
> the scope you happen to be editing and mislabel the runtime truth. The two dots disagreeing is
> correct; the column header tooltip says so, because otherwise it reads as a bug.
>
> ⭐ **The classifier comes off a new `ISchemaEditorFactory.Danger`**, not a second constructor
> parameter, so the Effective tab and the Properties tab of one page are the same instance **by
> construction**. Both factories already held the table; zero construction-site churn.
>
> ⛔ Classification uses `SchemaNode.JsonPath`, **never** the Property column's display title
> (`Title ?? Name`) — a title matches no rule, so passing it reports every row unremarkable while
> the column still renders and every other test stays green.
>
> **Slice 6 (`0f98645`) — the save-preview, DONE and seen running.** Every pending change carries
> a severity dot, and a headline appears when one writes a value the product calls unsafe.
> Verified live (dialog cancelled, nothing written): three toggles produced *"Saving 3 change(s)"*
> **and** *"▲ 1 of these changes set a value that weakens a safety boundary"* — one, not three,
> because only `respectGitignore=false` is the unsafe value. That gap between the two counts is
> the design.
>
> ⛔⛔ **The value is resolved from the document root, NOT from `PropertyDiff.NewValue`.** For an
> array change `JsonDiff` emits the ARRAY's path as the key but only the added ELEMENT as the
> value, so a rule written for `permissions.allow` would be handed one element's string, match no
> list pattern, and answer "nothing wrong right now" — a silent false negative on exactly the keys
> this dialog exists to catch. ⚠ The test for it had to be rewritten before it tested anything:
> the element-wise diff only fires when the key is an array on **both** sides, and a key absent
> from the baseline yields an `Added` row carrying the whole array.
>
> ⛔⛔ **The policy travels PER SOURCE (`DirtySource`), not as one parameter.** This dialog renders
> every open product at once, so a single classifier would label Claude Desktop's pending writes
> with Claude Code's threat model.
>
> ⭐ **`ProductSection` states the table once** and both consumers read it there — the settings
> pages via `BuildGroups`, the save dialog via `DirtySources()`. Two literals agreed only by
> vigilance before.
>
> **The tripwire (`797722c`) — DONE, and it found a real defect.** `GuardRawHexInViewModels` in
> `Directory.Build.targets`, a build error, modelled on `GuardUnusedResxKeys`.
>
> ⛔⛔ The literal it was written to catch was an **accessibility failure, not a style nit**:
> `SaveChangeEntryViewModel.KindBackground` returned `#F57C00`, giving its white `~` glyph
> **2.70:1** — under the 4.5:1 text floor and under even the 3.0:1 non-text one. The comment
> defending it reasoned purely about hue and measured nothing. Now `AppChangeKindModifiedBrush` =
> `#B45309`: keeps the hue argument (26°, not red's 0°), measures **5.02:1**.
>
> ⭐ **Scoping it to view-models dissolved the exception problem.** The earlier note here called
> for an opt-out property to spare ~30 legitimate literals; scoped to `*ViewModel.cs` /
> `ViewModels/` there are **zero** exceptions to carve out, because every one of those literals
> lives in a converter, control or service — colour *definitions* and resolution *backstops*. A
> view-model returning a colour is different in kind.
>
> **`docs/DANGER-TAXONOMY.md` — DONE.** The tenet, the tiers, the matcher, the scope-sensitivity
> rule, both products' tables (counted, not estimated), and the six guards.
>
> ### ⛔⛔ The surface ordering above is not buildable as written
>
> The plan calls the save-preview "the most valuable and the cheapest". It is currently
> **unreachable**, measured: **OpenCodeForge has no save dialog at all** (no `ISaveChangesPrompt`
> or `SaveDialogBuilder` reference anywhere in `src/OpenCodeForge/` or `src/OpenCode.Avalonia/`),
> and **ClaudeForge has the dialog but no danger table**. So the product with a table cannot show a
> save preview, and the product with a save preview has nothing to say in it. That is why slice 3a
> did the settings tree instead. ⚠ Also: `SaveDialogBuilder` is static with no product context
> (one production call site, `MainWindowViewModel.cs:1867`), and `PropertyDiff.OldValue`/`NewValue`
> are **JSON strings**, not the editor value currency — so that path needs
> `JsonCurrency.FromJsonNode` before any predicate can run on it.
>
> ⛔⛔ **The EFFECTIVE VIEW was unbuildable for the same reason, and the earlier handoff got this
> wrong.** *(Resolved by slice 5 — Claude's table landed in slice 4, which unblocked it. The
> analysis below is kept because it explains why the ordering had to change, and it still governs
> the save-preview.)* Session 14's anchor listed the effective view as the next buildable surface.
> Measured:
> the rows are produced in the **shared** shell (`SettingsGroupEditorViewModel.EffectiveRows` →
> `EffectivePropertyRow`), but the only thing that RENDERS them is
> `src/ClaudeForge/Views/GroupEffectiveView.axaml`, reached through ClaudeForge's own
> `GroupTabBodyTemplate`. **OpenCodeForge renders no tab strip at all** — its
> `SettingsPageHost.axaml` binds `GroupName`, `GroupDescription`, the scope selector and
> `FilteredEditors`, and nothing else. So OpenCodeForge computes effective rows nobody can see, and
> a severity column added there would be invisible in the only product with a table. (It does
> supply `TabEffective` text via `OpenCodeSettingsGroupText`, which is what makes this look wired.)
>
> ⭐ **So the ordering that survives measurement is: settings tree (3a) → search hits (3b) →
> Claude's own danger table (4a/4b) → then the effective view and the save-preview, both of which
> that table unblocks in ClaudeForge where the renderers already exist.** Search hits were
> buildable because `SearchViewModel`/`SearchResultViewModel` are shared **and both apps render
> results** (OpenCodeForge a `ListBox`, ClaudeForge a `Popup` + `ItemsControl`).
>
> ✅ **Slice 4 has landed, so both blocked surfaces are now buildable** — in ClaudeForge, against
> `GroupEffectiveView.axaml` and `SaveDialogBuilder` (one production call site,
> `MainWindowViewModel.cs:~1867`), which is where the renderers live. ⚠ The save-preview still
> needs `JsonCurrency.FromJsonNode` first: `PropertyDiff.OldValue`/`NewValue` are JSON strings,
> not the editor value currency the predicates take.
>
> ### What running the UI keeps finding — read this before trusting a green suite
>
> Session 13 ran both apps under UI Automation and found **six** user-facing defects that a
> 3,689-green, trim-clean tree was hiding. The pattern is consistent enough to plan around:
> **a guard that inspects markup cannot see a name or colour that arrives from a view-model or a
> theme dictionary at runtime.**
>
> | Defect | Fixed in | Why every existing guard missed it |
> |---|---|---|
> | `LE.DangerText` + `LE.DangerBorder` referenced 9×, declared 0× | `c7ea6fe` | Unresolvable `DynamicResource` is not a build error (**even under the trim gate**), not a runtime error, and logs nothing |
> | The artifacts page painted metadata RED and problems grey | `5435c18` | `LE.BoolDisabled` is `#C62828`; nothing asserts a token's *meaning* matches its use |
> | 5 artifacts tabs announced `…OpenCodeArtifactTabViewModel` | `5435c18` | `ItemsSource`-generated containers take their name from the ITEM |
> | Every OpenCodeForge property heading rendered unstyled | `6b8e9c1` | `App*Brush` is declared **per app**; a shared library referenced one ClaudeForge alone defined |
> | A folded `description: >-` read as the literal `">-"`, **and its continuation lines became phantom FIELDS** | `05fb560` | `">-"` is non-empty, so `SkillHasNoDescription` never fired — an unreadable description reported as healthy |
> | ClaudeForge's nav tree announced nothing; 6 settings tabs announced `…Settings.GroupTab` | `d528997` | `AxamlAccessibilityCoverageTests` did not scan `TreeView` **at all**, and scored the file a clean 0 |
> | Every search row announced `…Search.SearchResultViewModel` — and 3 more `ListBox`es did the same | slice 3b | Third container type in the same class; the two existing container guards cover `TabControl` and `TreeView` only. **4 of 4 ListBoxes were broken; the pattern is now 8 for 8.** |
>
> ⭐ **Both `ItemsSource`-bound TabControls in the repo were broken — a 100% hit rate.** New
> `ItemsSourceBoundTabsTests` fails any third one that forgets `ToString()`.
> ⭐ New `ThemeResourceIntegrityTests` closes the undeclared-token class in both directions.
> ⛔ `AxamlAccessibilityCoverageTests`' control list is now widened by six types and satisfied at
> **zero** everywhere — no baseline entries were added.
>
> ⓘ **Not yet audited:** ClaudeForge's nav `TreeViewItem`s are generated from
> `NavigationNodeViewModel`, so their names come from the item — the same `ToString()` question
> `GroupTab` just answered. And **the Essentials severity dots have never been seen on screen**:
> screen capture failed mid-session (`CopyFromScreen` → "The handle is invalid") because the RDP
> session became unattended. UIA still works unattended; pixels do not.
>
> ✅ **Phase 9a is COMPLETE as of 9a-10 (`keybinds`)** — the largest editor in the plan. Spike S6 is
> answered by measurement rather than estimate: the view realizes **5 of 184 rows** (11 after
> scrolling), because a virtualizing `ListBox` with a bounded viewport replaces what would have been
> 184 raw-JSON text boxes. ⛔ **Its exact-count guard found a real defect that had shipped in this
> editor**: two notification mechanisms — a direct callback *and* a `PropertyChanged` subscription —
> ran at both view-model levels, so **one keystroke raised `IsModified` twice and one mode change
> raised it seven times**, each running a full clash recompute plus a collection reset on the bound
> list. Fixed to one mechanism; see the `keybinds` row in the Phase 9 table.
>
> ✅ **PHASE 10 IS COMPLETE (10a–10c).** 10b landed both Claude surfaces on the artifact engine. The Memory inventory and
> the Agents & Skills editor resolve an ordered `IArtifactSource` list instead of walking
> directories, and **their 25 and 15 existing tests pass unmodified** — the faithfulness proof the
> phase asks for. ⛔ **The second consumer removed two of `IArtifactSource`'s four members**: one
> walk of the plugin tree yields three kinds from a different plugin scope at every level, so a
> source cannot be asked for "its" kind or scope. ⭐ Measured along the way: `*.md` never matches
> `reviewer.md.bak`, so the long-standing `.bak` exclusion only ever protected the `hooks` walk —
> and the test I first wrote for it was vacuous. **10c injects a path provider** and finds the
> plan's framing half wrong twice over: the root is the USER PROFILE (`~/.claude.json` and the
> cross-tool probes sit beside `.claude/`, not inside it), three of the nine members need no
> injection at all, and this section's own reference counts included **doc comments** — including
> 10a's claim that an enum file reaches for `CredentialsPath`, which it names only in prose.
> ⛔ `ClaudeArtifactPaths.Default` must be a property: caching it reddened **40+ tests** because
> the profile is `AsyncLocal` and the suite runs parallel. See the Phase 10 section.
>
> ✅ **Phase 11b is DONE too — the page's read model.** `OpenCodeArtifactSemantics`,
> `OpenCodeSkillManifest` and `OpenCodeArtifactInventory` turn the source list into grouped, ordered,
> diagnosed rows. ⛔⛔ **Its central finding: "shadowed" is a per-kind claim.** Skills' losing copies
> really are never loaded; **agents' and commands' are LIVE and contributing fields**, so a row saying
> "overridden" there tells the user to delete a file that is in force. See **Phase 11b** below.
> **11c is the UI.**
>
> ✅ **Phase 11a is DONE — the OpenCode artifact source list, the engine's first OpenCode consumer.**
> `OpenCodeProjectWalk`, `OpenCodeArtifactScopes`, `OpenCodeSkillArtifactSource` and
> `OpenCodeArtifactSources` in `src/OpenCode.Sdk/Artifacts/`, plus 20 tests, **all 20 canaried**.
> **No UI yet** and **no change to the shared engine** — see below for why the change I first made
> there was reverted. ⛔⛔ **Phase 11's source table was measured wrong in five ways, and the
> precedence error is the dangerous one: for agents and commands the GLOBAL directory outranks the
> project, the exact opposite of the intuition and of the skills rule.** Everything was re-measured
> against the installed **v1.17.9** binary — the same version the spikes probed — with
> `opencode debug config` / `debug skill` against a throwaway worktree. See **Phase 11** below for
> the corrected table.
>
> ✅ **9a-11 (`theme`) closed the phase's editor list with NO new editor at all** — the library's
> enum editor already is a picker-that-accepts-typing, so the slice is a schema overlay plus a
> schema wrapper. ⛔ Two plan claims measured wrong: the themes path is only one of three sources
> (the others are cwd-dependent or plugin-supplied), and the 37-name built-in theme list in the
> opencode binary belongs to the **web UI**, not the TUI — the TUI has no built-in table, which is
> why discovery exists. See the `theme` bullet.
>
> ⚠ **Pushed and CI-green through `de2525d` — TWO COMMITS ARE LOCAL ONLY:** `bb6218b` (a currency
> key-order fix) and `2508878` (docs). **CI has seen neither, nor any of the uncommitted work.**
> **Do not report the branch as CI-green at HEAD until a run says so.**
>
> ⚠ **No PR has ever been opened for this branch** — re-verified 2026-08-21 with
> `gh pr list --state all --head feat/agentforge-opencodeforge`.
>
> ⚠ **Pushing is a two-party operation.** The development machine's stored GitHub
> credential has **READ but not WRITE** on this repo and will not be changed, so commits
> reach `origin` via `git bundle` handed to a machine that can push. CI results *are*
> readable from the development side, so the split is: they push, the session analyses.
>
> | Phase | Status |
> |---|---|
> | 0 — Spikes | ✅ **10 of 11**; only **S5** (Desktop) open |
> | 1 — Rename + neutralize | ✅ **complete (1a–1h)** |
> | 2 — `AgentForge.Jsonc` | ✅ **complete** — library, wiring, `--writer legacy`, [`docs/JSONC-WRITER.md`](./JSONC-WRITER.md); smoke-tested against a real install |
> | 3 — Scope model | ✅ **complete** — `ConfigScope` is a struct, `ConfigScopeAdapter._cache` invariant retired. 4f then made the *ladder* the product's (`ScopeLadder`) and **kept the statics** — measured as 2 real edit sites, not 1,150 |
> | 4 — Product model | ✅ **complete (4a–4f)** — both `IsClaudeCode` booleans replaced, merge rules and the scope ladder are the product's own statements, the shell hosts a list of product sections, and an export names its products in a list at schema v2. One deferral stated explicitly: `ConfigFileDiscoverer` still knows only Claude's file layouts |
> | 5 — Extract the shell | ✅ **complete, 5 slices** — `AgentForge.Avalonia.Shell` holds `Status/`, `Navigation/`, `Search/`, `Save/`. **This was the plan's abandonment point and its trigger never fired**; all five slices reported in green. One piece **deferred, not rejected**: nav's tree assembly + `ProductSection` enrichment, measured at ~60 neutral lines for a rewrite of the 493-line `BuildNavigationTreeAsync`. Problem 8 was **restated, not solved** — see slice 5 |
> | 6 — Permission vocabulary | ✅ **complete (`a453063`)** — `PermissionOutcome` is neutral, `Default` is its zero value. **Three of the five drafted deliverables were rejected on measurement**, including `Decision<TRule>`, which does not describe any type that exists |
> | 7 — `OpenCode.Sdk` | ✅ **complete (7a–7g)** — schemas bundled and product-filtered out of archives, root-`$ref` fixed, and OpenCode's own SDK: products, five-rung ladder, per-key merge policy, two clients with scope discovery, and the permission model. **Seven corrections to this plan, all measured** — see the phase section. Two ladder rungs (Inline, Managed) deliberately undiscovered rather than guessed |
> | 8 — `OpenCodeForge` app | ✅ **complete (8a–8g)** — **the second app runs.** Own state path, own icon-less identity, wrapper localization wired, detection banner with per-distro install commands, search with OpenCode's gotcha phrasings, per-app release resolution. **8b was an unplanned ~1,900-line extraction**: the schema settings page lived in the ClaudeForge assembly, so no second app could render settings at all — the plan predicted only two static tables needed lifting. Also found **four headless tests that could not fail** and extended the trim gate, which covered only the first app |
> | 9 — OpenCode compound editors | ✅ **9a complete (9a-1 – 9a-11), committed locally 2026-09-07/08; ⛔ **UNPUSHED** — see the push note** — `OpenCode.Avalonia` is now in `ClaudeForge.slnx`, the app's `.slnf`, and referenced by `src/OpenCodeForge`, so **CI builds it for the first time**; the registration exposed unversioned `PackageReference`s that would have failed restore. **The v1 gate is met: the permission grid ships** — both shapes (bare action / tool × pattern), per-row reorder that makes last-match-wins visible, shadowed-rule detection, action-only-tool enforcement, an unparseable value echoed back untouched, and a live tester over `Resolve`. **9a-3 adds the `mcp` editor** — all **three** union arms (the plan described two), three-state `oauth`, and per-entry verbatim preservation of a shape this build cannot classify. `OrderedPropertyMap` (9a-1) landed because object key order was a `Dictionary` implementation detail and for the permission map the LAST match wins, so key order is the policy; 9a-3 moved it down to `AgentForge.Abstractions` so an SDK codec and a UI adapter share one copy. **Two guards added for silent-failure classes the repo documented but never checked**: every project on disk is in the solution, and every specialised editor has a `DataTemplate` (it caught `mcp` automatically, with no registration). **One plan claim measured false** — see the `mcp` bullet. **9a-5 adds the `agent{}` editor**, which hosts the permission grid as a real child editor (so nested rules get shadow detection, reordering and the tester with no second implementation) and implements `IChildEditorHost` so page filtering can descend into them. **9a-4 clears the two debts these editors were carrying**: `PermissionOutcomeToBrushConverter` moved to `LayeredEditors.Avalonia.Services` (Phase 6 left it; the home it feared was the app shell, but that project already carries both prerequisites and one consumer already references it, so the move costs a single light edge) and now has a second consumer plus its first tests; and `OpenCode.Avalonia` gained its own resx, with **Problem 8's guard half landed** — see Problem 8. **9a-6 adds `command{}`** — the phase's only `required` field (a missing `template` is reported, never invented) and a banner for templates that run a shell command through `` !`…` ``. **9a-7 adds `plugin[]` + the TUI's `plugin_enabled{}`** — one editor covers both products' identical `plugin` shape. **9a-8 adds `formatter` + `lsp`** — a shared **four**-state mode (absent / `false` / `true` / object, none foldable into another) over two *different* per-language shapes: the plan called them one shape and the schema disagrees three ways (an `lsp` entry is a two-arm union whose full arm **requires** `command`, its environment key is `env` not `environment`, and it carries an untyped `initialization`). It counts and names the entries that match neither arm — the state produced by unticking "disabled" on a disable-only server — and never repairs them. `disabled` is a **three**-state checkbox for the same absent-vs-`false` reason the mode picker exists. The `mcp` editor's argv and key/value list view-models moved to `OpenCode.Avalonia/Editing/` and now serve all three editors. ⛔ **A canary found a real defect in this slice**: the load path's explicit row-subscribe loop was decorative, because `CollectionChanged` was still attached during the rebuild and subscribed every row a second time — removing the loop broke nothing. Fixed by detaching the handler for the rebuild; guarded by a new reload-does-not-accumulate-handlers test. **9a-9 adds `autoupdate`** — the only `boolean | scalar` union in either schema (surveyed: 20 unions in the config schema, 924 in the TUI's), so it generalises to nothing; `"notify"` is matched `Ordinal` so the near-miss `"Notify"` is held verbatim rather than silently corrected. **9a-10 adds `keybinds` and COMPLETES 9a** — 184 actions, each a **four**-arm union nested three deep (the plan's `…` hid the two arms that decide the model: an inner three-way union, and an array of it). `"x"` and `["x"]` stay different files; `false` and `"none"` are not folded together; the literal `true` is offered nowhere because the boolean arm is `enum: [false]`. Search is the primary control and matches the action, its description **and its current binding** — the last is what answers "what is Ctrl+C bound to?". Cross-row clash detection names the other action and deliberately says **nothing** about which wins, since the schema states no precedence; a chord and a structured key are **never** reported as the same key, because equating them means inventing the chord parser the schema's pattern-less string arm refuses to define. Capture writes the object form only, with just the modifiers actually held. ⛔ **A real defect, found by the exact-count guard and shipped in this editor until now:** a direct callback *and* a `PropertyChanged` subscription both routed changes to the owner at both VM levels, so one keystroke reported **twice** and one mode change **seven times** — every `[NotifyPropertyChangedFor]` target is itself a `PropertyChanged`. Now one mechanism; an exclusion filter was rejected because it must name every derived property and forgetting one is silent. Two of the eight canaries also proved **my own tests vacuous** (a prefix-only clash canary could never collide; an all-modifiers-held capture case could not see a Ctrl regression) and one exposed a **dead assignment** that read as the mechanism. **Phase 9a: COMPLETE** |
> | 10 — `AgentForge.Artifacts` | ✅ **COMPLETE (10a–10c), committed locally 2026-09-07/08; ⛔ **UNPUSHED** — see the push note** — the engine resolves an ordered source list into one precedence-ordered chain per artifact, and **both Claude surfaces now go through it**: the Memory inventory and the Agents & Skills editor, with their 25 and 15 existing tests **unmodified** as the faithfulness proof. ⛔ **The second consumer deleted two of `IArtifactSource`'s four members** — one walk of the plugin tree yields three kinds from a different plugin scope at every level, so a source cannot state "its" kind or scope; a plugin is a SCOPE, not a source. ⭐ Entry names are identity, not display: a recursive `rules/` walk names `common/security`, a sibling tool's file is `.codex/AGENTS`, a skill is named by its directory — each coarser alternative would manufacture a shadowing relationship that does not exist. ⭐ Two TRUE relationships became expressible (user vs project `settings.json`; a user agent vs a plugin's) while both pages still list every entry in a chain, because they are browsable file lists rather than statements about who wins. ⚠ Measured: `*.md` never matches `reviewer.md.bak`, so the long-standing `.bak` exclusion only ever protected the `hooks` walk — and it made the first test I wrote for it vacuous. **10c injects a path provider**: rooted at the USER PROFILE (not `ClaudeHome` — `~/.claude.json` and the cross-tool probes are its siblings), with the statics kept as thin wrappers so the 40 existing tests stay unmodified. ⭐ The three project-scope paths need no injection at all — they are pure functions of a root the caller already passes — so the real surface is one root and eight derived paths, and three services now contain ZERO `PlatformPaths` code references. ⛔ `Default` must be a PROPERTY: caching it reddened 40+ tests, because the profile is `AsyncLocal` and the suite runs method-level parallel. A source-text seam guard keeps it from eroding, since a static property read leaves no per-type metadata to reflect over |
>
> **Phase 2 fixed a live data-loss bug the plan had only half-identified.**
> `ConfigFileLoader.LoadAsync` parsed with default `JsonDocumentOptions`, which **throw on a
> comment**; the throw was caught and turned into an *empty* `JsonObject`; the next save then
> serialized that emptiness over the file. **One comment, or one stray character, was enough
> to lose a config.** The plan predicted the OpenCode consequence but recorded "nothing is at
> risk today" — that was wrong for any Claude user who had ever hand-added a comment.
>
> ✅ **Since resolved:** Phase 2 *was* smoke-tested against a real install (2026-08-18) —
> both a headless harness over the maintainer's real 20 KB `settings.json` and a real GUI
> save through a throwaway profile. See the Phase 2 section.
>
> ### Verification status — every gate has now run
>
> For fifty-six commits the branch was deliberately local, so **CI had never executed on any
> of it** and CI's gates were reproduced locally instead. **Two separate first-ever runs each
> failed**, which is why this section exists rather than being an abundance of caution.
>
> **First: a local reproduction of the trim gate caught a Release-only break.**
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
> | **CI OS matrix — `windows`, `ubuntu`, `macos`** | ✅ **green @ `b0989c6`** (2026-08-19) |
> | **CodeQL** | ✅ **green @ `b0989c6`** — clean on first run |
>
> **Second: the first real CI run (@`8144fd4`) failed `Build & Test` on all three operating
> systems**, and both causes were real:
>
> 1. A doc cited `src/LayeredEditors.Avalonia/ViewModels` (the *namespace* shape) for a
>    directory that is `src/LayeredEditors.ViewModels`. **This branch introduced it in
>    `8834039`**, and an **empty untracked directory on the dev machine masked it for 56
>    commits** — git does not track empty directories, so the local guard is structurally
>    weaker than the identical guard in CI.
> 2. Avalonia's headless session was started **lazily by whichever test ran first**, which on
>    macOS threw a dispatcher-affinity error during platform start-up — reported against an
>    unrelated status-bar assertion. Now started once from `[AssemblyInitialize]`.
>    ⚠ **That fault was a race, so one green macOS run is evidence, not proof.**
>
> Both fixed in `29fd697`. What the remote gates cover that nothing local does:
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
| 10 | `AgentForge.Artifacts` — ✅ **DONE (10a–10c)** | nothing user-visible | ⚠ Memory page | — |
| 11 | Agents / Commands / Skills / **Rules** / Plugins | the headline feature | none | **E** |
| 11.5 | **Danger indication systematised** | severity everywhere incl. save-preview · 5 guards · **hex→token migration in both apps** | ⚠ touches shipped Essentials | **E2** |
| 12 | OpenCode Essentials | 17 pinned cards | none | — |
| 13 | Schema refresh (app + CI) — ✅ **DONE 2026-09-10** | in-app check + provenance badge, **in both apps**; OpenCodeForge also gained the About dialog and version button it never had | benefits both | — |
| 14 | Backup / Restore + footprint — ⛔ **"`auth.json` excluded" is NOT ENOUGH; see Phase 16** | archive + prune. ⛔ Secrets also live in **`opencode.db`** (`account`/`control_account` access+refresh tokens, `credential.value`, `session_share.secret`), which the planned JSON-key classifier **cannot reach**. ⚠ `auth.json` is absent here only because nobody has authenticated on this install — **keep excluding it** | none | — |
| 15 | Packaging — 🔶 **8 slices shipped 2026-09-10; only artwork + docs remain** | app descriptor for the publish scripts · icon + Linux integration · single-file artifact · own release workflow · winget `Bennewitz.Ninja.OpenCodeForge` · full app-update parity · `AssemblyProduct` | ⚠ release workflow, ⚠ shared update service refactored | **F** |
| 16 | Re-validate against a used install — 🔶 **PROBED 2026-09-10: 3 fully · 4 in part · 1 gate cleared · 2 open** | `scripts/probe-opencode.ps1` + a committed snapshot. ⛔ Still **not a used install** — every session table is empty, so 14's *quantitative* half stays blocked | none | — |

⚠ **Phase 16 is a real phase, promoted from a checkpoint on 2026-09-09**, and it is *blocked on
data, not effort*: every Phase-0 measurement was taken against an OpenCode install that had never
run a real session. It waits until usage accumulates. ✅ **The waiting is now instrumented** —
`scripts/probe-opencode.ps1` writes a committed snapshot whose `usage.isUsedInstall` flips from
`false` the moment there is history worth measuring, so nobody has to re-derive the answer.

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
- `ConfigScopeAdapter._cache` is an **array indexed by `(int)scope`** — a documented `AGENTS.md`
  hard invariant.
- `MergeResult` returns `ConfigScope?`; `LayeredValue.IsManagedLocked` hardcodes
  `== ConfigScope.Managed`.

OpenCode's ladder is different and longer: global → custom (`OPENCODE_CONFIG`) → project
→ inline (`OPENCODE_CONFIG_CONTENT`) → managed → macOS MDM.

**Solution.** A readonly record struct in `AgentForge.Abstractions`:

```csharp
public readonly record struct ConfigScopeId(string Id, int Priority, string DisplayName, bool IsReadOnly);
```

Each product declares an ordered `ScopeSet`. `ConfigScopeAdapter` collapses to a lookup over that
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

- `PermissionOutcome` — `Default` / `Allow` / `Ask` / `Deny`. Identical concept, ~10 lines.
  ✅ **Shipped in `a453063`**; `Default` leads so an uninitialised value is not a grant.
- ~~A **generic decision shape** — `Decision<TRule>(Outcome, MatchedRule, MatchedScope, Explanation)`.
  Genuinely reusable once parameterized on the rule type.~~ ⛔ **REJECTED ON MEASUREMENT
  (Phase 6).** The real `PermissionDecision` has six params — three Claude-only — and no
  `Explanation` at all; that field is computed in the tester. Reusable *in principle* is not
  the same claim as reusable *as written*, and this one was never read before being drafted.
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

> ⛔⛔ **The table above was written from prose and the schema disagrees with it in six places.**
> Measured 2026-09-04 against the bundled `opencode-config.json` (`$defs/Config`, 36 properties)
> while building slice 3. **Read this before building any card from that table.**
>
> | Row says | Schema / measurement says |
> |---|---|
> | #1 `permission` is "a bare string, or `"*"`" | `anyOf[PermissionActionConfig, per-tool object]`. **There is no `"*"` arm** — it does not exist. |
> | #2/#3/#4 are a 3-value enum | `PermissionRuleConfig` = `anyOf[action, pattern→action object]`. A plain enum card **cannot always represent them**; ordered per-pattern rules are a second shape. |
> | #5 is 🟠 | The danger table tiers `permission.webfetch` and `.websearch` **Critical**. |
> | #8 `plugin[]` is a string list | Items are `anyOf[string, [string, object]]` — a **tuple carrying an options object**. |
> | #9 `model` is 🔴 | The danger table tiers it **Caution**. |
> | #15 `default_agent` is a picker | Plain `string`, **no enum**. And only **`build`** and **`plan`** are offerable: probing `opencode debug agent` on v1.17.9 shows `general`/`explore` are `subagent`, `summary`/`compaction` are primary but `hidden`, and `title` **does not exist in the binary**. |
>
> ⭐⭐ **A card's severity must be read from the danger table, never written in `BuildCards`.** Both
> surfaces show a dot for the same key, and one literal per surface agreed only by vigilance —
> which had already failed: slice 2's `autoupdate` card said `Neutral` while the table says `Info`.
>
> ⛔⛔ **`permission` cards must interlock, or they destroy data.** When the file holds the bare
> form, writing `permission.bash` replaces that string with an object and **silently deletes the
> global rule covering every other tool**; when a tool holds ordered per-pattern rules, writing a
> bare action discards them, and their order is semantics (last match wins). Whichever form is in
> the file, the cards that cannot safely write it stand down and say why.

**Two card kinds are new:** a *derived / read-only* kind (#16, #17) that reports resolver
state rather than editing a key, and a *tri-state enum* for `autoupdate`'s
`true | false | "notify"` union — shipped as **`LabelledEnum`**, since absent makes four states and
an unreadable value five, and the real distinction is a display label separate from the committed
token. Everything else reuses the existing Bool / Int / EnumString kinds unchanged. ⚠ **Not
`StringList`** — the one card that looked like a list (#8 `plugin`) ships `Derived` instead, because
a string list would drop the tuple form's options on write.

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
> 1. **`Metadata` is write-only today.** `SchemaNodeAdapter.BuildMetadata` populates it,
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
| `AGENTS.md` | 531 | **Split.** The largest doc job. Invariants and "if you're doing X" checklists divide into shared (`AgentForge.*`) and per-app. Several entries die outright (`ConfigScopeAdapter._cache` ordering); one splits in two ("adding a debug flag" → shared vs per-app). |
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
| **Synthetic search hits** ✅ *(slice 3)* | ~~`SearchViewModel.EssentialsTriggers`~~ → `ClaudeSyntheticSearch.Build()` returns a `SyntheticSearchEntry` list; the shell owns matching (`SearchTrigger`), ordering and suppression | Done for Claude; OpenCode supplies its own list. OpenCode's set should include `share`/`auto-share`, `snapshot`, `permission allow`, `plugin`, `subagent depth`, and — importantly — the *gotcha* phrasings (`OPENCODE_CONFIG_DIR`, `AGENTS.md not loading`) so users searching a symptom land on the explanation. |
| **`SearchViewModel`'s header-title const** ✅ *(slice 3)* | **Gone, not parameterised.** It did two jobs — locate the Permissions node and label each synthetic row's section — and once entries carry both, the parameter disappeared. The neutral constructor names no product at all. | Nothing left to do. |
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
| `ci.yml` | Builds the `.slnx`, so new projects come along free — but the RID-qualified restore note applies to both app csprojs, and the smoke gate needs a per-app run. ✅ **Trim Check fixed 2026-09-04** — it now trims OpenCodeForge *and* sees the shared stack, which it never did for **either** app. The workflow itself needed no edit. See below. |
| `winget-submit.yml` | Per-app; carries the `40c3ebf` lessons (submit builds only from `packaging/winget/*.yaml`; pin `ManifestVersion`; set `[Console]::OutputEncoding`; duplicate guard; signing precondition). |
| `codeql.yml` | Likely unchanged. |
| `model-catalog-refresh.yml` | Claude-only; leave. |
| `schema-refresh.yml` | Already covered — multi-schema drift (see Schema updates). |

`packaging/` needs a second manifest set (`Bennewitz.Ninja.OpenCodeForge.{yaml,installer,locale}`)
and `Submit-Winget.ps1` parameterized on package identity.

> ✅ **RESOLVED (2026-09-04).** The Trim Check now trims OpenCodeForge and — the part that
> mattered more — can *see* the shared stack, which it never did for **either** app.
>
> ⛔⛔ **All three premises this was filed under (2026-09-03) were wrong, and the wrong one was
> load-bearing.** Recorded rather than quietly overwritten, because the way each was wrong is the
> reusable part.
>
> **1. "ClaudeForge does not hit it because of its `ILLink.Suppressions.xml`" — FALSE.** That file
> names **zero** `AgentForge.*` assemblies (grepped). ClaudeForge was never protected; it was
> equally blind. *I inferred the cause from adjacency — a suppressions file existed, so it must be
> what made the difference — and never tested it.* Re-measured here by canary: with the offending
> cast removed and `IsTrimmable` in place, **both** apps' publishes fail on the named `IL2026`
> (`T1`, `T3`); with `src/Directory.Build.props` moved aside, the identical break publishes
> **exit 0, zero diagnostics** (`T2`). The props file is the eyesight, not the suppressions.
>
> **2. "exactly one error" — FALSE; it was first-error masking.** The `IL2026` aborted the build
> before ILLink ran. Fixing it revealed 4 × `IL2070` in `Avalonia.Controls.DataGrid`, which
> collapse to a single file-less `IL2104` unless `TrimmerSingleWarn=false`.
>
> **3. "the fix is one file" — FALSE; four.** And the gate paid for itself immediately by finding
> **five more instances of the same defect** in ClaudeForge's own shipping SDK
> (`PermissionsAccessor` ×2, `HooksAccessor` ×3) — latent and invisible for exactly the same
> reason.
>
> ⭐⭐ **Why a csproj-only change would have made things worse.** The bug was ever visible only
> because `-p:PublishTrimmed=true` on the **command line** is a *global* MSBuild property: it flows
> into every project in the graph and switches on each one's Roslyn trim analyser. The same
> property written inside an app's csproj applies to that app alone. So enabling `PublishTrimmed`
> in `OpenCodeForge.csproj` and stopping there yields a gate that **trims but still cannot see
> shared code** — a more convincing false assurance than the one it replaced.
>
> **What shipped**
>
> | Change | Why |
> |---|---|
> | `src/Directory.Build.props` **(new)** — `<IsTrimmable>true</IsTrimmable>` | The actual fix: every shipped assembly becomes trim-**analysed**, not merely trim-**able**. Measured cost **zero** new diagnostics — `TrimMode=link` was already trimming these assemblies, so this changes what is *reported*, not what is *removed*. Scoped to `src/`; `tests/` is never published. Imports the root props explicitly via `GetPathOfFileAbove`, since MSBuild applies only the closest one. |
> | `McpServersAccessor` + `PermissionsAccessor` ×2 + `HooksAccessor` ×3 | Cast to `(JsonNode?)` to bind the non-generic `IList<JsonNode?>.Add`. Uncast, overload resolution prefers `JsonArray.Add<T>(T?)` — `T` infers to the exact argument type, beating the base-class parameter — and that overload carries `[RequiresUnreferencedCode]`. Genuinely trim-safe: the value is already a `JsonValue` from the non-generic `JsonValue.Create(string?)`. Same trick `SettingsGroupEditorViewModel.BuildPlaceholder` already documents. |
> | `OpenCodeForge.csproj` — `PublishTrimmed` / `TrimMode=link` under the Release condition, plus the four diagnostic settings | So the app is trimmed at all. Deliberately **no** `SelfContained` / `PublishSingleFile` / `RuntimeIdentifiers`: this app has no `release.yml` pipeline yet and its only Release publish is the CI trim check, which passes those on the command line. Add them with a real release workflow, not before. |
> | `src/OpenCodeForge/ILLink.Suppressions.xml` **(new)** — DataGrid `IL2070` + `IL2104` only | Third-party, unfixable upstream. The safety argument is *stronger* here than in ClaudeForge: OpenCodeForge instantiates **no** DataGrid (zero `<DataGrid` in its XAML — grepped), so the reflecting path is unreachable; the assembly is present only because `LayeredEditors.Avalonia` package-references it and the Semi bundle carries DataGrid styles. ⚠ `_ILLinkSuppressions` — the **underscore is load-bearing**; the un-prefixed spelling most guides show is silently ignored. |
>
> **No `TrimmerRootAssembly` entries, deliberately.** All five of ClaudeForge's roots
> (`Markdown.Avalonia`, `ColorTextBlock.Avalonia`, `Svg.Model`, `Svg.Custom`,
> `Svg.Controls.Skia.Avalonia`) are absent from OpenCodeForge's package closure. `Semi.Avalonia`
> *is* present but emits nothing here — ClaudeForge needs its Semi suppressions only because
> Svg.Skia widens ILLink's reachability graph into Semi's compiled AXAML, and this app has no
> Svg.Skia.
>
> **Verification.** Suite **3,998 · 0 · 11**, unchanged. Four-way canary (`T1`–`T4` above) in both
> directions and on both apps. ⭐ **Runtime proof that the XAML survived**, because a clean ILLink
> log is not that proof: a trimmed `win-x64` publish was launched against a sandboxed config dir
> and driven through UIA — 16 text elements, all three Essentials cards, the value read from disk,
> and the picker's four rows still announcing their labels.
>
> ⚠ **Unresolved counterexample, recorded rather than smoothed over.** `807087c` (2026-08-18)
> caught exactly this class of defect — an `IL2026` in `JsoncEditor.Quote` — when
> `AgentForge.Jsonc` was already its own project with no `IsTrimmable` anywhere. By the behaviour
> measured above that catch should not have been possible, so the rule is *not* simply "shared
> libraries are invisible". The likeliest explanation is that the August run differed from the
> command later written down for it (a global `-p:PublishTrimmed=true`, or `publish.ps1`, would
> both explain it) — **not verified.** ⛔ **Do not lean on the mechanism; lean on the canary.** If
> trim breaks ever stop being caught, re-run `T1`/`T2` before trusting a green log.

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

> ✅ **SHIPPED 2026-09-08 — the CI half of Phase 13.** `da89131` · `da8bf63` · `d937c8c` ·
> `a4cb70c`. The in-app half (provenance, opt-in promotion, badge, `--schema-source`) is
> untouched.
>
> **The table above is right about the URLs and wrong about the work.** All three upstreams are
> live and, measured 2026-09-08, every property set already matched bundled — but two things the
> table does not mention decide whether the tooling is safe to run at all.
>
> ⛔⛔ **The `models.dev` strip is mandatory, and it is not configuration — it is the reason a
> naive refresh ships a broken schema.** Upstream `opencode-config.json` types **four** `model`
> properties with `"$ref": "https://models.dev/model-schema.json#/$defs/Model"`. Leaving one in
> makes evaluation throw through `ValidateWorkspaceAsync` → `SaveAsync` for *any* config that
> sets a model, and the restore path's evaluate guard does not catch that exception type.
> Measured by planting the un-stripped upstream file: build succeeds, 645 OpenCode tests pass,
> and **only `BundledOpenCodeSchemaTests` fails.** That test's own remarks had already
> anticipated this phase — *"Any future refresh — by hand or by the tooling Phase 13 adds —
> re-downloads a file that has them"* — so the requirement was written down; the plan's table
> simply never inherited it.
>
> The strip is **textual**. Parse-and-reserialise would reformat all ~1,300 lines into a diff no
> reviewer could read. Two cases, and reversing them yields invalid JSON: a `$ref` line ending
> in a comma has siblings after it; one that does not was the last key, so the **preceding**
> line's comma must go too. All four sites today are the second case.
>
> ⚠ **The old up-to-date check was wrong on Windows.** `.gitattributes` sets `* text=auto`, so a
> Windows checkout holds CRLF while every download is LF — `claude-code-settings.json` is 4,260
> bytes larger on disk for exactly that reason, byte-identical once normalised. The byte-hash
> short-circuit therefore reported drift on every local run and `-DryRun` always claimed a
> 4,260-line change. **CI never saw it** (a Linux checkout is LF), which is how it survived.
> Comparison is now normalised.
>
> ⛔ **`refresh-schema.sh` had been broken since the initial commit** — `TARGET_PATH` named
> `src/ClaudeForge.Core/`, which Phase 1's rename deleted, so the script exited "target not
> found" before doing anything. `BuildFilePathIntegrityTests` scanned `scripts/**/*.ps1` only,
> and that guard exists *because of* this very rename. It scans `*.sh` now; widening it turned
> it red on the real defect before the fix.
>
> ⭐ **Parity between the two scripts is asserted by BYTE comparison of what they write, not by
> matching verdicts** — and that caught a real defect. `awk` always terminates its last line, so
> the two schemas that carry **no trailing newline** (`opencode-config`, `opencode-tui`) came
> back one byte longer and reported CHANGED on every run, while PowerShell round-tripped exactly.
> The shell version now takes the input's newline state as an `awk -v` variable and skips the
> transform entirely when there is nothing to strip.
>
> **The extra guard is `SchemaRefreshDriftTests` (5 tests, `tests/OpenCodeForge.Tests/`), not a
> scheduled job.** A test runs on every PR including the refresh PR, which is strictly better
> than a weekly job. It pins the top-level property counts per schema (142 / 36 / 13, measured
> against the live upstreams so they are the counts a refresh *today* would produce) and asserts
> every key `OpenCodePageLayout` maps still exists in its schema.
>
> ⭐ **What it deliberately does NOT assert, and why that was measured rather than assumed:** a
> *new* upstream key with no map entry is legal — it falls to the fallback page — and cannot ship
> untriaged because `OpenCodeDangerTableTests` already fails on any schema key with no danger
> entry. A canary that added a key confirmed both halves: `EveryConfigSchemaKeyIsClassified`
> reddens and the orphan check stays quiet. So the guard is neither duplicated nor over-tight.
>
> ⚠ **Not adopted: today's upstream content.** `opencode-config.json` has genuine drift
> (`chunkTimeout` became `anyOf[integer, false]`; two timeout descriptions now document a 300000
> default). Shipping the tooling and adopting a schema change are separate decisions, so the
> bundled files are untouched — every test run above restored them byte-identically. Run
> `pwsh scripts/refresh-schema.ps1` when that change is wanted.

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
| 3 | **Delete the `ConfigScopeAdapter._cache` array-ordering invariant** — it stops existing. Rewrite the `Core/Settings` sidecar's scope section. |
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

Two commits, as described. The `ConfigScopeAdapter._cache` invariant is gone from `AGENTS.md`,
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
>    `== ConfigScope.Managed`, now `Scope.IsReadOnly`), and `ConfigScopeAdapter` itself.
>
> **"Delete the statics" was deliberately not done.** `ConfigScope.User` and friends have
> **58 uses in `src/` but 1,074 in tests**, and nothing supplies a `ScopeSet` until
> Phase 4. Deleting them now would be a diff dominated by mechanical test churn, against an
> abstraction that does not exist yet, and Phase 4 would likely churn it again. The statics
> survive as Claude's canonical set; `ConfigScope.All` **is** the ordered scope set that
> `ConfigScopeAdapter` and the id-resolution fallback now build themselves from. **Phase 4 owns
> retiring them**, once a product descriptor can supply scopes.
>
> **The invariant was replaced by a test, not just deleted.** The old `AGENTS.md` entry
> said in as many words that a mis-ordered cache "produces the wrong wrapper silently" and
> that there was **no runtime check**. `ConfigScopeAdapterTests.For_ReturnsTheWrapperForTheScopeItWasAsked`
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
> 2. ⚠ **`ConfigScopeAdapterTests` stayed green through that whole failure**, because
>    `ConfigScopeAdapter._cache` is built from `ConfigScope.All` and was therefore
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
Add-to-PATH, ~~`EssentialsCardViewModel` + `EssentialsCardKind*`~~ + `EssentialsView`,
`BackupRestoreViewModel`, `AboutEditorViewModel`, `WelcomeView`.
Split `Strings.resx` (Problem 8) in the same phase.

> ✅ **The Essentials VIEW-MODELS are already done — `7ac2223`, ahead of this phase**, because
> Phase 12 needed them. `EssentialsCardViewModel`, `EssentialsCardKind`,
> `EssentialsCardKindConverters` and `ModelSuggestionItem` are in
> `AgentForge.Avalonia.Shell/Essentials/`, and the 14-parameter constructor became
> `EssentialsCardOptions` on the way. **`EssentialsView` deliberately did NOT move** — the shell
> still has no AXAML, and each app wants its own card view anyway.
>
> ⚠ **Note what that implies for this phase's risk.** The shell holding no AXAML is the thing that
> makes `MainWindow` / `WelcomeView` / `UpdateBanner` / `EssentialsView` expensive: the first view
> to move brings AXAML compilation, a `LayeredEditors.Avalonia` reference, and the resx-split
> question with it. Budget that once, not four times.
>
> ⭐ Also spent: the three `Messages/` records moved from `LayeredEditors.Avalonia` down to
> `LayeredEditors.ViewModels` at **zero** call-site cost (they have no using directives and the
> target project already uses the same namespace prefix). Any other view-model-layer type stranded
> in the control library can move the same way.

Diagnostics come along too: `AvaloniaDiagnostics` wiring, the F12 `LiveLogWindow` toggle,
and the new Shift+F12 config-activity `LiveTailWindow` — plus the ownerless-helper-window
cleanup in `App.axaml.cs`, extended to cover the second window.

**Parameterize while moving — don't just relocate.** `WindowStateService.StatePath` becomes
per-app (keeping the `=>` property form; the `static readonly` version bypasses the test
sandbox). `AppUpdateService` takes `{ Owner, Repo, AssetPattern, CurrentVersion }`.
~~`SearchViewModel`'s synthetic-trigger table and header-title const become per-product
inputs~~ — **done in slice 3**; the table became a supplied entry list and the const
disappeared entirely. Add-to-PATH takes the binary name. `NavigationNodeIdTests` is **extended** to scan
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
| **deep-link** | ✅ **taken 2nd, not 3rd** — `NavDeepPath` + `IDeepNavigable` moved; see the reorder note |
| **search** | ✅ — `SearchViewModel` + `SearchResultViewModel` moved; 5 seams became **2 interfaces + 1 entry list**; see below |
| **nav** | 🔶 **PARTIAL** — two clean seams taken (`INavigablePage`, `SchemaPageLayout`). **Tree assembly + `ProductSection` enrichment: DEFERRED, not rejected** — measured and costed below, available to pick up whenever the topology question is revisited |
| **save** | ✅ — model + builder moved; the host supplies a `SaveDialogText`. **Problem 8's resx split turned out to be the wrong move here — see below** |

### What slice 3 (search) actually cost, and the two things the measurement missed

The five seams collapsed into **three types**, because two of them were the same question
and two more were a question the editors should answer themselves:

| Measured seam | Became |
|---|---|
| synthetic-trigger table (`EssentialsTriggers`) | `SyntheticSearchEntry` list, supplied per pass |
| card titles (`Strings.EssentialsCard*`) | …the same list — entries carry their own text |
| editor→schema-key map (`PermissionsEditorViewModel => "permissions"`) | **`IJsonPathScopedEditor`** on the editor |
| `child.Editor is SettingsGroupEditorViewModel` | **`ISchemaGroupEditor`** on the editor |
| `n.Editor is EssentialsViewModel` + the `"Permissions"` child title | `SyntheticSearchEntry.FindTarget`, a product-supplied tree lookup |

⭐ **The type map's own comment documented the precondition the move falsified.** It said
the hardcoded `editor switch` was fine "because the specialized editors are a small, closed
set defined in this assembly" — true right up until the walk left the assembly. Dependency
inversion is the only shape that survives a second product: the editor declares its own
prefix, and there is no central list to forget to update.

**Two things the 5-seam measurement missed — both would have put Claude in the shell.**

1. **The two hand-written permission rows.** `--dangerouslySkipPermissions` and the
   `bypassPermissions` deep link were never counted as seams (they are string literals, not
   type couplings) but they are as Claude-specific as anything in the file — a Claude CLI
   flag and a Claude permission mode, with English prose bodies. **Count string literals as
   couplings, not just types.**
2. **`claudeCodeNavTitle` was a constructor parameter.** The seam list treated it as data
   the host passes, which it is — but it was doing two jobs (locating the Permissions node
   *and* labelling every synthetic row's section), and once the entries carry both, the
   parameter disappears. **The neutral constructor no longer names a product at all.**

**The matcher is the part worth having.** Three trigger flavours existed implicitly and
differed in ways that are easy to get wrong by hand: `Phrases` is bidirectional (query ⊇
phrase *or* phrase ⊇ query — this is what makes partial typing land early), `Mentions` is
one-directional, and `PrefixOf` is narrower than both. They are now declared, not coded:
`SearchTrigger` with `Phrases` / `PrefixOf` / `Mentions` / `Excluding` / `MinQueryLength`.
An empty trigger matches **nothing**, deliberately — an unreachable row is a visible bug, a
universally-reachable one pins itself to every search.

⚠ **One deliberate behaviour change, stated not smuggled.** The three matchers disagreed
about whitespace: Essentials trimmed the query, the two permission rows did not — so
`"  danger"` failed the prefix rule while `"  bypass"` still matched via its contains rule.
Unifying them required picking one normalisation. The query is now lower-cased **and
trimmed** once, before any rule sees it. Pinned by
`Query_IsTrimmedAndLowered_BeforeTriggersSeeIt`.

**Suppression became order-independent.** The old code added the bypass row and then
removed the opposite-intent Essentials card, which only worked because the card was added
first. `Suppresses` is now resolved after the whole list is walked — and an entry whose
target page is absent suppresses nothing, so a page one install lacks can no longer hide a
page it has.

⚠⚠ **`BuildFilePathIntegrityTests` did NOT fire on this slice, and that is a gap, not
good news.** Three stale references survived a fully green suite: a full path inside a
`.cs` doc comment (`AgentConfigClientCore` cites `SearchViewModel.cs`; the guard scans root
`*.md` + `AGENTS.md` sidecars only), and two **bare filenames** in an `AGENTS.md` table
cell, which are not path-shaped and so are not matched. **The phase note above says to
expect this guard to catch prose rot on every slice — it catches path rot, and only in the
files it scans.** Bare filenames and `.cs` comments still need a manual grep.

**Coverage the slice added, and why the existing tests could not provide it.** All 51
pre-existing search tests are Claude fixtures: they construct Claude editor VMs and assert
on Claude's rows, so they cannot distinguish "the walk works" from "the walk works for
Claude". Nineteen new tests drive a **fabricated** product — `FakeGroupEditor`,
`FakeScopedEditor`, a `Widget Forge` entry table — and cover the two-product case in both
the pinned-row path and the schema walk. **All 12 canaries went red, and the six
single-failure canaries each killed exactly the test named after the behaviour they broke.**


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
**Slice order changed: deep-link before search.** Nothing forces the plan's original order —
`SearchViewModel` has **zero** references to `NavDeepPath` / `IDeepNavigable`, so the two are
independent. The measurement is what decided it:

| | `Strings.` uses | Product-typed couplings | Work to move |
|---|---:|---|---|
| `NavDeepPath` (287) + `IDeepNavigable` (118) | **0** | none | namespace + 8 consumer `using`s |
| `SearchViewModel` (693) | 5 | `EssentialsViewModel`, `SettingsGroupEditorViewModel`, `PermissionsEditorViewModel` | **5 seams** |

Deep-link needed no parameterisation at all. Search needs five: the synthetic-trigger table
and its card titles (both Claude data — trigger phrases include `"opus"`, `"haiku"`,
`"claude_code_max_output_tokens"`), the editor→schema-key map
(`PermissionsEditorViewModel => "permissions"`), the "is this the Essentials node" test
(`n.Editor is EssentialsViewModel`), the schema-node reach-through
(`child.Editor is SettingsGroupEditorViewModel`), and the hardcoded `"Permissions"` child
title. The *algorithm* in `TryAddEssentialsSyntheticHits` is genuinely neutral — walk a
table, match the query — so the seam shape is the same one 4c and 4f already established:
**the product supplies the data, the shell owns the walk.**

> ⭐ **One plan item was already done.** `NavigationNodeViewModel` is listed as something to
> move, but it already lives in `LayeredEditors.ViewModels`; the file in `src/ClaudeForge` is
> a `global using` alias. That is also why the shell may reference
> `LayeredEditors.ViewModels` — the layering rule forbids `AgentForge.* -> ClaudeForge.*` /
> `OpenCode.*`, and `LayeredEditors.*` is neither. **The rest of the Phase 5 item list was
> checked and is accurate.**

> ⚠ **Expect `BuildFilePathIntegrityTests` to fail on most slices, and treat that as the
> guard working.** Slice 1 turned the suite red because root `AGENTS.md` cited
> `src/ClaudeForge/ViewModels/Status/StatusController.cs`. Prose falsified by a move is the
> failure mode this phase produces repeatedly, and the compiler cannot see it — that test is
> the only thing that can.

### Slice 4 (nav) — measured, then taken partway with the rest deferred

This is the abandonment point, so the slice was measured before anything moved and the
result put to the maintainer rather than absorbed. **The nav surface is ~1,058 lines:**

| Piece | Lines | Genuinely neutral |
|---|---:|---|
| `MainWindowViewModel.BuildNavigationTreeAsync` | **493** | ~60 (clear / dispose / dividers / restore); **~430 is this app's page composition** |
| `OnSelectedNodeChanged` | 128 | shape yes — but a switch over **8 concrete ClaudeForge view-model types** |
| `RestoreSelectedNode` | 104 | mostly; 3 product page titles pick the defaults |
| `NavigationTreeBuilder` | 238 | ~40 algorithm; **~110 is a hardcoded Claude property→group map** |
| 4 small helpers | 95 | yes |

⭐⭐ **THE TWO-COMPOSITIONS PREDICTION WAS WRONG, IN A USEFUL DIRECTION.** The plan
expected the blocker to be that "Claude Code has pages Desktop has none of", i.e. two
asymmetric per-product compositions. It is not. **The per-product section is symmetric** —
header + schema groups + About, identical for both. The asymmetry lives in **eight
top-level pages** (Essentials, Effective Settings, Environment, Memory, Agents & Skills,
Profiles, Backup, Welcome) that sit *outside* both product sections, six of which take
`ClaudeCodeSdk` directly. **Re-derive the obstacle before trusting a prediction about it.**

**What made this the stopping point is the ratio, not the asymmetry.** Slices 1–3 each
moved a whole unit (status; deep-path; the 753-line search machinery). Slice 4 would move
**~200 of 1,058 lines**, need two new seams *and* an enrichment of `ProductSection` to
carry scope contexts and schema nodes — while a 493-line composition stays behind by
design. That composition is correct where it is: `ProfilesViewModel`'s four callbacks and
the `_suppressProfileChangeReload` (I14) guard, the H-2 persistent-VM pattern, the
off-UI-thread editor build, the Backup `Initial*` re-sync that fixed a real
reload-clobbers-session-edits bug. None of it is neutral and none of it should be.

**Maintainer's decision (2026-08-19): take the two clean seams, leave the tree assembly
for now.** This is a deferral, not a verdict — Phase 5 is the abandonment point, so the
whole topology is still open, and nothing here forecloses doing the third piece later.

**What the deferred piece is, so it can be picked up without re-measuring.** Move the ~60
lines of assembly scaffolding (clear, dispose editors, insert dividers, restore header
expansion, restore selection) behind a nav-entry descriptor the shell assembles from, and
enrich `ProductSection` to carry the `SharedScopeContext` and the schema nodes so the
per-product section — which **is** symmetric — becomes a loop. Cost: a rewrite of the
493-line `BuildNavigationTreeAsync`, the most workaround-dense method in the file (I14
`_suppressProfileChangeReload`, the H-2 persistent-VM pattern, the off-UI-thread editor
build, the Backup `Initial*` re-sync). ~430 lines of page composition stay behind either
way, correctly.

#### Seam 1 — `INavigablePage` (the page-lifecycle protocol)

`OnSelectedNodeChanged` was a 128-line chain of `editor is SomeConcreteViewModel`, one arm
per page, each calling that page's differently-named refresh method (`Refresh`, `Reload`,
`Activate`, `RefreshConfigAvailability`, `RefreshAsync`). Now 61 lines and **zero page
types named**. Both members carry default no-op bodies so a page implements only the half
it needs.

⚠ **`OnNavigatedFrom(bool replaced)` carries a flag because the old code had a guard that
had to be preserved exactly.** `replaced` is `false` when the incoming editor is the same
instance — which happens because several pages deliberately survive a workspace reload and
are re-attached to a freshly built node. Without it, a reload throws away a filter the user
never navigated away from.

⚠⚠ **THE COVERAGE FINDING — the whole surface was untested, and the suite said nothing.**
Replacing 128 lines of behaviour failed **0 of 2,910**. Canaries then measured it exactly:

| Canary | Failures before the new tests | After |
|---|---:|---:|
| `OnNavigatedTo` never dispatched | 11 | 12 |
| `OnNavigatedFrom` never dispatched | **0** | **5** |
| `replaced` forced to `true` | **0** | **2** |
| `replaced` forced to `false` | **0** | **2** |

So the leave hook and *both directions* of the guard were protected by nothing.
`NavigationPageLifecycleTests` (7) closes it, and two of those drive the dispatch with a
page type this app does not own — the only way to tell "the host dispatches on the
interface" apart from "the host happens to name the right types".

#### Seam 2 — `SchemaPageLayout` (flat schema → ordered pages)

`NavigationTreeBuilder` keeps its three tables (property→page, page order, page
descriptions) as declared data and hands them to the shell's `Arrange`. Rules now stated
and tested rather than implied:

- unmapped property → `FallbackPage`; the fallback is ordinary once it is in `PageOrder`,
  so it keeps its declared position instead of being appended;
- a listed page with no properties is skipped;
- **a page named in the map but missing from `PageOrder` still renders**, appended sorted
  by title — so a typo silently relocates a whole page to the bottom of the tree and
  changes nothing else. `ClaudeLayout_EveryMappedPageIsAlsoOrdered` is the guard, and
  typo'ing `"Permissions"` in the map is what proves it fires.

10 tests, 7 canaries, all red.

⚠ **One of those canaries caught a weak test of mine.** `Arrange_EmitsPagesInTheDeclared-
Order` first used page titles whose declared order happened to equal their alphabetical
order, so ignoring `PageOrder` entirely left it green. Rewritten with titles where the
declared answer differs from *both* alphabetical and schema order. **A canary that fails
fewer tests than expected is telling you something about the tests, not the canary.**

### Slice 5 (save) — the cleanest ratio of the phase, and a correction to Problem 8

`SaveChangesDialogViewModel` (212 lines) and `SaveDialogBuilder` (170) moved to
`AgentForge.Avalonia.Shell/Save/`. **All 382 lines of logic are neutral** — which
documents are dirty, how their diffs are computed, how paths are shortened to `~/…`,
how values are truncated, how the summary counts read. Unlike nav, **no product
composition stays behind**: the only Claude knowledge in the whole surface was twenty
`Strings.*` lookups.

⚠⚠ **THE PLAN SAID TO SPLIT `Strings.resx` (Problem 8) IN THIS PHASE. DOING SO WOULD
HAVE SILENTLY UN-TRANSLATED THE STRINGS IT MOVED.** `LocalizationParityTests` locates
its resx files by walking to `src/ClaudeForge/Localization` **by hardcoded path**
(`FindLocalizationDirectory`), so a resource set anywhere else is checked by *none* of
its four contracts — not every-key-in-every-locale, not the `TODO`-placeholder
rejection, not the copy-of-English detection.

That is not a hypothetical. **`src/ClaudeForge.Avalonia/Localization/Strings.resx`
already holds 93 user-facing strings with no locale siblings at all**, and nothing in
the suite reports it. Moving fourteen currently-nine-locale keys into a new shell
resource set would have joined them, invisibly.

**So the seam is data, not resources:** the host hands over a `SaveDialogText` with
twelve strings, and the keys stay in the guarded directory with their translations and
their parity guard. This is the same shape as `IMergePolicy`, `ScopeLadder`,
`SyntheticSearchEntry` and `SchemaPageLayout` — the product states, the shell applies —
and it is *better* than a resx split anyway: what a save means, and what the user must
do afterwards for it to take effect, genuinely differs per product. OpenCode needs a
"restart OpenCode" affordance that Claude does not (Phase 0, S-findings).

**Problem 8 is therefore not "split the resx". It is "stop neutral code from needing
one", plus — separately — extend `LocalizationParityTests` to every resx in the repo
rather than one hardcoded directory.** The second half is worth doing on its own
merits: it would immediately surface the 93 unguarded strings.

> ✅ **The second half landed in Phase 9a-4, in the form the problem actually needs.**
> The damaging part was never the missing translations — it was that a resx could be
> added with **no locale siblings and no test saying anything**. Three new contracts in
> `LocalizationParityTests` close that: a **ledger** of every neutral `Strings.resx`
> under `src/` declaring it translated or deliberately English-only with a reason
> (#5, discovery both ways); a check that each claim **matches the locale files on disk**
> (#6); and a check that the four original contracts **cover every project the ledger
> calls localized** (#7). All three canaried.
>
> ⭐ **The asymmetry in #6 is the whole point.** Declaring a project English-only while
> locale files sit beside it *fails*, and the failure message says to widen contracts
> #1–#4 rather than edit the ledger — so a first translation cannot land unchecked. The
> four contracts still resolve one hardcoded directory, which is now **true and
> guarded** instead of true and invisible; #7 is what forces the generalisation at the
> moment it starts to matter.
>
> The 93 `ClaudeForge.Avalonia` strings are now a **declared** gap, not a hidden one.
> `OpenCode.Avalonia` gained its own resx in the same slice (118 keys, English-only,
> declared) — because the alternative it replaced was literals in AXAML, which is
> strictly worse: a literal cannot even be found by a translator.

**Three members are `required` rather than defaulted**, each closing a silent failure:
`SaveChangesDialogViewModel.Text` (a dialog must not inherit another product's words),
`SaveChangeSectionViewModel.ActionVerb` (it used to default to the *save* label, which
is the wrong answer inside a restore preview), and
`SaveChangeEntryViewModel.KindAccessibleName` (the pill renders a bare `+`/`-`/`~`, so
an empty automation name reads as nothing to a screen reader — a compile error is the
only reliable guard).

**The view stays in `src/ClaudeForge/Views/`.** Moving it drags three converters that
five to nine other AXAML files also use; that migration is really a
`LayeredEditors.Avalonia` question. Only its `xmlns` and `x:DataType` changed —
and renaming a bound member still fails the build with `AVLN2000` naming the *shell*
namespace, so the compiled-binding guard followed the type across the assembly.

**One SDK concession, stated not hidden.** The builder reads `SnapshotDirtyDocuments()`
/ `DirtyDocumentSnapshot`, which are `internal` because they traffic in `JsonNode` that
the public SDK surface deliberately excludes. `AgentForge.Sdk` now grants
`InternalsVisibleTo("AgentForge.Avalonia.Shell")` — the same call already made for
`ClaudeForge.Sdk.Claude`, and smaller than promoting a `JsonNode`-bearing snapshot to
the public surface to serve one dialog.

**Coverage.** The builder had exactly one test before this (one env change, one
product). Now 24 across three fixtures, including the multi-source case, the restore
wording, path shortening, truncation-with-full-value-retained, and per-entry accessible
names. Ten canaries, all red.

⚠ **A canary caught a self-consistent test of mine — the fourth instance of this trap
in the refactor.** `Build_GivesEveryEntryAnAccessibleName` asserted
`e.KindAccessibleName == Text.AccessibleNameFor(e.Kind)`, so breaking the kind mapping
left it green: both sides were computed by the thing under test. Rewritten to assert
against the raw `KindAdded` / `KindModified` properties, with a fixture that produces
both an added and a modified change. **A fixture derived from what it checks cannot
detect that thing moving** — same family as 4d-2's `ArchiveFolder`, 4c's single-document
workspaces, and 4f's `ConfigScopeAdapter._cache`.

### First CI run on this branch — what fifty-six unpushed commits were hiding

The branch reached CI for the first time at `8144fd4`, after fifty-six commits verified
only on Windows. **CodeQL passed clean and the Trim Check passed**; `Build & Test` failed
on **all three** operating systems, which immediately ruled out a platform bug and
pointed at something the local runs could not see.

**Failure 1 — `EveryHardcodedRepoPathInBuildFilesExists`, all three OSes.**
`src/ClaudeForge/ViewModels/Editors/AGENTS.md` cited
`src/LayeredEditors.Avalonia/ViewModels` for the generic leaf editors, which live in
`src/LayeredEditors.ViewModels`. The doc used the **namespace** shape
(`Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels`) as a **directory path**, and for
that project the two genuinely differ. **This branch introduced it**, in `8834039` during
the Phase 1 renames — precisely the rot the guard exists to catch.

⚠⚠ **WHY THE GUARD DID NOT CATCH IT LOCALLY, AND WHY THAT GENERALISES.** An **empty,
untracked** `src/LayeredEditors.Avalonia/ViewModels` directory existed on the development
machine. **Git does not track empty directories**, so the path resolved locally and was
absent in a fresh checkout. **For any path naming a DIRECTORY rather than a file, the
local guard is systematically weaker than the same guard in CI.** Phase 5 slice 1 hit the
same class of thing (five empty untracked directories reading as a half-finished
extraction), so this is the **second** time empty directories have misled this work.
Added to the root `AGENTS.md` verify-before-shipping checklist as a `find -type d -empty`
step. Reproduced before fixing: deleting the directory reproduced the CI message exactly.

**Failure 2 — `StatusControllerTests.Set_SuccessKind_AutoClearsAfterDelay`, macOS only.**
Avalonia platform start-up threw `InvalidOperationException: The calling thread cannot
access this object because a different thread owns it`, from `Compositor..ctor` →
`DefaultRenderLoop.Add` → `Dispatcher.VerifyAccess`. `HeadlessUnitTestSession.GetOrStartForAssembly`
starts the session **lazily**, so whichever test is scheduled first owns starting the
Avalonia platform — and that ordering differs per run and per OS. **The consequence was
worse than the fault**: a platform start-up failure was reported against a status-bar
auto-clear assertion with no connection to compositor construction.

`HeadlessSessionBootstrap` now starts the session once from `[AssemblyInitialize]`.
⚠ **That is justified on diagnosability grounds; whether it FIXES macOS is unverified** —
there is no macOS machine on the development side and CI cannot be re-run from there, so
CI is the only oracle. If it recurs, the next step is the platform-init path, not the
status-bar tests.

⚠ **Access split worth knowing:** the development machine's stored GitHub credential has
**READ** on the repo but not write, so CI results can be fetched and analysed from there
while pushing has to happen elsewhere — currently via `git bundle`. That is workable but
it makes every CI round-trip a two-party operation; **budget for it rather than assuming
a fast fix-and-repush loop.**

### Phase 6 — Shared permission *vocabulary* (Problem 5) — ✅ **DONE (`a453063`)**

Not an extraction. ~~Define `PermissionOutcome` and a generic
`Decision<TRule>(Outcome, MatchedRule, MatchedScope, Explanation)` in
`AgentForge.Abstractions`, move the three narrow UI interfaces, and share the tester/builder
**view templates**.~~ Everything else stays Claude-side; OpenCode gets its own implementation
in Phase 9.

**⚠⚠ THREE OF THE FIVE DELIVERABLES DID NOT SURVIVE MEASUREMENT.** The phase shrank again,
from "hours" to **one enum plus two tests**. What was actually built, and why the rest was
not:

| Deliverable as written | Measured |
|---|---|
| `PermissionOutcome` → `AgentForge.Abstractions` | ✅ **Done.** Genuinely shared — Claude sorts rules into `permissions.allow`/`ask`/`deny` arrays, OpenCode maps a tool or glob straight to the strings `"allow"`/`"ask"`/`"deny"`. Abstractions is reachable transitively from all four consumers, so **no new project references**. |
| generic `Decision<TRule>(…, Explanation)` | ⛔ **That shape does not exist.** Real `PermissionDecision` takes **six** params, three of them Claude-only (`PermissionBucket`, `PermissionDefaultMode?`, `DecidingSubcommand`), and there is **no `Explanation`** — the tester computes it in `Explain()`. One producer (`PermissionResolver`), one consumer (`PermissionTesterViewModel`), both Claude and both staying. Genericizing drags **two more Claude enums** into the neutral assembly — precisely the manufactured abstraction this phase forbids. |
| `IPermissionPathPicker` → `LayeredEditors.Avalonia.Services` | ✅ **Done** — but only after a **wrong rejection was caught and reversed**, which is the part worth keeping. See the correction note below. |
| tester/builder AXAML templates → the shell | ⛔ **Both carry `x:DataType` pointing at the Claude view-models** pass 11 concluded must stay (`PermissionTesterViewModel`, `GuidedRuleBuilderViewModel`). Sharing them means either dragging those VMs into the neutral shell, or dropping `x:DataType` — and that attribute is the strongest AXAML guard in this repo (established 4d-3, re-proved in slices 3 and 5). **Neither trade is worth a 125-line and a 300-line template.** |
| "five-minute fix": the `Services` → `Sdk` layering violation | ✅ **Already fixed in Phase 1**, by `693da36` "add AgentForge.Abstractions and break the Services -> Sdk violation". `LayeredEditors.Avalonia` now references only `LayeredEditors.*`. **The note was stale, not wrong-at-the-time.** |

⚠⚠ **A REJECTION THAT WAS WRONG, AND HOW IT WAS CAUGHT — read this before trusting any
"that target does not exist" claim in this document.** `IPermissionPathPicker` was first
rejected on the grounds that `LayeredEditors.Avalonia.Services` did not exist, because the
check was `git ls-files src/LayeredEditors.Avalonia | grep -i service` — which looks for a
**directory inside** that project and finds nothing. **`LayeredEditors.Avalonia.Services` is
a sibling PROJECT**, and it is exactly the right home: it already holds `IDialogService`,
`IShareService`, `IShellLauncher`, `IEnvironmentProvider` and `ISaveChangesPrompt`, and
references nothing but `AgentForge.Abstractions`. The error surfaced one commit later, from
an unrelated `.slnx` listing during Phase 7 measurement. **When checking whether an assembly
exists, read `ClaudeForge.slnx`, not the directory tree of a similarly-named project.**

The move cost one `ProjectReference` (`ClaudeForge.Avalonia` → `LayeredEditors.Avalonia.Services`),
which is a downward reference to neutral UI services and exactly the layering that project
exists to provide. ⚠ Noted in passing, out of scope: **nothing in `src` implements this
interface** — `GuidedRuleBuilderViewModel` defaults it to `null`, so the real app has never
supplied a picker. Only `FakePathPicker` in tests implements it.

⭐ **ONE DELIBERATE BEHAVIOUR CHANGE, stated not smuggled: `Default` is now the enum's zero
value.** The members were ordered `Allow`-first, so `default(PermissionOutcome)` was an
**affirmative grant** — the one verdict that must never arrive by accident. Nothing observes
it today because the tester's field is gated behind `HasResult`; but the type is shared now
and the next product to hold one is not bound by that gate. **Ordinals were measured before
reordering, not assumed:** shifting every ordinal by one changed **no test**, so nothing
persists or compares them. Same reasoning as Phase 3's `default(ConfigScope) == Managed`.

⚠⚠ **THE CANARY IS THE FINDING — restoring `Allow`-first failed exactly ONE test (the new
guard) while the other 2,943 stayed green.** The hazard was completely unguarded. That is
the **seventh** instance of the pattern first recorded under 4d ("*every other test in the
suite exercises one product at a time*"), and it generalizes past products and scopes: **a
value that is only ever written before it is read is invisible to a suite that only reads it
after it is written.** Nothing here constructs an unresolved verdict, because the product
never has one.

⚠ **`MSTEST0032` bit again** (same family as 4e). `Assert.AreEqual(PermissionOutcome.Default,
default(PermissionOutcome))` is two compile-time constants folded to a tautology and **fails
the build**. Route through a runtime read — here an uninitialised auto-property on a private
`UnresolvedVerdict` holder, which models the real hazard better anyway. ⚠ A bare *field*
does not work either: never-assigned is `CS0649`, and warnings are errors, so the compiler
objects to the very thing under test. **Use an auto-property.**

**One item the plan never listed, deliberately left for Phase 9:**
`PermissionOutcomeToBrushConverter` (41 lines) is **completely neutral** — pure outcome →
brush — and becomes movable now that the enum is shared. It was **not** moved: ⚠
`ClaudeForge.Avalonia` does **not** reference `AgentForge.Avalonia.Shell` (only the app
does), so relocating it adds a new layering edge for a single consumer. **Take it when
OpenCode's tester becomes the second consumer.**

~~This phase is now **hours, not days**~~ — it was **one commit**. Do not manufacture an
abstraction to justify the phase. That instruction did the work it was written to do: three
of the five items were rejected by applying it.

**Precision on the "tests pass unchanged" proof** — draft 9 overstated it.
`PermissionDecision` (`MatchedScope`) and `PermissionResolver` both reference
`ConfigScope`, so **Phase 3 has already touched these tests** by the time Phase 6 runs.
The honest claim: after Phase 3, the permission tests are green against the generalized
scope model; **Phase 6 must then change nothing but namespaces**. A behavioural diff at
Phase 6 means the extraction was unfaithful; a behavioural diff at Phase 3 means the scope
generalization was. Keeping those two attributions separate is the whole reason the phases
are ordered this way — do not merge them.

### Phase 7 — `OpenCode.Sdk` — ✅ **COMPLETE (7a–7g)**

| Slice | Commit | |
|---|---|---|
| 7a | `9bf2c9a` | `BundleSchemas` filters by product — taken **before** the schemas landed, so the hazard never existed in the tree |
| 7b | `1492adf` | Both schemas bundled; four `models.dev` `$ref`s stripped |
| 7c | `388b4ab` | Root-`$ref` fallback — **36** top-level nodes for `config.json`, **13** for `tui.json`, exactly S4's numbers |
| 7d | `e35a167` | `src/OpenCode.Sdk` + tests, both in `ClaudeForge.slnx`; products and the five-rung ladder |
| 7e | `c8e16bc` | `OpenCodeMergePolicy` — S1's per-key table |
| 7f | `5fac8ae` | `OpenCodeClient` + `OpenCodeTuiClient` + scope discovery |
| 7g | `d4f0280` | `OpenCodePermissionModel` + glob matcher |

**Corrections this phase made to the plan, all from measurement:**

1. ⚠ **There are FIVE action-only permission tools, not four.** This document lists
   `todowrite` / `question` / `webfetch` / `websearch`; the schema also types **`doom_loop`**
   as action-only. Read the schema, not the prose.
2. ⚠ **The scope ladder's sixth rung is unresolved and deliberately unimplemented.** Two
   places here say `… → managed → macOS MDM` (six rungs); Phase 7's own task list says five.
   Neither is measured. **Five are implemented.** The open question is whether MDM is a
   distinct layer or merely how `managed` is delivered on macOS, as it is for Claude.
   Resolving it needs an MDM-managed macOS install.
3. ⚠ **Confidence across the rungs is not uniform.** S1 measured only
   `custom < project < inline`. **`Global` and `Managed` are asserted and never exercised**,
   and the code says so per rung rather than flattening it.
4. ⛔ **Two rungs are not discovered at all, by decision.** **Inline**
   (`$OPENCODE_CONFIG_CONTENT`) is a config with *no file behind it* and `DiscoveredFile` is a
   path — supporting it needs the **shared** loader to accept content that never came from
   disk. **Managed** has no measured location; a guessed path yields a scope that silently
   never populates, indistinguishable from a working one. A test asserts both are absent.
5. ⚠ **`ConfigFileType` gained two members** — the "product members in a neutral enum"
   deferral surfacing. Measured before extending: **nothing in `src/` branches on
   `DiscoveredFile.FileType`**; it is a descriptive label. Mislabelling OpenCode's files as
   Claude's would have been worse. The generalization should take the whole enum.
6. ⚠ **`GetEffective<T>` does not deserialize collections** — `GetEffective<string[]>` returns
   `null`. A deliberate limit, not a gap: the obvious fix is reflection-based
   `JsonSerializer.Deserialize`, which is what produced the `IL2026` warning that broke the
   Release publish for three phases. **Read arrays as `JsonArray`.**
7. ⚠ **The glob's `*` crosses directory separators** (`~/.ssh/*` matches `~/.ssh/nested/key`).
   An interpretation, not a measurement — it fails *safe* for a deny rule and *unsafe* for an
   allow rule. Worth measuring before anyone leans on `allow` patterns containing separators.

⭐ **`BundleSchemas` was product-blind and would have added ~1.14 MB to every ClaudeForge
backup.** Correctness was already safe (4b left `RestoreEngine` routing by
`ProductDescriptor.SchemaFileName`, so a foreign schema is parsed and never matched) — the
cost was size and parse time, which is exactly why nothing failed. Emptying the bundle failed
**3** tests, so "schemas reach the archive" was guarded; **nothing asserted *which***, so the
product filter passed the whole suite unchanged.

⭐ **Size, measured so it is not re-litigated from the raw figure:** `AgentForge.Core.dll`
goes 721 KB → 1,916 KB, but `tui.json` is 184 near-identical keybind blocks and **gzips to
1.0%** — ~17.5 KB of real download. **Do not split resources across assemblies over this.**

⚠⚠ **A canary that DIDN'T fire, and a weak fixture — both my own tests.** Replacing the
client's ladder with `ScopeLadder.Default` failed **zero** tests: every test opened against
real files, and `EditableScopes` then derives from *discovered documents*, whose scopes
discovery takes from OpenCode's ladder directly. `Scopes` is read in exactly one place — the
`DefaultEditableScope` fallback *before* a workspace exists. Separately,
`KeyOrder_IsPreservedExactly` first used a fixture already in alphabetical order, so an
alphabetical re-sort left it green — **the same trap slice 4 hit.** *A canary failing fewer
tests than expected is telling you about the tests.*

⚠ **The schemas are NOT upstream-identical and every refresh must re-strip the four
`models.dev` `$ref`s.** Guarded rather than documented: `BundledConfigSchema_HasNoExternalRef`
fails the build. ⛔ An **overlay** was considered and rejected — `BundleSchemas` copies *raw*
resources into archives and `RestoreEngine` parses each alone, so the restore path would still
meet an unresolvable external `$ref`, and its evaluate guard catches only `JsonException` /
`InvalidOperationException` / `ArgumentException`.

⭐ **CI restores, builds AND tests `ClaudeForge.slnx`** — so a project missing from it never
builds in CI at all, and **a local build cannot catch that.** That `.slnx` entry is the CI
surface. `AssemblyLayeringTests` needed **no** registration (it scans `src/`+`tests/` from disk
and `"OpenCode"` was already a product prefix) — canaried by pointing `AgentForge.Sdk.Tests` at
`OpenCode.Sdk`, which fails and names the edge. ⚠ Any `AgentForge.* → OpenCode.Sdk` edge is a
**build cycle**, so the shared-*test*-project form is the only canary that compiles.

**The original plan text follows.**

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

- **`mcp`** — ✅ **DONE (Phase 9a-3).** Union on `type`. `McpLocalConfig` (`command[]` ·
  `cwd` · `environment` · `enabled` · `timeout`) vs `McpRemoteConfig` (`url` · `headers` ·
  `oauth` · `enabled` · `timeout`), where `oauth` is `McpOAuthConfig | false`. Not
  `McpServersEditorViewModel` — Claude's transport model differs.

  > ⛔ **THE UNION HAS THREE ARMS, not the two described above.** Read from the bundled
  > schema: alongside the two `$ref`s, `mcp.additionalProperties.anyOf` carries an **inline
  > `{ "enabled": boolean }`** with `required: ["enabled"]` and
  > `additionalProperties: false` — a toggle for a server another scope declares, without
  > restating it. That is the commonest *project-level* MCP entry there is, and an editor
  > built to the two-arm description classifies every one of them as unparseable. Also
  > measured: `oauth` is genuinely **three**-state (object / literal `false` / absent), since
  > the schema's enum permits only `false` — a nullable bool cannot carry it.

  > ⛔ **CORRECTED (Phase 9a-3, measured). Behaviour (a) is real; behaviour (b) is the
  > opposite of what this template does.** The claim was: it (a) **preserves per-variant
  > non-discriminator fields across a variant switch**, so flipping a server local↔remote
  > doesn't destroy the fields the other arm didn't use, and (b) **echoes an unknown variant
  > back unchanged** rather than dropping it.
  >
  > (a) holds — via `MarketplaceListEntryViewModel.ExtraFields`, captured at hydration and
  > replayed on save. **(b) is false.** `MarketplaceListEditorViewModel.TryHydrateEntry`
  > returns `null` for an unknown `source` and the caller does `continue`, so the row
  > **vanishes on load**; `ToVariantObject` returns `null` under the comment
  > `// unknown source — drop on save`, so it **vanishes again on save**. Its own comments
  > say so. The *reasoning* in (b) was right — a variant the editor doesn't know must
  > survive — but the cited evidence was wrong, which is worse than no evidence: it sends
  > the next implementer to copy the opposite behaviour from the one they were told to
  > reproduce.
  >
  > **What 9a-3 built instead**: (a) reproduced (both arms held simultaneously, never
  > swapped), and (b) built from the permission grid's proven echo pattern — but at
  > **per-entry** granularity, so one server written by a newer OpenCode doesn't make the
  > other twelve read-only. `OpenCodeMcpCodec` holds an unclassifiable entry verbatim and
  > writes it back byte-for-byte; unsurfaced fields on a *recognised* entry survive too.
  > **`plugin[]` should follow 9a-3, not the marketplace editor.**
- **`permission`** — ✅ **DONE (Phase 9a-2) — this was the v1 gate.** A purpose-built
  **two-level tool × pattern grid** plus the bare-string ("apply to all tools") mode. A
  tester binds over `OpenCodePermissionModel`; the **guided builder does not** — Claude's is
  a rule-syntax generator and stays Claude-side (see Problem 5).

  > ⚠ **Phase 6 had already rejected sharing the tester's AXAML** (both templates carry
  > `x:DataType`, so sharing means dragging Claude view-models into the neutral shell or
  > dropping compiled bindings). So the tester here is OpenCode's own view over
  > `Resolve` — which is the only reading of "the shared tester binds over
  > `OpenCodePermissionModel`" that Phase 6's own measurement permits.
  >
  > ⭐ Ships **more than the bullet asked for**, because the shape demanded it: per-row
  > reorder that makes last-match-wins visible, **shadowed-rule detection** (a narrow `deny`
  > from a lower-priority file landing before a broad rule and silently ceasing to apply),
  > action-only-tool enforcement, and an unparseable value echoed back untouched.
  >
  > ⛔ **A real inversion was found and fixed here.** Two rows may carry the same pattern;
  > JSON cannot. Collapsing onto the **first** occurrence's slot turns
  > `[git *=allow, *=ask, git *=deny]` into `{"git *":"deny","*":"ask"}`, whose last match for
  > `git status` is the broad `*` — **saving the file inverted the user's own rule.** The
  > survivor now keeps the last action at the **last position**.
  >
  > ⭐ **Reused as a child editor by `agent{}`** (9a-5), because `Config.permission` and
  > `AgentConfig.permission` are the same `$ref` — so nested overrides get the shadow scan,
  > the reordering and the tester with no second implementation.
- **`agent{}`** — ✅ **DONE (Phase 9a-5).** Object keyed by agent name with 7 named
  built-ins plus arbitrary keys. 15 fields (the count was right), including a **nested
  `PermissionConfig`** that binds the permission grid as a child, and a `color` field that
  is a hex-or-theme-name union. The effective view shows *global permission → agent
  override*.

  > ✅ **`Config.permission` and `AgentConfig.permission` are the SAME `$ref`** —
  > `#/$defs/PermissionConfig`, byte-identical — which is what makes the grid reusable
  > rather than merely similar, and retro-justifies matching on `schema.Name` without the
  > path. `ActionOnlyToolsSchemaDriftTests` now asserts they stay identical, so a schema
  > refresh that splits them fails loudly instead of quietly editing the wrong shape.
  >
  > ⛔ **Two corrections.** `color`'s union is `string | enum(...)` where the first arm has
  > **no pattern**, so every string validates and the enum is a *suggestion list*, not a
  > constraint — treating it as closed would reject the hex values the union exists to
  > permit. And the field list includes **both `steps` and `maxSteps`**, plus a
  > **`tools` map the schema marks `@deprecated`** ("Use 'permission' field instead"),
  > neither of which the plan mentioned. `tools` stays editable — existing configs have it
  > and dropping a user's flags changes agent behaviour — but is surfaced as deprecated and
  > offered no "add" affordance where absent.
  >
  > ⚠ **`AgentConfig` does NOT set `additionalProperties: false`**, unlike the MCP variants.
  > Unknown fields are legal rather than merely tolerated, so preservation is a correctness
  > requirement here, not a courtesy.
- **`command{}`** — ✅ **DONE (Phase 9a-6).** Object keyed by command name; `template`
  required, plus `description` · `agent` · `model` · `variant` · `subtask`.
  ✅ **The plan's description was exactly right — the first one this phase that needed no
  correction.**

  > ⭐ **Two things the generic editor cannot show, and both change what the user does next.**
  > `template` is the **only `required` field anywhere in Phase 9**, so a missing one is
  > *reported* rather than invented — writing `"template": ""` would claim the body is empty
  > rather than absent. And a template containing `` !`…` `` **runs a shell command with the
  > user's privileges every time the command is invoked**, which is worth a banner: a
  > template pasted from a shared config is exactly where that goes unnoticed. Detection
  > only; nothing executes anything.
  >
  > ⚠ **`additionalProperties: false` here**, unlike `AgentConfig` — so unknown fields are
  > violations, which is *why* they are still preserved: a config from a newer OpenCode is
  > the normal way to meet one, and stripping it on save is worse than round-tripping
  > something this build cannot use.
  >
  > ⚠ **An entirely empty entry is NOT written**, deliberately unlike an empty *agent* entry
  > (legal, since no agent field is required). Typing a name and clicking Add gives you a row
  > to fill in, not a command — writing `{}` would put a schema violation in the config, and
  > on a live-write host that lands the instant you click. Any single field present is enough
  > to write it, so nothing typed is withheld.
- **`plugin[]`** — ✅ **DONE (Phase 9a-7).** `string | [string, object]` discriminated union
  (the schema has the tuple form even though the docs say otherwise). TUI section
  additionally gets its own `plugin[]` **and** `plugin_enabled{}` name→bool toggle map.
  ✅ **The plan's description was right.**

  > ⭐ **`Config.plugin` and the TUI's `plugin` are byte-identical**, so ONE editor and one
  > registration serve both — the same reuse the permission grid gets from the two
  > `permission` locations being one `$ref`. `OpenCodePluginCodecTests` asserts the two
  > schema fragments stay equal, and the `DataTemplate` guard reports `plugin` **twice**
  > (once per product), which is how we know the single registration really covers both.
  >
  > ⚠ **`"foo"` and `["foo", {}]` are different values and must stay apart.** The tuple arm
  > is strict — `prefixItems` with `minItems`/`maxItems` both 2 — so an empty options object
  > is a deliberate statement. Collapsing it to the bare form looks like tidying and is a
  > silent move to the other arm of the union.
  >
  > ⚠ **Options are edited as raw JSON, and that is the honest control**: the schema types
  > the second element as `object` with *no declared properties*, so there is no shape to
  > render fields for. Unparseable text keeps the **last good** options rather than dropping
  > them — a JSON box is unparseable most of the time it is in use, and a live-write host
  > would otherwise delete the user's config mid-keystroke.
  >
  > ⛔ **`plugin_enabled` first draft silently dropped any non-boolean value** — caught while
  > writing it, fixed with the same opaque arm every other shape in this phase carries.
- **`formatter`** · **`lsp`** — ✅ **DONE (Phase 9a-8).** `bool | object-of-configs`, i.e. a
  **four**-state mode (*absent / `false` / `true` / object*) over a per-language map.
  **Missed by draft 9**; without this they render as raw JSON.
  ⛔ **The plan described both keys as the same per-language shape. That is right for
  `formatter` and wrong for `lsp` three ways.**

  > ⭐ **The mode is genuinely shared and is the only shared part.** Both keys declare
  > byte-identical `anyOf: [boolean, object]` with the same description, so one abstract base
  > owns the four states — because folding any two of them is exactly the silent rewrite this
  > phase keeps finding, and two implementations would eventually disagree about which folds
  > are safe. ⚠ **`false` and absent behave alike; `{}` and `true` behave alike. Neither pair
  > is the same file.** Selecting *Configured* from *Not set* writes `{}` and turns the
  > subsystem ON, so it must never collapse to a key removal.
  >
  > ⛔ **Measured against the bundled schema, an `lsp` entry is NOT a formatter entry:**
  > (a) it is a **two-arm union** — either `{"disabled": true}` exactly (`required`, typed
  > `enum: [true]`, `additionalProperties: false`) or an object with a **required `command`**;
  > (b) its environment key is **`env`**, not `environment`, and both entry objects forbid
  > additional properties, so borrowing either name produces a config OpenCode rejects;
  > (c) it carries an extra untyped **`initialization`** object the plan never mentions.
  > A formatter entry, by contrast, is one shape with **nothing required** — so an empty one
  > is written as `{}` (the agent precedent) while an empty `lsp` entry is skipped (the
  > command precedent). Six of the eight 9a slices have now found the plan wrong or
  > incomplete; two confirmed it.
  >
  > ⚠⚠ **The consequence is a state one obvious click produces.** Unticking "disabled" on a
  > disable-only server leaves `{"disabled": false}`, which matches **neither** arm — the
  > first needs the literal `true`, the second needs a command. Same for an entry carrying
  > only `extensions`. The editor **counts and names** those entries; it never repairs them,
  > because inventing a `command` is a claim about the user's machine and deleting their
  > `extensions` is a claim about their intent. `command` is the **second** `required` field
  > found in Phase 9, after a command template's, and gets the same report-never-invent
  > treatment.
  >
  > ⚠ **`disabled` is a THREE-state checkbox**, not a two-state one. A plain box would omit
  > the key when unticked and so delete an explicit `"disabled": false` on the first save —
  > the same absent-vs-false distinction the mode picker exists to preserve, one level down.
  >
  > ⚠ **The name collision the factory has to survive:** `PermissionConfig`'s object arm also
  > declares a property called **`lsp`** — the permission rule for the `lsp` *tool*. Editors
  > are registered by property name, path-insensitively, so this is the first time that
  > choice could have misfired. It cannot, because the permission grid owns its whole subtree
  > and never dispatches its children back through the factory, and
  > `OpenCodeToolingCodecTests.PermissionsInnerLsp_IsARuleConfig_NotAServerMap` pins the two
  > shapes apart so a refresh that made them alike fails loudly.
  >
  > ⭐ **The `mcp` editor's argv and key/value list view-models moved to
  > `OpenCode.Avalonia/Editing/`** and are now shared by all three editors, rather than a
  > third copy of 230 lines of list machinery. Blast radius was 3 source files and one AXAML,
  > with the 18 existing MCP tests as the proof the extraction was faithful.
  >
  > ⛔⛔ **A screenshot pass found a defect in ALL SEVEN earlier editors.** This file hosts each
  > specialised editor under a `MaxHeight` in `src/OpenCodeForge/App.axaml`, whose comment
  > claimed "the view's own scrolling handles a long grid" — but **not one view in
  > `OpenCode.Avalonia` had a `ScrollViewer`**, so every `MaxHeight` *clipped* instead of
  > scrolling. Measured, not inferred: the UI-Automation tree put the `lsp` editor's last two
  > entries at y=1262 and y=1565 against a window bottom edge of y=1100, while the page's only
  > scrollable pane was already at 100% — and those two were exactly the entries the editor's
  > own red "matches neither form" banner was pointing at. **The editor named a problem the user
  > could not scroll to.** Every view now carries a `ScrollViewer`, App.axaml's comment is
  > corrected, and the permission and command editors were re-screenshotted for regressions.
  > *A `MaxHeight` over a view with nothing scrollable inside it is a clip, not a cap.*
- **`autoupdate`** — `true | false | "notify"`. Small, but it is Essentials card #14 and a
  three-state union is not a checkbox. Shares the tri-state control with that card.
  **✅ DONE (9a-9).** Shipped: `OpenCodeAutoupdateCodec` + `OpenCodeAutoupdateEditorViewModel`,
  registered by name, 7 codec tests + 17 editor tests.

  > ✅ **The plan's description of the shape was RIGHT** — worth stating after six of the previous
  > eight slices found it wrong. The schema is exactly
  > `anyOf: [boolean, {"type":"string","enum":["notify"]}]`.
  >
  > ⭐ **It is the ONLY `boolean | scalar` union in either bundled schema** — surveyed, not assumed:
  > 20 unions in the config schema, 924 in the TUI schema, and this is the one whose arms are a
  > boolean and a string. So there is no family to generalise for, and "shares the tri-state control"
  > has nothing to share with. (The TUI's 924 also confirm keybinds is the largest remaining slice.)
  >
  > ⛔ **"Essentials card #14" does not apply to this app.** Essentials is a ClaudeForge surface;
  > OpenCodeForge has no such page, and `OpenCodePageLayout` files `autoupdate` under **General**.
  > Checked rather than inherited from the sibling app's vocabulary.
  >
  > ⚠ **Not built on `OpenCodeToolingEditorViewModel`**, despite both being a four-state mode with
  > an opaque arm: that base carries an entry list (`IsConfigured`, `EntryCount`, `RefreshDerived`)
  > and a scalar would have to pin all three permanently to "empty", which is a worse lie than a
  > little repetition. The *idioms* are shared, not the base.
  >
  > ⚠⚠ **`"notify"` is matched `Ordinal`, so `"Notify"` is held verbatim rather than corrected.**
  > The schema's enum is exact, so reading the variant as valid would mean silently rewriting a
  > user's text into a different string on the next save. Both this and the no-fold rule are
  > canaried — `OrdinalIgnoreCase` and folding absent→`false` each turn the suite red.
- **`keybinds`** (TUI section) — ✅ **SHIPPED in 9a-10** [decision 2]: a searchable action
  list over the 184 actions with a key-capture control, lazily realized, **not** 184 generic
  wrappers and **not** the raw-JSON fallback. This is the largest single new editor in the
  plan and the main reason people edit `tui.json` at all.
  > ⛔ **The `…` in this description hid the two arms that decide the model.** Measured: each
  > action's value is a **four**-arm `anyOf` — `false` (`enum: [false]`, so the literal `true`
  > is admitted **nowhere**), `"none"`, an **inner three-way union** (bare string / key object /
  > event object whose `key` is itself string-or-object), and an **array** of that inner union.
  > `"x"` and `["x"]` are therefore different files, and collapsing a one-element array looks
  > like tidying while silently moving the value to the other arm — the fourth time this phase
  > met that trap.
  > **Realized-row count, measured live via UIA rather than the trace: 5 of 184** on load, 11
  > after scrolling — a virtualizing `ListBox` with a bounded viewport, deliberately with **no**
  > outer `ScrollViewer` (which would hand it infinite height and defeat the virtualization that
  > S6 asked for). Spike S6 is answered.
  > Search matches the action, its description **and its current binding**; the last is what
  > answers "what is Ctrl+C bound to?", which 184 action names cannot. Clash detection names the
  > other action and says **nothing** about which wins, because the schema states no precedence —
  > and a chord is **never** compared against a structured key, since equating `"ctrl+q"` with
  > `{"name":"q","ctrl":true}` means inventing the parser the pattern-less string arm refuses to
  > define. Capture writes the object form with only the modifiers actually held.
- **`theme`** (TUI section) — ✅ **SHIPPED in 9a-11.** Schema declares it a bare `string` with
  no enum (confirmed: no description and no examples either), so it offers a picker sourced
  from installed themes on disk plus free text — the same shape as the free-form
  `model` field, and in fact **the same code path**.
  > ⭐ **NO tenth editor was written, and that is the finding.** The library's
  > `EnumPropertyEditorViewModel` already *is* a picker-that-accepts-typing, and it derives
  > everything from its schema: options from `EnumValues`, free-form from a non-empty
  > `Examples`, per-option tooltips from `EnumValueDescriptions`. `SchemaTreeBuilder` already
  > promotes a string-with-examples to `Enum`. So the slice is a **schema overlay** plus a
  > **schema wrapper** — no view-model, no view, no `DataTemplate`.
  > ⛔ **The plan's path was half wrong.** `~/.config/opencode/themes/*.json` is one source, but
  > measured against the installed opencode v1.17.9 binary, `discover()` also scans
  > `.opencode/themes/*.json` in the **cwd and every ancestor**, and plugins contribute themes
  > through an `oc-themes` manifest entry. Only the config directory is scanned here, deliberately:
  > the `.opencode` walk is **cwd-dependent**, and a GUI editing a global file cannot honestly
  > claim to enumerate a set that changes with where `opencode` was launched from. The other
  > locations are named in the overlay's description instead.
  > ⛔ **A 37-name built-in theme list was found and rejected.** `zU` in that binary maps theme
  > ids to display names (dracula, nord, tokyonight …) — but it sits with `setColorScheme`,
  > `previewThemeId` and the `oc-2` default, i.e. it is the **web UI's** theme provider, not the
  > TUI's. The TUI's theme store has **no built-in table at all**: its source is the disk scan and
  > its initial value is `opencode`. So `opencode` is the only name asserted statically, and
  > everything else is discovered — which is why a static suggestion list could never have been
  > right for anyone. `default` is deliberately not set in the overlay: it would drive an
  > "(inherits: …)" watermark, and the binary's two candidate defaults disagree across surfaces.

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

**Slice 10a — the engine — ✅ SHIPPED (uncommitted).** `AgentForge.Artifacts` +
`AgentForge.Artifacts.Tests` exist, framework-only, with 18 tests. `ArtifactKind` /
`ArtifactForm` / `ArtifactScope` / `ArtifactRef` / `ResolvedArtifact` / `IArtifactSource`
and `ArtifactResolver`.

**Slice 10b — Claude's sources through the engine — ✅ SHIPPED (uncommitted).** Both
Claude surfaces now resolve rather than walk: `UserMemoryService.SnapshotFiles` (the Memory
inventory) and `EditableMemoryService.Snapshot` (Agents & Skills). **Their 25 and 15
existing tests pass unmodified**, which is the faithfulness proof the section asks for.
Suite 3,627 · 0 · 11; both apps trim-clean; 21 canaries, every one reddening the named
tests. New: `FileSystemArtifactSources.cs` (file-probe / directory / skill-directory
sources, product-free), `ClaudeScopes`, `ClaudeArtifactSources`,
`ClaudeEditableArtifactSources`, `ClaudePluginArtifactSource`.

> ⛔ **`IArtifactSource` lost two of its four members, and the second consumer is what
> proved them unanswerable.** One depth-bounded walk of `~/.claude/plugins` yields agents,
> commands AND skills, each from a different plugin — a different `ArtifactScope` — none of
> it known before walking. So a source cannot be asked for "its" kind or "its" scope.
> Splitting it into one source per kind to keep the properties would walk a plugin tree three
> times per page load to satisfy something the resolver never reads. The interface is now
> `Id` + `Enumerate()`; kind and scope are per-ENTRY, stamped by an
> `ArtifactSourceIdentity` in the ordinary single-kind case. **A plugin is a SCOPE, not a
> source** — which is also what made the closed `EditableMemoryScope` enum survivable: it
> stays as the editor's writability tag, while the per-plugin identity rides on
> `ArtifactScope.DisplayName`.

> ⭐ **The naming rule is the load-bearing part, and it is about identity rather than
> display.** An entry's `Name` is what resolution groups by, so a name that is too coarse
> manufactures a shadowing relationship that does not exist — and the UI would then state it
> confidently. Three cases, all now guarded: a recursive `rules/` walk names entries
> `common/security`, not `security` (Claude reads both files); a sibling tool's
> `.codex/AGENTS.md` is `.codex/AGENTS`, not `AGENTS` (Codex's memory does not shadow
> Claude's); a skill is named by its DIRECTORY, since every skill's file is `SKILL.md`.
> Display names are computed separately, from the file, exactly as before — which is why the
> old tests still pass.

> ⭐ **`ArtifactRef` carries a location and nothing else — no size, no timestamp, no
> subtitle — and that is what let one source list serve two different row shapes.**
> Discovery moved out; stat-and-shape stayed in each service. `UserMemoryFile` and
> `EditableMemoryEntry` are still built by the same code that always built them.

> ⭐ **`UserMemoryCategory` conflates kind and scope.** `PrimaryMemory`, `ProjectMemory` and
> `CrossToolMemory` are all `ArtifactKind.Memory`, separated only by origin — so the category
> cannot be recovered from an entry and is carried on the source that produces it. New guard:
> **every `UserMemoryCategory` value has at least one source.** The enum's own remarks said
> "adding a new category requires extending both the enum AND the service's dispatch" — prose
> nothing checked, and a category with no source renders as an empty group that reads "you
> have none of these" rather than "nobody looked".

> ⚠ **Measured, and it made one of my own new tests vacuous: `*.md` does not match
> `reviewer.md.bak`.** The `.bak` sidecar exclusion the walk has carried for a long time
> therefore only ever protected the **hooks** walk, the one place the pattern is `*`. A test
> written against `agents/` passes with or without the rule — a canary reddening one test
> instead of two is what exposed it. The exclusion is now applied to `*` walks only, and the
> test that pins it uses `hooks/`.

> ⭐ **Two true shadow relationships became expressible, and both used to be invisible.**
> The user's and the project's `settings.json` are now one artifact with a two-entry chain
> (they were two unrelated rows told apart by a hand-written "(project)" suffix — now derived
> from scope); a user agent and a plugin agent of the same name likewise. ⚠ **Both surfaces
> still list EVERY entry in a chain, not the winner** — they are browsable file lists, and
> hiding a file because another outranks it would leave a user unable to edit something
> sitting in their own home directory. Guarded on both pages; the canary that lists only
> `Effective` reddens by name.

> ⚠ **Precedence orders a chain for display. It is NOT a measured claim about which
> definition Claude executes** when two scopes hold the same agent or skill — that has not
> been measured here, and the resolver deliberately neither merges nor discards. The settings
> ordering (managed over project over user) IS documented behaviour. Recorded on
> `ClaudeScopes` so the distinction cannot quietly erode.

> ⭐ **The resolver ORDERS AND GROUPS. It does not merge, and that restraint is forced by
> S7.** Spike S7 measured that OpenCode's inline JSON and a markdown file of the same name
> **deep-merge, file winning per field**, so an inline-only `temperature` stays live. The
> sketch above ("returns the winner plus everything it shadowed") would therefore be
> structurally unable to express the truth — a UI built on it would say "the file shadows the
> inline definition", which is false. So `ResolvedArtifact` mirrors `LayeredValue` exactly
> (`Entries` chain · `Effective` head · `IsShadowed`), the whole chain comes out ordered and
> complete, and folding it into one artifact is **per-kind policy in the consumer**.
> `ArtifactForm` travels on each entry precisely so that fold is implementable.

> ⛔ **Three of this section's own claims measured wrong.** Counted, not inferred:
> - **"each resolving roots internally from `PlatformPaths.ClaudeHome`"** — two of the five
>   (`MemoryArtifactDeleter`, `MemoryFileWriter`) touch **no** `PlatformPaths` at all; they
>   already take paths as arguments and need no injection. And the dependency is far wider
>   than one member: `UserMemoryService` alone names **10 distinct** `PlatformPaths` members
>   (`UserSettingsPath`, `UserMcpPath`, `ManagedSettingsPath`, `ManagedSettingsDropInDir`,
>   `ClaudeJsonPath`, `ProjectSettingsPath`, `ProjectMcpPath`, `LocalSettingsPath`,
>   `CredentialsPath`, `ClaudeHome`), 11 across the folder — and `UserMemoryCategory.cs`, an
>   *enum* file, reaches for `CredentialsPath`. So 10c injects a **path provider**, not "a
>   root": several of those are specific Claude *files*, not directories under a root.
> - **"~10 call sites outside the Memory folder"** — **15 in `src/`, across only 3 files**
>   (`AgentConfigClientCore`, `AgentsSkillsEditorViewModel`, `MemoryEditorViewModel`). The
>   file count is the good news; the reference count is 50% over the estimate.
> - ⛔⛔ **"its existing Memory / Agents-&-Skills tests must pass unchanged" CONTRADICTS
>   "static→instance conversion".** There are **58 test references** to the five services. A
>   static→instance conversion rewrites every one of them, so the tests cannot both be
>   converted and unchanged. **Resolution: keep the statics as thin wrappers delegating to
>   injected instances**, exactly as Phase 3 did for `ConfigScope.User` (58 src uses vs 1,074
>   test uses) and left retirement to Phase 4. Unchanged tests then really are the
>   faithfulness proof, and the seam is real rather than shimmed.

> ⚠ **Adding a shared project needs FOUR registrations, and two guards caught the misses.**
> `ClaudeForge.slnx`, **both** `.slnf` filters (a shared project belongs to every product
> filter), and the test project likewise. `FilterIsSharedPlusExactlyOneProduct` failed on both
> filters until they were updated — and `SharedProjectsNeverDeclareAProductReference` was
> canaried against the new csproj and named it, so a new `AgentForge.*` project is inside the
> layering net automatically (it globs `AgentForge.*.csproj` across `src/` and `tests/`).

**Slice 10c — path-provider injection — ✅ SHIPPED (uncommitted). PHASE 10 IS COMPLETE.**
`ClaudeArtifactPaths` is a sealed class rooted at one user-profile directory;
`UserMemoryService.SnapshotFiles`, `EditableMemoryService.Snapshot` and
`FootprintService` all take one, with the existing statics kept as thin wrappers over
`ClaudeArtifactPaths.Default`. Suite 3,637 · 0 · 11; both apps trim-clean; 7 canaries, all
matching their written-down predictions. The 40 pre-existing Memory / Agents-&-Skills tests
are still unmodified.

> ⛔ **This section's own counts included DOC COMMENTS, and one of them was 10a's.** Re-measured
> as code references after 10b: **14 references across 4 files**, using **9 distinct members** —
> not "~10 call sites" and not "11 across the folder". In particular, 10a's claim that
> "`UserMemoryCategory.cs` — an *enum* file — reaches for `CredentialsPath`" is **false**: the
> file names that path in **prose**, inside a `<summary>` explaining why credentials are excluded.
> A file that mentions a path in a comment has no dependency on it at all. *Grep counts static
> members; it does not distinguish code from documentation, and this phase now has two claims
> that went wrong the same way.*

> ⛔ **"A path provider, not a root" is half right, and the wrong half is load-bearing.** Every
> one of the nine members is `Path.Combine(<root>, <literal>)`:
> - **Six are rooted at the USER PROFILE** — and that is the correction. Not `ClaudeHome`:
>   `~/.claude.json` sits *beside* `.claude/` rather than inside it, and the cross-tool memory
>   probes (`.codex`, `.gemini`, `.opencode`) are siblings too. A `ClaudeHome`-rooted provider
>   could express neither, which is why 10b had to recover the profile with
>   `Directory.GetParent(home)` — a step 10c deletes, along with a null branch that could never
>   be taken.
> - **Three need no injection at all.** `ProjectSettingsPath`, `LocalSettingsPath` and
>   `ProjectMcpPath` are pure functions of a project root the caller already passes as an
>   argument. Routing them through a profile-rooted provider would imply a relationship that does
>   not exist — the same reason `MemoryArtifactDeleter` and `MemoryFileWriter` need nothing.
>
> **So the injection surface is one root and eight derived paths**, and after the slice
> `UserMemoryService`, `EditableMemoryService` and `FootprintService` contain **zero** code
> references to `PlatformPaths`.

> ⭐ **The literals are duplicated on purpose, and a drift guard is the price.**
> `ClaudeArtifactPaths` restates `.claude`, `settings.json`, `mcp.json`,
> `managed-settings.json`, `managed-settings.d`, `.claude.json` and `.credentials.json` rather
> than delegating to `PlatformPaths` — because a provider that delegates can only ever return the
> **process-global** paths, which is a seam in name only. Re-rooting has to be a constructor
> argument, not a mutation of a process-wide static, or profiles stay impossible.
> `EveryPath_AgreesWithPlatformPaths_ForTheSameProfile` pins the two equal member by member and
> names the one that drifted.

> ⛔⛔ **`Default` is a PROPERTY returning a fresh instance, and caching it breaks forty tests.**
> The underlying profile is `AsyncLocal`-backed and `AgentForge.Sdk.Tests` runs method-level
> parallel, so a cached static instance freezes whichever sandbox was current when it was first
> touched — every later test then reads another test's directory. Canary P2 turned one line into
> **40+ reds across five unrelated fixtures**, which is the clearest possible statement of why
> the property is written the way it is. The same trap applies per-instance:
> `FootprintService` resolves its default **per use**, never in its constructor, because the
> service is cached for the lifetime of an `AgentConfigClientCore`.

> ⭐ **A source-text guard keeps the seam open**, because a static property read leaves no
> per-type trace in assembly metadata — there is nothing for a reflection guard to see, and the
> assembly-level table `AssemblyLayeringTests` uses cannot say which type did the reading.
> `InjectedPathSeamTests` scans `src/AgentForge.Sdk/Memory/*.cs` with comments stripped and
> allows exactly four reads: `PlatformPaths.UserProfile` in the provider, and the three project
> functions. Without it the conversion is a one-time cleanup: a reintroduced static read compiles,
> passes every test, and silently ignores the paths the caller handed in.

> ⚠ **One of 10c's own new tests was vacuous, and writing the predicted red names down first is
> what caught it.** The first `FootprintService` lazy-resolve test constructed the service and
> then asserted through the **static** `ResolveCategoryPath` wrapper — which never touches the
> instance field, so it passed whether the constructor captured the default or not. Rewritten
> against `GetProjectTranscriptStatsAsync`, an instance method that actually reads it.

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

**Sources per artifact kind**, all resolved through Phase 10's engine.

> ⛔⛔ **CORRECTED 2026-08-26 by measurement against the installed v1.17.9 binary.** The table as
> originally written was wrong in five ways, listed under **Phase 11a** below. It is reproduced here
> in corrected form; do not restore the earlier wording.

| Kind | Convention sources | Recursion & naming | Config-declared | Inline JSON |
|---|---|---|---|---|
| Agents | `<global>/agent(s)/*.md` · `.opencode/agent(s)/*.md` for **every ancestor** up to the worktree root | **recursive**; name keeps the relative path (`nested/reviewer`) | — | `Config.agent{}` — incl. 7 overridable built-ins (`plan` `build` `general` `explore` `title` `summary` `compaction`) |
| Commands | `<global>/command(s)/*.md` · `.opencode/command(s)/*.md`, same ancestor walk | **recursive**; same relative-path naming | — | `Config.command{}` (`template` required · `description` · `agent` · `model` · `variant` · `subtask`) |
| Skills | `.opencode/skill(s)/` and **`.claude/skills/`** per ancestor · `<global>/skill(s)/` · **`~/.claude/skills/`** · **`~/.agents/skills/`** | **recursive `**/SKILL.md`**; name comes from **front-matter `name:`**, flattened — *not* the folder, *not* the path | `skills.paths[]` · `skills.urls[]` *(listed, not fetched in v1)* | — |
| Rules | `AGENTS.md` **then `CLAUDE.md`** traversing upward · `<global>/AGENTS.md` · `~/.claude/CLAUDE.md` fallback | n/a | `instructions[]` — globs + remote URLs | — |
| **Plugins** | `.opencode/plugin(s)/*.{ts,js}` per ancestor · **both** `$OPENCODE_CONFIG_DIR/plugin(s)/` **and** `~/.config/opencode/plugin(s)/` | **flat** — the one kind that is not recursive; the extension stays in the name | — | `Config.plugin[]` (npm specs) · TUI `plugin[]` + `plugin_enabled{}` |

**Precedence is per kind, and the two ladders are inverted:**

| Kind | Highest first |
|---|---|
| Agents, Commands | **global** > worktree root > … > nearest ancestor — they deep-merge per field with global loaded last |
| Skills | worktree root > … > nearest ancestor > global > external (`~/.claude`, `~/.agents`) > built-in |

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

### Phase 11a — DONE (uncommitted). The OpenCode source list, and five corrections

**Suite 3,658 / 0 / 11** (+21: 20 in the new `tests/OpenCode.Sdk.Tests/Artifacts/`, 1 new repo guard). Both apps
trim-clean. **20 canaries; 18 matched their written-down prediction by name, 2 over-reddened and
the reason is recorded below.** No UI, and — after a reversal — **no change to
`AgentForge.Artifacts` at all**.

Shipped: `src/OpenCode.Sdk/Artifacts/` — `OpenCodeProjectWalk` (the ancestor chain),
`OpenCodeArtifactScopes` (the two ladders), `OpenCodeSkillArtifactSource` (front-matter naming),
`OpenCodeArtifactSources` (the list), plus an explicit `AgentForge.Artifacts` ProjectReference.

#### ⭐ How this was measured, and why it beats reading the docs

**OpenCode ships its own version-matched spec**, and the installed binary here is **exactly
v1.17.9** — the version the spikes probed. `opencode debug skill` extracts the built-in
`customize-opencode` skill; `opencode debug config` prints the fully resolved config, which names
every discovered agent, command and plugin. Seeded a throwaway git worktree plus a sandbox
`$OPENCODE_CONFIG_DIR` and read the answers off the tool. **The user's real config was never
written to.**

#### ⛔ Five corrections — and the vendor's own spec is wrong twice

| # | Claim | Plan said | Vendor spec said | Measured |
|---|---|---|---|---|
| 1 | Agent discovery | flat `agent(s)/*.md` | flat `<name>.md` | **recursive**, and the name keeps the relative path |
| 2 | Command discovery | flat | `**/*.md` | ✅ spec right, plan wrong |
| 3 | Project `.claude/skills/` | listed | **omitted** | ⭐ **plan right, vendor spec incomplete** — it *is* scanned |
| 4 | Plugin roots | `plugins/` only | project `.opencode/plugin(s)/` only | singular **and** plural, project **and** global |
| 5 | Precedence | project > global | — | ⛔⛔ **inverted for agents and commands** |

#### ⛔⛔ The precedence inversion is the one that would have shipped a lie

With the same agent name in the project and the global directory, the **global** file supplied the
winning `description` while a field only the project file set survived beside it — agent and command
files **deep-merge per field**, loaded nearest-ancestor outward and then global, **last writer
wins**. The same collision for a **skill** resolved to the **project** copy, as a single entry.
So the two kinds rank the same two directories in opposite orders. A page built on "project beats
global" would name the wrong winning agent on every machine that has a global agent directory —
confidently, with no visible failure. Within the project chain both kinds agree that the **farther**
ancestor wins, which is also counter-intuitive, and is not alphabetical: the nearer path sorts later
and still lost.

#### ⛔⛔ Skills are named by front matter, and a nameless skill silently never loads

A manifest in a folder called `dirname-x` declaring `name: frontmatter-y` registers as
**`frontmatter-y`**. Since the name is the identity the resolver groups by, naming skills after
their folder would *invent* shadowing between skills that merely share a folder name and *miss* it
between two that genuinely collide. Separately, a manifest carrying a `description` but **no
`name`** did not appear among the resolved skills **at all** — so a skill folder can sit on disk,
look complete, and be inert. It is listed anyway, under its folder name, because hiding it is
hiding exactly what the user is looking for; diagnosing it is the tab's job at render time.

#### ⭐ The reversal: the shared engine needed no change after all

The first move was to add opt-in recursion to `SkillDirectoryArtifactSource`. Measurement killed
it: OpenCode's skill naming comes from front matter, which the shared engine deliberately never
reads (`ArtifactRef` carries no parsed content, so enumeration stays a stat). So OpenCode gets its
own source in its own SDK — the precedent `ClaudePluginArtifactSource` already set — and the
edit to `AgentForge.Artifacts` was reverted. **Product-specific policy belongs in the product's
SDK; the win is that Claude's 40 unmodified tests stay untouched by a second product's discovery
rules.**

#### ⚠ Two more measured behaviours worth keeping

- **`$OPENCODE_CONFIG_DIR` does not relocate plugin discovery — it adds to it.** With the variable
  pointed at a sandbox, a plugin there *and* one in the real `~/.config/opencode/plugins/` both
  loaded, both reported as `scope: global`. Same family as the documented gotcha that a global
  `AGENTS.md` under that variable is silently ignored: the variable is honoured for some surfaces
  and not others, so **each surface must be treated on its own evidence.**
- **`plugin_origins[]` is an undocumented resolved-config key** giving `{spec, source, scope}` per
  plugin — exactly the provenance the Plugins tab needs, straight from the tool.
- **Without a git repository the upward walk does not stop.** Removing `.git` made an `.opencode/`
  above the former root readable. So a stray `.opencode/` in a home directory applies to every
  non-repository project beneath it — worth showing, not hiding.

#### ⛔⛔ THE DEFECT THIS SLICE ALMOST SHIPPED: `.gitignore` SWALLOWED ALL SIX NEW FILES

Caught at the very end, by reading `git status` rather than trusting it. **`src/OpenCode.Sdk/Artifacts/`
and `tests/OpenCode.Sdk.Tests/Artifacts/` did not appear in `git status` at all** — not even under
`--untracked-files=all`. Cause: `.gitignore` carried an **unanchored `artifacts/`** (the .NET SDK's
root `ArtifactsPath` output folder), and `core.ignorecase=true` on Windows made it match the capital
spelling too.

**All four source files and both test files were invisible to git.** They built, they tested green,
they published trim-clean — and they would have reached CI as a compile error in files that did not
exist, after a handoff note saying the slice was complete. Nothing in the repository could have
caught it: `EveryProjectOnDiskIsInTheSolution` checks *projects*, and there was no project to add.

Fixed by **anchoring the rule to the repo root** (`/artifacts/`), which is what it always meant —
nothing else in the tree relied on the unanchored form. And guarded, because prose is not a guard:
**`BuildFilePathIntegrityTests.NoCompiledSourceFileIsHiddenFromGitByAnIgnoreRule`** feeds every
non-`bin`/`obj` `.cs` file under `src/` and `tests/` to `git check-ignore` and fails naming any that
are excluded. Canaried by restoring the unanchored rule: it reddens by name and lists all six files.

⚠ **Its own implementation had the same shape of bug.** Feeding paths with `WriteLine` on Windows
sends `
`, git splits on `
`, and every path it tested carried a trailing `
`. It still matched
here because the rule is a directory prefix — but a rule matching an exact file name would have been
**missed silently and reported all-clear**. Now `--stdin -z` with NUL separators.

⚠ **It fails rather than skips when git is missing**, because a guard that opts out on the machines
where it cannot run is the decorative-protection problem it exists to catch.

#### ⚠ Canary notes

Two canaries over-reddened, and both were **my prediction being wrong, not the code**: making the
skill walk `TopDirectoryOnly` finds *nothing* in the ordinary `skills/<name>/SKILL.md` layout
(5 red, not 1), and truncating the ancestor walk removes every project source because the fixture
works from `repo/sub/deeper` (15 red, not 4). **The ancestor walk is a shared dependency of almost
every test in this file, so a canary on it is not isolable** — worth knowing before reading a future
run of it as a narrow result.

⚠ **Two tests reddened under no canary in the first batch** and had to be aimed at directly, which
is the whole point of tracking that. One was fine once the boundary itself was removed rather than
truncated. The other, `ABlankOrUnusableDirectoryYieldsNothingRatherThanThrowing`, cannot have its
blank-input guard canaried at all: deleting it sends a `string?` into `Path.GetFullPath(string)` and
the build fails on `CS8604` first. **The compiler enforces that line and no test can**, so the
remark says so instead of crediting it.

⚠ **The canary harness itself reported a false result first.** All sixteen came back
"predicted 1 → got 0" while the failure *counts* were exactly right: `dotnet test -v q` prints no
per-test names, so the name extraction was empty. **A count is not evidence that the named test is
the one that went red** — the harness now cross-checks the count against the number of names and
flags a mismatch.

### Phase 11b — DONE (uncommitted). The page's read model, and what a chain MEANS

**Suite 3,672 / 0 / 11** (+14). Both apps trim-clean. **17 canaries, all matching prediction, and
all 14 new tests proven non-vacuous.** Still **no UI** — that is 11c.

Shipped in `src/OpenCode.Sdk/Artifacts/`: `OpenCodeArtifactSemantics` (what a chain means, per kind),
`OpenCodeSkillManifest` (the bounded front-matter read), `OpenCodeArtifactInventory` (grouped,
ordered, diagnosed rows). `OpenCodeSkillArtifactSource` now reads through the shared manifest reader
instead of its own copy, so the name it **groups** by and the name a consumer **displays** cannot
drift apart.

#### ⛔⛔ "SHADOWED" IS A STRUCTURAL FACT AND A PER-KIND CLAIM — AND CONFLATING THEM LIES

`ResolvedArtifact.IsShadowed` only says more than one source declared the name. What OpenCode does
with the losers is **opposite** for its two families, both measured:

| Semantics | Kinds | What the losers are |
|---|---|---|
| `SingleWinner` | Skills (and every unmeasured kind, conservatively) | genuinely never loaded — "overridden" is accurate |
| `DeepMerge` | **Agents, Commands** | **live**, contributing fields the winner does not set |

**A row that prints "2 copies overridden" under a merging agent tells the user to delete a file that
is supplying settings.** That is not a cosmetic error, and it renders as a perfectly plausible row —
which is why `OpenCodeChainSemantics` is required reading before a consumer says anything about a
chain. An unmeasured kind defaults to `SingleWinner` rather than being assumed into the merging
bucket; the way to move one is to measure it.

#### ⚠⚠ THE CROSS-TOOL BADGE CANNOT BE ANSWERED BY THE SCOPE

Two of the three cross-tool skill roots are their own scopes, but **a project's own
`.claude/skills/` sits in the *project* scope — the same scope as `.opencode/skills/` beside it**.
Badging off `scope.Id` catches the two user-level roots and silently misses the copy inside the
user's own repository, which is the one most likely to be edited. `IsCrossTool` therefore takes the
whole `ArtifactRef` and reads the source id too, off constants the source list itself exports so a
restated string cannot drift. Canary I3 (scope-only) reddens exactly that test.

#### ⚠ THE THREE SKILL DIAGNOSES DO NOT SHARE AN EVIDENCE LEVEL, AND THE TYPE SAYS SO

- ✅ **Measured** — no `name:` → **never loads**. Absent from `opencode debug skill` entirely. This
  is the only one that sets `IsInert`.
- ✅ **Measured** — `name:` differs from the folder → loads under a name nothing in the tree
  suggests. A warning, not a failure.
- ⚠ **Spec-sourced, NOT measured** — no `description:` → per the bundled spec, "filtered out and
  never surfaced to the model". **Measured that such a skill IS still discovered**, so discovery and
  model-exposure are different questions and only the first is observable with these probes.
  Reported as a model-visibility warning, never as "this does not load".

`IsInert` is deliberately narrow for the same reason: a warning sharing a treatment with a hard
failure trains users to ignore both.

#### ⭐ A VACUOUS TEST, CAUGHT BY A CANARY THAT REDDENED NOTHING

`ItemsAreOrderedByName` seeded three agents into one directory — and removing the sort entirely
reddened **nothing**, because the resolver preserves source order and a single directory walk
already returns names alphabetically. **The filesystem was supplying the ordering the test credited
to the code.** Fixed by splitting the names across the project and global directories so the natural
order is wrong (`zebra, alpha, Mango`), which only the sort can fix; `Mango` also pins that the
comparison is case-insensitive. ⚠ Two canaries also had to be re-aimed because `if (false)` trips
`CS0162` under `TreatWarningsAsErrors` — the same trap this plan has now recorded three times.

#### Deliberately NOT done, stated not hidden

- **No UI.** The page, its tabs and the chain-expansion affordance are 11c. Everything above is
  headlessly testable precisely so the view can be a dumb projection.
- **Inline `Config.agent{}` / `command{}` are still not sources** — `ForPage` takes an environment
  and a directory, not a parsed config document, so `ArtifactForm.Inline` never appears yet. S7's
  per-field merge is implementable (`Semantics` is the hook) but not implemented.
- **`skills.paths[]`, `skills.urls[]`, `instructions[]`, `references{}` are not read.**
- **The plugin hook-name scan is not written** — the plan wants each plugin file listed with the
  events it subscribes to, from a shallow static scan. Deferred to 11c with the view that shows it.
- **Only the effective declaration is read for diagnosis.** Reading every copy would multiply file
  reads by chain length to answer a question about the artifact actually in force.

### Phase 11c — DONE (committed `eaa9c3d`). The page, and the first non-schema section

**Suite 3,689 / 0 / 11** (+17). Both apps trim-clean. **14 canaries; all 14 page tests proven
non-vacuous.**

Shipped: `OpenCodeArtifactsPageViewModel` + `OpenCodeArtifactRowViewModel` + the AXAML in
`src/OpenCode.Avalonia/Artifacts/`, 30 resx keys, `App.axaml` page templates, the `MainWindow.axaml`
`Content` binding, the navigation node, and `OpenCodePageTemplateTests`.

#### ⚠⚠ NOT VERIFIED VISUALLY — the first job of the next session

**The page has never been seen running.** No screenshot, no UIA pass. That is the standing rule for
every UI slice in this plan, and it has earned its keep every time — 9a-8's screenshot found that
**all seven** existing editors clipped instead of scrolling, a defect three slices old. Treat this
page as unproven until someone looks at it.

#### ⭐ A NON-SCHEMA PAGE WAS ALWAYS POSSIBLE; ONE LINE OF AXAML BLOCKED IT

`NavigationNodeViewModel.Editor` is typed `object?` and always was, so the navigation tree never
cared what kind of page a node owned. The blocker was `MainWindow.axaml` naming `SettingsPageHost`
directly. It now binds `Content` and `App.axaml` selects the view by view-model type — the same
mechanism the specialised editors already used.

⚠ That trades a compile-time guarantee for a runtime lookup, so it comes with a guard:
`OpenCodePageTemplateTests` walks the **real** navigation tree and requires a `DataTemplate` for
every page view-model it finds, which covers a third page kind the day it is added.

#### ⚠ THE PROJECT FOLDER IS ASKED FOR, NEVER GUESSED

Most of what this page explains is project-scope, and `Environment.CurrentDirectory` — the obvious
shortcut — is wrong for a GUI launched from a shortcut: it would confidently describe a project the
user is not in. With no folder set, the page names the sources that are therefore missing rather
than letting an empty list imply none exist.

#### ⛔ THREE OF THIS SLICE'S OWN TESTS WERE VACUOUS, AND CANARIES FOUND ALL THREE

1. The filter test seeded one agent and one skill and filtered for the skill — which passes whether
   or not the **unselected** tab was filtered, because the surviving row was the one searched for.
2. The reload test assigned the working directory the page **already had**, and the generated
   setter no-ops on an equal value, so no reload ever ran.
3. The empty-message test asserted only that two sentences **differ**, which a swap preserves while
   making both of them wrong.

⛔⛔ **Two mutation shapes are unusable here.** `if (false)` trips `CS0162` under
`TreatWarningsAsErrors`; deleting the only reference to a resx key trips the dead-string guard,
which emits **`error :`** rather than `error CS`, so a harness watching for the latter reports a
bogus "no summary" instead of a build failure. Where a canary would orphan a string, **swap the two
arms** — both stay referenced and the behaviour still inverts.

#### Deliberately NOT done

- **Read-only.** No create, edit or delete; a front-matter and markdown-body editor is a later slice.
- **No plugin hook-name scan** (the plan wants each plugin listed with the events it subscribes to).
- **No `skills.paths[]` / `skills.urls[]` / `instructions[]` / `references{}`**, and no inline
  `Config.agent{}` / `command{}` sources — `ForPage` takes an environment and a directory, not a
  parsed config document.
- **No deep-link or search routing** to the new page, though it carries a stable `NodeId`.

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

> ⛔⛔ **This section used to open "the card infrastructure already moved to the shell in Phase 5".
> It had not** — Phase 5 is still largely unspent, and the shell holds **no AXAML at all**. Taken
> literally the sentence sends you into the plan's own ⚠⚠ highest-risk phase (shell gains AXAML
> compilation, a control-library reference, the resx split) before a single card exists. Third
> ordering assumption in this plan that did not hold; see the Phase 11.5 notes for the other two.
>
> ✅ **Slice 1 (`7ac2223`) — DONE, on the agreed narrower route: the card VIEW-MODELS moved, the
> views did not.** `EssentialsCardViewModel`, `EssentialsCardKind`, `EssentialsCardKindConverters`
> and `ModelSuggestionItem` now live in `AgentForge.Avalonia.Shell/Essentials/`; each app keeps its
> own `EssentialsView.axaml`, which OpenCodeForge needs anyway since two of its card kinds render
> differently. ClaudeForge keeps `BuildCards` — the product half.
>
> ⭐ The three `Messages/` records moved **down** (`LayeredEditors.Avalonia` →
> `LayeredEditors.ViewModels`) rather than the shell reaching **up** into a control library for one
> record. They carry zero using directives and the target project already uses the same namespace
> prefix, so the move cost **zero call-site edits**.
>
> ⭐ The 14-parameter constructor is now `EssentialsCardOptions`, per this plan's own note to
> convert it "here, where the signature is already being changed".

> ✅ **Slice 2 — DONE. The two new card kinds, the page they render on, and the three cards that
> exercise them (#14 `autoupdate`, #16 rules, #17 active config).**
>
> **Sliced this way on purpose.** "Add the kinds" alone would have landed view-models that no view
> could draw, and this plan's own Phase 11.5 lesson (canary C8) is that the gap between a
> view-model guard and a markup guard is where an unguarded wiring lives. So slice 2 took the
> machinery end-to-end — `OpenCode.Avalonia/Essentials/` (view-model + `OpenCodeEssentialsView`),
> OpenCodeForge's nav node, its `App.axaml` template — and left the fourteen cards that reuse
> existing kinds as a purely additive slice 3.
>
> ⭐ **`LabelledEnum`, not "tri-state enum".** The plan's name describes `autoupdate`'s first use,
> not the mechanism: absent makes four states and an unreadable value makes five. What actually
> separates it from `EnumString` is that each option carries a **display label distinct from the
> token it commits** — which is what lets the card say "Notify only" while the file holds
> `"notify"`. The card reuses the full editor's own `Strings.Autoupdate*` resources and its
> ordering, so the two surfaces cannot describe one key with two vocabularies; a test asserts that
> against `OpenCodeAutoupdateEditorViewModel.Options` rather than against a copy of the words.
>
> ⭐ **`Derived` cards have no writer at all**, and the constructor enforces the pairing in **both**
> directions: a derived card carrying a writer looks editable and persists nothing; an editable
> card without one drops every edit. Neither has a visible symptom. Same for `LabelledOptions` on a
> kind that cannot render them.
>
> ⛔⛔ **Card #16 ships the SOURCED gotcha as sourced, because it could not be measured.** The
> `OPENCODE_CONFIG_DIR`-`AGENTS.md`-is-ignored claim comes from an upstream issue. Probed against
> the installed v1.17.9 with markers planted in both files: `opencode debug config` does not carry
> rules at all, and `opencode debug agent build` does not include their text — neither surfaces
> which `AGENTS.md` loaded. So the card reports the **observable precondition** (directory
> redirected, both files present) and says OpenCode *may* be ignoring one. Same treatment, for the
> same reason, as `OpenCodeArtifactIssue.SkillHasNoDescription`.
>
> ⭐⭐ **Two things measured against v1.17.9 while scoping this, both of which change what the code
> may assume:**
> 1. **`OPENCODE_CONFIG_DIR` IS honoured for the config file.** A distinct marker in each location
>    showed `debug config` returning the redirected file's value. `OpenCodePaths.GlobalDirectory`
>    is right.
> 2. ⛔ **`opencode debug paths` reports the DEFAULT config directory even when
>    `OPENCODE_CONFIG_DIR` is set** — it is a static path table, not a resolution. Do not "fix"
>    our resolution against that command's output. This is also the strongest justification for
>    card #17: OpenCode's own obvious diagnostic answers "which config am I using?" wrongly.
>
> ⛔⛔ **You cannot sandbox the running app from outside via `USERPROFILE`.**
> `PlatformPaths.UserProfile` falls back to
> `Environment.GetFolderPath(SpecialFolder.UserProfile)`, which on Windows reads the **token's**
> profile path, not the environment variable — an app launched with `USERPROFILE=<sandbox>` still
> reported `C:\Users\brian\.config\opencode\opencode.jsonc`. `PlatformPaths.TestUserProfileOverride`
> is the only lever and it is `AsyncLocal`/in-process. `OPENCODE_CONFIG_DIR` and
> `OPENCODE_CONFIG_CONTENT` *are* ordinary environment reads and do work out-of-process.
>
> **Consequence for card #16:** its shadowed-rules banner cannot be provoked by an out-of-process
> probe without writing into the real `~/.config/opencode`, so the predicate is covered by unit
> test and the *banner markup* was proven instead through card #17's inline-config danger, which
> is reachable. Anyone adding a card whose danger state depends on the home directory should plan
> for the same split rather than assume a launch-time sandbox.
>
> ⚠ **`Navigation.Insert(0, …)` silently broke the landing selection.** `SelectedNode` was
> `Navigation[0].Children.FirstOrDefault()` — correct while element 0 was a section header, `null`
> the moment a childless top-level node went in front of it, leaving a full tree beside an empty
> page area. One existing assertion (`Initialize_BuildsSettingsPagesForBothSections`) does catch
> it, confirmed by reverting; nothing said *which* page should be selected. Both are named now.
>
> ⚠ `NavigateToNavGroupMessage` had **no subscriber in OpenCodeForge** — every card's "View in …"
> button would have been a control that does nothing, with no compiler or runtime signal.
> Registered, and `INavigablePage` is now dispatched from `OnSelectedNodeChanged` so the two
> filesystem-derived cards re-read on arrival instead of showing startup state all session.

**Slice 3 — ✅ complete.** The other fourteen cards, all on kinds that already shipped, plus their
editor surfaces in `OpenCodeEssentialsView.axaml`. The measurements are recorded in the corrections
block above the card table — they were the expensive half, and they invalidate six of its rows.

Shipped as **19 cards, not 17**: two rows name two keys each (`permission.webfetch`·`websearch`,
`tool_output.max_lines`·`max_bytes`) and a card binds exactly one value, so splitting them keeps
each independently editable and independently dangerous.

| What landed | Why it is not what the table said |
|---|---|
| Severity read from `IDangerClassifier`, never written in `BuildCards` | Fixes the slice-2 defect where the two surfaces disagreed about `autoupdate`. Classified with a **null scope and null value** so the standing dot shows the setting's base tier and does not flicker as the user edits — the value-sensitive half is what `IsDangerNow` and the per-card predicates are for. |
| The `permission` cards **interlock** | `anyOf[bare action, per-tool object]`, and writing either arm destroys the other. Whichever form is in the file, the cards that cannot safely write it stand down with an explanation. |
| `plugin` is `Derived`, read-only | A `StringList` would read a `[name, options]` tuple as nothing and write the list back without it. |
| `model` / `small_model` free-form with **no** suggestions | The real set comes from the configured providers; a hardcoded subset would read as complete. |
| `default_agent` free-form, suggesting only `build` and `plan` | The five other schema-named agents are subagent, hidden, or absent from the binary. A user's own agents are added; those five never are. |
| **Per-card integer bounds** on `EssentialsCardOptions` (`IntMinimum` / `IntMaximum` / `IntIncrement`) | `subagent_depth` admits 0; `tool_output.max_lines` and `max_bytes` are `exclusiveMinimum: 0`. One range hardcoded in markup offers two of the three cards a value OpenCode rejects at load — and a rejected config bricks every command rather than falling back. |

The window takes the danger table from its **section list**, not from the opened-section local: the
client must have opened (`GetEffective` throws otherwise, inside a fire-and-forget read) but a
static tiering of keys need not, and greying out every dot on a section whose open threw would
strip the signal from the one user who cannot load their configuration.

### ⛔⛔ The interlock was defeatable in one visit, and only re-reading the code found it

**The guard was computed at read time and never recomputed after a sibling wrote.** Cards refresh on
construction and on arrival, so setting the global action left the five tool cards holding the
`EnumDisabled` they had computed *before* that write — enabled, with no notice. Two ordinary clicks
(global → `deny`, then bash → `allow`) replaced the bare string with an object and **deleted the
global rule covering every tool without a card.** Precisely the data loss the interlock exists to
prevent, reached through the interlock.

Nothing was asserting it: every interlock test loaded a file already in one arm and checked the
cards read it correctly, which is a different claim from *the cards stay correct as the file
changes underneath them*. Fixed by `RefreshPermissionCards`, called after either permission writer;
seven new `[DataRow]`-driven assertions cover both directions **and** the release case — the
refresh has to stand the cards back **up** when the last rule is removed, or the page latches
read-only after the first edit and still looks like a working interlock.

⚠ The refresh cannot recurse: every permission read sets `IsLoading` around its assignment and the
value-changed routers return early while it is set, so the re-read that lands on the card currently
being written raises no second write.

### Two things the canary pass corrected

⛔ **A test asserting a premise it could not produce.** `TheTiersSurviveASectionThatFailedToOpen`
planted malformed JSON to force a failed open, and passed. A premise assertion on
`MainWindowViewModel.Status` proved it was passing as a duplicate of its neighbour:
`ConfigFileLoader` catches `JsonException` deliberately, loading the file as an empty root and
recording `SettingsDocument.LoadFailure` rather than throwing — so **a parse-broken config opens
successfully**, and no amount of bad JSON reaches that branch. The invariant it meant to cover is a
statement about the page, and is now asserted directly against a null client.

⛔ **The kind-coverage guard could not see the `EnumString` flavours.** It scans for
`EssentialsCardKindConverters.Is*`, and both flavours live inside one container keyed on
`IsEnumString` — so deleting the free-form control left the *kind* with a surface while three cards
rendered no editor at all. Found by a canary whose prediction was deliberately `[]`. Closed by
`BothEnumStringFlavoursThePageBuilds_HaveASurfaceInTheMarkup`, which checks both directions against
`IsStrictEnumString` / `IsFreeFormEnumString`; those are card properties rather than kinds, which is
precisely why the converter scan is blind to them.

⭐ Thirteen mutations canaried in all, no missed reds. One deliberately-empty prediction was wrong in
the useful direction: cross-wiring the TUI danger table into the Config page **was** caught, because
the app-side spot-check compares against the named table rather than merely asserting "some card is
not Neutral".

Card #16's "link to the Rules tab" stays deferred: it targets the artifacts page's Memory tab rather
than a settings page, which is a different mechanism from `NavigateToNavGroupMessage`.

⛔ `OpenCodeEssentialsViewKindCoverageTests` fails the moment a card is built whose kind the markup
cannot draw — that is the guard working, and it is what stops a blank card shipping quietly.

Re-assert the `IsLoading`-must-not-span-`await` guard here
(`IntValueWrite_NotSuppressed_WhileReadIsInAsyncPhase`) — that bug class is not
Claude-specific and will recur in any new Essentials VM. ✅ Held in slice 2 by construction: every
read delegate in `OpenCodeEssentialsViewModel` is synchronous, and the reason is stated at the
delegate block.

### Phase 13 — Schema refresh: in-app + CI

> **The CI half is ✅ DONE (2026-09-08).** Both refresh scripts walk a three-schema table with
> the mandatory `models.dev` strip and line-ending-normalised comparison; the workflow's drift
> check covers the whole `Assets/Schemas/` directory and names the changed files in the PR body;
> `SchemaRefreshDriftTests` pins the property counts and the nav-map keys. Full write-up, with
> the two things the plan's table omitted, is in **CI changes** above.
>
> **In-app half: three of four landed 2026-09-09.**
>
> | Item | State |
> |---|---|
> | Network-first loading | ✅ `6260636` — memory → HTTPS (+ strip, + overlay) → bundled (+ strip, + overlay). No disk cache, no empty fallback. |
> | `SchemaProvenance` | ✅ `e47773d` — source, UTC timestamp, short digest, per schema file. |
> | Provenance badge | ✅ `6e0051b` — on the nav section header, following ClaudeForge’s nav layout. |
> | `--schema-source <bundled\|fetched>` | ✅ `839e3be` — `fetched` is FATAL on failure, not a fallback. |
> | *Check for schema updates* action | ✅ — About dialog in both apps. OpenCodeForge had no such dialog and now has one, plus the status-bar version button that opens it. |
>
> ⛔ **The per-product opt-in promotion is obsolete, not pending.** It existed to let a fetched
> copy outrank bundled; network-first makes that the default, so there is nothing to opt into.

> ✅ **PHASE 13 COMPLETE 2026-09-10.** The last item landed, and finishing it turned up two
> defects that had made the phase's earlier half less true than it read.
>
> ### ⛔⛔ ClaudeForge had never fetched a schema
>
> `App.axaml.cs` wrote `SchemaRegistry schemaRegistry = new()` from the **initial commit**.
> That was correct while the chain was bundled-first — bundled won for every product, so the
> `HttpClient` would never have been reached — and network-first (`6260636`) updated
> OpenCodeForge's composition root without touching ClaudeForge's. Its own commit message
> anticipated the shape of this ("a production site that forgets behaves as the app did before
> this change") without noticing that a site had.
>
> So for the whole of Phase 13 the **shipped** app built its pages *and* validated its saves
> against bundled schemas, while `CLAUDE.md` described a fetch it never made. Nothing failed;
> there was nothing to fail. Fixed, and guarded by `ProductionSchemaRegistryTests`.
>
> ⭐ One consequence is worth keeping: **ClaudeForge builds ONE registry and hands it to both
> SDK clients**, so its badge reports the copy save-validation uses too. OpenCodeForge builds
> three — its own plus one inside each client it constructs without passing one — so its badge
> is careful to speak only for the pages. Sharing is the better shape; OpenCodeForge does not
> do it yet.
>
> ### ⛔⛔ 26 test sites were resolving schemas over the live internet
>
> `new SchemaRegistry(new HttpClient())` across 21 files, written when an `HttpClient` here was
> inert. Network-first turned each into a live call to schemastore.org or opencode.ai.
> **Measured, not inferred:** a probe registry built the same way reported `Source=Fetched`.
> The suite's assertions therefore depended on what upstream served that day — which can redden
> a run for reasons outside this repo, or keep one green over a real regression by supplying a
> shape the bundled copy no longer has. All 26 moved to the offline default, which is what those
> sites meant before the default inverted.
>
> ⚠ **The guard's first draft missed two of them**, and the miss is the reusable lesson: it
> matched `new SchemaRegistry(new HttpClient())` and walked past
> `SchemaRegistry x = new(new HttpClient())`. Target-typed `new` puts the type on the left of
> the assignment, where a constructor-shaped pattern cannot see it — in the same file whose
> sibling regex already carried a comment about exactly that trap.
>
> ### What the action does, and three decisions inside it
>
> `SchemaRefresher` (in `AgentForge.Core`, not the Avalonia shell — it is registry logic with
> no UI) re-fetches each checkable product and reports per product: `Unchanged`, `Updated`,
> `Unavailable`, `Failed`.
>
> 1. **`Unavailable` is not `Unchanged`.** `RefreshAsync` drops the cached copy *before*
>    re-fetching, so a session that had a fetched schema and then fails to reach the network is
>    left on the bundled one. Pressing the button can move a registry **backwards**, and
>    collapsing that into "no updates" would report a downgrade as good news.
> 2. **A product with no upstream is omitted from the results, not reported up to date.** Claude
>    Desktop's schema is hand-maintained (`$id` is a bare token, descriptor URL `bundled://…`),
>    so no request is ever made for it. It also gets its own badge tooltip —
>    `SchemaBadgeTooltipNoUpstreamFmt` — because the ordinary bundled one says the app "tried to
>    fetch a newer copy and could not", which on that section points the reader at a network
>    problem they do not have.
> 3. **It refreshes but does not reload.** An `Updated` result means the badge and the pages now
>    disagree: the tree on screen was built from the previous copy. The summary says to reload
>    rather than implying otherwise. Reloading automatically would interrupt or discard unsaved
>    edits from a button whose label says *check*, and network-first picks the new copy up on
>    the next launch regardless. ⚠ In ClaudeForge, where one registry is shared, save-validation
>    *does* switch immediately — a tightened upstream constraint can fail a save against a rule
>    the visible tree never showed. It surfaces as a validation error, not silently.
>
> ### Deviation from this section's stated placement
>
> The spec says "on the About / Version page (next to the existing update check)". Three facts
> made that untransferable: ClaudeForge's existing update check is in the About **dialog**, not
> the Version Info page (the Essentials "Check for updates" card is a launch-time preference,
> not an action); the Version Info page is **per product**, and this action is global; and
> OpenCodeForge had neither surface. Resolved by putting it in ClaudeForge's About dialog
> directly under the app-update check — the literal neighbour intended — and giving
> OpenCodeForge an About dialog of its own.
>
> ⓘ **OpenCodeForge's dialog is deliberately not a mirror.** No app-update check, no copyright,
> no repository links: it has no release pipeline and sets no copyright attribute, so those rows
> would render blank or offer an action that does not exist. They belong with Phase 15. Its
> status-bar row now reserves space where it previously collapsed when empty — the cost of the
> app having a version visible anywhere at all, which it did not before.
>
> ⓘ `SummariseSchemaCheck` is duplicated in both apps' view-models. That is the same rule as the
> badge appliers: each formats from its own resx, and `NavigationNodeViewModel.Badge` is a plain
> string precisely so the shared library never learns what a schema is.
>
> **Verification:** suite **4,138 · 0 · 11** (+27). Nine canaries, every prediction exact —
> including two deliberately checking both arms of the no-upstream tooltip branch, which is the
> kind of test that passes for the wrong reason when only one arm is pinned.

> ⛔⛔ **RE-SCOPED 2026-09-09, because network-first landed and this section was written for a
> bundled-first world.** Read this before building any of it — three of its items are obsolete
> rather than pending, and one has changed meaning.
>
> | Item as specified | State |
> |---|---|
> | "fetches each registered schema URL" | ✅ **That is now the DEFAULT load path**, not an action. |
> | "writes to the disk cache" | ⛔ **Obsolete.** There is no disk cache; it was write-only and was removed. |
> | "**Opt-in promotion** … fetched outranks bundled only after the user opts in … default stays bundled-first" | ⛔⛔ **Obsolete and inverted.** Network-first is unconditional and default. There is nothing left to opt into, no per-product `WindowState` flag, and no promotion to wire. |
> | "the overlay is merged onto whichever base wins" | ✅ Done — and it is what made network-first safe. |
> | `SchemaProvenance { Source, FetchedUtc, Sha256 }` | ⬜ **Open.** Nothing records where a loaded schema came from. |
> | Provenance badge per product section | ⬜ **Open.** This is now the *main* deliverable. |
> | *Check for schema updates* action | 🔶 `SchemaRegistry.RefreshAsync` is the working primitive — it drops the memory cache, clears the offline latch and re-fetches. No UI surface yet. |
> | `--schema-source bundled\|fetched` | 🔶 **Changed meaning, and got MORE useful.** It was "exercise both sides of the opt-in". It is now a source override — and `bundled` is the only deterministic way to test the offline path without disabling networking. |
> | "`SchemaSnapshotService` gets this for free" | ⚠ Verify rather than assume. Fetched schemas load by default now, so new upstream properties should already light the ✨ NEW chips — which would mean the chips move on somebody else's release cadence. That may want a deliberate decision. |
>
> ⭐ **The phase's own motivation is already satisfied.** It opens: *"bundled-first, a new OpenCode
> key is invisible until ClaudeForge ships a release."* That is fixed. What remains is not
> freshness but **legibility** — a user looking at a settings page cannot tell whether its shape
> came from the binary or from the network five seconds ago, and neither can a bug report. That is
> what provenance is for, and it is why the badge outranks the action in what is left.
>
> ⚠ **`SchemaLoadPrecedenceTests` has already been rewritten** for the new order (13 tests,
> canaried 13 ways). The warning below — "this phase must not regress it … update them as part of
> the promotion work" — is discharged: there is no promotion work, and the tests now lock
> network-first in both directions, including that a fetched copy receives the overlay.

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

> 🔶 **PHASE 14 — NOT STARTED, AND THREE OF ITS PREMISES WERE CORRECTED 2026-09-10.** Measured
> against a real install by `scripts/probe-opencode.ps1`; the evidence is
> [`opencode-install-probe.json`](./opencode-install-probe.json) and the reasoning sits in the
> callouts below. **Read those before implementing any of this.** All three are premises that
> get built on long before anyone thinks to recheck them.
>
> | Premise as written | Status |
> |---|---|
> | Redaction target is `auth.json` | ⛔ **WIDENS.** Secrets are also four tables in `opencode.db` — and the planned `SensitiveKeys`/`JsonRedactor` work is a **JSON-key** classifier that cannot reach a SQLite table at all. A mandatory control that silently does not run |
> | Footprint categories: `storage/` · `log/` · `snapshot/` · `tool-output/` · `bin/` · `repos/` | ⛔ **Three do not exist, two are empty**, and the list omits every large item actually on disk — `node_modules/` included, at 11× the rest combined |
> | Exclusions: `node_modules/`, `package-lock.json`, `bun.lock` | ⛔ **Short by two.** Read the `.gitignore` instead — and note it has no trailing newline |
>
> ⛔ **The STRUCTURAL half is answerable now; the QUANTITATIVE half is not.** Every figure below
> comes from an install with **zero sessions**, so these sizes are *structure, not scale*.
> Growth, retention and prune ordering still need real usage —
> `usage.isUsedInstall` in the probe snapshot is the gate, and it currently reads `false`.

- **Backup** archives `~/.config/opencode/` — ⛔ **but not verbatim.**

  > ⛔ **A naïve archive of that directory is ~52 MB of regenerable dependencies.**
  > ✅ **Re-measured 2026-09-10** (`docs/opencode-install-probe.json`): `node_modules/` is
  > **55,058,354 B (52.5 MiB) across 3,458 files and 26 top-level entries** — **99.97 % of the
  > whole config root** — materialized to resolve plugin imports for a *single* declared
  > dependency, `@opencode-ai/plugin@1.17.9`. The earlier "~60 MB, 24 packages" was close
  > enough to act on and wrong enough not to quote.
  >
  > ⛔ **The exclusion list in this plan is SHORT BY TWO, and the `.gitignore` claim is why.**
  > This document says OpenCode "maintains a `.gitignore` in that directory listing exactly
  > those" and then names three: `node_modules/`, `package-lock.json`, `bun.lock`. The real
  > file has **five entries** — `node_modules`, **`package.json`**, `package-lock.json`,
  > `bun.lock`, **`.gitignore`**. `package.json` is named in this plan's own prose as
  > something OpenCode materializes, and then omitted from the list that excludes it.
  >
  > ⭐ **So do not hardcode the list — read the `.gitignore`.** "Honouring it is correct and
  > self-maintaining" was the right instinct; transcribing a snapshot of it into a plan is
  > what made it wrong. Parse the file at backup time and the count stops mattering.
  >
  > ⛔ **And parse it properly: the file has NO TRAILING NEWLINE.** `wc -l` reports **4** for
  > those five entries, because the last one — `.gitignore` itself — is unterminated. A reader
  > that only accepts newline-terminated lines silently drops the final entry, and which entry
  > that is depends on whatever order OpenCode wrote them in. Nothing about this fails loudly:
  > the backup simply includes a file the user was told it would exclude.
  >
  > ⚠ **And `~/.config/opencode/` is not the whole story.** State lives in
  > `~/.local/share/opencode/opencode.db` (SQLite + `-wal`/`-shm`), with additional roots at
  > `~/.local/state/opencode/` and `~/.cache/opencode/`.
  >
  > ✅ **Re-checkpoint item 2 no longer gates the STRUCTURE of this decision** — it still gates
  > the sizes. What is settled: the database is `journal_mode=wal`, and a copy of
  > `opencode.db` **without** its `-wal` is a stale snapshot by construction. Measured on this
  > install the WAL happened to carry no rows, but that is a property of an unused install and
  > must not be designed against. **A backup must either checkpoint first or copy all three
  > files together** — and the probe demonstrates the cost of getting it wrong the other way:
  > merely *opening* a `-wal` database checkpoints it, mutating the user's file. A backup is a
  > read; it must not leave a write behind.

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
- **Redaction is mandatory** — ⛔⛔ **and the file this plan named is not the whole target.**

  > **`~/.local/share/opencode/auth.json` is absent on this install — but that is NOT evidence
  > it has gone away, and the distinction matters.** Every account and credential table is at
  > **0 rows**, which means *nobody has ever authenticated here*. A file that only appears on
  > sign-in cannot be declared extinct by an install that has never signed in. **Keep
  > excluding it.**
  >
  > ⭐ **What IS machine-independent is the SCHEMA**, and it says secrets go to SQLite by
  > design. `opencode.db` carries four secret-bearing tables whether or not they hold rows
  > today, so the requirement **widens** — the database *in addition to* `auth.json`, never
  > instead of it:
  >
  > | Table | Secret-bearing columns |
  > |---|---|
  > | `account` | `access_token`, `refresh_token` |
  > | `control_account` | `access_token`, `refresh_token` |
  > | `credential` | `value` (`text NOT NULL`) — a generic store, so the column name gives nothing away |
  > | `session_share` | `secret` |
  >
  > ⛔⛔ **THE PLANNED MECHANISM CANNOT REACH THEM, and that is the real gap.**
  > `SensitiveKeys._segmentExact` and `JsonRedactor.SegmentExact` are **JSON key**
  > classifiers. They match a property path in a document. A SQLite table has no JSON path,
  > so adding `auth` to both — the whole of what this bullet asked for — redacts **nothing**
  > here and every existing test still passes. That is the shape of a mandatory security
  > control that silently does not run.
  >
  > ⭐ **So the decision to make is a policy one, before any code.** Either **exclude
  > `opencode.db` from the archive entirely** — simple, verifiable, and it costs the user
  > their session history on restore — or **redact within the database**, which means opening
  > it, rewriting four tables, and owning a schema that upstream changes without telling us.
  > The first is the honest default; the second is a feature nobody has asked for yet.
  > ⚠ Whichever is chosen, `credential.value` proves a **column-name classifier is not enough
  > either**: the sensitive column is called `value`.
  >
  > ⚠ **Keep the `auth` classifier work anyway.** `SensitiveKeys` / `JsonRedactor` parity is
  > still right for the JSON layers — `opencode.json` can carry provider keys — and
  > `SensitiveKeysParityTests` still enforces the `RedactedMarker` match. It is simply not
  > the answer to *this* bullet, and the two must stop being conflated.
- **Footprint page** mirrors the Memory page's Tier-2 view — ⛔⛔ **but the category list
  below was documentation, and measuring it broke most of it.**

  > This plan listed, over `~/.local/share/opencode/` alone: `storage/` (`message` · `part` ·
  > `project` · `session` · `session_diff`) · `log/` · `snapshot/` · `tool-output/` · `bin/` ·
  > `repos/`, flagged *"treat the documented layout as unverified (Spike S3)"*. It is now
  > verified, and **a page built to that list would show three categories that do not exist,
  > two that are empty, and would miss every large item on disk.**
  >
  > | Planned | Measured 2026-09-10 |
  > |---|---|
  > | `storage/` + its five children | ⛔ **DOES NOT EXIST.** Superseded by SQLite — `session`, `message`, `part` are *tables*. This is the substitution the plan feared, confirmed |
  > | `log/` | ✅ exists — 22,020 B, 1 file |
  > | `snapshot/` | ⛔ absent |
  > | `tool-output/` | ⛔ absent |
  > | `bin/` | ⚠ exists **under `cache`, not `data`** — and **EMPTY**, inverting its billing as the largest prune candidate |
  > | `repos/` | ⚠ exists, **EMPTY** |
  >
  > ⚠ `snapshot/` and `tool-output/` may simply be usage-created; absence on an unused install
  > is not proof they never appear. `storage/` is different — its contents are demonstrably
  > elsewhere, so that one is a permanent correction rather than a pending one.
  >
  > ⭐ **What the page must actually show, and none of it was on the list:**
  >
  > | Item | Root | Measured |
  > |---|---|---|
  > | `node_modules/` | config | **55,058,354 B** — **11.2× everything else on this list combined**, and the one real prune target |
  > | `models.json` | cache | 4,332,470 B — the entire cache is this one file |
  > | `opencode.db` (+ `-wal`, `-shm`) | data | 542,216 B combined, and **the only irreplaceable item** |
  > | `log/` | data | 22,020 B |
  > | `locks/` | state | 141 B |
  >
  > ⛔ **The prune ordering and the backup ordering are OPPOSITE, and the page has to say so.**
  > The biggest item is the most disposable (`node_modules` regenerates from `package.json`)
  > and the smallest meaningful one is the only thing a user cannot get back. A footprint page
  > sorted by size alone puts the irreplaceable database at the bottom and invites exactly the
  > wrong click.
  >
  > ⚠ **Still unverified:** the claim that `OPENCODE_DATA_DIR` overrides the data root and
  > accepts a comma-separated list. It is not one of the four variables `OpenCodeEnvironment`
  > models, and the probe did not test it. Do not build a multi-root walker on it without
  > measuring first.
  >
  > ⛔ **Sizes here are STRUCTURE, not scale.** Every figure is from an install with zero
  > sessions. Growth rates, retention and what is worth surfacing still need a used install —
  > that half of Phase 16 is genuinely open.

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

> 🔶 **PHASE 15 — EIGHT SLICES SHIPPED 2026-09-10.** `46b5dd5` publish-script app descriptor ·
> `d98845f` icon + Linux integration · `60c1011` real artifact + failed-RID exit · `2c95379`
> release workflow + guards · `c9bff28` winget · `cad1c28` app-update check · `600fcb7`
> `AssemblyProduct` + this block · `86570fd` both parity gaps closed.
> Suite **4,172 · 0 · 11**, green at every commit; both apps publish trimmed, single-file, zero
> ILLink warnings.
>
> ⛔⛔ **TWO CLAIMS IN THIS DOCUMENT WERE WRONG, and the repo had already ruled against one.**
>
> **"`release.yml`'s matrix gains an app dimension — 12 publish jobs" is not what happened, and
> must not.** A workflow is selected by its TAG TRIGGER, and the two apps' tag shapes are
> disjoint by construction (`v*.*.*` versus `opencodeforge-v*.*.*`). Widening one to catch both
> would attach ClaudeForge binaries to what users see as an OpenCodeForge release. `release.yml`'s
> own header said exactly this back in Phase 8 — *"A second app needs its own release workflow,
> not another trigger on this one"* — so the plan line was already stale when it was read.
> `release-opencodeforge.yml` is a separate workflow, and `ReleaseWorkflowTests` now enforces the
> disjointness by BUILDING each app's tag and asserting exactly one workflow accepts it.
>
> **"`AssemblyProduct` moves out of `Directory.Build.props` into the per-app csproj" was half
> done and half wrong.** OpenCodeForge already overrode it; the global default remained
> `ClaudeForge`, stamping that name into every shared `AgentForge.*` and `LayeredEditors.*`
> assembly. The default is now the neutral `AgentForge` and each app overrides it — measured out
> of the built assemblies, not assumed.
>
> ⚠ **`release.yml`'s header also claims `publish.ps1` "reads `$env:PublicVersion` … and embeds
> the version in archive filenames".** The filename half is false — archives are unversioned, as
> the workflow's own release-notes body says. `PublicVersion` appears in NO repo-owned file; it
> may still reach the external AutoVersioning generator through MSBuild's
> environment-variable-to-property mapping, which is **not verified**.
>
> **⛔ SIX DEFECTS FOUND, none of them anticipated here.** Recorded because the way each was
> invisible is the reusable part:
>
> | | Defect | Why nothing caught it |
> |---|---|---|
> | 1 | `publish.ps1` **always exited 0**, printing "Publish Complete!" in green, even when a RID's publish failed | `Publish-Rid.ps1` reports failure in a result object rather than throwing — right for an orchestrator, but nothing downstream read it. In CI a failed architecture gave a partial artifact set and a green step; `if-no-files-found: error` only fires when EVERY archive is missing, so it surfaced two jobs later at `gh release create` |
> | 2 | `Analyze-XamlClosures.ps1` filtered candidates with the regex `\linked\` | Windows-only by construction, on the one OS where four of the six RIDs build |
> | 3 | Publish logs were `publish-<rid>.log` while `dist/` is shared | The second app silently overwrote the first's — and that log is the input to the trim-warning scan |
> | 4 | The "did the suppression XML reach ILLink?" check **has never been able to succeed** | `dotnet publish` runs at minimal verbosity, where ILLink's command line is filtered out entirely. Measured on both apps: a clean publish is 31 lines with no ILLink mention. It printed a red "the csproj wiring is broken" for that |
> | 5 | Both winget paths staged `packaging/winget/*.yaml` | Correct with one app; the moment there were two it would have carried BOTH packages into one winget-pkgs PR |
> | 6 | The sibling's pre-release rule `[[ $GITHUB_REF_NAME == *-* ]]` | Marks **every stable OpenCodeForge release** as a pre-release, because the tag prefix itself contains a hyphen. Ported blindly it would have shipped that way |
>
> ⭐ **The app-update service is now SHARED, not copied.** Only five things in ClaudeForge's
> 278-line `AppUpdateService` were ever product-specific; the rest is `AgentForge.Core`'s
> `AppUpdateCoordinator` and both apps are thin statics over it. ClaudeForge's public surface is
> unchanged and its **12 existing tests pass unmodified** — the faithfulness proof.
>
> ✅ **THE TWO PARITY GAPS ARE CLOSED (`86570fd`)**, and closing them exposed a seventh defect.
>
> ⛔⛔ **`cad1c28` SHIPPED A DEFECT THAT DEFEATED BOTH NEW PREFERENCES.** `MainWindow`'s
> `Closing` handler built a **fresh three-argument** `WindowState` from the geometry it had —
> correct while the record held geometry and nothing else, silently destructive once it also held
> preferences: both update fields fell back to their constructor defaults on **every close**. The
> opt-out re-enabled itself and the dismissed-tag list emptied, so the banner returned on every
> launch however many times it was dismissed. ⭐ **Nothing could have caught it** — every test for
> those fields drives `WindowStateService` directly, where the round-trip is honest; the bug lived
> entirely in a caller that assembled the record itself. Geometry is now saved **BY NAME**
> (`SaveGeometry`, `SaveCheckForUpdatesOnLaunch`, both read-modify-write), so a caller that owns
> part of the record cannot drop the parts it has never heard of.
>
> - **The periodic re-check landed WITH disposal**, which is why it was held back.
>   `MainWindowViewModel` is now `IDisposable` and `MainWindow` disposes it on `Closing`. The loop
>   and the disposal are one feature: a detached 4-hourly task with nothing to stop it outlives
>   its window and keeps marshalling to a dispatcher for a window that is gone.
> - **The Essentials card inverted the dependency instead of fighting it.** The host hands the
>   page an `EssentialsAppPreference` (id, title, body, getter, setter) — the text included, so the
>   card shows the app's own resx string rather than a duplicate in the library. The card claims no
>   JSON path, no severity above `Neutral` and no "View in …" link, and each omission is tested: a
>   path would surface it under a filter for an unrelated key, and a danger dot would dilute what
>   one means beside keys that auto-approve tool execution. The About dialog's checkbox is gone —
>   one preference, one control.
>
> **⛔ WHAT REMAINS IN PHASE 15**
>
> - **The icon is PLACEHOLDER artwork** (`src/OpenCodeForge/Resources/OpenCodeForge.svg`).
>   Replacing it means regenerating the `.png` and the six-size `.ico` by hand; nothing does that
>   automatically, and `AppIconTests` fails deliberately when the placeholder marker is removed.
> - **Per-app `README.md` + screenshot galleries, `TRIMMING.md` baselines, and the two-app
>   signing procedure.** The signing script is still not in the repository.
> - ⚠ **Winget is not submittable-verified and cannot be from this machine**: it needs a signed
>   binary from a real release and would open a public PR. The derivation is verified — both apps
>   resolve to the right package id, tag, URLs and exe name, and each stages 3 manifests, not 6.

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


### Phase 16 — Re-validate against a used install **[promoted from a checkpoint, 2026-09-09]**

> 🔶 **PHASE 16 — PROBED 2026-09-10. THREE ITEMS FULLY ANSWERED, FOUR IN PART, ONE GATE CLEARED,
> TWO STILL OPEN — AND PHASE 14 STAYS BLOCKED.** Three of the answers contradict what this
> document recorded. The split matters more than a single total, so it is stated rather than
> rounded: **fully** 4, 6, 11 · **in part** 1, 2, 3, 5 — the structure, layout and mechanism are
> known, the *growth* is not · **gate cleared** 9 · **open** 7, 10.
>
> ⚠ **"In part" is not modesty.** Every partial has the same cause: shape can be read off an idle
> install, and rate cannot. That is the distinction Phase 14 turns on, so collapsing these into
> "answered" would hand the next reader a phase that looks finished and a Phase 14 that is not
> actually unblocked.
>
> `scripts/probe-opencode.ps1` measures all of it and writes
> [`opencode-install-probe.json`](./opencode-install-probe.json), committed, so the next re-check
> is a `git diff` rather than a re-investigation. It is also the field diagnostic when a user
> reports something the app got wrong.
>
> ⛔⛔ **THE HEADLINE IS A NEGATIVE RESULT, and the note carried into this session got it wrong.**
> That note read a 250 KB `opencode.db` beside a 260 KB `-wal` as *"sessions ARE in SQLite,
> confirming Phase 14's corruption risk"*. The **tables** exist; **every user-content table is
> empty.** `session`, `message`, `part`, `todo`, `credential`, `permission`, `event` — all **0
> rows**. Only `migration` (35, OpenCode's own schema bookkeeping) and `project` (1) hold
> anything, and that one `project` row's `worktree` points at **a deleted agent scratchpad from an
> earlier session of this project**. It is our own synthetic probing, not usage. The 250 KB is 61
> pages of schema, and `page_count` × `page_size` accounts for the file exactly.
>
> **So the install still has no accumulated session history, and every quantitative probe — growth
> rates, prune ordering, what a footprint page should show — remains unanswerable.** The snapshot
> computes that verdict itself (`usage.isUsedInstall`, `usage.blocksPhase14`) rather than leaving
> it to be re-formed from a wall of numbers: the next run flips one boolean, and that is the signal
> Phase 14 is waiting on.
>
> ⭐ **The `-wal` was measured, not reasoned about.** A WAL larger than its own database looks
> alarming. Counting rows twice — once with the `-wal` alongside, once from a copy of the database
> alone — returns identical counts, so here the WAL is migration churn that was never
> checkpointed and carries no user data (`walCarriedRows: false`). ⚠ **That is a fact about this
> install, not a general result.** A WAL *can* hold committed transactions, so
> "copy `opencode.db`" is still the wrong shape for a backup. The probe keeps the control so the
> claim never has to be re-argued from two file sizes.
>
> | # | Recorded here | Measured 2026-09-10 | |
> |---|---|---|---|
> | 1 | `data`, `state`, `cache` all **0 bytes**; populated layout unknown | `data` 564,236 B / 4 files · `state` 141 B / 2 files · `cache` 4,332,470 B / **1 file** · `config` 55,076,208 B / 3,464 files, each broken down per child in the snapshot | ✅ layout **ANSWERED**; growth still not |
> | 2 | *"if sessions live in SQLite, backup of `~/.config/opencode/` misses them entirely"* | ⭐ **Confirmed structurally, and wider than recorded: the database holds SECRETS.** `account` and `control_account` each carry `access_token` + `refresh_token`; `credential` is a store with a `value text NOT NULL`; `session_share` has `secret`. 20 tables, all user rows 0 | ⭐ structure **ANSWERED**; growth ⛔ |
> | 3 | `locks/` empty; *"a lock held during our save is a real failure mode on Windows"* | ⭐⭐ **The lock is a DIRECTORY**, not a file: `<sha1>.lock/` holding `heartbeat` and `meta.json` (token, pid, hostname, createdAt) — one per project, keyed by a 40-hex digest. A mkdir mutex is **advisory**: it acquires no OS lock, so it cannot block a config write. ⚠ **That is the mechanism, not a live test.** And it says nothing about `opencode.db`, which SQLite locks by its own means — the hazard for a Phase 14 backup reading that file is real and separate | ⭐ *what takes locks* **ANSWERED**; blocking untested |
> | 4 | `bin/` is *"the likeliest prune target and its size is unmeasured"* | ⛔ **The premise is inverted.** `bin/` is **empty** — 0 bytes, 0 files, confirmed twice. The whole cache is **one file**: `models.json`, 4,332,470 B. `packages/` is two empty directories left by this project's own synthetic plugin tests | ⛔ **ANSWERED — inverted** |
> | 5 | node_modules **60 MB** with one plugin | 55,058,354 B (**52.5 MiB**), **3,458 files**, 26 top-level entries, for the single declared dependency `@opencode-ai/plugin@1.17.9` — **99.97 % of the config root** | ✅ one-plugin baseline **ANSWERED** |
> | 6 | only the synthetic `global` entry existed | **Unchanged.** One project, `id=global`, `sandboxes: []`, worktree a deleted agent scratchpad | ✅ **ANSWERED — unchanged** |
> | 7 | permission merge proven on synthetic layers only | ⛔ **Nothing to answer with.** The global `opencode.jsonc` is **50 bytes — a `$schema` line and nothing else**. No `permission` block exists in any layer, and the `permission` table has 0 rows | ⛔ **STILL OPEN** |
> | 9 | S9 established from docs at the tag, not observed; `ProductVersionProbe` must gate it | Installed OpenCode is **1.17.9** — the tag S9 was established at, so the gate passes and the documented semantics still apply. The `~/.claude/CLAUDE.md` fallback actually firing is still **unobserved** | ⚠ **gate PASSES**, behaviour not |
> | 10 | install commands sourced, never executed | ⛔ Not runnable from here; it needs a clean machine or VM | ⛔ **STILL OPEN** |
> | 11 | version-matched; a bump may silently change the spec | **1.17.9 — the tag this plan is built on.** No drift, no re-extract this cycle. The probe records the version, so a bump shows up in the diff | ✅ **ANSWERED — no drift** |
>
> ⭐ **A finding that was not on the list: this app writes into the directory Phase 14 would back
> up.** `~/.config/opencode/cache/OpenCodeForge-gui-state.json` is OpenCodeForge's own window
> geometry, and `WindowStateService` puts it there deliberately — state belongs beside the config
> the app edits, and it honours `$OPENCODE_CONFIG_DIR` for the same reason. It is 64 bytes, so the
> footprint cost is nil. But a backup of `~/.config/opencode/` captures it and a **restore rolls
> the user's window back**, which is a support call nobody will connect to a restore.
>
> ⛔ **THE PROBE IS READ-ONLY BY CONSTRUCTION, and one of those reasons is not obvious.** Opening
> a SQLite database whose `-wal` is present **checkpoints it**, rewriting the user's file — so the
> three files are copied out and every query runs against the copy, and the sizes are read
> *before* the copy, because sqlite deletes the `-wal` the moment it touches one. The only
> statements issued are `COUNT(*)`, `PRAGMA`, and reads of `sqlite_master`: nothing selects a row,
> which is what makes it safe to commit the output of a database that has a `credential` table.
> Paths render as `~` and `<temp>`, and lock metadata contributes **key names only** — its values
> are a token, a pid and a hostname.

**Was the ⏱ Deferred re-checkpoint.** Promoted to a phase at the maintainer's direction because
treating it as a gate did not work: it was supposed to clear before Phase 10, and Phase 10
shipped without it. A gate nobody can satisfy is not a gate — the install still has no
accumulated usage, so the eleven probes cannot be run yet.

As a phase it is schedulable, reviewable, and cannot be shipped past silently.

**Scope: the eleven items in the re-checkpoint table**, re-run against an install with real
session history, diffed against what is recorded there. Nothing in it needs new code; it is
measurement, plus whatever the measurements invalidate.

⚠ **Item 8 is already answered** — `opencode debug agent <name>` was run during Phase 12 slice 3
once a provider was configured: `build` and `plan` report `"mode": "primary"`, `general` and
`explore` are subagents, `summary` and `compaction` are primary but `hidden`, and `title` does
not exist in the binary at all. That measurement is why `default_agent`'s picker suggests exactly
two built-ins. Ten items remain.

⭐ **Item 11 is not one-off.** Re-extracting `customize-opencode` is version-matched, so it
belongs in the routine on every OpenCode version bump rather than only in this phase.

**Phase 14 (Backup / Restore + footprint) is BLOCKED on this**, and that dependency is real
rather than procedural: 14's central decision — exclude `node_modules/`, `package-lock.json`
and `bun.lock`, ~60 MB of regenerable dependencies — was measured on an install where `data`,
`state` and `cache` were **all 0 bytes**. Sizes, growth rates and the prune ordering of a
footprint page cannot be derived from an empty install, and a backup that omits the wrong thing
loses user data.

**Phase 15 (Packaging and release) is NOT blocked** — nothing in it depends on OpenCode usage.

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

⛔⛔ **This is now Phase 16, not a gate.** It was specified as "must clear before Phase 10
ships, and again before Phase 14" — and **Phase 10 shipped without it**, because the install
still has no accumulated usage and the probes cannot be run. A gate nobody can satisfy gets
walked past; a phase gets scheduled. Promoted 2026-09-09 at the maintainer's direction.

**Phase 14 remains genuinely blocked on it** — see Phase 16 for why that dependency is real
rather than procedural. Phase 15 is not blocked. Re-run the probes below against the used
install and diff against what is recorded here.

| # | Re-check | Why it can't be trusted yet | Blocks |
|---|---|---|---|
| 1 | **`opencode debug paths` + a full `find` of `data`, `state`, `cache`** | All three were **0 bytes**. The populated layout is unknown. | Phase 14 |
| 2 | **`opencode.db` size, growth, and whether anything in it is user-authored** | It appeared only *after* the debug commands ran. If sessions live in SQLite, "backup `~/.config/opencode/`" misses them entirely. | Phase 14 |
| 3 | **`~/.local/state/opencode/locks/` — what takes locks, and does a running OpenCode block our writes?** | Empty. A lock held during our save is a real failure mode on Windows. | Phase 8 |
| 4 | **`~/.cache/opencode/` growth, esp. `bin/`** | Empty. This is the likeliest prune target and its size is unmeasured. | Phase 14 |
| 5 | **`node_modules` size on a real install** | 60 MB with **one** plugin. Users with several plugins will be larger. | Phase 14 |
| 6 | **`opencode debug scrap` with real projects — and what `sandboxes` holds** | Only the synthetic `global` entry existed. | Phase 10 |
| 7 | **A real multi-layer `permission` merge** | The inversion hazard was proven on synthetic layers. Confirm on real config, and confirm **last-match-wins** by actually triggering a rule. | Phase 6 / 9 |
| 8 | ~~**`opencode debug agent <name>` output**~~ ✅ **ANSWERED** in Phase 12 slice 3, once a provider was configured: `build`/`plan` are `"mode": "primary"`, `general`/`explore` are subagents, `summary`/`compaction` are primary but `hidden`, and `title` is **absent from the binary**. That is why `default_agent` suggests exactly two built-ins. | — |
| 9 | **Rule resolution end-to-end (S9)** | Established from **docs at the tag**, not observed. Confirm the `~/.claude/CLAUDE.md` fallback actually fires, and re-check the version — `ProductVersionProbe` must gate this. | Phase 11 |
| 10 | **S10 install commands** | **Sourced, never executed.** Each must run on a clean machine/VM. | Phase 8 |
| 11 | **Re-extract `customize-opencode`** | It is version-matched; a version bump may silently change the spec this plan is built on. | every phase |

✅ **The cheap insurance EXISTS as of 2026-09-10.** `scripts/probe-opencode.ps1` measures every
scriptable item above and writes [`opencode-install-probe.json`](./opencode-install-probe.json),
committed, so a re-check is a diff rather than a re-investigation — and the same script is the
field diagnostic when a user reports something the app got wrong.

⛔ **The table above is the record of what was BELIEVED, and three rows of it are now known to be
wrong.** Read Phase 16's status block for what was measured; do not "confirm" a row here without
re-running the probe. **Fully answered: 4, 6, 11** — row 4's premise inverted outright.
**Answered in part: 1, 2, 3, 5**, every one of them because shape reads off an idle install and
growth does not. **Gate cleared: 9.** **Still open: 7 and 10** — this install has no session
history and no clean VM.

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
| **Shared permission vocabulary** ✅ *(done, `a453063`)* | ~~`PermissionOutcome` and `Decision<TRule>` compile against both products' rule types.~~ **`Decision<TRule>` was rejected on measurement — see Phase 6.** What is asserted instead: `default(PermissionOutcome)` is `Default`, never `Allow`, read through an uninitialised property rather than a folded constant; and the vocabulary is exactly the three shared answers plus fall-through, so a fifth outcome has to be added deliberately. Claude's ~200 existing permission tests stayed **exactly where they are and unchanged**, as predicted — there is no extraction to prove faithful, because there is no extraction. | Draft 11 specified an extraction-parity suite here; with Phase 6 reduced to a vocabulary, that suite has nothing to test. |
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
