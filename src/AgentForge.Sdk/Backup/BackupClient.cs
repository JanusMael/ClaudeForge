using System.Globalization;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using CoreBackup = Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Sdk.Backup;

/// <summary>
/// Default <see cref="IBackupClient"/> implementation. Bridges the SDK's
/// public surface to <see cref="Bennewitz.Ninja.AgentForge.Core.Backup.BackupEngine"/>:
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
/// product(s) the underlying Core engine should include. Each concrete client passes
/// its own product descriptor, so the client no longer restates its identity as a pair
/// of booleans the other product also has to answer. Cross-product unified
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
    private readonly IReadOnlyList<ProductDescriptor> _products;

    /// <summary>
    /// Construct a backup client that produces archives covering the requested
    /// product set. Pass <see cref="CoreBackup.BackupEngine.Default"/> for the
    /// production engine; tests may pass a custom <see cref="CoreBackup.BackupEngine"/>
    /// constructed with stub collaborators.
    /// </summary>
    /// <param name="engine">The engine that writes archives.</param>
    /// <param name="products">
    /// Products this client's archives cover. Replaced a
    /// <c>(bool includeClaudeCode, bool includeClaudeDesktop)</c> pair, which meant a client
    /// could only ever describe one of two products — and which every concrete client
    /// answered by restating its own identity as two literals. A client now passes its own
    /// <c>Product</c> descriptor instead.
    /// </param>
    public BackupClient(
        BackupEngine engine,
        IReadOnlyList<ProductDescriptor> products)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _products = products ?? throw new ArgumentNullException(nameof(products));
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

        CoreBackup.BackupRequest coreReq = new()
        {
            DestinationZipPath = destPath,
            Mode = request.Mode,
            Products = _products,
            IncludeCredentials = request.IncludeCredentials,
            ExplicitProjectDirs = request.ExplicitProjectDirs ?? Array.Empty<string>(),
            KeepLast = request.KeepLast,
        };

        IProgress<CoreBackup.BackupProgress>? coreProgress = WrapProgress(onProgress);
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

        IProgress<CoreBackup.BackupProgress>? coreProgress = WrapProgress(onProgress);
        CoreBackup.RestoreResult
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

    private static IProgress<CoreBackup.BackupProgress>? WrapProgress(BackupProgressHandler? onProgress)
    {
        if (onProgress is null)
        {
            return null;
        }

        return new Progress<CoreBackup.BackupProgress>(p =>
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

    private static BackupManifest ProjectManifest(CoreBackup.BackupManifest core)
    {
        return new BackupManifest(
            Kind: core.Kind,
            SchemaVersion: core.SchemaVersion,
            CreatedUtc: core.CreatedUtc,
            Platform: core.Platform,
            AppVersion: core.AppVersion,
            Mode: core.Mode,
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

    private static CoreBackup.BackupManifest ProjectManifestToCore(BackupManifest sdk)
    {
        return new CoreBackup.BackupManifest
        {
            Kind = sdk.Kind,
            SchemaVersion = sdk.SchemaVersion,
            CreatedUtc = sdk.CreatedUtc,
            Platform = sdk.Platform,
            AppVersion = sdk.AppVersion,
            Mode = sdk.Mode,
            Clients = sdk.Clients.ToList(),
            Projects = sdk.Projects.ToList(),
            Worktrees = sdk.Worktrees
                           .Select(w => new CoreBackup.BackupWorktreeEntry
                               { ProjectRoot = w.ProjectRoot, WorktreePath = w.WorktreePath })
                           .ToList(),
            IncludedCredentials = sdk.IncludedCredentials,
            SizeBytes = sdk.SizeBytes,
            ItemCount = sdk.ItemCount,
            Warnings = sdk.Warnings.ToList(),
        };
    }
}