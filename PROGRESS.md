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

## Where things stand — 2026-09-12

| | |
|---|---|
| Branch | `feat/agentforge-opencodeforge` |
| HEAD | the `docs: handoff` commit carrying this file — `git log -1`. The last **functional** commit is `15af56a`, *fix(memory): the footprint's config root follows the config that loads* |
| Working tree | clean |
| Unpushed | **19 commits**, this file's included (`git rev-list --count @{u}..HEAD` — trust that over this cell). Nothing pushed; **no PR** (`gh pr list --head feat/agentforge-opencodeforge` is empty) |
| Suite | **4,245 passed · 0 failed · 11 skipped**, Debug |
| Trim check | Release `win-x64` publish clean for **both** apps, zero ILLink warnings |
| ⚠ Unverified | **The Backup page has never been seen rendered.** See *Verification gap* — this is the one soft spot in the work below |

---

## ▶ RESUME HERE — the Footprint/Memory page

The last item of Phase 14. `OpenCodeFootprint.Catalog` and `.Roots()` are built, tested and now
*correct* (see below); nothing renders them.

**The Backup page is the worked example — copy its five pieces:**

1. A host options record in `src/OpenCodeForge/ViewModels/` — see
   [`OpenCodeBackupPage.cs`](src/OpenCodeForge/ViewModels/OpenCodeBackupPage.cs).
2. A view in `src/OpenCodeForge/Views/` — see
   [`BackupRestoreView.axaml`](src/OpenCodeForge/Views/BackupRestoreView.axaml). Theme tokens and
   `Opacity` only, never hex; `x:DataType` on every template.
3. A `DataTemplate` in [`src/OpenCodeForge/App.axaml`](src/OpenCodeForge/App.axaml) — **not**
   `MainWindow.axaml`; the window binds `SelectedNode.Editor` into a `ContentControl` and the
   templates live at application level. A missing one renders the type name and logs nothing.
4. A node from `MainWindowViewModel.InitializeAsync`, with a `NodeId` constant beside
   `BackupNodeId`. Cache the VM in a field if it holds state worth surviving a rebuild.
5. A wiring test beside
   [`OpenCodeBackupWiringTests.cs`](tests/OpenCodeForge.Tests/OpenCodeBackupWiringTests.cs).

**Resx:** OpenCodeForge's `Strings.resx` is hand-maintained alongside `Strings.Designer.cs` — both
files, same keys, or the build fails. Every key must be referenced as the literal token
`Strings.<Key>` somewhere in the project or under `tests/`; the dead-string guard in
`Directory.Build.targets` is a **build error**, not a warning.

⛔ **Phase 16's quantitative half gates the page's NUMBERS, not the page.** `usage.isUsedInstall` in
`docs/opencode-install-probe.json` still reads `false`, so growth, retention and prune *rates* are
unmeasurable. The page can list categories, roots and current sizes without them.

### Two things worth doing alongside it

- ⭐ **Add a `--deep-link <nodeId>` flag to OpenCodeForge.** ClaudeForge has one; this app does not,
  which is precisely why the Backup page could never be driven to and looked at. One flag closes the
  standing verification gap for *both* pages. `src/OpenCodeForge/Services/DebugFlags.cs` — ⚠ it is a
  two-token flag, so it must advance the loop index explicitly and validate before assigning.
- The `Tui` checkbox and the restore-reload gap under *Known issues* are both small and both real.

---

## Done — 2026-09-12

Eight commits, all on `feat/agentforge-opencodeforge`, none pushed. In order:

