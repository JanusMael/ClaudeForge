using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="GitignoreReader"/> — the minimal .gitignore parser and matcher.
/// </summary>
[TestClass]
public sealed class GitignoreReaderTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "gir-" + Guid.NewGuid().ToString("N"));
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

    // -----------------------------------------------------------------------
    // Read — parsing
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Read_FileNotFound_ReturnsEmpty()
    {
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(Path.Combine(_scratch, "nonexistent", ".gitignore"));
        Assert.AreEqual(0, patterns.Count);
    }

    [TestMethod]
    public void Read_EmptyFile_ReturnsEmpty()
    {
        string path = WriteGitignore("");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.AreEqual(0, patterns.Count);
    }

    [TestMethod]
    public void Read_CommentLines_AreSkipped()
    {
        string path = WriteGitignore("# this is a comment\n  # indented comment\n\n*.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.AreEqual(1, patterns.Count);
        Assert.AreEqual("*.log", patterns[0].RawPattern);
    }

    [TestMethod]
    public void Read_DirOnlyPattern_SetsDirOnlyFlag()
    {
        string path = WriteGitignore("dist/\nbuild/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.AreEqual(2, patterns.Count);
        Assert.IsTrue(patterns[0].DirOnly, "dist/ should have DirOnly=true");
        Assert.IsTrue(patterns[1].DirOnly, "build/ should have DirOnly=true");
        Assert.IsFalse(patterns[0].Negated);
    }

    [TestMethod]
    public void Read_NegationPattern_SetsNegatedFlag()
    {
        string path = WriteGitignore("*.log\n!important.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.AreEqual(2, patterns.Count);
        Assert.IsFalse(patterns[0].Negated);
        Assert.IsTrue(patterns[1].Negated);
        Assert.AreEqual("important.log", patterns[1].RawPattern);
    }

    // -----------------------------------------------------------------------
    // IsIgnored — matching
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsIgnored_SimpleNameMatch_ReturnsTrue()
    {
        string path = WriteGitignore("node_modules");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsTrue(
            GitignoreReader.IsIgnored("node_modules", "node_modules", isDirectory: true, patterns),
            "Bare name should be ignored");
    }

    [TestMethod]
    public void IsIgnored_WildcardExtension_ReturnsTrue()
    {
        string path = WriteGitignore("*.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsTrue(
            GitignoreReader.IsIgnored("error.log", "error.log", isDirectory: false, patterns));
        Assert.IsTrue(
            GitignoreReader.IsIgnored("debug.log", "subdir/debug.log", isDirectory: false, patterns),
            "Pattern should match file in subdirectory via relPath");
    }

    [TestMethod]
    public void IsIgnored_DirOnlyPattern_DoesNotMatchFile()
    {
        string path = WriteGitignore("dist/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsFalse(
            GitignoreReader.IsIgnored("dist", "dist", isDirectory: false, patterns),
            "Dir-only pattern must not match a file named 'dist'");
    }

    [TestMethod]
    public void IsIgnored_DirOnlyPattern_MatchesDirectory()
    {
        string path = WriteGitignore("dist/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsTrue(
            GitignoreReader.IsIgnored("dist", "dist/", isDirectory: true, patterns),
            "Dir-only pattern should match directory");
    }

    [TestMethod]
    public void IsIgnored_NegationAfterMatch_ReturnsNotIgnored()
    {
        // *.log ignores all .log files, but !important.log re-includes it.
        string path = WriteGitignore("*.log\n!important.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsFalse(
            GitignoreReader.IsIgnored("important.log", "important.log", isDirectory: false, patterns),
            "Negation should override the earlier wildcard match");

        Assert.IsTrue(
            GitignoreReader.IsIgnored("debug.log", "debug.log", isDirectory: false, patterns),
            "Non-negated .log file should still be ignored");
    }

    [TestMethod]
    public void IsIgnored_NonMatchingPattern_ReturnsFalse()
    {
        string path = WriteGitignore("*.tmp");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.IsFalse(
            GitignoreReader.IsIgnored("readme.md", "readme.md", isDirectory: false, patterns));
    }

    [TestMethod]
    public void IsIgnored_NoPatterns_ReturnsFalse()
    {
        Assert.IsFalse(
            GitignoreReader.IsIgnored("anything.log", "anything.log",
                isDirectory: false, Array.Empty<GitignorePattern>()));
    }

    // -----------------------------------------------------------------------
    // MergePatterns
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MergePatterns_BothEmpty_ReturnsEmpty()
    {
        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(null, Array.Empty<GitignorePattern>());
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void MergePatterns_InheritedOnly_ReturnsInherited()
    {
        string parent = WriteGitignore("*.log");
        IReadOnlyList<GitignorePattern> inherited = GitignoreReader.Read(parent);

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(inherited, Array.Empty<GitignorePattern>());
        Assert.AreEqual(inherited.Count, result.Count);
        Assert.IsTrue(ReferenceEquals(result, inherited),
            "Should return inherited list directly when local is empty");
    }

    [TestMethod]
    public void MergePatterns_LocalOnly_ReturnsLocal()
    {
        IReadOnlyList<GitignorePattern> local = GitignoreReader.Read(WriteGitignore("*.tmp"));

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(null, local);
        Assert.AreEqual(local.Count, result.Count);
        Assert.IsTrue(ReferenceEquals(result, local),
            "Should return local list directly when inherited is empty");
    }

    [TestMethod]
    public void MergePatterns_Both_ReturnsCombinedInheritedfirst()
    {
        string inheritedPath = WriteGitignore("*.log");
        string localPath = Path.Combine(_scratch, "sub.gitignore");
        File.WriteAllText(localPath, "*.tmp");

        IReadOnlyList<GitignorePattern> inherited = GitignoreReader.Read(inheritedPath);
        IReadOnlyList<GitignorePattern> local = GitignoreReader.Read(localPath);

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(inherited, local);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("*.log", result[0].RawPattern, "Inherited pattern should come first");
        Assert.AreEqual("*.tmp", result[1].RawPattern, "Local pattern should come second");
    }

    // -----------------------------------------------------------------------
    // Catastrophic-backtracking guard
    // -----------------------------------------------------------------------

    [TestMethod]
    [Timeout(2000)] // 2s wall-clock; the match timeout is 200ms so should finish fast
    public void IsIgnored_PathologicalPattern_ReturnsFalseWithinTimeout()
    {
        // Patterns like "a*a*a*a*a*z" end with a literal that is absent from the input,
        // forcing catastrophic backtracking as the NFA tries every way to split the 'a'
        // repetitions before concluding 'z' can never match.
        // The C1 fix adds matchTimeout: TimeSpan.FromMilliseconds(200) so the
        // regex bails out and IsIgnored returns false (safe no-match default).
        string path = WriteGitignore("a*a*a*a*a*z");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        // All 'a's — the required terminal 'z' is absent, guaranteeing no match
        // and maximum backtracking before the timeout fires.
        string longInput = new('a', 25);

        // Must complete within the test timeout (2s). The match timeout (200ms) causes
        // RegexMatchTimeoutException which the reader swallows as a no-match.
        bool result = GitignoreReader.IsIgnored(longInput, longInput, isDirectory: false, patterns);
        Assert.IsFalse(result,
            "Pathological pattern should time out and return false (safe no-match default).");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string WriteGitignore(string content)
    {
        string path = Path.Combine(_scratch, Guid.NewGuid().ToString("N") + ".gitignore");
        File.WriteAllText(path, content);
        return path;
    }
}