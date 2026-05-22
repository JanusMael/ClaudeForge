# GitHub Actions — Workflow Reference

This document describes every automated workflow in this repository, how they
interrelate, and how to use them day-to-day.

The release and trim-check paths both delegate to **`src/publish/publish.ps1`** —
the same script developers run locally (`pwsh src/publish/publish.ps1 -All`).
Keeping a single canonical entry point means a green CI build and a green
local publish exercise the same code, with the same per-RID cleaning, IL
warning scan, closure analyzer, and archive creation.

---

## Workflow map

```
 any branch push  ──────────────────────────────────────────────────────────────
   │                                                                            │
   ├─▶ CI › build-and-test   ubuntu / windows / macos   (parallel, fail ≠ stop)│
   ├─▶ CI › trim-check       ubuntu  publish.ps1 -Rids linux-x64                │
   └─▶ CodeQL › analyze      ubuntu                     (security scan)        │
                                                                                │
 any PR  ──────────────────────────────────────────────────────────────────────┘
   (same three jobs as branch push above; concurrency group cancels stale runs)

 v*.*.* tag push  ──────────────────────────────────────────────────────────────
   │
   └─▶ Release › test            ubuntu / windows / macos  fail-fast: true
         │  (all must pass)
         ├─▶ Release › publish-windows   publish.ps1 -Rids win-x64,win-arm64
         ├─▶ Release › publish-linux     publish.ps1 -Rids linux-x64,linux-arm64
         └─▶ Release › publish-macos     publish.ps1 -Rids osx-x64,osx-arm64
               │  (all three must succeed)
               └─▶ Release › release     create GitHub Release + upload archives

 Monday 08:00 UTC  ─────────────────────────────────────────────────────────────
   └─▶ CodeQL › analyze      (weekly scheduled scan)

 Monday 09:00 UTC  ─────────────────────────────────────────────────────────────
   ├─▶ Dependabot            NuGet + GitHub Actions version checks → open PRs
   └─▶ Schema refresh        refresh-schema.ps1 → open PR if upstream drifted
```

---

## Workflows

### `workflows/ci.yml` — Continuous Integration

**Triggers:** push to any branch; any pull request  
**Concurrency:** `ci-${{ github.ref }}` — cancels a stale run the moment a new
commit lands on the same branch or PR.

| Job | Runner | What it does |
|-----|--------|-------------|
| `build-and-test` | ubuntu, windows, macos | Restore → Debug build → test with TRX results uploaded as artifacts (14-day retention). `fail-fast: false` so all three platforms always report. |
| `trim-check` | ubuntu | Invokes `pwsh src/publish/publish.ps1 -All -Rids linux-x64`. Same entry point the release path uses, so trim regressions are caught on every PR. |

The CI badge in the README reflects the status of the most recent run on the
default branch.

---

### `workflows/release.yml` — Release

**Trigger:** push of a tag matching `v*.*.*` (e.g. `v1.2.3`, `v1.2.3-rc.1`)  
**Permissions:** `contents: write` (create release + upload assets), `pull-requests: read`
(read PR body for release notes)

#### Job graph

```
test (all 3 OS, fail-fast) ─── PASS ───▶ publish-windows ─┐
                                          publish-linux    ─┼─▶ release
                                          publish-macos    ─┘
```

All three `publish-*` jobs run in parallel after the test gate. Each one invokes
`pwsh src/publish/publish.ps1 -All -Rids <subset>` on its host OS — keeping the
build native to its target architecture and (on Windows) honouring the
`maui-windows` SDK workload preflight that `publish.ps1` performs automatically.

| Job | Runner | RIDs |
|-----|--------|------|
| `publish-windows` | windows-latest | win-x64, win-arm64 |
| `publish-linux` | ubuntu-latest | linux-x64, linux-arm64 |
| `publish-macos` | macos-latest | osx-x64, osx-arm64 |

#### Versioning

Each `publish-*` job sets `PublicVersion: ${{ github.ref_name }}` (with the
leading `v` stripped). `publish.ps1` reads `$env:PublicVersion`, forwards it
to `Publish-Rid.ps1` as `-Version`, which:

1. Passes `-p:Version=<value>` to `dotnet publish` so the produced assembly's
   `AssemblyVersion` / `FileVersion` match the tag.
