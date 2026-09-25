using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

public class FootprintServiceTests : IDisposable
{
    private string _fakeHome = null!;
    private string _claudeHome => Path.Combine(_fakeHome, ".claude");

    public FootprintServiceTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(),
            "claudeforge-fp-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_claudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void WriteUnder(string relPath, string content = "x")
    {
        string full = Path.Combine(_claudeHome, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static FootprintService NewService()
    {
        return new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default);
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmptyHome_AllCategoriesReportZero()
    {
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        Assert.True(rows.Count >= 7);
        foreach (FootprintCategoryStats row in rows)
        {
            MessageAssert.Equal(0, row.FileCount, $"{row.Category} expected zero files");
            MessageAssert.Equal(0, row.TotalBytes, $"{row.Category} expected zero bytes");
        }
    }

    [Fact]
    public async Task SessionTranscripts_Stats_CountsJsonl()
    {
        WriteUnder("projects/repo-a/session-1.jsonl", "abc");
        WriteUnder("projects/repo-a/session-2.jsonl", "abcd");
        WriteUnder("projects/repo-b/session-1.jsonl", "ab");
        // .json (non-jsonl) sibling — must not count
        WriteUnder("projects/repo-a/notes.json", "ignored");

        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.Equal(3, transcripts.FileCount);
        Assert.Equal(3 + 4 + 2, transcripts.TotalBytes);
    }

    [Fact]
    public async Task PromptHistory_SingleFileCounted()
    {
        WriteUnder("history.jsonl", "abcdef");
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats hist = rows.Single(r => r.Category == FootprintCategory.PromptHistory);
        Assert.Equal(1, hist.FileCount);
        Assert.Equal(6, hist.TotalBytes);
    }

    [Fact]
    public async Task IsInStandardBackup_Matches_BackupEngineSkipDecision()
    {
        // Mirror the only documented skip: SessionTranscripts (~/.claude/projects).
        IReadOnlyList<FootprintCategoryStats> rows = await NewService().GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.False(transcripts.IsInStandardBackup,
            "Session transcripts must be flagged as NOT in Standard backup.");
        FootprintCategoryStats hist = rows.Single(r => r.Category == FootprintCategory.PromptHistory);
        Assert.True(hist.IsInStandardBackup,
            "Prompt history must be flagged as IN Standard backup.");
    }

    [Fact]
    public async Task DeleteAsync_RemovesEveryFileInCategory()
    {
        WriteUnder("history.jsonl", "abcdef");
        WriteUnder("projects/repo/session-1.jsonl");

        FootprintService svc = NewService();
        await svc.DeleteAsync(FootprintCategory.PromptHistory, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_claudeHome, "history.jsonl")));
        // Sibling category untouched.
        Assert.True(File.Exists(Path.Combine(_claudeHome, "projects/repo/session-1.jsonl")));
    }

    [Fact]
    public async Task DeleteAsync_MissingCategory_NoOps()
    {
        // No files anywhere — DeleteAsync must not throw.
        await NewService().DeleteAsync(FootprintCategory.Todos, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteAsync_ThenStats_ReportsZero()
    {
        WriteUnder("projects/repo/session-1.jsonl");
        WriteUnder("projects/repo/session-2.jsonl", "y");

        FootprintService svc = NewService();
        await svc.DeleteAsync(FootprintCategory.SessionTranscripts, CancellationToken.None);
        IReadOnlyList<FootprintCategoryStats> rows = await svc.GetStatsAsync(CancellationToken.None);
        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.Equal(0, transcripts.FileCount);
        Assert.Equal(0, transcripts.TotalBytes);
    }

    [Fact]
    public async Task DeleteAsync_PropagatesIoFailure()
    {
        // Inject a fake IBackupFileSystem that throws on DeleteFile.
        ThrowingDeleteFileSystem fake = new(_claudeHome);
        FootprintService svc = new(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default, fake);
        WriteUnder("history.jsonl");

        await Assert.ThrowsAsync<IOException>(() =>
            svc.DeleteAsync(FootprintCategory.PromptHistory, CancellationToken.None));
    }

    // ── Per-project transcript breakdown (Phase 5 v2) ────────────────────

    [Fact]
    public async Task GetProjectTranscriptStats_EmptyHome_ReturnsEmpty()
    {
        IReadOnlyList<ProjectTranscriptStats> rows = await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetProjectTranscriptStats_OneRowPerProjectDirectory()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl", "abc");
        WriteUnder("projects/-Users-brian-foo/sess-2.jsonl", "abcd");
        WriteUnder("projects/-Users-brian-bar/sess-1.jsonl", "ab");

        List<ProjectTranscriptStats> rows = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None))
                                            .OrderBy(r => r.MangledName)
                                            .ToList();

        Assert.Equal(2, rows.Count);
        ProjectTranscriptStats bar = rows[0];
        ProjectTranscriptStats foo = rows[1];

        Assert.Equal("-Users-brian-bar", bar.MangledName);
        Assert.Equal(1, bar.FileCount);
        Assert.Equal(2, bar.TotalBytes);

        Assert.Equal("-Users-brian-foo", foo.MangledName);
        Assert.Equal(2, foo.FileCount);
        Assert.Equal(3 + 4, foo.TotalBytes);
    }

