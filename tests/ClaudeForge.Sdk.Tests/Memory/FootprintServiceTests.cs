using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

[TestClass]
public class FootprintServiceTests
{
    private string _fakeHome = null!;
    private string _claudeHome => Path.Combine(_fakeHome, ".claude");

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(),
            "claudeforge-fp-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_claudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_fakeHome))
        {
            try
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
            catch
            {
                /* leave temp on lock */
            }
        }
    }

    private void WriteUnder(string relPath, string content = "x")
    {
        string full = Path.Combine(_claudeHome, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static FootprintService NewService()
    {
        return new FootprintService();
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task EmptyHome_AllCategoriesReportZero()
    {
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        Assert.IsTrue(rows.Count >= 7);
        foreach (FootprintCategoryStats row in rows)
        {
            Assert.AreEqual(0, row.FileCount, $"{row.Category} expected zero files");
            Assert.AreEqual(0, row.TotalBytes, $"{row.Category} expected zero bytes");
        }
    }

    [TestMethod]
    public async Task SessionTranscripts_Stats_CountsJsonl()
    {
        WriteUnder("projects/repo-a/session-1.jsonl", "abc");
        WriteUnder("projects/repo-a/session-2.jsonl", "abcd");
        WriteUnder("projects/repo-b/session-1.jsonl", "ab");
        // .json (non-jsonl) sibling — must not count
        WriteUnder("projects/repo-a/notes.json", "ignored");

        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.AreEqual(3, transcripts.FileCount);
        Assert.AreEqual(3 + 4 + 2, transcripts.TotalBytes);
    }

    [TestMethod]
    public async Task PromptHistory_SingleFileCounted()
    {
        WriteUnder("history.jsonl", "abcdef");
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats hist = rows.Single(r => r.Category == FootprintCategory.PromptHistory);
        Assert.AreEqual(1, hist.FileCount);
        Assert.AreEqual(6, hist.TotalBytes);
    }

    [TestMethod]
    public async Task IsInStandardBackup_Matches_BackupEngineSkipDecision()
    {
        // Mirror the only documented skip: SessionTranscripts (~/.claude/projects).
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.IsFalse(transcripts.IsInStandardBackup,
            "Session transcripts must be flagged as NOT in Standard backup.");
        FootprintCategoryStats hist = rows.Single(r => r.Category == FootprintCategory.PromptHistory);
        Assert.IsTrue(hist.IsInStandardBackup,
            "Prompt history must be flagged as IN Standard backup.");
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesEveryFileInCategory()
    {
        WriteUnder("history.jsonl", "abcdef");
        WriteUnder("projects/repo/session-1.jsonl");

        FootprintService svc = NewService();
        await svc.DeleteAsync(FootprintCategory.PromptHistory, CancellationToken.None);

        Assert.IsFalse(File.Exists(Path.Combine(_claudeHome, "history.jsonl")));
        // Sibling category untouched.
        Assert.IsTrue(File.Exists(Path.Combine(_claudeHome, "projects/repo/session-1.jsonl")));
    }

    [TestMethod]
    public async Task DeleteAsync_MissingCategory_NoOps()
    {
        // No files anywhere — DeleteAsync must not throw.
        await NewService().DeleteAsync(FootprintCategory.Todos, CancellationToken.None);
    }

    [TestMethod]
    public async Task DeleteAsync_ThenStats_ReportsZero()
    {
        WriteUnder("projects/repo/session-1.jsonl");
        WriteUnder("projects/repo/session-2.jsonl", "y");

        FootprintService svc = NewService();
        await svc.DeleteAsync(FootprintCategory.SessionTranscripts, CancellationToken.None);
        IReadOnlyList<FootprintCategoryStats> rows = await svc.GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.AreEqual(0, transcripts.FileCount);
        Assert.AreEqual(0, transcripts.TotalBytes);
    }

    [TestMethod]
    public async Task DeleteAsync_PropagatesIoFailure()
    {
        // Inject a fake IBackupFileSystem that throws on DeleteFile.
        ThrowingDeleteFileSystem fake = new(_claudeHome);
        FootprintService svc = new(fake);
        WriteUnder("history.jsonl");

        await Assert.ThrowsExceptionAsync<IOException>(() =>
            svc.DeleteAsync(FootprintCategory.PromptHistory, CancellationToken.None));
    }

    // ── Per-project transcript breakdown (Phase 5 v2) ────────────────────

    [TestMethod]
    public async Task GetProjectTranscriptStats_EmptyHome_ReturnsEmpty()
    {
        IReadOnlyList<ProjectTranscriptStats> rows = await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None);
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public async Task GetProjectTranscriptStats_OneRowPerProjectDirectory()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl", "abc");
        WriteUnder("projects/-Users-brian-foo/sess-2.jsonl", "abcd");
        WriteUnder("projects/-Users-brian-bar/sess-1.jsonl", "ab");

        List<ProjectTranscriptStats> rows = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None))
                                            .OrderBy(r => r.MangledName)
                                            .ToList();

        Assert.AreEqual(2, rows.Count);
        ProjectTranscriptStats bar = rows[0];
        ProjectTranscriptStats foo = rows[1];

        Assert.AreEqual("-Users-brian-bar", bar.MangledName);
        Assert.AreEqual(1, bar.FileCount);
        Assert.AreEqual(2, bar.TotalBytes);

        Assert.AreEqual("-Users-brian-foo", foo.MangledName);
        Assert.AreEqual(2, foo.FileCount);
        Assert.AreEqual(3 + 4, foo.TotalBytes);
    }

    [TestMethod]
    public async Task GetProjectTranscriptStats_DisplayName_DecodesLeadingDash()
    {
        WriteUnder("projects/-Users-brian-foo/sess.jsonl");
        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        Assert.AreEqual("/Users/brian/foo", row.DisplayName);
    }

    [TestMethod]
    public async Task GetProjectTranscriptStats_DisplayName_FallsBackToRaw_ForUnusualNames()
    {
        WriteUnder("projects/no-dash-prefix/sess.jsonl");
        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        // No leading dash → no slash prefix; the dashes still decode to slashes.
        Assert.AreEqual("no/dash/prefix", row.DisplayName);
    }

    [TestMethod]
    public async Task GetProjectTranscriptStats_LastWriteUtc_IsMostRecentFile()
    {
        WriteUnder("projects/-Users-brian-foo/old.jsonl");
        string oldFile = Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "old.jsonl");
        File.SetLastWriteTimeUtc(oldFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        WriteUnder("projects/-Users-brian-foo/new.jsonl", "y");
        string newFile = Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "new.jsonl");
        File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc));

        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        Assert.AreEqual(2026, row.LastWriteUtc.Year);
        Assert.AreEqual(5, row.LastWriteUtc.Month);
    }

    [TestMethod]
    public async Task DeleteProjectTranscripts_RemovesOnlyTargetProject()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");
        WriteUnder("projects/-Users-brian-foo/sess-2.jsonl", "y");
        WriteUnder("projects/-Users-brian-bar/sess-1.jsonl", "z");

        await NewService().DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        Assert.IsFalse(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "sess-1.jsonl")));
        Assert.IsFalse(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "sess-2.jsonl")));
        // Sibling project untouched.
        Assert.IsTrue(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-bar", "sess-1.jsonl")));
    }

    [TestMethod]
    public async Task DeleteProjectTranscripts_LeavesEmptyDirectoryInPlace()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");

        await NewService().DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        // Directory still present even though empty — Claude Code may
        // re-use it on next session, and racing the running CLI by
        // removing it isn't a meaningful privacy win.
        Assert.IsTrue(Directory.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo")));
    }

    [TestMethod]
    public async Task DeleteProjectTranscripts_MissingDirectory_NoOps()
    {
        // Calling with a name that doesn't exist on disk must not throw —
        // GUI may have cached stale stats.
        await NewService().DeleteProjectTranscriptsAsync("never-existed", CancellationToken.None);
    }

    [TestMethod]
    public async Task DeleteProjectTranscripts_RejectsPathTraversal()
    {
        WriteUnder("history.jsonl", "should-survive");
        FootprintService svc = NewService();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("../history.jsonl", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("foo/bar", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("foo\\bar", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("C:foo", CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync(string.Empty, CancellationToken.None));

        // Defence-in-depth assertion: nothing under ~/.claude/ outside the
        // (non-existent) projects/<bad-name> path should have been touched.
        Assert.IsTrue(File.Exists(Path.Combine(_claudeHome, "history.jsonl")));
    }

    [TestMethod]
    public async Task GetProjectTranscriptStats_ThenDelete_StatsRefreshToZero()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");
        FootprintService svc = NewService();

        IReadOnlyList<ProjectTranscriptStats> before = await svc.GetProjectTranscriptStatsAsync(CancellationToken.None);
        Assert.AreEqual(1, before.Single().FileCount);

        await svc.DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        IReadOnlyList<ProjectTranscriptStats> after = await svc.GetProjectTranscriptStatsAsync(CancellationToken.None);
        ProjectTranscriptStats row = after.Single();
        Assert.AreEqual(0, row.FileCount);
        Assert.AreEqual(0, row.TotalBytes);
    }

    private sealed class ThrowingDeleteFileSystem : IBackupFileSystem
    {
        private readonly string _claudeHome;

        public ThrowingDeleteFileSystem(string claudeHome)
        {
            _claudeHome = claudeHome;
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }

        public void DeleteFile(string path)
        {
            throw new IOException("fake delete failure");
        }
    }
}