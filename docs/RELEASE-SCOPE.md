# Release scope — what a `v*.*.*` tag from this branch ships

> Scope of the **next** ClaudeForge release, cut from `release/claudeforge-on-packages`.
> Replaced at each release rather than appended to. For the phase plan see
> [`plans/00003`](../plans/00003-release-built-from-shared-packages.md); for where the work
> stands see [`PROGRESS.md`](../PROGRESS.md).

---

## What the tag produces

`release.yml` triggers on a bare `v*.*.*` tag — deliberately *not* prefixed, because thirteen
releases already exist in that shape and every installed copy looks for exactly it. It is
branch-agnostic, so a tag on this branch works mechanically.

| | |
|---|---|
| Artifacts | **Six** self-contained trimmed archives — `win-x64`, `win-arm64` (`.zip`), `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (`.tar.gz`) |
| Built on | Native hosts — Windows, Linux and macOS runners, one job each, so the native payloads match their target |
| Version | Resolved **from the tag** by `Resolve-ReleaseVersion.ps1`, which exports `BuildTimestamp` so the assembly version and the tag describe the same thing by construction. Archive filenames stay unversioned |
| Signing | ⚠ **Unsigned, on both platforms.** macOS ships `allow-app-to-run.sh` to strip the quarantine attribute; Windows binaries are signed **locally, after the release publishes** and before the winget submission, because the certificate cannot be automated on a hosted runner |
| winget | ⛔ **Not part of the tag.** `winget-submit.yml` is manually dispatched and reads the live release asset's SHA256, so it runs after the release exists |
| Gate | The full suite on all three platforms must pass before any publish job starts |

---

## ⛔ The release notes do not come from `CHANGELOG.md`

`release.yml` never reads it. The *Resolve changelog source* step takes the first of:

1. the body of the **pull request** whose merge commit is the tagged commit;
2. otherwise the **tagged commit's own message body**;
3. otherwise GitHub's auto-generated notes.

**This branch has no pull request.** So the notes under *What's Changed* would be the message body
of whichever commit the tag points at — today, a dependency-bump message — and the curated
`## [Unreleased]` section below would not appear in the release at all.

Three ways out, in descending order of fit:

| Approach | Effect |
|---|---|
| Write the notes into the tagged commit's body | The commit that closes the release carries the user-facing text; no PR needed |
| Open a PR for the branch and tag its merge commit | Uses the documented convention, and puts the release through review |
| Accept auto-generated notes | A commit-subject list, which for this branch is mostly CI and build plumbing |

---

## What is in — what a user would notice

Curated in [`CHANGELOG.md`](../CHANGELOG.md) under `## [Unreleased]`, and current as of this branch.

**Added.** Saves preserve comments and formatting, because writes now edit the JSONC bytes in
place instead of re-serializing a parsed object (the old path is available for one release as
`--writer legacy`). Every setting carries a severity indicator, on rows, in search results and in
the effective view. The save dialog calls out which pending edits weaken a boundary. A new
**Artifacts** page lists the agents, skills and commands resolved for the workspace and where each
came from. Schemas are fetched upstream with a bundled fallback, and every navigation section
badges which copy it was built from; *Check for schema updates* re-fetches on demand, and a product
with no upstream is omitted from the results rather than reported up to date.
`--schema-source <bundled|fetched>` forces one branch of that chain. **Shift+F12** opens a live
config-file event window, mirrored to `logs/events-*.txt`.

**Changed.** Backup wording comes from the host application rather than generic text. The accent
colour and the "✨ NEW" badge no longer depend on an undefined system brush that rendered
differently across platforms.

**Fixed** — eleven entries, the load-bearing ones being:

- **Editing one environment variable no longer deletes the others**, and more generally keys the
  editor does not render are no longer keys it deletes. ⚠ This entry describes two commits: the
  app's object editor, and then the **library's** copy inside
  `Bennewitz.Ninja.LayeredEditors.ViewModels`. Until the second landed the sentence was only half
  true — there are two classes by that name and neither derives from the other, so one fix covered
  half the object editors in play.
- **Restoring a backup puts your project's files back**, where it previously reported success and
  wrote nothing for any project kept outside the home folder.
- **A restore cleans up after itself** — the `.pre-restore-*.bak` copies are removed once a run
  completes without a single failure, instead of staying and roughly doubling `~/.claude` each time.
- **Sharing worked on no platform and reported on none.** *Share config*, *Share log* and *Share*
  on a backup row now each say what actually happened, and a failure stays on screen.
