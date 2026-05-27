using System.Security;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Scope-aware discovery for the three editable Claude Code artifact kinds —
/// sub-agents, skills, and slash commands — across User, Project, and Plugin
/// scopes.  The backing store for the Agents &amp; Skills editor page (see
/// <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c>, group #2).
///
/// <para>
/// <b>Stat-only enumeration:</b> <see cref="Snapshot"/> reads no file
/// contents — it only walks directories and stats files — so it returns fast
/// even with many plugins.  The front-matter <c>description</c> subtitle is
/// loaded lazily by the UI via <see cref="LoadDescription"/> once a row is
/// shown.  The plugin walk is depth-bounded and skips heavy / irrelevant
/// directories (<c>node_modules</c>, <c>.git</c>, …) so it never descends into
/// a plugin's dependency tree.
/// </para>
///
/// <para>
/// Tolerant of missing directories, unreadable files, and permission errors —
/// enumeration never throws.
/// </para>
/// </summary>
public static class EditableMemoryService
{
    // Bounded head-read size for lazy description extraction.  Front-matter
    // always sits at the very top of the file, so 8 KiB is generous.
    private const int DescriptionScanBytes = 8192;

    // Plugin trees can be arbitrarily deep (they're git repos), but the
    // artifact dirs (skills/agents/commands) sit near the top.  Cap recursion
    // so we never crawl a plugin's dependency tree.
    private const int MaxPluginDepth = 6;

    // Directory names never worth descending into during the plugin walk.
    private static readonly HashSet<string> SkipDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", ".hg", ".svn", "bin", "obj", "dist", "build" };

    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Enumerate every editable artifact across all applicable scopes.
    /// Stat-only; descriptions are loaded later via <see cref="LoadDescription"/>.
    /// </summary>
    public static IReadOnlyList<EditableMemoryEntry> Snapshot(string? projectRoot = null)
    {
        string home = PlatformPaths.ClaudeHome;
        List<EditableMemoryEntry> results = [];

        // User scope — ~/.claude/{agents,commands,skills}/  (writable)
        WalkClaudeDir(results, home, EditableMemoryScope.User, isWritable: true, source: "User");

        // Project scope — <project>/.claude/{agents,commands,skills}/  (writable)
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            WalkClaudeDir(results, Path.Combine(projectRoot, ".claude"),
                EditableMemoryScope.Project, isWritable: true, source: "Project");
        }

        // Plugin scope — ~/.claude/plugins/…  (read-only, depth-bounded)
        WalkPlugins(results, Path.Combine(home, "plugins"));

