using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// End-to-end resolution: deny&gt;ask&gt;allow precedence, defaultMode
/// fallthrough, compound-command protection, wrapper stripping, and merged
/// cross-scope attribution.
/// </summary>
[TestClass]
public sealed class PermissionResolverTests
{
    private static readonly PermissionMatchContext Ctx =
        new(CurrentDirectory: "/proj", ProjectRoot: "/proj", HomeDirectory: "/home/alice");

    private static IReadOnlyList<PermissionRule> Rules(params string[] rules) =>
        rules.Select(PermissionRule.Parse).ToList();

    private static PermissionDecision Resolve(
        PermissionCandidate candidate,
        string[]? allow = null,
        string[]? deny = null,
        string[]? ask = null,
        PermissionDefaultMode? defaultMode = PermissionDefaultMode.Default) =>
        PermissionResolver.Resolve(
            candidate,
            Rules(allow ?? []),
            Rules(deny ?? []),
            Rules(ask ?? []),
            defaultMode,
            Ctx);

    [TestMethod]
    public void Allow_WhenOnlyAllowMatches()
    {
        PermissionDecision d = Resolve(PermissionCandidate.Bash("npm test"), allow: ["Bash(npm *)"]);
        Assert.AreEqual(PermissionOutcome.Allow, d.Outcome);
        Assert.AreEqual(PermissionBucket.Allow, d.MatchedBucket);
        Assert.AreEqual("Bash(npm *)", d.MatchedRule!.Value);
    }

    [TestMethod]
    public void Deny_BeatsAllow_EvenWhenBothMatch()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("git push origin main"),
            allow: ["Bash(git *)"],
            deny: ["Bash(git push *)"]);
        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
        Assert.AreEqual("Bash(git push *)", d.MatchedRule!.Value);
    }

    [TestMethod]
    public void Ask_WhenOnlyAskMatches()
    {
        PermissionDecision d = Resolve(PermissionCandidate.Bash("rm file"), ask: ["Bash(rm *)"]);
        Assert.AreEqual(PermissionOutcome.Ask, d.Outcome);
    }

    [TestMethod]
    public void Default_WhenNoRuleMatches()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("obscure-cmd"),
            allow: ["Bash(npm *)"],
            defaultMode: PermissionDefaultMode.AcceptEdits);
        Assert.AreEqual(PermissionOutcome.Default, d.Outcome);
        Assert.IsNull(d.MatchedRule);
        Assert.AreEqual(PermissionDefaultMode.AcceptEdits, d.DefaultMode);
    }

    [TestMethod]
    public void Compound_NotAllowed_WhenOneSubcommandUnmatched()
    {
        // The "chaining guard": an allow for npm test does NOT permit the whole
        // compound because the rm subcommand matches nothing.
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("npm test && rm -rf /"),
            allow: ["Bash(npm *)"]);
        Assert.AreNotEqual(PermissionOutcome.Allow, d.Outcome);
        Assert.AreEqual(PermissionOutcome.Default, d.Outcome);
        StringAssert.Contains(d.DecidingSubcommand!, "rm");
    }

    [TestMethod]
    public void Compound_Denied_WhenOneSubcommandDenied()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("npm test && git push origin main"),
            allow: ["Bash(npm test *)", "Bash(git *)"],
            deny: ["Bash(git push *)"]);
        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
    }

    [TestMethod]
    public void Compound_Allowed_WhenEverySubcommandAllowed()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("git status && npm test"),
            allow: ["Bash(git status)", "Bash(npm test)"]);
        Assert.AreEqual(PermissionOutcome.Allow, d.Outcome);
    }

    [TestMethod]
    public void Wrapper_Stripped_BeforeMatching()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("timeout 30 npm test foo"),
            allow: ["Bash(npm test *)"]);
        Assert.AreEqual(PermissionOutcome.Allow, d.Outcome);
    }

    [TestMethod]
    public void Merged_DenyInHigherScope_BeatsAllowInLowerScope()
    {
        var scopes = new List<ScopedPermissionRules>
        {
            new(ConfigScope.Managed, Rules(), Rules("Bash(git push *)"), Rules()),
            new(ConfigScope.User, Rules("Bash(git push *)"), Rules(), Rules()),
        };

        PermissionDecision d = PermissionResolver.ResolveMerged(
            PermissionCandidate.Bash("git push origin main"),
            scopes,
            PermissionDefaultMode.Default,
            Ctx);

        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
        Assert.AreEqual(ConfigScope.Managed, d.MatchedScope);
    }

    [TestMethod]
    public void Merged_AllowAttributedToHighestPrecedenceScope()
    {
        // Same allow rule in Project (2) and User (3); Project wins attribution.
        var scopes = new List<ScopedPermissionRules>
        {
            new(ConfigScope.User, Rules("Bash(npm *)"), Rules(), Rules()),
            new(ConfigScope.Project, Rules("Bash(npm *)"), Rules(), Rules()),
        };

        PermissionDecision d = PermissionResolver.ResolveMerged(
            PermissionCandidate.Bash("npm test"),
            scopes,
            PermissionDefaultMode.Default,
            Ctx);

        Assert.AreEqual(PermissionOutcome.Allow, d.Outcome);
        Assert.AreEqual(ConfigScope.Project, d.MatchedScope);
    }

    [TestMethod]
    public void Merged_DenyCheckedAcrossAllScopes_BeforeAnyAllow()
    {
        // Deny lives in the LOWEST-precedence scope (User); allow in the highest
        // (Managed). Deny must still win — bucket order dominates scope order.
        var scopes = new List<ScopedPermissionRules>
        {
            new(ConfigScope.Managed, Rules("Bash(git *)"), Rules(), Rules()),
            new(ConfigScope.User, Rules(), Rules("Bash(git push *)"), Rules()),
        };

        PermissionDecision d = PermissionResolver.ResolveMerged(
            PermissionCandidate.Bash("git push origin main"),
            scopes,
            PermissionDefaultMode.Default,
            Ctx);

        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
        Assert.AreEqual(ConfigScope.User, d.MatchedScope);
    }

    [TestMethod]
    public void PathCandidate_EditRuleAppliesToWrite()
    {
        // Spec: Edit rules apply to all file-editing tools, including Write.
        PermissionDecision d = Resolve(
            PermissionCandidate.Write("/proj/src/a.ts"),
            deny: ["Edit(/src/**)"]);
        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
    }
}
