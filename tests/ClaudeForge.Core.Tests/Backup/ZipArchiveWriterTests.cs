using System.IO.Compression;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Exercises <see cref="ZipArchiveWriter"/>'s contract:
/// forward-slash entry names, atomic commit, traversal rejection, and temp-file cleanup.
/// </summary>
[TestClass]
public sealed class ZipArchiveWriterTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "zw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public async Task Commit_WritesEntriesWithForwardSlashNames()
    {
        string dest = Path.Combine(_scratch, "test.zip");
        string src = Path.Combine(_scratch, "input.txt");
        await File.WriteAllTextAsync(src, "hello");

        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddFile(src, "ClaudeCode/claude.json");
            w.AddTextEntry("manifest.json", "{\"kind\":\"backup\"}");
            await w.CommitAsync();
        }

        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        List<string> names = archive.Entries.Select(e => e.FullName).ToList();

        CollectionAssert.Contains(names, "ClaudeCode/claude.json");
        CollectionAssert.Contains(names, "manifest.json");
        Assert.IsFalse(names.Any(n => n.Contains('\\')),
            "Entry names must use forward slashes.");
    }

    [TestMethod]
    public async Task Dispose_WithoutCommit_DeletesTempFile()
    {
        string dest = Path.Combine(_scratch, "abandon.zip");

        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddTextEntry("foo", "bar");
            // No commit — simulates an exception or user cancel.
        }

        Assert.IsFalse(File.Exists(dest),
            "Final file should never appear when CommitAsync was not called.");
        List<string> leftovers = Directory.EnumerateFiles(_scratch, "*.tmp-*").ToList();
        Assert.AreEqual(0, leftovers.Count,
            "Temp files should be cleaned up on dispose without commit.");
    }

    [TestMethod]
    public async Task Commit_OverwritesExistingFinalFile()
    {
        string dest = Path.Combine(_scratch, "replace.zip");
        await File.WriteAllTextAsync(dest, "pre-existing bytes");

        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddTextEntry("a.txt", "one");
            await w.CommitAsync();
        }

        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        Assert.AreEqual(1, archive.Entries.Count);
        Assert.AreEqual("a.txt", archive.Entries[0].FullName);
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("foo/../bar")]
    [DataRow("/absolute")]
    [DataRow("foo/./bar")]
    public void NormaliseEntryName_RejectsDangerousPaths(string bad)
    {
        Assert.ThrowsException<ArgumentException>(() =>
            ZipArchiveWriter.NormaliseEntryName(bad));
    }

    [TestMethod]
    public void NormaliseEntryName_ConvertsBackslashes()
    {
        Assert.AreEqual("foo/bar", ZipArchiveWriter.NormaliseEntryName("foo\\bar"));
    }

    [TestMethod]
    public async Task DisposeAsync_AfterCommit_IsIdempotent()
    {
        // Regression: the `await using` block always calls DisposeAsync on exit, even after
        // CommitAsync succeeded.  The sequence is: CommitAsync() → (implicit) DisposeAsync().
        // Callers who also explicitly call DisposeAsync will trigger it a second time.
        // Both the first and second dispose must be no-ops on a committed writer.
        string dest = Path.Combine(_scratch, "idempotent.zip");
        ZipArchiveWriter w = ZipArchiveWriter.Create(dest);
        w.AddTextEntry("a.txt", "hello");
        await w.CommitAsync();

        // First dispose (simulates the implicit await-using cleanup).
        await w.DisposeAsync();
        // Second dispose — must also be a no-op, not throw.
        await w.DisposeAsync();

        // The committed archive must still be intact.
        Assert.IsTrue(File.Exists(dest), "Committed archive must survive double-dispose.");
    }

    [TestMethod]
    public async Task DisposeAsync_AfterCancellation_DeletesTempFile_AndDoesNotThrow()
    {
        // Regression: if CommitAsync throws OperationCanceledException (e.g. due to
        // cancellation during the write loop), DisposeAsync must clean up the temp file
        // without itself throwing.
        string dest = Path.Combine(_scratch, "cancelled.zip");
        using CancellationTokenSource cts = new();

        ZipArchiveWriter w = ZipArchiveWriter.Create(dest);
        // Queue a large text entry so the write loop is not a no-op.
        w.AddTextEntry("big.txt", new string('x', 1_000_000));

        // Cancel immediately; CommitAsync should throw OperationCanceledException.
        await cts.CancelAsync();
        try
        {
            await w.CommitAsync(ct: cts.Token);
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }

        // Dispose must not throw.
        await w.DisposeAsync();

        Assert.IsFalse(File.Exists(dest),
            "Final file must not appear when CommitAsync was cancelled.");
        List<string> leftovers = Directory.EnumerateFiles(_scratch, "*.tmp-*").ToList();
        Assert.AreEqual(0, leftovers.Count,
            "Temp files must be cleaned up after a cancelled commit.");
    }

    // -----------------------------------------------------------------------
    // .gitignore integration
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task AddDirectory_HonorsGitignore_ExcludesMatchedFile()
    {
        string srcDir = Path.Combine(_scratch, "proj");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.cs"), "code");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "debug.log"), "log data");
        await File.WriteAllTextAsync(Path.Combine(srcDir, ".gitignore"), "*.log\n");

        string dest = Path.Combine(_scratch, "gi-basic.zip");
        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddDirectory(srcDir, "proj");
            await w.CommitAsync();
        }

        List<string> entries = GetEntryNames(dest);
        Assert.IsTrue(entries.Any(n => n.EndsWith("app.cs")), "app.cs should be included");
        Assert.IsFalse(entries.Any(n => n.EndsWith("debug.log")), "debug.log should be excluded by *.log pattern");
    }

    [TestMethod]
    public async Task AddDirectory_HonorsGitignore_InheritedFromParent_ExcludesChildFile()
    {
        string srcDir = Path.Combine(_scratch, "proj2");
        string subDir = Path.Combine(srcDir, "logs");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "app.cs"), "code");
        await File.WriteAllTextAsync(Path.Combine(subDir, "service.log"), "log");
        await File.WriteAllTextAsync(Path.Combine(srcDir, ".gitignore"), "*.log\n");

        string dest = Path.Combine(_scratch, "gi-inherit.zip");
        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddDirectory(srcDir, "proj2");
            await w.CommitAsync();
        }

        List<string> entries = GetEntryNames(dest);
        Assert.IsTrue(entries.Any(n => n.EndsWith("app.cs")), "app.cs should be included");
        Assert.IsFalse(entries.Any(n => n.EndsWith("service.log")),
            "service.log in subdir should be excluded by inherited *.log");
    }

    [TestMethod]
    public async Task AddDirectory_NegatedGitignorePattern_ReincludesFile()
    {
        string srcDir = Path.Combine(_scratch, "proj3");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "debug.log"), "noise");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "important.log"), "keep me");
        await File.WriteAllTextAsync(Path.Combine(srcDir, ".gitignore"), "*.log\n!important.log\n");

        string dest = Path.Combine(_scratch, "gi-negation.zip");
        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddDirectory(srcDir, "proj3");
            await w.CommitAsync();
        }

        List<string> entries = GetEntryNames(dest);
        Assert.IsTrue(entries.Any(n => n.EndsWith("important.log")), "important.log should be re-included by negation");
        Assert.IsFalse(entries.Any(n => n.EndsWith("debug.log")), "debug.log should still be excluded");
    }

    private static List<string> GetEntryNames(string zipPath)
    {
        using FileStream fs = File.OpenRead(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    [TestMethod]
    public async Task AddDirectory_SkipsSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            // Creating symlinks on Windows needs either Dev Mode or admin; skip to avoid
            // environment-sensitive failures in CI.
            Assert.Inconclusive("Symlink creation on Windows needs Dev Mode — covered in non-Windows runs.");
            return;
        }

        string srcDir = Path.Combine(_scratch, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "real.txt"), "hi");

        // Create a symlink pointing outside the directory.
        string linkTarget = Path.Combine(_scratch, "outside");
        Directory.CreateDirectory(linkTarget);
        string linkPath = Path.Combine(srcDir, "link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, linkTarget);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"Symlink creation failed: {ex.Message}");
            return;
        }

        string dest = Path.Combine(_scratch, "sym.zip");
        await using (ZipArchiveWriter w = ZipArchiveWriter.Create(dest))
        {
            w.AddDirectory(srcDir, "src");
            Assert.AreEqual(1, w.SkippedSymlinks.Count, "Symlink should have been detected and skipped.");
            await w.CommitAsync();
        }

        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        List<string> names = archive.Entries.Select(e => e.FullName).ToList();
        CollectionAssert.Contains(names, "src/real.txt");
        Assert.IsFalse(names.Any(n => n.Contains("link")),
            "Symlinked directory should not be traversed.");
    }
}