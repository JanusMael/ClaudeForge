namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// File-system seam for the backup engine. Production code uses
/// <see cref="RealBackupFileSystem.Instance"/>; tests inject an in-memory
/// implementation so retention, discovery, and (eventually) the home-walk and
/// restore paths can be exercised without real disk I/O.
/// </summary>
/// <remarks>
/// <para>
/// The interface is deliberately narrow — it covers the call sites the engine
/// touches today. New methods are added as <see cref="BackupEngine"/> internals
/// are progressively threaded through the seam.
/// Avoid bloating the interface with operations that have no current call site.
/// </para>
/// <para>
/// All methods accept absolute paths. Behaviour matches the corresponding
/// <see cref="System.IO.File"/> / <see cref="System.IO.Directory"/> static method
/// (including thrown exception types) so a code path written against the
/// interface can be ported back to direct calls without semantic change.
/// </para>
/// </remarks>
public interface IBackupFileSystem
{
    /// <summary>True when an existing file is present at <paramref name="path"/>.</summary>
    bool FileExists(string path);

    /// <summary>True when an existing directory is present at <paramref name="path"/>.</summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Enumerates files under <paramref name="path"/> matching
    /// <paramref name="searchPattern"/>. Mirrors
    /// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>.
    /// </summary>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>
    /// Returns the UTC last-write timestamp of <paramref name="path"/>.
    /// Mirrors <see cref="File.GetLastWriteTimeUtc(string)"/>; for a missing
    /// file, returns a sentinel timestamp (DateTime.MinValue equivalent in UTC),
    /// matching the underlying API's behaviour.
    /// </summary>
    DateTime GetLastWriteTimeUtc(string path);

    /// <summary>Deletes the file at <paramref name="path"/>. No-op for a missing file.</summary>
    void DeleteFile(string path);
}

/// <summary>
/// Production <see cref="IBackupFileSystem"/> — thin pass-through to the
/// <see cref="System.IO.File"/> / <see cref="System.IO.Directory"/> static APIs.
/// </summary>
public sealed class RealBackupFileSystem : IBackupFileSystem
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly RealBackupFileSystem Instance = new();

    /// <inheritdoc/>
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    /// <inheritdoc/>
    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return Directory.EnumerateFiles(path, searchPattern, searchOption);
    }

    /// <inheritdoc/>
    public DateTime GetLastWriteTimeUtc(string path)
    {
        return File.GetLastWriteTimeUtc(path);
    }

    /// <inheritdoc/>
    public void DeleteFile(string path)
    {
        File.Delete(path);
    }
}