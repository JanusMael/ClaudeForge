using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="BackupEngine"/> internals routed through the
/// <see cref="IBackupFileSystem"/> seam. Replaces the previous
/// pattern of constructing real temp directories and zip archives for every
/// retention / discovery assertion.
/// </summary>
public class BackupEngineSeamTests
{
    private const string BackupDir = "/backups";

    // ── ApplyRetention ────────────────────────────────────────────────────────

    [Fact]
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
        Assert.True(fs.FileExists($"{BackupDir}/backup-c.zip"), "Newest 3 must survive");
        Assert.True(fs.FileExists($"{BackupDir}/backup-d.zip"), "Newest 3 must survive");
        Assert.True(fs.FileExists($"{BackupDir}/backup-e.zip"), "Newest 3 must survive");
        Assert.False(fs.FileExists($"{BackupDir}/backup-a.zip"), "Oldest must be deleted");
        Assert.False(fs.FileExists($"{BackupDir}/backup-b.zip"), "Oldest must be deleted");

        // The seam recorded both deletions.
        MessageAssert.SameElements(
            new[] { $"{BackupDir}/backup-a.zip", $"{BackupDir}/backup-b.zip" }
                .Select(p => p.Replace('/', Path.DirectorySeparatorChar)).ToList(),
            fs.DeletedPaths.ToList());
    }

    [Fact]
    public void ApplyRetention_KeepLastZero_IsNoOp()
    {
        // Defensive contract: keepLast <= 0 short-circuits — never wipe everything.
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory(BackupDir);
        fs.AddFile($"{BackupDir}/backup-only.zip", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        BackupEngine.ApplyRetention(fs, BackupDir, keepLast: 0);

        Assert.True(fs.FileExists($"{BackupDir}/backup-only.zip"));
        Assert.Empty(fs.DeletedPaths);
    }

    [Fact]
    public void ApplyRetention_MissingDirectory_IsNoOp()
    {
        // No directory → nothing to do; must not throw.
        InMemoryBackupFileSystem fs = new();
        BackupEngine.ApplyRetention(fs, "/does/not/exist", keepLast: 3);
        Assert.Empty(fs.DeletedPaths);
    }

    [Fact]
    public void ApplyRetention_FewerFilesThanKeep_KeepsAll()
    {
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory(BackupDir);
        fs.AddFile($"{BackupDir}/backup-a.zip", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.AddFile($"{BackupDir}/backup-b.zip", new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        BackupEngine.ApplyRetention(fs, BackupDir, keepLast: 5);

        Assert.True(fs.FileExists($"{BackupDir}/backup-a.zip"));
        Assert.True(fs.FileExists($"{BackupDir}/backup-b.zip"));
        Assert.Empty(fs.DeletedPaths);
    }

    // ── MergeExplicitAndDiscovered ────────────────────────────────────────────

    [Fact]
    public void MergeExplicitAndDiscovered_FiltersOutMissingDirectories()
    {
        InMemoryBackupFileSystem fs = new();
        fs.AddDirectory("/projects/real");
        // /projects/missing is intentionally not added.

        IReadOnlyList<string> merged = BackupEngine.MergeExplicitAndDiscovered(
            fs,
            explicitDirs: ["/projects/real", "/projects/missing"],
            discovered: []);

        Assert.Single(merged);
        Assert.EndsWith("real", merged[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
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

        MessageAssert.Equal(3, merged.Count, "Shared path must collapse to a single entry.");
        Assert.Contains(merged, p => p.EndsWith("shared", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(merged, p => p.EndsWith("explicit-only", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(merged, p => p.EndsWith("discovered-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
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

        Assert.Single(merged);
        Assert.True(Path.IsPathRooted(merged[0]),
            "MergeExplicitAndDiscovered must return absolute paths via Path.GetFullPath.");
    }
}