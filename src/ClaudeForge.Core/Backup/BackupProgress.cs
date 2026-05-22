namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Progress payload reported through <see cref="IProgress{T}"/> during a long-running
/// backup, restore, or zip-write operation. Values are safe to render on the UI thread.
/// </summary>
/// <param name="Current">Zero-based index of the entry currently being processed.</param>
/// <param name="Total">Total entry count known at the time of reporting. May be <c>0</c>
/// during early phases (e.g. discovery) — consumers should treat <c>0</c> as "indeterminate".</param>
/// <param name="CurrentItem">Human-readable description of the current entry (file name,
/// short relative path, or a phase label such as "Discovering projects").</param>
/// <param name="BytesDone">Cumulative uncompressed bytes processed so far.</param>
public sealed record BackupProgress(
    int Current,
    int Total,
    string CurrentItem,
    long BytesDone);