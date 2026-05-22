using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Builds Claude backup / export archives as ordinary <c>.zip</c> files.
/// Shared by <c>BackupEngine</c> (raw-file archives) and the refactored Export
/// feature (effective-config archives) so that all archive output follows the
/// same conventions: forward-slash entry names, atomic writes (no half-files
/// on crash or cancellation), and an indented <c>manifest.json</c> entry.
/// </summary>
/// <remarks>
/// <para>
/// **Atomic writes.** We compose the archive under a sibling <c>.tmp-{timestamp}</c>
/// path first, then rename into place on success. Cancellation or exception
/// deletes the temp file — the destination is left untouched.
/// </para>
/// <para>
/// **Entry naming.** All entry names are normalised to forward slashes, matching
/// the PowerShell reference implementation so archives round-trip cross-platform.
/// </para>
/// </remarks>
public sealed class ZipArchiveWriter : IAsyncDisposable
{
    private readonly string _finalPath;
    private readonly string _tempPath;
    private readonly FileStream _tempStream;
    private readonly ZipArchive _archive;
    private readonly List<PendingEntry> _pending = new();
    private bool _committed;
    private bool _streamsClosed;
    private bool _disposed;

    private ZipArchiveWriter(string finalPath, string tempPath, FileStream stream, ZipArchive archive)
    {
        _finalPath = finalPath;
        _tempPath = tempPath;
        _tempStream = stream;
        _archive = archive;
    }

    /// <summary>
    /// Opens a new archive. The file at <paramref name="finalPath"/> is not created or
    /// modified until <see cref="CommitAsync"/> succeeds. If the writer is disposed
    /// without committing (or cancelled), the final path is left unchanged.
    /// </summary>
    public static ZipArchiveWriter Create(string finalPath)
    {
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            throw new ArgumentException("Destination path must be non-empty.", nameof(finalPath));
        }

        string? dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        string tempPath = finalPath + $".tmp-{stamp}";

        FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: false);
        return new ZipArchiveWriter(finalPath, tempPath, stream, archive);
    }

    /// <summary>
    /// Queue a file-on-disk for inclusion. The file is read only when the archive is
    /// flushed, so callers can enumerate paths cheaply in advance and decide to abort.
    /// </summary>
    public void AddFile(string sourcePath, string entryName)
    {
        ThrowIfDisposed();
        _pending.Add(new PendingEntry(NormaliseEntryName(entryName), sourcePath, null));
    }

    /// <summary>
    /// Recursively queue every file under <paramref name="sourceDirectory"/> under a
    /// common <paramref name="entryPrefix"/> in the archive. Symlinks and junctions
    /// are **not** followed — they are silently skipped and the caller can inspect
    /// <see cref="SkippedSymlinks"/> after writing.
    /// </summary>
    public void AddDirectory(string sourceDirectory, string entryPrefix)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        string prefix = NormaliseEntryName(entryPrefix).TrimEnd('/');

        // DirectoryInfo enumeration lets us inspect the reparse-point attribute
        // without following links.
        DirectoryInfo root = new(sourceDirectory);
        EnumerateRecursive(root, prefix.Length == 0 ? string.Empty : prefix + "/",
            inheritedPatterns: null);
    }

    private void EnumerateRecursive(
        DirectoryInfo dir,
        string relPrefix,
        IReadOnlyList<GitignorePattern>? inheritedPatterns)
    {
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            SkippedSymlinks.Add(dir.FullName);
            return;
        }

        // Merge any .gitignore found in this directory with the inherited pattern list.
        IReadOnlyList<GitignorePattern> localPatterns = GitignoreReader.Read(Path.Combine(dir.FullName, ".gitignore"));
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.MergePatterns(inheritedPatterns, localPatterns);

        foreach (FileInfo file in dir.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                SkippedSymlinks.Add(file.FullName);
                continue;
            }

            // Skip pre-ClaudeForge original snapshots — these are created by
            // ClaudeForge on first save and should not be included in backups so
            // they remain isolated as a separate safety copy.
            if (file.Name.EndsWith(BackupConstants.B4ForgeSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip *.bak sidecars.  These are point-in-time
            // safety copies created by RestoreEngine.RestoreAsync when an
            // existing file would be overwritten (suffix
            // ".pre-restore-{stamp}.bak").  They:
            //   * accumulate one generation per restore × every restored
            //     file, so backups compound in size over time;
            //   * carry no user-authored content (each is a snapshot
            //     captured at restore-moment, redundant with the backup
            //     archive the user restored from);
            //   * also catches editor/IDE-style .bak files (vim, sed -i.bak,
            //     etc.) that occasionally land in ~/.claude.  In every
            //     case the .bak is derivative — not config the backup
            //     needs to preserve.
            // User report 2026-05-13: backup of a profile with many prior
            // restores took ~minutes, mostly spinning on .bak sidecars.
            if (file.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // relPrefix already ends in "/" (or is empty for the root), so the relative
            // path passed to IsIgnored is e.g. "subdir/foo.log" for a file in a subdir.
            string relPath = relPrefix + file.Name;
            if (patterns.Count > 0 &&
                GitignoreReader.IsIgnored(file.Name, relPath, isDirectory: false, patterns))
            {
                continue;
            }

            string entryName = relPath;
            _pending.Add(new PendingEntry(NormaliseEntryName(entryName), file.FullName, null));
        }

        foreach (DirectoryInfo sub in dir.EnumerateDirectories())
        {
            // Skip .git directories — they are version-control internals, not config
            // data, and backing them up causes failures on restore (locked pack files,
            // object database, etc.).
            if (sub.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relDirPath = relPrefix + sub.Name;
            if (patterns.Count > 0 &&
                GitignoreReader.IsIgnored(sub.Name, relDirPath + "/", isDirectory: true, patterns))
            {
                continue;
            }

            EnumerateRecursive(sub, relDirPath + "/", patterns);
        }
    }

    /// <summary>
    /// Queue a string body (UTF-8) for inclusion — used for the manifest and for
    /// in-memory effective-config JSON blobs produced by Export.
    /// </summary>
    public void AddTextEntry(string entryName, string content)
    {
        ThrowIfDisposed();
        _pending.Add(new PendingEntry(NormaliseEntryName(entryName), null, content));
    }

    /// <summary>
    /// Symlinks and reparse points encountered during <see cref="AddDirectory"/>.
    /// The caller (typically <c>BackupEngine</c>) forwards these as non-fatal
    /// warnings on the manifest.
    /// </summary>
    public List<string> SkippedSymlinks { get; } = new();

    /// <summary>
    /// Files that could not be read during <see cref="CommitAsync"/> due to an
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> (e.g. locked
    /// log files held open by a running process). Skipped files are non-fatal; the
    /// caller should forward these as warnings on the manifest.
    /// </summary>
    public List<string> SkippedFiles { get; } = new();

    /// <summary>
    /// Number of entries queued so far (files + in-memory strings). The manifest
    /// writer uses this to record an accurate <c>ItemCount</c> on the archive.
    /// </summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Optional in-flight content transformer for source-file entries (added via
    /// <see cref="AddFile"/> or <see cref="AddDirectory"/>).  When set,
    /// <see cref="CommitAsync"/> calls the delegate with the original source
    /// path and writes the returned string's UTF-8 bytes into the archive
    /// INSTEAD of the file's original bytes.  In-memory string entries
    /// (added via <see cref="AddTextEntry"/>) bypass the transformer
    /// entirely — those are typically the manifest and bundled schemas
    /// which must round-trip verbatim.
    /// <para>
    /// **Routing.** The transformer is called for EVERY source-file entry
    /// regardless of extension — the caller is responsible for deciding
    /// inside the delegate whether a given file should be transformed
    /// (e.g. JSON via <c>JsonRedactor</c>, text via <c>TextRedactor</c>)
    /// or passed through unchanged.  This is a contract change from the
    /// earlier <c>JsonFileTransformer</c> which had a built-in <c>.json</c>
    /// extension filter; the new shape moves routing into the caller so
    /// expanded redaction coverage is callable without modifying
    /// <see cref="ZipArchiveWriter"/>.
    /// </para>
    /// <para>
    /// Used by <c>BackupEngine</c> to plug the redaction pass for
    /// <see cref="BackupMode.Sanitized"/> backups without complicating the
    /// queueing API — the engine queues paths the same way and the redaction
    /// happens transparently at commit time.
    /// </para>
    /// <para>
    /// Delegate contract: takes the absolute source path, returns the content
    /// the archive should contain for that entry.  Implementations MUST NOT
    /// return the original bytes for files they couldn't safely transform —
    /// returning the original content from a transformer whose purpose is
    /// redaction would silently leak secrets.  Callers that want a fallback
    /// should compose their own "transform-or-marker" string inside the
    /// delegate.
    /// </para>
    /// </summary>
    public Func<string, string>? FileTransformer { get; set; }

    /// <summary>
    /// Precomputed transformer outputs, keyed by entry name (forward-slash
    /// normalised).  Populated by <see cref="PrecomputeTransformsAsync"/>;
    /// consumed by <see cref="CommitAsync"/> when present.  If a precomputed
    /// entry exists, the writer emits its UTF-8 bytes and skips the
    /// synchronous transformer call entirely — moving the
    /// CPU+IO-bound redaction work out of the single-threaded
    /// commit critical path.
    /// </summary>
    private Dictionary<string, string>? _precomputedTransforms;

    /// <summary>
    /// Run <see cref="FileTransformer"/> against every queued source-file
    /// entry in PARALLEL and cache the results.  <see cref="CommitAsync"/>
    /// then consumes the cached strings sequentially (the zip writer itself
    /// is single-threaded — Deflate compresses each entry on the commit
    /// thread — but the read+transform stage can fan out across cores).
    /// No-op if <see cref="FileTransformer"/> is not set or no source-file
    /// entries are queued.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// Concurrency cap for the parallel transformer invocations.
    /// <c>-1</c> (default) → <c>Math.Max(2, Environment.ProcessorCount / 2)</c>,
    /// leaving headroom for the dispatcher and other ambient work.  Tests
    /// pin to a specific value for deterministic concurrency assertions.
    /// </param>
    /// <param name="ct">Cancellation token; cancellation aborts pending
    /// transformer runs and propagates as <see cref="OperationCanceledException"/>.</param>
    /// <remarks>
    /// **Failure handling.** A transformer that throws is treated as a
    /// per-file skip — the entry name is added to <see cref="SkippedFiles"/>
    /// and NOT cached, so <see cref="CommitAsync"/> sees a transformer-less
    /// fallback for that one entry (which then itself fails the per-file
    /// open and adds to SkippedFiles a second time, mirroring the
    /// non-precompute path's behaviour for inaccessible files).  Net: the
    /// archive still commits with all healthy entries; failed entries
    /// surface in the manifest's <c>SkippedFiles</c> warning row.
    /// </remarks>
    public async Task PrecomputeTransformsAsync(
        int maxDegreeOfParallelism = -1,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (FileTransformer is null)
        {
            return;
        }

        // Snapshot the source-file entries (skip in-memory string entries
        // and any entries with neither — defensive against future record
        // shape additions).
        List<PendingEntry> sourceEntries = _pending
                                           .Where(e => e.SourcePath is not null)
                                           .ToList();
        if (sourceEntries.Count == 0)
        {
            return;
        }

        int degree = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Math.Max(2, Environment.ProcessorCount / 2);

        ConcurrentDictionary<string, string> transforms = new();
        ConcurrentBag<string> failures = new();

        // Capture the delegate once so the lambda doesn't re-read the
        // property under each iteration (it's nullable and would force
        // a null-check per iteration otherwise).
        Func<string, string>? transformer = FileTransformer!;

        await Parallel.ForEachAsync(
            sourceEntries,
            new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct },
            async (entry, innerCt) =>
            {
                try
                {
                    // Transformer is sync I/O + CPU, run it on the thread pool
                    // so Parallel.ForEachAsync can fan it out properly.
                    string result = await Task.Run(
                        () => transformer(entry.SourcePath!),
                        innerCt).ConfigureAwait(false);
                    transforms[entry.EntryName] = result;
                }
                catch (Exception ex) when (ex is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or OutOfMemoryException)
                {
                    // Per-file failure — record + leave un-precomputed; the
                    // commit path's fallback will then itself fail-and-skip
                    // for this entry, adding to SkippedFiles consistently.
                    _ = ex;
                    failures.Add(entry.SourcePath!);
                }
            }).ConfigureAwait(false);

        // Drain the concurrent failure bag into SkippedFiles on the
        // calling thread (SkippedFiles is a List<string>, not thread-safe).
        foreach (string f in failures)
        {
            SkippedFiles.Add(f);
        }

        // Take the dictionary in one shot — no further mutation expected
        // after this point.
        _precomputedTransforms = new Dictionary<string, string>(transforms);
    }

    /// <summary>
    /// Writes all queued entries, closes the archive, and atomically renames it into
    /// place. Cancellation deletes the temp file and leaves the destination untouched.
    /// Returns the total uncompressed byte count actually written (excluding the
    /// archive's own overhead).
    /// </summary>
    public async Task<long> CommitAsync(
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        long bytesWritten = 0;
        int total = _pending.Count;
        int current = 0;

        foreach (PendingEntry entry in _pending)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.StringContent is not null)
            {
                ZipArchiveEntry zipEntry = _archive.CreateEntry(entry.EntryName, CompressionLevel.Optimal);
                await using Stream es = await zipEntry.OpenAsync(ct);
                byte[] bytes = Encoding.UTF8.GetBytes(entry.StringContent);
                await es.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
                bytesWritten += bytes.Length;
            }
            else if (entry.SourcePath is not null)
            {
                // Sanitized-backup transformer pipeline.
                // Three-tier lookup:
                //
                //   1. _precomputedTransforms (populated by
                //      PrecomputeTransformsAsync — parallel pre-commit phase).
                //      Hits here cost zero extra CPU on the commit thread:
                //      the bytes are already a ready-to-deflate UTF-8 string.
                //
                //   2. FileTransformer synchronous fallback.  Used when the
                //      caller set the hook but didn't precompute (or for
                //      transformer FAILURES during precompute — those entries
                //      land in SkippedFiles and bypass the cache; here we'd
                //      hit the catch + skip path too).
                //
                //   3. Raw byte copy (no transformer set at all — non-Sanitized
                //      backups).
                //
                // Skip / mark on transformer failure so a malformed file in
                // ~/.claude/ doesn't kill the whole backup AND isn't silently
                // emitted verbatim (which would leak the secrets the
                // redactor exists to scrub).
                string? transformed = null;
                if (_precomputedTransforms is not null &&
                    _precomputedTransforms.TryGetValue(entry.EntryName, out string? cached))
                {
                    transformed = cached;
                }
                else if (FileTransformer is not null)
                {
                    try
                    {
                        transformed = FileTransformer(entry.SourcePath);
                    }
                    catch (Exception ex) when (ex is IOException
                                                   or UnauthorizedAccessException
                                                   or JsonException
                                                   or OutOfMemoryException)
                    {
                        // Source file is locked / malformed / inaccessible /
                        // too big — record + skip rather than risk emitting
                        // raw bytes.  OutOfMemoryException added to the
                        // filter so a multi-GB malformed .json doesn't
                        // escape the catch and crash the whole backup.
                        _ = ex;
                        SkippedFiles.Add(entry.SourcePath);
                        current++;
                        progress?.Report(new BackupProgress(current, total, $"(skipped) {entry.EntryName}",
                            bytesWritten));
                        continue;
                    }
                }

                if (transformed is not null)
                {
                    ZipArchiveEntry zipEntry = _archive.CreateEntry(entry.EntryName, CompressionLevel.Optimal);
                    await using Stream es = zipEntry.Open();
                    byte[] bytes = Encoding.UTF8.GetBytes(transformed);
                    await es.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
                    bytesWritten += bytes.Length;
                }
                else
                {
                    // Open the source file with FileShare.ReadWrite so files held open
                    // by other processes (e.g. Chrome-native-host.log locked by Claude
                    // Desktop) are read-shared where the OS permits, or skipped with a
                    // non-fatal warning when the file is exclusively locked.
                    FileStream? fs;
                    try
                    {
                        fs = new FileStream(
                            entry.SourcePath, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete,
                            bufferSize: 81920, useAsync: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // File is locked or inaccessible — skip it and record the warning.
                        SkippedFiles.Add(entry.SourcePath);
                        current++;
                        progress?.Report(new BackupProgress(current, total, $"(skipped) {entry.EntryName}",
                            bytesWritten));
                        continue;
                    }

                    ZipArchiveEntry zipEntry = _archive.CreateEntry(entry.EntryName, CompressionLevel.Optimal);
                    await using (fs)
                    await using (Stream es = zipEntry.Open())
                    {
                        await fs.CopyToAsync(es, ct).ConfigureAwait(false);
                        // Use fs.Position (bytes actually read from this point to EOF)
                        // rather than fs.Length, which reflects the file's declared size.
                        // Position == bytes read because we opened at offset 0.
                        bytesWritten += fs.Position;
                    }
                }
            }

            current++;
            progress?.Report(new BackupProgress(current, total, entry.EntryName, bytesWritten));
        }

        // Close the archive (flush) before renaming.  Set _streamsClosed first so
        // DisposeAsync never attempts a second dispose of these objects even if the
        // File.Move below throws — double-disposing ZipArchive in Create mode tries to
        // write the central directory to a closed stream and throws IOException.
        _streamsClosed = true;
        await _archive.DisposeAsync();
        await _tempStream.DisposeAsync().ConfigureAwait(false);

        // Atomic move into the final location.  File.Move(overwrite:true) is a single
        // OS-level call on Windows (MoveFileEx) and most Unix filesystems — no delete/move
        // gap where both old and new files are absent simultaneously.
        File.Move(_tempPath, _finalPath, overwrite: true);

        _committed = true;
        return bytesWritten;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_streamsClosed)
        {
            // Streams still open: clean close before (possibly) deleting the temp file.
            // Disposal is in the failure/dying-writer path; swallow the documented
            // exceptions so we always reach the temp-file cleanup below.
            try
            {
                await _archive.DisposeAsync();
            }
            catch (Exception ex) when (ex is IOException
                                           or InvalidOperationException
                                           or ObjectDisposedException)
            {
                _ = ex;
            }

            try
            {
                await _tempStream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException
                                           or InvalidOperationException
                                           or ObjectDisposedException)
            {
                _ = ex;
            }
        }

        if (!_committed)
        {
            // Cleanup after failure / cancellation: remove the incomplete temp file.
            // If File.Move threw after the streams were already closed (_streamsClosed=true
            // but _committed=false), the temp file still exists and must be removed.
            try
            {
                if (File.Exists(_tempPath))
                {
                    File.Delete(_tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZipArchiveWriter));
        }

        if (_committed)
        {
            throw new InvalidOperationException("Archive already committed.");
        }
    }

    /// <summary>
    /// Normalises an entry name for the zip. Rejects anything that would escape the
    /// restore root on extraction: absolute paths (leading <c>/</c>, <c>\</c>, or a
    /// drive letter on Windows), <c>..</c> segments, and empty/<c>.</c> segments.
    /// </summary>
    internal static string NormaliseEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Entry name must be non-empty.", nameof(name));
        }

        // Reject absolute paths *before* any trimming so traversal guards cannot be
        // bypassed by a single leading slash.
        if (name[0] is '/' or '\\' || Path.IsPathRooted(name))
        {
            throw new ArgumentException(
                $"Entry name '{name}' is absolute — entries must be relative.",
                nameof(name));
        }

        string normalised = name.Replace('\\', '/');

        foreach (string segment in normalised.Split('/'))
        {
            if (segment is ".." or "." || segment.Length == 0)
            {
                throw new ArgumentException(
                    $"Entry name '{name}' contains an empty or traversal segment and is not allowed.",
                    nameof(name));
            }
        }

        return normalised;
    }

    /// <summary>Convenience: serialise <paramref name="manifest"/> via the source-gen context.</summary>
    public static string SerialiseBackupManifest(BackupManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, BackupJsonContext.Default.BackupManifest);
    }

    /// <summary>Convenience: serialise <paramref name="manifest"/> via the source-gen context.</summary>
    public static string SerialiseExportManifest(ExportManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, BackupJsonContext.Default.ExportManifest);
    }

    private sealed record PendingEntry(string EntryName, string? SourcePath, string? StringContent);
}