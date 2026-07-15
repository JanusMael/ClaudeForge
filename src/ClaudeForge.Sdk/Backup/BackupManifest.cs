using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Structured metadata embedded in every backup archive as
/// <c>manifest.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// SDK-side mirror of <c>ClaudeForge.Core.Backup.BackupManifest</c>. The Sdk keeps
/// its own record surface so no JSON-serialization attributes leak into the public
/// contract; Core's manifest is projected to this type at the SDK boundary.
/// <see cref="Mode"/> is the shared <c>Core.Backup.BackupMode</c> — formerly a
/// duplicated Sdk-local enum, consolidated since both surfaces enumerate the same
/// backup modes.
/// </para>
/// </remarks>
/// <param name="Kind">Discriminator. Always <c>"backup"</c> for backup archives.</param>
/// <param name="SchemaVersion">On-disk schema version of the manifest itself.</param>
/// <param name="CreatedUtc">UTC timestamp the backup was created.</param>
/// <param name="Platform"><c>"windows"</c>, <c>"macos"</c>, or <c>"linux"</c>.</param>
/// <param name="AppVersion">ClaudeForge app version that produced the archive.</param>
/// <param name="Mode">How much of the Claude footprint was included.</param>
/// <param name="Clients">
/// Product categories included — typically <c>"ClaudeCode"</c>, <c>"ClaudeDesktop"</c>.
/// </param>
/// <param name="Projects">Absolute paths of project roots whose <c>.claude</c> folders were backed up.</param>
/// <param name="Worktrees">External git worktrees (paths outside their project root).</param>
/// <param name="IncludedCredentials">
/// <see langword="true"/> when <c>~/.claude/.credentials.json</c> was deliberately included.
/// </param>
/// <param name="SizeBytes">Uncompressed byte count of everything in the archive (excluding the manifest itself).</param>
/// <param name="ItemCount">Number of archive entries written (files, not directories).</param>
/// <param name="Warnings">
/// Non-fatal warnings collected during the backup (e.g. "git not on PATH — worktree discovery skipped").
/// </param>
public sealed record BackupManifest(
    string Kind,
    int SchemaVersion,
    DateTime CreatedUtc,
    string Platform,
    string AppVersion,
    BackupMode Mode,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> Projects,
    IReadOnlyList<BackupWorktreeEntry> Worktrees,
    bool IncludedCredentials,
    long SizeBytes,
    int ItemCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Row shape for <see cref="BackupManifest.Worktrees"/> — one external git
/// worktree captured by the backup.
/// </summary>
/// <param name="ProjectRoot">Project the worktree is associated with.</param>
/// <param name="WorktreePath">Absolute path of the worktree on disk.</param>
public sealed record BackupWorktreeEntry(
    string ProjectRoot,
    string WorktreePath);