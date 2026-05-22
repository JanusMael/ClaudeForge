namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Row shown in the Restore list. Captures what the UI needs to render a single
/// backup without having to re-open the archive.
/// </summary>
/// <remarks>
/// <see cref="IsCorrupt"/> is true when the zip could not be opened or its manifest
/// failed to parse. The UI shows only a "Delete" action for corrupt entries, never Restore.
/// </remarks>
public sealed record BackupEntry
{
    public required string ArchivePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime LastModifiedUtc { get; init; }

    /// <summary>
    /// Null when <see cref="IsCorrupt"/> is true; otherwise the parsed manifest body.
    /// </summary>
    public BackupManifest? Manifest { get; init; }

    /// <summary>True when the archive is readable and the manifest parsed cleanly.</summary>
    public bool IsCorrupt => Manifest is null;

    /// <summary>
    /// True when the backup was taken on a different OS than the current one.
    /// Non-fatal — a warning is shown during Restore.
    /// </summary>
    public bool IsCrossPlatform { get; init; }
}