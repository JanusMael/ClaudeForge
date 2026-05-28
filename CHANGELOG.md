# Changelog

All notable changes to ClaudeForge will be documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
version numbering follows [Semantic Versioning](https://semver.org/).

The release workflow auto-generates a download table + install instructions
on every tagged release. For per-release detail beyond what's recorded here,
see the corresponding entry on the [Releases page](https://github.com/JanusMael/ClaudeForge/releases).



## [2026.2.528] - TBD

### Changed

- Fixed 'missing files' that originate in the 'selected project' tree during backup scenarios
- Newly available localizations

### 

## [2026.2.527] - [2026.2.528]

### Added

- Public-release CI/CD scaffolding: tag-triggered release workflow that
  delegates to `src/publish/publish.ps1` for all six RIDs (win-x64, win-arm64,
  linux-x64, linux-arm64, osx-x64, osx-arm64).
- `$env:PublicVersion` support in `publish.ps1` / `Publish-Rid.ps1` — versioned
  archive filenames and assembly stamping flow from a single env var.
- Weekly bundled-schema drift detector (`.github/workflows/schema-refresh.yml`):
  runs `scripts/refresh-schema.ps1` against schemastore.org every Monday and
  opens a `chore/schema-refresh` PR when upstream has changed. The sibling
  overlay (`claude-code-settings.overlay.json`) is untouched, so hand-curated
  additions persist across refreshes.
- Agent & Skills page + bug fixes
- Added - Scope-aware agent/skill/command discovery
- Added - Agents & Skills page with basic viewing and front-matter editing experiences

### Changed

- CI trim-check now invokes `publish.ps1` (same entry point as the release
  workflow), so the closure analyzer + IL-warning scan run on every PR.
- Publish scripts silence the cmdlet progress UI (`Remove-Item -Recurse`,
  `Get-ChildItem -Recurse`) so build output is readable in IDE output panes
  that don't render VT escape sequences.
- Improved - perf via de-bouncing saves for some scenarios
- Fixed - ObjectDisposedException + binding null-traversal warnings
- Fixed - Binding noise in hooks/MCP/permissions editors
- Fixed - Essentials SetValue scope consistency
- Fixed - Inherited-display row showing Dictionary<K,V>.ToString()

## [2026.2.523] - [2026.2.527]

Initial public release. See [README.md](./README.md) for the feature highlights
and the [Releases page](https://github.com/JanusMael/ClaudeForge/releases/tag/v1.0.0)
for the full per-platform binary list once tagged.
