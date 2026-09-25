using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Covers worktree discovery's in-project filtering, non-git skip, and the
/// <c>git worktree list --porcelain</c> line-parser.
/// </summary>
public sealed class WorktreeProbeTests : IDisposable
{
    private string _scratch = string.Empty;

    public WorktreeProbeTests() => Setup();

    private void Setup()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "wt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParseWorktreeList_ExtractsEveryWorktreeLine()
    {
        string[] lines =
        [
            "worktree /home/user/repo",
            "HEAD abcdef",
            "branch refs/heads/main",
            "",
            "worktree /tmp/feature-x",
            "HEAD 123456",
            "branch refs/heads/feature-x",
            "",
        ];

        IReadOnlyList<string> result = WorktreeProbe.ParseWorktreeList(lines);

        Assert.Equal(
            new[] { "/home/user/repo", "/tmp/feature-x" },
            result.ToArray());
    }

    [Fact]
    public async Task DiscoverExternal_FiltersInProjectWorktrees()
    {
        // Set up a fake "git project" directory with a .git sub-folder so the probe
        // believes it is a repo.
        string project = Path.Combine(_scratch, "repo");
        Directory.CreateDirectory(Path.Combine(project, ".git"));

        // The project itself plus an in-project worktree plus an external worktree.
        string inside = Path.Combine(project, ".claude", "worktrees", "feature-x");
        string outside = Path.Combine(_scratch, "external-wt");
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(outside);

        FakeRunner runner = new([
            $"worktree {project}", "HEAD x", "", "",
            $"worktree {inside}", "HEAD y", "", "",
            $"worktree {outside}", "HEAD z", "", "",
        ]);
        WorktreeProbe probe = new(runner);

        WorktreeDiscoveryResult result = await probe.DiscoverExternalAsync([project], TestContext.Current.CancellationToken);

        Assert.Single(result.Worktrees);
        Assert.Equal(Path.GetFullPath(outside), result.Worktrees[0].WorktreePath);
        Assert.Equal(Path.GetFullPath(project), result.Worktrees[0].ProjectRoot);
    }

    [Fact]
    public async Task DiscoverExternal_SkipsNonGitProjects()
    {
        string notAGit = Path.Combine(_scratch, "plain");
        Directory.CreateDirectory(notAGit);

        FakeRunner runner = new([]);
        WorktreeProbe probe = new(runner);

        WorktreeDiscoveryResult result = await probe.DiscoverExternalAsync([notAGit], TestContext.Current.CancellationToken);

        Assert.Empty(result.Worktrees);
        Assert.Equal(0, runner.Calls); // Never invoked git
    }

    [Fact]
    public async Task DiscoverExternal_HandlesNullRunnerResultGracefully()
    {
        string project = Path.Combine(_scratch, "repo");
        Directory.CreateDirectory(Path.Combine(project, ".git"));

        FakeRunner runner = new(null); // simulates 'git' missing / timeout
        WorktreeProbe probe = new(runner);

        WorktreeDiscoveryResult result = await probe.DiscoverExternalAsync([project], TestContext.Current.CancellationToken);
        Assert.Empty(result.Worktrees);
        Assert.True(result.GitMissing, "GitMissing should be true when runner returns null.");
    }

    [Fact]
    public async Task DiscoverExternal_RecognizesGitFileMarker()
    {
        // Regression: when a project directory was itself created by `git worktree add`,
        // .git is a *file* (containing "gitdir: …") rather than a directory.
        // The probe previously checked only Directory.Exists and silently skipped such
        // roots, meaning their worktrees were never discovered.
        string project = Path.Combine(_scratch, "worktree-as-project");
        Directory.CreateDirectory(project);

        // Simulate the .git file that git creates for linked worktrees.
        string dotGitFile = Path.Combine(project, ".git");
        await File.WriteAllTextAsync(dotGitFile, "gitdir: /some/repo/.git/worktrees/feature", TestContext.Current.CancellationToken);

        // Set up an external worktree that should be discovered.
        string external = Path.Combine(_scratch, "external");
        Directory.CreateDirectory(external);

        FakeRunner runner = new([
            $"worktree {project}", "HEAD a", "", "",
            $"worktree {external}", "HEAD b", "", "",
        ]);
        WorktreeProbe probe = new(runner);

        WorktreeDiscoveryResult result = await probe.DiscoverExternalAsync([project], TestContext.Current.CancellationToken);

        // git must have been invoked — proves the .git file was accepted as a valid marker.
        MessageAssert.Equal(1, runner.Calls,
            "git should have been invoked for a project with a .git file marker.");
        MessageAssert.Equal(1, result.Worktrees.Count,
            "External worktree should have been discovered via a .git-file project.");
        Assert.Equal(Path.GetFullPath(external), result.Worktrees[0].WorktreePath);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        private readonly IReadOnlyList<string>? _lines;
        public int Calls { get; private set; }

        public FakeRunner(IReadOnlyList<string>? lines)
        {
            _lines = lines;
        }

        public Task<IReadOnlyList<string>?> RunAsync(
            string fileName, string[] args, string workingDirectory,
            TimeSpan timeout, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_lines);
        }
    }
}