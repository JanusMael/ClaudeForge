using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Structured metadata written as <c>manifest.json</c> inside every backup archive.
/// Source-generated through <see cref="BackupJsonContext"/> so it round-trips correctly
/// under <c>PublishTrimmed=true</c>.
/// </summary>
/// <remarks>
/// The <see cref="SchemaVersion"/> constant is checked on read; unknown future versions
/// are rejected with a clear error rather than silently mis-interpreted. Bump it whenever
/// the on-disk shape changes incompatibly.
/// </remarks>
public sealed class BackupManifest
{
    /// <summary>Current on-disk schema version. Written by every new backup.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("kind")] public string Kind { get; set; } = "backup";
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; }

    /// <summary>"windows", "macos", or "linux" — OS the backup was produced on.</summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    /// <summary>ClaudeForge app version string.</summary>
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    /// <summary><see cref="BackupMode.SettingsOnly"/> or <see cref="BackupMode.Full"/>.</summary>
    [JsonPropertyName("mode")]
    public BackupMode Mode { get; set; } = BackupMode.SettingsOnly;

    /// <summary>Product categories included — typically "ClaudeCode", "ClaudeDesktop".</summary>
    [JsonPropertyName("clients")]
    public List<string> Clients { get; set; } = new();

    /// <summary>Absolute paths of the project roots whose .claude folders were backed up.</summary>
    [JsonPropertyName("projects")]
    public List<string> Projects { get; set; } = new();

    /// <summary>External git worktrees (those whose path is outside their project root).</summary>
    [JsonPropertyName("worktrees")]
    public List<BackupWorktreeEntry> Worktrees { get; set; } = new();

    /// <summary>True when <c>~/.claude/.credentials.json</c> was deliberately included.</summary>
    [JsonPropertyName("includedCredentials")]
    public bool IncludedCredentials { get; set; }

    /// <summary>Uncompressed byte count of everything in the archive (excluding the manifest itself).</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>Number of archive entries written (files, not directories).</summary>
    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }

    /// <summary>
    /// Non-fatal warnings collected during the backup (e.g. "git not on PATH — worktree
    /// discovery skipped"). Displayed in the Restore list as an amber badge.
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Row shape for <see cref="BackupManifest.Worktrees"/>.</summary>
public sealed class BackupWorktreeEntry
{
    [JsonPropertyName("projectRoot")] public string ProjectRoot { get; set; } = string.Empty;
    [JsonPropertyName("worktreePath")] public string WorktreePath { get; set; } = string.Empty;
}