using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// High-level entry point for Claude backup / restore / list / delete operations.
/// All methods are cancellation-aware, progress-reporting, and avoid throwing for
/// expected failure modes — they return typed results the UI can render directly.
/// </summary>
/// <remarks>
/// <para>
/// **Path discipline.** Every source path is taken from <see cref="PlatformPaths"/>
/// so there is a single source of truth for where Claude stores its data. The engine
/// never fabricates paths from string literals.
/// </para>
/// <para>
/// **Atomicity.** Archive writes go through <see cref="ZipArchiveWriter"/>, which
/// writes to a sibling <c>.tmp</c> file and renames on success. A cancelled backup
/// cleans up after itself and never leaves a half-written <c>.zip</c> on disk.
/// </para>
/// <para>
/// **Security.** Restore rejects any zip entry whose resolved destination path escapes
/// the restore target directory (classic zip-slip defence).
/// </para>
/// </remarks>
public sealed class BackupEngine
{
    /// <summary>Shared instance.</summary>
    public static readonly BackupEngine Default = new();

    private readonly WorktreeProbe _worktreeProbe;
    private readonly IBackupFileSystem _fs;

    /// <summary>
    /// Construct with custom collaborators (used by tests).
    /// </summary>
    /// <param name="worktreeProbe">Probe used to discover external git worktrees.
    /// Defaults to a new <see cref="WorktreeProbe"/>.</param>
    /// <param name="fileSystem">File-system seam. Defaults to
    /// <see cref="RealBackupFileSystem.Instance"/> for production. Tests inject an
    /// in-memory implementation to exercise retention, discovery, and similar
    /// purely-file-system code paths without real disk I/O.</param>
    public BackupEngine(WorktreeProbe? worktreeProbe = null, IBackupFileSystem? fileSystem = null)
    {
        _worktreeProbe = worktreeProbe ?? new WorktreeProbe();
        _fs = fileSystem ?? RealBackupFileSystem.Instance;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CREATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a backup archive according to <paramref name="request"/>. Returns a
    /// result describing success / failure and (on success) the manifest that was
    /// written. Never throws for expected failures — returns a failure result instead.
    /// </summary>
    public async Task<BackupResult> CreateAsync(
        BackupRequest request,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // resolve project set + worktrees.
        progress?.Report(new BackupProgress(0, 0, "Discovering projects…", 0));
        IReadOnlyList<string> settingsFilesForDiscovery = CollectSettingsFilesForDiscovery(request.ExplicitProjectDirs);
        IReadOnlyList<string> discovered = AdditionalDirectoriesResolver.Resolve(settingsFilesForDiscovery);
        IReadOnlyList<string> projects = MergeExplicitAndDiscovered(request.ExplicitProjectDirs, discovered);

        List<string> warnings = [];
        IReadOnlyList<BackupWorktreeEntry> worktrees = [];
        try
        {
            WorktreeDiscoveryResult discovery = await _worktreeProbe.DiscoverExternalAsync(projects, ct).ConfigureAwait(false);
            worktrees = discovery.Worktrees;
            if (discovery.GitMissing)
            {
                warnings.Add(
                    "git not found on PATH or timed out; external worktrees were not included in this backup.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add($"Worktree discovery failed: {ex.Message}");
        }

        // build manifest body.
        BackupManifest manifest = new()
        {
            Kind = "backup",
            SchemaVersion = BackupManifest.CurrentSchemaVersion,
            CreatedUtc = DateTime.UtcNow,
            Platform = PlatformPaths.PlatformId,
            AppVersion = GetAppVersion(),
            Mode = request.Mode,
            Clients = BuildClientList(request),
            Projects = projects.ToList(),
            Worktrees = worktrees.ToList(),
            // Sanitized backups deliberately drop credentials regardless of the
            // request flag — the file is opaque bytes Anthropic/OAuth tokens
            // and would leak verbatim into a sharing-targeted archive.  This
            // is defence-in-depth: the redactor itself only touches .json
            // files, so a credentials file (.credentials.json) would otherwise
            // be redacted, but skipping it entirely is the safer default.
            IncludedCredentials = request.IncludeCredentials
                                  && request.Mode != BackupMode.Sanitized
                                  && File.Exists(PlatformPaths.CredentialsPath),
            Warnings = warnings,
        };

        // annotate Sanitized backups in the manifest so anyone
        // reading manifest.json out-of-band knows the contract: this archive
        // is for SHARING, not RESTORE.  The Restore engine refuses to apply
        // it; the GUI's Restore list shows a "(sanitized — for sharing)"
        // chip; this warning is the third surface communicating the same
        // thing, intended for support engineers who receive the archive
        // and crack it open in a hex viewer.
        if (request.Mode == BackupMode.Sanitized)
        {
            warnings.Add(
                "Sanitized backup: secret-bearing values were replaced with \"[redacted]\". " +
                "Not restorable. Intended for sharing config shape with support / community / bug reports.");
        }

        // build the archive.
        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(request.DestinationZipPath);

        // Sanitized mode wires a JSON in-flight transformer so
        // every *.json file is parsed and secret-bearing values replaced
        // with the literal string "[redacted]" before landing in the zip.
        // Failure mode: a JsonException on a malformed file gets caught
        // INSIDE the transformer and the file is emitted as an explicit
        // error placeholder rather than its raw bytes — we never want a
        // sanitized archive to leak the original content of a file we
        // couldn't redact.
        if (request.Mode == BackupMode.Sanitized)
        {
            writer.FileTransformer = RedactFileForSharing;
        }

        try
        {
            // --- Claude Code ---
            if (request.IncludeClaudeCode)
            {
                if (File.Exists(PlatformPaths.ClaudeJsonPath))
                {
                    writer.AddFile(PlatformPaths.ClaudeJsonPath, "ClaudeCode/claude.json");
                }

                if (Directory.Exists(PlatformPaths.ClaudeHome))
                {
                    AddClaudeHome(writer, request);
                }

                foreach (string projectRoot in projects)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(projectRoot.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    AddProjectClaudeData(writer, projectRoot, $"ClaudeCode/projects/{name}");
                }

                foreach (BackupWorktreeEntry wt in worktrees)
                {
                    ct.ThrowIfCancellationRequested();
                    string wtName = Path.GetFileName(wt.WorktreePath.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(wtName))
                    {
                        continue;
                    }

                    AddProjectClaudeData(writer, wt.WorktreePath, $"ClaudeCode/worktrees/{wtName}");
                    // Stash the worktree's project-root mapping so restore can re-locate it.
                    BackupWorktreeEntry meta = new() { ProjectRoot = wt.ProjectRoot, WorktreePath = wt.WorktreePath };
                    string metaJson = JsonSerializer.Serialize(meta, BackupJsonContext.Default.BackupWorktreeEntry);
                    writer.AddTextEntry($"ClaudeCode/worktrees/{wtName}/.worktree-meta.json", metaJson);
                }
            }

            // --- Claude Desktop ---
            // Backs up: the live config file, any named Desktop profiles, and the
            // .desktop-current active-profile pointer. Log files are excluded — they
            // are runtime-only artefacts, can't be meaningfully restored, and may be
            // locked by a running Claude Desktop process.
            if (request.IncludeClaudeDesktop)
            {
                if (File.Exists(PlatformPaths.DesktopConfigPath))
                {
                    writer.AddFile(PlatformPaths.DesktopConfigPath,
                        "ClaudeDesktop/claude_desktop_config.json");
                }

                // Desktop profiles directory (parallel to CLI's ~/.claude/profiles/).
                if (Directory.Exists(PlatformPaths.DesktopProfilesDirectory))
                {
                    writer.AddDirectory(PlatformPaths.DesktopProfilesDirectory,
                        "ClaudeDesktop/profiles");
                }

                // Active-profile pointer so the restore reinstates which profile was live.
                if (File.Exists(PlatformPaths.DesktopCurrentProfileFilePath))
                {
                    writer.AddFile(PlatformPaths.DesktopCurrentProfileFilePath,
                        "ClaudeDesktop/.desktop-current");
                }
            }

            // Sanitized-mode parallel precompute.  Runs the
            // RedactFileForSharing transformer in parallel across multiple
            // cores BEFORE CommitAsync starts so the single-threaded zip
            // writer feeds on ready-to-write strings instead of doing
            // the CPU-bound regex scan inline.  Halves wall-clock time
            // on multi-core machines for typical Sanitized backups.
            // No-op for non-Sanitized modes (FileTransformer is null).
            if (request.Mode == BackupMode.Sanitized)
            {
                await writer.PrecomputeTransformsAsync(ct: ct).ConfigureAwait(false);
            }

            // --- Manifest goes last so a truncated archive is obviously broken ---
            // Capture the real file-entry count before writing the manifest itself, then
            // include the count inside the manifest for accurate stats. Note: we add 1
            // to cover the manifest entry we are about to write.
            int pendingCountAtCommit = writer.PendingCount + 1;
            manifest.ItemCount = pendingCountAtCommit;
            // Bundle the current schema files into the archive under Schemas/ so that
            // restore can validate against the schema version in effect at backup time,
            // not whatever version is currently installed.  BundleSchemas is called AFTER
            // ItemCount is captured so the schema entries don't inflate the user-visible
            // item count, and BEFORE the manifest so manifest.json remains the last entry.
            //
            // Manifest + schemas are added via AddTextEntry (string content)
            // so they bypass the FileTransformer + precompute pipeline
            // naturally — neither needs redaction.
            BundleSchemas(writer);
            writer.AddTextEntry("manifest.json",
                JsonSerializer.Serialize(manifest, BackupJsonContext.Default.BackupManifest));

            // Flush to disk.
            long actualBytes = await writer.CommitAsync(progress, ct).ConfigureAwait(false);

            // Bake the true byte count into the manifest by rewriting it inside the
            // finished zip. Small cost, huge UX benefit (Restore list shows real sizes).
            // CancellationToken.None is intentional here: the archive has already been
            // committed (temp-file renamed to the final path). A cancellation at this
            // point would leave a valid archive with a stale-but-accurate ItemCount in
            // the manifest — not a corrupt archive. We accept that outcome rather than
            // leave no SizeBytes field at all, which would break the Restore list size
            // column. The rewrite is ~hundreds of bytes and always completes promptly.
            manifest.SizeBytes = actualBytes;
            // Note: ItemCount was already set before CommitAsync (line above).
            // We do NOT re-assign it here — the value is identical and re-assigning
            // would be misleading.
            if (writer.SkippedSymlinks.Count > 0)
            {
                manifest.Warnings.Add(
                    $"Skipped {writer.SkippedSymlinks.Count} symlink(s)/junction(s) during backup.");
            }

            // SkippedFiles records only filenames (Path.GetFileName), not file content.
            // This is intentional: the manifest is bundled in the archive, so filenames
            // provide useful diagnostics without leaking any sensitive data.
            if (writer.SkippedFiles.Count > 0)
            {
                manifest.Warnings.Add(
                    $"Skipped {writer.SkippedFiles.Count} locked/inaccessible file(s): " +
                    string.Join(", ", writer.SkippedFiles.Select(Path.GetFileName)));
            }

            await RewriteManifestAsync(request.DestinationZipPath, manifest, CancellationToken.None)
                .ConfigureAwait(false);

            // Retention
            if (request.KeepLast > 0)
            {
                ApplyRetention(Path.GetDirectoryName(request.DestinationZipPath)!, request.KeepLast);
            }

            // no trailing period: the message renders inline
            // next to the filename in the Backup page's status row and the
            // center status bar pill; a period after the filename reads as
            // part of the filename (file.zip. — easy to mis-copy).
            return new BackupResult(Succeeded: true,
                Message: $"Backup saved to {Path.GetFileName(request.DestinationZipPath)}",
                Manifest: manifest, ArchivePath: request.DestinationZipPath);
        }
        catch (OperationCanceledException)
        {
            return new BackupResult(Succeeded: false, Message: "Backup cancelled.", Manifest: null, ArchivePath: null);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or OutOfMemoryException)
        {
            // OutOfMemoryException added to the filter.
            // A multi-GB malformed .json file could OOM during
            // JsonRedactor.Redact (which materialises the whole document
            // as a JsonNode tree); without this catch, the OOM would
            // crash the whole backup task instead of producing a graceful
            // BackupResult(Succeeded: false).
            return new BackupResult(Succeeded: false, Message: $"Backup failed: {ex.Message}", Manifest: null,
                ArchivePath: null);
        }
    }

    /// <summary>
    /// Helper for the <see cref="BackupMode.Sanitized"/> writer hook.
    /// Reads <paramref name="sourcePath"/>, routes to the right redactor
    /// based on extension, and returns the redacted content.  On read or
    /// parse failure returns a short JSON-shaped placeholder marking the
    /// redaction-failed state instead of the original bytes — a sanitized
    /// archive must NEVER fall back to copying raw content, because that
    /// would leak the secrets the mode exists to scrub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Extension routing:** files ending in <c>.json</c>
    /// (case-insensitive) go through <see cref="JsonRedactor.Redact"/>
    /// (key-name based walker over a parsed object tree); everything else
    /// goes through <see cref="TextRedactor.Redact"/> (regex-based scan
    /// for known token shapes).  Both surfaces emit
    /// <see cref="JsonRedactor.RedactedMarker"/> as the replacement so the
    /// archive contains a single unified marker string.
    /// </para>
    /// <para>
    /// The placeholder emitted on failure is itself well-formed JSON so
    /// consumers that round-trip the archive through a parser don't choke
    /// on it.  We include only the filename (<c>Path.GetFileName</c>, not
    /// the full path) so support workflows can grep for it without
    /// disclosing user directory structure.
    /// </para>
    /// </remarks>
    internal static string RedactFileForSharing(string sourcePath)
    {
        string content;
        try
        {
            content = File.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked / inaccessible — emit a placeholder.  The original
            // bytes never travel into the sanitized archive.
            _ = ex;
            return BuildSanitizationPlaceholder(sourcePath, "io-failure");
        }

        bool isJson = Path.GetExtension(sourcePath)
                          .Equals(".json", StringComparison.OrdinalIgnoreCase);

        if (isJson)
        {
            try
            {
                return JsonRedactor.Redact(content);
            }
            catch (JsonException)
            {
                // The file's not parseable as JSON — emit a placeholder
                // rather than copying the original (potentially
                // secret-bearing) text.
                return BuildSanitizationPlaceholder(sourcePath, "redaction-failed-malformed-json");
            }
        }

        // Non-JSON: regex-based text scan.  TextRedactor never throws
        // for well-formed strings; it returns content unchanged when
        // no patterns match.  Any unexpected runtime exception
        // (OutOfMemoryException on a gigantic file, regex backtracking
        // timeout) propagates to ZipArchiveWriter's catch filter which
        // skips the entry with SkippedFiles accounting.
        return TextRedactor.Redact(content);
    }

    /// <summary>
    /// Build a JSON-shaped placeholder for a file that couldn't be
    /// safely redacted.  Uses <see cref="JsonSerializer"/> via the
    /// source-generated <see cref="BackupJsonContext"/> so escaping is
    /// always correct (control chars, embedded quotes, etc. replacing the prior hand-rolled escaper that only
    /// handled <c>\\</c> and <c>"</c>).
    /// </summary>
    /// <remarks>
    /// Only the filename (<see cref="Path.GetFileName"/>) is included
    /// in the placeholder — never the absolute path.  Consistent with
    /// the M1 hygiene principle for error messages: errors shouldn't
    /// disclose user directory layout to a support workflow.
    /// </remarks>
    internal static string BuildSanitizationPlaceholder(string sourcePath, string reason)
    {
        string name = Path.GetFileName(sourcePath);
        SanitizationErrorPlaceholder record = new(
            ClaudeForgeSanitizationError: reason,
            File: name);
        return JsonSerializer.Serialize(record,
            BackupJsonContext.Default.SanitizationErrorPlaceholder);
    }

    private static void AddClaudeHome(ZipArchiveWriter writer, BackupRequest request)
    {
        // Walk ~/.claude top-level, deciding per-entry whether to include.
        foreach (string file in Directory.EnumerateFiles(PlatformPaths.ClaudeHome))
        {
            if (ShouldSkipHomeFile(file, request))
            {
                continue;
            }

            writer.AddFile(file, $"ClaudeCode/claude-dir/{Path.GetFileName(file)}");
        }

        foreach (string sub in Directory.EnumerateDirectories(PlatformPaths.ClaudeHome))
        {
            string name = Path.GetFileName(sub);
            if (ShouldSkipHomeSubdir(name, request))
            {
                continue;
            }

            writer.AddDirectory(sub, $"ClaudeCode/claude-dir/{name}");
        }
    }

    private static bool ShouldSkipHomeFile(string filePath, BackupRequest request)
    {
        string name = Path.GetFileName(filePath);
        // Credentials: only include when explicitly opted in.
        // Sanitized backups always drop credentials — the file is opaque
        // bytes (Anthropic / OAuth tokens) that the JsonRedactor would
        // technically "redact" because the file has a .json extension and
        // sensitive-keyed values inside, but skipping it entirely is the
        // safer default for sharing-targeted archives.  Mirror of the
        // IncludedCredentials manifest-flag gating in CreateAsync.
        if (name.Equals(".credentials.json", StringComparison.OrdinalIgnoreCase))
        {
            return !request.IncludeCredentials || request.Mode == BackupMode.Sanitized;
        }

        // Pre-ClaudeForge snapshots: these are outside backup scope — they exist as
        // a separate safety net and should not be included in backup archives.
        if (name.EndsWith(BackupConstants.B4ForgeSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // *.bak sidecars from RestoreEngine (".pre-restore-{stamp}.bak") and
        // any editor-style backup file.  Sibling of the ZipArchiveWriter
        // exclusion that covers nested directories — see that file's
        // EnumerateRecursive for the full rationale.  2026-05-13 user
        // report: prior-restore .bak files dominated backup size and
        // time on a heavily-restored profile.
        if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldSkipHomeSubdir(string dirName, BackupRequest request)
    {
        // Skip our own backup output so backups never nest.
        if (dirName.Equals("backups", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Skip the big projects/ directory unless Full mode was requested.
        if (dirName.Equals("projects", StringComparison.OrdinalIgnoreCase))
        {
            return request.Mode != BackupMode.Full;
        }

        // Skip the schema / app cache — regenerated on demand; not config data.
        if (dirName.Equals("cache", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Skip downloaded update binaries (e.g. claude-2.0.0-win32-x64.exe).
        // These are large, platform-specific, and restored automatically by the updater.
        if (dirName.Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Skip Statsig telemetry/feature-flag data — runtime state regenerated
        // on next launch; never user-authored config.
        if (dirName.Equals("statsig", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Skip shell-command snapshots — ephemeral runtime state, not config data.
        if (dirName.Equals("shell-snapshots", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Skip the Claude Code binary install directory (~/.claude/local/).
        // It may contain the claude / claude.exe binary: large, platform-specific,
        // and reinstalled automatically by the updater on next run.
        if (dirName.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static void AddProjectClaudeData(ZipArchiveWriter writer, string projectRoot, string entryPrefix)
    {
        foreach (string item in new[] { ".claude", ".mcp.json", "CLAUDE.md", "CLAUDE.local.md" })
        {
            string src = Path.Combine(projectRoot, item);
            if (Directory.Exists(src))
            {
                writer.AddDirectory(src, $"{entryPrefix}/{item}");
            }
            else if (File.Exists(src))
            {
                writer.AddFile(src, $"{entryPrefix}/{item}");
            }
        }
    }

    private static async Task RewriteManifestAsync(string zipPath, BackupManifest manifest, CancellationToken ct)
    {
        // Open the zip in Update mode and replace the manifest entry with the final
        // numbers. This keeps manifest.json as the last entry chronologically but
        // updates its content — the rest of the archive is untouched.
        await using FileStream fs = new(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Update);

        ZipArchiveEntry? existing = archive.GetEntry("manifest.json");
        existing?.Delete();

        ZipArchiveEntry updated = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using Stream es = updated.Open();
        string json = JsonSerializer.Serialize(manifest, BackupJsonContext.Default.BackupManifest);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await es.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> CollectSettingsFilesForDiscovery(IReadOnlyList<string> explicitProjects)
    {
        List<string> list =
        [
            PlatformPaths.UserSettingsPath,
            Path.Combine(PlatformPaths.ClaudeHome, "settings.local.json"),
        ];
        foreach (string p in explicitProjects)
        {
            list.Add(PlatformPaths.ProjectSettingsPath(p));
            list.Add(PlatformPaths.LocalSettingsPath(p));
        }

        return list;
    }

    /// <summary>
    /// Returns the union of <paramref name="explicitDirs"/> and
    /// <paramref name="discovered"/>, filtered to entries that currently exist on
    /// disk and de-duplicated via <see cref="Path.GetFullPath(string)"/> +
    /// case-insensitive comparison.
    /// </summary>
    /// <remarks>
    /// Goes through the <see cref="IBackupFileSystem"/> seam so
    /// tests can exercise the dedup contract against an in-memory file system
    /// rather than real temp directories. Internal helper, not part of the
    /// public API; the wrapper version that takes an <see cref="IBackupFileSystem"/>
    /// is callable by tests via <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static IReadOnlyList<string> MergeExplicitAndDiscovered(
        IBackupFileSystem fs,
        IEnumerable<string> explicitDirs,
        IEnumerable<string> discovered)
    {
        SortedSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        foreach (string d in explicitDirs)
        {
            if (fs.DirectoryExists(d))
            {
                set.Add(Path.GetFullPath(d));
            }
        }

        foreach (string d in discovered)
        {
            if (fs.DirectoryExists(d))
            {
                set.Add(Path.GetFullPath(d));
            }
        }

        return set.ToList();
    }

    private IReadOnlyList<string> MergeExplicitAndDiscovered(
        IEnumerable<string> explicitDirs, IEnumerable<string> discovered)
    {
        return MergeExplicitAndDiscovered(_fs, explicitDirs, discovered);
    }

    private static List<string> BuildClientList(BackupRequest r)
    {
        List<string> list = [];
        if (r.IncludeClaudeCode)
        {
            list.Add("ClaudeCode");
        }

        if (r.IncludeClaudeDesktop)
        {
            list.Add("ClaudeDesktop");
        }

        return list;
    }

    private static string GetAppVersion()
    {
        return BackupConstants.AppVersion;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LIST
    // ═══════════════════════════════════════════════════════════════════════

    // per-zip manifest cache. The manifest-parse loop below
    // opens every backup zip and deserialises its manifest.json; with N
    // backups the cost is N file opens + N JSON parses on every List call.
    // Rider monitoring re-flagged List as a hot path after the
    // BackupRestoreViewModel.RebuildBackupList async refactor — moving it
    // off the UI thread didn't reduce the work, only stopped it from
    // freezing the UI. The bulk of the work repeats per call (e.g., when
    // the backup directory is rescanned after a profile switch).
    //
    // Cache key: (path, lastWriteTimeUtc, length). Invalidates automatically
    // when a zip is rewritten (mtime bumps) or grows/shrinks. No external
    // mutation can produce a different manifest with the same key — zip
    // file metadata is the canonical version stamp.
    //
    // Cache value: the parsed manifest, OR null sentinel meaning "we tried
    // and the file is corrupt / unreadable / not a backup". Either way we
    // skip the work next time. The cache is keyed by full path so multiple
    // backup directories don't collide.
    //
    // Memory: each entry is a parsed BackupManifest (small POCO with a few
    // string lists). On a large machine with hundreds of backups this is
    // tens of KB, well within budget. Process-lifetime is fine; explicit
    // invalidation happens on Delete and after CreateAsync via the
    // RewriteManifestAsync path that bumps mtime (so the next List re-reads
    // the rewritten zip naturally).
    private sealed record ManifestCacheEntry(DateTime LastWriteUtc, long Length, BackupManifest? Manifest);

    private static readonly ConcurrentDictionary<string, ManifestCacheEntry> _manifestCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Discards every cached parsed manifest. Tests call this in cleanup;
    /// production code does not need to invoke it because the cache is
    /// keyed on file mtime + length and self-invalidates on rewrite.
    /// </summary>
    public static void InvalidateListCache()
    {
        _manifestCache.Clear();
    }

    /// <summary>
    /// Returns every <c>backup-*.zip</c> file in <paramref name="backupDirectory"/>,
    /// newest first, with parsed manifests (or <see cref="BackupEntry.IsCorrupt"/> when
    /// unreadable). Non-existent directory → empty list. Per-zip manifest parses are
    /// memoised; see <see cref="InvalidateListCache"/>.
    /// </summary>
    public IReadOnlyList<BackupEntry> List(string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
        {
            return [];
        }

        string currentPlatform = PlatformPaths.PlatformId;
        List<BackupEntry> results = [];

        foreach (string file in Directory.EnumerateFiles(backupDirectory, "backup-*.zip", SearchOption.TopDirectoryOnly))
        {
            FileInfo info = new(file);
            BackupManifest? manifest = GetCachedOrParseManifest(file, info.LastWriteTimeUtc, info.Length);

            results.Add(new BackupEntry
            {
                ArchivePath = file,
                FileName = info.Name,
                SizeBytes = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc,
                Manifest = manifest,
                IsCrossPlatform = manifest != null &&
                                  !string.Equals(manifest.Platform, currentPlatform, StringComparison.Ordinal),
            });
        }

        results.Sort((a, b) => b.LastModifiedUtc.CompareTo(a.LastModifiedUtc));
        return results;
    }

    /// <summary>
    /// Parses a single backup archive at <paramref name="archivePath"/> and returns a
    /// <see cref="BackupEntry"/> describing it, or <c>null</c> if the file does not exist.
    /// The returned entry's <see cref="BackupEntry.IsCorrupt"/> is <c>true</c> when the zip
    /// is unreadable / has no valid manifest / is the wrong <c>Kind</c> — the caller is
    /// responsible for refusing to act on a corrupt entry.  Used by the drag-drop "restore
    /// from this specific zip" path (W8) where the file may live outside the user's
    /// configured restore directory and therefore would not be discovered by
    /// <see cref="List(string)"/>.
    /// </summary>
    /// <remarks>
    /// Shares the same parse + cache pipeline as <see cref="List(string)"/> so a zip read
    /// once from a dropped path and again from a restore-folder enumeration hits the
    /// memoised manifest the second time.
    /// </remarks>
    public BackupEntry? TryReadEntry(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        BackupManifest? manifest = GetCachedOrParseManifest(info.FullName, info.LastWriteTimeUtc, info.Length);
        string currentPlatform = PlatformPaths.PlatformId;

        return new BackupEntry
        {
            ArchivePath = info.FullName,
            FileName = info.Name,
            SizeBytes = info.Length,
            LastModifiedUtc = info.LastWriteTimeUtc,
            Manifest = manifest,
            IsCrossPlatform = manifest != null &&
                              !string.Equals(manifest.Platform, currentPlatform, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Returns the cached manifest for <paramref name="file"/> when the cache
    /// entry's mtime + length match the live file; otherwise re-opens the zip
    /// and parses manifest.json, storing the result (success OR null) for next
    /// time.
    /// </summary>
    private static BackupManifest? GetCachedOrParseManifest(string file, DateTime lastWriteUtc, long length)
    {
        if (_manifestCache.TryGetValue(file, out ManifestCacheEntry? cached)
            && cached.LastWriteUtc == lastWriteUtc
            && cached.Length == length)
        {
            return cached.Manifest;
        }

        BackupManifest? manifest = null;
        try
        {
            using FileStream fs = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive archive = new(fs, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.GetEntry("manifest.json");
            if (entry != null)
            {
                using Stream es = entry.Open();
                manifest = JsonSerializer.Deserialize(es, BackupJsonContext.Default.BackupManifest);
                // Reject manifests from unknown future schema versions.
                if (manifest != null && manifest.SchemaVersion > BackupManifest.CurrentSchemaVersion)
                {
                    manifest = null;
                }

                // Reject anything that isn't a "backup" (e.g. exports) so they
                // cannot be listed as restorable.
                if (manifest != null && !string.Equals(manifest.Kind, "backup", StringComparison.Ordinal))
                {
                    manifest = null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or JsonException)
        {
            manifest = null;
        }

        _manifestCache[file] = new ManifestCacheEntry(lastWriteUtc, length, manifest);
        return manifest;
    }

    /// <summary>Delete a backup archive. Returns <c>true</c> if the file was removed.</summary>
    public bool Delete(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            if (File.Exists(entry.ArchivePath))
            {
                File.Delete(entry.ArchivePath);
            }

            // Drop the cache entry — even though a future zip with the same path
            // would have a different mtime/length and self-invalidate, leaving a
            // stale entry around for a deleted file wastes memory across long
            // sessions where the user creates and deletes many backups.
            _manifestCache.TryRemove(entry.ArchivePath, out ManifestCacheEntry? _);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Trims older <c>backup-*.zip</c> archives in <paramref name="backupDirectory"/>
    /// so that at most <paramref name="keepLast"/> survive (newest first by last-write
    /// time). Best-effort — locked or otherwise unreachable files are skipped.
    /// </summary>
    /// <remarks>
    /// Goes through the <see cref="IBackupFileSystem"/> seam so the
    /// retention contract is testable without real disk I/O. <c>internal</c> so the
    /// test project can invoke it directly via <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static void ApplyRetention(IBackupFileSystem fs, string backupDirectory, int keepLast)
    {
        if (keepLast <= 0)
        {
            return;
        }

        if (!fs.DirectoryExists(backupDirectory))
        {
            return;
        }

        List<string> oldFiles = fs.EnumerateFiles(backupDirectory, "backup-*.zip", SearchOption.TopDirectoryOnly)
                                  .Select(path => (Path: path, LastWriteTimeUtc: fs.GetLastWriteTimeUtc(path)))
                                  .OrderByDescending(t => t.LastWriteTimeUtc)
                                  .Skip(keepLast)
                                  .Select(t => t.Path)
                                  .ToList();

        foreach (string oldPath in oldFiles)
        {
            try
            {
                fs.DeleteFile(oldPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort; skip if locked or unreachable.
                _ = ex;
            }
        }
    }

    private void ApplyRetention(string backupDirectory, int keepLast)
    {
        ApplyRetention(_fs, backupDirectory, keepLast);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RESTORE  (delegated to RestoreEngine)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts <paramref name="entry"/> into the real Claude paths. Existing
    /// files are moved aside with a <c>.pre-restore-{stamp}.bak</c> suffix
    /// before being overwritten. Returns a result summary.
    /// </summary>
    /// <remarks>
    /// the restore implementation lives in
    /// <see cref="RestoreEngine"/>; this method is a stable public-surface
    /// passthrough so existing consumers (notably <c>BackupClient</c>) keep
    /// compiling unchanged.
    /// </remarks>
    public Task<RestoreResult> RestoreAsync(
        BackupEntry entry,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        return RestoreEngine.RestoreAsync(entry, progress, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCHEMA BUNDLING & POST-RESTORE VALIDATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Copies every embedded schema resource into the archive under <c>Schemas/</c>.
    /// Called during <see cref="CreateAsync"/> so that the archive carries the
    /// exact schema version that was current when the backup was made.
    /// </summary>
    private static void BundleSchemas(ZipArchiveWriter writer)
    {
        Assembly assembly = typeof(SchemaRegistry).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            // Only include resources under ClaudeForge.Core.Assets.Schemas.*
            const string prefix = "Bennewitz.Ninja.ClaudeForge.Core.Assets.Schemas.";
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string fileName = resourceName[prefix.Length..];
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                continue;
            }

            using StreamReader reader = new(stream, Encoding.UTF8);
            writer.AddTextEntry($"Schemas/{fileName}", reader.ReadToEnd());
        }
    }
}

/// <summary>Outcome of <see cref="BackupEngine.CreateAsync"/>.</summary>
public sealed record BackupResult(bool Succeeded, string Message, BackupManifest? Manifest, string? ArchivePath);

/// <summary>Outcome of <see cref="BackupEngine.RestoreAsync"/>.</summary>
public sealed record RestoreResult(
    bool Succeeded,
    string Message,
    int ItemsRestored,
    IReadOnlyList<string>? ValidationWarnings = null,
    IReadOnlyList<string>? FileFailures = null);