| Commit | What |
|---|---|
| `6968ae1` | `TipCell` moves `ClaudeForge.Controls` → `LayeredEditors.Avalonia.Controls`; six ClaudeForge views follow it. The two products cannot reference each other, so the alternative was a second copy |
| `040d26b` | ⛔ **Defect:** the shared credentials prompt hardcoded `~/.claude/.credentials.json`. Now `BackupPageOptions.CredentialsPathDisplay`, `required` like its siblings, so a third host is a compile error until it answers |
| `1634a4d` | ⭐ **OpenCodeForge's Backup / Restore page** — host options record, view + code-behind, `App.axaml` template, nav node, persisted state, 104 resx keys, 12 wiring tests. `OpenCodeBackup` went `internal` → `public` |
| `24fc550` | docs: the anchor catches up with its own commits |
| `820a098` | docs: the redirected-config defect, measured with a throwaway canary |
| `86bee98` | ⛔⛔ **Defect:** a redirected config backed up to an EMPTY archive that reported success. Now `config/` ← `GlobalDirectory(env)` plus `config-default/` ← the default root when they differ, gated by a new `ProductArchiveSection.IncludeWhen` |
| `9883518` | docs: the twin survey — five `DefaultGlobalDirectory()` call sites, one of them a live bug |
| `15af56a` | ⛔ **Defect:** that twin — `OpenCodeFootprint.Roots()` measured the wrong config root. Fixed and guarded |

**Three defects, one shape.** All three were *a write path resolving through a different function
than every read path*. Worth holding as a pattern rather than three incidents: when a product
exposes `X()` and `DefaultX()`, anything that WRITES or MEASURES must justify which one it uses.

### Bisectability — checked, not assumed

`6968ae1`, `040d26b` and `1634a4d` were each rebuilt at their own commit in a detached worktree.
All three build alone; `040d26b` runs the pre-change suite green at 4,227, so the +12 arrive with
the page rather than with the defect fix underneath it.

### Verification gap — read this before trusting the Backup page

**Observed:** the solution builds; 4,245 tests pass; both apps publish trimmed with zero ILLink
warnings; the published OpenCodeForge launches clean; and the new persisted fields appear in a real
`OpenCodeForge-gui-state.json` with the pre-existing fields intact — so the page's view-model really
was constructed and its `PersistentStateChanged` really did reach disk.

⚠ **NOT observed: the page on screen.** Nothing drove the app to select the Backup node. The
headless test app is deliberately stripped of the App's resource dictionaries and cannot instantiate
views, and this app has no `--deep-link` flag. The standing guards are `x:DataType` on every
template — a mistyped binding is `AVLN2000`, a build error, and the build is clean — plus
`OpenCodePageTemplateTests`, which walks the real tree and fails if a node's view-model has no
`DataTemplate`. **No backup has been taken or restored through the GUI.** The round trip itself is
covered by `OpenCodeBackupRoundTripTests` and `OpenCodeRedirectedConfigBackupTests` at the SDK
level; what is unproven is the path from a button to it.

---

## 🔒 Locked decisions — do not relitigate

- **`tests/AgentForge.Core.Tests/Fixtures/*.zip` are frozen.** Never re-mint one to make a test
  pass. A change that cannot restore one needs a migration, or a *second* fixture beside it.
- **A host that backs up a product must also be able to restore it** — same set to
  `BackupRequest.Products` and `new BackupEngine(restorableProducts:)`. OpenCodeForge passes
  `OpenCodeBackup.Engine`; `BackupEngine.Default` restores Claude's two and nothing else.
- **`ProductArchiveSection.IncludeWhen` is consulted by the WRITER only.** What a restore may apply
  is decided by what is in the archive: the machine reading it need not have the environment of the
  machine that wrote it.
- **OpenCodeForge's Backup view offers two scope radios and no MSIX tab**, and uses no literal
  colours. All three are deliberate divergences from ClaudeForge's copy, each guarded by a markup
  scan in `OpenCodeBackupWiringTests` because an omission cannot be observed from a running VM.
- **The footprint's config root is ONE root**, unlike the backup's two — the backup carries both
  because both hold user-authored config, while the only footprint category against that root is a
  regenerable cache. Re-opening this needs a *measurement* of a redirected install, not an argument.
- **`BackupMode` stays an enum** and cannot move to `AgentForge.Abstractions` (BCL-only by design).
  That is why `ProductSkippedSubdir.IncludedInFullBackup` is a bool rather than a mode name.
- **No second resx in the shell.** Hosts supply wording via `BackupPageText`.
- **OpenCodeForge's resx is English-only and declared so** — `ResxLedger` in
  `LocalizationParityTests` carries `("OpenCodeForge", false, …)`, and contracts #1–#4 run only
  against `src/ClaudeForge/Localization`. ⚠ Declaring this project *localized* is what would force
  #1–#4 to be generalised first.
