using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Restore-side counterpart to <see cref="BackupEngine"/>. Encapsulates
/// the zip-archive extraction, zip-slip / zip-bomb defences, manifest-driven
/// project + worktree restoration, and post-extraction schema validation
/// against the bundled schemas.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of <see cref="BackupEngine"/>
/// so the BackupEngine god class can shrink and the create / restore
/// halves can evolve independently. Public consumers continue to call
/// <see cref="BackupEngine.RestoreAsync"/>, which delegates here.
/// </para>
/// <para>
/// All members <c>internal static</c>. The class holds no state — every
/// restore is a pure function of (entry, progress, ct).
/// </para>
/// </remarks>
internal static class RestoreEngine
{
    /// <summary>
    /// Maximum total bytes the extracted archive is allowed to write to
    /// disk. Defends against zip-bomb attacks where an archive's compressed
    /// size is small but its declared (or actual) extracted size is huge.
    /// 4 GiB — well above the largest legitimate Claude config archive
    /// while still bounded.
    /// </summary>
    internal const long MaxExtractedBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Extracts <paramref name="entry"/> into the real Claude paths. Existing files
    /// are moved aside with a <c>.pre-restore-{stamp}.bak</c> suffix before being
    /// overwritten. Returns a result summary.
    /// </summary>
    internal static async Task<RestoreResult> RestoreAsync(
        BackupEntry entry,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsCorrupt || entry.Manifest == null)
        {
            return new RestoreResult(false, "Backup is corrupt or missing a manifest.", 0);
        }

        // Sanitized backups are sharing-only artefacts.  Every
        // *.json file inside has had secret-bearing values rewritten to the
        // literal string "[redacted]" — restoring would overwrite the user's
        // working config with that placeholder, breaking authenticated MCP
        // servers, OAuth-backed Anthropic access, and any custom env-var
        // tokens.  Refuse here, surface a clear message, and let the GUI
        // disable the Restore button via BackupRowViewModel.IsRestorable so
        // the user never even sees the click reach this guard in practice.
        if (entry.Manifest.Mode == BackupMode.Sanitized)
        {
            return new RestoreResult(
                Succeeded: false,
                Message: "This backup is sanitized — secret-bearing values were replaced with \"[redacted]\" " +
                         "before archival.  Sanitized backups are for SHARING (support, community, bug reports) " +
                         "and cannot be restored without corrupting your working config.  Create a non-sanitized " +
                         "backup if you need a recoverable copy.",
                ItemsRestored: 0);
        }

