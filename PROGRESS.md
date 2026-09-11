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
| Last functional commit | `4ee8da8` — *refactor(shell): the Backup/Restore page moves out of ClaudeForge*. HEAD is the docs commit carrying this file, which sits directly on top |
| Working tree | clean |
| Unpushed | **10 commits**, this file's included — nothing pushed, no PR opened |
| Suite | **4,227 passed · 0 failed · 11 skipped**, Debug |
| Trim check | Release `win-x64` publish clean for **both** apps, zero ILLink warnings |

---

## ▶ RESUME HERE — finish Backup/Restore on the OpenCode side

The goal in one line: **OpenCodeForge has no Backup page.** `4ee8da8` made one possible; it did not
add one. Everything below it already works — OpenCode archives are written and restored, and the
round-trip is tested.

In order:

1. **Add an OpenCodeForge Backup page.** The view-model is already shared — construct
   `AgentForge.Avalonia.Shell.Backup.BackupRestoreViewModel` with a `BackupPageOptions`.
   - ⛔ **`Engine` MUST be `OpenCodeBackup.Engine`, never `BackupEngine.Default`.** The default
     engine writes OpenCode archives happily and restores **nothing** from them, reporting success.
     `OpenCodeBackupRoundTripTests` catches it; `AGENTS.md` carries the invariant.
   - `Products` = `OpenCodeProducts.All`; `AgentProcessNames` = `["opencode"]`.
2. **Write `OpenCodeBackupPage`** — the mirror of
   [`src/ClaudeForge/ViewModels/ClaudeBackupPage.cs`](./src/ClaudeForge/ViewModels/ClaudeBackupPage.cs).
   It needs **49 resx keys** in OpenCodeForge's `Strings.resx`. Copy the English wording from
   ClaudeForge's resx; the two Claude-named ones become OpenCode wording
   (`StatusAgentRunningFmt`, and the product checkbox labels via `ProductCheckboxLabel`).
3. **Add the view.** `src/ClaudeForge/Views/BackupRestoreView.axaml` is the template — it binds the
   shell VM already, so the copy needs the `xmlns:vm` shell namespace plus OpenCodeForge's own
   `loc:` keys (65 more strings live in the AXAML, on top of the 49 above).
4. **Wire the nav.** `src/OpenCodeForge/ViewModels/MainWindowViewModel.cs` ~line 421 builds the
   section list; Essentials is the precedent (page VM under `src/OpenCode.Avalonia/Essentials/`).
   Add the `DataTemplate` to `src/OpenCodeForge/Views/MainWindow.axaml`.
5. **Then** the Footprint/Memory page, which is the same shape: `OpenCodeFootprint.Catalog` and
   `.Roots()` are built and tested, and nothing renders them.

⚠ **Before step 2, check whether OpenCodeForge's resx has a locale parity test** like ClaudeForge's
(789 keys × 9 locales). If it does, 114 new English-only keys will redden it, and that is a
decision to take deliberately rather than discover.

---

## Done this session — 2026-09-11

All nine commits are on `feat/agentforge-opencodeforge`, none pushed.

| Commit | What |
|---|---|
| `bbadfe8` | `opencode debug v2` examined — **not** the v1/v2 surface (clean negative). Found: it *writes* and re-downloads ripgrep; Phase 16 item 4 re-opened (`bin/` is not empty); an orphaned 4.6 MB `models.json.*.tmp` nothing cleans |
| `9269000` | `FootprintCategory` enum → product data (`FootprintCatalog`, `FootprintSource`, `FootprintRoots`) |
| `746d540` | **Frozen pre-change archive fixture**, built *before* the layout moved |
| `9767c27` | Restore stops naming archive folders as literals |
| `8757582` | What a backup mode *includes* becomes data |
| `6d2d030` | That data moves onto `ProductDescriptor` (`ProductBackupLayout`) |
| `61193a7` | **OpenCode archives written and restored**; manifest v1 → v2 |
| `b48be1e` | OpenCode footprint catalog; restore advisory; `IncludedCredentials` defect fixed |
| `4ee8da8` | Backup/Restore page extracted from ClaudeForge into the shell |

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
- **OpenCode's config root is archived whole**, letting OpenCode's own `.gitignore` exclude the
  52 MiB `node_modules` — never a hardcoded skip list. Two earlier plan drafts got that list wrong.
- **`opencode.db` is opt-in with an advisory, never redacted**, and excluded from `Sanitized`
  outright. `auth.json` is never archived at all.
- **The manifest bump's trigger** is the first archive that can hold a non-Claude folder — not "the
  layout changed". That has now fired (v2).

---

## Known issues / debt

- ⚠ **Unlocalised strings I moved, not introduced.** Progress labels on `ProductArchiveSection` and
  the restore advisory in `RestoreEngine` are English literals. The repo's rule is resx-sourced
  user-visible text; they should be keyed by section id the way footprint labels are keyed by
  `FootprintCategory.Id`.
- ⚠ **`WithNoNetwork_BothSectionsSayBundled` flaked once** during a full-solution run on 2026-09-11,
  then passed in isolation, in its own assembly, and in four further full runs. Unrelated to this
  session's changes; cause unknown. Not reproducible on demand.
- ⛔ **Phase 16's quantitative half stays blocked.** `usage.isUsedInstall` in
  `docs/opencode-install-probe.json` still reads `false`, so growth, retention and prune *rates* are
  unmeasurable — which gates the Footprint page's numbers even once the page exists.
- ⓘ **This session contaminated the local OpenCode install**: three `opencode debug v2` runs fetched
  `models.json` and ripgrep, and left two empty `~/.cache/opencode/bin/ripgrep-*` temp dirs.
  Recorded rather than hidden; deleting them is a user decision.
- ⓘ `CLAUDE.md` still says `src/publish/publish.ps1` "builds ClaudeForge only". Phase 15 shipped a
  separate `release-opencodeforge.yml`, so that line may be stale. Unverified.
