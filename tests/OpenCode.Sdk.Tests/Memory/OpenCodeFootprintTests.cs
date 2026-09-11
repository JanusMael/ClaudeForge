using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Memory;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Memory;

/// <summary>
/// OpenCode's footprint catalog — the second product to supply one, and the first whose categories
/// span more than one root.
/// </summary>
/// <remarks>
/// These assertions encode measurements from <c>docs/opencode-install-probe.json</c> plus what a
/// later inspection found that the probe did not. They are what stops the category list drifting
/// back towards the documented-but-wrong one it replaced.
/// </remarks>
[TestClass]
public sealed class OpenCodeFootprintTests
{
    private string _home = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(Path.GetTempPath(), "ocf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        PlatformPaths.TestUserProfileOverride = _home;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public void TheCatalog_IsOrderedMostDisposableFirst_NotBySize()
    {
        // ⛔⛔ The prune order and the backup order are OPPOSITE. node_modules is the largest item
        // by an order of magnitude AND the most disposable; the database is the smallest meaningful
        // one AND the only irreplaceable one. A size sort puts them the wrong way round and invites
        // the wrong click, so the ORDER is guidance rather than presentation.
        string[] expected =
            ["node-modules", "download-temps", "model-catalog", "logs", "locks", "session-database"];

        CollectionAssert.AreEqual(
            expected,
            OpenCodeFootprint.Catalog.All.Select(c => c.Id).ToArray());

        Assert.AreEqual("session-database", OpenCodeFootprint.Catalog.All[^1].Id,
            "The irreplaceable category must be last, never first.");
    }

    [TestMethod]
    public void NoCategory_ClaimsToBeInAStandardBackup()
    {
        // ⛔ Every one is false, and the database's reason is the one that would mislead: it is
        // archived only behind an explicit credential opt-in and never when sharing, so a user who
        // took an ordinary backup does NOT have their session history. A page saying otherwise
        // would send someone to delete it believing it was safe.
        foreach (FootprintCategory category in OpenCodeFootprint.Catalog.All)
        {
            Assert.IsFalse(category.IsInStandardBackup,
                $"{category.Id} claims a standard backup preserves it. None of OpenCode's does.");
        }
    }

    [TestMethod]
    public void TheCategoriesSpanAllFourRoots()
    {
        // The reason FootprintSource carries a root KEY rather than an absolute path: Claude's
        // footprint is one tree, OpenCode's is four, and the largest item sits under a different
        // root from the only irreplaceable one.
        string[] roots =
        [
            .. OpenCodeFootprint.Catalog.All
                .SelectMany(c => c.Definition.Sources)
                .Select(s => s.RootKey)
                .Distinct()
                .Order(StringComparer.Ordinal)
        ];

        CollectionAssert.AreEqual(new[] { "cache", "config", "data", "state" }, roots);
    }

    [TestMethod]
    public void TheDatabaseCategory_CoversAllThreeFiles()
    {
        // A copy of opencode.db without its -wal is a stale snapshot by construction, so a
        // footprint row that reported only the .db would understate what deleting it destroys.
        string[] paths =
        [
            .. OpenCodeFootprint.Catalog.TryGetById("session-database")!.Value.Definition.Sources
                .Select(s => s.RelativePath)
        ];

        CollectionAssert.AreEquivalent(
            new[] { "opencode.db", "opencode.db-wal", "opencode.db-shm" }, paths);
    }

    [TestMethod]
    public async Task DownloadTemps_FindTheOrphanBesideTheCompleteFile()
    {
        // ⭐ The finding nothing on the original list covered: an interrupted download leaves a
        // FULL-SIZE orphan next to the finished file, and nothing cleans it up — so the cache
        // silently holds two copies of a ~4.6 MB file for one useful one. A page keyed on the name
        // "models.json" misses it entirely.
        string cache = OpenCodePaths.CacheDirectory();
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "models.json"), new string('m', 400));
        await File.WriteAllTextAsync(Path.Combine(cache, "models.json.30476.1789135661679.tmp"), new string('o', 900));

        FootprintService service = new(catalog: OpenCodeFootprint.Catalog, roots: OpenCodeFootprint.Roots);
        IReadOnlyList<FootprintCategoryStats> stats = await service.GetStatsAsync(CancellationToken.None);

        FootprintCategoryStats temps = stats.Single(s => s.Category.Id == "download-temps");
        Assert.AreEqual(1, temps.FileCount, "The orphaned .tmp must be found.");
        Assert.AreEqual(900, temps.TotalBytes);

        FootprintCategoryStats catalogFile = stats.Single(s => s.Category.Id == "model-catalog");
        Assert.AreEqual(400, catalogFile.TotalBytes,
            "…and must NOT be double-counted against the complete file.");
    }

    [TestMethod]
    public async Task TheWalkReadsTheProfileCurrentAtCallTime()
    {
        // The AsyncLocal-override lesson, applied to a catalog whose roots come from a factory.
        string config = OpenCodePaths.DefaultGlobalDirectory();
        Directory.CreateDirectory(Path.Combine(config, "node_modules", "pkg"));
        await File.WriteAllTextAsync(Path.Combine(config, "node_modules", "pkg", "index.js"), "x");

        FootprintService service = new(catalog: OpenCodeFootprint.Catalog, roots: OpenCodeFootprint.Roots);
        IReadOnlyList<FootprintCategoryStats> stats = await service.GetStatsAsync(CancellationToken.None);

        Assert.AreEqual(1, stats.Single(s => s.Category.Id == "node-modules").FileCount);
    }

    [TestMethod]
    public async Task AMissingRoot_ReportsZero_RatherThanThrowing()
    {
        // A fresh install has none of these directories. GetStatsAsync walks every category in one
        // pass, so a throw on an absent root would take out the whole page.
        FootprintService service = new(catalog: OpenCodeFootprint.Catalog, roots: OpenCodeFootprint.Roots);
        IReadOnlyList<FootprintCategoryStats> stats = await service.GetStatsAsync(CancellationToken.None);

        Assert.AreEqual(OpenCodeFootprint.Catalog.Count, stats.Count);
        Assert.IsTrue(stats.All(s => s.FileCount == 0 && s.TotalBytes == 0));
    }
}
