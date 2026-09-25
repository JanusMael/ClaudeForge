using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// File-path matching for Read/Edit/Write rules — the four gitignore anchor
/// types and the <c>*</c> vs <c>**</c> depth semantics.
/// </summary>
public sealed class PathRuleMatcherTests
{
    private static readonly PermissionMatchContext Ctx =
        new(CurrentDirectory: "/proj", ProjectRoot: "/proj", HomeDirectory: "/home/alice");

    private static bool Match(string rule, string path) =>
        PathRuleMatcher.Match(ParsedPermissionRule.Parse(rule), path, Ctx);

    [Fact]
    public void BareName_MatchesAtAnyDepth()
    {
        // Read(.env) ≡ Read(**/.env): any .env at or under cwd.
        Assert.True(Match("Read(.env)", "/proj/.env"));
        Assert.True(Match("Read(.env)", "/proj/sub/deeper/.env"));
        Assert.False(Match("Read(.env)", "/other/.env"));
    }

    [Fact]
    public void DotSlash_AnchorsToCwdRoot()
    {
        Assert.True(Match("Read(./.env)", "/proj/.env"));
        Assert.False(Match("Read(./.env)", "/proj/sub/.env"));
    }

    [Fact]
    public void DoubleStar_MatchesRecursively()
    {
        Assert.True(Match("Read(secrets/**)", "/proj/secrets/a/b.txt"));
        Assert.True(Match("Read(secrets/**)", "/proj/secrets/x.txt"));
    }

    [Fact]
    public void BackslashCandidate_MatchesForwardSlashRule()
    {
        // A Windows-style candidate path (backslashes) matches a forward-slash
        // rule — the matcher normalizes separators on both sides, so the add-time
        // rule normalization and matching agree.
        Assert.True(Match("Read(src/**)", @"/proj\src\app\main.ts"));
        Assert.True(Match("Read(src/app/main.ts)", @"/proj\src\app\main.ts"));
    }

    [Fact]
    public void WindowsDriveAbsolute_MatchesAsAbsolute()
    {
        // Read(C:\b\d\e) normalizes to Read(C:/b/d/e); it's an absolute drive path,
        // not a cwd-relative pattern, and matches the same candidate path.
        Assert.True(Match("Read(C:/b/d/e)", @"C:\b\d\e"));
        Assert.True(Match("Read(C:/b/**)", @"C:\b\d\e"));
        Assert.False(Match("Read(C:/b/d/e)", @"C:\b\d\x"));
    }

    [Fact]
    public void WindowsAbsolutePath_IsTreatedAsAbsolute()
    {
        // A drive-letter rule (as typed, or normalized to forward slashes) is an
        // absolute path, not a cwd-relative pattern. Reported case: Read(C:\b\d\e).
        Assert.True(Match(@"Read(C:\b\d\e)", @"C:\b\d\e"));
        Assert.True(Match("Read(C:/b/d/e)", @"C:\b\d\e"));
        Assert.True(Match("Read(C:/b/d/e)", "C:/b/d/e"));
        // A different drive / path does not match.
        Assert.False(Match("Read(C:/b/d/e)", @"C:\b\d\f"));
        Assert.False(Match("Read(C:/b/**)", @"D:\b\d\e"));
        // Recursive form under a Windows absolute base.
        Assert.True(Match("Read(C:/b/**)", @"C:\b\d\e"));
    }

    [Fact]
    public void SingleStar_StaysWithinOneSegment()
    {
        Assert.True(Match("Read(/logs/*.log)", "/proj/logs/a.log"));
        Assert.False(Match("Read(/logs/*.log)", "/proj/logs/sub/a.log"));
    }

    [Fact]
    public void ProjectRootAnchor_ResolvesAgainstProjectRoot()
    {
        Assert.True(Match("Edit(/src/**/*.ts)", "/proj/src/a/b.ts"));
        Assert.False(Match("Edit(/src/**/*.ts)", "/other/src/a/b.ts"));
    }

    [Fact]
    public void HomeAnchor_ResolvesAgainstHome_AndIsAnchored()
    {
        Assert.True(Match("Read(~/.zshrc)", "/home/alice/.zshrc"));
        Assert.False(Match("Read(~/.zshrc)", "/home/alice/sub/.zshrc"));
        Assert.False(Match("Read(~/.zshrc)", "/proj/.zshrc"));
    }

    [Fact]
    public void AbsoluteAnchor_ResolvesFromFilesystemRoot()
    {
        Assert.True(Match("Read(//tmp/scratch.txt)", "/tmp/scratch.txt"));
        Assert.False(Match("Read(//tmp/scratch.txt)", "/proj/tmp/scratch.txt"));
    }

    [Fact]
    public void AbsoluteDoubleSlash_WithWindowsDrive_NormalizesAndMatches()
    {
        // //C:/c/cl/** is an absolute path whose remainder is itself a Windows
        // drive path. The drive must normalize to /c (same as candidate paths) so
        // the base matches. Reported case: Allow Read recursively under C:\c\cl.
        Assert.True(Match("Read(//C:/c/cl/**)", @"C:\c\cl\foo.txt"));
        Assert.True(Match("Read(//C:/c/cl/**)", @"C:\c\cl\private\secret.txt"));
        Assert.True(Match("Read(//C:/c/cl/**)", "/c/c/cl/foo.txt")); // POSIX-form candidate
        // Different root / drive must NOT match.
        Assert.False(Match("Read(//C:/c/cl/**)", @"C:\other\foo.txt"));
        Assert.False(Match("Read(//C:/c/cl/**)", @"D:\c\cl\foo.txt"));
    }

