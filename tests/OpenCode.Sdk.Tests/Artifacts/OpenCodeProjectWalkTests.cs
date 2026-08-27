using Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Artifacts;

/// <summary>
/// The ancestor chain project-scope artifacts are read from.
/// </summary>
/// <remarks>
/// Every claim here was measured against opencode v1.17.9 with <c>opencode debug config</c>, not
/// taken from documentation: the vendor's own bundled spec describes the <i>config file</i> walk and
/// says nothing about artifact directories following the same rule.
/// </remarks>
[TestClass]
public sealed class OpenCodeProjectWalkTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ocwalk-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private string Dir(params string[] parts)
    {
        string path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// ⭐ Measured: <c>.opencode/agent/</c> directories at both the worktree root and an
    /// intermediate directory <b>both</b> contributed when running from deeper still.
    /// </summary>
    [TestMethod]
    public void EveryAncestorContributes_NotJustTheNearest()
    {
        string worktree = Dir("repo");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string deep = Dir("repo", "sub", "deeper");

        IReadOnlyList<string> ancestors = OpenCodeProjectWalk.Ancestors(deep);

        CollectionAssert.AreEqual(
            new[] { deep, Path.Combine(worktree, "sub"), worktree },
            ancestors.ToArray(),
            "the chain must be nearest-first and reach the worktree root");
    }

    /// <summary>
    /// ⚠ Measured both ways: an <c>.opencode/agent/</c> one level above the worktree root was not
    /// read, and removing the repository's <c>.git</c> made it readable.
    /// </summary>
    [TestMethod]
    public void TheWalkStopsAtTheGitWorktreeRoot()
    {
        string worktree = Dir("repo");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));

        IReadOnlyList<string> ancestors = OpenCodeProjectWalk.Ancestors(worktree);

        CollectionAssert.AreEqual(new[] { worktree }, ancestors.ToArray());
        CollectionAssert.DoesNotContain(ancestors.ToArray(), _root,
            "the directory above the worktree root is outside the project");
    }

    /// <summary>
    /// ⚠ A linked worktree and a submodule both record <c>.git</c> as a <b>file</b> holding a
    /// <c>gitdir:</c> pointer, so a directory-only test walks straight past their root.
    /// </summary>
    [TestMethod]
    public void AGitFileMarksTheRootJustAsAGitDirectoryDoes()
    {
        string worktree = Dir("linked");
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../real/.git/worktrees/linked");
        string inner = Dir("linked", "src");

        IReadOnlyList<string> ancestors = OpenCodeProjectWalk.Ancestors(inner);

        CollectionAssert.AreEqual(new[] { inner, worktree }, ancestors.ToArray());
    }

    /// <summary>
    /// ⚠ Measured: with no repository at all the walk genuinely continues upward — which is worth
    /// showing a user, because a stray <c>.opencode/</c> in a home directory then applies to every
    /// non-repository project beneath it.
    /// </summary>
    [TestMethod]
    public void WithNoRepositoryTheWalkContinuesPastWhereARootWouldHaveBeen()
    {
        string inner = Dir("plain", "nested");

        IReadOnlyList<string> ancestors = OpenCodeProjectWalk.Ancestors(inner);

        CollectionAssert.Contains(ancestors.ToArray(), _root,
            "without a .git boundary there is nothing to stop the walk");
        Assert.IsTrue(ancestors.Count > 3, "it should keep going above the sandbox too");
    }

    /// <summary>
    /// A page builds this list while rendering; an unusable path costs its own rows, not the list.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Two mechanisms produce this outcome and only one of them is testable.</b> Narrowing the
    /// <c>catch</c> so it no longer covers <see cref="ArgumentException"/> reddens this test — that
    /// is what handles the malformed path. The blank-input guard cannot be canaried at all: deleting
    /// it makes a nullable <c>string?</c> reach <see cref="Path.GetFullPath(string)"/> and the build
    /// fails on <c>CS8604</c> before any test runs, so the compiler enforces it and no test can.
    /// Recorded as measured rather than as "the guard is load-bearing", which would credit the
    /// wrong line.
    /// </remarks>
    [TestMethod]
    public void ABlankOrUnusableDirectoryYieldsNothingRatherThanThrowing()
    {
        Assert.AreEqual(0, OpenCodeProjectWalk.Ancestors(null).Count);
        Assert.AreEqual(0, OpenCodeProjectWalk.Ancestors("   ").Count);
        Assert.AreEqual(0, OpenCodeProjectWalk.Ancestors("\0not-a-path").Count);
    }
}
