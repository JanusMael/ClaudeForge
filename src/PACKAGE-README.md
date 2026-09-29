# AgentForge

Shared libraries behind [ClaudeForge](https://github.com/JanusMael/ClaudeForge) — an Avalonia
desktop app that edits a coding agent's configuration. They are deliberately **product-neutral**:
every product-shaped answer, from which files to which scopes to which schema, arrives as a
descriptor from the host rather than being built in.

These packages are published for ClaudeForge and the apps that follow it. The public API is not
stable and carries no compatibility guarantee between versions.

**The layering rule:** nothing product-specific is referenced by `AgentForge.*`, and no two products
reference each other. Every package here is consumable by a host that knows nothing about Claude.

## The packages

| Package | Layer |
|---|---|
| `AgentForge.Abstractions` | BCL-only vocabulary |
| `AgentForge.Core` | Schema, backup, settings, platform |
| `AgentForge.Artifacts` | Artifact resolution |
| `AgentForge.Sdk` | Client core |
| `AgentForge.Avalonia.Shell` | Nav, search, save, Essentials, Backup page |

All five ship at one lockstep version and target `net10.0`.

They build on three libraries published to nuget.org from their own repositories:
`Bennewitz.Ninja.ScopedEditors.*` (the schema-driven editor library), `Bennewitz.Ninja.AppServices.*`
(its services), and `Bennewitz.Ninja.JsonC` (the comment- and formatting-preserving JSONC reader and
writer).

## Licence

MIT. See [LICENSE](https://github.com/JanusMael/ClaudeForge/blob/main/LICENSE).