- **OpenCode's config root is archived whole**, letting OpenCode's own `.gitignore` exclude the
  52 MiB `node_modules` — never a hardcoded skip list. Two earlier plan drafts got that list wrong.
- **`opencode.db` is opt-in with an advisory, never redacted**, and excluded from `Sanitized`
  outright. `auth.json` is never archived at all.
- **No AI attribution trailers on commits**, per the global `CLAUDE.md`. That decision outranks a
  session instruction mandating one; the 18 commits on this branch carry none.

---

## Known issues / debt

Newest first.

- ⚠ **The `OpenCode TUI` checkbox archives nothing of its own.** `OpenCodeProducts.Tui` carries no
  `BackupLayout`; `tui.json` travels anyway inside the config root. Ticking it changes only the
  archive's `manifest.clients` and which schema is bundled. Listing it is still right — it starts
  working the day the product gains a layout — but the checkbox promises more than it delivers.
- ⚠ **A restore does not reload the open documents.** ClaudeForge passes `OnRestoreCompleted`,
  `IsAnyWorkspaceDirty` and `SaveAllWorkspaces` into the shared page; OpenCodeForge's window has no
  save-all pipeline to wire them to, so they are unset. The editor keeps showing the pre-restore
  file until the app is restarted.
- ⓘ **The two-roots survey.** Five `DefaultGlobalDirectory()` call sites in `src/`: one is the
  fallback inside `GlobalDirectory` itself; two — `OpenCodeArtifactSources.AddGlobalSources` and
  `OpenCodeEssentialsViewModel.HasShadowedGlobalRules` — already handle both roots deliberately and
  are the precedent the backup fix followed; the fifth was the footprint bug, now fixed. ⚠ Both
  correct sites compare with an unconditional `OrdinalIgnoreCase` where the backup's new
  `SameDirectory` asks the real OS. On Linux two roots differing only in case would read as one
  there. Vanishingly unlikely; on the record rather than silently inconsistent.
- ⚠ **`DisplayClients` does not abbreviate OpenCode's product names.**
  `BackupRowViewModel.AbbreviateClient` maps `claudecode`/`claudedesktop` and passes anything else
  through, so the Clients column renders `OpenCode+OpenCodeTui` in a 110 px cell. Verbose rather
  than wrong — the passthrough was designed for it — and the tooltip carries the full text.
- ⓘ **Three accessible names on the Backup page are formatted in markup**, not resx —
  `{Binding DisplayName, StringFormat='{}{0} — Restore'}` and two siblings. Carried over from
  ClaudeForge, which has the same three, so the two apps at least agree.
- ⓘ **ClaudeForge's `BackupRestoreView.axaml` declares a `BytesToHumanReadableConverter` resource it
  never uses.** Noticed while porting; left alone.
- ⚠ **Unlocalised strings moved, not introduced.** Progress labels on `ProductArchiveSection` and
  the restore advisory in `RestoreEngine` are English literals. They should be keyed by section id
  the way footprint labels are keyed by `FootprintCategory.Id`. ⚠ This session added one more:
  `"Restoring the default opencode config root…"`.
- ⚠ **`WithNoNetwork_BothSectionsSayBundled` flaked once** on 2026-09-11, then passed in isolation,
  in its own assembly, and in every full run since. Cause unknown; not reproducible on demand.
- ⛔ **Phase 16's quantitative half stays blocked.** `usage.isUsedInstall` is still `false`, so
  growth, retention and prune *rates* are unmeasurable.
- ⓘ **The local OpenCode install stays contaminated** from an earlier session: three
  `opencode debug v2` runs fetched `models.json` and ripgrep and left two empty
  `~/.cache/opencode/bin/ripgrep-*` temp dirs. Deleting them is a user decision.
- ✅ **RESOLVED 2026-09-12** — `CLAUDE.md`'s "builds ClaudeForge only" line was indeed stale.
  Verified against `publish.ps1`'s own `-App` parameter (which documents `-App OpenCodeForge`) and
  against `.github/workflows/`, which carries both `release.yml` and `release-opencodeforge.yml`.
  The paragraph is corrected and carries a dated note saying what it used to claim.