    [Fact]
    public void AbsoluteDoubleSlash_WithBackslashDrive_Matches()
    {
        // As a Windows user actually types it: //C:\c\cl\** — double-slash anchor
        // plus a backslash drive path. Backslashes normalize to forward, drive to /c.
        Assert.True(Match(@"Read(//C:\c\cl\**)", @"C:\c\cl\deep\nested\x.txt"));
        Assert.True(Match(@"Read(//C:\c\cl\**)", "/c/c/cl/x.txt"));
    }

    [Fact]
    public void AbsoluteDoubleSlash_WindowsDrive_SingleStarVsDoubleStarVsQuestion()
    {
        // * stays within one segment; ** is recursive; ? is one non-slash char —
        // all under a //drive base. Locks the "wildcards behave as globs" contract.
        Assert.True(Match("Read(//C:/c/cl/*)", @"C:\c\cl\foo.txt"));
        Assert.False(Match("Read(//C:/c/cl/*)", @"C:\c\cl\sub\foo.txt"));
        Assert.True(Match("Read(//C:/c/cl/**)", @"C:\c\cl\sub\foo.txt"));
        Assert.True(Match("Read(//C:/c/cl/file?.txt)", @"C:\c\cl\file1.txt"));
        Assert.False(Match("Read(//C:/c/cl/file?.txt)", @"C:\c\cl\file12.txt"));
    }

    [Fact]
    public void BareTool_MatchesAnyPath()
    {
        Assert.True(Match("Read", "/anywhere/at/all.txt"));
    }

    [Fact]
    public void RelativeCandidate_ResolvesAgainstCwd()
    {
        Assert.True(Match("Read(.env)", ".env"));
        Assert.True(Match("Read(/src/a.ts)", "src/a.ts"));
    }

    // ── Case sensitivity is driven by the target filesystem (the context), NOT
    //    the host OS. Both contexts below behave identically regardless of where
    //    the test executes, proving the host-OS static was removed. ───────────
    private static bool MatchWithCase(string rule, string path, bool caseInsensitive)
    {
        PermissionMatchContext ctx = Ctx with { CaseInsensitivePaths = caseInsensitive };
        return PathRuleMatcher.Match(ParsedPermissionRule.Parse(rule), path, ctx);
    }

    [Fact]
    public void CaseSensitiveContext_RejectsCaseMismatch_InSubPattern()
    {
        // The sub-pattern segment differs only by case.
        Assert.False(MatchWithCase("Read(/src/App.ts)", "/proj/src/app.ts", caseInsensitive: false));
        Assert.True(MatchWithCase("Read(/src/App.ts)", "/proj/src/app.ts", caseInsensitive: true));
    }

    [Fact]
    public void CaseSensitiveContext_RejectsCaseMismatch_InBasePrefix()
    {
        // The base-directory prefix (RelativeUnder) differs only by case.
        Assert.False(MatchWithCase("Read(/secrets/**)", "/PROJ/secrets/k.txt", caseInsensitive: false));
        Assert.True(MatchWithCase("Read(/secrets/**)", "/PROJ/secrets/k.txt", caseInsensitive: true));
    }

    [Fact]
    public void DefaultContext_UsesHostConvention()
    {
        // A context that does not set the flag inherits the host-OS default, so a
        // case-exact path matches on every platform.
        Assert.Equal(
            PermissionMatchContext.HostIsCaseInsensitive,
            new PermissionMatchContext("/proj", "/proj", "/home/alice").CaseInsensitivePaths);
        Assert.True(Match("Read(/src/app.ts)", "/proj/src/app.ts"));
    }

    // -----------------------------------------------------------------------
    // `**/` spans WHOLE SEGMENTS
    //
    // ⛔ This shipped wrong, inherited from GitignoreReader.PatternToRegex, which this
    // file's own comment says it was adapted from. `**/` emitted `.*`, so `**/foo`
    // compiled to `^.*foo$` and matched `barfoo`. On a permission surface that meant an
    // `allow` rule granting more than it said.
    // -----------------------------------------------------------------------

    [Fact]
    public void DoubleStar_DoesNotMatchPartialSegment()
    {
        // `secrets` is a whole segment. `notsecrets` is not, and never was.
        Assert.False(
            Match("Read(**/secrets/key.txt)", "/proj/notsecrets/key.txt"),
            "`**/` is a segment boundary, not a character run.");
        Assert.False(
            Match("Edit(/src/**/app.ts)", "/proj/src/notapp.ts"),
            "A mid-pattern `**/` must not swallow part of the following segment.");
    }

    [Fact]
    public void DoubleStar_StillMatchesAtZeroAndAnyDepth()
    {
        // The behaviour the fix must NOT break: `**/` includes the zero-segment case.
        Assert.True(Match("Read(**/secrets/key.txt)", "/proj/secrets/key.txt"));
        Assert.True(Match("Read(**/secrets/key.txt)", "/proj/a/b/secrets/key.txt"));
        Assert.True(Match("Edit(/src/**/app.ts)", "/proj/src/app.ts"));
        Assert.True(Match("Edit(/src/**/app.ts)", "/proj/src/x/y/app.ts"));
    }

    [Fact]
    public void TrailingDoubleStar_IsStillAnyCharacters()
    {
        // ⚠ A trailing `**` is NOT the segment rule — `secrets/**` means everything
        // beneath, and the fix deliberately keeps emitting `.*` for it. Without this
        // distinction the correction would break every prefix rule in the suite above.
        Assert.True(Match("Read(secrets/**)", "/proj/secrets/x.txt"));
        Assert.True(Match("Read(secrets/**)", "/proj/secrets/a/b.txt"));
    }
}
