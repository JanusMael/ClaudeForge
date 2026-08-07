# Manual test plan — Agents & Skills filter, deep-path restore, `--deep-link`

Branch `feat/nav-deep-linking-and-list-search`, **uncommitted**.
Automated state: build 0 warnings, **2694 passed / 11 skipped / 0 failed** (baseline
was 2597 — 97 new tests), trim publish 0 IL warnings.

This plan covers what automation *can't*: real rendering, real theme tokens, real
filesystem scale, and the actual reload/relaunch experience.

> Three bugs in this feature were found by an adversarial pass **after** the tests were
> first green, because the repo's headless-test harness silently swallowed assertions
> (`return Session.Dispatch(async …)` never awaits the body — 19 pre-existing tests are
> inert for the same reason; flagged separately). The scenarios most worth your
> scepticism are therefore **C3** (unsaved edit survives reload), **D1/D2** (relaunch
> locates the item but does *not* re-enter edit mode), and **E12** (a deep link isn't
> re-applied on every reload) — those are the three that were actually broken.

---

## Setup

```bash
dotnet run --project src/ClaudeForge/ClaudeForge.csproj
```

You'll want a realistic number of artifacts. If your `~/.claude/skills` is thin,
this seeds 60 throwaway skills you can delete afterwards:

```bash
for i in $(seq 1 60); do
  mkdir -p ~/.claude/skills/zz-test-$i
  printf -- "---\nname: zz-test-%s\ndescription: Throwaway test skill number %s\n---\n\nBody.\n" "$i" "$i" > ~/.claude/skills/zz-test-$i/SKILL.md
done
```

Cleanup when done:

```bash
rm -rf ~/.claude/skills/zz-test-*
```

**Where the logs are.** ClaudeForge writes to `<folder containing the exe>/logs` — for a
Debug run that's `src/ClaudeForge/bin/Debug/net10.0/logs`. **Not** `~/.claude/logs/`,
which belongs to Claude Code itself. Or just press **F12** in the app for the live log
window and skip the file hunt.

> ⚠ **Copy the logs somewhere outside `src/` before anyone runs
> `src/publish/publish.ps1`.** That script's first action is a manual wipe of every
> `bin/` and `obj/` under `src/`, which takes the log folder with it — and it uses
> `Remove-Item -Force`, so nothing lands in the recycle bin.

Useful log tags while testing: `[AgentsSkills.Realized]`, `[DeepLink]`,
`[AgentsSkills.Refresh]`, `[AgentsSkills.Command]`, `[DebugFlags]`.

---

## A. Filter — the primary ask

| # | Steps | Expected |
|---|---|---|
| A1 | Open **Agents & Skills**. Look at the header area. | A filter box sits under the accent pill, placeholder "Filter agents, skills, commands…". No count and no Clear button yet (both appear only once filtering). |
| A2 | Type `zz-test` (or any partial name). | List narrows as you type. A count appears (`N of M`) and a **✕ Clear filter** button. |
| A3 | Clear the box manually (select-all, delete). | Full list returns; count and Clear button disappear. |
| A4 | Type a filter, then click **✕ Clear filter**. | Same as A3. |
| A5 | Filter on a word that appears only in a **description**, not a name (e.g. `Throwaway`). | Matching rows shown. This is the async-subtitle path — if descriptions haven't loaded yet, wait a beat and confirm they become matchable. |
| A6 | Filter on a **source** — type `User`, then a plugin's name. | Rows narrow by origin chip. |
| A7 | Filter so that only *Yours* rows match (e.g. a name unique to your own skills). | The **Plugin** section header disappears. No orphan header floating above nothing. |
| A8 | Filter so both groups match. | **Both** headers remain, each above its own surviving rows. |
| A9 | Filter to something matching nothing (`zzzzqqq`). | Empty list, no headers at all, count reads `0 of M`. |
| A10 | With a filter active, switch tabs (Sub-agents ↔ Skills ↔ Slash Commands). | Filter stays applied to each tab; the count updates to the tab you're on. |
| A11 | Filter, then navigate to another page and back. | Filter is **cleared** — a fresh visit starts with the full list (same convention as the Environment page). |
| A12 | Check casing: filter `ZZ-TEST` and `zz-test`. | Identical results. |

