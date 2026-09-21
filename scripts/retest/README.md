# `scripts/retest` — driving ClaudeForge through UI Automation

Tooling for the manual-retest passes. It exists because most of the retest list is
*"open the app and look"*, and a human reading values off a screen is both slow and
the weakest link in the evidence chain — a byte comparison beats an impression.

These are **harnesses, not tests.** Nothing here runs in CI, nothing gates a build.
They are meant to be edited and extended during a retest, and the lessons kept.

> ⚠ Gaps in the app's automation surface found while building these are recorded in
> [`../../docs/UIA-AUTOMATION-GAPS.md`](../../docs/UIA-AUTOMATION-GAPS.md). Several are
> accessibility gaps wearing automation clothes; read that file before concluding a
> control is "missing".

---

## The pieces

| Script | What it does |
|---|---|
| `UiaCommon.ps1` | Dot-sourced helpers: window lookup, the settle loop, element enumeration with diagnostic counters, dirty-state detection. Everything else builds on it. |
| `Find-UiElement.ps1` | "Is this control present, and what is it called?" Settles, then lists matches with type / AutomationId / optional value. |
| `Invoke-UiElement.ps1` | Act on one control by UIA pattern: `Select`, `Invoke`, `SetValue`, `Expand`, `Collapse`, `Read`. |
| `Set-SettingAtScope.ps1` | End-to-end: pick an editing scope, set `model`, confirm the edit registered, save, clear the save dialog. |
| `Get-BackupState.ps1` | The Backup page's *real* state — which mode radio is armed, which targets are ticked, what is in flight. |
| `New-SafetyBackup.ps1` | Copies the `~/.claude` config surface somewhere outside the testing zone before a destructive item. Excludes `.credentials.json` deliberately. |
| `Get-BackupArchiveInfo.ps1` | Reads a backup `.zip`: roots, sizes, whether credentials leaked in, whether a given project is present. |
| `Test-ScratchHomeIsolation.ps1` | Proves a run honours `CLAUDE_CONFIG_DIR`: ClaudeForge's artifacts appear in a scratch home and its copies in the real `~/.claude` are untouched. ⭐ **With this green, E1–E3 can be driven against a scratch home instead of the tester's own config.** |

## Typical session

```powershell
# what is on screen?
pwsh -NoProfile -File scripts/retest/Find-UiElement.ps1 -Needles '*' -InteractiveOnly

# navigate
pwsh -NoProfile -File scripts/retest/Invoke-UiElement.ps1 -Name 'Backup / Restore' -Type TreeItem -Action Select

# before anything destructive
pwsh -NoProfile -File scripts/retest/New-SafetyBackup.ps1
```

---

## ⛔ Ten things that cost real time before they were written down

**1 · A fixed sleep is not a measurement.** Measured on one cold single-file launch:
72 descendants at 5.3s, a COMException at 7.1s, 168 at 8.0s. Always go through
`Wait-ForgeSettled`. Without it the tree reports *"element missing"* when it means
*"not built yet"* — and that reads like a product defect.

**2 · `pwsh -File` does not parse PowerShell syntax.** `-Needles 'a','b','c'` arrives
as the single literal string `a,b,c`. This produced **0 matches against a healthy
tree** and looked exactly like a missing control. `Split-Needles` absorbs it; pass
needles comma-joined in one argument.

**3 · Never `catch { continue }` without counting.** If the COM state degrades,
*every* element throws and the loop reports "no matches" — indistinguishable from a
healthy tree with nothing to match. `Get-ForgeElements` always reports
`seen / named / threw`, and says so loudly when most elements threw.

**4 · Patterns, never coordinate clicks.** The app is not the foreground window while
an agent drives it, so a synthesised click lands nowhere and the script "succeeds"
having done nothing.

**5 · Modals are a separate `Window` at the TOP of the tree.** Piping a dump through
`Select-Object -Last N` hides them, which once turned a perfectly ordinary consent
dialog into an apparent silent hang. When an action seems to do nothing, dump the
*first* 20 elements, not the last.

⛔ **Refined 2026-09-18: "top of the tree" does NOT mean "top-level window".** The Save
Changes and Include-API-credentials dialogs are `Window` elements **inside the main
window's descendants** — element `[1]` of a descendant walk, not a sibling of it. A scan
of `RootElement`'s children finds only the main window and reports *"no modal dialog
found"* while the dialog is on screen. Enumerate the main window's descendants and take
the first `ControlType.Window`.

