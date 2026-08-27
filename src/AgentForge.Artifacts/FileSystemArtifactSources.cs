namespace Bennewitz.Ninja.AgentForge.Artifacts;

/// <summary>
/// What a single-kind, single-scope source stamps onto every entry it produces.
/// </summary>
/// <param name="Id">Stable identifier, recorded on every entry the source produces.</param>
/// <param name="Kind">The kind of artifact the source supplies.</param>
/// <param name="Scope">The precedence layer its entries sit in.</param>
/// <remarks>
/// ⚠ <b>A stamp, not a claim about sources in general.</b> <see cref="IArtifactSource"/> exposes
/// neither kind nor scope, because a source is free to yield several of each — the plugin-tree
/// walk does. This record exists for the ordinary case, where one directory holds one kind of
/// artifact in one scope, and it keeps each implementation's own parameters to the part that
/// actually differs: a root, a pattern, a probe list.
/// </remarks>
public sealed record ArtifactSourceIdentity(string Id, ArtifactKind Kind, ArtifactScope Scope);

/// <summary>
/// One candidate file at a known location, named by the caller.
/// </summary>
/// <param name="Name">
/// The identity resolution groups by — supplied rather than derived, because a probe's caller knows
/// what the file <i>is</i> and the file name alone often does not say
/// (<c>.codex/AGENTS.md</c> is not the same artifact as <c>~/.claude/AGENTS.md</c>).
/// </param>
/// <param name="Path">Absolute path to test.</param>
public sealed record ArtifactProbe(string Name, string Path);

/// <summary>
/// A fixed list of known locations: each one that exists becomes an entry.
/// </summary>
/// <remarks>
/// This is the shape for the files a tool reads from a hardcoded path — <c>CLAUDE.md</c>,
/// <c>settings.json</c>, a sibling tool's memory file. Probing a small known set is deliberate:
/// walking a home directory open-endedly to find them would surface unrelated files and cost far
/// more on a large profile.
/// </remarks>
public sealed class FileProbeArtifactSource(
    ArtifactSourceIdentity identity,
    IReadOnlyList<ArtifactProbe> probes) : IArtifactSource
{
    /// <inheritdoc/>
    public string Id => identity.Id;

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        List<ArtifactRef> found = [];
        foreach (ArtifactProbe probe in probes)
        {
            // File.Exists is total — it reports false for a malformed path, a missing directory
            // and a permission failure alike, and throws for none of them.
            if (File.Exists(probe.Path))
            {
                found.Add(new ArtifactRef(
                    probe.Name, identity.Kind, identity.Scope, ArtifactForm.File, probe.Path, identity.Id));
            }
        }

        return found;
    }
}

