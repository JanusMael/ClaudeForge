using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Json.Schema;
using Serilog;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.AgentForge.Core.Backup;

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
    /// <param name="openProjectRoots">
    /// The project(s) the host currently has open, if any. Forwarded to
    /// <see cref="BuildAuthorisedRoots"/>. ⚠ Omitting it is safe but NARROWS what can be
    /// restored — a project no other source on this machine knows about is refused.
    /// </param>
    internal static async Task<RestoreResult> RestoreAsync(
        BackupEntry entry,
        IReadOnlyList<ProductDescriptor>? restorableProducts = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyCollection<string>? openProjectRoots = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        IReadOnlyList<ProductDescriptor> products = restorableProducts ?? DefaultRestorableProducts;
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
            IReadOnlyList<string> validationWarnings =
                await ValidateExtractedConfigsAsync(tempRoot, ct).ConfigureAwait(false);

            // Signal start of the apply phase.
            // applySections counts every row of ArchiveSections plus the two manifest-driven
            // sections below (projects, worktrees). ⚠ Derived, not a constant: it was `const int
            // applySections = 7` with a comment listing the seven by name, so adding a section
            // meant remembering to bump a number in a different place — and getting it wrong only
            // shows up as a progress bar that stops short or never fills.
            // We use a rolling applyStep counter to give the progress bar visible
            // movement during the apply phase (extraction is done; bar would stall).
            int applySections = products.Sum(p => p.Backup.Sections.Count) + 2;
            int applyStep = 0;
            progress?.Report(new BackupProgress(
                0, applySections, "Applying restore…", totalExtracted, RestoreProgressIds.Applying));

            // Place files into real paths, with .bak sidecars.
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int restored = 0;
            RestoreJournal journal = new();
            List<string> skipped = journal.Skipped;
            List<string> fileFailures = journal.FileFailures;

            foreach (ProductDescriptor product in products)
            {
                foreach (ProductArchiveSection section in product.Backup.Sections)
                {
                    string source = Path.Combine([tempRoot, product.ArchiveFolder, .. section.SubPath]);

                    if (section.IsDirectory)
                    {
                        (int r, List<string> errs) = RestoreDirectory(source, section.Destination(), stamp, journal);
                        restored += r;
                        fileFailures.AddRange(errs);
                    }
                    else
                    {
                        (int r, string? f) = RestoreSection(source, section.Destination(), stamp, journal);
                        restored += r;
                        if (f != null)
                        {
                            fileFailures.Add(f);
                        }
                    }

                    progress?.Report(
                        new BackupProgress(
                            ++applyStep, applySections, section.ProgressLabel, totalExtracted,
                            section.ProgressLabelId));
                }
            }

            // Per-project restore: the manifest says where each project lives, and THIS machine's
            // own project list says which of those the restore is allowed to write to. Read here
            // rather than inside RestoreProjects so the authorisation source is visible at the
            // call site — it is the difference between a security check and a silent scope limit.
            IReadOnlyCollection<string> authorisedRoots = BuildAuthorisedRoots(openProjectRoots);
            restored += RestoreProjects(tempRoot, entry.Manifest, stamp, authorisedRoots, journal);
            progress?.Report(new BackupProgress(
                ++applyStep, applySections, "Restoring projects…", totalExtracted,
                RestoreProgressIds.Projects));

            restored += RestoreWorktrees(tempRoot, stamp, journal);
            progress?.Report(new BackupProgress(
                ++applyStep, applySections, "Restoring worktrees…", totalExtracted,
                RestoreProgressIds.Worktrees));

            progress?.Report(new BackupProgress(restored, restored,
                "Restore complete", 0, RestoreProgressIds.Complete));

            // ⭐ Sweep the sidecars this run wrote, and ONLY when every file landed.
            //
            // A sidecar is the undo trail for a file the restore overwrote. Once the restore has
            // committed cleanly there is nothing to undo TO — the content it shadowed came from a
            // prior save that the archive itself carries — and leaving them roughly doubles the
            // directory on disk, silently, every time. A 211 MB profile produced 5,899 of them and
            // nothing in the product ever removed one.
            //
            // ⛔ Two bounds, both load-bearing:
            //   • Only on a clean run. One file failure means the restore is PARTIAL, and a partial
            //     restore is exactly when someone wants the previous bytes back.
            //   • Only THIS run's sidecars, by exact path from the journal. An older restore's are
            //     someone else's undo trail, and --cleanup-restore-sidecars remains the tool for
            //     those — including everything written before this sweep existed.
            int sidecarsSwept = fileFailures.Count == 0 ? SweepSidecars(journal.Sidecars) : 0;

            string message = $"Restored {restored} item(s).";
            message += sidecarsSwept > 0
                ? $" Cleaned up {sidecarsSwept} .pre-restore-{stamp}.bak sidecar(s) written during the restore."
                : fileFailures.Count > 0 && journal.Sidecars.Count > 0
                    ? $" {journal.Sidecars.Count} .pre-restore-{stamp}.bak sidecar(s) were kept because"
                      + " some files could not be restored."
                    : $" Existing files were moved aside as .pre-restore-{stamp}.bak.";

            if (skipped.Count > 0)
            {
                // ⚠ The wording used to assert the paths were "not present on this machine",
                // which was false for every path the engine merely refused to write to — and that
                // is the sentence that sent a user looking for a missing folder instead of a
                // refusal. The reason now travels with each entry rather than in the preamble.
                message += $" Skipped {skipped.Count} project/worktree(s): "
                           + string.Join(", ", skipped.Take(5))
                           + (skipped.Count > 5 ? $" … (+{skipped.Count - 5} more)" : "");
            }

            // ⛔ Say what the archive did NOT carry, not just what it did.
            //
            // A product can declare credential-gated sections — OpenCode's session database is
            // one — and an archive taken without the opt-in simply has no such entry. The restore
            // then succeeds, reports a healthy item count, and the user's entire session history
            // is missing with nothing anywhere saying why. "Restored 42 items" is a true statement
            // that leaves someone hunting a bug that does not exist.
            //
            // ⚠ Driven by the MANIFEST's flag rather than by what was found in the archive: an
            // archive with the opt-in whose database happened not to exist is a different
            // situation from one taken without it, and only the manifest distinguishes them.
            if (!entry.Manifest.IncludedCredentials)
            {
                string[] omitted =
                [
                    .. products
                        .Where(p => p.Backup.Sections.Any(s => s.RequiresCredentialOptIn))
                        .Select(p => p.DisplayName)
                ];

                if (omitted.Length > 0)
                {
                    message += " This backup was taken without the credentials opt-in, so it does not"
                               + " contain the credential-bearing data for "
                               + string.Join(", ", omitted)
                               + " — including saved session history. That data was never in the"
                               + " archive; it has not been lost from this machine.";
                }
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
    /// The set of roots this restore may write to, built from the SAME sources
    /// <see cref="BackupEngine.CreateAsync"/> captures from — read on this machine, never from
    /// the archive.
    /// </summary>
    /// <param name="openProjectRoots">
    /// The project the host currently has open, when it has one. The user chose it in this
    /// application in this session, so it is as trustworthy as the files below and it is the case
    /// <c>F7</c> actually reported: a *Settings only* backup captures exactly the open project,
    /// and nothing else on the machine need ever have heard of it.
    /// </param>
    /// <remarks>
    /// ⛔ <b>The first F7 fix covered only one of the three sources and would not have closed the
    /// reported case.</b> Backup captures the explicitly-open project, the
    /// <c>additionalDirectories</c> its settings files name, and — in Full mode — every project in
    /// <c>~/.claude.json</c>. Authorising only the last still refuses a freshly-opened project that
    /// Claude Code itself has never seen, which is precisely the archive the finding was written
    /// about. Measured, not reasoned: the retest fixture was absent from a 62-entry project list.
    /// </remarks>
    internal static IReadOnlyCollection<string> BuildAuthorisedRoots(
        IReadOnlyCollection<string>? openProjectRoots)
    {
        List<string> explicitRoots = openProjectRoots?
                                     .Where(r => !string.IsNullOrWhiteSpace(r))
                                     .ToList() ?? [];

        HashSet<string> roots = new(OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase);

        foreach (string r in explicitRoots)
        {
            roots.Add(r);
        }

        foreach (string r in KnownProjectsDiscovery
                             .ResolveExisting(PlatformPaths.ClaudeJsonPath).ExistingProjectRoots)
        {
            roots.Add(r);
        }

        // additionalDirectories, resolved from the live settings files — the user's own
        // configuration, and the second thing a backup turns into a captured project.
        try
        {
            foreach (string r in AdditionalDirectoriesResolver.Resolve(
                         BackupEngine.CollectSettingsFilesForDiscovery(explicitRoots)))
            {
                roots.Add(r);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable settings file narrows the authorised set; it must never fail the
            // restore, which still has the other two sources and the home-folder allow.
            Log.Debug(ex, "[Restore] Could not resolve additionalDirectories for authorisation.");
        }

        return roots;
    }

    /// <summary>
    /// Delete the sidecars a committed restore no longer needs. Returns how many went.
    /// </summary>
    /// <remarks>
    /// Best-effort per file, like every other sidecar operation here: a locked or read-only
    /// sidecar is left behind and the restore still reports success, because failing a completed
    /// restore over a leftover copy would be the worse outcome. Those survivors are what
    /// <c>--cleanup-restore-sidecars</c> is for.
    /// </remarks>
    internal static int SweepSidecars(IReadOnlyList<string> sidecars)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        int deleted = 0;
        foreach (string path in sidecars)
        {
            // Belt and braces: only ever delete something that still looks like our own
            // output. The list is built by this engine, but a path is a path and this is
            // the one place in the restore that removes user-visible files.
            if (!RestoreSidecarCleanup.LooksLikeRestoreSidecar(Path.GetFileName(path)))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Debug(ex, "[Restore] Could not sweep sidecar {Path}", path);
            }
        }

        return deleted;
    }

    /// <summary>
    /// May the restore write to <paramref name="candidate"/>? True when it lies under the user's
    /// own profile, or when this machine's own project list names it (exactly, or as an ancestor).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>The manifest proposes; this machine disposes.</b> Both allows are things a crafted
    /// archive cannot influence: the running user's home directory, and the keys of their own
    /// <c>~/.claude.json</c>. A zip naming <c>C:\Windows\System32</c> satisfies neither.
    /// </para>
    /// <para>
    /// ⚠ An ANCESTOR match counts, so a project at <c>D:\src\app</c> authorises
    /// <c>D:\src\app\.claude</c>. It is a prefix test on a canonicalised path with an explicit
    /// separator appended, so <c>D:\src\app-secrets</c> does NOT match <c>D:\src\app</c> — the
    /// same trap the gitignore glob copy fell into.
    /// </para>
    /// </remarks>
    internal static bool IsAuthorisedRestoreTarget(
        string? candidate, IReadOnlyCollection<string> knownProjectRoots)
    {
        ArgumentNullException.ThrowIfNull(knownProjectRoots);
        if (IsUnderUserProfile(candidate))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate) || knownProjectRoots.Count == 0)
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(candidate)
                       .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or PathTooLongException
                                       or NotSupportedException
                                       or SecurityException)
        {
            _ = ex;
            return false;
        }

        // A UNC path is never authorised, whatever the project list says — the same rule
        // IsUnderUserProfile applies, restated because this branch does not go through it.
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Windows and macOS compare paths case-insensitively; Linux does not, and a
        // case-insensitive match there would authorise a genuinely different directory.
        StringComparison cmp = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (string root in knownProjectRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string rootFull;
            try
            {
                rootFull = Path.GetFullPath(root)
                               .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException
                                           or PathTooLongException
                                           or NotSupportedException
                                           or SecurityException)
            {
                _ = ex;
                continue;
            }

            if (string.Equals(full, rootFull, cmp)
                || full.StartsWith(rootFull + Path.DirectorySeparatorChar, cmp))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="candidate"/> is an absolute path that
    /// lies under the current user's profile directory. ⓘ One of the two allows in
    /// <see cref="IsAuthorisedRestoreTarget"/>, which is what callers ask — on its own this
    /// was a scope limit wearing a security check's clothes, and it is why a project kept
    /// outside the home directory could be backed up but never restored.
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

        // ⚠ PlatformPaths.UserProfile, not Environment.GetFolderPath. Every other path this
        // engine resolves goes through PlatformPaths, which honours the test profile override;
        // reading Environment here made this one predicate disagree with the rest of the engine
        // under a redirected home — so the refusal path could not be exercised against a real
        // directory at all, and the F7 repro was untestable end to end for that reason alone.
        // Identical in production, where the override is null.
        string home = Path.GetFullPath(PlatformPaths.UserProfile)
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
    internal static (int Restored, string? Failure) RestoreSection(
        string srcFile, string destFile, string stamp, RestoreJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
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
                string sidecar = $"{destFile}.pre-restore-{stamp}.bak";
                File.Copy(destFile, sidecar, overwrite: true);
                journal.Sidecars.Add(sidecar);
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
    internal static (int Restored, List<string> Failures) RestoreDirectory(
        string srcDir, string destDir, string stamp, RestoreJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
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
                    string sidecar = $"{dest}.pre-restore-{stamp}.bak";
                    File.Copy(dest, sidecar, overwrite: true);
                    journal.Sidecars.Add(sidecar);
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

    /// <summary>
    /// Restore each project subtree the archive carries, to the live path the manifest names.
    /// </summary>
    /// <param name="knownProjectRoots">
    /// ⭐ <b>The authorisation set, read from THIS machine</b> — the keys of
    /// <c>~/.claude.json</c>'s <c>projects</c> object, via <see cref="KnownProjectsDiscovery"/>.
    /// <para>
    /// ⛔ It exists because <see cref="IsUnderUserProfile"/> alone was **not** the security rule
    /// it looked like — it was also, silently, a scope limit. Backup captures whatever project is
    /// open; a great many people keep their repositories outside their home directory, and for
    /// every one of those the restore refused the project, reported it as a path *"not present on
    /// this machine"*, and returned success. The archive said the files were there, the Restore
    /// tab said it would overwrite, and nothing put them back.
    /// </para>
    /// <para>
    /// The manifest is untrusted — a crafted zip can name <c>C:\Windows\System32</c> — so the
    /// answer is not to drop the check but to ask a source the zip cannot write. A path the user's
    /// own Claude Code has opened is a path the user chose; <c>C:\Windows\System32</c> is not in
    /// anybody's project list. Under-profile stays an independent allow, so a machine with no
    /// <c>~/.claude.json</c> behaves exactly as before.
    /// </para>
    /// <para>
    /// ⚠ Pass an EMPTY set, never null, to mean "authorise nothing extra". Every call site names
    /// it, because a defaulted one is how the scope limit stayed invisible the first time.
    /// </para>
    /// </param>
    internal static int RestoreProjects(string tempRoot, BackupManifest manifest, string stamp,
                                        IReadOnlyCollection<string> knownProjectRoots,
                                        RestoreJournal journal)
    {
        ArgumentNullException.ThrowIfNull(knownProjectRoots);
        ArgumentNullException.ThrowIfNull(journal);
        List<string> skipped = journal.Skipped;
        List<string> fileFailures = journal.FileFailures;
        // Folder from the descriptor, not a literal — the write side has always used it, and a
        // rename on one side only would silently stop matching rather than erroring.
        string projectsDir = Path.Combine(tempRoot, SchemaRegistry.ClaudeCodeProduct.ArchiveFolder, "projects");
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
            // attacker crafts a zip), so it must be one this machine independently vouches
            // for — under the user's own home, or a project their own Claude Code has opened.
            if (!IsAuthorisedRestoreTarget(livePath, knownProjectRoots))
            {
                skipped.Add(name + " (not a path this machine recognises)");
                continue;
            }

            (int c, List<string> failures) = RestoreDirectory(projBackupDir, livePath, stamp, journal);
            count += c;
            fileFailures.AddRange(failures);
        }

        return count;
    }

    /// <summary>
    /// Restore each external git worktree the archive carries.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Worktrees keep the under-profile rule, and that is a KNOWN GAP, not an oversight.</b>
    /// The projects fix above works because this machine keeps an independent list of the paths
    /// the user has opened; nothing equivalent exists for worktrees. The manifest's
    /// <c>projectRoot</c> cannot serve — a crafted zip would simply name a real project beside an
    /// arbitrary <c>worktreePath</c>, which authorises nothing. The sound source is
    /// <see cref="WorktreeProbe"/> against the live repositories, and that means spawning
    /// <c>git</c> per project, with a timeout each, in front of a destructive operation. That is a
    /// decision with a cost, so it is recorded rather than taken in passing.
    /// </remarks>
    internal static int RestoreWorktrees(string tempRoot, string stamp, RestoreJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        List<string> skipped = journal.Skipped;
        List<string> fileFailures = journal.FileFailures;
        string wtDir = Path.Combine(tempRoot, SchemaRegistry.ClaudeCodeProduct.ArchiveFolder, "worktrees");
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
                    skipped.Add(wtName + " (worktree path is outside your home folder)");
                    continue;
                }

                (int c, List<string> failures) = RestoreDirectory(wtBackupDir, meta.WorktreePath, stamp, journal);
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
        foreach ((string filePath, ProductDescriptor _) in FindConfigFilesToValidate(tempRoot))
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

            // Load bundled schemas by file name so each config file can be routed to the
            // schema its own product names (ProductDescriptor.SchemaFileName).
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
                catch (Exception ex) when (ex is IOException or JsonException or JsonSchemaException)
                {
                    // One unreadable/invalid schema entry must not abort the restore or stop the
                    // others from loading: skip it — IOException (unreadable), JsonException
                    // (malformed JSON), or JsonSchemaException (well-formed JSON that isn't a valid
                    // JSON-Schema, e.g. a keyword with a wrong-typed value, or an unknown keyword
                    // under a strict $vocabulary dialect).
                    _ = ex;
                }
            }

            if (schemaByName.Count == 0)
            {
                return warnings;
            }

            EvaluationOptions evalOpts = new() { OutputFormat = OutputFormat.List };

            foreach ((string filePath, ProductDescriptor product) in FindConfigFilesToValidate(tempRoot))
            {
                ct.ThrowIfCancellationRequested();

                if (!schemaByName.TryGetValue(product.SchemaFileName, out JsonSchema? schema))
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
                                       or InvalidOperationException
                                       or JsonSchemaException)
        {
            // Outer guard: any unexpected I/O or schema load/evaluate failure → return
            // whatever warnings we accumulated so far without surfacing the error
            // (honors this method's documented "never throws" contract).
            _ = ex;
        }

        return warnings;
    }

    /// <summary>
    /// The products a restore knows how to apply when the caller names none — Claude's two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>A DEFAULT, not the list.</b> A restore is driven by an archive, and
    /// <c>manifest.json</c> records only archive FOLDER NAMES — turning those back into descriptors
    /// needs a lookup, and a host that edits a different product supplies its own via
    /// <see cref="BackupEngine(WorktreeProbe, IBackupFileSystem, IReadOnlyList{ProductDescriptor})"/>.
    /// This assembly must never reference <c>OpenCode.Sdk</c>, so it could not name that product
    /// even if it wanted to.
    /// </para>
    /// <para>
    /// ⚠ <b>Claude's two stay the default deliberately.</b> Every existing caller —
    /// <c>BackupEngine.Default</c> included — keeps restoring exactly what it restored before,
    /// which is what makes the frozen v1 fixture still pass.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<ProductDescriptor> DefaultRestorableProducts =
    [
        SchemaRegistry.ClaudeCodeProduct,
        SchemaRegistry.ClaudeDesktopProduct,
    ];

    /// <summary>
    /// Which config files a backup archive can contain, as data: each row pairs the product
    /// whose schema validates those files with the archive-relative directory they live in,
    /// the file names to look for there, and whether to recurse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a <c>bool IsClaudeCode</c> that the validator turned straight back into
    /// one of two schema file names with a ternary — the <i>second</i> such boolean in the
    /// codebase; Phase 4a removed the first, on <c>AgentConfigClientCore</c>. Naming the
    /// product per row makes a third product one more row instead of a third branch, and
    /// reduces the schema selection at the call site to a dictionary lookup on
    /// <see cref="ProductDescriptor.SchemaFileName"/>.
    /// </para>
    /// <para>
    /// The top-level folder is <b>not</b> restated here: each row takes it from its own
    /// <see cref="ProductDescriptor.ArchiveFolder"/>, the same property
    /// <see cref="BackupEngine"/> writes with. Phase 4b left those as literals on both sides
    /// and flagged the hazard — a folder renamed on one side only does not error, it silently
    /// stops matching, and a config that stops being found just stops being validated.
    /// Only the sub-paths below (<c>claude-dir</c>, <c>profiles</c>) remain per-row data.
    /// </para>
    /// </remarks>
    private static readonly (ProductDescriptor Product, string[] SubDir, string[] FileNames, SearchOption Depth)[]
        ValidatableConfigs =
        [
            // Claude Code: claude.json at the product root — deliberately NOT recursive, or a
            // claude.json captured under projects/ would be validated as a settings file.
            (SchemaRegistry.ClaudeCodeProduct, [], ["claude.json"],
                SearchOption.TopDirectoryOnly),

            // Claude Code: settings.json / settings.local.json anywhere under claude-dir.
            (SchemaRegistry.ClaudeCodeProduct, ["claude-dir"],
                ["settings.json", "settings.local.json"], SearchOption.AllDirectories),

            // Claude Desktop: the main config.
            (SchemaRegistry.ClaudeDesktopProduct, [], ["claude_desktop_config.json"],
                SearchOption.TopDirectoryOnly),

            // Claude Desktop: every profile config, whatever it is named.
            (SchemaRegistry.ClaudeDesktopProduct, ["profiles"], ["*.json"],
                SearchOption.AllDirectories),
        ];

    /// <summary>
    /// Enumerates config files under <paramref name="tempRoot"/> that should be validated,
    /// each paired with the product whose schema validates it.
    /// </summary>
    private static IEnumerable<(string FilePath, ProductDescriptor Product)> FindConfigFilesToValidate(
        string tempRoot)
    {
        foreach ((ProductDescriptor product, string[] subDir, string[] fileNames, SearchOption depth)
                 in ValidatableConfigs)
        {
            string dir = Path.Combine([tempRoot, product.ArchiveFolder, .. subDir]);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            // One file NAME at a time rather than one directory at a time: preserves the
            // order the four hardcoded yield blocks produced (every settings.json, then
            // every settings.local.json), which is the order warnings accumulate in.
            foreach (string fileName in fileNames)
            {
                foreach (string file in Directory.EnumerateFiles(dir, fileName, depth))
                {
                    yield return (file, product);
                }
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