**6 · Some results are announced only in a pill that clears, and are never logged.**
`RestoreResult.Message` — which carries the restored and swept counts — is assigned to
`StatusMessage` and nothing else. `Watch-TransientStatus.ps1` caught the *progress* labels
(`Restoring claude.json…`, `Restore complete`) and never the message itself.

⭐ **When the report is unreadable, measure the effect instead.** To prove sidecars were
written and then swept — a final count of zero cannot tell *swept* from *never written* —
poll the trees at ~150 ms across a live restore and record the PEAK alongside the final:

```
C:\c\cl\retest-2026.3.918   peak=2      final=0
~/.claude                   peak=6023   final=0
```

**7 · `Find-UiElement` matches on the NAME, not the AutomationId** — so a needle like
`ExpanderHeader` returns `matched: 0` against a tree full of `id=ExpanderHeader` elements.

⛔ **That zero is indistinguishable from "fixed".** Chasing `F10` this read as confirmation
that the defect was gone, and it happened to be — which is worse, because the method would
have "confirmed" it either way. The ids in the earlier dump had been matched by a *different*
needle in the same comma-joined list (`Avalonia.`), not by the id needle.

⭐ **Confirm a fix with a POSITIVE assertion, then a negative one whose needle you have seen
match.** For `F10`: first that the headers announce `ANTHROPIC · 41`, then that a scan for
`Avalonia.` / `System.` returns zero — that second needle had produced seven hits an hour
earlier, so its zero means something.

**8 · `Invoke()` can succeed and do nothing when the element came from a stale walk.**
The first invoke of a virtualised list row's *Restore* button returned cleanly and wrote
no log line; re-resolving the element after the tree stabilised worked. ⚠ This is lesson
4 wearing a different coat — confirm against the app's **own log**, not the pattern's
return value.

**10 · `git push` can start failing mid-session with `Permission denied (publickey)`.** The SSH
key here is served by an agent rather than a file — there is a `~/.ssh/agent/` directory and no
private key — and the Windows `ssh-agent` service is Stopped and Disabled, which is correct when
1Password owns the agent. When 1Password stops exposing `\\.\pipe\openssh-ssh-agent`, Windows
OpenSSH has nothing to ask and every push dies. ⭐ **Workaround that changes no config and does not
touch the user's credentials**: `gh` is already authenticated, so push once over HTTPS with an
inline helper rather than `git remote set-url` —
`git -c credential.helper='!f(){ echo username=x-access-token; echo password=$(gh auth token); };f' push https://github.com/<owner>/<repo>.git <branch>:<branch>`.
⚠ `git ls-remote origin` keeps failing afterwards because it still uses SSH; verify the push
landed with `gh api repos/<owner>/<repo>/branches/<url-encoded-branch>` instead. ⓘ The durable fix
is `git config --global url."https://github.com/".insteadOf "git@github.com:"`, which takes SSH out
of git's path entirely while leaving 1Password for interactive work.

⚠ **Unrelated but adjacent: a `.bashrc` that runs `eval $(ssh-agent)` per shell is a red herring
here.** It produces MSYS unix-domain sockets under `~/.ssh/agent/` — seven accumulated in one day
— and Windows OpenSSH cannot use any of them, so they look like a working agent while serving
nothing.

**9 · `~/.claude` is NOT quiescent, so a whole-home before/after diff proves nothing.**
Measured with a control window and **no app running at all**: 8 changes in 25 seconds — five
session transcripts under `projects/`, a `~/.claude.json` backup rotation, and a `file-history/`
entry, all written by Claude Code itself. The first draft of
`Test-ScratchHomeIsolation.ps1` diffed the whole home and reported a confident **FAIL** on that
churn. ⭐ **Run the control before trusting an attribution**: scope the comparison to the files
the app under test actually writes, by name. ⚠ `cache/model-catalog/tok-*-ccd.json` looks like
ClaudeForge's and is not — one landed **75 seconds before** the app was launched.

---

## Confirming an edit actually happened

⛔ **A save is not evidence.** Two separate retest items looked green while nothing
had been written:

- an agent file that was never saved still matched its baseline, and "identical"
  was briefly read as "round-tripped";
- a settings save with nothing pending wrote no file at all, so the value under test
  was never exercised.

So: set a value, confirm the **window title gains ` *`** (`Test-ForgeDirty`) — that
is the binding acknowledging the change — and only then save. `Set-SettingAtScope.ps1`
aborts rather than saving when the dirty marker does not appear, because a vacuous
save reports success and proves nothing.
