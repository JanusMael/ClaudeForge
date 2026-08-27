using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// The installed-plugin tree: one depth-bounded walk yielding agents, commands and skills from
/// every plugin it finds.
/// </summary>
/// <param name="root">The <c>~/.claude/plugins</c> directory. A missing one contributes nothing.</param>
/// <remarks>
/// <para>
/// ⭐⭐ <b>This is the source that decided <see cref="IArtifactSource"/>'s shape.</b> It yields
/// three <see cref="ArtifactKind"/>s and a different <see cref="ArtifactScope"/> per plugin, none
/// of which it knows before walking — so a source cannot be asked for "its" kind or "its" scope.
/// Splitting it into one source per kind to satisfy such properties would walk a plugin tree three
/// times on every page load, which is a real cost paid for a property nothing reads.
/// </para>
/// <para>
/// ⚠ <b>Open-ended discovery, deliberately.</b> Plugins are git repositories, so the artifact
/// directories sit at no fixed depth — the real marketplace layout is
/// <c>plugins/marketplaces/&lt;mkt&gt;/&lt;plugin&gt;/agents/…</c>, and a hand-installed plugin's
/// is shallower. Hence a bounded search for directories NAMED <c>agents</c> / <c>commands</c> and
/// for a <c>SKILL.md</c> at any level, rather than a fixed path per plugin. Do not "simplify" this
/// into <c>plugins/&lt;name&gt;/agents</c>: it would stop finding most installed plugins.
/// </para>
/// <para>
/// ⚠ <b>Fail-soft is PER DIRECTORY, not per walk.</b> One unreadable plugin costs its own entries
/// and nothing else. A single try around the whole traversal would abandon every plugin after the
/// first bad one — and on a machine with a half-synced plugin directory that is the difference
/// between one missing row and an empty page.
/// </para>
/// </remarks>
internal sealed class ClaudePluginArtifactSource(string root) : IArtifactSource
{
    // Plugin trees can be arbitrarily deep (they're git repos), but the artifact dirs
    // (skills/agents/commands) sit near the top. Cap recursion so we never crawl a plugin's
    // dependency tree.
    private const int MaxDepth = 6;

    // Directory names never worth descending into during the plugin walk.
    private static readonly HashSet<string> SkipDirNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".git", ".hg", ".svn", "bin", "obj", "dist", "build" };

    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <inheritdoc/>
    public string Id => "claude-plugins";

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        List<ArtifactRef> found = [];
        Walk(found, root, depth: 0);
        return found;
    }

    /// <summary>
    /// Pick up whatever this directory level offers, then descend.
    /// </summary>
    private void Walk(List<ArtifactRef> found, string directory, int depth)
    {
        string name = Path.GetFileName(directory.TrimEnd(PathSeparators));

        if (name.Equals("agents", StringComparison.OrdinalIgnoreCase))
        {
            AddMarkdown(found, directory, ArtifactKind.Agent);
        }
        else if (name.Equals("commands", StringComparison.OrdinalIgnoreCase))
        {
            AddMarkdown(found, directory, ArtifactKind.Command);
        }

        // Not an `else`: the current layout never puts a SKILL.md inside an agents/ directory, but
        // nothing prevents it, and dropping the file would be a silent loss rather than a visible
        // one. Matches the walk this replaced.
        string manifest = Path.Combine(directory, "SKILL.md");
        if (File.Exists(manifest))
        {
            found.Add(Entry(Path.GetFileName(directory.TrimEnd(PathSeparators)), ArtifactKind.Skill, manifest));
        }

        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (string child in EnumerateDirectoriesSafe(directory))
        {
            string childName = Path.GetFileName(child.TrimEnd(PathSeparators));
            if (SkipDirNames.Contains(childName) || childName.StartsWith('.'))
            {
                continue;
            }

            Walk(found, child, depth + 1);
        }
    }

    private void AddMarkdown(List<ArtifactRef> found, string directory, ArtifactKind kind)
    {
        foreach (string file in EnumerateFilesSafe(directory, "*.md"))
        {
            found.Add(Entry(Path.GetFileNameWithoutExtension(file), kind, file));
        }
    }

    private ArtifactRef Entry(string name, ArtifactKind kind, string location)
    {
        return new ArtifactRef(name, kind, ClaudeScopes.ForPlugin(PluginLabel(location)),
            ArtifactForm.File, location, Id);
    }

    /// <summary>
    /// Derive a plugin's display name from a file path: the path segments under <c>plugins/</c> up
    /// to (not including) the <c>skills</c> / <c>agents</c> / <c>commands</c> directory — e.g.
    /// <c>everything-claude-code</c> or <c>acme-mkt/cool-plugin</c>. Falls back to
    /// <c>"Plugin"</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The <c>marketplaces</c> segment is dropped: Claude Code installs marketplace plugins at
    /// <c>plugins/marketplaces/&lt;mkt&gt;/&lt;plugin&gt;/…</c>, and that level is an installation
    /// layout detail rather than part of the plugin's name.
    /// </remarks>
    private string PluginLabel(string filePath)
    {
        string relative = Path.GetRelativePath(root, filePath);
        List<string> segments = [];
        foreach (string part in relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("skills", StringComparison.OrdinalIgnoreCase)
                || part.Equals("agents", StringComparison.OrdinalIgnoreCase)
                || part.Equals("commands", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (part.Equals("marketplaces", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            segments.Add(part);
        }

        return segments.Count > 0 ? string.Join('/', segments) : "Plugin";
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
