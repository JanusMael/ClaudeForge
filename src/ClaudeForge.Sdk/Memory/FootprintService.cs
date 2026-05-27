using System.Security;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Tier 2 footprint stats + per-category deletion. Walks the on-disk paths
/// described by each <see cref="FootprintCategory"/> and reports aggregate
/// file count / byte size; <see cref="DeleteAsync"/> wipes the whole
/// category in one call (with confirmation handled by the GUI layer).
/// </summary>
/// <remarks>
/// <para>
/// File-system access goes through <see cref="IBackupFileSystem"/> so tests
/// can inject an in-memory fake with deterministic IO failures. Production
/// callers pass <see cref="RealBackupFileSystem.Instance"/>.
/// </para>
/// <para>
/// All operations are tolerant of missing directories — a category whose
/// directory does not exist returns <c>FileCount = 0, TotalBytes = 0</c>
/// and <see cref="DeleteAsync"/> is a no-op. Per-file IO failures during
/// enumeration are silently skipped (the partially-deleted state is
/// surfaced via the rerun-after-delete <see cref="GetStatsAsync"/> call).
/// </para>
/// </remarks>
public sealed class FootprintService
{
    private readonly IBackupFileSystem _fs;

    public FootprintService(IBackupFileSystem? fs = null)
    {
        _fs = fs ?? RealBackupFileSystem.Instance;
    }

    /// <summary>
    /// Compute stats for every <see cref="FootprintCategory"/> in one pass.
    /// Returns one row per enum value, in declaration order. Cancellable —
    /// honoured between categories AND between files within a category.
    /// </summary>
    public async Task<IReadOnlyList<FootprintCategoryStats>> GetStatsAsync(CancellationToken ct)
    {
        // Run the walks on a worker thread so a slow disk doesn't block the
        // UI thread; per-file enumeration is synchronous via IBackupFileSystem.
        return await Task.Run(() =>
        {
            List<FootprintCategoryStats> rows = new(Enum.GetValues<FootprintCategory>().Length);
            foreach (FootprintCategory category in Enum.GetValues<FootprintCategory>())
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(ComputeStatsFor(category, ct));
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
        await Task.Run(() =>
        {
            foreach (string path in EnumerateCategoryFiles(category))
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
            string dir = Path.Combine(PlatformPaths.ClaudeHome, "projects");
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

        string projectDir = Path.Combine(PlatformPaths.ClaudeHome, "projects", mangledName);
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

    private FootprintCategoryStats ComputeStatsFor(FootprintCategory category, CancellationToken ct)
    {
        string path = ResolveCategoryPath(category);
        int fileCount = 0;
        long totalBytes = 0;

        foreach (string file in EnumerateCategoryFiles(category))
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
            IsInStandardBackup: IsInStandardBackup(category));
    }

    /// <summary>
    /// Enumerate every concrete file for a category. The returned paths are
    /// always absolute. Missing directories yield zero results.
    /// </summary>
    private IEnumerable<string> EnumerateCategoryFiles(FootprintCategory category)
    {
        string home = PlatformPaths.ClaudeHome;

        switch (category)
        {
            case FootprintCategory.SessionTranscripts:
            {
                string dir = Path.Combine(home, "projects");
                return SafeEnumerate(dir, "*.jsonl", recursive: true);
            }

            case FootprintCategory.SessionMetadata:
            {
                // Three sibling directories — concatenate their walks.
                IEnumerable<string> sessions = SafeEnumerate(Path.Combine(home, "sessions"), "*", recursive: true);
                IEnumerable<string> sessionData =
                    SafeEnumerate(Path.Combine(home, "session-data"), "*", recursive: true);
                IEnumerable<string> sessionEnv = SafeEnumerate(Path.Combine(home, "session-env"), "*", recursive: true);
                return sessions.Concat(sessionData).Concat(sessionEnv);
            }

            case FootprintCategory.PromptHistory:
            {
                string file = Path.Combine(home, "history.jsonl");
                return _fs.FileExists(file) ? new[] { file } : Array.Empty<string>();
            }

            case FootprintCategory.BashCommandLog:
            {
                string file = Path.Combine(home, "bash-commands.log");
                return _fs.FileExists(file) ? new[] { file } : Array.Empty<string>();
            }

            case FootprintCategory.CostTrackerLog:
            {
                string file = Path.Combine(home, "cost-tracker.log");
                return _fs.FileExists(file) ? new[] { file } : Array.Empty<string>();
            }

            case FootprintCategory.Todos:
                return SafeEnumerate(Path.Combine(home, "todos"), "*", recursive: true);

            case FootprintCategory.FileEditHistory:
                return SafeEnumerate(Path.Combine(home, "file-history"), "*", recursive: true);

            default:
                return Array.Empty<string>();
        }
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
    /// <see cref="FootprintCategory.SessionMetadata"/>, returns the parent
    /// (<c>~/.claude</c>) so the user can see all three siblings at once.
    /// </summary>
    public static string ResolveCategoryPath(FootprintCategory category)
    {
        string home = PlatformPaths.ClaudeHome;
        return category switch
        {
            FootprintCategory.SessionTranscripts => Path.Combine(home, "projects"),
            FootprintCategory.SessionMetadata => home, // sessions / session-data / session-env all live here
            FootprintCategory.PromptHistory => Path.Combine(home, "history.jsonl"),
            FootprintCategory.BashCommandLog => Path.Combine(home, "bash-commands.log"),
            FootprintCategory.CostTrackerLog => Path.Combine(home, "cost-tracker.log"),
            FootprintCategory.Todos => Path.Combine(home, "todos"),
            FootprintCategory.FileEditHistory => Path.Combine(home, "file-history"),
            var _ => home,
        };
    }

    /// <summary>
    /// Whether the Standard backup mode preserves this category. Mirrors the
    /// decisions in <c>BackupEngine.ShouldSkipHomeSubdir</c> so the Memory
    /// page's "In Standard backup?" badge stays in lockstep with the
    /// Backup/Restore page.
    /// </summary>
    /// <remarks>
    /// Currently <c>BackupEngine</c> only skips <c>projects</c> from
    /// Standard mode (Full mode includes it). Every other footprint category
    /// IS preserved by Standard. If new skip rules are added there, this
    /// switch must be updated in lockstep.
    /// </remarks>
    public static bool IsInStandardBackup(FootprintCategory category)
    {
        return category switch
        {
            FootprintCategory.SessionTranscripts => false, // ~/.claude/projects skipped from Standard.
            var _ => true,
        };
    }
}