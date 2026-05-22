using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// In-memory <see cref="IBackupFileSystem"/> for unit tests. Stores a flat
/// dictionary of file paths → last-write timestamps and a separate set of
/// directory paths.
/// </summary>
/// <remarks>
/// <para>
/// Path semantics mirror the real file system on the test host:
/// <see cref="StringComparer.OrdinalIgnoreCase"/> on Windows-like hosts and
/// case-sensitive elsewhere is approximated here as case-insensitive on all
/// platforms (matches Windows behaviour, which is the more common dev host).
/// Tests that need case-sensitive semantics should construct paths consistently.
/// </para>
/// <para>
/// Only the slice of <see cref="IBackupFileSystem"/> used by  is
/// honoured.
/// </para>
/// </remarks>
public sealed class InMemoryBackupFileSystem : IBackupFileSystem
{
    private readonly Dictionary<string, DateTime> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _deletedPaths = new();

    /// <summary>Paths that <see cref="DeleteFile"/> was invoked on, in call order.</summary>
    public IReadOnlyList<string> DeletedPaths => _deletedPaths;

    /// <summary>Adds a file with an explicit last-write timestamp. Auto-creates parent directories.</summary>
    public void AddFile(string path, DateTime lastWriteTimeUtc)
    {
        string normalized = NormalizePath(path);
        _files[normalized] = lastWriteTimeUtc;

        // Materialize ancestor directories so DirectoryExists / EnumerateFiles work.
        string? dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _dirs.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    /// <summary>Adds an empty directory.</summary>
    public void AddDirectory(string path)
    {
        _dirs.Add(NormalizePath(path));
    }

    public bool FileExists(string path)
    {
        return _files.ContainsKey(NormalizePath(path));
    }

    public bool DirectoryExists(string path)
    {
        return _dirs.Contains(NormalizePath(path));
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        string dir = NormalizePath(path);
        if (!_dirs.Contains(dir))
        {
            yield break;
        }

        // Convert glob pattern → trivial prefix/suffix match. This is a tiny subset
        // of real glob behaviour, sufficient for the patterns the engine actually
        // uses ("backup-*.zip", "*.json", etc.).
        foreach (string file in _files.Keys)
        {
            string fileDir = Path.GetDirectoryName(file) ?? string.Empty;
            bool inScope = searchOption == SearchOption.AllDirectories
                ? fileDir.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                : string.Equals(fileDir, dir, StringComparison.OrdinalIgnoreCase);

            if (!inScope)
            {
                continue;
            }

            if (!MatchesGlob(Path.GetFileName(file), searchPattern))
            {
                continue;
            }

            yield return file;
        }
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return _files.TryGetValue(NormalizePath(path), out DateTime ts)
            ? ts
            : new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // matches Windows missing-file sentinel
    }

    public void DeleteFile(string path)
    {
        string normalized = NormalizePath(path);
        _files.Remove(normalized);
        _deletedPaths.Add(normalized);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar)
                   .Replace('\\', Path.DirectorySeparatorChar)
                   .TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool MatchesGlob(string filename, string pattern)
    {
        // Tiny glob: leading / trailing '*' wildcards. "backup-*.zip" → "backup-" prefix
        // + ".zip" suffix; "*.json" → ".json" suffix.
        if (pattern == "*")
        {
            return true;
        }

        int star = pattern.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(filename, pattern, StringComparison.OrdinalIgnoreCase);
        }

        string prefix = pattern[..star];
        string suffix = pattern[(star + 1)..];
        return filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
               filename.Length >= prefix.Length + suffix.Length;
    }
}