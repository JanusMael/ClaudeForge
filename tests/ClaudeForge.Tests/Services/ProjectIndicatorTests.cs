using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Tests for <see cref="ProjectIndicator.BuildIndicator"/> — the helper that
/// produces the titlebar suffix describing the loaded project.
/// Each test covers one resolution tier from the helper's tiered logic:
/// null/empty path → "No Project Loaded", git repo → branch name (or short
/// SHA for detached HEAD), non-git folder → folder name.
/// </summary>
[TestClass]
public sealed class ProjectIndicatorTests
{
    private string _tmpRoot = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), "pi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tmpRoot))
            {
                Directory.Delete(_tmpRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    // ── Tier 1: empty / null path ────────────────────────────────────────────

    [TestMethod]
    public void BuildIndicator_NullPath_ReturnsNoProjectLoaded()
    {
        Assert.AreEqual(Strings.TitleNoProjectLoaded, ProjectIndicator.BuildIndicator(null));
    }

    [TestMethod]
    public void BuildIndicator_EmptyPath_ReturnsNoProjectLoaded()
    {
        Assert.AreEqual(Strings.TitleNoProjectLoaded, ProjectIndicator.BuildIndicator(string.Empty));
    }

    [TestMethod]
    public void BuildIndicator_WhitespacePath_ReturnsNoProjectLoaded()
    {
        // Empty-string whitespace SHOULD NOT be treated as a real project
        // root — user clearly hasn't picked one.
        Assert.AreEqual(Strings.TitleNoProjectLoaded, ProjectIndicator.BuildIndicator("   "));
        Assert.AreEqual(Strings.TitleNoProjectLoaded, ProjectIndicator.BuildIndicator("\t"));
    }

    // ── Tier 2: git repo ─────────────────────────────────────────────────────

    [TestMethod]
    public void BuildIndicator_GitRepoOnNamedBranch_ReturnsBranchName()
    {
        string gitDir = Path.Combine(_tmpRoot, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        Assert.IsTrue(ProjectIndicator.BuildIndicator(_tmpRoot).EndsWith("- main"));
    }

    [TestMethod]
    public void BuildIndicator_GitRepoOnSlashedBranch_PreservesSlashes()
    {
        // Branch names like "feature/auth" or "release/v1.2" contain
        // forward slashes — the helper must preserve them verbatim
        // rather than truncate at the last slash.
        string gitDir = Path.Combine(_tmpRoot, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/auth\n");

        Assert.IsTrue(ProjectIndicator.BuildIndicator(_tmpRoot).EndsWith("- feature/auth"));
    }

    [TestMethod]
    public void BuildIndicator_GitRepoInDetachedHead_ReturnsShortSha()
    {
        string gitDir = Path.Combine(_tmpRoot, ".git");
        Directory.CreateDirectory(gitDir);
        // Detached HEAD: 40-char hex SHA, no "ref:" prefix.
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "a1b2c3d4e5f6789012345678901234567890abcd\n");

        // Helper truncates to git's default short-sha length (7).
        Assert.IsTrue(ProjectIndicator.BuildIndicator(_tmpRoot).EndsWith("- a1b2c3d"));
    }

    [TestMethod]
    public void BuildIndicator_GitRepoWithMissingHEADFile_FallsBackToFolderName()
    {
        // .git directory exists but HEAD is missing — corrupt repo or
        // mid-init state.  Falls through to the folder-name tier.
        string gitDir = Path.Combine(_tmpRoot, ".git");
        Directory.CreateDirectory(gitDir);

        string folderName = Path.GetFileName(_tmpRoot);
        Assert.AreEqual(folderName, ProjectIndicator.BuildIndicator(_tmpRoot));
    }

    [TestMethod]
    public void BuildIndicator_GitWorktreePointer_FollowsToWorktreeHead()
    {
        // Worktree case: <project>/.git is a FILE with "gitdir: <path>"
        // pointing at the main repo's .git/worktrees/<name>/ subdirectory.
        // That subdirectory has its own HEAD describing the worktree's
        // current branch.
        string mainRepoGit = Path.Combine(_tmpRoot, "main", ".git");
        string worktreeDir = Path.Combine(_tmpRoot, "wt-feature");
        string worktreeGitDir = Path.Combine(mainRepoGit, "worktrees", "wt-feature");
        Directory.CreateDirectory(worktreeGitDir);
        Directory.CreateDirectory(worktreeDir);

        File.WriteAllText(Path.Combine(worktreeDir, ".git"), $"gitdir: {worktreeGitDir}\n");
        File.WriteAllText(Path.Combine(worktreeGitDir, "HEAD"), "ref: refs/heads/feature/x\n");

        Assert.AreEqual("wt-feature - feature/x", ProjectIndicator.BuildIndicator(worktreeDir));
    }

    // ── Tier 3: non-git folder ───────────────────────────────────────────────

    [TestMethod]
    public void BuildIndicator_NonGitFolder_ReturnsFolderName()
    {
        // No .git at all — fall through to the leaf folder name.
        string folderName = Path.GetFileName(_tmpRoot);
        Assert.AreEqual(folderName, ProjectIndicator.BuildIndicator(_tmpRoot));
    }

    [TestMethod]
    public void BuildIndicator_FolderWithTrailingSeparator_StripsItBeforeNameExtraction()
    {
        // "C:\projects\foo\" and "C:\projects\foo" both should resolve to "foo".
        // Same on POSIX: "/home/user/foo/" → "foo".
        string folderName = Path.GetFileName(_tmpRoot);
        string withTrailing = _tmpRoot + Path.DirectorySeparatorChar;
        Assert.AreEqual(folderName, ProjectIndicator.BuildIndicator(withTrailing));
    }
}