        return results;
    }

    /// <summary>
    /// Read the full text of an artifact file.  Returns <see langword="null"/>
    /// when the file no longer exists or the read failed.
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

    /// <summary>
    /// Lazily read a file's <c>description</c> front-matter scalar for the
    /// list subtitle (bounded head-read + parse).  Synchronous so the UI can
    /// fan it out across a background pass.  Returns <see langword="null"/> on
    /// read failure, absent key, or front-matter whose closing delimiter falls
    /// outside the scanned head.
    /// </summary>
    public static string? LoadDescription(string absolutePath)
    {
        try
        {
            // Share write + delete: this is a best-effort background subtitle
            // read and must never lock a file the user might be editing or
            // deleting concurrently (in the app, or a test mutating the file
            // right after a refresh).
            using var stream = new FileStream(
                absolutePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            char[] buffer = new char[DescriptionScanBytes];
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return null;
            }

            FrontMatter fm = YamlFrontMatter.Parse(new string(buffer, 0, read));
            return fm.FindScalar("description");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    // ── Scope walks ──────────────────────────────────────────────────────

    private static void WalkClaudeDir(
        List<EditableMemoryEntry> results, string claudeDir,
        EditableMemoryScope scope, bool isWritable, string source)
    {
        AddMarkdownFiles(results, Path.Combine(claudeDir, "agents"),
            UserMemoryCategory.Subagent, scope, isWritable, source);
        AddMarkdownFiles(results, Path.Combine(claudeDir, "commands"),
            UserMemoryCategory.SlashCommand, scope, isWritable, source);
        AddSkills(results, Path.Combine(claudeDir, "skills"), scope, isWritable, source);
    }

    /// <summary>
    /// Depth-bounded walk of the installed-plugins tree.  At each directory we
    /// pick up <c>SKILL.md</c> (a skill), and <c>*.md</c> inside <c>agents/</c>
    /// or <c>commands/</c> dirs, then recurse into child dirs — skipping
    /// dependency / VCS folders and stopping at <see cref="MaxPluginDepth"/>.
    /// </summary>
    private static void WalkPlugins(List<EditableMemoryEntry> results, string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir))
        {
            return;
        }

        WalkPluginDir(results, pluginsDir, pluginsDir, depth: 0);
    }

    private static void WalkPluginDir(
        List<EditableMemoryEntry> results, string pluginsRoot, string dir, int depth)
    {
        string dirName = Path.GetFileName(dir.TrimEnd(PathSeparators));

        // Pick up artifacts at this level.
        if (dirName.Equals("agents", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string md in EnumerateFilesSafe(dir, "*.md"))
            {
                AddEntry(results, md, UserMemoryCategory.Subagent, EditableMemoryScope.Plugin,
                    isWritable: false, source: PluginSource(pluginsRoot, md));
            }
        }
        else if (dirName.Equals("commands", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string md in EnumerateFilesSafe(dir, "*.md"))
            {
                AddEntry(results, md, UserMemoryCategory.SlashCommand, EditableMemoryScope.Plugin,
                    isWritable: false, source: PluginSource(pluginsRoot, md));
            }
        }

        string skillMd = Path.Combine(dir, "SKILL.md");
        if (File.Exists(skillMd))
        {
            AddEntry(results, skillMd, UserMemoryCategory.Skill, EditableMemoryScope.Plugin,
                isWritable: false, source: PluginSource(pluginsRoot, skillMd));
        }

        if (depth >= MaxPluginDepth)
        {
            return;
        }

        foreach (string child in EnumerateDirsSafe(dir))
        {
            string childName = Path.GetFileName(child.TrimEnd(PathSeparators));
            if (SkipDirNames.Contains(childName) || childName.StartsWith('.'))
            {
                continue;
            }

            WalkPluginDir(results, pluginsRoot, child, depth + 1);
        }
    }

    /// <summary>
    /// Derive a plugin source label from a file path: the path segments under
    /// <c>plugins/</c> up to (not including) the <c>skills</c>/<c>agents</c>/
    /// <c>commands</c> dir — e.g. <c>everything-claude-code</c> or
    /// <c>everything-claude-code/some-plugin</c>.  Falls back to <c>"Plugin"</c>.
    /// </summary>
    private static string PluginSource(string pluginsRoot, string filePath)
    {
        string rel = Path.GetRelativePath(pluginsRoot, filePath);
        var prefix = new List<string>();
        foreach (string part in rel.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("skills", StringComparison.OrdinalIgnoreCase)
                || part.Equals("agents", StringComparison.OrdinalIgnoreCase)
                || part.Equals("commands", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Skip the "marketplaces" directory level — it's a Claude Code
            // installation-layout detail, not meaningful as a display name.
            // Real path: plugins/marketplaces/<mkt>/<plugin>/agents/…
            // Displayed as: <mkt>/<plugin>  (not marketplaces/<mkt>/<plugin>)
            if (part.Equals("marketplaces", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prefix.Add(part);
        }

        return prefix.Count > 0 ? string.Join('/', prefix) : "Plugin";
    }

    // ── Directory helpers ────────────────────────────────────────────────

    private static void AddMarkdownFiles(
        List<EditableMemoryEntry> results, string dir,
        UserMemoryCategory category, EditableMemoryScope scope, bool isWritable, string source)
    {
        foreach (string file in EnumerateFilesSafe(dir, "*.md"))
        {
            AddEntry(results, file, category, scope, isWritable, source);
        }
    }

    private static void AddSkills(
        List<EditableMemoryEntry> results, string skillsDir,
        EditableMemoryScope scope, bool isWritable, string source)
    {
        foreach (string dir in EnumerateDirsSafe(skillsDir))
        {
            string skillMd = Path.Combine(dir, "SKILL.md");
            if (File.Exists(skillMd))
            {
                AddEntry(results, skillMd, UserMemoryCategory.Skill, scope, isWritable, source);
            }
        }
    }

    private static void AddEntry(
        List<EditableMemoryEntry> results, string path,
        UserMemoryCategory category, EditableMemoryScope scope, bool isWritable, string source)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                return;
            }

            results.Add(new EditableMemoryEntry(
                AbsolutePath: fi.FullName,
                Category: category,
                Scope: scope,
                DisplayName: DisplayNameFor(fi, category),
                Source: source,
                IsWritable: isWritable,
                SizeBytes: fi.Length,
                LastWriteUtc: fi.LastWriteTimeUtc));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Skip unreadable entries rather than aborting the whole walk.
        }
    }

    /// <summary>
    /// Skills are identified by their parent directory name (the file is
    /// always <c>SKILL.md</c>); agents / commands by the bare file name.
    /// </summary>
    private static string DisplayNameFor(FileInfo fi, UserMemoryCategory category)
    {
        if (category == UserMemoryCategory.Skill)
        {
            string? parent = fi.Directory?.Name;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return parent;
            }
        }

        return Path.GetFileNameWithoutExtension(fi.Name);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string dir, string pattern)
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirsSafe(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}