/// <summary>
/// Every file under one directory that matches a pattern.
/// </summary>
/// <param name="identity">Id, kind and scope for the entries this source produces.</param>
/// <param name="root">The directory to walk. A missing one contributes nothing.</param>
/// <param name="searchPattern">
/// The <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> pattern, e.g.
/// <c>*.md</c>, or <c>*</c> for a directory whose files have no agreed extension.
/// </param>
/// <param name="recursive">Whether to descend into subdirectories.</param>
/// <param name="stripExtension">
/// Whether the entry name drops the file extension. True for artifact kinds, where
/// <c>code-reviewer.md</c> is the agent <c>code-reviewer</c>; false for configuration files, where
/// the extension is part of what the file <i>is</i> and dropping it turns every entry into a
/// meaningless bare "settings" / "mcp".
/// </param>
/// <remarks>
/// ⚠ <b>Names are relative to <paramref name="root"/>, not bare file names.</b> A recursive walk
/// legitimately meets <c>rules/common/security.md</c> and <c>rules/csharp/security.md</c>, and
/// neither shadows the other — the tool reads both. Grouping them under one name would invent a
/// shadowing relationship that does not exist, so the name carries the whole relative path with
/// <c>/</c> separators (stable across platforms, unlike <see cref="Path.DirectorySeparatorChar"/>).
/// </remarks>
public sealed class DirectoryArtifactSource(
    ArtifactSourceIdentity identity,
    string root,
    string searchPattern,
    bool recursive,
    bool stripExtension) : IArtifactSource
{
    /// <summary>
    /// File-name suffixes this source refuses to surface, compared case-insensitively.
    /// </summary>
    /// <remarks>
    /// Defaults to none on purpose. Which sidecars count as noise is the <i>caller's</i> policy —
    /// <c>.bak</c> files left by an editor or a restore are not authored artifacts in one
    /// directory and may be exactly what a caller is looking for in another, and a default here
    /// would silently apply one product's judgement to every source.
    /// </remarks>
    public IReadOnlyList<string> ExcludedSuffixes { get; init; } = [];

    /// <summary>
    /// Prepended to every entry name, so unrelated artifacts that share a base name stay distinct.
    /// </summary>
    /// <remarks>
    /// Needed when several sources of the same <see cref="ArtifactKind"/> hold artifacts that are
    /// not each other's alternatives — a sibling tool's memory directory beside this tool's own.
    /// Without it two unrelated <c>AGENTS.md</c> files would resolve as one artifact declared
    /// twice, and any UI reading <c>IsShadowed</c> would state something false.
    /// </remarks>
    public string NamePrefix { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string Id => identity.Id;

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            // ⚠ Materialised INSIDE the try, not returned lazily. EnumerateFiles hands back a
            // sequence that walks as the caller iterates, so a `try` around a bare `return` of it
            // catches nothing at all — the throw lands in the caller's foreach. Same lazy-throw
            // trap the resolver's own SafeEnumerate documents.
            List<ArtifactRef> found = [];
            IEnumerable<string> files = Directory.EnumerateFiles(
                root,
                searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach (string file in files)
            {
                if (IsExcluded(file))
                {
                    continue;
                }

                found.Add(new ArtifactRef(
                    NamePrefix + RelativeName(file), identity.Kind, identity.Scope,
                    ArtifactForm.File, file, identity.Id));
            }

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private bool IsExcluded(string path)
    {
        foreach (string suffix in ExcludedSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string RelativeName(string path)
    {
        string relative = Path.GetRelativePath(root, path);
        if (stripExtension)
        {
            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            relative = Path.Combine(directory, Path.GetFileNameWithoutExtension(relative));
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/')
                       .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

/// <summary>
/// A directory of directories, each holding one manifest file: the skill layout.
/// </summary>
/// <param name="identity">Id, kind and scope for the entries this source produces.</param>
/// <param name="root">The directory whose immediate children are candidate skills.</param>
/// <param name="manifestFileName">
/// The file that makes a child directory a skill, e.g. <c>SKILL.md</c>. A child without it is not
/// a skill and contributes nothing.
/// </param>
/// <remarks>
/// ⚠ <b>The identity is the DIRECTORY name, not the file name.</b> Every skill's file is called
/// exactly <c>SKILL.md</c>, so naming entries after the file would make every skill in a root the
/// same artifact — one chain of N entries all shadowing each other, which is false in every case.
/// </remarks>
public sealed class SkillDirectoryArtifactSource(
    ArtifactSourceIdentity identity,
    string root,
    string manifestFileName) : IArtifactSource
{
    /// <inheritdoc/>
    public string Id => identity.Id;

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            // Materialised inside the try for the same reason as DirectoryArtifactSource.
            List<ArtifactRef> found = [];
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string manifest = Path.Combine(directory, manifestFileName);
                if (!File.Exists(manifest))
                {
                    continue;
                }

                found.Add(new ArtifactRef(
                    Path.GetFileName(directory), identity.Kind, identity.Scope,
                    ArtifactForm.File, manifest, identity.Id));
            }

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
