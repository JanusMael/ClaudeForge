# Claude Config MCP server (sample)

A minimal headless consumer of `AgentForge.Sdk` + `ClaudeForge.Sdk.Claude`.

## Why it exists

This project proves two SDK contracts:

1. **The SDK is Avalonia-free.** This project's `.csproj` references *only* the
   two SDK assemblies. If a future change accidentally pulls Avalonia into their
   transitive closure, this build will start failing with package conflicts long
   before the GUI does.
2. **The SDK is consumable from a strictly headless context.** The whole
   `IClaudeConfigClient` surface — the product-neutral `IAgentConfigClient`
   members plus Claude's typed accessors, the generic escape hatch, and
   backup/restore — runs from a console process with no UI thread, no
   dispatcher, and no Core types in any consumer-facing call site.

Which assembly a member comes from is worth noticing while reading this sample:
`GetEffective` / `SetValue` / `SaveAsync` / `Backup` are neutral
(`AgentForge.Sdk`), while `client.Permissions` is Claude-specific
(`ClaudeForge.Sdk.Claude`). A sample for a second agent tool would keep the
former verbatim and replace the latter.

The "MCP server" framing is illustrative: a real MCP server would speak the
[Model Context Protocol](https://modelcontextprotocol.io/) over stdio JSON-RPC
and negotiate capabilities with the host. That wire layer is intentionally
out of scope here — adding it would not exercise more of the SDK.

## Running

From the repo root:

```bash
dotnet run --project samples/ClaudeForge.Samples.McpServer -- <command> [args...]
```

### Commands

| Command                         | What it does                                                       |
|---------------------------------|--------------------------------------------------------------------|
| `get-effective <path>`          | Reads the merged effective value at the dotted path.               |
| `set-value <path> <value>`      | Writes a string value at the path to the User scope.               |
| `save`                          | Validates and persists dirty documents to disk.                    |
| `permissions-add-allow <rule>`  | Adds a typed permission rule via `IPermissionsAccessor`.           |
| `permissions-list-allow`        | Lists current Allow rules from the merged effective view.          |
| `list-backups <directory>`      | Lists `backup-*.zip` files in the directory with parsed manifests. |
| `create-backup <directory>`     | Creates a backup archive in the directory.                         |
| `restore-backup <archive-path>` | Restores from the given archive.                                   |

### Examples

```bash
# Read the current effective model.
dotnet run --project samples/ClaudeForge.Samples.McpServer -- get-effective model

# Set a permission rule and save.
dotnet run --project samples/ClaudeForge.Samples.McpServer -- permissions-add-allow "Bash(git status)"
dotnet run --project samples/ClaudeForge.Samples.McpServer -- save

# Create a backup.
dotnet run --project samples/ClaudeForge.Samples.McpServer -- create-backup /tmp/claude-backups

# List and restore.
dotnet run --project samples/ClaudeForge.Samples.McpServer -- list-backups /tmp/claude-backups
dotnet run --project samples/ClaudeForge.Samples.McpServer -- restore-backup /tmp/claude-backups/backup-20251010-093000.zip
```

## Code map

| File                        | What it shows                                                            |
|-----------------------------|--------------------------------------------------------------------------|
| `Program.cs`                | Single client instance per process; argv-style command dispatch.         |
| `Tools/GetEffectiveTool.cs` | `IAgentConfigClient.GetEffective<T>(path)` for merged reads.            |
| `Tools/SetValueTool.cs`     | `IAgentConfigClient.SetValue<T>(path, value)` — generic escape hatch.   |
| `Tools/SaveTool.cs`         | `SaveAsync(force=false)` + `SchemaValidationException` error path.       |
| `Tools/PermissionsTool.cs`  | Strongly-typed `IPermissionsAccessor` + `PermissionRule.TryParse`.       |
| `Tools/BackupTool.cs`       | `IBackupClient` Create / List / Restore + async `BackupProgressHandler`. |

Each tool is self-contained — copy whichever pattern fits your real consumer.
