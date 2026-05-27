using System.Globalization;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Default <see cref="IBackupClient"/> implementation. Bridges the SDK's
/// public surface to <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.BackupEngine"/>:
///
/// <list type="bullet">
///   <item>Projects SDK <see cref="BackupRequest"/> / <see cref="BackupArchive"/>
///         / <see cref="BackupProgress"/> to and from their Core counterparts.</item>
///   <item>Bridges the SDK's async <see cref="BackupProgressHandler"/> to Core's
///         synchronous <see cref="IProgress{T}"/>.</item>
///   <item>Composes the destination zip filename
///         (<c>backup[-with-creds]-yyyyMMdd-HHmmss.zip</c>) from the consumer's
///         output directory.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Per-product: a <see cref="BackupClient"/> instance is configured with which
/// product(s) the underlying Core engine should include. <see cref="ClaudeCodeClient"/>
/// constructs one with <c>includeClaudeCode=true, includeClaudeDesktop=false</c>;
/// <see cref="ClaudeDesktopClient"/> does the inverse. Cross-product unified
/// backups (the existing GUI's behaviour) are out of scope for v1 — the GUI swap
/// in 4.3.7 either invokes both clients in sequence or wraps them in a
/// higher-level orchestrator added later.
/// </para>
/// <para>
/// <b>Progress back-pressure.</b> Core's engine reports progress via
/// <see cref="IProgress{T}.Report"/>, which is synchronous and fire-and-forget.
/// The SDK's async <see cref="BackupProgressHandler"/> is invoked from inside
/// the wrapped <see cref="Progress{T}"/> callback as a fire-and-forget
/// <see cref="Task"/>; the producer does NOT await it. This honours the
/// SDK contract's "handlers can do real async work" intent — the handler is
/// free to await — but does not slow the producer when the handler is slow.
/// A future iteration may add a buffered async pump for true back-pressure;
/// for the v1 contract this trade-off is documented and acceptable.
/// </para>
/// </remarks>
internal sealed class BackupClient : IBackupClient
{
    private readonly BackupEngine _engine;
    private readonly bool _includeClaudeCode;
    private readonly bool _includeClaudeDesktop;

