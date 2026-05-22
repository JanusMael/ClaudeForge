namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Per-project breakdown row for the Tier 2 Session Transcripts category.
/// Each entry rolls up the <c>*.jsonl</c> files under one
/// <c>~/.claude/projects/&lt;mangled&gt;/</c> subdirectory — Claude Code
/// stores every session's transcript in a directory mangled from the
/// project root path (e.g. <c>C--c-cl-myproject</c> for
/// <c>C:\c\cl\myproject</c>).
/// </summary>
/// <param name="MangledName">
/// The raw subdirectory name as it appears on disk. Used as the key for
/// <see cref="FootprintService.DeleteProjectTranscriptsAsync"/> so the
/// caller doesn't have to reconstruct the path.
/// </param>
/// <param name="DisplayName">
/// A best-effort human-friendlier rendering of <see cref="MangledName"/>.
/// Today: the mangled name with leading/trailing dashes trimmed and
/// inner double-dash sequences collapsed to a slash; future versions
/// may decode the full path. Falls back to the raw mangled name when
/// no useful decoding applies.
/// </param>
/// <param name="AbsolutePath">
/// Full path on disk to the project's transcript directory. The GUI's
/// "Reveal in Explorer" button passes this through unchanged.
/// </param>
/// <param name="FileCount">Number of <c>*.jsonl</c> files in the directory.</param>
/// <param name="TotalBytes">
/// Aggregate size in bytes of every transcript in the directory.
/// </param>
/// <param name="LastWriteUtc">
/// Most-recent file-write timestamp across the directory's transcripts.
/// Useful as a sort key — projects the user worked in most recently
/// surface at the top of the breakdown table.
/// </param>
public sealed record ProjectTranscriptStats(
    string MangledName,
    string DisplayName,
    string AbsolutePath,
    int FileCount,
    long TotalBytes,
    DateTime LastWriteUtc);