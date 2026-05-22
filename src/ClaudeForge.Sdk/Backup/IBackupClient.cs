namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Backup / restore surface for an <see cref="IClaudeConfigClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Same threading and cancellation contracts apply as for the parent client
/// (see <see cref="IClaudeConfigClient"/>'s remarks). All async methods take
/// a required <see cref="CancellationToken"/>; progress is reported via the
/// async <see cref="BackupProgressHandler"/> delegate so handlers can do real
/// async work — UI dispatcher marshaling, log writes, MCP streaming —
/// without blocking the backup pipeline.
/// </para>
/// </remarks>
public interface IBackupClient
{
    /// <summary>
    /// Build a backup archive according to <paramref name="request"/>.
    /// Returns a <see cref="BackupArchive"/> on success; throws on cancellation
    /// or unrecoverable I/O failure.
    /// </summary>
    Task<BackupArchive> CreateAsync(
        BackupRequest request,
        BackupProgressHandler? onProgress,
        CancellationToken ct);

    /// <summary>
    /// Returns every <c>backup-*.zip</c> file in <paramref name="directory"/>,
    /// newest first, with parsed manifests. Non-existent directories return an
    /// empty list.
    /// </summary>
    Task<IReadOnlyList<BackupArchive>> ListAsync(
        string directory,
        CancellationToken ct);

    /// <summary>
    /// Restore the contents of <paramref name="archive"/> into the live
    /// configuration tree. Returns a <see cref="RestoreResult"/> describing
    /// success / failure plus any per-file skip / failure detail.
    /// </summary>
    Task<RestoreResult> RestoreAsync(
        BackupArchive archive,
        BackupProgressHandler? onProgress,
        CancellationToken ct);
}

/// <summary>
/// Async progress callback. The producer awaits each invocation, so handlers
/// can do real async work — UI dispatcher marshaling, log writes, MCP
/// streaming — without blocking the backup pipeline.
/// </summary>
/// <remarks>
/// Replaces the synchronous <c>IProgress&lt;BackupProgress&gt;.Report</c>
/// pattern used by <c>ClaudeForge.Core.Backup.BackupEngine</c>.
/// </remarks>
public delegate ValueTask BackupProgressHandler(BackupProgress progress);