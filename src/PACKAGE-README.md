# AgentForge and LayeredEditors

Shared libraries behind [ClaudeForge](https://github.com/JanusMael/ClaudeForge) — an Avalonia
desktop app that edits a coding agent's configuration. They are deliberately **product-neutral**:
every product-shaped answer, from which files to which scopes to which schema, arrives as a
descriptor from the host rather than being built in.

These packages are **private** and are published for those apps and the projects that follow them.
The public API is not stable and carries no compatibility guarantee between versions.

## The two families

| Prefix | What it knows |
|---|---|
| `Bennewitz.Ninja.LayeredEditors.*` | The schema-driven editor library: JSON Schema, property editors, scopes, layered values. Knows about no product at all |
| `Bennewitz.Ninja.AgentForge.*` | Product-neutral agent-configuration machinery: the SDK, settings core, backup/restore, artifact resolution, and the Avalonia shell |

**The layering rule:** nothing product-specific is referenced by `AgentForge.*`, and no two products
reference each other. Every package here is consumable by a host that knows nothing about Claude.

## The packages

| Package | Layer |
|---|---|
| `AgentForge.Abstractions` | BCL-only vocabulary |
| `AgentForge.Core` | Schema, backup, settings, platform |
| `JsonC` | JSONC reader/writer |
| `AgentForge.Artifacts` | Artifact resolution |
| `AgentForge.Sdk` | Client core |
| `AgentForge.Avalonia.Shell` | Nav, search, save, Essentials, Backup page |
| `LayeredEditors.Abstractions` | Editor vocabulary |
| `LayeredEditors.ViewModels` | Editor view-models |
| `LayeredEditors.Avalonia` | Editor controls and templates |
| `LayeredEditors.Avalonia.Services` | Dialogs, share service |
| `LayeredEditors.Avalonia.Diagnostics` | F12 window, binding logger |

All eleven ship at one lockstep version and target `net10.0`.

## Licence

MIT. See [LICENSE](https://github.com/JanusMael/ClaudeForge/blob/main/LICENSE).
