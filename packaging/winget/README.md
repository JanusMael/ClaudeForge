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

The automated workflow below uses `wingetcreate update`, which requires the
package to already exist in winget-pkgs. The **initial** submission is manual:

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

`.github/workflows/winget-submit.yml` runs on every **non-prerelease** GitHub
Release and calls `wingetcreate update` to open the version-bump PR
automatically. It needs one repository secret:

- **`WINGET_TOKEN`** — a GitHub Personal Access Token (classic) with the
  `public_repo` scope. `wingetcreate` uses it to fork `microsoft/winget-pkgs`
  under your account and open the PR. Add it under
  *Settings → Secrets and variables → Actions → New repository secret*.

Prerelease tags (`v*-rc.*`, etc.) are skipped — the community repo has no
prerelease channel.

You can also trigger the workflow manually (`workflow_dispatch`) with an explicit
version to re-submit or back-fill a release.
