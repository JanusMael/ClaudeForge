using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// File-path matching for Read/Edit/Write rules — the four gitignore anchor
/// types and the <c>*</c> vs <c>**</c> depth semantics.
/// </summary>
[TestClass]
public sealed class PathRuleMatcherTests
{
    private static readonly PermissionMatchContext Ctx =
        new(CurrentDirectory: "/proj", ProjectRoot: "/proj", HomeDirectory: "/home/alice");

    private static bool Match(string rule, string path) =>
        PathRuleMatcher.Match(ParsedPermissionRule.Parse(rule), path, Ctx);

    [TestMethod]
    public void BareName_MatchesAtAnyDepth()
    {
        // Read(.env) ≡ Read(**/.env): any .env at or under cwd.
        Assert.IsTrue(Match("Read(.env)", "/proj/.env"));
        Assert.IsTrue(Match("Read(.env)", "/proj/sub/deeper/.env"));
        Assert.IsFalse(Match("Read(.env)", "/other/.env"));
    }

    [TestMethod]
    public void DotSlash_AnchorsToCwdRoot()
    {
        Assert.IsTrue(Match("Read(./.env)", "/proj/.env"));
        Assert.IsFalse(Match("Read(./.env)", "/proj/sub/.env"));
    }

    [TestMethod]
    public void DoubleStar_MatchesRecursively()
    {
        Assert.IsTrue(Match("Read(secrets/**)", "/proj/secrets/a/b.txt"));
        Assert.IsTrue(Match("Read(secrets/**)", "/proj/secrets/x.txt"));
    }

    [TestMethod]
    public void BackslashCandidate_MatchesForwardSlashRule()
    {
        // A Windows-style candidate path (backslashes) matches a forward-slash
        // rule — the matcher normalizes separators on both sides, so the add-time
        // rule normalization and matching agree.
        Assert.IsTrue(Match("Read(src/**)", @"/proj\src\app\main.ts"));
        Assert.IsTrue(Match("Read(src/app/main.ts)", @"/proj\src\app\main.ts"));
    }

    [TestMethod]
    public void WindowsDriveAbsolute_MatchesAsAbsolute()
    {
        // Read(C:\b\d\e) normalizes to Read(C:/b/d/e); it's an absolute drive path,
        // not a cwd-relative pattern, and matches the same candidate path.
        Assert.IsTrue(Match("Read(C:/b/d/e)", @"C:\b\d\e"));
        Assert.IsTrue(Match("Read(C:/b/**)", @"C:\b\d\e"));
        Assert.IsFalse(Match("Read(C:/b/d/e)", @"C:\b\d\x"));
    }

    [TestMethod]
    public void WindowsAbsolutePath_IsTreatedAsAbsolute()
    {
        // A drive-letter rule (as typed, or normalized to forward slashes) is an
        // absolute path, not a cwd-relative pattern. Reported case: Read(C:\b\d\e).
        Assert.IsTrue(Match(@"Read(C:\b\d\e)", @"C:\b\d\e"));
        Assert.IsTrue(Match("Read(C:/b/d/e)", @"C:\b\d\e"));
        Assert.IsTrue(Match("Read(C:/b/d/e)", "C:/b/d/e"));
        // A different drive / path does not match.
        Assert.IsFalse(Match("Read(C:/b/d/e)", @"C:\b\d\f"));
        Assert.IsFalse(Match("Read(C:/b/**)", @"D:\b\d\e"));
        // Recursive form under a Windows absolute base.
        Assert.IsTrue(Match("Read(C:/b/**)", @"C:\b\d\e"));
    }

    [TestMethod]
    public void SingleStar_StaysWithinOneSegment()
    {
        Assert.IsTrue(Match("Read(/logs/*.log)", "/proj/logs/a.log"));
        Assert.IsFalse(Match("Read(/logs/*.log)", "/proj/logs/sub/a.log"));
    }

    [TestMethod]
    public void ProjectRootAnchor_ResolvesAgainstProjectRoot()
    {
        Assert.IsTrue(Match("Edit(/src/**/*.ts)", "/proj/src/a/b.ts"));
        Assert.IsFalse(Match("Edit(/src/**/*.ts)", "/other/src/a/b.ts"));
    }

    [TestMethod]
    public void HomeAnchor_ResolvesAgainstHome_AndIsAnchored()
    {
        Assert.IsTrue(Match("Read(~/.zshrc)", "/home/alice/.zshrc"));
        Assert.IsFalse(Match("Read(~/.zshrc)", "/home/alice/sub/.zshrc"));
        Assert.IsFalse(Match("Read(~/.zshrc)", "/proj/.zshrc"));
    }

    [TestMethod]
    public void AbsoluteAnchor_ResolvesFromFilesystemRoot()
    {
        Assert.IsTrue(Match("Read(//tmp/scratch.txt)", "/tmp/scratch.txt"));
        Assert.IsFalse(Match("Read(//tmp/scratch.txt)", "/proj/tmp/scratch.txt"));
    }

