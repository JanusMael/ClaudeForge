using System.Security;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Deletes a single user-authored memory artifact from disk.  A plain file for
/// agents / commands / hooks / plans / rules / primary + project memory; the
/// whole skill directory for a skill (whose file is always <c>SKILL.md</c> and
/// whose sibling assets — supporting scripts, references — belong to the same
/// skill, so removing only <c>SKILL.md</c> would orphan the rest).
/// </summary>
/// <remarks>
/// <para>
/// Shared by the Memory page (Tier 1 user memory) and the Agents &amp; Skills
/// page.  Each surface gates the call on its own "is this deletable?" policy —
/// never plugin-provided, never another tool's file (Codex / Gemini / OpenCode)
/// — BEFORE invoking this; the deleter itself only performs the filesystem
/// removal.
/// </para>
/// <para>
/// Mirrors <see cref="FootprintService.DeleteAsync"/>'s contract: the IO runs on
/// a worker thread and a failure (permission denied, IO error) propagates so the
/// GUI can surface it; partial state (e.g. some skill-folder files removed before
/// a failure) is NOT rolled back and is reflected by the caller's refresh.
/// </para>
/// </remarks>
public static class MemoryArtifactDeleter
{
    private const string SkillFileName = "SKILL.md";

    /// <summary>
    /// Delete the artifact at <paramref name="absolutePath"/>.  When
    /// <paramref name="isSkill"/> is <see langword="true"/> and the path is a
    /// <c>SKILL.md</c>, the entire containing skill directory is removed
    /// recursively; otherwise only the single file is removed.
    /// </summary>
    /// <param name="absolutePath">Full path to the artifact file (for a skill, its <c>SKILL.md</c>).</param>
    /// <param name="isSkill">
    /// When <see langword="true"/> the artifact is a skill — delete its whole
    /// directory rather than the lone <c>SKILL.md</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The path actually removed — the skill directory for a skill, else the file.</returns>
    public static async Task<string> DeleteAsync(string absolutePath, bool isSkill, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        // Skills are a directory unit: skills/<name>/SKILL.md (+ sibling assets).
        // Delete the parent <name>/ folder — but ONLY when the path really is a
        // SKILL.md, so a mis-tagged caller can never recursively wipe an
        // unexpected directory.
        if (isSkill
            && string.Equals(Path.GetFileName(absolutePath), SkillFileName, StringComparison.OrdinalIgnoreCase))
        {
            string? dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                string skillDir = dir;
                await Task.Run(() => Directory.Delete(skillDir, recursive: true), ct).ConfigureAwait(false);
                return skillDir;
            }
        }

        if (File.Exists(absolutePath))
        {
            await Task.Run(() => File.Delete(absolutePath), ct).ConfigureAwait(false);
        }

        return absolutePath;
    }

    /// <summary>
    /// Compute the <c>(path, file count, total bytes)</c> a confirm dialog should
    /// cite for deleting the artifact at <paramref name="absolutePath"/>.  A plain
    /// file is <c>(itself, 1, <paramref name="knownSize"/>)</c>.  A skill is its
    /// whole directory — walked for the true count + size so the dialog honestly
    /// reflects what the recursive delete removes.  Never throws (falls back to
    /// the known single-file size on a walk failure).
    /// </summary>
    public static (string Path, int FileCount, long Bytes) StatTarget(
        string absolutePath, bool isSkill, long knownSize)
    {
        if (isSkill
            && string.Equals(Path.GetFileName(absolutePath), SkillFileName, StringComparison.OrdinalIgnoreCase))
        {
            string? dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                try
                {
                    List<string> files = Directory
                                         .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                                         .ToList();
                    long sum = 0;
                    foreach (string f in files)
                    {
                        try
                        {
                            sum += new FileInfo(f).Length;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                        {
                            // Skip the unreadable file in the size total; keep walking.
                        }
                    }

                    return (dir, files.Count, sum);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return (dir, 1, knownSize);
                }
            }
        }

        return (absolutePath, 1, knownSize);
    }
}