2. Embeds the version in the archive filename:
   - Windows: `ClaudeForge-<version>-<rid>.zip`
   - Linux / macOS: `ClaudeForge-<version>-<rid>.tar.gz`

Local devs who run `pwsh src/publish/publish.ps1` without a `-Version` or
`$env:PublicVersion` get unversioned filenames (`ClaudeForge-<rid>.{zip,tar.gz}`)
and the csproj's baseline `<Version>` is used for the assembly — handy for
sanity-check builds where you don't need the version surfaced.

#### Pre-release detection

Any tag containing a hyphen (`v1.2.3-rc.1`, `v2.0.0-beta.2`) is published as a
pre-release on the Releases page. A plain `v1.2.3` tag creates a full release.

#### Release notes strategy

The `release` job determines the changelog content in priority order:

| Scenario | Source |
|----------|--------|
| Tag's commit is the merge commit of a PR | **PR body** — the author-written description becomes the changelog. Write your changelog in the PR description. |
| Tag is on a direct-push commit with a multi-line message | **Commit message body** — everything after the first blank line. Write changelog markdown directly in the commit message. |
| Neither the PR body nor the commit message has content | **`--generate-notes` fallback** — GitHub auto-generates a categorised list of merged PRs and commits since the previous tag. |

The download table and platform install instructions are always prepended
regardless of which source is used.

#### Triggering a release

```bash
# 1. Merge your PR (or push directly to main with a detailed commit message).

# 2. Tag the resulting commit:
git tag v1.2.3
git push --tags

# Pre-release:
git tag v1.2.3-rc.1
git push --tags
```

The workflow fires automatically, tests on all platforms, builds six archives,
and publishes a GitHub Release with the download table and changelog attached.

---

### `workflows/codeql.yml` — Security Scanning

**Triggers:** push to any branch; any pull request; Monday 08:00 UTC schedule  
**Concurrency:** `codeql-${{ github.ref }}` — cancels stale scans on new pushes.

Uses the `security-and-quality` query suite (OWASP Top 10 CWEs + code quality
checks). Results appear in the repository's **Security → Code scanning alerts**
tab. The weekly scheduled run catches newly-published CVE signatures even when
the codebase has not changed.

---

### `workflows/schema-refresh.yml` — Bundled-schema drift detection

**Triggers:** Monday 09:00 UTC schedule (sibling slot to Dependabot); manual
`workflow_dispatch`.  
**Concurrency:** `schema-refresh` group, `cancel-in-progress: false` — a
manual dispatch never kills an in-flight scheduled run.  
**Permissions:** `contents: write` (commit), `pull-requests: write` (open PR).

Runs `pwsh scripts/refresh-schema.ps1` against `schemastore.org`'s current
schema. The script is idempotent: when the upstream bytes match the bundled
copy, it exits 0 with no working-tree changes and the workflow finishes
silently. When the bytes differ, `peter-evans/create-pull-request@v7` opens
(or updates) a PR on the `chore/schema-refresh` branch with two labels:
`schema-refresh` and `dependencies` — the `dependencies` label routes the
PR out of the release changelog via `.github/release.yml`.

The sibling overlay (`src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.overlay.json`)
is NEVER touched by the script, so this workflow can't silently overwrite
hand-curated additions. Auto-merge is deliberately not configured — the
schema diff requires human review every time.

---

## Supporting configuration

### `dependabot.yml` — Dependency updates

Dependabot opens pull requests every **Monday 09:00 UTC** for outdated
dependencies. Packages are grouped so related updates land in a single PR:

| Group | Patterns | Update types |
|-------|----------|-------------|
| `avalonia` | `Avalonia*`, `Semi.Avalonia*` | minor, patch |
| `communitytoolkit` | `CommunityToolkit.*` | minor, patch |
| `github-actions` | `*` (Actions ecosystem) | minor, patch |

Dependabot PRs are labelled `dependencies` and excluded from the release
changelog by `.github/release.yml`.

### `.github/release.yml` — Release notes categorisation

When the `--generate-notes` fallback is used, GitHub reads `.github/release.yml`
to bucket merged PRs into sections based on their labels.