    [TestMethod]
    public void AbsoluteDoubleSlash_WithWindowsDrive_NormalizesAndMatches()
    {
        // //C:/c/cl/** is an absolute path whose remainder is itself a Windows
        // drive path. The drive must normalize to /c (same as candidate paths) so
        // the base matches. Reported case: Allow Read recursively under C:\c\cl.
        Assert.IsTrue(Match("Read(//C:/c/cl/**)", @"C:\c\cl\foo.txt"));
        Assert.IsTrue(Match("Read(//C:/c/cl/**)", @"C:\c\cl\private\secret.txt"));
        Assert.IsTrue(Match("Read(//C:/c/cl/**)", "/c/c/cl/foo.txt")); // POSIX-form candidate
        // Different root / drive must NOT match.
        Assert.IsFalse(Match("Read(//C:/c/cl/**)", @"C:\other\foo.txt"));
        Assert.IsFalse(Match("Read(//C:/c/cl/**)", @"D:\c\cl\foo.txt"));
    }

    [TestMethod]
    public void AbsoluteDoubleSlash_WithBackslashDrive_Matches()
    {
        // As a Windows user actually types it: //C:\c\cl\** — double-slash anchor
        // plus a backslash drive path. Backslashes normalize to forward, drive to /c.
        Assert.IsTrue(Match(@"Read(//C:\c\cl\**)", @"C:\c\cl\deep\nested\x.txt"));
        Assert.IsTrue(Match(@"Read(//C:\c\cl\**)", "/c/c/cl/x.txt"));
    }

    [TestMethod]
    public void AbsoluteDoubleSlash_WindowsDrive_SingleStarVsDoubleStarVsQuestion()
    {
        // * stays within one segment; ** is recursive; ? is one non-slash char —
        // all under a //drive base. Locks the "wildcards behave as globs" contract.
        Assert.IsTrue(Match("Read(//C:/c/cl/*)", @"C:\c\cl\foo.txt"));
        Assert.IsFalse(Match("Read(//C:/c/cl/*)", @"C:\c\cl\sub\foo.txt"));
        Assert.IsTrue(Match("Read(//C:/c/cl/**)", @"C:\c\cl\sub\foo.txt"));
        Assert.IsTrue(Match("Read(//C:/c/cl/file?.txt)", @"C:\c\cl\file1.txt"));
        Assert.IsFalse(Match("Read(//C:/c/cl/file?.txt)", @"C:\c\cl\file12.txt"));
    }

    [TestMethod]
    public void BareTool_MatchesAnyPath()
    {
        Assert.IsTrue(Match("Read", "/anywhere/at/all.txt"));
    }

    [TestMethod]
    public void RelativeCandidate_ResolvesAgainstCwd()
    {
        Assert.IsTrue(Match("Read(.env)", ".env"));
        Assert.IsTrue(Match("Read(/src/a.ts)", "src/a.ts"));
    }

    // ── Case sensitivity is driven by the target filesystem (the context), NOT
    //    the host OS. Both contexts below behave identically regardless of where
    //    the test executes, proving the host-OS static was removed. ───────────
    private static bool MatchWithCase(string rule, string path, bool caseInsensitive)
    {
        PermissionMatchContext ctx = Ctx with { CaseInsensitivePaths = caseInsensitive };
        return PathRuleMatcher.Match(ParsedPermissionRule.Parse(rule), path, ctx);
    }

    [TestMethod]
    public void CaseSensitiveContext_RejectsCaseMismatch_InSubPattern()
    {
        // The sub-pattern segment differs only by case.
        Assert.IsFalse(MatchWithCase("Read(/src/App.ts)", "/proj/src/app.ts", caseInsensitive: false));
        Assert.IsTrue(MatchWithCase("Read(/src/App.ts)", "/proj/src/app.ts", caseInsensitive: true));
    }

    [TestMethod]
    public void CaseSensitiveContext_RejectsCaseMismatch_InBasePrefix()
    {
        // The base-directory prefix (RelativeUnder) differs only by case.
        Assert.IsFalse(MatchWithCase("Read(/secrets/**)", "/PROJ/secrets/k.txt", caseInsensitive: false));
        Assert.IsTrue(MatchWithCase("Read(/secrets/**)", "/PROJ/secrets/k.txt", caseInsensitive: true));
    }

    [TestMethod]
    public void DefaultContext_UsesHostConvention()
    {
        // A context that does not set the flag inherits the host-OS default, so a
        // case-exact path matches on every platform.
        Assert.AreEqual(
            PermissionMatchContext.HostIsCaseInsensitive,
            new PermissionMatchContext("/proj", "/proj", "/home/alice").CaseInsensitivePaths);
        Assert.IsTrue(Match("Read(/src/app.ts)", "/proj/src/app.ts"));
    }
}