    /// <summary>
    /// Construct a backup client that produces archives covering the requested
    /// product set. Pass <see cref="Core.Backup.BackupEngine.Default"/> for the
    /// production engine; tests may pass a custom <see cref="Core.Backup.BackupEngine"/>
    /// constructed with stub collaborators.
    /// </summary>
    public BackupClient(
        BackupEngine engine,
        bool includeClaudeCode,
        bool includeClaudeDesktop)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _includeClaudeCode = includeClaudeCode;
        _includeClaudeDesktop = includeClaudeDesktop;
    }

    public async Task<BackupArchive> CreateAsync(
        BackupRequest request,
        BackupProgressHandler? onProgress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.OutputDirectory))
        {
            throw new ArgumentException("OutputDirectory must be a non-empty path.", nameof(request));
        }

        Directory.CreateDirectory(request.OutputDirectory);

        // Compose the destination filename. Mirrors the existing GUI flow so
        // archives produced via the SDK look identical on disk and integrate
        // with the existing Restore list filter ("backup-*.zip").
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string prefix = request.IncludeCredentials ? "backup-with-creds" : "backup";
        string destPath = Path.Combine(request.OutputDirectory, $"{prefix}-{stamp}.zip");

        Core.Backup.BackupRequest coreReq = new()
        {
            DestinationZipPath = destPath,
            Mode = ToCoreMode(request.Mode),
            IncludeClaudeCode = _includeClaudeCode,
            IncludeClaudeDesktop = _includeClaudeDesktop,
            IncludeCredentials = request.IncludeCredentials,
            ExplicitProjectDirs = request.ExplicitProjectDirs ?? Array.Empty<string>(),
            KeepLast = request.KeepLast,
        };

        IProgress<Core.Backup.BackupProgress>? coreProgress = WrapProgress(onProgress);
        BackupResult result = await _engine.CreateAsync(coreReq, coreProgress, ct).ConfigureAwait(false);

        if (!result.Succeeded || result.Manifest is null || result.ArchivePath is null)
        {
            throw new InvalidOperationException(
                $"Backup failed: {result.Message}");
        }

        return new BackupArchive(
            FilePath: result.ArchivePath,
            CreatedUtc: new DateTimeOffset(File.GetLastWriteTimeUtc(result.ArchivePath), TimeSpan.Zero),
            Manifest: ProjectManifest(result.Manifest));
    }

    public Task<IReadOnlyList<BackupArchive>> ListAsync(string directory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ct.ThrowIfCancellationRequested();

        // Core.List is synchronous (a directory scan). Run it on the calling
        // thread; for an SSD with a typical ~10-50 archive history this is
        // microseconds. Wrapping in Task.Run would just add ceremony.
        IReadOnlyList<BackupEntry> entries = _engine.List(directory);
        IReadOnlyList<BackupArchive> projected = entries
                                                 .Where(e => !e.IsCorrupt && e.Manifest is not null)
                                                 .Select(e => new BackupArchive(
                                                     FilePath: e.ArchivePath,
                                                     CreatedUtc: new DateTimeOffset(e.LastModifiedUtc, TimeSpan.Zero),
                                                     Manifest: ProjectManifest(e.Manifest!)))
                                                 .ToList();

        return Task.FromResult(projected);
    }

    public async Task<RestoreResult> RestoreAsync(
        BackupArchive archive,
        BackupProgressHandler? onProgress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);

        BackupEntry coreEntry = new()
        {
            ArchivePath = archive.FilePath,
            FileName = Path.GetFileName(archive.FilePath),
            SizeBytes = File.Exists(archive.FilePath) ? new FileInfo(archive.FilePath).Length : 0,
            LastModifiedUtc = archive.CreatedUtc.UtcDateTime,
            Manifest = ProjectManifestToCore(archive.Manifest),
            IsCrossPlatform = !string.Equals(archive.Manifest.Platform, PlatformPaths.PlatformId,
                StringComparison.OrdinalIgnoreCase),
        };

        IProgress<Core.Backup.BackupProgress>? coreProgress = WrapProgress(onProgress);
        Core.Backup.RestoreResult
            result = await _engine.RestoreAsync(coreEntry, coreProgress, ct).ConfigureAwait(false);

        return new RestoreResult(
            Success: result.Succeeded,
            Message: result.Message,
            FilesRestored: result.ItemsRestored,
            // Core's engine doesn't expose a "skipped" list yet; surface an
            // empty placeholder. A future Core change can fill this in
            // without breaking the SDK contract since the field already exists.
            Skipped: Array.Empty<string>(),
            Failures: result.FileFailures ?? Array.Empty<string>());
    }

    // ── Bridges ──────────────────────────────────────────────────────────

    private static IProgress<Core.Backup.BackupProgress>? WrapProgress(BackupProgressHandler? onProgress)
    {
        if (onProgress is null)
        {
            return null;
        }

        return new Progress<Core.Backup.BackupProgress>(p =>
        {
            // Fire-and-forget per the back-pressure note above. Discard the
            // returned ValueTask so the C# compiler doesn't warn about the
            // unobserved task; exceptions thrown by the async handler will be
            // surfaced through the Task scheduler's UnobservedTaskException
            // event, which the GUI already wires up via App.axaml.cs.
            _ = onProgress(new BackupProgress(
                Step: p.Current,
                Total: p.Total,
                Message: p.CurrentItem,
                BytesWritten: p.BytesDone)).AsTask();
        });
    }

    private static Core.Backup.BackupMode ToCoreMode(BackupMode mode)
    {
        return mode switch
        {
            BackupMode.SettingsOnly => Core.Backup.BackupMode.SettingsOnly,
            BackupMode.Full => Core.Backup.BackupMode.Full,
            var _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BackupMode"),
        };
    }

    private static BackupMode ToSdkMode(Core.Backup.BackupMode mode)
    {
        return mode switch
        {
            Core.Backup.BackupMode.SettingsOnly => BackupMode.SettingsOnly,
            Core.Backup.BackupMode.Full => BackupMode.Full,
            var _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Core BackupMode"),
        };
    }

    private static BackupManifest ProjectManifest(Core.Backup.BackupManifest core)
    {
        return new BackupManifest(
            Kind: core.Kind,
            SchemaVersion: core.SchemaVersion,
            CreatedUtc: core.CreatedUtc,
            Platform: core.Platform,
            AppVersion: core.AppVersion,
            Mode: ToSdkMode(core.Mode),
            Clients: core.Clients.ToList(),
            Projects: core.Projects.ToList(),
            Worktrees: core.Worktrees
                           .Select(w => new BackupWorktreeEntry(w.ProjectRoot, w.WorktreePath))
                           .ToList(),
            IncludedCredentials: core.IncludedCredentials,
            SizeBytes: core.SizeBytes,
            ItemCount: core.ItemCount,
            Warnings: core.Warnings.ToList());
    }

    private static Core.Backup.BackupManifest ProjectManifestToCore(BackupManifest sdk)
    {
        return new Core.Backup.BackupManifest
        {
            Kind = sdk.Kind,
            SchemaVersion = sdk.SchemaVersion,
            CreatedUtc = sdk.CreatedUtc,
            Platform = sdk.Platform,
            AppVersion = sdk.AppVersion,
            Mode = ToCoreMode(sdk.Mode),
            Clients = sdk.Clients.ToList(),
            Projects = sdk.Projects.ToList(),
            Worktrees = sdk.Worktrees
                           .Select(w => new Core.Backup.BackupWorktreeEntry
                               { ProjectRoot = w.ProjectRoot, WorktreePath = w.WorktreePath })
                           .ToList(),
            IncludedCredentials = sdk.IncludedCredentials,
            SizeBytes = sdk.SizeBytes,
            ItemCount = sdk.ItemCount,
            Warnings = sdk.Warnings.ToList(),
        };
    }
}