        // Extract to a temp directory first. This lets us validate entries, perform
        // zip-slip defence, and bail safely before touching real files.
        // A short random suffix prevents two rapid sequential restores from colliding
        // on the same temp path (ms-precision timestamp alone is insufficient).
        string tempRoot = Path.Combine(Path.GetTempPath(),
            $"ClaudeRestore-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetRandomFileName()[..4]}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using FileStream fs = new(entry.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using ZipArchive archive = new(fs, ZipArchiveMode.Read);

            // Count file entries up-front for per-entry progress reporting.
            List<ZipArchiveEntry> fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            int total = fileEntries.Count;
            int current = 0;
            long totalExtracted = 0;

            foreach (ZipArchiveEntry zipEntry in fileEntries)
            {
                ct.ThrowIfCancellationRequested();

                string? destPath = ResolveSafeExtractPath(tempRoot, zipEntry.FullName);
                if (destPath is null)
                {
                    continue; // slip-guard rejected
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await using Stream inStream = await zipEntry.OpenAsync(ct);
                await using FileStream outStream = new(
                    destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await inStream.CopyToAsync(outStream, ct).ConfigureAwait(false);

                // Zip-bomb defence: count *actual* bytes written (not the declared
                // zipEntry.Length, which an attacker can set to 0 to bypass the check).
                totalExtracted += outStream.Position;
                if (totalExtracted > MaxExtractedBytes)
                {
                    return new RestoreResult(false,
                        $"Backup exceeds the {MaxExtractedBytes / (1024L * 1024 * 1024)} GB extraction limit.",
                        0);
                }

                current++;
                progress?.Report(new BackupProgress(current, total, zipEntry.FullName, totalExtracted));
            }

            // M3 (2026-05-14): tamper-detection.  Sanitized archives are
            // refused at the top of this method by reading manifest.Mode,
            // but the manifest is just JSON inside the zip — an attacker
            // (or a curious user with a hex editor) can flip
            // "mode": "Sanitized" to "SettingsOnly" and bypass the
            // mode-based guard.  Defence-in-depth: if ANY extracted *.json
            // contains the literal [redacted] marker as a value, the
            // archive was sanitized regardless of what the manifest
            // claims.  Refuse the restore with a tamper message so the
            // user's working config isn't overwritten with placeholder
            // strings.
            if (ContainsRedactedMarker(tempRoot))
            {
                return new RestoreResult(
                    Succeeded: false,
                    Message: "This archive contains \"[redacted]\" placeholder values but " +
                             "is not labelled as Sanitized.  The manifest may have been " +
                             "tampered with.  Refusing to apply — restoring would " +
                             "overwrite your working config with literal \"[redacted]\" " +
                             "strings.  If this archive was generated as Sanitized, the " +
                             "manifest is incorrect; create a fresh non-sanitized backup " +
                             "for restore purposes.",
                    ItemsRestored: 0);
            }

            // Post-extraction validation: validate restored configs against the schemas
            // bundled in the archive (i.e. the schema version that was current when the
            // backup was made), not the schemas installed today.  This catches cases where
            // the backup contained invalid configs BEFORE it was made.  Validation is
            // non-blocking — warnings are logged by the caller; the restore proceeds.
            IReadOnlyList<string> validationWarnings = await ValidateExtractedConfigsAsync(tempRoot, ct).ConfigureAwait(false);

            // Signal start of the apply phase.
            // applySections counts: claude.json, claude-dir, desktop config, desktop
            // profiles dir, desktop-current pointer, projects dir, worktrees dir.
            // We use a rolling applyStep counter to give the progress bar visible
            // movement during the apply phase (extraction is done; bar would stall).
            const int applySections = 7;
            int applyStep = 0;
            progress?.Report(new BackupProgress(0, applySections, "Applying restore…", totalExtracted));

            // Place files into real paths, with .bak sidecars.
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int restored = 0;
            List<string> skipped = new();
            List<string> fileFailures = new();

            {
                (int r, string? f) = RestoreSection(Path.Combine(tempRoot, "ClaudeCode", "claude.json"),
                    PlatformPaths.ClaudeJsonPath, stamp);
                restored += r;
                if (f != null)
                {
                    fileFailures.Add(f);
                }
            }
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring claude.json…", totalExtracted));

            {
                (int r, List<string> errs) = RestoreDirectory(Path.Combine(tempRoot, "ClaudeCode", "claude-dir"),
                    PlatformPaths.ClaudeHome, stamp);
                restored += r;
                fileFailures.AddRange(errs);
            }
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring ~/.claude/…", totalExtracted));

            {
                (int r, string? f) = RestoreSection(Path.Combine(tempRoot, "ClaudeDesktop", "claude_desktop_config.json"),
                    PlatformPaths.DesktopConfigPath, stamp);
                restored += r;
                if (f != null)
                {
                    fileFailures.Add(f);
                }
            }
            progress?.Report(
                new BackupProgress(++applyStep, applySections, "Restoring Desktop config…", totalExtracted));

            {
                (int r, List<string> errs) = RestoreDirectory(Path.Combine(tempRoot, "ClaudeDesktop", "profiles"),
                    PlatformPaths.DesktopProfilesDirectory, stamp);
                restored += r;
                fileFailures.AddRange(errs);
            }
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring Desktop profiles…",
                totalExtracted));

            {
                (int r, string? f) = RestoreSection(Path.Combine(tempRoot, "ClaudeDesktop", ".desktop-current"),
                    PlatformPaths.DesktopCurrentProfileFilePath, stamp);
                restored += r;
                if (f != null)
                {
                    fileFailures.Add(f);
                }
            }
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring Desktop active profile…",
                totalExtracted));

            // Per-project restore: look at the manifest to know where each project lives.
            restored += RestoreProjects(tempRoot, entry.Manifest, stamp, skipped, fileFailures);
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring projects…", totalExtracted));

            restored += RestoreWorktrees(tempRoot, stamp, skipped, fileFailures);
            progress?.Report(new BackupProgress(++applyStep, applySections, "Restoring worktrees…", totalExtracted));

            progress?.Report(new BackupProgress(restored, restored,
                "Restore complete", 0));

            string message = $"Restored {restored} item(s). Existing files were moved aside as .pre-restore-{stamp}.bak.";
            if (skipped.Count > 0)
            {
                message += $" Skipped {skipped.Count} project/worktree(s) whose paths are not present on this machine: "
                           + string.Join(", ", skipped.Take(5))
                           + (skipped.Count > 5 ? $" … (+{skipped.Count - 5} more)" : "");
            }

            return new RestoreResult(
                Succeeded: true,
                Message: message,
                ItemsRestored: restored,
                ValidationWarnings: validationWarnings.Count > 0 ? validationWarnings : null,
                FileFailures: fileFailures.Count > 0 ? fileFailures : null);
        }
        catch (OperationCanceledException)
        {
            return new RestoreResult(false, "Restore cancelled.", 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new RestoreResult(false, $"Restore failed: {ex.Message}", 0);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a stuck temp file means the next restore writes
                // to a fresh temp dir; nothing in the live state is affected.
                _ = ex;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="candidate"/> is an absolute path that
    /// lies under the current user's profile directory. The restore engine uses this
    /// to reject manifest-provided paths like <c>C:\Windows\System32</c> or
    /// <c>/etc</c> — an attacker who crafts a zip can put any path in the manifest,
    /// so we cannot trust paths that fall outside the user's own files.
    /// </summary>
    internal static bool IsUnderUserProfile(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        // Reject UNC paths (\\server\share\...) — they are network locations outside
        // the user's local profile and could silently redirect writes to a remote host.
        if (candidate.StartsWith(@"\\", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or PathTooLongException
                                       or NotSupportedException
                                       or SecurityException)
        {
            // Path is malformed for the host platform — reject conservatively.
            _ = ex;
            return false;
        }

        if (!Path.IsPathRooted(full))
        {
            return false;
        }

        // Re-check UNC after Path.GetFullPath in case a relative path resolved to one.
        if (full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        string home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                          .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(home))
        {
            return false;
        }

        return full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(full, home, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zip-slip defence: resolve <paramref name="entryFullName"/> under
    /// <paramref name="baseDir"/> and ensure the final absolute path stays inside.
    /// Returns <c>null</c> when the entry would escape.
    /// </summary>
    /// <remarks>
    /// Normalises **both** slash kinds before combining so a crafted entry like
    /// <c>..\\escape</c> (backslash) cannot bypass the guard on platforms where only
    /// the forward slash is treated as a separator.
    /// </remarks>
    internal static string? ResolveSafeExtractPath(string baseDir, string entryFullName)
    {
        // Explicit reject: absolute entry paths and rooted Windows drives must never
        // pass through Path.Combine (which on Windows silently discards baseDir when
        // the second argument is rooted).
        if (string.IsNullOrEmpty(entryFullName))
        {
            return null;
        }

        if (entryFullName[0] is '/' or '\\' || Path.IsPathRooted(entryFullName))
        {
            return null;
        }

        // Reject entry names containing ':' — on Windows a colon after the filename
        // (e.g. "file.txt:evil") creates an Alternate Data Stream, which could silently
        // attach attacker-controlled bytes to a real file. Safe on other platforms too.
        if (entryFullName.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        // Normalise every separator to the OS form *before* combining so nothing like
        // "..\\sneaky" slips past the final containment check.
        string normalised = entryFullName
                            .Replace('\\', Path.DirectorySeparatorChar)
                            .Replace('/', Path.DirectorySeparatorChar);

        string combined = Path.Combine(baseDir, normalised);
        string full = Path.GetFullPath(combined);
        string rootFull = Path.GetFullPath(baseDir)
                              .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        char separator = Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull + separator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    /// <summary>
    /// Restores a single file from <paramref name="srcFile"/> to <paramref name="destFile"/>,
    /// creating a <c>.pre-restore-{stamp}.bak</c> sidecar if the destination already exists.
    /// Returns <c>(1, null)</c> on success, <c>(0, errorMessage)</c> if the file is locked
    /// or inaccessible — the caller accumulates errors rather than aborting the restore.
    /// </summary>
    internal static (int Restored, string? Failure) RestoreSection(string srcFile, string destFile, string stamp)
    {
        if (!File.Exists(srcFile))
        {
            return (0, null);
        }

        string? destDir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        try
        {
            if (File.Exists(destFile))
            {
                // H5 (2026-05-14): cap accumulated sidecars per file
                // BEFORE writing the new one.  Without this, every
                // restore × every restored file would compound until
                // the user runs --cleanup-restore-sidecars manually.
                EvictOldSidecarsIfNeeded(destFile);
                File.Copy(destFile, $"{destFile}.pre-restore-{stamp}.bak", overwrite: true);
            }

            File.Copy(srcFile, destFile, overwrite: true);
            return (1, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, $"{Path.GetFileName(destFile)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Cap on the number of <c>.pre-restore-{stamp}.bak</c> sidecars
    /// kept per live file.  When a new restore would exceed the cap,
    /// <see cref="EvictOldSidecarsIfNeeded"/> deletes the oldest
    /// sidecars first so exactly <see cref="MaxSidecarsPerFile"/>
    /// survive after the new one is written.
    /// </summary>
    /// <remarks>
    /// Pre-fix, every restore appended a new
    /// sidecar with no cap; a heavily-restored profile reported
    /// 99 307 sidecars consuming 3.3 GB.  The
    /// <c>--cleanup-restore-sidecars</c> CLI handles already-
    /// accumulated sidecars; this cap prevents further accumulation
    /// at write time.  3 is a balance — 1 leaves no roll-back depth,
    /// 5+ adds no meaningful safety.
    /// </remarks>
    internal const int MaxSidecarsPerFile = 3;

    /// <summary>
    /// Prune accumulated <c>.pre-restore-{stamp}.bak</c> sidecars for
    /// <paramref name="liveFile"/> down to
    /// <see cref="MaxSidecarsPerFile"/> minus 1 so a new sidecar can
    /// be written without exceeding the cap.  Editor-style
    /// <c>.bak</c> files (vim, sed -i.bak, hand-rolled) are NOT
    /// touched — only sidecars matching
    /// <see cref="RestoreSidecarCleanup.LooksLikeRestoreSidecar"/>'s
    /// strict regex.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Best-effort: per-file delete failures (locked / read-only)
    /// fall back to <c>File.SetAttributes(Normal)</c> + retry once
    /// (mirroring the read-only retry in
    /// <see cref="RestoreSidecarCleanup"/> for Git pack-object
    /// sidecars that inherit 0444 from their source).  Final
    /// failure is logged at <c>Debug</c> and swallowed — the
    /// restore itself MUST NOT fail because cap eviction couldn't
    /// prune one stale sidecar.
    /// </para>
    /// <para>
    /// Sort order: ordinal-ascending by full path.  The sidecar
    /// stamp is <c>YYYYMMDD-HHMMSS</c> which is lexically equivalent
    /// to chronological order, so the oldest sidecar sorts first
    /// without parsing the timestamp.
    /// </para>
    /// </remarks>
    internal static void EvictOldSidecarsIfNeeded(string liveFile)
    {
        string? dir = Path.GetDirectoryName(liveFile);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return;
        }

        string baseName = Path.GetFileName(liveFile);
        if (string.IsNullOrEmpty(baseName))
        {
            return;
        }

        // Enumerate existing sidecars for THIS live file specifically
        // (file-name match prefix), then narrow to the strict
        // pre-restore-stamp pattern so editor-style .bak files are
        // never touched.
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(dir, $"{baseName}.pre-restore-*.bak");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return;
        }

        string[] sidecars = candidates
                            .Where(p => RestoreSidecarCleanup.LooksLikeRestoreSidecar(Path.GetFileName(p)))
                            .ToArray();
        if (sidecars.Length < MaxSidecarsPerFile)
        {
            return;
        }

        Array.Sort(sidecars, StringComparer.Ordinal);

        // Keep (MaxSidecarsPerFile - 1) most recent so the new sidecar
        // about to be written brings the post-write count to exactly
        // MaxSidecarsPerFile.
        int toDelete = sidecars.Length - (MaxSidecarsPerFile - 1);
        for (int i = 0; i < toDelete; i++)
        {
            TryDeleteSidecar(sidecars[i]);
        }
    }

    private static void TryDeleteSidecar(string path)
    {
        try
        {
            File.Delete(path);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }

        // Read-only retry — matches RestoreSidecarCleanup's recovery
        // path for Git pack-object sidecars (0444 attrs inherited
        // from source).
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort only — log + swallow.  Restore continues.
            _ = ex;
        }
    }

    /// <summary>
    /// Copies every file under <paramref name="srcDir"/> into <paramref name="destDir"/>,
    /// creating <c>.pre-restore-{stamp}.bak</c> sidecars for pre-existing files.
    /// Per-file failures (e.g. locked files) are collected rather than aborting the whole
    /// directory — the caller aggregates them for post-restore reporting.
    /// </summary>
    internal static (int Restored, List<string> Failures) RestoreDirectory(string srcDir, string destDir, string stamp)
    {
        List<string> failures = new();
        if (!Directory.Exists(srcDir))
        {
            return (0, failures);
        }

        Directory.CreateDirectory(destDir);

        // Pre-compute the canonical destination root for the per-file containment
        // check below. ResolveSafeExtractPath defends the *extraction* boundary, but
        // RestoreDirectory copies from the temp extract dir to live paths and the
        // per-file `Path.GetRelativePath` + `Path.Combine` chain has no equivalent
        // guard. A path that was safe at extract time but escapes via Unicode
        // normalisation tricks or a relative segment that survived ResolveSafeExtractPath
        // would otherwise land outside destDir. Defense-in-depth: re-verify each file.
        string destDirFull = Path.GetFullPath(destDir)
                                 .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(srcDir, file);
            string dest = Path.Combine(destDir, rel);

            // Reject any per-file destination that does not stay rooted under destDirFull.
            string destFull;
            try
            {
                destFull = Path.GetFullPath(dest);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                failures.Add($"{rel}: invalid destination path");
                continue;
            }

            if (!destFull.StartsWith(destDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(destFull, destDirFull, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{rel}: refused (resolved outside destination)");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                if (File.Exists(dest))
                {
                    // H5 (2026-05-14): cap accumulated sidecars per
                    // file BEFORE writing the new one — same rule as
                    // RestoreSection.
                    EvictOldSidecarsIfNeeded(dest);
                    File.Copy(dest, $"{dest}.pre-restore-{stamp}.bak", overwrite: true);
                }

                File.Copy(file, dest, overwrite: true);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{rel}: {ex.Message}");
            }
        }

        return (count, failures);
    }

    internal static int RestoreProjects(string tempRoot, BackupManifest manifest, string stamp,
                                        List<string> skipped, List<string> fileFailures)
    {
        string projectsDir = Path.Combine(tempRoot, "ClaudeCode", "projects");
        if (!Directory.Exists(projectsDir))
        {
            return 0;
        }

        int count = 0;
        foreach (string projBackupDir in Directory.EnumerateDirectories(projectsDir))
        {
            string name = Path.GetFileName(projBackupDir);
            string? livePath = manifest.Projects.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar)),
                    name, StringComparison.OrdinalIgnoreCase));

            if (livePath == null || !Directory.Exists(livePath))
            {
                skipped.Add(name + " (path missing)");
                continue;
            }

            // Defence: the path comes from the archive's manifest (untrusted when an
            // attacker crafts a zip), so refuse to write outside the user's home.
            if (!IsUnderUserProfile(livePath))
            {
                skipped.Add(name + " (path outside user profile)");
                continue;
            }

            (int c, List<string> failures) = RestoreDirectory(projBackupDir, livePath, stamp);
            count += c;
            fileFailures.AddRange(failures);
        }

        return count;
    }

    internal static int RestoreWorktrees(string tempRoot, string stamp,
                                         List<string> skipped, List<string> fileFailures)
    {
        string wtDir = Path.Combine(tempRoot, "ClaudeCode", "worktrees");
        if (!Directory.Exists(wtDir))
        {
            return 0;
        }

        int count = 0;
        foreach (string wtBackupDir in Directory.EnumerateDirectories(wtDir))
        {
            string metaPath = Path.Combine(wtBackupDir, ".worktree-meta.json");
            string wtName = Path.GetFileName(wtBackupDir);
            if (!File.Exists(metaPath))
            {
                skipped.Add(wtName + " (no worktree metadata)");
                continue;
            }

            try
            {
                BackupWorktreeEntry? meta = JsonSerializer.Deserialize(
                    File.ReadAllText(metaPath), BackupJsonContext.Default.BackupWorktreeEntry);
                if (meta == null || !Directory.Exists(meta.WorktreePath))
                {
                    skipped.Add(wtName + " (worktree path missing)");
                    continue;
                }

                // Same defence as projects: only write to worktree paths under the user's
                // home directory so a crafted manifest cannot direct writes to arbitrary
                // locations (e.g. C:\Windows\System32 or /etc).
                if (!IsUnderUserProfile(meta.WorktreePath))
                {
                    skipped.Add(wtName + " (worktree path outside user profile)");
                    continue;
                }

                (int c, List<string> failures) = RestoreDirectory(wtBackupDir, meta.WorktreePath, stamp);
                count += c;
                fileFailures.AddRange(failures);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                skipped.Add(wtName + " (metadata read failed)");
            }
        }

        return count;
    }

    /// <summary>
    /// M3 tamper-detection probe (2026-05-14): returns <c>true</c> if
    /// ANY extracted <c>.json</c> file under <paramref name="tempRoot"/>
    /// contains the literal <c>"[redacted]"</c> marker as a value.
    /// Sanitized archives are normally refused at the manifest-mode
    /// guard, but the manifest is editable; this content-side scan is
    /// defence-in-depth against an archive whose manifest was flipped
    /// from <c>Sanitized</c> to <c>SettingsOnly</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Budget.** Returns on the first match — we don't need a full
    /// inventory, just the existence signal.  Per-file: parse with
    /// <c>JsonNode.Parse</c>, walk leaves, check string-typed leaves
    /// against the marker literal.  Bail-on-match avoids the worst
    /// case of touching every file in a large archive when a single
    /// match would suffice.
    /// </para>
    /// <para>
    /// **Catch-all on failure.** Per-file parse / IO failures are
    /// swallowed (the file may not be JSON despite the extension; or
    /// it may be locked).  Better to err on the side of "no marker
    /// found, proceed with restore" than to spuriously refuse a
    /// legitimate restore because of one malformed file.
    /// </para>
    /// </remarks>
    internal static bool ContainsRedactedMarker(string tempRoot)
    {
        if (!Directory.Exists(tempRoot))
        {
            return false;
        }

        const string marker = JsonRedactor.RedactedMarker;
        foreach ((string filePath, bool _) in FindConfigFilesToValidate(tempRoot))
        {
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
                continue;
            }

            // Cheap pre-filter: skip files that don't even contain the
            // marker substring textually.  Avoids JsonNode.Parse on the
            // vast majority of files in a non-tampered archive.
            if (!content.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            // Substring hit — parse and verify the marker appears as a
            // string-valued leaf (not, say, inside a comment or a
            // legitimate description field).
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(content);
            }
            catch (JsonException)
            {
                continue;
            }

            if (root is null)
            {
                continue;
            }

            if (AnyLeafEqualsMarker(root, marker))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyLeafEqualsMarker(JsonNode node, string marker)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> kv in obj)
                {
                    if (kv.Value is not null && AnyLeafEqualsMarker(kv.Value, marker))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray arr:
                foreach (JsonNode? item in arr)
                {
                    if (item is not null && AnyLeafEqualsMarker(item, marker))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValue val:
                return val.TryGetValue(out string? s)
                       && string.Equals(s, marker, StringComparison.Ordinal);
            default:
                return false;
        }
    }

    /// <summary>
    /// Validates every config file found under <paramref name="tempRoot"/> against the
    /// schema files bundled in the archive (under <c>Schemas/</c>).  If the archive
    /// predates schema bundling (no <c>Schemas/</c> folder), validation is silently
    /// skipped (fail-open).
    /// </summary>
    /// <returns>
    /// A list of human-readable issue strings (empty when all files pass or no schemas
    /// were bundled).  Never throws — all exceptions are caught and returned as warnings.
    /// </returns>
    private static async Task<IReadOnlyList<string>> ValidateExtractedConfigsAsync(
        string tempRoot, CancellationToken ct)
    {
        List<string> warnings = new();
        try
        {
            string schemasDir = Path.Combine(tempRoot, "Schemas");
            if (!Directory.Exists(schemasDir))
            {
                return warnings; // old backup — skip
            }

            // Load bundled schemas by name so we can route ClaudeCode vs Desktop.
            Dictionary<string, JsonSchema> schemaByName = new(StringComparer.OrdinalIgnoreCase);
            foreach (string schemaFile in Directory.EnumerateFiles(schemasDir, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string json = await File.ReadAllTextAsync(schemaFile, ct).ConfigureAwait(false);
                    JsonSchema schema = SchemaRegistry.ParseSchema(json);
                    schemaByName[Path.GetFileName(schemaFile)] = schema;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                }
            }

            if (schemaByName.Count == 0)
            {
                return warnings;
            }

            EvaluationOptions evalOpts = new() { OutputFormat = OutputFormat.List };

            foreach ((string filePath, bool isClaudeCode) in FindConfigFilesToValidate(tempRoot))
            {
                ct.ThrowIfCancellationRequested();

                string schemaName = isClaudeCode ? "claude-code-settings.json" : "claude-desktop-config.json";
                if (!schemaByName.TryGetValue(schemaName, out JsonSchema? schema))
                {
                    continue;
                }

                string json;
                try
                {
                    json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                    continue;
                }

                JsonObject? root;
                try
                {
                    root = JsonNode.Parse(json) as JsonObject;
                }
                catch (JsonException)
                {
                    continue;
                }

                if (root == null)
                {
                    continue;
                }

                try
                {
                    using JsonDocument jsonDoc = JsonDocument.Parse(root.ToJsonString());
                    EvaluationResults results = schema.Evaluate(jsonDoc.RootElement, evalOpts);
                    if (results.IsValid)
                    {
                        continue;
                    }

                    string rel = RelPath(tempRoot, filePath);
                    foreach (EvaluationResults detail in results.Details ?? [])
                    {
                        if (detail.IsValid || detail.Errors is not { Count: > 0 } errs)
                        {
                            continue;
                        }

                        string path = detail.InstanceLocation.ToString() ?? string.Empty;
                        foreach ((string _, string msg) in errs)
                        {
                            warnings.Add($"{rel}: {path}: {msg}");
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException
                                               or InvalidOperationException
                                               or ArgumentException)
                {
                    // Schema evaluation failures are non-fatal — the warnings list
                    // accumulates what we can validate; broken inputs simply contribute
                    // nothing rather than aborting the whole pre-restore validation.
                    _ = ex;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or JsonException
                                       or InvalidOperationException)
        {
            // Outer guard: any unexpected I/O or schema-loading failure → return
            // whatever warnings we accumulated so far without surfacing the error.
            _ = ex;
        }

        return warnings;
    }

    /// <summary>
    /// Enumerates config files under <paramref name="tempRoot"/> that should be
    /// validated, paired with a flag indicating whether to use the Claude Code schema
    /// (<c>true</c>) or the Claude Desktop schema (<c>false</c>).
    /// </summary>
    private static IEnumerable<(string FilePath, bool IsClaudeCode)> FindConfigFilesToValidate(string tempRoot)
    {
        // Claude Code: claude.json at root level
        string claudeJson = Path.Combine(tempRoot, "ClaudeCode", "claude.json");
        if (File.Exists(claudeJson))
        {
            yield return (claudeJson, true);
        }

        // Claude Code: settings.json / settings.local.json anywhere under claude-dir
        string claudeDir = Path.Combine(tempRoot, "ClaudeCode", "claude-dir");
        if (Directory.Exists(claudeDir))
        {
            foreach (string f in Directory.EnumerateFiles(claudeDir, "settings.json", SearchOption.AllDirectories))
            {
                yield return (f, true);
            }

            foreach (string f in Directory.EnumerateFiles(claudeDir, "settings.local.json", SearchOption.AllDirectories))
            {
                yield return (f, true);
            }
        }

        // Claude Desktop: main config
        string desktopConfig = Path.Combine(tempRoot, "ClaudeDesktop", "claude_desktop_config.json");
        if (File.Exists(desktopConfig))
        {
            yield return (desktopConfig, false);
        }

        // Claude Desktop: profile configs
        string desktopProfiles = Path.Combine(tempRoot, "ClaudeDesktop", "profiles");
        if (Directory.Exists(desktopProfiles))
        {
            foreach (string f in Directory.EnumerateFiles(desktopProfiles, "*.json", SearchOption.AllDirectories))
            {
                yield return (f, false);
            }
        }
    }

    /// <summary>
    /// Returns <paramref name="filePath"/> relative to <paramref name="baseDir"/>,
    /// using forward slashes for cross-platform legibility in log messages.
    /// </summary>
    private static string RelPath(string baseDir, string filePath)
    {
        return Path.GetRelativePath(baseDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}