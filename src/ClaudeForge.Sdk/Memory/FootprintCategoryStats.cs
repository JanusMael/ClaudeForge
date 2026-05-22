namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Tier 2 footprint stats for a single <see cref="FootprintCategory"/>.
/// Point-in-time snapshot of the on-disk size and file count, plus the
/// "in Standard backup?" badge that mirrors
/// <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.BackupEngine"/>'s skip decisions.
/// </summary>
/// <param name="Category">The category this row reports on.</param>
/// <param name="AbsolutePath">
/// Full path on disk to the directory or file the category covers. The GUI's
/// "Reveal in Explorer" button passes this through unchanged. May point at a
/// directory that does not exist (count = 0, size = 0) — the editor still
/// renders the row so the user knows the category was checked.
/// </param>
/// <param name="FileCount">Number of files matched by the category.</param>
/// <param name="TotalBytes">Aggregate size in bytes; rendered humanised by the View.</param>
/// <param name="IsInStandardBackup">
/// <see langword="true"/> when the Standard backup mode preserves this
/// category, <see langword="false"/> when only Full mode does. Sourced from
/// <c>BackupEngine.ShouldSkipHomeSubdir</c>'s decision tree so the Memory
/// page and the Backup/Restore page stay in lockstep.
/// </param>
public sealed record FootprintCategoryStats(
    FootprintCategory Category,
    string AbsolutePath,
    int FileCount,
    long TotalBytes,
    bool IsInStandardBackup);