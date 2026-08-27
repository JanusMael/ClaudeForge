using System.Security;
using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Scope-aware discovery for the three editable Claude Code artifact kinds —
/// sub-agents, skills, and slash commands — across User, Project, and Plugin
/// scopes.  The backing store for the Agents &amp; Skills editor page (see
/// <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c>, group #2).
///
/// <para>
/// <b>Stat-only enumeration:</b> <see cref="Snapshot(string)"/> reads no file
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

    /// <summary>
    /// Enumerate every editable artifact across all applicable scopes.
    /// Stat-only; descriptions are loaded later via <see cref="LoadDescription"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Every entry in a chain, not just the winner.</b> A user <c>reviewer.md</c> and a
    /// plugin's <c>reviewer.md</c> resolve as one artifact with two declarations, and this page
    /// lists both — it is an editor over files on disk, and hiding one because another outranks it
    /// would leave the user unable to edit a file they can see in their own home directory.
    /// Which one Claude actually runs is a separate question the resolver deliberately does not
    /// answer; see <see cref="ClaudeScopes"/>.
    /// </remarks>
    public static IReadOnlyList<EditableMemoryEntry> Snapshot(string? projectRoot = null)
    {
        return Snapshot(ClaudeArtifactPaths.Default, projectRoot);
    }

    /// <summary>
    /// Enumerate every editable artifact for an explicitly supplied set of paths.
    /// </summary>
    /// <param name="paths">Where this profile's Claude files live.</param>
    /// <param name="projectRoot">
    /// The open project's root, or <see langword="null"/> when none is open.
    /// </param>
    /// <remarks>
    /// ⭐ The real entry point; the single-argument overload is a thin wrapper over
    /// <see cref="ClaudeArtifactPaths.Default"/>. See <see cref="UserMemoryService.SnapshotFiles(ClaudeArtifactPaths, string)"/>
    /// for why the static wrapper stays.
    /// </remarks>
    public static IReadOnlyList<EditableMemoryEntry> Snapshot(
        ClaudeArtifactPaths paths, string? projectRoot)
    {
        ArgumentNullException.ThrowIfNull(paths);

        IReadOnlyList<ScopedArtifactSource> sources =
            ClaudeEditableArtifactSources.ForEditor(paths, projectRoot);

        Dictionary<string, EditableMemoryScope> scopes =
            sources.ToDictionary(s => s.Source.Id, s => s.Scope, StringComparer.Ordinal);

        List<EditableMemoryEntry> results = [];
        foreach (ResolvedArtifact artifact in ArtifactResolver.Resolve(sources.Select(s => s.Source)))
        {
            foreach (ArtifactRef entry in artifact.Entries)
            {
                AddEntry(results, entry, scopes[entry.SourceId]);
            }
        }

        return results;
    }

    /// <summary>
    /// Map an artifact kind onto the inventory category the editor pages group by.
    /// </summary>
    /// <remarks>
    /// Total over the three kinds these sources produce. Anything else is a wiring mistake —
    /// throwing names it at the point it happens rather than letting an agent quietly render as a
    /// slash command.
    /// </remarks>
    private static UserMemoryCategory CategoryFor(ArtifactKind kind)
    {
        return kind switch
        {
            ArtifactKind.Agent => UserMemoryCategory.Subagent,
            ArtifactKind.Command => UserMemoryCategory.SlashCommand,
            ArtifactKind.Skill => UserMemoryCategory.Skill,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "The editable surface is agents, commands and skills only."),
        };
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

    // ── Entry shaping ────────────────────────────────────────────────────

    /// <summary>
    /// Stat one declared location into a row.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The source label is the scope's display name</b>, which is <c>"User"</c> /
    /// <c>"Project"</c> for the writable scopes and the plugin's path-derived name otherwise —
    /// so two same-named artifacts from different plugins stay distinguishable at a glance.
    /// </remarks>
    private static void AddEntry(
        List<EditableMemoryEntry> results, ArtifactRef entry, EditableMemoryScope scope)
    {
        UserMemoryCategory category = CategoryFor(entry.Kind);
        try
        {
            var fi = new FileInfo(entry.Location);
            if (!fi.Exists)
            {
                return;
            }

            results.Add(new EditableMemoryEntry(
                AbsolutePath: fi.FullName,
                Category: category,
                Scope: scope,
                DisplayName: DisplayNameFor(fi, category),
                Source: entry.Scope.DisplayName,
                IsWritable: scope != EditableMemoryScope.Plugin,
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

}