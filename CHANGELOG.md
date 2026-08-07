# Changelog

All notable changes to ClaudeForge will be documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
version numbering follows [Semantic Versioning](https://semver.org/).

The release workflow auto-generates a download table + install instructions
on every tagged release. For per-release detail beyond what's recorded here,
see the corresponding entry on the [Releases page](https://github.com/JanusMael/ClaudeForge/releases).



## [2026.2.528] - TBD

### Added

- **Filter on the Agents & Skills page** — narrows all three segments
  (Sub-agents / Skills / Slash Commands) by artifact name, description, or source,
  with a match count and a clear button. Section headers drop out when their group
  has no surviving row, so there are no orphan "Yours" / "Plugin" labels above
  nothing. Long lists no longer have to be scrolled to find a known item.
- **Deep links (`--deep-link <path>`)** — launch straight into a page, tab, or
  item: `--deep-link claude-code/permissions`,
  `--deep-link agents-skills/skills/pdf`. The grammar is
  `page[/tab][/item]` (or `product/page[/…]`), keyed on new stable
  `NavigationNodeViewModel.NodeId` identifiers rather than display labels, so a
  link keeps resolving in any language. A deep-linked item is revealed by
  filtering the list to it, with the filter box outlined to show the narrowing
  came from navigation. An unresolvable path is logged and ignored — it never
  blocks launch. See the README "Deep links" section.
- **"Copy deep link"** on the Agents & Skills detail toolbar — puts a ready-to-use
  `--deep-link` path for the open artifact on the clipboard, fully qualified as
  `name@source`, so nobody has to derive an id by hand (and a path can be pasted into
  a ticket or a runbook). A malformed `--deep-link` now reports itself on the terminal
  with the valid pages listed instead of failing silently — the binary is a `WinExe`,
  so it attaches to the parent console to do it — and a well-formed-but-unresolvable
  path raises a status-bar warning. An unresolvable *persisted* path stays quiet,
  since that is routine after deleting an artifact.
- **Your place is kept below the page level** — the active tab and open item are
  persisted (`WindowState.lastDeepPath`) and restored on relaunch, instead of only
  the page. **Reload Window** restores the full state, including an edit in
  progress (see Fixed). Reusable via the new `IDeepNavigable` contract; the
  Agents & Skills page is the first adopter.
- **Model catalog** — a single bundled source of truth
  (`src/ClaudeForge.Core/Assets/ModelCatalog/model-catalog.json`) for the allowed
  `model` / `effortLevel` / `permissions.defaultMode` values and their
  inter-relationships, replacing several hardcoded lists. Surfaced through the
  new SDK accessor `IClaudeConfigClient.Models` (`IModelCatalogAccessor`); the
  curated file is overlay-able (`model-catalog.overlay.json`, RFC 7396) and
  validated by `scripts/validate-model-catalog.ps1` +
  `.github/workflows/model-catalog-refresh.yml`.
- **Model-aware effort & mode editors** — the Essentials effort dropdown now
  shows only the levels the selected model supports; an invalidated effort
  auto-coerces to the nearest analog (e.g. `max`/`xhigh` → `high` on Sonnet 4.6)
  as an editing-scope override, surfaced in the Save preview. A model with no
  effort (Haiku) disables the control. `permissions.defaultMode = auto` is gated
  to auto-capable models at User scope, with the option filtered out and an
  ineligible selection coerced to `default`. A read-only "current model —
  supports …" indicator sits beside the effort editor.
- **Editable model field** — the Essentials model card is now a free-form
  AutoCompleteBox (catalog entries are suggestions; any custom id can be typed).
- **`bypassPermissions` search shortcut** — typing "bypass" deep-links to the
  Default Mode editor with a hint, distinct from the `--dangerouslySkipPermissions`
  flag result and the "disable bypass" card.
- **Dynamic-access tripwire** — the build-time dead-string guard now also fails
  the build on by-name/reflective resource access (`Strings.ResourceManager`,
  `typeof(Strings)`) in project source, keeping its literal-`Strings.<Key>`
  analysis sound.

### Fixed

- **A saved Agents & Skills edit left its list row stale.** `SaveAsync` refreshed
  the detail pane but never the row's subtitle, which is what the list renders —
  so editing a `description` and saving kept showing the old text until the next
  full refresh, making the edit look like it hadn't taken.
- **Reload Window silently discarded an in-progress front-matter edit.** That
  editor writes files directly, so its buffer never counted toward
  `HasUnsavedChanges` and nothing warned. The unsaved text now rides across the
  in-process reload in memory and comes back with the editor — the user's actual
  text, not a re-read from disk. It is never written to the UI-state file.

### Changed

- `model` / `effortLevel` / `permissions.defaultMode` option lists are now
  catalog-driven and inter-aware rather than hardcoded.
- Fixed 'missing files' that originate in the 'selected project' tree during backup scenarios
- Newly available localizations

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
