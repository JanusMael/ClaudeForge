using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Security;
using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// Tier 2 footprint stats + per-category deletion. Walks the on-disk locations each
/// <see cref="FootprintCategory"/> declares and reports aggregate file count / byte size;
/// <see cref="DeleteAsync"/> wipes the whole category in one call (with confirmation handled by
/// the GUI layer).
/// </summary>
/// <remarks>
/// <para>
/// <b>The category set is product data.</b> This service used to iterate
/// <c>Enum.GetValues&lt;FootprintCategory&gt;()</c> and hand-dispatch each of Claude's seven
/// members to a <c>~/.claude</c> subpath. It now walks whatever <see cref="FootprintCatalog"/> and
/// <see cref="FootprintRoots"/> it was handed, so a product whose footprint spans four unrelated
/// roots is a table rather than a rewrite.
/// </para>
/// <para>
/// File-system access goes through <see cref="IBackupFileSystem"/> so tests can inject an
/// in-memory fake with deterministic IO failures. Production callers pass
/// <see cref="RealBackupFileSystem.Instance"/>.
/// </para>
/// <para>
/// All operations are tolerant of missing directories — a category whose directory does not exist
/// returns <c>FileCount = 0, TotalBytes = 0</c> and <see cref="DeleteAsync"/> is a no-op. Per-file
/// IO failures during enumeration are silently skipped (the partially-deleted state is surfaced
/// via the rerun-after-delete <see cref="GetStatsAsync"/> call).
/// </para>
/// <para>
/// ⚠ <b>The per-project transcript methods below are still Claude-shaped</b> — the mangled-name
/// decode and the <c>projects/</c> walk are Claude Code's layout, not a general one. They are left
/// as residue rather than forced into the catalog: OpenCode's equivalent is SQLite rows, and a
/// shared abstraction over a directory walk and a database query would be a name, not a
/// mechanism.
/// </para>
/// </remarks>
public sealed class FootprintService
{
    private readonly IBackupFileSystem _fs;
    private readonly ClaudeArtifactPaths? _paths;
    private readonly FootprintCatalog _catalog;
    private readonly Func<FootprintRoots>? _roots;

    /// <param name="fs">File-system seam; defaults to the real one.</param>
    /// <param name="paths">
    /// Where this profile's Claude files live. Defaults to
    /// <see cref="ClaudeArtifactPaths.DefaultFor"/>, resolved per use rather than captured here.
    /// </param>
    /// <param name="catalog">
    /// The product's category set. Defaults to <see cref="FootprintCatalog.Default"/> — Claude's
    /// seven — so every existing call site keeps its behaviour.
    /// </param>
    /// <param name="roots">
    /// Factory for the named roots the catalog is expressed against. Defaults to the single
    /// <c>"home"</c> root taken from <paramref name="paths"/>.
    /// <para>
    /// ⛔ <b>A factory, not an instance.</b> The roots derive from <c>PlatformPaths.UserProfile</c>,
    /// which honours an <c>AsyncLocal</c> test override; capturing them would pin whichever
    /// sandbox was current when the client was first built. Same reason
    /// <see cref="Paths"/> resolves lazily.
    /// </para>
    /// </param>
    public FootprintService(
        IBackupFileSystem? fs = null,
        ClaudeArtifactPaths? paths = null,
        FootprintCatalog? catalog = null,
        Func<FootprintRoots>? roots = null)
    {
        _fs = fs ?? RealBackupFileSystem.Instance;
        _paths = paths;
        _catalog = catalog ?? FootprintCatalog.Default;
        _roots = roots;
    }