| Label on the PR | Section in release notes |
|-----------------|--------------------------|
| `breaking-change` | ⚠️ Breaking Changes |
| `enhancement`, `feature` | 🎉 New Features |
| `bug`, `fix` | 🐛 Bug Fixes |
| `performance` | ⚡ Performance |
| `security` | 🔒 Security |
| `documentation`, `docs` | 📝 Documentation |
| `chore`, `refactor`, `ci`, `build` | 🔧 Maintenance |
| `dependencies` | *(excluded — Dependabot bumps)* |
| `ignore-for-release` | *(excluded — internal maintenance)* |
| *(no matching label)* | Other Changes |

---

## Secrets and variables reference

All secrets and variables are set under **Settings → Secrets and variables →
Actions** on the repository.

### Repository secrets

| Secret | Used in | Purpose |
|--------|---------|---------|
| `CODECOV_TOKEN` | `ci.yml` | codecov.io upload token for per-PR coverage tracking |
| `SONAR_TOKEN` | `ci.yml` | SonarCloud project token for SAST analysis |
| `APPLE_SIGNING_CERT` | `release.yml` | Base64-encoded `.p12` Apple Developer certificate |
| `APPLE_CERT_PASSWORD` | `release.yml` | Password for the `.p12` certificate |
| `APPLE_NOTARIZE_USER` | `release.yml` | Apple ID email for `xcrun notarytool` |
| `APPLE_NOTARIZE_PASS` | `release.yml` | App-specific password for notarization |
| `APPLE_TEAM_ID` | `release.yml` | 10-character Apple Developer Team ID |
| `WINDOWS_SIGNING_CERT` | `release.yml` | Base64-encoded `.pfx` Authenticode certificate |
| `WINDOWS_CERT_PASSWORD` | `release.yml` | Password for the `.pfx` certificate |
| `SLACK_RELEASE_WEBHOOK` | `release.yml` | Incoming webhook URL for release announcements |

All secrets above are **placeholders** — the workflow ships without any
signing or notification steps wired up. Add the step bodies as documented in
the comment block at the top of each workflow when you're ready to enable
them; until then the listed secrets can stay unset.

### Repository variables

| Variable | Used in | Default | Purpose |
|----------|---------|---------|---------|
| `DOTNET_VERSION` | `ci.yml`, `release.yml`, `codeql.yml` | `'10.x'` | .NET SDK version for all `setup-dotnet` steps |
| `APP_NAME` | `release.yml` | `ClaudeForge` | Release title prefix + download-table heading. Note: archive filenames are produced by `publish.ps1` and use the assembly name directly — changing `APP_NAME` here only affects the visible release metadata. |
| `CODEQL_QUERIES` | `codeql.yml` | `security-and-quality` | CodeQL query suite |

Variables are optional — hardcoded defaults are used when a variable is not
defined.  See the comment block near the top of each workflow for the exact
`${{ vars.NAME }}` substitution syntax.

---

## Local-vs-CI parity

A developer can reproduce exactly what CI does:

```bash
# Reproduces the trim-check job (CI ubuntu runner):
pwsh src/publish/publish.ps1 -All -Rids linux-x64

# Reproduces the publish-windows job (CI windows runner):
$env:PublicVersion = '1.2.3'
pwsh src/publish/publish.ps1 -All -Rids win-x64,win-arm64
Remove-Item Env:\PublicVersion

# Reproduces the full release on a single host (won't pass — Windows RIDs
# need a Windows host for the maui-windows workload + native binaries):
pwsh src/publish/publish.ps1 -All
```

When CI fails and a local run passes (or vice versa), the divergence is almost
always one of: missing `maui-windows` workload, a stale `bin/obj` the orchestrator
didn't clean (`-Clean` is implicit when called via `publish.ps1`), or a
non-default `$env:PublicVersion` left in the shell environment.

---

## Badge reference

The badges at the top of the README use these URLs:

```markdown
[![CI](https://github.com/JanusMael/ClaudeForge/actions/workflows/ci.yml/badge.svg)](https://github.com/JanusMael/ClaudeForge/actions/workflows/ci.yml)
[![Release](https://github.com/JanusMael/ClaudeForge/actions/workflows/release.yml/badge.svg)](https://github.com/JanusMael/ClaudeForge/actions/workflows/release.yml)
[![Latest Release](https://img.shields.io/github/v/release/JanusMael/ClaudeForge?label=latest)](https://github.com/JanusMael/ClaudeForge/releases/latest)
```

The CI badge reflects the status of the most recent run on the **default
branch** (not on feature branches). The Release badge reflects the most recent
tag-triggered run.
