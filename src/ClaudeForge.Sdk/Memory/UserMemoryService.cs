using System.Security;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Tier 1 user-memory file inventory. Walks the per-category directories
/// under <see cref="PlatformPaths.ClaudeHome"/> (and the optional project
/// root) and produces an unsorted list of <see cref="UserMemoryFile"/>
/// entries. Tolerant of missing directories, unreadable files, and broken
/// symlinks — never throws on enumeration.
/// </summary>
/// <remarks>
/// <para>
/// This is a Claude-Code-specific inventory: the categories list is the
/// CLI's set, not Claude Desktop's (Desktop has no <c>CLAUDE.md</c>-equivalent
/// — its memory lives in IndexedDB). The <c>ClaudeDesktopClient</c>
/// implementation of <see cref="IClaudeConfigClient.SnapshotUserMemoryFiles"/>
/// returns an empty list.
/// </para>
/// <para>
/// File reads are lazy: <see cref="ReadAsync"/> opens a single file on
/// demand. The enumeration phase only stats the file (size + last-write +
/// optional first-line subtitle) so populating the page is fast even with
/// hundreds of skill / agent files.
/// </para>
/// </remarks>
public static class UserMemoryService
{
    /// <summary>
    /// Enumerate every Tier 1 file. <paramref name="projectRoot"/> may be
    /// <see langword="null"/> when no project is open — in that case
    /// <see cref="UserMemoryCategory.ProjectMemory"/> contributes zero
    /// entries.
    /// </summary>
    public static IReadOnlyList<UserMemoryFile> SnapshotFiles(string? projectRoot = null)
    {
        string home = PlatformPaths.ClaudeHome;
        List<UserMemoryFile> results = [];

        // PrimaryMemory — CLAUDE.md or AGENTS.md (or both, if both exist).
        AddIfExists(results, Path.Combine(home, "CLAUDE.md"), UserMemoryCategory.PrimaryMemory);
        AddIfExists(results, Path.Combine(home, "AGENTS.md"), UserMemoryCategory.PrimaryMemory);

        // ProjectMemory — same pair, scoped to the project root.
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            AddIfExists(results, Path.Combine(projectRoot, "CLAUDE.md"), UserMemoryCategory.ProjectMemory);
            AddIfExists(results, Path.Combine(projectRoot, "AGENTS.md"), UserMemoryCategory.ProjectMemory);
        }

        // Per-category directory walks. Each helper handles the missing-
        // directory case so we never throw during enumeration.
        EnumerateDirectory(results, Path.Combine(home, "agents"), "*.md", UserMemoryCategory.Subagent,
            recursive: false);
        EnumerateDirectory(results, Path.Combine(home, "commands"), "*.md", UserMemoryCategory.SlashCommand,
            recursive: false);
        EnumerateDirectory(results, Path.Combine(home, "hooks"), "*", UserMemoryCategory.Hook, recursive: false);
        EnumerateDirectory(results, Path.Combine(home, "plans"), "*.md", UserMemoryCategory.Plan, recursive: false);
        EnumerateDirectory(results, Path.Combine(home, "rules"), "*.md", UserMemoryCategory.Rule, recursive: true);

        // Skills — one SKILL.md per subdirectory.
        EnumerateSkills(results, Path.Combine(home, "skills"));

        // Cross-tool sibling memory files. We probe a small known set rather
        // than walking siblings open-endedly.
        AddCrossToolMemory(results, home);

