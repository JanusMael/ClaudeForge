# PROGRESS — work state

> **What this file is.** The resume anchor: where the work stands *right now*, what comes next, and
> which decisions are already settled. It is replaced, not appended — a status table that lags
> reality is worse than none.
>
> **Ground truth beats this file.** Treat every claim here as a hypothesis and reconcile before
> acting: `git log`, `git status`, the test suite. Let reality win on any conflict.
>
> For the *why* behind the architecture see [`CLAUDE.md`](./CLAUDE.md); for enforceable rules see
> [`AGENTS.md`](./AGENTS.md); for phase-by-phase detail and every measurement see
> [`docs/OPENCODEFORGE-PLAN.md`](./docs/OPENCODEFORGE-PLAN.md).

---

## Where things stand — 2026-09-11

| | |
|---|---|
| Branch | `feat/agentforge-opencodeforge` |
| Working tree | **dirty** — the OpenCodeForge Backup page is written and verified but NOT committed |
| Unpushed | 10 commits, plus the uncommitted work above. Nothing pushed, no PR opened |
| Suite | **4,239 passed · 0 failed · 11 skipped**, Debug (was 4,227; +12 new) |
| Trim check | Release `win-x64` publish clean for **both** apps, zero ILLink warnings |
| Observed | OpenCodeForge launches, builds its tree, writes the new persisted fields. ⚠ The Backup page itself has **not been seen rendered** — see *Verification gap* |

---

## ▶ RESUME HERE — commit, then the Footprint/Memory page

**First: commit what is in the working tree.** It is a complete, verified slice and it is large;
leaving it uncommitted is the main risk on this branch right now. Suggested split, coarse to fine:

1. `refactor(ui): TipCell moves to LayeredEditors.Avalonia` — the control plus the six ClaudeForge
   views whose `xmlns` follows it. Self-contained; ClaudeForge builds and its suite passes alone.
2. `fix(backup): the credentials prompt names the host's own credential store` — the
   `BackupPageOptions.CredentialsPathDisplay` member, the shell's use of it, ClaudeForge supplying
   it. A real defect fix, worth its own commit so it can be read on its own.
3. `feat(14): OpenCodeForge's Backup / Restore page` — everything else.

**Then: the Footprint/Memory page.** Same shape as the page just built and the last item of Phase 14
— `OpenCodeFootprint.Catalog` and `.Roots()` are built and tested, and nothing renders them. The
Backup page is now the worked example to copy: a host options record in
`src/OpenCodeForge/ViewModels/`, a view in `src/OpenCodeForge/Views/`, a `DataTemplate` in
`App.axaml`, a node from `MainWindowViewModel.InitializeAsync`, and a wiring test beside
`OpenCodeBackupWiringTests`.

⛔ **Phase 16's quantitative half still gates the Footprint page's numbers** — see *Known issues*.
The page can list categories and roots without them; it cannot show growth or retention rates.

---

## Done this session — 2026-09-11

Uncommitted. The goal was PROGRESS's own steps 1–4: give OpenCodeForge a Backup page.

| Area | What |
|---|---|
| **The page** | `OpenCodeBackupPage.Options` (the host half), `Views/BackupRestoreView.axaml` + code-behind, the `App.axaml` `DataTemplate`, and a top-level nav node built in `InitializeAsync` |
| **Strings** | 104 keys added to OpenCodeForge's `Strings.resx` and its hand-maintained `Strings.Designer.cs`. Generated from ClaudeForge's resx by script, with 17 rewritten for OpenCode and 10 dropped |
| **Persistence** | `WindowState` gains `BackupDirectory`, `RestoreDirectory`, `IncludeCredentialsInBackup`, `LastBackupUtc`; `WindowStateService.SaveBackupState` writes all four read-modify-write |
| **`OpenCodeBackup`** | `internal` → `public`. The host assembly is the one place the wrong engine would be supplied, so the right one has to be reachable from it |
| **`TipCell`** | Moved `ClaudeForge.Controls` → `LayeredEditors.Avalonia.Controls`; six ClaudeForge views follow it. The two products cannot reference each other, so the alternative was a second copy |
| **Defect fixed** | The shared credentials prompt hardcoded `~/.claude/.credentials.json`. Now `BackupPageOptions.CredentialsPathDisplay`, `required` like its siblings |
| **Trim** | `ILLink.Suppressions.xml` gains IL2026 + IL2075 for `Avalonia.Controls.DataGrid` — this page is the app's first real DataGrid, and the file's previous safety argument said in so many words that it would lapse the day one arrived |
| **Tests** | `OpenCodeBackupWiringTests`, 12 tests. Canaried: flipping the engine to `BackupEngine.Default` reddens two of them |

