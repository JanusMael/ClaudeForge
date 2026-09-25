using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Bash glob matching for a single (already split + wrapper-stripped) command.
/// Locks the spec's documented behaviors: any-position <c>*</c>, the space
/// word-boundary, the <c>:*</c> ≡ trailing <c> *</c> equivalence, and literal
/// non-wildcard characters.
/// </summary>
public sealed class BashRuleMatcherTests
{
    private static bool Match(string rule, string command) =>
        BashRuleMatcher.Match(ParsedPermissionRule.Parse(rule), command);

    [Fact]
    public void Exact_MatchesOnlyExactCommand()
    {
        Assert.True(Match("Bash(npm run build)", "npm run build"));
        Assert.False(Match("Bash(npm run build)", "npm run build --watch"));
    }

    [Fact]
    public void TrailingSpaceStar_EnforcesWordBoundary()
    {
        // Spec: Bash(ls *) matches "ls -la" but not "lsof".
        Assert.True(Match("Bash(ls *)", "ls -la"));
        Assert.False(Match("Bash(ls *)", "lsof"));
    }

    [Fact]
    public void TrailingWildcard_MeansOptionalArguments()
    {
        // The trailing wildcard (":*" or " *") means "the prefix, optionally
        // followed by arguments" — so the BARE command matches too. This is the
        // canonical Claude "any args including none" semantics and fixes the
        // reported Bash(git push *) ✗ "git push" case.
        Assert.True(Match("Bash(git push:*)", "git push"));
        Assert.True(Match("Bash(git push *)", "git push"));
        Assert.True(Match("Bash(git push:*)", "git push origin main"));
        Assert.True(Match("Bash(ls *)", "ls"));
        Assert.True(Match("Bash(ls:*)", "ls"));
        // The space boundary is still preserved: no match on a longer token.
        Assert.False(Match("Bash(git push:*)", "git pushx"));
    }

    [Fact]
    public void NoSpaceStar_HasNoWordBoundary()
    {
        // Spec: Bash(ls*) matches both "ls -la" and "lsof".
        Assert.True(Match("Bash(ls*)", "ls -la"));
        Assert.True(Match("Bash(ls*)", "lsof"));
    }

    [Fact]
    public void Wildcard_MatchesAtAnyPosition()
    {
        Assert.True(Match("Bash(* install)", "npm install"));
        Assert.True(Match("Bash(* install)", "pip install"));
        Assert.True(Match("Bash(git * main)", "git checkout main"));
        Assert.True(Match("Bash(git * main)", "git push origin main"));
        Assert.False(Match("Bash(git * main)", "git checkout dev"));
    }

    [Fact]
    public void WildcardSpansSpaces()
    {
        // A single * matches multiple arguments.
        Assert.True(Match("Bash(git *)", "git log --oneline --all"));
    }

    [Fact]
    public void ColonStarSuffix_MatchesBare_ColonSuffix_AndSpaceArgs()
    {
        // ":*" is the canonical prefix form: bare, a ':'-suffixed subcommand, OR space args.
        Assert.True(Match("Bash(npm run test:*)", "npm run test"), "bare prefix matches");
        Assert.True(Match("Bash(npm run test:*)", "npm run test:unit"), "colon-suffixed subcommand matches (the bug fix)");
        Assert.True(Match("Bash(npm run test:*)", "npm run test:e2e"));
        Assert.True(Match("Bash(npm run test:*)", "npm run test --watch"), "space args match");
        Assert.False(Match("Bash(npm run test:*)", "npm run testify"), "no ':' or space delimiter → not a subcommand");

        // It also behaves like space-star for the bare/space cases (back-compat).
        Assert.True(Match("Bash(ls:*)", "ls -la"));
        Assert.False(Match("Bash(ls:*)", "lsof"));
    }

    [Fact]
    public void SpaceStarSuffix_IsBareOrSpaceArgs_NotColonSuffix()
    {
        // " *" excludes the colon-suffix that ":*" allows.
        Assert.True(Match("Bash(git push *)", "git push"));
        Assert.True(Match("Bash(git push *)", "git push origin main"));
        Assert.False(Match("Bash(git push *)", "git pushx"));
        Assert.False(Match("Bash(git push *)", "git push:weird"), "space-star does not match a colon suffix.");
    }

    [Fact]
    public void MidPatternColon_IsLiteral()
    {
        // Spec: in Bash(git:* push) the colon is literal and won't match git
        // commands like "git push".
        Assert.False(Match("Bash(git:* push)", "git push"));
        // It does match a literal "git:" prefix.
        Assert.True(Match("Bash(git:* push)", "git:anything push"));
    }

    [Fact]
    public void BareTool_MatchesAnyCommand()
    {
        Assert.True(Match("Bash", "rm -rf /"));
        Assert.True(Match("Bash(*)", "anything at all"));
    }

    [Fact]
    public void PowerShell_IsCaseInsensitive()
    {
        ParsedPermissionRule rule = ParsedPermissionRule.Parse("PowerShell(Get-ChildItem *)");
        Assert.True(BashRuleMatcher.Match(rule, "get-childitem -force", caseInsensitive: true));
        // Bash (case-sensitive) would not match the lowercased form.
        Assert.False(BashRuleMatcher.Match(rule, "get-childitem -force", caseInsensitive: false));
    }

    [Fact]
    public void SingleCommandMatcher_DoesNotSplitCompound()
    {
        // At the single-command level, "*" spans the "&&" too — which is exactly
        // why compound protection lives in the resolver, not here. This test
        // documents that boundary (see PermissionResolverTests for the guard).
        Assert.True(Match("Bash(npm test *)", "npm test && rm -rf /"));
    }
}
