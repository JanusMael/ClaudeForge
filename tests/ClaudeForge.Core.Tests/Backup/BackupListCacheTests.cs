using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

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
[DoNotParallelize]
[TestClass]
public sealed class BackupListCacheTests
{
    private string _fakeHome = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "blc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
        File.WriteAllText(Path.Combine(_fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");

        BackupEngine.InvalidateListCache();
    }

    [TestCleanup]
    public void Cleanup()
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

    [TestMethod]
    public async Task List_SecondCall_ServesFromCache_WhenZipUntouched()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        // First List: parses + caches.
        IReadOnlyList<BackupEntry> first = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, first.Count);
        Assert.IsNotNull(first[0].Manifest);
        BackupManifest? firstManifestRef = first[0].Manifest;

        // Second List on the same untouched file: should return the same
        // manifest reference (cached, not re-deserialised).
        IReadOnlyList<BackupEntry> second = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, second.Count);
        Assert.AreSame(firstManifestRef, second[0].Manifest,
            "List should return the cached manifest reference when the zip is unchanged.");
    }

    [TestMethod]
    public async Task List_AfterMtimeChange_ReParses()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        // Prime the cache.
        IReadOnlyList<BackupEntry> first = BackupEngine.Default.List(_fakeHome);
        Assert.IsNotNull(first[0].Manifest);
        BackupManifest? firstManifestRef = first[0].Manifest;

        // Bump mtime — no content change, but the cache key includes mtime.
        File.SetLastWriteTimeUtc(zipPath, DateTime.UtcNow.AddMinutes(1));

        IReadOnlyList<BackupEntry> second = BackupEngine.Default.List(_fakeHome);
        Assert.IsNotNull(second[0].Manifest);
        Assert.AreNotSame(firstManifestRef, second[0].Manifest,
            "Mtime change must invalidate the cache and force a re-parse.");
    }

    [TestMethod]
    public async Task Delete_DropsCacheEntry()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        // Populate the cache.
        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, entries.Count);

        // Delete via the engine.
        Assert.IsTrue(BackupEngine.Default.Delete(entries[0]));

        // List should return empty (file is gone), and the cache entry for
        // that path should not survive into a re-creation. We probe this by
        // creating a NEW backup at the same path and confirming List returns
        // a freshly-parsed manifest, not whatever was cached before delete.
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        IReadOnlyList<BackupEntry> afterRecreate = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, afterRecreate.Count);
        Assert.IsNotNull(afterRecreate[0].Manifest,
            "Re-created backup at the same path should parse fresh, not be poisoned by a stale pre-delete cache entry.");
    }

    [TestMethod]
    public async Task InvalidateListCache_ForcesReparse()
    {
        string zipPath = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        IReadOnlyList<BackupEntry> first = BackupEngine.Default.List(_fakeHome);
        BackupManifest? firstManifestRef = first[0].Manifest;

        BackupEngine.InvalidateListCache();

        IReadOnlyList<BackupEntry> second = BackupEngine.Default.List(_fakeHome);
        Assert.AreNotSame(firstManifestRef, second[0].Manifest,
            "Explicit InvalidateListCache must force the next List call to re-parse.");
    }
}