### Four decisions taken, with their reasons

- ⛔ **Two scope radios, not three.** `BackupMode.Full` differs from `SettingsOnly` only by which
  of a product's `SkippedSubdirs` it lets through, and `OpenCodeProducts.Config` declares none —
  the config root's own `.gitignore` does that work. Three radios would have put two options on the
  page producing byte-identical archives, distinguishable only by the label the manifest records
  and the Restore tab's Mode column then shows as if it meant something. Guard:
  `TheScopeRadiosOfferBackupAndSanitizedOnly`, which asserts the premise before the conclusion.
- ⛔ **No MSIX tab.** `MsixPathProbe` scans `%LOCALAPPDATA%\Packages` for a `Claude_*` package, so
  the shared VM's `ShowMsixTab` goes true on any Windows machine that *also* has Claude Desktop —
  offering, from inside OpenCodeForge, to repair another vendor's app. The view binds nothing from
  that surface. Guard: `TheViewBindsNothingFromTheMsixSurface`.
- ⚠ **Both products on the Clients list**, per the plan, even though `OpenCodeProducts.Tui`
  archives nothing of its own — see *Known issues*.
- ⚠ **No literal colours.** ClaudeForge's copy of this page paints its advisory banners with six
  light-mode hex literals; carried over they are unreadable in Semi Dark, and the repo's no-hex
  guard is scoped to view-models and would not have said a word. Guard:
  `TheViewUsesThemeTokensRatherThanLiteralColours`.

### Verification gap — read this before trusting the page

What was observed: the solution builds, 4,239 tests pass, both apps publish trimmed with zero
ILLink warnings, the published OpenCodeForge launches clean, and the new persisted fields appear in
a real `OpenCodeForge-gui-state.json` with the pre-existing fields intact — which means the page's
view-model really was constructed and its `PersistentStateChanged` really did reach disk.

⚠ **What was NOT observed: the page on screen.** Nothing drove the app to select the Backup node.
The headless test app is deliberately stripped of the App's resource dictionaries and so cannot
instantiate views, and OpenCodeForge has no `--deep-link` flag to navigate with. The standing
guards for this are `x:DataType` on every template — a mistyped binding is `AVLN2000`, a build
error, and the build is clean — plus `OpenCodePageTemplateTests`, which walks the real tree and
would fail if the node's view-model had no `DataTemplate`. **A backup has not been taken or
restored through the GUI.** The round trip itself is covered by `OpenCodeBackupRoundTripTests` at
the SDK level; what is unproven is the path from a button to it.

---

## 🔒 Locked decisions — do not relitigate

- **`tests/AgentForge.Core.Tests/Fixtures/*.zip` are frozen.** Never re-mint one to make a test
  pass. A change that cannot restore one needs a migration, or a *second* fixture beside it.
- **A host that backs up a product must also be able to restore it** — same set to
  `BackupRequest.Products` and `new BackupEngine(restorableProducts:)`.
- **`BackupMode` stays an enum** and cannot move to `AgentForge.Abstractions` (BCL-only by design).
  That is why `ProductSkippedSubdir.IncludedInFullBackup` is a bool rather than a mode name.
- **No second resx in the shell.** Each app's `Strings.resx` has a parity test hard-wired to its own
  directory; a resource set on the shell side would be unguarded. Hosts supply wording via
  `BackupPageText`.
- **OpenCodeForge's resx is English-only and declared so** — `ResxLedger` in
  `LocalizationParityTests` carries `("OpenCodeForge", false, …)`, and contracts #1–#4 run only
  against `src/ClaudeForge/Localization`. The 104 keys added this session therefore redden nothing.
  ⚠ Declaring this project *localized* is what would force #1–#4 to be generalised first.
