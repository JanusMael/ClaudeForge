# winget packaging

`winget install Bennewitz.Ninja.ClaudeForge`

This folder holds the [Windows Package Manager](https://learn.microsoft.com/windows/package-manager/)
manifest **templates** for ClaudeForge and documents how they reach the
community repository, [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs).

## Identity

| Field | Value |
|-------|-------|
| `PackageIdentifier` | `Bennewitz.Ninja.ClaudeForge` |
| `Publisher` (display) | `Brian Bennewitz` |
| `Moniker` | `claudeforge` |
| Installer | the release `ClaudeForge-win-<arch>.zip`, installed **portable** (nested `ClaudeForge.exe`) |

The identifier prefix is the author's namespace (`Bennewitz.Ninja`, matching the
NuGet convention); the `Publisher` display field is the author's name. winget
does not require those to match, nor to match the GitHub account (`JanusMael`)
that hosts the releases. The identifier is effectively permanent once accepted —
keep the exact casing everywhere.

## What "portable" means here

The Release build is a single self-contained `ClaudeForge.exe`
(`PublishSingleFile`), shipped inside a `.zip`. winget extracts it and adds a
`claudeforge` command to `PATH`, but **does not** create a Start Menu shortcut or
an Add/Remove Programs entry (portable installs never do). If shortcuts become a
requirement, wrap the exe in an Inno Setup / WiX installer and switch the
manifest to `InstallerType: inno` / `wix`; that's a separate, larger change.

Note: winget installs the portable package fine whether or not the exe is signed.
Signing (which removes the SmartScreen prompt as reputation builds, and avoids
occasional Defender flags during winget-pkgs validation) is handled as a separate
local step after each release, before this submission runs — it is intentionally
not part of the public build pipeline.

## First submission (one-time, manual)

The package now exists in winget-pkgs (first submitted 2026-07 as `2026.3.708`),
so this section is historical reference. The **initial** submission was done
manually with the interactive wizard:

1. Cut a normal release so the `ClaudeForge-win-x64.zip` / `-win-arm64.zip`
   assets exist at stable URLs.
2. From a Windows machine with [wingetcreate](https://github.com/microsoft/winget-create):
   ```powershell
   winget install wingetcreate      # or: Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile wingetcreate.exe
   wingetcreate new `
     https://github.com/JanusMael/ClaudeForge/releases/download/v<VER>/ClaudeForge-win-x64.zip `
     https://github.com/JanusMael/ClaudeForge/releases/download/v<VER>/ClaudeForge-win-arm64.zip
   ```
   Answer the prompts using the values in the template `*.yaml` files here
   (identity, publisher, license, description, `NestedInstallerType: portable`,
   `RelativeFilePath: ClaudeForge.exe`, `PortableCommandAlias: claudeforge`).
   `wingetcreate` computes the SHA256 hashes for you and opens the PR.

   The template `.yaml` files in this folder mirror exactly what the accepted
   manifest should contain, so you can also copy them, fill the `<...>`
   placeholders, and `wingetcreate submit <folder>` instead.
3. A winget-pkgs moderator reviews the new-package PR (first-time packages get
   extra scrutiny; it's your own app/repo, so identity checks out).

## Ongoing updates (automated)

`.github/workflows/winget-submit.yml` is **manually dispatched**
(`workflow_dispatch`) with an explicit version — run it after the release's
Windows zips are signed and re-uploaded. It builds the PR from the template
`*.yaml` files in this folder (filling in the version and freshly computed
SHA256 hashes, and injecting the release's notes + date) and opens it with
`wingetcreate submit`.

Building from the templates keeps every release **fully populated**. Using
`wingetcreate update` instead — as an earlier version of this workflow did —
fetches the currently published manifest and only bumps the version + URLs, so it
inherits whatever metadata is already published and silently drops the rich
fields (`Author`, `Description`, `Tags`, `Moniker`, …). That is what left the
catalog entry showing "Author: Not available" with empty release notes.

It needs one repository secret:

- **`WINGET_TOKEN`** — a GitHub Personal Access Token (classic) with **both** the
  `public_repo` **and** `workflow` scopes. `wingetcreate` uses it to fork
  `microsoft/winget-pkgs` under your account, sync that fork with upstream, and
  open the PR. The `workflow` scope is required because syncing the fork writes
  upstream's `.github/workflows/*` files, which GitHub refuses to let an OAuth
  token do without it. Add it under
  *Settings → Secrets and variables → Actions → New repository secret*.

Dispatch is manual by design (there is no Release trigger): the signed-zip
re-upload happens out of band, and submitting before it would pin the unsigned
hash.

## Three ways to submit — pick exactly one

All three end in the same `wingetcreate submit`. Running two for one release opens
two PRs against the same manifest, which winget-pkgs asks contributors not to do
(2026.3.810 did exactly that — see the incident notes in `packaging/BBWinget.md`).

| Path | How it fires | Leaves an Actions run? |
|---|---|---|
| `packaging/sign-release.ps1` | **Dispatches the workflow itself** at the end, unless `-SkipWinget` | yes |
| `.github/workflows/winget-submit.yml` | `gh workflow run winget-submit.yml -f version=<ver>` | yes |
| `packaging/Submit-Winget.ps1` | run locally; submits straight from your machine | **no** |

**The normal release is: run `sign-release.ps1` and stop — it already submits.** The
other two are for re-submitting after a failure, or when you passed `-SkipWinget`.

Both submit paths refuse to run when an open winget-pkgs PR already exists for that
package + version.

## Why order matters: CI cannot sign

The release workflow publishes **unsigned** zips. `sign-release.ps1` signs them with
the SimplySign cloud certificate — which only exists on a developer machine, never in
CI — re-uploads them **in place**, and only then submits. Because `wingetcreate` pins
a SHA256 computed from the *live* asset, submitting first would publish the hash of an
unsigned binary permanently.

So neither path trusts the caller to get the order right: each cracks the downloaded
zips and requires a **valid Authenticode signature** on `ClaudeForge.exe`, aborting
otherwise.

## Keep the templates complete

`wingetcreate submit` builds the manifest **only** from the `*.yaml` files in this
folder. Unlike `update`, it does **not** inherit anything from the published version —
so any field missing here is silently dropped from the catalog entry. That is how the
`Documentations` (Wiki) entry disappeared in 2026.3.810. When adding a manifest field,
add it to the template, not to a one-off submission.
