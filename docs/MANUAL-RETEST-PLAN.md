# ClaudeForge retest — outstanding items

⚠ **There are EIGHT items now, not seven** — `F3` joined the list on 2026-09-16 when its fix
landed, on the same footing as `F1`/`F2`/`F4`/`F6`: fixed, unverified against a running UI.

**Purpose: regress ClaudeForge for the split-library release.** Only what is still to do is below;
verified items are listed once at the end and should not be repeated. Findings go to
[`RETEST-FINDINGS.md`](./RETEST-FINDINGS.md).

| | |
|---|---|
| Build under test | ⛔⛔ **PACKAGE MODE, or this retest validates the wrong artifact.** `src/ClaudeForge/bin/Release/net10.0/win-x64/publish/ClaudeForge.exe` — ⚠ **this path is not in the repository and does not survive a clean.** A plain `dotnet publish -c Release` produces a `ProjectReference` build, and the release ships `PackageReference`; this phase gates an irreversible tag, so the two must not be assumed equivalent. Produce it in two steps, both pinned to the CalVer the packages tag will carry (decided at the **start** of this phase, not in advance): `pwsh -NoProfile -File scripts/package-canary.ps1 -PackOnly -CanaryVersion <CalVer>` then `dotnet publish src/ClaudeForge -c Release -r win-x64 --self-contained true -p:UseSharedPackages=true -p:SharedPackageVersion=<the same CalVer>`. A single-file exe is the whole output. ⛔ **Not through `src/publish/publish.ps1`**, which wipes `artifacts/localfeed` on purpose — here the local feed *is* the point. ⚠ Any exe built before this row changed is a `ProjectReference` build and does not count, including the `2026.3.917.1244` one |
| ⛔ Do NOT retest against `artifacts/` | Both Windows builds there — `a11y-untrimmed/` and `a11y-trimmed-loose/` — are **dated 2026-09-14**, so they predate the wholesale YAML front-matter parser replacement *and* the `F3` share-outcome work. They would exercise the **old write path**, which is exactly what `E1` exists to catch. They were left over from the `F5` investigation and are no longer needed for the accessibility pass either, because `F5` is refuted and the shipping artifact exposes a full tree |
| Date the binary before trusting a run | The version stamp encodes **build** time — `Starting ClaudeForge v2026.3.<MMDD>.<HHmm>` in `logs/app-*.txt`. Read it first on any "I don't see the fix" result, to separate a real defect from a stale exe |
| Logs | `logs/app-*.txt` and `logs/events-*.txt` **beside each exe** |

⚠ **Use a scratch project for anything that writes.** E1–E3 modify config; a throwaway directory
with its own `.claude/settings.json` keeps a mistake cheap.

⚠ **One ClaudeForge at a time.** Two instances on one `~/.claude` each see the other's writes as
external changes.

---

## The write path — what a library extraction actually risks

`docs/EXTRACTION-VERIFICATION.md` §4 states the gap:

> **No save, edit, backup or restore was exercised in either build.** The live comparison is
> read-and-render only. A regression in the write path would not appear in it.

Reading and rendering are covered. Everything that writes is not. These five are ordered by that
risk, one per shared library.

### ✅ E1 · Save preserves comments and formatting — `JsonC` — PASSED 2026-09-17

