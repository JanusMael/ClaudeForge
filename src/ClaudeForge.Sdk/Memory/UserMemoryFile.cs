namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Tier 1 user-memory file inventory entry. A point-in-time snapshot of one
/// file Claude reads on every session — the user authored it, and the
/// <see cref="UserMemoryService"/> walks the per-category directories to
/// discover instances.
/// </summary>
/// <param name="AbsolutePath">Full path on disk; the GUI's "Reveal in Explorer" button passes this through.</param>
/// <param name="Category">Which user-memory category produced this entry.</param>
/// <param name="DisplayName">Friendly label (file name minus extension, or first line for SKILL.md).</param>
/// <param name="SizeBytes">File size in bytes; rendered humanised by the View.</param>
/// <param name="LastWriteUtc">Last-write timestamp in UTC; surfaced as a sort key.</param>
/// <param name="Subtitle">
/// First non-empty descriptive line of the file, truncated to ~120 chars; used
/// as a per-row subtitle in the Tier 1 list. <see langword="null"/> when the
/// file is empty or the service couldn't read it (e.g. permission denied).
/// </param>
public sealed record UserMemoryFile(
    string AbsolutePath,
    UserMemoryCategory Category,
    string DisplayName,
    long SizeBytes,
    DateTime LastWriteUtc,
    string? Subtitle)
{
    /// <summary>
    /// <see langword="true"/> for a skill (whose file is always <c>SKILL.md</c>).
    /// Deleting a skill removes its whole directory, not just the markdown file.
    /// </summary>
    public bool IsSkill => Category == UserMemoryCategory.Skill;

    /// <summary>
    /// Whether the Memory page may offer a standalone Delete for this file.
    /// <see langword="true"/> for every user-authored category;
    /// <see langword="false"/> for <see cref="UserMemoryCategory.CrossToolMemory"/>
    /// — those files are owned by ANOTHER tool (Codex / Gemini / OpenCode), so a
    /// Claude config tool must not offer to delete them (the governing theme:
    /// never delete things installed / owned by something else).
    /// </summary>
    public bool IsDeletable => Category != UserMemoryCategory.CrossToolMemory;
}