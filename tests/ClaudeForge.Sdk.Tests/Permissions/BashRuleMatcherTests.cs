using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Bash glob matching for a single (already split + wrapper-stripped) command.
/// Locks the spec's documented behaviors: any-position <c>*</c>, the space
/// word-boundary, the <c>:*</c> ≡ trailing <c> *</c> equivalence, and literal
/// non-wildcard characters.
/// </summary>
[TestClass]
public sealed class BashRuleMatcherTests
{
    private static bool Match(string rule, string command) =>
        BashRuleMatcher.Match(ParsedPermissionRule.Parse(rule), command);

    [TestMethod]
    public void Exact_MatchesOnlyExactCommand()
    {
        Assert.IsTrue(Match("Bash(npm run build)", "npm run build"));
        Assert.IsFalse(Match("Bash(npm run build)", "npm run build --watch"));
    }

    [TestMethod]
    public void TrailingSpaceStar_EnforcesWordBoundary()
    {
        // Spec: Bash(ls *) matches "ls -la" but not "lsof".
        Assert.IsTrue(Match("Bash(ls *)", "ls -la"));
        Assert.IsFalse(Match("Bash(ls *)", "lsof"));
    }

    [TestMethod]
    public void TrailingWildcard_MeansOptionalArguments()
    {
        // The trailing wildcard (":*" or " *") means "the prefix, optionally
        // followed by arguments" — so the BARE command matches too. This is the
        // canonical Claude "any args including none" semantics and fixes the
        // reported Bash(git push *) ✗ "git push" case.
        Assert.IsTrue(Match("Bash(git push:*)", "git push"));
        Assert.IsTrue(Match("Bash(git push *)", "git push"));
        Assert.IsTrue(Match("Bash(git push:*)", "git push origin main"));
        Assert.IsTrue(Match("Bash(ls *)", "ls"));
        Assert.IsTrue(Match("Bash(ls:*)", "ls"));
        // The space boundary is still preserved: no match on a longer token.
        Assert.IsFalse(Match("Bash(git push:*)", "git pushx"));
    }

    [TestMethod]
    public void NoSpaceStar_HasNoWordBoundary()
    {
        // Spec: Bash(ls*) matches both "ls -la" and "lsof".
        Assert.IsTrue(Match("Bash(ls*)", "ls -la"));
        Assert.IsTrue(Match("Bash(ls*)", "lsof"));
    }

    [TestMethod]
    public void Wildcard_MatchesAtAnyPosition()
    {
        Assert.IsTrue(Match("Bash(* install)", "npm install"));
        Assert.IsTrue(Match("Bash(* install)", "pip install"));
        Assert.IsTrue(Match("Bash(git * main)", "git checkout main"));
        Assert.IsTrue(Match("Bash(git * main)", "git push origin main"));
        Assert.IsFalse(Match("Bash(git * main)", "git checkout dev"));
    }

    [TestMethod]
    public void WildcardSpansSpaces()
    {
        // A single * matches multiple arguments.
        Assert.IsTrue(Match("Bash(git *)", "git log --oneline --all"));
    }

    [TestMethod]
    public void ColonStarSuffix_EquivalentToSpaceStar()
    {
        // Spec: Bash(ls:*) matches the same as Bash(ls *).
        Assert.IsTrue(Match("Bash(ls:*)", "ls -la"));
        Assert.IsFalse(Match("Bash(ls:*)", "lsof"));
    }

    [TestMethod]
    public void MidPatternColon_IsLiteral()
    {
        // Spec: in Bash(git:* push) the colon is literal and won't match git
        // commands like "git push".
        Assert.IsFalse(Match("Bash(git:* push)", "git push"));
        // It does match a literal "git:" prefix.
        Assert.IsTrue(Match("Bash(git:* push)", "git:anything push"));
    }

    [TestMethod]
    public void BareTool_MatchesAnyCommand()
    {
        Assert.IsTrue(Match("Bash", "rm -rf /"));
        Assert.IsTrue(Match("Bash(*)", "anything at all"));
    }

    [TestMethod]
    public void PowerShell_IsCaseInsensitive()
    {
        ParsedPermissionRule rule = ParsedPermissionRule.Parse("PowerShell(Get-ChildItem *)");
        Assert.IsTrue(BashRuleMatcher.Match(rule, "get-childitem -force", caseInsensitive: true));
        // Bash (case-sensitive) would not match the lowercased form.
        Assert.IsFalse(BashRuleMatcher.Match(rule, "get-childitem -force", caseInsensitive: false));
    }

    [TestMethod]
    public void SingleCommandMatcher_DoesNotSplitCompound()
    {
        // At the single-command level, "*" spans the "&&" too — which is exactly
        // why compound protection lives in the resolver, not here. This test
        // documents that boundary (see PermissionResolverTests for the guard).
        Assert.IsTrue(Match("Bash(npm test *)", "npm test && rm -rf /"));
    }
}