- **Screen readers now announce the interface** — navigation rows, settings tabs, four list boxes,
  numeric spinner buttons, composite controls, expander headers and every control in the
  diagnostics windows previously announced nothing or read out an internal type name.
- **Backup patterns**: `/foo` matched nothing and `**/foo` matched too much.

## What is in — invisible, and the reason the release exists

This is the **first ClaudeForge release built from the published shared packages.** Every release
before it used project references while two comments in the repository claimed otherwise.

- The only `dotnet publish` in the release chain selects package mode at the pinned
  `SharedPackageVersion`, restoring the eleven `Bennewitz.Ninja.*` packages from the feed.
- A build-time guard fails a shipping publish that quietly takes the project-reference path; the
  escape hatch, when used, names itself in the log.
- Every shipping publish states its mode positively, and `release.yml` uploads those logs as
  `provenance-<host>` with 90-day retention, so the record outlives the run.

---

## What is out

### Deferred by plan, and shipping as-is

**Managed settings are read from the wrong directory.** Claude Code reads enterprise policy from a
system location — `/Library/Application Support/ClaudeCode/`, `/etc/claude-code/`,
`C:\Program Files\ClaudeCode\` — and ClaudeForge reads `~/.claude/managed-settings.json`. It fails
both ways silently: a machine with real policy shows **no managed layer**, so the effective view
tells the user their own value wins where policy actually overrides it; and a file the user places
at `~/.claude/managed-settings.json` displays as enforced while doing nothing. `managed-mcp.json`
is not handled at all.

**`CLAUDE_CONFIG_DIR` is documented to the user and ignored.** The app ships a tooltip describing
it, live on two surfaces, and does not read it.

Both are [`plans/00002`](../plans/00002-claude-code-real-config-locations.md), sequenced as Phase E
— **after** this release, as the second package version. The reason is stated in the plan: landing
a rewrite of path resolution in front of the release that exists to prove the package pipeline
would leave any failure with two candidate causes.

### Deferred by a locked decision

**External git worktrees are not authorised for restore** (`F11`). The sound fix spawns `git` once
per known project, each with a timeout, immediately in front of a destructive operation — a real
runtime cost and a real dependency on `git` being present, decided on its own rather than folded
into release week. Nothing regresses by waiting: the refusal is reported honestly, and external
worktrees are only captured in Full mode.

### Not in this branch at all

**OpenCodeForge**, and the two libraries it owns. They were removed here so the ClaudeForge release
carries no OpenCodeForge code, and live on the parked `feat/agentforge-opencodeforge` branch.

---

## Against `main`, and why this is not a regression

`main` released `v2026.3.916`; its history was integrated into this branch by hand, and `main` has
moved four commits since. None of them is a user-facing gap here:

| Commit on `main` | Status here |
|---|---|
| `Microsoft.Extensions.TimeProvider.Testing` 9.10.0 → 10.10.0 | **Ported.** Debug build clean at 0 warnings; suite unchanged at 3,552 / 0 / 13, total 3,565 |
| `Microsoft.Maui.Essentials` 10.0.100 → 10.0.101 | **Not applicable.** This branch has no MAUI dependency — sharing was reimplemented as `IShareService` / `DefaultShareService`, which is what the *Sharing worked on no platform* fix above is |
| `docs(agents)` deep-navigable checklist refresh | Documentation only; not user-facing |
| Its merge commit | — |

⚠ **Counting cannot confirm this and neither can `git cherry`.** The 2026-09-16 history rewrite
renamed every already-merged commit, so a revision count over-reports; and a hand-ported fix has a
different patch-id than the commit it ports, so patch-id matching over-reports too. The integration
point is a **recorded SHA**, kept in [`PROGRESS.md`](../PROGRESS.md); compare against that.

---

## Before the tag

| | Whose |
|---|---|
| Choose the CalVer and cut the `## [Unreleased]` heading to it | maintainer |
| Decide where the release notes come from — see the three options above | maintainer |
| Push the tag. ⛔ **Irreversible**, and it publishes to every installed copy's update check | maintainer |

⚠ **The tag points at this branch, not `main`.** A locked decision keeps split work out of `main`
until the maintainer approves, so this release is cut from a feature branch. That is a departure
worth stating rather than discovering.

⚠ **Day-resolution CalVer means one release per calendar day.** A second tag on the same day
collides with a version that already exists, and the recovery is tomorrow.
