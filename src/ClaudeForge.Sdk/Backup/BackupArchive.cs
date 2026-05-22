namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Handle to a backup archive on disk, returned from
/// <see cref="IBackupClient.CreateAsync"/> and
/// <see cref="IBackupClient.ListAsync"/>.
/// </summary>
/// <param name="FilePath">Absolute path of the <c>backup-*.zip</c> file.</param>
/// <param name="CreatedUtc">UTC timestamp the archive was created.</param>
/// <param name="Manifest">Parsed manifest from inside the archive.</param>
public sealed record BackupArchive(
    string FilePath,
    DateTimeOffset CreatedUtc,
    BackupManifest Manifest);