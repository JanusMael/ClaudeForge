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
}
