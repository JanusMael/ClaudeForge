# Changelog

All notable changes to ClaudeForge will be documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
version numbering follows [Semantic Versioning](https://semver.org/).

The release workflow auto-generates a download table + install instructions
on every tagged release. For per-release detail beyond what's recorded here,
see the corresponding entry on the [Releases page](https://github.com/JanusMael/ClaudeForge/releases).



## [Unreleased]

### Added

- **Saving a config now preserves its comments and formatting.** Writes go through a
  JSONC editor that edits the bytes in place rather than re-serializing the document,
  so comments, key order, blank lines and indentation survive a save. Previously a
  save rewrote the file from a parsed object and quietly discarded all of it. The
  previous behaviour is available for one release as `--writer legacy` if a file
  round-trips wrongly; please report it if you need that flag.
- **Every setting now says how much it matters.** A severity indicator sits on
  settings rows, on search results, and in the effective view — so scanning a page
  shows at a glance which values carry weight and which are routine. The classification
  is Claude Code's own answer about its own settings, not a generic heuristic, and it
  travels per product rather than being a colour chosen in markup.
- **The save dialog says which pending changes weaken a boundary.** Before writing,
  the preview calls out edits that loosen a permission or a safety-relevant setting,
  instead of listing every change with equal weight.
- **An Artifacts page** — the first page that is not a settings group. It shows the
  agents, skills and commands resolved for the current workspace, with where each one
  came from.
- **Schemas are fetched, and the app says which copy it used.** A launch tries the
  upstream schema first and falls back to the bundled copy, and every section of the
  navigation carries a badge naming the copy it was built from. *Check for schema
  updates* in the About dialog re-fetches on demand and re-labels those badges. A
  product with no upstream — Claude Desktop, whose schema is hand-maintained — is
  omitted from a check's results rather than reported as up to date, so the result
  never describes a fetch that was not attempted.
- **`--schema-source <bundled|fetched>`** forces one branch of that chain, for
  reproducing a report against a known copy. `fetched` is fatal if the fetch fails
  rather than falling back, because a run that silently used the bundled copy would
  prove nothing.
- **Live config-file events, and a log of them on disk.** **Shift+F12** opens a window
  showing config-file changes as they happen, and the same stream is written to
  `logs/events-*.txt` beside the executable, with the scope each change belongs to.

### Changed

- **Backup wording comes from the host application.** Progress phase labels, the
  Clients column's short names, and the credentials prompt now name the host's own
  credential store instead of using generic text.
- **The accent colour and the "✨ NEW" badge are owned rather than borrowed.** The badge
  is a tint pill rather than a solid chip, and the accent no longer depends on an
  undefined system brush that rendered differently across platforms.

### Fixed

- **Themed colours now follow a light/dark switch immediately.** Severity glyphs and
  other themed elements kept whichever palette was live when they were last drawn, so
  a switch could leave one screen showing both palettes at once.
- **Screen readers now announce the interface.** Navigation rows in the tree, settings
  tabs, rows in four list boxes, the spinner buttons on numeric fields, composite
  controls, and every control in the diagnostics windows previously announced nothing
  or read out an internal type name. The diagnostics window's header links are now
  reachable by keyboard with a visible focus ring.
- **The Artifacts page painted its metadata red and its problems grey**, inverting the
  two colours that matter most on it.
- **The `apiKey` escalation warning never fired.** The condition it was guarded by
  could not be true.
- **Sharing did nothing on Windows, and said nothing anywhere.** *Share config* on the
  effective-settings view now copies the JSON to the clipboard, and the status bar reports what
  actually happened — copied to the clipboard, opened in your browser, handed to your mail
  client — rather than completing in silence. *Share log* in the About dialog and *Share* on a
  backup row got the same treatment: both say the file was revealed in your file manager, which
  is what they do. A share that fails now says so and stays on screen until dismissed;
  previously it was recorded only in the log.
- **A config file created while the app was running was not picked up** — a new
  `settings.local.json` or `.mcp.json` is now watched from the moment it appears.
- **The live-log window hid itself when F12 was pressed again**, instead of staying put.
- **A config that fails to parse is no longer installed by a reload**, and overlapping
  reloads are serialized rather than each guarding itself.
- **Backup patterns: `/foo` matched nothing and `**/foo` matched too much.**


> **Releases 2026.2.612 through 2026.3.901 are not written up here.** Eight releases
> shipped in that window while this file was not being updated; their auto-generated
> notes are on the [Releases page](https://github.com/JanusMael/ClaudeForge/releases).
> The gap is stated rather than left to look like a quiet period.

## [2026.2.528] - [2026.3.916]

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

- **Screen readers had nothing to announce in the diagnostics windows.** The crash
  and notice dialogs, the F12 live-log window, and the live-tail window are built in
  C#, so the AXAML accessibility guard never saw them: their buttons, text boxes,
  log list, Copy menu item, and link-styled header text carried no
  `AutomationProperties.Name`. Every interactive control there now has a clean-text
  name (and help text where the label alone is ambiguous), the header links announce
  as links, and a headless test in `LayeredEditors.Avalonia.Diagnostics.Tests` fails
  if a control lands without one.
- **Every number field and model picker announced itself as an unnamed edit box.**
  `NumericUpDown` and `AutoCompleteBox` are composite controls: each carried the correct
  screen-reader name, but neither ever holds focus — focus goes to an inner text box that
  had no name of its own, so the name sat on an element a screen-reader user never lands
  on. Measured with UIA: 6 number fields and 8 pickers. The text box now inherits the
  name the view already sets, so tabbing into "Max Output Tokens" announces
  "Max Output Tokens" instead of nothing. A field whose name is genuinely missing stays
  unnamed rather than being papered over.
- **Screen readers read out `Avalonia.Controls.PathIcon` on every number field's
  up/down buttons.** Those two buttons come from the `NumericUpDown` control template,
  not from any view, so nothing could annotate them and the AXAML accessibility guard
  had no element to flag at any scan width — and with no name set, Avalonia announces a
  `Content.ToString()`, which for an icon is its type name. Twelve buttons in ClaudeForge:
  Essentials (Max Output Tokens, Max Thinking Tokens), General, Sandbox, and the
  Backup / Restore retention count; six more on OpenCodeForge's Essentials page. They now
  announce "Increase value" / "Decrease value", translated in all eight locales, named
  once in the shared theme so both apps get it.
- **Screen readers announced every tab in the app as a class name.** A `TabItem` is
  focusable and selectable, so a screen-reader user lands on one — but unnamed, UI
  Automation falls back to the bound item's `ToString()`. Settings pages announced
  their tabs as "Bennewitz.Ninja.ClaudeForge.ViewModels.GroupTab" and Agents & Skills
  announced its own as "Avalonia.Controls.ScrollViewer". The accessibility guard
  never caught it because `TabItem` was not in its list of interactive controls;
  it is now, so this cannot come back. The tab strip on the settings pages names the
  `TabItem` container rather than the header text inside it, which is what UI
  Automation actually reports. No new strings — every tab already had a localized
  header to announce.
- **A deep link into a tab worked on exactly one page.** Only Agents & Skills
  implemented the deep-navigable contract, so every settings page — Hooks,
  Permissions, General, all of them — logged "not deep-navigable; 1 segment(s)
  dropped" and landed on whichever tab it defaulted to, and nothing below the page
  was persisted, so place-keeping could not bring a tab back either. The README had
  documented `claude-code/permissions/properties` as working the whole time. Every
  settings group page, Effective Settings and Backup / Restore now round-trip their
  tab, and a tab contributed by the per-group customizer addresses exactly like a
  built-in one — `claude-code/hooks/hooks.flow` opens the Hooks flow diagram. A tab
  id the page does not have is now reported as a miss instead of being applied
  silently and ignored.
- **Every plugin artifact's remembered position was thrown away on the next
  launch.** The captured path qualifies an item as `name@source`, and a plugin's
  source is itself a path (`claude-plugins-official/plugins/math-olympiad`) — so the
  path split into five segments, was rejected against the four-segment maximum, and
  the restore fell back to the default tab. "Copy deep link" emitted the same
  unusable string. Path separators inside a source are now written as `:`, so the
  whole qualifier stays in one segment; both spellings are accepted when a path is
  typed by hand.
- **A skill whose `description` was a folded block scalar showed `>-` instead of its
  description.** Nearly every skill Claude Code ships writes `description: >-` with the
  prose on the following indented lines, and the front-matter parser understood only
  plain and quoted scalars: the value read as the literal header token, and the prose
  lines fell through as unparsed filler that an edit would have stranded. Block scalars
  — folded (`>`) and literal (`|`), with all three chomping indicators (`-`, `+`, none)
  and an explicit indent digit — now parse to their real text, and a field keeps its
  shape when edited, so changing a `>-` description re-renders as a folded block
  instead of collapsing the file into one very long line. An untouched block still
  round-trips byte-for-byte. A description carrying newlines is flattened to one line
  for the list row, which is a single ellipsised line; the detail pane and the editor
  keep the real multi-line value.
- **A description split across lines without a `>` or `|` read as empty.** A YAML
  value can be carried over several indented lines with no block indicator at all,
  opening either on the key line or the line below it — Anthropic's own
  `math-olympiad` plugin skill writes its description that way, so it showed
  "(no description)". Those lines now fold into the value, and the surrounding
  quotes come off the joined result rather than either line alone, which is what
  left a stray `"` on the shapes that did parse. Editing one re-renders it as a
  folded block rather than collapsing it into a single very long line.
- **A nested mapping was flattened into phantom top-level fields, and editing one
  broke the file.** `metadata:` with indented `node_type:` / `type:` beneath it
  surfaced `node_type` and `type` as if they were top-level keys; writing one then
  re-rendered it at column 0, silently lifting it out of its parent. Nested
  mappings are unmodelled by design, so one is now consumed whole and re-emitted
  verbatim — it round-trips byte-for-byte and is invisible to the typed read/write
  surface, which is what the class documentation already promised. The supported
  and unsupported YAML constructs are now written down in
  [docs/YAML-FRONT-MATTER.md](docs/YAML-FRONT-MATTER.md).
- **A saved Agents & Skills edit left its list row stale.** `SaveAsync` refreshed
  the detail pane but never the row's subtitle, which is what the list renders —
  so editing a `description` and saving kept showing the old text until the next
  full refresh, making the edit look like it hadn't taken.
- **Reload Window silently discarded an in-progress front-matter edit.** That
  editor writes files directly, so its buffer never counted toward
  `HasUnsavedChanges` and nothing warned. The unsaved text now rides across the
  in-process reload in memory and comes back with the editor — the user's actual
  text, not a re-read from disk. It is never written to the UI-state file.
- **F12 inside the live-log window did nothing.** The window's title promises
  "F12 to hide", but the toggle lived on the main window's key handler, which never
  sees a key pressed while the log window has focus. The window now hides itself
  on plain F12; Shift+F12 stays with the host. Covered by a headless test in
  `LayeredEditors.Avalonia.Diagnostics.Tests`.
- **`dotnet pack` of `LayeredEditors.Avalonia.Diagnostics` failed** because the
  project names a `PackageReadmeFile` it did not ship. The package now carries a
  README describing the three-call wiring and each piece.

### Changed

- **The accessibility coverage guard now sees the whole app.** It scanned
  `src/ClaudeForge/Views/*.axaml` flat, which left 16 of the repository's 43
  AXAML files — everything under `Controls/`, all of `ClaudeForge.Avalonia`,
  all of `LayeredEditors.Avalonia` — never examined, and did not count
  `MenuItem` or `RepeatButton` at all. It now walks the three view-bearing
  assemblies recursively. Five controls that were missing a screen-reader name
  gained one, each reusing the string key its own label or tooltip already
  used: the copy items in the Save Changes dialog and Effective Settings, Open
  File Location in Backup & Restore, and the model picker's `▾` button, whose
  content is a glyph a screen reader would otherwise announce as "▾".
- **The status pills' contrast contract is now a test.** `App.axaml` stated in
  prose that every pair clears 4.5:1 on its pill and 7.3:1 on the page, and asked
  whoever retints one to recheck both numbers by hand. `StatusPaletteContrastTests`
  reads those very brush values and does the recheck on every build. The margin is
  thinner than the prose suggests — the worst pill pair clears by 0.07 and the
  worst page pair by 0.05 — so the palette was one careless retint from a
  regression nothing would have caught.
- `model` / `effortLevel` / `permissions.defaultMode` option lists are now
  catalog-driven and inter-aware rather than hardcoded.
- Fixed 'missing files' that originate in the 'selected project' tree during backup scenarios
- Newly available localizations
- **The status bar's auto-clear runs on an injected `TimeProvider`**, and
  `StatusController` emits only through the typed `SetActive` / `SetSuccess` /
  `SetWarning` / `SetFailure` / `SetState` methods — the kind can no longer be
  passed as a parameter, so it cannot be passed wrongly. Three mutable statics
  (`DelayOverride` and the two delay properties) and their `ResetForTesting`
  companion are gone with it; the delays are per instance. The tests advance a
  fake clock instead of overriding a delay, so none of them sleeps, polls, or
  needs a pumped dispatcher, and a warning's longer dwell time is now actually
  asserted rather than assumed. A clear that comes due just before the next
  message is posted no longer clears that message.

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