    [Fact]
    public async Task GetProjectTranscriptStats_DisplayName_DecodesLeadingDash()
    {
        WriteUnder("projects/-Users-brian-foo/sess.jsonl");
        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        Assert.Equal("/Users/brian/foo", row.DisplayName);
    }

    [Fact]
    public async Task GetProjectTranscriptStats_DisplayName_FallsBackToRaw_ForUnusualNames()
    {
        WriteUnder("projects/no-dash-prefix/sess.jsonl");
        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        // No leading dash → no slash prefix; the dashes still decode to slashes.
        Assert.Equal("no/dash/prefix", row.DisplayName);
    }

    [Fact]
    public async Task GetProjectTranscriptStats_LastWriteUtc_IsMostRecentFile()
    {
        WriteUnder("projects/-Users-brian-foo/old.jsonl");
        string oldFile = Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "old.jsonl");
        File.SetLastWriteTimeUtc(oldFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        WriteUnder("projects/-Users-brian-foo/new.jsonl", "y");
        string newFile = Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "new.jsonl");
        File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc));

        ProjectTranscriptStats row = (await NewService().GetProjectTranscriptStatsAsync(CancellationToken.None)).Single();
        Assert.Equal(2026, row.LastWriteUtc.Year);
        Assert.Equal(5, row.LastWriteUtc.Month);
    }

    [Fact]
    public async Task DeleteProjectTranscripts_RemovesOnlyTargetProject()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");
        WriteUnder("projects/-Users-brian-foo/sess-2.jsonl", "y");
        WriteUnder("projects/-Users-brian-bar/sess-1.jsonl", "z");

        await NewService().DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "sess-1.jsonl")));
        Assert.False(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo", "sess-2.jsonl")));
        // Sibling project untouched.
        Assert.True(File.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-bar", "sess-1.jsonl")));
    }

    [Fact]
    public async Task DeleteProjectTranscripts_LeavesEmptyDirectoryInPlace()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");

        await NewService().DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        // Directory still present even though empty — Claude Code may
        // re-use it on next session, and racing the running CLI by
        // removing it isn't a meaningful privacy win.
        Assert.True(Directory.Exists(Path.Combine(_claudeHome, "projects", "-Users-brian-foo")));
    }

    [Fact]
    public async Task DeleteProjectTranscripts_MissingDirectory_NoOps()
    {
        // Calling with a name that doesn't exist on disk must not throw —
        // GUI may have cached stale stats.
        await NewService().DeleteProjectTranscriptsAsync("never-existed", CancellationToken.None);
    }

    [Fact]
    public async Task DeleteProjectTranscripts_RejectsPathTraversal()
    {
        WriteUnder("history.jsonl", "should-survive");
        FootprintService svc = NewService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("../history.jsonl", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("foo/bar", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("foo\\bar", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync("C:foo", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.DeleteProjectTranscriptsAsync(string.Empty, CancellationToken.None));

        // Defence-in-depth assertion: nothing under ~/.claude/ outside the
        // (non-existent) projects/<bad-name> path should have been touched.
        Assert.True(File.Exists(Path.Combine(_claudeHome, "history.jsonl")));
    }

    [Fact]
    public async Task GetProjectTranscriptStats_ThenDelete_StatsRefreshToZero()
    {
        WriteUnder("projects/-Users-brian-foo/sess-1.jsonl");
        FootprintService svc = NewService();

        IReadOnlyList<ProjectTranscriptStats> before = await svc.GetProjectTranscriptStatsAsync(CancellationToken.None);
        Assert.Equal(1, before.Single().FileCount);

        await svc.DeleteProjectTranscriptsAsync("-Users-brian-foo", CancellationToken.None);

        IReadOnlyList<ProjectTranscriptStats> after = await svc.GetProjectTranscriptStatsAsync(CancellationToken.None);
        ProjectTranscriptStats row = after.Single();
        Assert.Equal(0, row.FileCount);
        Assert.Equal(0, row.TotalBytes);
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