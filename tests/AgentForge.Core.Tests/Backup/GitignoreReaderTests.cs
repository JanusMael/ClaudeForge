using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Unit tests for <see cref="GitignoreReader"/> — the minimal .gitignore parser and matcher.
/// </summary>
public sealed class GitignoreReaderTests : IDisposable
{
    private string _scratch = string.Empty;

    public GitignoreReaderTests() => Setup();

    private void Setup()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "gir-" + Guid.NewGuid().ToString("N"));
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

    // -----------------------------------------------------------------------
    // Read — parsing
    // -----------------------------------------------------------------------

    [Fact]
    public void Read_FileNotFound_ReturnsEmpty()
    {
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(Path.Combine(_scratch, "nonexistent", ".gitignore"));
        Assert.Empty(patterns);
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmpty()
    {
        string path = WriteGitignore("");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.Empty(patterns);
    }

    [Fact]
    public void Read_CommentLines_AreSkipped()
    {
        string path = WriteGitignore("# this is a comment\n  # indented comment\n\n*.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.Single(patterns);
        Assert.Equal("*.log", patterns[0].RawPattern);
    }

    [Fact]
    public void Read_DirOnlyPattern_SetsDirOnlyFlag()
    {
        string path = WriteGitignore("dist/\nbuild/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.Equal(2, patterns.Count);
        Assert.True(patterns[0].DirOnly, "dist/ should have DirOnly=true");
        Assert.True(patterns[1].DirOnly, "build/ should have DirOnly=true");
        Assert.False(patterns[0].Negated);
    }

    [Fact]
    public void Read_NegationPattern_SetsNegatedFlag()
    {
        string path = WriteGitignore("*.log\n!important.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        Assert.Equal(2, patterns.Count);
        Assert.False(patterns[0].Negated);
        Assert.True(patterns[1].Negated);
        Assert.Equal("important.log", patterns[1].RawPattern);
    }

    // -----------------------------------------------------------------------
    // IsIgnored — matching
    // -----------------------------------------------------------------------

    [Fact]
    public void IsIgnored_SimpleNameMatch_ReturnsTrue()
    {
        string path = WriteGitignore("node_modules");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("node_modules", "node_modules", isDirectory: true, patterns),
            "Bare name should be ignored");
    }

    [Fact]
    public void IsIgnored_WildcardExtension_ReturnsTrue()
    {
        string path = WriteGitignore("*.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("error.log", "error.log", isDirectory: false, patterns));
        Assert.True(
            GitignoreReader.IsIgnored("debug.log", "subdir/debug.log", isDirectory: false, patterns),
            "Pattern should match file in subdirectory via relPath");
    }

    [Fact]
    public void IsIgnored_DirOnlyPattern_DoesNotMatchFile()
    {
        string path = WriteGitignore("dist/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.False(
            GitignoreReader.IsIgnored("dist", "dist", isDirectory: false, patterns),
            "Dir-only pattern must not match a file named 'dist'");
    }

    [Fact]
    public void IsIgnored_DirOnlyPattern_MatchesDirectory()
    {
        string path = WriteGitignore("dist/");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("dist", "dist/", isDirectory: true, patterns),
            "Dir-only pattern should match directory");
    }

    [Fact]
    public void IsIgnored_NegationAfterMatch_ReturnsNotIgnored()
    {
        // *.log ignores all .log files, but !important.log re-includes it.
        string path = WriteGitignore("*.log\n!important.log");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.False(
            GitignoreReader.IsIgnored("important.log", "important.log", isDirectory: false, patterns),
            "Negation should override the earlier wildcard match");

        Assert.True(
            GitignoreReader.IsIgnored("debug.log", "debug.log", isDirectory: false, patterns),
            "Non-negated .log file should still be ignored");
    }

    [Fact]
    public void IsIgnored_NonMatchingPattern_ReturnsFalse()
    {
        string path = WriteGitignore("*.tmp");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.False(
            GitignoreReader.IsIgnored("readme.md", "readme.md", isDirectory: false, patterns));
    }

    [Fact]
    public void IsIgnored_NoPatterns_ReturnsFalse()
    {
        Assert.False(
            GitignoreReader.IsIgnored("anything.log", "anything.log",
                isDirectory: false, Array.Empty<GitignorePattern>()));
    }

    // -----------------------------------------------------------------------
    // IsIgnored — anchoring and ** depth
    //
    // ⛔ Both of these shipped wrong, and both failed SILENTLY in the direction that
    // matters for a backup: a root-anchored pattern matched NOTHING, so `/node_modules`
    // was archived anyway; and `**/` matched partial segments, so files nobody excluded
    // were dropped from the archive. Over-inclusion bloats a backup; over-exclusion
    // loses data. Neither raised anything.
    // -----------------------------------------------------------------------

    [Fact]
    public void IsIgnored_RootAnchoredDirectory_MatchesAtRoot()
    {
        // `/node_modules` is the single most common root-anchored pattern in the wild.
        string path = WriteGitignore("/node_modules");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        // ⚠ ZipArchiveWriter passes directories with a TRAILING SLASH (relDirPath + "/"),
        // so a fix that only handles the bare form still misses every directory.
        Assert.True(
            GitignoreReader.IsIgnored("node_modules", "node_modules/", isDirectory: true, patterns),
            "A root-anchored directory pattern must match at the root.");
    }

    [Fact]
    public void IsIgnored_RootAnchoredFile_MatchesAtRoot()
    {
        string path = WriteGitignore("/secrets.env");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("secrets.env", "secrets.env", isDirectory: false, patterns),
            "A root-anchored file pattern must match at the root.");
    }

    [Fact]
    public void IsIgnored_RootAnchoredPattern_DoesNotMatchNested()
    {
        // The whole point of the leading slash: anchored to the .gitignore's own directory.
        string path = WriteGitignore("/node_modules");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.False(
            GitignoreReader.IsIgnored("node_modules", "packages/app/node_modules/", isDirectory: true, patterns),
            "A root-anchored pattern must NOT match the same name nested deeper.");
    }

    [Fact]
    public void IsIgnored_UnanchoredPattern_StillMatchesAtAnyDepth()
    {
        // The contrast that gives the anchored case its meaning - and a regression guard,
        // since anchoring is implemented by suppressing the bare-name match.
        string path = WriteGitignore("node_modules");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("node_modules", "packages/app/node_modules/", isDirectory: true, patterns),
            "An UNanchored pattern must still match at any depth.");
    }

    [Fact]
    public void IsIgnored_DoubleStarSlash_DoesNotMatchPartialSegment()
    {
        // `**/` means "any number of whole path segments", never "any characters".
        string path = WriteGitignore("**/foo");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.False(
            GitignoreReader.IsIgnored("barfoo", "barfoo", isDirectory: false, patterns),
            "`**/foo` must not match `barfoo` - that is a segment boundary, not a character run.");
    }

    [Fact]
    public void IsIgnored_DoubleStarSlash_MatchesAtZeroAndAnyDepth()
    {
        string path = WriteGitignore("**/foo");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("foo", "foo", isDirectory: false, patterns),
            "`**/foo` must match at zero depth.");
        Assert.True(
            GitignoreReader.IsIgnored("foo", "a/b/foo", isDirectory: false, patterns),
            "`**/foo` must match at any depth.");
    }

    [Fact]
    public void IsIgnored_DoubleStarInMiddle_SpansWholeSegmentsOnly()
    {
        string path = WriteGitignore("a/**/b");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.True(
            GitignoreReader.IsIgnored("b", "a/b", isDirectory: false, patterns),
            "`a/**/b` must match with zero intervening segments.");
        Assert.True(
            GitignoreReader.IsIgnored("b", "a/x/y/b", isDirectory: false, patterns),
            "`a/**/b` must match across several segments.");
        Assert.False(
            GitignoreReader.IsIgnored("xb", "a/xb", isDirectory: false, patterns),
            "`a/**/b` must not match `a/xb` - again a segment boundary.");
    }

    // -----------------------------------------------------------------------
    // MergePatterns
    // -----------------------------------------------------------------------

    [Fact]
    public void MergePatterns_BothEmpty_ReturnsEmpty()
    {
        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(null, Array.Empty<GitignorePattern>());
        Assert.Empty(result);
    }

    [Fact]
    public void MergePatterns_InheritedOnly_ReturnsInherited()
    {
        string parent = WriteGitignore("*.log");
        IReadOnlyList<GitignorePattern> inherited = GitignoreReader.Read(parent);

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(inherited, Array.Empty<GitignorePattern>());
        Assert.Equal(inherited.Count, result.Count);
        Assert.True(ReferenceEquals(result, inherited),
            "Should return inherited list directly when local is empty");
    }

    [Fact]
    public void MergePatterns_LocalOnly_ReturnsLocal()
    {
        IReadOnlyList<GitignorePattern> local = GitignoreReader.Read(WriteGitignore("*.tmp"));

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(null, local);
        Assert.Equal(local.Count, result.Count);
        Assert.True(ReferenceEquals(result, local),
            "Should return local list directly when inherited is empty");
    }

    [Fact]
    public void MergePatterns_Both_ReturnsCombinedInheritedfirst()
    {
        string inheritedPath = WriteGitignore("*.log");
        string localPath = Path.Combine(_scratch, "sub.gitignore");
        File.WriteAllText(localPath, "*.tmp");

        IReadOnlyList<GitignorePattern> inherited = GitignoreReader.Read(inheritedPath);
        IReadOnlyList<GitignorePattern> local = GitignoreReader.Read(localPath);

        IReadOnlyList<GitignorePattern> result = GitignoreReader.MergePatterns(inherited, local);
        Assert.Equal(2, result.Count);
        MessageAssert.Equal("*.log", result[0].RawPattern, "Inherited pattern should come first");
        MessageAssert.Equal("*.tmp", result[1].RawPattern, "Local pattern should come second");
    }

    // -----------------------------------------------------------------------
    // Catastrophic-backtracking guard
    // -----------------------------------------------------------------------

    [Fact(Timeout = 15000)]
    // 15s wall-clock. The budget only needs to distinguish "finished" from "hung
    // forever": on the backtracking engine this pattern runs effectively
    // indefinitely. The engine is now linear, so it finishes in well under a
    // millisecond. A 2s budget was once tight enough that a CPU-starved CI
    // Windows runner tripped it on a machine hiccup rather than a real regression.
    //
    // ⚠ This test cannot tell the engine from the old 200ms timeout: both answer
    // false, which is correct here. IsIgnored_NegatedPathologicalPattern_StillReincludes
    // is the one whose correct answer the timeout got wrong.
    //
    // ⓘ Made async by hand in the xUnit move (plans/00006): xUnit v3 enforces Timeout only on an
    // async test, and only while it is awaiting — so the match runs on the pool and is awaited,
    // which is what lets a hang be cut off instead of running forever.
    public async Task IsIgnored_PathologicalPattern_ReturnsFalseWithinTimeout()
    {
        // Patterns like "a*a*a*a*a*z" end with a literal that is absent from the input,
        // which makes a backtracking engine try every way to split the 'a' repetitions
        // before concluding 'z' can never match. The linear engine does not search splits.
        string path = WriteGitignore("a*a*a*a*a*z");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        // All 'a's: the required terminal 'z' is absent, so there is no match.
        string longInput = new('a', 25);

        bool result = await Task.Run(
            () => GitignoreReader.IsIgnored(longInput, longInput, isDirectory: false, patterns),
            TestContext.Current.CancellationToken);
        Assert.False(result, "A pathological pattern with no match must answer false, and promptly.");
    }

    [Fact]
    public void Read_PatternRegex_IsLinear_WithNoTimeout()
    {
        // The pattern regex had a 200 ms match timeout, and IsIgnored read a timeout as
        // "no match". The timeout measured elapsed time, not work.
        string path = WriteGitignore("*.log\n!keep.log\nbuild/\n/dist\n**/cache/**");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);

        Assert.Equal(5, patterns.Count);
        Assert.All(patterns, p =>
        {
            Assert.True(
                p.Regex.Options.HasFlag(System.Text.RegularExpressions.RegexOptions.NonBacktracking),
                $"Pattern {p.RawPattern} must run on NonBacktracking, which is linear in the input length.");
            Assert.Equal(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, p.Regex.MatchTimeout);
        });
    }

    [Fact(Timeout = 30000)]
    // ⓘ The 30 s budget only separates "finished" from "hung". The assertion is the RESULT.
    public async Task IsIgnored_NegatedPathologicalPattern_StillReincludes()
    {
        // The timeout's failure direction: IsIgnored `continue`d past a pattern whose match
        // timed out. On a NEGATED line that drops the re-include, so a file the user kept
        // explicitly was left out of the backup. `**` not beside a slash is "any characters",
        // so the negation compiles to ^a.*a.*a.*a.*a.*Z.*Q$, which backtracks for seconds
        // on a name a hundred characters long.
        string path = WriteGitignore("*\n!a**a**a**a**a**Z**Q");
        IReadOnlyList<GitignorePattern> patterns = GitignoreReader.Read(path);
        string name = "aaaaaZ" + new string('a', 2000) + "Q";

        bool ignored = await Task.Run(
            () => GitignoreReader.IsIgnored(name, "sub/" + name, isDirectory: false, patterns),
            TestContext.Current.CancellationToken);

        Assert.False(ignored, "The negated line matches, so the file must be re-included.");
        Assert.True(
            GitignoreReader.IsIgnored(name + "x", "sub/" + name + "x", isDirectory: false, patterns),
            "A name the negation does not match must stay ignored by '*'.");
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