> Verified byte-exact against pristine baselines on the package-mode build `v2026.3.917.1839`.
> Comments (line and block), deliberate key order, ragged indentation and blank lines all survived;
> only the edited value changed. The folded `description: >-` round-tripped as prose, trailing
> newlines survived, and a non-ASCII **value** (`café — naïve ✨ 日本語`) came back untouched.
> ⓘ Two candidate defects were raised and both cleared **by measurement**: the writer does not
> escape user content (the `—` escaping is confined to the app's own generated stamp), and it
> does not rewrite line endings (round 1's mixed endings came from the tester's paste).
> ⛔ First attempt looked green while the agent files had **never been written** — "identical to
> baseline" is the absence of a test, not a passing round-trip. Confirm a write happened.

The newest shared library, with no prior release behind it. It replaced a serialize-and-overwrite
writer, so a regression here **destroys user content silently**.

**Do:** hand-write a `settings.json` containing comments, deliberate key order, blank lines and odd
indentation. Open it, change one value in the app, save, then diff the file.

**Pass:** only the edited value changed — comments, key order, blank lines and indentation all
survive.

**Fail:** a reformatted file, or comments gone. That is what the old writer did and what this
library exists to prevent.

⚠ **Also drive an agent/skill `.md` while you are here, not just a settings JSON.** On 2026-09-16
the YAML front-matter parser was replaced wholesale with `main`'s, plus two behaviours `main` had
lost, so the **artifact** write path moved underneath this item. Edit a skill whose `description`
is a folded block (`description: >-`) and one that ends in a blank line, save, and diff: the prose
must round-trip, and trailing blank lines must survive. Those two are exactly what the union fixed.

### ✅ E2 · Save lands in the right scope — `AgentForge.Core` + `LayeredEditors` — PASSED 2026-09-17

> All three scopes driven through UIA with distinct markers. Each landed in its own file and
> **nowhere else**: `e2-project-write` in `settings.json`, `e2-local-write` in
> `settings.local.json`, the User write in `~/.claude/settings.json`. Values survived a cold
> restart, and the effective view attributed the winner correctly (`LOCAL` / `(overridden)`).
> ⓘ A suspected layering defect — one surface showing `opus` while another showed the Local value —
> did **not** reproduce from a cold start and was not filed; see `F5` for why that discipline
> matters. The scope selector drives the editor, and each scope correctly shows its own value.

**Do:** change values at **User** and **Project** scope (and **Local** if present). Save. Reload the
window.

**Pass:** each value lands in the file for **its own scope**, survives the reload, and the effective
view shows the correct layer winning.

**Fail:** a value written to the wrong file. The scope ladder is shared-library code and is exactly
what an extraction can scramble.

### ✅ E3 · Backup, then restore — `AgentForge.Core.Backup` — **PASSED 2026-09-18** on the re-drive

> Re-driven against the package-mode build `v2026.3.918.839` after `F7` and `F8` were fixed,
> with the fixture project at `C:\c\cl\retest-2026.3.918` — **outside the home folder**, which
> is the condition the original failure needed.
>
> | | Before restore | After restore | Expected |
> |---|---|---|---|
> | project `settings.json` value | `CHANGED-AFTER-BACKUP` | ✅ rolled back | rolled back |
> | project `agents/retest-agent.md` | deleted | ✅ restored | restored |
> | `.pre-restore-*.bak` under the project | — | ✅ **0** | 0 |
> | `.pre-restore-*.bak` under `~/.claude` | — | ✅ **0** | 0 |
>
> ⭐ **Zero sidecars was measured, not assumed to mean "swept".** A final count of zero cannot
> tell *swept* from *never written*, and the result message that carries the count is not logged
> and clears before a settle-based probe can read it. So the trees were polled at 150 ms
> throughout a live restore:
>
> | Tree | Peak during | Final |
> |---|---|---|
> | `C:\c\cl\retest-2026.3.918` | **2** | **0** |
> | `~/.claude` | **6,023** | **0** |
>
> ⭐ **That same measurement is independent evidence for `F7`.** The original finding's tell was
> *"zero sidecars under the project — the restore never considered the path at all"*. A peak of
> **2** says it considered the path and wrote there.

<details>
<summary>The 2026-09-17 failure this replaces</summary>

### ⛔ E3 · **FAILED 2026-09-17**, two findings

> **Backup passed.** A *Settings only* archive was written, and the *Include API credentials?*
> consent dialog's promise held — `Omit` produced an archive containing **zero** credential
> entries, verified by reading the zip.
>
> **Restore failed on two counts**, both silent:
> - ⛔ [`F7`](RETEST-FINDINGS.md) — the archive **contained** the open project's four `.claude/`
>   files; restore put none of them back, wrote no sidecar under the project, and reported no
>   error. A changed value stayed changed and a deleted file stayed deleted.
> - ⛔ [`F8`](RETEST-FINDINGS.md) — **5,899** `.pre-restore-*.bak` sidecars remained after the
>   restore completed, roughly doubling the on-disk size of `~/.claude`.
>
> ⓘ User-scope restore itself worked correctly.

</details>


The largest piece of shared machinery, and destructive when wrong.

**Do:** take a backup. Change something. Restore.

**Pass:** the changed value comes back, the centre status pill reports success, and **no `*.bak`
sidecars remain**.

**Fail:** a partial restore, or surviving sidecars — `--cleanup-restore-sidecars` exists because
that has happened.

### ✅ E4 · Secrets stay redacted — `AgentForge.Sdk` — PASSED 2026-09-17, re-driven 2026-09-18

> **Re-driven on `v2026.3.918.839` after `F9` was fixed**, at Project scope, with two env keys
> the schema models and two it does not.
>
> ⭐ **The editor still does not RENDER the unmodelled keys** — `RETEST_MARKER_918` and
> `MY_CUSTOM_TOOL_PATH` appear nowhere on the page. That is the premise of the finding, live:
> the fix preserves what it declines to show rather than starting to show it.
>
> The save diff, which listed three changes before, now lists one:
>
> ```
> [Save] Claude Code settings — Project: 1 pending change(s)
> [Save]   "Modified" env.ANTHROPIC_API_KEY: [redacted] → [redacted]
> ```
>
> ⛔ **No `"Removed"` lines.** The 2026-09-17 run logged two. On disk afterwards, both unmodelled
> keys survive, both comments survive, and key order is unchanged — so `E1` rode along.
>
> ⚠ **One claim in the `F9` write-up was wrong and is corrected here.** It said the save dialog
> shows `[redacted]` values. It does not — the dialog renders the full old and new values
> (`"sk-ant-FAKE-retest-918-not-a-real-key"` → `"sk-ant-FAKE-EDITED-918"`); it is the **audit
> log** that redacts. Showing the user their own value on their own screen before writing it is
> defensible, and the argument the write-up built on it (that the dialog could not have let a
> user rescue a deleted value) is moot now that nothing is removed. Recorded because the claim
> was stated as measured.

> **Redaction passes.** The save diff redacts every env value, not just the obviously secret
> one: `"Modified" env.ANTHROPIC_API_KEY: [redacted] → [redacted]`. The planted key's raw value
> appears **nowhere** in `logs/app-*.txt` or `logs/events-*.txt`, and the backup `manifest.json`
> is clean (`includedCredentials: false`).
> ⓘ The raw value **is** present inside the backup archive's copy of `settings.json` — by design,
> and clearly warned: *"This mode preserves secrets verbatim"*, with a separate *Sanitized for
> sharing* mode offered. An archive that redacted secrets could not restore them.
> ⛔ **Driving this item surfaced [`F9`](RETEST-FINDINGS.md): editing any env value DELETES every
> env key the app does not model.** Reproduced twice. That is data loss, and it is the most
> serious finding of this retest pass.
> ⚠ Checking "the secret does not appear" is only evidence if the surface would otherwise carry
> it — `Get-BackupArchiveInfo.ps1 -SecretPattern` reports the key NAME count alongside the value
> count, and calls a zero-name result INCONCLUSIVE rather than a pass.

Two classifiers are deliberately duplicated across the layering boundary
(`JsonRedactor.IsSensitiveKey` in Core, `SensitiveKeys.IsSensitive` in Sdk), so drift between them
is live risk and the symptom is a secret in a log.

**Do:** with an `env` block containing e.g. `ANTHROPIC_API_KEY`, make an edit and save. Read
`logs/app-*.txt` beside the exe.

**Pass:** redacted everywhere it appears — audit log, save diff, backup manifest.

### ✅ E5 · Artifact resolution — `AgentForge.Artifacts` — PASSED 2026-09-17 (source attribution)

> **Every artifact is attributed to the right source**, which is what this item exists to check:
>
> | Scope | On disk | Listed | Source label |
> |---|---|---|---|
> | User skills | 7 | **7/7** | `User` |
> | Project agents | 2 | **2/2** | `Project` |
> | Plugin artifacts | many | yes | `Plugin (read-only)` + marketplace path |
>
> Enumerated with `scripts/retest/Get-VirtualizedRows.ps1`, which scrolls the list and accumulates
> rows because the list is virtualized (gap `G8`) and the filter box ignores programmatic text
> (gap `G9`).
>
> ⛔ **Two false conclusions were caught before being filed, both by a count that did not add up.**
> First, `LargeIncrement` scrolling jumped 0% → 84.2% in one step, enumerating **34 of 111** rows
> while finishing at a tidy "100%" — *a scroll that reaches the end is not a scroll that saw
> everything*. Second, the page has **three tabs** (Sub-agents / Skills / Slash Commands) and the
> user skills live on a tab that was never opened; all seven read as MISSING until then.
>
> ⚠ **The exact count criterion is NOT verified, and is recorded as such.** The app reports 111;
> scroll-enumeration recovered ~103 across the three tabs (rows realize lazily, so a sweep still
> misses some); disk holds 116 counting every plugin in every marketplace folder, while the app
> loads only *enabled* plugins. Both figures are approximations for different reasons, so the
> comparison proves nothing either way — it is **not** evidence of a defect, and it is **not**
> evidence of correctness.

**Do:** open Agents & Skills in a project having both user-level and project-level artifacts.

**Pass:** each is listed under the right source, and the counts match what is on disk.

---

## Also outstanding

### ✅ C3 · The `--cleanup-restore-sidecars` CLI tool — PASSED 2026-09-18, output included

> ⭐ Tested against real work on 2026-09-17: the **5,899** sidecars `E3` had just created. It
> removed **all of them** (5,899 → 0), opened **no window**, and left `~/.claude/settings.json`
> valid.
>
> ⛔ **The output was captured on 2026-09-18, and the reason it "could not be" was wrong.** The
> tool writes to **`Console.Error`**, not stdout — every line in `RunRestoreSidecarCleanup` is a
> `Console.Error.WriteLine`. Redirecting stdout alone captures nothing; merging stderr captures
> everything, with no terminal and no console attach involved:
>
> ```
> [ClaudeForge] Cleaning up *.bak restore sidecars under C:\Users\Janus\.claude…
> [ClaudeForge] Scanned 3 *.bak file(s); deleted 3 (0.0 MB reclaimed); 0 failure(s).
> ```
>
> ⭐ Driven against **three planted sidecars** rather than a zero-file run, so the counts are a
> measurement and not a format string over zeros. All three were gone afterwards, the app log
> carried the identical summary (`[Cleanup] Scanned 3 …`), `~/.claude/settings.json` stayed valid
> JSON, and **no process survived the run** — so the no-window claim is measured too.
>
> ⚠ The Fail note below still stands for an *interactive* launch: `AttachConsole` is what makes
> the lines visible in a terminal the binary was started from. What it does **not** do is prevent
> capture by a parent that redirects — the two were conflated.

Its call site changed in this batch: `Program.cs` now passes the home explicitly, where the message
and the walk previously resolved it independently.

```powershell
& "C:\c\cl\OpenForge2k\src\ClaudeForge\bin\Release\net10.0\win-x64\publish\ClaudeForge.exe" --cleanup-restore-sidecars
```

**Pass:** prints `Cleaning up *.bak restore sidecars under <the current user's ~/.claude>…`, reports a
scanned/deleted summary, and **exits without opening a window**.

**Fail:** a window appears, a different directory is named, or there is no output (run it from a
terminal you can see — it reattaches to the parent console).

### ✅ F3 · *Share config* now says what it did — PASSED 2026-09-17, all three surfaces

> Captured with `scripts/retest/Watch-TransientStatus.ps1`. ⚠ A settle-based probe **cannot see
> this** — the tree went 274 → 276 → 274 across one settle, so the pill appeared and cleared while
> the probe waited for stability, and the first attempt reported "no pill" for a pill that worked.
>
> | Surface | Pill | Side effect verified |
> |---|---|---|
> | Share config | *"Configuration copied to the clipboard."* | clipboard really changed — 6,509 chars of JSON |
> | Backup row *Share* | *"Backup archive revealed in your file manager."* | Explorer opened on the archive folder |
> | *Share Log* | *"Log file revealed in your file manager."* | Explorer opened on the logs folder |
>
> All three carried the **✓ icon** — not the grey icon-less legacy `StatusMessage` channel — and
> **self-cleared at 6.4–6.9 s** without needing a dismiss. All three say **revealed**, never
> *shared*.
> ⓘ **Location correction:** *Share Log* is on the **Version Information** page, not the About
> dialog. The About dialog carries only Close / Check for updates / Check for schema updates /
> GitHub Repository / Report an Issue.

**Do:** on the Effective settings page, click **Share config**.

**Pass:** the centre status pill reports *"Configuration copied to the clipboard."* on Windows,
and the clipboard really holds the JSON. The pill clears itself after ~6 s rather than needing a
dismiss.

**Fail:** no pill; a grey pill with no icon that never clears (that is the legacy `StatusMessage`
channel, and it means the emission bypassed the typed helpers); or a success message on a machine
where the clipboard did not actually change.

**Also check the two siblings, which now report too:**

- **Share log** — About dialog → *Share log*. Pass: the pill says the log file was revealed, and
  Explorer opens with it selected.
- **Share** on a backup row — right-click a row in the Restore tab. Pass: the pill says the
  archive was revealed, and Explorer opens with it selected.

⚠ Both say *revealed*, never *shared* — no platform here opens a share sheet, and claiming one is
the defect. A pill reading "shared" is a **fail**.

### ✅ B2 · Diagnostics-window accessibility — PASSED 2026-09-17

> Run against the **shipping** package-mode build, all three windows open:
>
> ```
> Tree settled at 291 descendants after 4.1s.
> Top-level windows: 3
>   walking: Live Config-File Events — Shift+F12 to hide
>   walking: ClaudeForge — retest-2026.3.917
>   walking: Live Debug Logs — F12 to hide
> Elements visited: 291
> Findings: 1
>   [1] focusable, no accessible name  type=Thumb
> ```
>
> **Zero findings in either diagnostics window.** The single finding is the scrollbar `Thumb`,
> already recorded as accepted noise. ⭐ The 26 chevrons are absent, independently confirming
> `F6`'s fix.
>
> ⓘ **No human keypress was needed after all** — `scripts/retest/Open-DiagnosticsWindows.ps1`
> drives F12 / Shift+F12. ⛔ Plain `SetForegroundWindow` **fails silently** from a background
> process (returns false, throws nothing), so the keys went nowhere and the binding looked broken;
> `AttachThreadInput` is the documented remedy and makes it reliable.
>
> ⚠ **A green audit means "nothing is unnamed", not "everything is named usefully."** This run
> reported 1 finding while seven controls were separately found announcing `Avalonia.Controls.Grid`
> — see [`F10`](RETEST-FINDINGS.md). The audit's rule is *focusable with **no** name*; a wrong name
> passes it.

The UIA audit covered the main window only; it walks whatever top-level windows exist when it runs.

**Do:** on the running untrimmed build, press **F12** and **Shift+F12** so both diagnostics windows
are open, then say so — `scripts/Audit-Accessibility.ps1` does the rest.

✅ **Run it against the SHIPPING build.** This line previously said an untrimmed build was required
because the shipping one exposed no accessibility tree. That was `F5`, and `F5` is refuted — the
shipping, trimmed, single-file artifact walks **168** UIA descendants. Auditing the untrimmed build
measures something users never run.

ⓘ The script now **settles before it walks**, so it may be started immediately after launching the
app; it prints the count it settled on and how long that took. Do not pass `-NoSettle`.

---

## ⏸ Parked

**OpenCodeForge has not been driven at all**, and waits until ClaudeForge's release work is done.
Setup is already in place and keeps: `artifacts/retest-opencode-config` holds a copy of the real
`opencode.json` plus a seeded `tui.json` with two deliberate keybind conflicts, reached via
`OPENCODE_CONFIG_DIR`. Its five items are in this file's git history.

---

## Verified 2026-09-14 — do not repeat

| | Evidence |
|---|---|
| **A1** themed brushes follow the variant | Confirmed across repeated toggles *and* navigation — where the old defect showed |
| **A2** glyphs render; Critical is red | `⚠` is a themed text glyph on Windows, not emoji or tofu. Dual coding intact |
| **A3** Critical red | ⛔ Caution fails in light theme — `F2` |
| **A4** Share config copies | ⛔ Silent success — `F3` |
| **A5** watcher arms *and* reacts | `armed for 6 of 6` (was 3 of 6); a file created while running logged `scope=Local, external change → reloading`, and `[App.Command] action=Reload` rebuilt 144 editors |
| **A6** F12 keyboard reach | Header links are chromeless `Button`s, so focus and Enter/Space come from the framework. Shared `HeaderLink.Create` carries it to the Shift+F12 window too |
| **A7** About dialog | Stray vertical line gone |
| **B1** theming both variants | Change pills, ✨ NEW badge, severity dots all correct |
| **C1** NEW badge reads its snapshot | No spurious badges; `--showAllNew` proved the badge renders, so the absence was genuine |
| **C2** snapshot written on close | `12:02:51` → `15:08:28` on a clean close |
| — | **layering display** confirmed incidentally: a Local-scope value arriving and winning |

---

## Reporting

For each failure: the item, the theme if visual, a screenshot where it helps, and the matching lines
from `logs/app-*.txt` and `logs/events-*.txt` beside the exe.

⚠ `src/publish/publish.ps1` deletes every `bin/` and `obj/` under `src/`, all of `dist/`, and now
`artifacts/localfeed` — and logs live beside the executable. Collect what you need **before**
running it.
