using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;
using System.Text.RegularExpressions;

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

    // -----------------------------------------------------------------------
    // Linear-time matching, no wall-clock timeout
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToRegex_IsLinear_WithNoTimeout(bool caseInsensitive)
    {
        // Match used a 100 ms timeout and returned "no match" when it fired. For a DENY
        // rule that is fail-open, and the timeout measured elapsed time, not work.
        Regex regex = BashRuleMatcher.ToRegex("npm run test:*", caseInsensitive);

        Assert.True(
            regex.Options.HasFlag(RegexOptions.NonBacktracking),
            "The Bash rule regex must run on NonBacktracking, which is linear in the input length.");
        Assert.Equal(Regex.InfiniteMatchTimeout, regex.MatchTimeout);
    }

    [Fact(Timeout = 30000)]
    // ⓘ The 30 s budget only separates "finished" from "hung". The assertion is the
    // RESULT: the old 100 ms timeout returned false for this matching command, idle or not.
    public async Task Match_PathologicalGlob_LongMatchingCommand_IsTrue()
    {
        // ^.*a.*a.*a.*a.*a.*Z.*Q$ — the greedy stars first try every split among the
        // trailing a's, which never contain the Z, before reaching the early one that does.
        // Backtracking took 429 ms at 51 characters and over 20 s at 86. This is 2,007.
        string command = "aaaaaZ" + new string('a', 2000) + "Q";

        bool matched = await Task.Run(
            () => Match("Bash(*a*a*a*a*a*Z*Q)", command),
            TestContext.Current.CancellationToken);

        Assert.True(matched, "A command that matches the glob must match, however long the search.");
        Assert.False(Match("Bash(*a*a*a*a*a*Z*Q)", command + "x"), "The same glob must still reject a non-match.");
    }

    [Fact]
    public void GlobToRegex_BuildsOnNonBacktracking_AndAgreesWithBacktracking()
    {
        // NonBacktracking throws at construction on a lookaround or backreference, so
        // building every generated pattern proves GlobToRegex emits neither. Comparing
        // against the backtracking engine proves the engine switch changed no answer.
        // Regex metacharacters are in the alphabet because GlobToRegex escapes them.
        Random rng = new(20260930);
        const string globAlphabet = "aB*: ?.\\()[]$^+|{}";
        const string fill = "aBb: .\\()[]$^+|{}\n";
        int matches = 0, nonMatches = 0;

        for (int g = 0; g < 300; g++)
        {
            string glob = RandomString(rng, globAlphabet, rng.Next(1, 9));
            bool caseInsensitive = g % 2 == 1;
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Singleline
                                   | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
            string pattern = BashRuleMatcher.GlobToRegex(glob);

            Regex linear = new(pattern, options | RegexOptions.NonBacktracking);
            Regex backtracking = new(pattern, options);

            for (int i = 0; i < 20; i++)
            {
                // Half the inputs are the glob with each * filled in, so matches occur.
                string input = i % 2 == 0
                    ? string.Concat(glob.Select(c => c == '*' ? RandomString(rng, fill, rng.Next(0, 4)) : c.ToString()))
                    : RandomString(rng, fill, rng.Next(0, 9));
                bool expected = backtracking.IsMatch(input);
                Assert.True(
                    expected == linear.IsMatch(input),
                    $"Engines disagree on glob \"{glob}\" (pattern {pattern}) for input \"{input}\".");
                if (expected)
                {
                    matches++;
                }
                else
                {
                    nonMatches++;
                }
            }
        }

        // The premise: the corpus exercised both answers, not only one.
        Assert.True(matches > 100 && nonMatches > 100, $"Corpus too one-sided: {matches} matches, {nonMatches} non-matches.");
    }

    private static string RandomString(Random rng, string alphabet, int length) =>
        new(Enumerable.Range(0, length).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
}
