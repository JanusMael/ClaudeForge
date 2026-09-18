# Changelog

All notable changes to ClaudeForge will be documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the
version numbering follows [Semantic Versioning](https://semver.org/).

The release workflow auto-generates a download table + install instructions
on every tagged release. For per-release detail beyond what's recorded here,
see the corresponding entry on the [Releases page](https://github.com/JanusMael/ClaudeForge/releases).

Sections from `2026.2.612` onward were reconstructed from the published release
descriptions, which are the authoritative record of what each release contained.
The two oldest sections predate that and keep their original `[from] - [to]`
range headings: the releases they describe carry no notes, so there is nothing to
reconcile them against and renumbering them would be guesswork.

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

- **Restoring a backup now puts your project's files back.** A backup taken with a project
  open contains that project's `.claude/` files, but restore quietly skipped any project
  kept outside your home folder — reported success, wrote nothing, and described the
  refusal as a path that was *"not present on this machine"*. Projects your Claude Code has
  opened are now restored wherever they live, and anything genuinely refused says so in
  words that match the reason.
- **A restore cleans up after itself.** Files it overwrites are still moved aside as
  `.pre-restore-*.bak` first, and once the restore has completed without a single failure
  those copies are removed and counted in the result — previously every one of them stayed,
  roughly doubling the size of `~/.claude` on each restore with nothing saying so. A restore
  that could not place every file keeps them, deliberately, and says that instead.
- **Editing one environment variable no longer deletes the others.** Saving a change to
  any variable the app recognises removed every variable it did not — proxy settings,
  internal tool paths, anything an organisation adds that the schema has never heard of.
  Variables the app does not model are now left exactly as they were. The same applies
  to any other settings object: keys the editor does not render are no longer keys it
  deletes.
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


## [2026.3.916] - 2026-09-16

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

## [2026.3.901] - 2026-09-01

Claude Fable 5.1 (`claude-fable-5-1`) is the current Fable build, so the `fable`
alias now resolves to it and Fable 5 becomes a legacy-but-still-pinnable row.
Verified against the models overview rather than recall — Fable 5 is listed
under "Legacy models (still available)".

Followed the AGENTS.md / docs/MODEL-CATALOG.md "new model launch" playbook:

- model-catalog.json — new `claude-fable-5-1` row (1M context, effort low..max
  incl. ultracode, default `high`, auto-mode capable, copied from the outgoing
  build); `alias: "fable"` moved onto it; `aliases.fable` repointed; the previous
  holder demoted to `legacy: true, alias: null` (row kept — still a valid pin).
  Invariant preserved: exactly one non-legacy row per family alias.
- claude-code-settings.overlay.json — AutoCompleteBox suggestion and the
  `model.description` example swapped to the new snapshot id.
- enumdescriptions.json — added the 5.1 pin tooltip, refreshed the `fable` and
  `best` tooltips, and REPLACED the old pin row rather than accumulating it.
  `model.examples` <-> tooltip keys are exactly 1:1 (11/11, no orphans).

No test change needed: ModelCatalogTests only hardcodes the `opus` alias target.
The bundled JSON schema is untouched — schemastore.org omits model names, so
`refresh-schema` is never the mechanism that adds a model (a dry run confirms
zero content changes; PR #33 already brought the schema current).

Docs: both playbooks now name where to verify model facts
(platform.claude.com models overview) and warn that a family can supersede
itself within a generation — Fable 5 -> 5.1 is not a whole-number bump. That
lookup was the one step being re-derived every session.

Verified: validate-model-catalog.ps1 passes (models=9, aliases=4); full suite
2708 passed / 11 skipped / 0 failed.

## [2026.3.810] - 2026-08-10

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

## [2026.3.724] - 2026-07-24



### Added

- **Claude Opus 5 in the model picker.** The `opus` alias now resolves to Opus 5,
  and it appears in the Essentials model card and the Model & Effort suggestion
  list with its own tooltip. Opus 4.8 remains selectable as a pinned snapshot
  (`claude-opus-4-8`).
- **Memory page — "Configuration" category.** The JSON files Claude reads are now
  inventoried alongside memory files: user-scope `settings.json`, `mcp.json`,
  `managed-settings.json` (and its `managed-settings.d/*.json` drop-ins) and
  `~/.claude.json`, plus the open project's `settings.json`, `settings.local.json`,
  and `mcp.json` — each openable from the list. Credentials are deliberately
  excluded.
- **About page — config file path shown.** The resolved path of the primary config
  file (e.g. `~/.claude/settings.json`) is now displayed and selectable next to the
  Open / Reveal buttons, so it's clear which file those actions target.

### Fixed

- **Pages no longer show stale state after changes made outside the app or on
  another page.** Each page now re-reads its source when you navigate to it, rather
  than only at first load:
  - **Essentials** — model, effort, token-limit, and update-channel cards refresh
    after an external `settings.json` edit or an edit made on the Model & Effort page.
  - **Environment** — a User / Machine environment variable set outside the app now
    appears without a settings reload or restart (current selection is preserved).
  - **Memory** — a file created after launch (e.g. a new global `~/.claude/CLAUDE.md`)
    shows up without pressing Refresh.
  - **Agents & Skills** — agents, skills, and commands added after the first visit
    now appear.
  - **Profiles** / **Backups** — profiles and archives created or removed outside the
    app are reflected.
  - **About** — "Open Config" / "Reveal Config" no longer stay disabled for the rest
    of the session when the config file is created after launch.

## [2026.3.715] - 2026-07-15



### Added

**One model picker, everywhere.** Essentials and Model & Effort had two different
controls — Essentials had fuzzy, friendly suggestions but looked like a bare text box;
Model & Effort had the drop-down chevron but listed raw hyphenated ids. They're now a
single shared `ModelPicker`: friendly brand names ("Opus 4.8") over the dim raw id, fuzzy
matching (typing `Opus` finds `opus`, `claude-opus-4-8`, `opus[1m]`), **and** the chevron
that drops the full list. A custom id can still be typed by hand.

**Environment page: `env` is now browsable.** Its ~305 declared variables render as
collapsible, virtualized categories grouped by name prefix — `CLAUDE · 159`, `OTEL · 30`,
`ANTHROPIC · 40`, … plus `Other` — with headers in the page accent colour. Previously a
single flat wall of editors.

**`theme` is a real picker.** It's schema-defined as a string (a fixed enum *or* a
`custom:<slug>` reference), but was falling through to the raw-JSON box. It now renders as
a value picker with the 7 theme names as suggestions and free-form typing for
`custom:<slug>` — which also makes the old, baffling *"Value matches none of the 2
permitted variants"* error impossible to hit.

**Smarter raw-JSON editor** for settings with no structured editor: a **Format** button
(parse + pretty-print) and live structural validation — a wrong root kind ("expects a JSON
array, but the value is an object") or a missing required property now surfaces inline as
an amber advisory, distinct from the red hard parse error.

**Loading is no longer easy to miss.** App load (which can take several seconds, while the
window is still painting) now shows a centered progress card over a dimmed content area,
replacing the 100×4 bar tucked into the status bar's bottom-right corner.

**Richer schema-validation banner** — reports the offending value, the scope that defines
it, and the permitted values, and stays selectable/copyable.

**Memory → User Memory** renders front-matter richly, matching Agents & Skills.

**JSON tooltips + formatted-JSON popups** are now consistent across the grids and the
Effective tabs (previously only some surfaces had them).

**Deep-link filters are visibly marked** — navigating from a search result injects a
property filter, and the filter box now carries an orange frame matching the deep-link
Back button, which clears the moment you edit or clear the filter.


- `[Editor.Rebuild] group=… editors=N elapsedMs=T` and `[Editor.Activate] … rebuilt=…` —
  shows whether a slow page is a view-model rebuild or view realization.
- `[PropView.Realized] group=… wrappers=N sinceCtorMs=T` — a standing perf guard. A healthy
  page realizes a screenful; a count in the hundreds means something is eagerly building a
  subtree again (this is what localized the Environment regression).

### Changed

**Environment properties page: ~4.4s → instant.** The `env` object rendered its ~305
children eagerly through a non-virtualized nested list, realizing **306** property-editor
rows on every visit. (The page's top-level list was virtualizing correctly all along —
`Advanced`, with 94 *top-level* editors, only ever realized 7. The cost was nesting, not
count.) Large objects now render as collapsed categories whose children aren't built until
expanded, and each expanded section is bounded and virtualized.

Mid-sized objects (e.g. `sandbox`, 35 children) are untouched and still render inline —
the accordion is reserved for the pathological case (>150 children).


- **`docs/AVALONIA-GOTCHAS.md`** — three new sections: *Styling / theming* (LocalValue
  outranks every Style setter; a control's `Styles` apply to descendants, not itself — and
  both traps can mask each other), *Templates / controls* (`DataTemplate`s match in
  declaration order, so a subclass template must precede the base; `ItemFilter` supersedes
  `FilterMode`), and *Virtualization / perf* (virtualization is per items-host and needs a
  bounded viewport; `IsVisible="False"` still realizes a subtree).
- **`AGENTS.md`** — the same rules added to the agent rules table and topic index.

### Fixed

- **Diagnostics windows crashed on re-open.** Closing the F12 log window (or the Shift+F12
  config-events window) with the OS ✕ destroyed it, so the next toggle threw
  `Cannot re-show a closed window`. They now hide on close instead.
- **The Essentials model editor rendered blank** (title and description, no input). A
  subclassed `AutoCompleteBox` takes its own type as style key, so it matched no
  `ControlTheme` and got no template.
- **The deep-link orange filter frame never appeared** — for *any* deep link, not just
  search. Two Avalonia traps stacked: the style was scoped to the element it targeted
  (a control's `Styles` apply to descendants, not itself), and the element also set
  `BorderBrush` as an attribute — a *LocalValue*, which outranks every Style setter.
- **Claude Desktop → Version Information:** "Open Config" and "Show in files" were disabled.

## [2026.3.710] - 2026-07-10



### Added

#### Rendered markdown body on the Agents & Skills page
The Sub-agents / Skills / Slash-commands viewer now renders the file body as **formatted markdown** (view mode) instead of a raw monospace `TextBox`, matching the Memory viewer. Edit mode still shows the raw text, so *what you edit stays honest*.

- Front-matter card and body now share **one scroll region**, so the front-matter scrolls away with the content instead of the body scrolling in its own inner pane.
- New **Copy markdown** toolbar button — copies the full file (front-matter + body recomposed), so a paste is a self-contained agent/skill/command definition.

*Files:* `Views/AgentsSkillsEditorView.axaml[.cs]`, `ViewModels/AgentsSkillsEditorViewModel.cs`

#### Reusable `MarkdownBodyView` control
Extracted the markdown renderer (the dark-theme `MarkdownStyle` + the force-restyle tree-walk that fixes Markdown.Avalonia's inline `Foreground`) into a single shared control used by both **Memory** and **Agents & Skills**. Removes ~460 lines of duplicated styling/code-behind from the Memory view. Exposes a `Markdown` property and a `VerticalScrollBarVisibility` knob (Disabled lets a host's outer scroller own scrolling).

*Files:* new `Controls/MarkdownBodyView.axaml[.cs]`; `Views/MemoryEditorView.axaml[.cs]` (slimmed); localization: removed deprecated `ButtonCloseViewer` string (Memory now uses `ButtonBack`) across all 10 locales.

> Note: this refactor and the Agents & Skills feature above touch overlapping files — cherry-pick them together.

#### Live config-file-events window (Shift+F12)
A second floating diagnostics window that streams **every debounced config-file-watcher hit** in real time, tagged with how the app reacted (`external change → reloading`, `self-write suppressed`, etc.) — useful for watching external edits from the Claude CLI or other editors. Opt-in via the diagnostics options; reachable by **Shift+F12** or a launch link in the F12 log window's header. Also adds **row multi-select + Ctrl+C / right-click Copy** to the existing F12 log window.

*Files:* new `LayeredEditors.Avalonia.Diagnostics/UI/LiveTailWindow.cs`; `AvaloniaDiagnostics.cs`, `AvaloniaDiagnosticsOptions.cs`, `UI/LiveLogWindow.cs`; `Program.cs`, `ViewModels/MainWindowViewModel.cs` (feed), `Views/MainWindow.axaml.cs` (Shift+F12).

---


#### Navigation & command tracing
Added structured `Log.Information` traces: `[App.Nav]` for every landed-on page, `[App.Command] NavigateBack`, plus `[Memory.Command]` / `[AgentsSkills.Command]` actions.

*Files:* `ViewModels/MainWindowViewModel.cs`, `MemoryEditorViewModel.cs`, `AgentsSkillsEditorViewModel.cs`

#### Tab-switch tracing across all tabbed pages
Every `TabControl` in the app now logs tab switches on a VM-driven `SelectedIndex`. Rebuild/programmatic re-selection is suppressed where applicable, so only real user clicks (and deep links) are logged.

- `[Editor.Tab]`, `[Memory.Tab]`, `[AgentsSkills.Tab]` — settings groups, Memory, Agents & Skills
- `[Backup.Tab]` — Backup / Restore / MSIX Fix
- `[Effective.Tab]` — Effective Settings (Properties / Raw JSON)

*Files:* `ViewModels/SettingsGroupEditorViewModel.cs`, `MemoryEditorViewModel.cs`, `AgentsSkillsEditorViewModel.cs`, `BackupRestoreViewModel.cs`, `EffectiveSettingsViewModel.cs`; `Views/BackupRestoreView.axaml`, `EffectiveSettingsView.axaml`

### Fixed

#### Save button no longer disabled by the install banner
`CanSave` gated on `!ShowInstallBanner`, so the `--showInstallBanner` debug flag disabled **Save** even with products installed and real unsaved changes pending. Save now depends only on there being unsaved changes (and not being mid-load). Covered by a new regression test.

*Files:* `ViewModels/MainWindowViewModel.cs`, `tests/ClaudeForge.Tests/ViewModels/HasUnsavedChangesRecheckTests.cs`

---

## [2026.3.708] - 2026-07-08

#### ClaudeForge — changes (2026‑07‑07 → 07‑08)

#### Hooks — schema descriptions, SDK‑first

- Surfaced hook **event**, **command‑type**, and **field** descriptions from the schema through the SDK (`IHooksAccessor`), so headless callers and the editor share one source. New Core types `HookEventInfo` / `HookEventCatalog` / `HookCommandVariantInfo`; `SchemaRegistry` gained `GetHookEvents` / `GetHookCommandVariants`; killed the hardcoded `CommandTypeInfos` mirror.
- **Bug fixed:** event descriptions never rendered in the app — the GUI builds its client via `FromExistingWorkspace` (no `OpenAsync`), so `SchemaHookEvents()` read an empty `_cachedSchemaNodes`. Added a bundled‑schema fallback + regression tests.

#### Hooks — new "Flow" tab

- Added a **Flow** tab between Properties and Effective, hosting your `hooks-lifecycle.svg`, via the `IGroupTabCustomizer` mechanism (now threads a `SelectTab` deep‑link callback).
- Added `Svg.Controls.Skia.Avalonia 12.0.0.13` (+ `Svg.Skia` → 5.1.1) for a native `<svg:Svg>` control; new `HooksFlowView` with **zoom** (Ctrl+wheel cursor‑anchored, ±, Fit‑width, Reset), scroll, natural‑size render, and a floating overlay toolbar (so the diagram scrolls under the pane, not under a band).
- New localized strings (`HeaderTabFlow`, `LinkHookFlowDiagram`, zoom labels) across all 8 locales.

#### Hooks — Properties polish

- Event header → 2‑line (name + **Add hook** on row 1, full wrapped description below); Add‑hook now sits right after the event name.
- **"View flow diagram"** link moved up to the "Hooks" property‑name header via a new generic `HeaderAction` slot on the base `PropertyEditorViewModel` (compiled‑bound, reusable by any editor).

#### Model catalog / effort levels

- Refreshed schema narrowed the settings‑file `effortLevel` to `[low, medium, high, xhigh]` (max/ultracode are **session‑only**); fixed the parity test.
- Added **`ultracode`** as a session‑only, non‑persisted tier to mirror Claude Desktop's slider, on models with the full xhigh+max range (Fable 5, Opus 4.8, Sonnet 5, Opus 4.7).

#### Bundled schema

- Refreshed `claude-code-settings.json` from schemastore.org to current (+~1,200 lines)

#### Test infra

- Fixed the order‑dependent flake `DeleteFootprintAsync_NoDialogService_DeletesImmediately`: the `AsyncLocal` test‑home override didn't propagate from the sync `[TestInitialize]` into the async test's `Task.Run` (which reads `ClaudeHome` on a pool thread), so it deleted from the wrong home. Re‑asserted the override in `NewFakeClient()`; kept `AsyncLocal` (Sdk.Tests runs parallel and needs it). Backed out an `AsyncLocal`→static attempt that caused ~48 races/run in Sdk.Tests.

<!-- Release notes generated using configuration in .github/release.yml at v2026.3.708 -->

## [2026.3.701] - 2026-07-01

- Added buttons to the UI on  'memory artifact' list views that allow for direct 'deletion' of those items
- Updated the 'model list' overlay for the currently supported Claude models (including the just-released Sonnet 5)

## [2026.2.615] - 2026-06-15



### Added

- **Plugin Hooks tab** (Hooks page) — read-only view of hooks contributed by installed plugins, with one-click **copy into `settings.json`** (routed through the normal add path; copy is disabled for events `settings.json` can't express). Hooks are organized into **per-plugin accordions**, and within a plugin an event with **multiple hooks gets its own nested event accordion** (a lone hook renders inline). Theme-aware styling; the command shows in a reusable read-only/copyable text box. — `d20a2d5`, `ff9ae5a`
- **Model picker** — per-value hover tooltips explaining each option (`best`, `opusplan`, the `[1m]` extended-context variants, alias vs pinned ID) and **newest→oldest ordering** with each `[1m]` variant above its base alias. Both fully data-driven. — `5c43950`, `1d2e8fe`

### Changed

- **Permission builder hints** — dropped the Wildcards/Anchors segmented toggle; both groups now show at once, stacked under sub-labels, and every tool's hint box gets a **"Hints"** caption. — `ae6d322`
- **Editor view-models are now UI-free** — extracted into dedicated projects with no Avalonia dependency, so a view-model physically can't reference a UI control (compile-time enforced). — `c609cee`, `d20a2d5`
- Removed the **"[Experimental]"** tag from the dry-run tester labels. — `e21f660`


- **Non-freezing startup** — settings editors (every group + child property editor) build off the UI thread; the window paints the Welcome page immediately while the sidebar fills in. — `fe043e5`

#### Internal / Tests

- Guard test that the full editor set constructs off the UI thread (startup offload path). — `83191dd`
- New coverage: plugin-hook discovery/editor/accordion grouping, permission matcher + guided builder, Windows-drive idioms, restore-with-bad-schema. — `d20a2d5`, `704cfc0`, `0f8e417`, `ff9ae5a`

### Fixed

- **Windows drive-letter permission rules now match.** `Read(//C:/c/cl/**)` previously matched nothing (drive-case mismatch in the `//`-anchored resolver). The guided builder now canonicalizes `C:\c\cl` → `//c/c/cl` and flags invalid wildcard drives (`?:` / `*:`). — `704cfc0`, `d20a2d5`
- **Restore no longer crashes on an invalid bundled schema** — a valid-JSON-but-invalid-JSON-Schema entry in a backup is skipped instead of aborting the whole restore. — `0f8e417`
- **Plugin Hooks dark-theme washout** — disabled plugins use a muted *header* instead of a 55%-opacity row, so text, accent, and chips stay full-contrast. — `ff9ae5a`

## [2026.2.612] - 2026-06-12



### Added

- **Permissions tabs** — the single Permissions editor is now a set of sibling tabs contributed by `ClaudeGroupTabCustomizer`: **Overview** (rule-syntax explainer, the "How permission rules work" accordion, the `defaultMode` selector with its contextual banners, and an **Advanced** accordion for `disableBypassPermissionsMode` + `additionalDirectories`), **Common**, **Build**, and **Lists**. Overview is the default landing tab; all tabs bind to one `PermissionsEditorViewModel`, so the header and every activity tab share a single source of truth.
- **Common tab — one-click taxonomy presets** — a curated catalog of common actions grouped per tool (File, Bash, PowerShell, Web, …) and ordered safe-first. Each row carries a `Read` / `Write` / `Network` / `Destructive` chiclet and an Allow / Deny / Ask button, so granting or blocking a typical action is a single click instead of hand-typed syntax. A Windows hint points at the WSL group; an empty state appears once everything is already configured.
- **Build tab — guided rule builder** — pick a tool family, fill the one field that matters (command, path, domain, MCP server, or agent), toggle prefix-match (` *`) or recursive as appropriate, and watch a **live preview of the emitted rule plus a plain-English gloss**. The builder emits only syntax that satisfies `PermissionRule.TryParse`, so it **cannot produce a shape-invalid rule**. A file/folder picker backs path tools; an MCP server dropdown backs `mcp__` rules.
- **Build tab — dry-run permission tester** — a "Test this rule" bridge seeds the tester from the builder's current inputs and expands it; the tester simulates a candidate operation against the **full** Allow/Deny/Ask ruleset and reports the resulting decision, so you can confirm a rule does what you intended before saving.
- **Permission matching engine (SDK)** — new `Permissions/Matching` layer powering the builder, tester, and collision detection: `PermissionRule.TryParse` / `ParsedPermissionRule`, per-tool matchers (`BashCommandSplitter` + `BashRuleMatcher`, `PathRuleMatcher`, `McpRuleMatcher`, `WebFetchRuleMatcher`, `AgentRuleMatcher`, `BareToolMatcher`), and a `PermissionResolver` / `PermissionCollisionDetector` that produce a precedence-aware `PermissionDecision`.
- **Automatic conflict resolution** — adding a rule now prunes an identical lower-precedence rule from another bucket, or is skipped when a higher-precedence bucket already covers it, with the action explained inline (`ConflictResolutionMessage`).
- **Schema-coverage guarantee** — the permissions editor renders **every** key in the permissions schema. Keys with a bespoke control are declared in `CoveredKeys`; any other schema key (e.g. `disableAutoMode`) is auto-surfaced through the generic editor factory and rendered in place on Advanced — fixing a class of silent drop-bugs where an unhandled key round-tripped but was uneditable. Config shapes the typed editors can't represent are preserved as editable raw JSON and recorded once to the log (`UnsupportedShapeSink`) rather than lost.
- **Default Mode safety rails** — the `defaultMode` selector shows each mode's description and a screen-reader-friendly name; selecting `bypassPermissions` raises a red danger banner; `auto` is gated to auto-capable models at User scope (ineligible selections coerce to `default` with an advisory); and "danger"/"bypass" searches deep-link here with a contextual hint clarifying that `--dangerouslySkipPermissions` has no config key but `bypassPermissions` is equivalent.
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

### Changed

- **Lists tab is now one surface among several, not the whole page** — the original free-text Allow / Deny / Ask lists survive on the **Lists** tab for direct/bulk editing, with inline schema validation (red outline + explanation), per-list counts, and worked examples. Rules added from Common or Build land in these same lists.
- Permissions editing was hardened against emitting no-op ("ghost") config changes from UI controls, so the Save preview reflects only real edits.
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
