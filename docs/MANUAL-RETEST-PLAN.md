# ClaudeForge retest — outstanding items

⚠ **There are EIGHT items now, not seven** — `F3` joined the list on 2026-09-16 when its fix
landed, on the same footing as `F1`/`F2`/`F4`/`F6`: fixed, unverified against a running UI.

**Purpose: regress ClaudeForge for the split-library release.** Only what is still to do is below;
verified items are listed once at the end and should not be repeated. Findings go to
[`RETEST-FINDINGS.md`](./RETEST-FINDINGS.md).

| | |
|---|---|
| Build under test | `src/ClaudeForge/bin/Release/net10.0/win-x64/publish/ClaudeForge.exe` |
| Currently running | ⓘ **Nothing, by default.** `artifacts/a11y-untrimmed/` holds an untrimmed build left over from the `F5` investigation; it is **no longer needed for the accessibility pass**, because `F5` is refuted and the shipping artifact exposes a full tree. Use the build under test |
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

### ☐ E1 · Save preserves comments and formatting — `JsonC`

The newest shared library, with no prior release behind it. It replaced a serialize-and-overwrite
writer, so a regression here **destroys user content silently**.

**Do:** hand-write a `settings.json` containing comments, deliberate key order, blank lines and odd
indentation. Open it, change one value in the app, save, then diff the file.

**Pass:** only the edited value changed — comments, key order, blank lines and indentation all
survive.

**Fail:** a reformatted file, or comments gone. That is what the old writer did and what this
library exists to prevent.

### ☐ E2 · Save lands in the right scope — `AgentForge.Core` + `LayeredEditors`

**Do:** change values at **User** and **Project** scope (and **Local** if present). Save. Reload the
window.

**Pass:** each value lands in the file for **its own scope**, survives the reload, and the effective
view shows the correct layer winning.

**Fail:** a value written to the wrong file. The scope ladder is shared-library code and is exactly
what an extraction can scramble.

### ☐ E3 · Backup, then restore — `AgentForge.Core.Backup`

The largest piece of shared machinery, and destructive when wrong.

**Do:** take a backup. Change something. Restore.

**Pass:** the changed value comes back, the centre status pill reports success, and **no `*.bak`
sidecars remain**.

**Fail:** a partial restore, or surviving sidecars — `--cleanup-restore-sidecars` exists because
that has happened.

### ☐ E4 · Secrets stay redacted — `AgentForge.Sdk`

Two classifiers are deliberately duplicated across the layering boundary
(`JsonRedactor.IsSensitiveKey` in Core, `SensitiveKeys.IsSensitive` in Sdk), so drift between them
is live risk and the symptom is a secret in a log.

**Do:** with an `env` block containing e.g. `ANTHROPIC_API_KEY`, make an edit and save. Read
`logs/app-*.txt` beside the exe.

**Pass:** redacted everywhere it appears — audit log, save diff, backup manifest.

### ☐ E5 · Artifact resolution — `AgentForge.Artifacts`

**Do:** open Agents & Skills in a project having both user-level and project-level artifacts.

**Pass:** each is listed under the right source, and the counts match what is on disk.

---

## Also outstanding

### ☐ C3 · The `--cleanup-restore-sidecars` CLI tool

Its call site changed in this batch: `Program.cs` now passes the home explicitly, where the message
and the walk previously resolved it independently.

```powershell
& "C:\c\cl\OpenForge2k\src\ClaudeForge\bin\Release\net10.0\win-x64\publish\ClaudeForge.exe" --cleanup-restore-sidecars
```

**Pass:** prints `Cleaning up *.bak restore sidecars under C:\Users\Janus2\.claude…`, reports a
scanned/deleted summary, and **exits without opening a window**.

**Fail:** a window appears, a different directory is named, or there is no output (run it from a
terminal you can see — it reattaches to the parent console).

### ☐ F3 · *Share config* now says what it did

**Do:** on the Effective settings page, click **Share config**.

**Pass:** the centre status pill reports *"Configuration copied to the clipboard."* on Windows,
and the clipboard really holds the JSON. The pill clears itself after ~6 s rather than needing a
dismiss.

**Fail:** no pill; a grey pill with no icon that never clears (that is the legacy `StatusMessage`
channel, and it means the emission bypassed the typed helpers); or a success message on a machine
where the clipboard did not actually change.

ⓘ While you are there: **Share log** in the About dialog and **Share** on a backup row still
acknowledge nothing on screen by design — see F3's note in
[`RETEST-FINDINGS.md`](./RETEST-FINDINGS.md). Their outcome goes to `logs/app-*.txt` only. Not a
regression; the decision on whether to give them pills too is open.

### ☐ B2 · Diagnostics-window accessibility — *needs one action from you, then I run it*

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