- **OpenCode's config root is archived whole**, letting OpenCode's own `.gitignore` exclude the
  52 MiB `node_modules` — never a hardcoded skip list. Two earlier plan drafts got that list wrong.
- **`opencode.db` is opt-in with an advisory, never redacted**, and excluded from `Sanitized`
  outright. `auth.json` is never archived at all.
- **The manifest bump's trigger** is the first archive that can hold a non-Claude folder — not "the
  layout changed". That has now fired (v2).

---

## Known issues / debt

Newest first.

- ⚠ **The `OpenCode TUI` checkbox archives nothing of its own.** `OpenCodeProducts.Tui` carries no
  `BackupLayout`, so its sections are `ProductBackupLayout.Empty`; `tui.json` travels anyway
  because it sits inside the config root that `Config` archives whole. Ticking or clearing the box
  therefore changes only the archive's `manifest.clients` and which schema is bundled. Listing it
  is still the right call — it starts doing real work the day the product gains a layout — but the
  checkbox currently promises more than it delivers.
- ⛔ **A redirected config is not backed up.** `OpenCodeProducts.Config`'s section archives
  `OpenCodePaths.DefaultGlobalDirectory()`, not `GlobalDirectory(env)`. A user with
  `$OPENCODE_CONFIG_DIR` set gets an archive containing neither their `opencode.json` nor their
  `tui.json`, and the backup reports success. The literal is deliberate — plugin discovery reads
  the default directory regardless — but the backup wants *both* roots, not one. **Not introduced
  this session; surfaced by it.**
- ⚠ **A restore does not reload the open documents.** ClaudeForge passes `OnRestoreCompleted`,
  `IsAnyWorkspaceDirty` and `SaveAllWorkspaces` into the shared page; OpenCodeForge's window has no
  save-all pipeline to wire them to, so they are left unset. The editor keeps showing the
  pre-restore file until the app is restarted.
- ⚠ **`DisplayClients` does not abbreviate OpenCode's product names.**
  `BackupRowViewModel.AbbreviateClient` maps `claudecode`/`claudedesktop` to `Code`/`Desktop` and
  passes anything else through, so the Clients column renders `OpenCode+OpenCodeTui` in a 110 px
  cell. Verbose rather than wrong — the passthrough was designed for exactly this — and the
  tooltip carries the full text. The twin of the credentials-path defect, found by searching the
  shell for Claude-specific literals; left unfixed because the fix is another `BackupPageOptions`
  member for a cosmetic gain.
- ⓘ **Three accessible names on the Backup page are formatted in markup**, not resx —
  `{Binding DisplayName, StringFormat='{}{0} — Restore'}` and its two siblings. Carried over from
  ClaudeForge, which has the same three, so the two apps at least agree.
- ⓘ **ClaudeForge's `BackupRestoreView.axaml` declares a `BytesToHumanReadableConverter` resource
  it never uses.** Noticed while porting; left alone.
- ⚠ **Unlocalised strings moved, not introduced.** Progress labels on `ProductArchiveSection` and
  the restore advisory in `RestoreEngine` are English literals. They should be keyed by section id
  the way footprint labels are keyed by `FootprintCategory.Id`.
- ⚠ **`WithNoNetwork_BothSectionsSayBundled` flaked once** during a full-solution run on
  2026-09-11, then passed in isolation, in its own assembly, and in five further full runs.
  Unrelated to this session's changes; cause unknown. Not reproducible on demand.
- ⛔ **Phase 16's quantitative half stays blocked.** `usage.isUsedInstall` in
  `docs/opencode-install-probe.json` still reads `false`, so growth, retention and prune *rates* are
  unmeasurable — which gates the Footprint page's numbers even once the page exists.
- ⓘ **The local OpenCode install stays contaminated** from the previous session: three
  `opencode debug v2` runs fetched `models.json` and ripgrep and left two empty
  `~/.cache/opencode/bin/ripgrep-*` temp dirs. Recorded rather than hidden; deleting them is a user
  decision.
- ⓘ `CLAUDE.md` still says `src/publish/publish.ps1` "builds ClaudeForge only". Phase 15 shipped a
  separate `release-opencodeforge.yml`, so that line may be stale. Unverified.
