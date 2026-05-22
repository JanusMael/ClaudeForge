using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="BackupEngine"/> internals routed through the
/// <see cref="IBackupFileSystem"/> seam. Replaces the previous
/// pattern of constructing real temp directories and zip archives for every
/// retention / discovery assertion.
/// </summary>
[TestClass]
public class BackupEngineSeamTests
{
    private const string BackupDir = "/backups";

    // ── ApplyRetention ────────────────────────────────────────────────────────

    [TestMethod]
    public void ApplyRetention_KeepsNewestN_DeletesOlderFiles()
    {
        // Arrange: 5 archives, written across 5 distinct days. Newest first by
        // last-write time should be: e, d, c, b, a.
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory(BackupDir);
        fs.AddFile($"{BackupDir}/backup-a.zip", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-b.zip", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-c.zip", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-d.zip", new DateTime(2025, 1, 4, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-e.zip", new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        // Act: keep the 3 newest.
        BackupEngine.ApplyRetention(fs, BackupDir, keepLast: 3);

        // Assert: c, d, e survive; a, b are gone (oldest).
        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-c.zip"), "Newest 3 must survive");
        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-d.zip"), "Newest 3 must survive");
        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-e.zip"), "Newest 3 must survive");
        Assert.IsFalse(fs.FileExists($"{BackupDir}/backup-a.zip"), "Oldest must be deleted");
        Assert.IsFalse(fs.FileExists($"{BackupDir}/backup-b.zip"), "Oldest must be deleted");

        // The seam recorded both deletions.
        CollectionAssert.AreEquivalent(
            new[] { $"{BackupDir}/backup-a.zip", $"{BackupDir}/backup-b.zip" }
                .Select(p => p.Replace('/', Path.DirectorySeparatorChar)).ToList(),
            fs.DeletedPaths.ToList());
    }

    [TestMethod]
    public void ApplyRetention_KeepLastZero_IsNoOp()
    {
        // Defensive contract: keepLast <= 0 short-circuits — never wipe everything.
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory(BackupDir);
        fs.AddFile($"{BackupDir}/backup-only.zip", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        BackupEngine.ApplyRetention(fs, BackupDir, keepLast: 0);

        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-only.zip"));
        Assert.AreEqual(0, fs.DeletedPaths.Count);
    }

    [TestMethod]
    public void ApplyRetention_MissingDirectory_IsNoOp()
    {
        // No directory → nothing to do; must not throw.
        InMemoryBackupFileSystem fs = new();
        BackupEngine.ApplyRetention(fs, "/does/not/exist", keepLast: 3);
        Assert.AreEqual(0, fs.DeletedPaths.Count);
    }

    [TestMethod]
    public void ApplyRetention_FewerFilesThanKeep_KeepsAll()
    {
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory(BackupDir);
        fs.AddFile($"{BackupDir}/backup-a.zip", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-b.zip", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        BackupEngine.ApplyRetention(fs, BackupDir, keepLast: 5);

        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-a.zip"));
        Assert.IsTrue(fs.FileExists($"{BackupDir}/backup-b.zip"));
        Assert.AreEqual(0, fs.DeletedPaths.Count);
    }

    // ── MergeExplicitAndDiscovered ────────────────────────────────────────────

    [TestMethod]
    public void MergeExplicitAndDiscovered_FiltersOutMissingDirectories()
    {
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory("/projects/real");
        // /projects/missing is intentionally not added.

        IReadOnlyList<string> merged = BackupEngine.MergeExplicitAndDiscovered(
            fs,
            explicitDirs: ["/projects/real", "/projects/missing"],
            discovered: []);

        Assert.AreEqual(1, merged.Count);
        Assert.IsTrue(merged[0].EndsWith("real", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void MergeExplicitAndDiscovered_DeduplicatesPathInBothLists()
    {
        // a path that appears in
        // both explicit and discovered lists must produce a single entry.
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory("/projects/shared");
        fs.AddDirectory("/projects/explicit-only");
        fs.AddDirectory("/projects/discovered-only");

        IReadOnlyList<string> merged = BackupEngine.MergeExplicitAndDiscovered(
            fs,
            explicitDirs: ["/projects/shared", "/projects/explicit-only"],
            discovered: ["/projects/shared", "/projects/discovered-only"]);

        Assert.AreEqual(3, merged.Count, "Shared path must collapse to a single entry.");
        Assert.IsTrue(merged.Any(p => p.EndsWith("shared", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(merged.Any(p => p.EndsWith("explicit-only", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(merged.Any(p => p.EndsWith("discovered-only", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void MergeExplicitAndDiscovered_ReturnsCanonicalAbsolutePaths()
    {
        // Path.GetFullPath should normalise the entries — verify the dedup
        // surface uses canonical paths so case-only differences and
        // separator-only differences collapse correctly.
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory("/projects/canonical");

        IReadOnlyList<string> merged = BackupEngine.MergeExplicitAndDiscovered(
            fs,
            explicitDirs: ["/projects/canonical"],
            discovered: []);

        Assert.AreEqual(1, merged.Count);
        Assert.IsTrue(Path.IsPathRooted(merged[0]),
            "MergeExplicitAndDiscovered must return absolute paths via Path.GetFullPath.");
    }
}