    /// <summary>
    /// The paths this instance reads, resolving the default lazily.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>Resolved per use, never captured in the constructor.</b>
    /// <see cref="ClaudeArtifactPaths.DefaultFor"/> reads
    /// <c>PlatformPaths.UserProfile</c>, which honours an <c>AsyncLocal</c> test override — and
    /// this service is cached for the lifetime of an <c>AgentConfigClientCore</c>. Capturing the
    /// default at construction would freeze whichever sandbox was current when the client first
    /// touched it, so a test that sets its override after the client exists would silently read
    /// another test's directory. That reads as flakiness, not as a stale cache.
    /// </para>
    /// <para>
    /// ⚠ <b>The fallback resolves the DEFAULT home, and that is this type's known
    /// Claude-defaulting site rather than an oversight.</b> It is the entry
    /// <c>NeutralLayerDefaultsTests.KnownSites</c> holds, recorded there with its reason; a
    /// caller that has an environment passes <c>paths</c> instead, which is what
    /// <c>ClaudeCodeClient</c> does. Making the fallback itself neutral is the separate refactor
    /// that entry describes, not something to slip in here.
    /// </para>
    /// </remarks>
    private ClaudeArtifactPaths Paths => _paths ?? ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty);

    /// <summary>The named roots, resolved per use for the reason the constructor documents.</summary>
    private FootprintRoots Roots =>
        _roots is not null
            ? _roots()
            : FootprintRoots.Single(FootprintRoots.Home, Paths.ClaudeHome);

    /// <summary>
    /// Compute stats for every category in this service's catalog, in one pass. Returns one row
    /// per category, in catalog order. Cancellable — honoured between categories AND between
    /// files within a category.
    /// </summary>
    public async Task<IReadOnlyList<FootprintCategoryStats>> GetStatsAsync(CancellationToken ct)
    {
        // Run the walks on a worker thread so a slow disk doesn't block the
        // UI thread; per-file enumeration is synchronous via IBackupFileSystem.
        return await Task.Run(() =>
        {
            FootprintRoots roots = Roots;
            IReadOnlyList<FootprintCategory> categories = _catalog.All;
            List<FootprintCategoryStats> rows = new(categories.Count);
            foreach (FootprintCategory category in categories)
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(ComputeStatsFor(category, roots, ct));
            }

            return (IReadOnlyList<FootprintCategoryStats>)rows;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete every file under the named category. Throws on partial failure
    /// so the GUI can surface which file the deletion stopped on.
    /// </summary>
    /// <remarks>
    /// Single-file failures (permission denied, IO error) propagate — the
    /// GUI layer wraps the call in a Destructive-category dialog and shows
    /// the message inline. Successful per-file deletions before the failure
    /// are NOT rolled back; the next <see cref="GetStatsAsync"/> shows the
    /// partial state.
    /// </remarks>
    public async Task DeleteAsync(FootprintCategory category, CancellationToken ct)
    {
        FootprintRoots roots = Roots;
        await Task.Run(() =>
        {
            foreach (string path in EnumerateCategoryFiles(category, roots))
            {
                ct.ThrowIfCancellationRequested();
                _fs.DeleteFile(path);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Per-project breakdown of the Tier 2 Session Transcripts category.
    /// Walks the immediate subdirectories of <c>~/.claude/projects/</c>
    /// (one per managed project) and reports each as its own row. The
    /// caller can use <see cref="DeleteProjectTranscriptsAsync"/> to wipe
    /// a single project's transcripts without touching the others —
    /// finer-grained than <see cref="DeleteAsync"/> which wipes the whole
    /// SessionTranscripts category.
    /// </summary>
    /// <remarks>
    /// Directories whose <c>*.jsonl</c> file count is zero are still
    /// reported (count = 0, bytes = 0) so the caller's UI can decide
    /// whether to suppress empty rows or surface them so the user can
    /// delete the empty husk. Per-file IO failures during the walk are
    /// silently skipped per the rest of the service's contract.
    /// </remarks>
    public async Task<IReadOnlyList<ProjectTranscriptStats>> GetProjectTranscriptStatsAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            List<ProjectTranscriptStats> rows = new();
            string dir = Path.Combine(Paths.ClaudeHome, "projects");
            if (!_fs.DirectoryExists(dir))
            {
                return rows;
            }

            IEnumerable<string> projectDirs;
            try
            {
                // _fs.EnumerateFiles is the only directory-walker the seam
                // exposes; for subdirectory listing we drop to System.IO
                // directly. Tests that need to inject directory-listing
                // failures should populate the sandbox via the same
                // mechanism the rest of FootprintServiceTests uses
                // (PlatformPaths.TestUserProfileOverride + real Directory).
                projectDirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return rows;
            }

            foreach (string projectDir in projectDirs)
            {
                ct.ThrowIfCancellationRequested();

                string mangled = Path.GetFileName(projectDir);
                int fileCount = 0;
                long totalBytes = 0;
                DateTime lastWrite = DateTime.MinValue;

                IEnumerable<string> files;
                try
                {
                    files = _fs.EnumerateFiles(projectDir, "*.jsonl", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        FileInfo fi = new(file);
                        if (!fi.Exists)
                        {
                            continue;
                        }

                        fileCount++;
                        totalBytes += fi.Length;
                        if (fi.LastWriteTimeUtc > lastWrite)
                        {
                            lastWrite = fi.LastWriteTimeUtc;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        // Skip the file but keep walking.
                    }
                }

                rows.Add(new ProjectTranscriptStats(
                    MangledName: mangled,
                    DisplayName: DecodeMangledProjectName(mangled),
                    AbsolutePath: projectDir,
                    FileCount: fileCount,
                    TotalBytes: totalBytes,
                    LastWriteUtc: lastWrite));
            }

            return (IReadOnlyList<ProjectTranscriptStats>)rows;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete every <c>*.jsonl</c> file under the named project's transcript
    /// directory. The directory itself is left in place — Claude Code will
    /// re-create it on next use; deleting the empty directory provides no
    /// material privacy benefit and risks racing the running CLI.
    /// </summary>
    /// <param name="mangledName">
    /// The raw subdirectory name (the <c>MangledName</c> from
    /// <see cref="GetProjectTranscriptStatsAsync"/>). Caller MUST NOT pass
    /// arbitrary user input — callers should populate this from the typed
    /// stats record, never from a freeform text input.
    /// </param>
    /// <param name="ct">Cancellation token; honoured between files.</param>
    /// <remarks>
    /// Throws on the first per-file IO failure for the same reason
    /// <see cref="DeleteAsync"/> does: the GUI surfaces the message
    /// inline and re-runs stats to show the partial state. A
    /// <paramref name="mangledName"/> that contains a path separator,
    /// drive letter, or is otherwise not a flat directory-name segment
    /// is rejected up front to defend against path-traversal even though
    /// the in-app caller path doesn't supply user input.
    /// </remarks>
    public async Task DeleteProjectTranscriptsAsync(string mangledName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mangledName))
        {
            throw new ArgumentException("Mangled project name must not be blank.", nameof(mangledName));
        }

        // Defence in depth: reject anything that smells like a path component.
        // The legitimate per-project directory names are flat segments under
        // ~/.claude/projects/ so a separator / drive-spec / dotted-segment
        // is never expected.
        if (mangledName.Contains('/')
            || mangledName.Contains('\\')
            || mangledName.Contains(':')
            || mangledName == "."
            || mangledName == ".."
            || mangledName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Mangled project name '{mangledName}' contains an unexpected path component.",
                nameof(mangledName));
        }

        string projectDir = Path.Combine(Paths.ClaudeHome, "projects", mangledName);
        if (!_fs.DirectoryExists(projectDir))
        {
            return;
        }

        await Task.Run(() =>
        {
            IEnumerable<string> files;
            try
            {
                files = _fs.EnumerateFiles(projectDir, "*.jsonl", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                _fs.DeleteFile(file);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Best-effort decode of a Claude Code project mangled-path directory
    /// name. Today: trim leading dashes (which encode a leading slash on
    /// macOS / Linux paths) and replace inner dashes with slashes so a
    /// name like <c>-Users-brian-myproject</c> decodes to
    /// <c>/Users/brian/myproject</c>. Falls back to the raw mangled name
    /// when no decoding rule fires (so the caller never gets an empty
    /// string).
    /// </summary>
    /// <remarks>
    /// This is heuristic — Claude Code's mangling rule is internal and
    /// subject to change. A future version may pull the canonical decode
    /// rule from the SDK if it stabilises.
    /// </remarks>
    internal static string DecodeMangledProjectName(string mangled)
    {
        if (string.IsNullOrWhiteSpace(mangled))
        {
            return mangled;
        }

        // Heuristic: a single leading dash typically encodes a leading
        // path separator (POSIX root). We don't try to decode Windows
        // drive letters because the mangled name shape there isn't
        // consistent across CLI versions.
        string decoded = mangled.StartsWith("-", StringComparison.Ordinal)
            ? "/" + mangled[1..].Replace('-', '/')
            : mangled.Replace('-', '/');

        // Collapse any double-slash that came from an original literal "/-/"
        // pattern in the path. Cosmetic.
        while (decoded.Contains("//"))
        {
            decoded = decoded.Replace("//", "/");
        }

        return decoded;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private FootprintCategoryStats ComputeStatsFor(
        FootprintCategory category,
        FootprintRoots roots,
        CancellationToken ct)
    {
        string path = ResolveCategoryPath(roots, category);
        int fileCount = 0;
        long totalBytes = 0;

        foreach (string file in EnumerateCategoryFiles(category, roots))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                FileInfo fi = new(file);
                if (!fi.Exists)
                {
                    continue;
                }

                fileCount++;
                totalBytes += fi.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Skip the file in the count but keep walking.
            }
        }

        return new FootprintCategoryStats(
            Category: category,
            AbsolutePath: path,
            FileCount: fileCount,
            TotalBytes: totalBytes,
            IsInStandardBackup: category.IsInStandardBackup);
    }

    /// <summary>
    /// Enumerate every concrete file for a category, across all of its declared sources. The
    /// returned paths are always absolute. Missing directories and unknown roots yield nothing.
    /// </summary>
    private IEnumerable<string> EnumerateCategoryFiles(FootprintCategory category, FootprintRoots roots)
    {
        IEnumerable<string> all = Array.Empty<string>();

        foreach (FootprintSource source in category.Definition.Sources)
        {
            string? resolved = source.Resolve(roots);
            if (resolved is null)
            {
                continue;
            }

            all = source.IsFile
                ? all.Concat(_fs.FileExists(resolved) ? new[] { resolved } : Array.Empty<string>())
                : all.Concat(SafeEnumerate(resolved, source.Pattern, source.Recursive));
        }

        return all;
    }

    private IEnumerable<string> SafeEnumerate(string dir, string searchPattern, bool recursive)
    {
        if (!_fs.DirectoryExists(dir))
        {
            return Array.Empty<string>();
        }

        try
        {
            return _fs.EnumerateFiles(
                dir,
                searchPattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Resolve the canonical anchor path for the category — used as the
    /// "click-to-copy" / "Reveal in Explorer" destination on the row.
    /// For multi-directory categories like
    /// <see cref="FootprintCategory.SessionMetadata"/>, this is the parent
    /// (<c>~/.claude</c>) so the user can see all three siblings at once.
    /// </summary>
    public static string ResolveCategoryPath(ClaudeEnvironment env, FootprintCategory category)
    {
        return ResolveCategoryPath(ClaudeArtifactPaths.DefaultFor(env), category);
    }

    /// <summary>
    /// The same resolution against an explicitly supplied set of Claude paths.
    /// </summary>
    /// <remarks>
    /// ⚠ Convenience for Claude call sites: wraps the single <c>"home"</c> root and delegates. A
    /// product with more than one root calls the <see cref="FootprintRoots"/> overload.
    /// </remarks>
    public static string ResolveCategoryPath(ClaudeArtifactPaths paths, FootprintCategory category)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return ResolveCategoryPath(FootprintRoots.Single(FootprintRoots.Home, paths.ClaudeHome), category);
    }

    /// <summary>
    /// The anchor path for a category against an arbitrary root set.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The sub-paths are no longer here.</b> They used to be a hand-written <c>switch</c> in
    /// this file, on the reasoning that they are what a category MEANS. That is still true — they
    /// just live on <see cref="FootprintCategoryDefinition"/> now, where a second product can
    /// supply its own without editing this assembly.
    /// <para>
    /// Falls back to the category's first root when the anchor names a root the product does not
    /// supply, and to the empty string when it supplies none — a reveal button with no target is
    /// better than a throw inside the stats walk.
    /// </para>
    /// </remarks>
    public static string ResolveCategoryPath(FootprintRoots roots, FootprintCategory category)
    {
        ArgumentNullException.ThrowIfNull(roots);

        FootprintCategoryDefinition definition = category.Definition;
        string? anchor = definition.Anchor.Resolve(roots);
        if (anchor is not null)
        {
            return anchor;
        }

        foreach (FootprintSource source in definition.Sources)
        {
            if (source.Resolve(roots) is { } fallback)
            {
                return fallback;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Whether the Standard backup mode preserves this category. Mirrors the
    /// product's backup skip rules so the Memory page's "In Standard backup?"
    /// badge stays in lockstep with the Backup/Restore page.
    /// </summary>
    /// <remarks>
    /// ⛔ Now read from <see cref="FootprintCategoryDefinition.IsInStandardBackup"/> rather than a
    /// <c>switch</c> here. For Claude that still means: <c>~/.claude/projects</c> is the one
    /// subdirectory Standard skips, and every other category is preserved. If new skip rules are
    /// added to <c>BackupEngine.ShouldSkipHomeSubdir</c>, the catalog must change in lockstep.
    /// </remarks>
    public static bool IsInStandardBackup(FootprintCategory category) => category.IsInStandardBackup;
}