        return results;
    }

    /// <summary>
    /// Read the full text of a Tier 1 file. Returns <see langword="null"/>
    /// when the file no longer exists OR when the read failed (permission
    /// denied / IO error / cancellation). The caller's UI should fall back
    /// to "(file no longer available)" rather than crashing.
    /// </summary>
    public static async Task<string?> ReadAsync(string absolutePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void AddIfExists(List<UserMemoryFile> results, string path, UserMemoryCategory category)
    {
        if (!File.Exists(path))
        {
            return;
        }

        UserMemoryFile? entry = TryBuildEntry(path, category);
        if (entry is not null)
        {
            results.Add(entry);
        }
    }

    private static void EnumerateDirectory(
        List<UserMemoryFile> results,
        string dir,
        string searchPattern,
        UserMemoryCategory category,
        bool recursive)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                dir,
                searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string file in files)
        {
            // Skip *.bak sidecars left behind by RestoreEngine or by
            // user-side editors (vim, `sed -i.bak`).  These are not
            // user-authored memory entries — surfacing them on the
            // Memory page would create one phantom Tier 1 row per past
            // restore × memory file, exactly the same compounding noise
            // the backup pipeline already excludes (see
            // `ZipArchiveWriter.EnumerateRecursive` and
            // `BackupEngine.ShouldSkipHomeFile`).
            if (file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            UserMemoryFile? entry = TryBuildEntry(file, category);
            if (entry is not null)
            {
                results.Add(entry);
            }
        }
    }

    private static void EnumerateSkills(List<UserMemoryFile> results, string skillsDir)
    {
        if (!Directory.Exists(skillsDir))
        {
            return;
        }

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(skillsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string dir in dirs)
        {
            string skillMd = Path.Combine(dir, "SKILL.md");
            if (File.Exists(skillMd))
            {
                UserMemoryFile? entry = TryBuildEntry(skillMd, UserMemoryCategory.Skill);
                if (entry is not null)
                {
                    results.Add(entry);
                }
            }
        }
    }

    private static void AddCrossToolMemory(List<UserMemoryFile> results, string home)
    {
        // Probe known sibling agents' memory files. We look INSIDE the user
        // profile root (not inside ~/.claude/) because the sibling tools'
        // homes live there.
        string? profile = Directory.GetParent(home)?.FullName;
        if (profile is null)
        {
            return;
        }

        string[] probes =
        [
            Path.Combine(profile, ".codex", "AGENTS.md"),
            Path.Combine(profile, ".gemini", "GEMINI.md"),
        ];
        foreach (string probe in probes)
        {
            AddIfExists(results, probe, UserMemoryCategory.CrossToolMemory);
        }

        // .opencode tends to be a directory of markdown files; walk it.
        EnumerateDirectory(
            results,
            Path.Combine(profile, ".opencode"),
            "*.md",
            UserMemoryCategory.CrossToolMemory,
            recursive: false);
    }

    private static UserMemoryFile? TryBuildEntry(string path, UserMemoryCategory category)
    {
        try
        {
            FileInfo fi = new(path);
            if (!fi.Exists)
            {
                return null;
            }

            return new UserMemoryFile(
                AbsolutePath: fi.FullName,
                Category: category,
                DisplayName: ResolveDisplayName(fi, category),
                SizeBytes: fi.Length,
                LastWriteUtc: fi.LastWriteTimeUtc,
                Subtitle: ReadFirstNonEmptyLine(fi.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pick the human-friendly display name for an entry.  For most categories
    /// the file's own bare name (without extension) is the right answer —
    /// <c>my-rule.md</c> → <c>my-rule</c>.  Skills are an exception: every
    /// skill's text file is named exactly <c>SKILL.md</c>, with the actual
    /// skill name carried by the parent directory
    /// (<c>~/.claude/skills/&lt;skill-name&gt;/SKILL.md</c>).  Without this
    /// branch, every skill row in the inventory would render as the literal
    /// "SKILL", giving zero per-row identity.
    /// </summary>
    private static string ResolveDisplayName(FileInfo fi, UserMemoryCategory category)
    {
        if (category == UserMemoryCategory.Skill)
        {
            string? parentName = fi.Directory?.Name;
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                return parentName;
            }
        }

        return Path.GetFileNameWithoutExtension(fi.Name);
    }

    /// <summary>
    /// Read up to the first 4 KiB of the file and return the first
    /// descriptive line — meaning a line that conveys actual prose, not a
    /// markdown horizontal rule, JSON / YAML structural punctuation, or a
    /// bare HTML/XML tag. Trimmed and capped at 120 chars. Returns
    /// <see langword="null"/> on read failure or when the file has nothing
    /// useful to surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Smoke testing on surfaced subtitles like <c>---</c> (YAML
    /// front-matter delimiter / markdown HR) and <c>{</c> (JSON object
    /// opener) as the first-line content of legitimate skill / agent files.
    /// This implementation continues past those markers to find the first
    /// line that has descriptive content.
    /// </para>
    /// <para>
    /// YAML front-matter handling: when the file opens with <c>---</c>, we
    /// scan the front-matter block for a <c>description:</c> or <c>name:</c>
    /// field and prefer that as the subtitle. Falls back to the first
    /// post-front-matter descriptive line.
    /// </para>
    /// </remarks>
    private static string? ReadFirstNonEmptyLine(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using StreamReader reader = new(stream);
            char[] buf = new char[4096];
            int read = reader.Read(buf, 0, buf.Length);
            if (read <= 0)
            {
                return null;
            }

            string text = new(buf, 0, read);

            // Three states:
            //   Normal           — looking for the first descriptive line.
            //   MaybeFrontMatter — saw an opening "---" as the first non-empty
            //                      content; speculatively capturing key/value
            //                      pairs but ready to bail to Normal if the
            //                      next non-key line proves this isn't actually
            //                      YAML front-matter (just a markdown HR).
            //   AfterFrontMatter — confirmed YAML front-matter has closed; scan
            //                      body for a descriptive line UNLESS we already
            //                      captured a description / name to return.
            const int Normal = 0;
            const int MaybeFrontMatter = 1;
            const int AfterFrontMatter = 2;
            int state = Normal;
            string? frontMatterDescription = null;
            string? frontMatterName = null;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim('\r', ' ', '\t');
                if (line.Length == 0)
                {
                    continue;
                }

                if (state == Normal)
                {
                    // Opening "---" is ambiguous: YAML front-matter delimiter
                    // OR a markdown horizontal rule.  Defer the decision —
                    // see MaybeFrontMatter below.
                    if (line == "---")
                    {
                        state = MaybeFrontMatter;
                        continue;
                    }

                    if (IsNoiseLine(line))
                    {
                        continue;
                    }

                    string trimmed = line.TrimStart('#').Trim();
                    if (trimmed.Length == 0 || IsNoiseLine(trimmed))
                    {
                        continue;
                    }

                    return Cap(trimmed);
                }

                if (state == MaybeFrontMatter)
                {
                    // Closing front-matter delimiter.
                    if (line == "---" || line == "...")
                    {
                        state = AfterFrontMatter;
                        if (!string.IsNullOrEmpty(frontMatterDescription))
                        {
                            return Cap(frontMatterDescription!);
                        }

                        if (!string.IsNullOrEmpty(frontMatterName))
                        {
                            return Cap(frontMatterName!);
                        }

                        continue;
                    }

                    // Inside the block, look for `key: value` lines; capture
                    // description / name (later occurrences win, matching
                    // typical YAML semantics for duplicate keys).
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string key = line[..colon].Trim().ToLowerInvariant();
                        string val = line[(colon + 1)..].Trim().Trim('"', '\'');
                        if (val.Length > 0)
                        {
                            if (key == "description")
                            {
                                frontMatterDescription = val;
                            }
                            else if (key == "name")
                            {
                                frontMatterName = val;
                            }
                        }

                        continue;
                    }

                    // Non-key-value, non-delimiter line: this WASN'T actually
                    // YAML front-matter — the opening "---" was a markdown
                    // horizontal rule.  Treat THIS line as the first
                    // descriptive content and switch to Normal state.
                    state = Normal;
                    if (IsNoiseLine(line))
                    {
                        continue;
                    }

                    string trimmedAfterHr = line.TrimStart('#').Trim();
                    if (trimmedAfterHr.Length == 0 || IsNoiseLine(trimmedAfterHr))
                    {
                        continue;
                    }

                    return Cap(trimmedAfterHr);
                }

                // AfterFrontMatter: same scan as Normal, but the front-matter
                // had no description/name (otherwise we would have returned).
                if (IsNoiseLine(line))
                {
                    continue;
                }

                string bodyTrimmed = line.TrimStart('#').Trim();
                if (bodyTrimmed.Length == 0 || IsNoiseLine(bodyTrimmed))
                {
                    continue;
                }

                return Cap(bodyTrimmed);
            }

            // EOF inside MaybeFrontMatter without a closing delimiter and
            // without captured fields → genuinely unparseable; return null.
            if (frontMatterDescription is not null)
            {
                return Cap(frontMatterDescription);
            }

            if (frontMatterName is not null)
            {
                return Cap(frontMatterName);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lines that don't make useful subtitles in their own right — markdown
    /// horizontal rules, bare JSON / YAML / HTML structural punctuation,
    /// blank dividers, etc.
    /// </summary>
    private static bool IsNoiseLine(string line)
    {
        // Markdown horizontal rules: --- / *** / ___ (or runs thereof).
        if (line.Length <= 8 && line.All(c => c is '-' or '*' or '_' or ' '))
        {
            return line.Any(c => c is '-' or '*' or '_');
        }

        // Bare structural punctuation a JSON / array / HTML / fenced-code line
        // would start with. Continue past them to find descriptive content.
        if (line is "{" or "}" or "[" or "]" or "{}" or "[]")
        {
            return true;
        }

        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            return true;
        }

        // Bare opening tags like "<html>" / "<!DOCTYPE html>" — single tag
        // and nothing else.
        if (line.StartsWith('<') && line.EndsWith('>')
                                 && !line.Contains(' ') && line.Length < 40)
        {
            return true;
        }

        return false;
    }

    private static string Cap(string s)
    {
        return s.Length > 120 ? s[..120] + "…" : s;
    }
}