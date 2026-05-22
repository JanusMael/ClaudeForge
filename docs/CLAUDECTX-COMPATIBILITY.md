# claudectx Compatibility

ClaudeForge's profile system is designed to interoperate with
[**claudectx**](https://github.com/foxj77/claudectx), a Go CLI tool by
@foxj77 that switches Claude Code configuration profiles in seconds.

> **claudectx is OPTIONAL.**  ClaudeForge's Profiles-page "Apply to CLI"
> button is a fully self-contained, in-process operation — pure
> `System.IO` file copies plus `JsonNode` edits via
> `ProfileEngine.ApplyProfileToLiveAsync`.  ClaudeForge never invokes a
> `claudectx` binary (`grep -rnE "Process\.Start.*claudectx" src/` is
> empty by design).  Profile-switching works exactly the same whether
> claudectx is installed or not.  The "compatibility" below is a
> shared **file-format protocol** (the on-disk layout +
> `.claudectx-current` pointer file), not a runtime dependency.
> Install claudectx only when you want a terminal-driven workflow as
> an alternative to the GUI button.

The two tools are complementary, not redundant:

| | claudectx | ClaudeForge |
|---|---|---|
| **What it does** | Activates a profile system-wide | Edits profile contents AND can activate via the Profiles-page "Apply to CLI" button |
| **Surface** | Terminal CLI | Avalonia GUI |
| **Toolbar profile dropdown** | n/a | **Non-destructive** — re-binds the in-memory editor only; the live `~/.claude/` is NOT touched |
| **"Apply to CLI" button (Profiles page)** | n/a | **Destructive** — copies profile files into live `~/.claude/` locations.  Functional equivalent of `claudectx use <name>`.  See § "Apply-to-CLI gotchas" below |
| **`claudectx <name>`** | Destructive switch with auto-backup, rollback, and a backup-retention window | n/a |
| **Best at** | Fast context switching from a terminal, scripting, CI | Browsing schemas, editing structured config (hooks, MCP servers, permissions, marketplaces), AND switching the active profile when you're already in the GUI |

You can use them together: edit a profile in ClaudeForge, then activate
it from a terminal with `claudectx use <name>` — or stay in the GUI and
click **Apply to CLI** on the Profiles page.

---

## Compatibility surfaces

ClaudeForge maintains two explicit interop contracts with claudectx.
Both are tested in
`tests/ClaudeForge.Core.Tests/Profile/ExportImportTests.cs`.

### 1. On-disk profile layout

Both tools store profiles at the same path with the same file shape:

```
~/.claude/profiles/
├── work/
│   ├── settings.json   (required)
│   ├── CLAUDE.md       (optional)
│   └── mcp.json        (optional — a plain {mcpServers} object)
└── personal/
    └── settings.json
```

A profile created by either tool is immediately usable by the other.
ClaudeForge's `ProfileEngine.DiscoverProfiles()` walks this directory
the same way claudectx's `store.List()` does.

### 2. Active-profile pointer

ClaudeForge reads and writes `~/.claude/.claudectx-current` — the
single-line text file claudectx uses to track its CLI-active profile.

| Action | claudectx | ClaudeForge |
|---|---|---|
| Switch via `claudectx work` | Writes `work` to the file | Reads it; UI shows `work` as CLI-active |
| Apply-to-CLI in the GUI | n/a | Writes the new name to the file |
| Delete the active profile in the GUI | n/a | Clears the file |
| `claudectx -` (toggle previous) | Reads `~/.claude/.claudectx-previous` | Not consulted (GUI uses dropdown) |

This means the GUI's "CLI Active" badge stays in sync regardless of
whether you switched profiles via the CLI or the GUI.

### 3. Per-profile JSON export / import

Both tools produce and consume a single-file JSON artefact in the same
shape so a profile can move between machines or teammates as a small
`.json` file.

**Schema** (defined in `src/ClaudeForge.Core/Profile/ExportedProfile.cs`,
mirrors claudectx's `internal/exporter/exporter.go::ExportedProfile`
struct exactly):

```json
{
  "version": "1.0.0",
  "name": "work",
  "settings": { "model": "sonnet", "env": { "FOO": "bar" } },
  "claude_md": "# Work guidelines\nUse pytest.\n",
  "mcp_servers": {
    "context7": { "command": "npx", "args": ["-y", "@upstash/context7-mcp"] }
  },
  "exported_at": "2024-01-15T10:30:00Z"
}
```

Critical compatibility points:

- **Snake-case JSON keys** (`claude_md`, `mcp_servers`, `exported_at`) —
  matches Go struct tags.
- **`version` is `"1.0.0"` exactly.** Both tools refuse imports with any
  other version (no implicit migration).
- **`claude_md` and `mcp_servers` are omitted when empty** — Go's
  `,omitempty` semantics, mirrored in C# via
  `JsonIgnoreCondition.WhenWritingNull`.
- **`settings` is carried through verbatim** as a JSON object — no
  schema reshaping on either side. Future schema additions on either
  tool's settings shape don't break import compatibility.
- **`exported_at` is RFC 3339 UTC** (e.g. `2024-01-15T10:30:00Z`).
- **Imports refuse when the target name already exists** — never
  overwrite. Use a different name (claudectx: `claudectx import file.json
  new-name`; ClaudeForge: rename the file before importing, or rename
  the profile after).

### Cross-tool round-trip

```bash
# ClaudeForge → claudectx
# (in ClaudeForge GUI: Profiles tab → select 'work' → Export → save work.json)
claudectx import work.json work-imported

# claudectx → ClaudeForge
claudectx export work work-from-cli.json
# (in ClaudeForge GUI: Profiles tab → Import → pick work-from-cli.json)
```

Both directions are exercised by the
`Import_AcceptsClaudectxProducedFixture` test, which imports a JSON
fixture shaped exactly the way claudectx emits.

---

## Where compatibility ends — what each tool does that the other doesn't

These are not bugs; they're deliberate division of responsibilities.

### claudectx-only

- **`claudectx use <name>` from a terminal** — destructive switch
  driven from a shell.  ClaudeForge's Profiles-page "Apply to CLI"
  button does the same on-disk operation, but from the GUI; pick the
  surface that fits the workflow.
- **`claudectx -`** — toggle to the previous profile (reads
  `~/.claude/.claudectx-previous`, which ClaudeForge doesn't currently
  consult).
- **`claudectx run <name>`** — one-shot session launch with
  `--settings` passed directly to the `claude` binary (no global
  state change).
- **`~/.claude/backups/backup-<ns>/`** — per-switch automatic backups,
  retained for the last 10.  ClaudeForge's "Apply to CLI" does NOT
  produce one of these per-switch backups; use the Backup/Restore tab
  for snapshots, which captures a broader scope (whole `~/.claude/`
  tree, manifest-versioned ZIP archive) and is manually triggered.

### ClaudeForge-only

- **GUI editing** of every Claude Code config surface — schema-typed,
  with inline validation, scope-aware merge view, search, and a
  right-click "Copy value" affordance on every cell.
- **Claude Desktop profiles** — stored at
  `<DesktopConfigDir>/profiles/<name>/`, surfaced alongside CLI
  profiles in the same dropdown via `UnifiedProfileEntry`. claudectx
  is Claude Code only.
- **Whole-tree backups + cross-platform restore** — manifest-versioned
  ZIP archives that capture more than profiles (memory, agents,
  hooks, the full `~/.claude/` tree).
- **The Memory page (Tier 1 + Tier 2)** — viewer for what Claude has
  read about you across sessions.

---

## Apply-to-CLI gotchas

The Profiles-page "Apply to CLI" button calls
`ProfileEngine.ApplyProfileToLiveAsync(name, autoSync: true, ct)`.  It is
a five-step disk operation, not a database-style transaction.  Each
caveat below is a real footgun worth knowing.

### 1. Auto-sync catches edits made outside the GUI — usually

`autoSync: true` is the default.  Before overwriting the live files,
the engine reads `~/.claude/.claudectx-current` to find the
currently-active profile, and if it's set AND different from the one
being applied, copies the LIVE files BACK into that profile's directory
first.  This preserves edits the user made directly to
`~/.claude/settings.json` outside the GUI (via `claude config`, hand-
editing, etc.).

**Where it doesn't help:**
- If `.claudectx-current` is empty (no previously-active profile), live
  edits since the last sync are simply overwritten.
- If the previously-active profile's directory was deleted between the
  last apply and this one, the auto-sync silently no-ops on that path.
- If the previously-active profile name matches the one being applied
  (same profile re-applied), no sync runs because the engine bails on
  the "different from name" check.

### 2. MCP servers are REPLACED, not merged

Step 5 of the apply writes the profile's `mcp.json` over the
`mcpServers` key of `~/.claude.json` (the file in `$HOME`, not inside
`.claude/`).  This is a full replace — any `mcpServers` entries that
existed in `~/.claude.json` but NOT in the profile's `mcp.json` are
gone.  Other top-level keys of `~/.claude.json` (`customApiKeyResponses`,
`installMethod`, telemetry flags, etc.) are preserved untouched.

If you added an MCP server directly via `claude mcp add <name>` after
the last sync, applying a different profile **wipes** that server
unless step 1 (auto-sync) catches it first — which it only does when a
different prior profile was active.

### 3. `settings.json` overwrite is total

Step 3 atomically replaces `~/.claude/settings.json` with the profile's
copy.  No content merge, no diff, no opt-out.  In-flight edits to the
live file since the last sync are lost (unless auto-sync grabbed them
in step 1).

### 4. `CLAUDE.md` follows the profile's presence or absence

Step 4 copies the profile's `CLAUDE.md` to the live location — but if
the profile DOESN'T have a `CLAUDE.md`, the live one is **deleted**.
The live `CLAUDE.md` always matches the profile exactly.

### 5. Other contents of `~/.claude/` are untouched

`agents/`, `commands/`, `hooks/`, `plans/`, `projects/`, `skills/`,
`cache/`, `output-styles/`, `statsig/`, etc. are NOT part of the
profile and NOT modified by Apply.  Profiles only carry settings +
CLAUDE.md + mcp.json.  If you want those auxiliary contents to follow
the profile too, that's an enhancement we haven't shipped — file an
issue.

### 6. 30-second timeout

The button cancels after 30 seconds with an "Apply timed out" status.
Atomic file writes to `~/.claude/` are normally <100 ms — the timeout
exists as a guard against a stuck antivirus scan, locked file, or
network-mounted home directory hang.  Almost never trips in practice.

### 7. No automatic per-switch backup

claudectx writes `~/.claude/backups/backup-<ns>/` before each switch
(last 10 retained, rollback-able).  ClaudeForge's "Apply to CLI" does
NOT produce one of these.  If you want a pre-switch snapshot, use the
Backup/Restore tab manually before clicking Apply — or run the switch
through `claudectx use <name>` instead.

### 8. Action lands in the rolling log

Every Apply emits `[Profiles.Command] action=ApplyCli name="<profile>"`
at Information level to `~/.claude/cache/logs/app-*.txt`.  If a
"where did my setting go?" question comes up later, the log carries the
audit trail.

---

## When to use which

| Goal | Tool |
|---|---|
| Edit a profile's hooks / MCP servers / permissions visually | ClaudeForge |
| Switch profiles from a terminal in seconds | claudectx (`claudectx use <name>`) |
| Switch profiles while already in the GUI | ClaudeForge ("Apply to CLI" button on the Profiles page) |
| Share one profile with a teammate | Either — JSON export is interchangeable |
| Snapshot your full `~/.claude/` for a complete restore | ClaudeForge backup |
| Roll back to the auto-backup from your last profile switch | claudectx (it auto-backs-up on `use`; the GUI's Apply does not) |
| Run a one-off Claude session with a specific profile without changing global state | `claudectx run <name>` |
| Compare what's set per-scope across User / Local / Project / Managed | ClaudeForge (Effective Settings + scope chiclets) |

---

## Implementation references

| Surface | ClaudeForge file | claudectx file |
|---|---|---|
| Profile discovery | `src/ClaudeForge.Core/Profile/ProfileEngine.cs::DiscoverProfiles` | `internal/store/store.go::List` |
| Active-profile pointer read | `ProfileEngine.cs::ReadCurrentProfileName` | `internal/store/state.go::GetCurrent` |
| Active-profile pointer write | `ProfileEngine.cs::WriteCurrentProfileName` | `internal/store/state.go::SetCurrent` |
| Apply (live-write) | `ProfileEngine.cs::ApplyProfileToLiveAsync` | `cmd/switch.go::SwitchProfile` |
| Sync (live → profile) | `ProfileEngine.cs::SyncFromLiveAsync` | `cmd/sync.go::SyncProfile` |
| Export | `ProfileEngine.cs::ExportProfileAsync` | `internal/exporter/exporter.go::ExportProfile` |
| Import | `ProfileEngine.cs::ImportProfileAsync` | `internal/exporter/exporter.go::ImportProfile` |
| Schema DTO | `src/ClaudeForge.Core/Profile/ExportedProfile.cs` | `internal/exporter/exporter.go::ExportedProfile` |

---

## See also

- `CLAUDE.md` — the "Profiles" section, which describes the
  non-destructive design and the unified CLI / Desktop dropdown.
