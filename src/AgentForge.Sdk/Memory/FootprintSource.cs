namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// One on-disk location a <see cref="FootprintCategory"/> covers: a root key, a path relative to
/// that root, and how to walk it.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A category is not one directory.</b> Claude's <c>SessionMetadata</c> spans three sibling
/// directories (<c>sessions</c>, <c>session-data</c>, <c>session-env</c>) and three other
/// categories are single files, not directories. A definition that modelled "a category = a
/// directory + a glob" would have to special-case both on day one, which is what kept the old
/// closed enum's dispatch hand-written.
/// </para>
/// <para>
/// ⚠ <b><see cref="RootKey"/> is an indirection on purpose.</b> Claude's footprint lives entirely
/// under one root (<c>~/.claude</c>), so the temptation is to store an absolute path. OpenCode's
/// spans four unrelated XDG roots — config, data, state and cache — and the measured items are
/// distributed across all of them. Resolving the root at walk time is also what keeps the
/// <c>PlatformPaths</c> test override working; see <see cref="FootprintService"/>.
/// </para>
/// </remarks>
/// <param name="RootKey">
/// Key into the <see cref="FootprintRoots"/> the service was handed — <c>"home"</c> for Claude,
/// one of <c>"config"</c> / <c>"data"</c> / <c>"state"</c> / <c>"cache"</c> for OpenCode. An
/// unknown key resolves to nothing rather than throwing, so a catalog naming a root the product
/// does not supply degrades to an empty category instead of breaking the whole page.
/// </param>
/// <param name="RelativePath">
/// Path beneath the root, using <c>/</c> as the separator regardless of platform — it is split
/// and recombined with <see cref="Path.Combine(string[])"/>, never concatenated. Empty means the
/// root itself.
/// </param>
/// <param name="Pattern">
/// Search pattern for a directory source (<c>"*"</c>, <c>"*.jsonl"</c>). Ignored when
/// <paramref name="IsFile"/> is <see langword="true"/>.
/// </param>
/// <param name="Recursive">Whether a directory source walks subdirectories.</param>
/// <param name="IsFile">
/// <see langword="true"/> when <paramref name="RelativePath"/> names a single file rather than a
/// directory to walk. The file contributes one entry when it exists and none when it does not.
/// </param>
public readonly record struct FootprintSource(
    string RootKey,
    string RelativePath,
    string Pattern = "*",
    bool Recursive = true,
    bool IsFile = false)
{
    /// <summary>A single file beneath <paramref name="rootKey"/>.</summary>
    public static FootprintSource File(string rootKey, string relativePath) =>
        new(rootKey, relativePath, Pattern: "*", Recursive: false, IsFile: true);

    /// <summary>A directory walk beneath <paramref name="rootKey"/>.</summary>
    public static FootprintSource Directory(
        string rootKey,
        string relativePath,
        string pattern = "*",
        bool recursive = true) =>
        new(rootKey, relativePath, pattern, recursive, IsFile: false);

    /// <summary>
    /// Resolve this source against a root set. Returns <see langword="null"/> when the root key is
    /// not one the product supplies.
    /// </summary>
    public string? Resolve(FootprintRoots roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        string? root = roots.TryResolve(RootKey);
        if (root is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(RelativePath))
        {
            return root;
        }

        // Split on '/' so a catalog entry reads the same on every platform and still lands on the
        // platform's own separator. Never concatenate — see the portability rule in CLAUDE.md.
        string[] segments = RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] parts = new string[segments.Length + 1];
        parts[0] = root;
        segments.CopyTo(parts, 1);
        return Path.Combine(parts);
    }
}
