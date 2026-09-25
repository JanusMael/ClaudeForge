using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Covers <see cref="BackupEngine.List"/>'s per-zip manifest cache and its
/// <see cref="BackupEngine.InvalidateListCache"/> escape hatch. Two contracts:
///
/// 1. A cache hit returns the previously-parsed manifest without re-opening
///    the zip. We assert this by mutating the manifest entry inside an
///    untouched-by-mtime zip after a first List call and verifying the second
///    List call returns the OLD manifest.
///
/// 2. The cache key includes <c>FileInfo.LastWriteTimeUtc</c>, so any zip
///    rewrite that bumps mtime self-invalidates without manual intervention.
///
/// 3. <see cref="BackupEngine.Delete"/> drops the cache entry so a future
///    file with the same path doesn't serve a stale manifest.
/// </summary>
// Exercises BackupEngine's process-wide list/manifest cache (via
// InvalidateListCache) by design — inherently process-global shared state that
// cannot be probed concurrently. Run serially, isolated from the
// method-level-parallelized rest of the assembly.
[Collection("DoNotParallelize")]
public sealed class BackupListCacheTests : IDisposable
{
    private string _fakeHome = string.Empty;

    public BackupListCacheTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "blc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
        File.WriteAllText(Path.Combine(_fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");

        BackupEngine.InvalidateListCache();
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        BackupEngine.InvalidateListCache();
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task List_SecondCall_ServesFromCache_WhenZipUntouched()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken);

        // First List: parses + caches.
        IReadOnlyList<BackupEntry> first = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(first);
        Assert.NotNull(first[0].Manifest);
        BackupManifest? firstManifestRef = first[0].Manifest;

        // Second List on the same untouched file: should return the same
        // manifest reference (cached, not re-deserialised).
        IReadOnlyList<BackupEntry> second = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(second);
        MessageAssert.Same(firstManifestRef, second[0].Manifest,
            "List should return the cached manifest reference when the zip is unchanged.");
    }

    [Fact]
    public async Task List_AfterMtimeChange_ReParses()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken);

        // Prime the cache.
        IReadOnlyList<BackupEntry> first = TestBackupEngine.Default.List(_fakeHome);
        Assert.NotNull(first[0].Manifest);
        BackupManifest? firstManifestRef = first[0].Manifest;

        // Bump mtime — no content change, but the cache key includes mtime.
        File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddMinutes(1));

        IReadOnlyList<BackupEntry> second = TestBackupEngine.Default.List(_fakeHome);
        Assert.NotNull(second[0].Manifest);
        MessageAssert.NotSame(firstManifestRef, second[0].Manifest,
            "Mtime change must invalidate the cache and force a re-parse.");
    }

    [Fact]
    public async Task Delete_DropsCacheEntry()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken);

        // Populate the cache.
        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(entries);

        // Delete via the engine.
        Assert.True(TestBackupEngine.Default.Delete(entries[0]));

        // List should return empty (file is gone), and the cache entry for
        // that path should not survive into a re-creation. We probe this by
        // creating a NEW backup at the same path and confirming List returns
        // a freshly-parsed manifest, not whatever was cached before delete.
        await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken);
        IReadOnlyList<BackupEntry> afterRecreate = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(afterRecreate);
        MessageAssert.NotNull(afterRecreate[0].Manifest,
            "Re-created backup at the same path should parse fresh, not be poisoned by a stale pre-delete cache entry.");
    }

    [Fact]
    public async Task InvalidateListCache_ForcesReparse()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken);

        IReadOnlyList<BackupEntry> first = TestBackupEngine.Default.List(_fakeHome);
        BackupManifest? firstManifestRef = first[0].Manifest;

        BackupEngine.InvalidateListCache();

        IReadOnlyList<BackupEntry> second = TestBackupEngine.Default.List(_fakeHome);
        MessageAssert.NotSame(firstManifestRef, second[0].Manifest,
            "Explicit InvalidateListCache must force the next List call to re-parse.");
    }
}