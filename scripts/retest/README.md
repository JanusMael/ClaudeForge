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

## ⛔ Five things that cost real time before they were written down

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