## B. Saved edit updates the list row *(confirmed bug, now fixed)*

| # | Steps | Expected |
|---|---|---|
| B1 | Scroll well down the Skills list. Note a row's grey subtitle text. | — |
| B2 | Click its name to open it → **Edit** → change **Description** to something distinctive → **Save**. | Save succeeds; the detail card shows the new description. |
| B3 | Click **Back** to the list. | **The row's subtitle shows the NEW description.** Before this change it kept showing the old text until a full refresh. |
| B4 | Edit the same item and clear the description entirely → **Save** → **Back**. | Row subtitle reads `(no description)`, not blank and not the stale old text. |
| B5 | Edit the front-matter **name** (not description) → Save → Back. | The row **label** does *not* change — it's derived from the file/folder name. This is correct, not a bug. |

## C. Reload Window keeps your place *(the second ask)*

**C1–C3 are the headline scenario.** F5 is the same as the toolbar Reload Window.

| # | Steps | Expected |
|---|---|---|
| C1 | Go to **Skills**, scroll down, open an item. Press **F5**. | You come back to the **Skills** tab with **the same item open**. The list behind it is filtered to that item and the filter box has an **orange outline** (that's the "this narrowing came from navigation" marker). |
| C2 | Clear the filter after C1. | Full list returns; the orange outline disappears. |
| C3 | Open an item → **Edit** → type into Description and Body but **do not save** → press **F5**. | The editor re-opens **with your typed text still there**. Verify the file on disk is unchanged (`Open in editor`) — nothing was silently saved. *Before this change your typing was silently discarded.* |
| C4 | Repeat C3 but toggle **Edit raw YAML** on and type in the raw box before F5. | Raw mode comes back **on**, with your raw text intact (not re-seeded from disk). |
| C5 | After any reload-restore, look at the bottom-left of the window. | **No** deep-link "Back" button appears — a restore isn't a navigation you can go back from. |
| C6 | Open an item, then delete that skill's folder from outside the app, then F5. | App reloads cleanly, lands on the Skills tab, nothing open, no error dialog. Log shows `[DeepLink] artifact not found`. |
| C7 | Open an item on **Sub-agents**, F5. Then an item on **Slash Commands**, F5. | Correct tab and item each time — the segment follows the artifact, not the last tab you looked at. |

## D. Relaunch keeps your place — but not your edit mode

| # | Steps | Expected |
|---|---|---|
| D1 | Open a skill. **Quit the app** (window close) — do *not* navigate away first. Relaunch. | You land on **Agents & Skills**, correct tab, with **that item open**, list filtered to it. *(This needed an explicit shutdown capture: quitting straight from an open item never fires the navigate-away capture, so without it you'd get the page you were on before.)* |
| D1b | Open a skill, navigate to **Essentials**, then quit. Relaunch. | Lands on **Essentials** — the remembered position follows where you actually were, not the last artifact you happened to open. |
| D2 | Open a skill → **Edit** → type something → **quit without saving** → relaunch. | You land on the item, **but NOT in edit mode.** This is deliberate: the buffer died with the process, so re-entering an editor seeded from disk would look like your unsaved work had come back. |
| D3 | Navigate to **Essentials**, quit, relaunch. | Lands on Essentials. Deep-path restore doesn't hijack non-artifact pages. |

## E. `--deep-link` *(the third ask)*

Run from a terminal against the built binary (or `dotnet run … -- --deep-link …`).

| # | Command | Expected |
|---|---|---|
| E1 | `--deep-link essentials` | Opens on Essentials. |
| E2 | `--deep-link agents-skills` | Opens on Agents & Skills, default tab, nothing open. |
| E3 | `--deep-link agents-skills/skills` | Opens on the **Skills** tab. |
| E4 | `--deep-link agents-skills/skills/<a real skill name>` | Opens with that skill **open**, list filtered to it, orange outline on the filter box. |
| E5 | `--deep-link claude-code/permissions` | Opens Claude Code → **Permissions**. Confirms `permissions` resolves as a child *page*, not as a tab of the Claude Code header. |
| E6 | `--deep-link claude-code/mcp-servers` | Opens MCP Servers. (Ids are the sidebar name lower-cased, punctuation → `-`.) |
| E7 | `--deep-link no-such-page` | **App launches normally** on your usual page. Log: `[DeepLink] path=… resolved=false`. Must not hang, crash, or show an error dialog. |
| E8 | `--deep-link agents-skills/skills/no-such-skill` | Lands on the Skills tab with nothing open. Log: `artifact not found`. |
| E9 | `--deep-link "AGENTS-SKILLS/SKILLS"` | Case-insensitive — same as E3. |
| E10 | `--deep-link /leading-slash` and `--deep-link a/b/c/d/e` | Rejected at parse. App launches normally; log shows the rejection reason from `[DebugFlags]`. |
| E11 | `--deep-link` with **no value at all** | App launches normally; log says the flag requires a value. |
| E12 | E4, then navigate to Essentials, then press **F5**. | You stay on **Essentials**. The command-line target is applied once at launch, not re-applied on every reload. |
| E13 | Any successful deep link — check the startup log. | `[DebugFlags] active: --deep-link <path>` line present. |
| E14 | `--debug-help` | Emitted flag list includes `--deep-link <path>`. |

## J. Discoverability — "Copy deep link" and failure messages

| # | Steps | Expected |
|---|---|---|
| J1 | Open any skill. Look at the detail toolbar (right side). | A **Copy deep link** button sits next to Copy markdown, with a tooltip explaining it. |
| J2 | Click it. Watch **two** places. | Announced in both: (a) a line under the detail toolbar reading `Deep link copied: agents-skills/skills/<name>@User`, and (b) a green **✓ pill in the centre of the bottom status bar** with the same text, auto-clearing after ~6 s. An 11px grey line alone is easy to miss, which is why both exist. |
| J3 | Paste the clipboard into a terminal after the exe, i.e. `ClaudeForge.exe --deep-link <pasted>`. | Opens exactly that item. This is the round trip that makes the feature self-teaching — no need to know the grammar. |
| J4 | Go **Back** to the list (nothing selected), then look for the button. | Disabled — there's no artifact to link to. |
| J5 | Open a **plugin-provided** skill and copy its link. | Path is qualified with the plugin name, e.g. `…/skills/widget@everything-claude-code`, so it can't resolve to a same-named user skill. |
| J6 | Run with a **malformed** path: `--deep-link "agents-skills//pdf"` | Your **terminal** prints `[ClaudeForge] --deep-link … rejected: path contains an empty segment.`, the expected shape, the list of valid pages, and the Copy-deep-link tip. App then launches normally. |
| J7 | Run with a **well-formed but unresolvable** path: `--deep-link agents-skills/skills/definitely-not-a-skill` | Nothing on the terminal (the prompt is back by then) — instead an amber **warning in the status bar**: "Couldn't open the deep link '…' — no such page or item." App launches normally. |
| J8 | Quit while an artifact is open, relaunch, and watch the status bar. | **No** warning. An unresolvable *persisted* path is routine (you may have deleted the artifact) and must not nag on every launch — only an explicitly-typed `--deep-link` warns. |
| J9 | `--debug-help` | Output lists `--deep-link <path>`. |

## F. Theme, a11y, i18n

| # | Steps | Expected |
|---|---|---|
| F1 | Do C1 in **light** theme, then toggle to **dark** (and back). | The orange navigated outline is clearly visible in both. Filter box, count text, and Clear button are all legible in both — no dark-on-dark. |
| F2 | Tab through the filter row with the keyboard. | Filter box and Clear button are both reachable and focusable. |
| F3 | With a screen reader (Narrator/NVDA/VoiceOver), focus the filter box, then the Clear button. | Announced as "Filter agents, skills, commands…" and "Clear filter" — not as *property* filter, and no emoji/glyph read aloud. |
| F4 | Relaunch with `--culture fr-FR` (or `de-DE`, `ja-JP`) and open the page. | Filter placeholder, count, and Clear button are translated — no English fallback, no literal `{0}`/`{1}` visible in the count. |
| F5 | `--culture ja-JP --deep-link agents-skills/skills/<name>` | Deep link still resolves. Ids are language-independent. |

## G. Performance / virtualization

| # | Steps | Expected |
|---|---|---|
| G1 | With 60+ skills seeded, open the page and check the log for `[AgentsSkills.Realized]`. | `rows=` is roughly **a screenful** (single digits to low tens), not the full `ofTotal=` count. Hundreds would mean virtualization broke. |
| G2 | Scroll the long list quickly. | Smooth; no multi-second stalls. |
| G3 | Type quickly into the filter box on the long list. | Keeps up with typing; no visible lag per keystroke. |
| G4 | Open the page for the first time in a session. | Rows appear promptly, with grey subtitles filling in a moment later (descriptions load lazily by design). |

## H. Regression sweep — things nearby that shouldn't have moved

| # | Steps | Expected |
|---|---|---|
| H1 | On Agents & Skills, use per-row **Open in editor**, **Reveal**, **Delete**. | All still work. Delete still confirms first and is absent on plugin rows. |
| H2 | Open a **plugin-provided** skill. | Read-only badge shows, **Edit** button hidden. |
| H3 | Save an artifact, watch for the status line. | Post-save message appears; the once-per-session "applies to your next session" hint still shows on the first save. |
| H4 | Use the **global search bar** (top toolbar) for a config setting like `permissions.allow`. | Still deep-links correctly, and the *property* filter box on that page still shows **its** orange navigated frame. (Confirms the shared frame styling wasn't broken.) |
| H5 | Global-search for a **skill name**. | Only a page-title-level hit at best — global search does not yet find individual artifacts. **Known gap, deliberately deferred**, flagged for the follow-up review. |
| H6 | Switch profiles (Profiles page), and separately edit `~/.claude/settings.json` in an external editor to trigger the file watcher. | Both reload normally. If you had an artifact open, you come back to it — an automatic reload doesn't lose your place or eat an in-progress edit either. |
| H7 | Click **Clear App Data** (only if you're willing to reset UI state), then relaunch. | Starts from clean defaults; no remembered position. |

## I. Privacy check

| # | Steps | Expected |
|---|---|---|
| I1 | Open an artifact, **Edit**, type a distinctive string (e.g. `SECRET-CANARY-123`), **don't save**, press F5, then quit. | — |
| I2 | Inspect `~/.claude/cache/ClaudeForge-gui-state.json`. | Contains a `lastDeepPath` like `agents-skills/skills/<name>@User`, and **does NOT contain `SECRET-CANARY-123`**. The unsaved buffer is in-memory only and must never be persisted. |

---

## Reporting

For anything that fails, the most useful bundle is: the row from this table, what
you saw, and the tail of `src/ClaudeForge/bin/Debug/net10.0/logs` filtered to
`[DeepLink]` / `[AgentsSkills.` — those lines carry the resolved path, the mode, and
whether the restore applied.

**Known limitations, expected — not bugs:**

- Multi-select and export are **not** in this branch (groundwork only, no UI).
- No new grouping options — deferred pending your read on whether the filter is
  sufficient at real scale (that's the first item in the follow-up review).
- Selection state does not survive navigating away and back (rows are rebuilt on
  each visit). Only relevant once multi-select ships.
- Global search doesn't find individual artifacts (H5).
