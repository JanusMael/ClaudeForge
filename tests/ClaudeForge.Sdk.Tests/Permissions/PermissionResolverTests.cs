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
    public void Read_DenySubHierarchyUnderAllowRoot_DenyWins()
    {
        // Reported scenario: allow Read recursively under a Windows root, deny a
        // private sub-hierarchy. Deny must take precedence for files under private;
        // files elsewhere under the root stay allowed.
        PermissionDecision underRoot = Resolve(
            PermissionCandidate.Read(@"C:\c\cl\notes.txt"),
            allow: ["Read(//C:/c/cl/**)"],
            deny: ["Read(//C:/c/cl/private/**)"]);
        Assert.AreEqual(PermissionOutcome.Allow, underRoot.Outcome);

        PermissionDecision underPrivate = Resolve(
            PermissionCandidate.Read(@"C:\c\cl\private\secret.txt"),
            allow: ["Read(//C:/c/cl/**)"],
            deny: ["Read(//C:/c/cl/private/**)"]);
        Assert.AreEqual(PermissionOutcome.Deny, underPrivate.Outcome);
        Assert.AreEqual("Read(//C:/c/cl/private/**)", underPrivate.MatchedRule!.Value);
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

    // ── B3: PowerShell compound resolution (case-insensitive subcommands) ────

    [TestMethod]
    public void Compound_PowerShell_AllSubsAllowed_CaseInsensitive()
    {
        // Each subcommand is rebuilt as a PowerShell candidate (WithCommand), so
        // matching is case-insensitive — 'Remove-Item' matches a lowercase rule.
        PermissionDecision d = Resolve(
            PermissionCandidate.PowerShell("Get-ChildItem; Remove-Item x"),
            allow: ["PowerShell(get-childitem*)", "PowerShell(remove-item*)"]);
        Assert.AreEqual(PermissionOutcome.Allow, d.Outcome);
    }

    [TestMethod]
    public void Compound_PowerShell_OneSubDenied_Denies()
    {
        PermissionDecision d = Resolve(
            PermissionCandidate.PowerShell("Get-ChildItem; Remove-Item x"),
            allow: ["PowerShell(get-childitem*)", "PowerShell(remove-item*)"],
            deny: ["PowerShell(remove-item*)"]);
        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
        StringAssert.Contains(d.DecidingSubcommand!, "Remove-Item");
    }

    [TestMethod]
    public void Compound_BashCandidate_DoesNotMatch_PowerShellCasedRules()
    {
        // A Bash candidate must NOT match PowerShell rules: wrong tool, and Bash
        // matching is case-sensitive ('Remove-Item' ≠ 'remove-item').
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("Remove-Item x"),
            allow: ["PowerShell(remove-item*)"]);
        Assert.AreEqual(PermissionOutcome.Default, d.Outcome);
    }

    // ── B7: duplicate deny across scopes → highest-precedence attribution ────

    [TestMethod]
    public void Merged_DuplicateDenyInMultipleScopes_AttributedToHighestPrecedence()
    {
        // Identical deny in BOTH Managed and User. The resolver orders scopes by
        // precedence, so the Managed group is checked first and wins attribution —
        // even though User is listed first in the input.
        var scopes = new List<ScopedPermissionRules>
        {
            new(ConfigScope.User, Rules(), Rules("Bash(git push *)"), Rules()),
            new(ConfigScope.Managed, Rules(), Rules("Bash(git push *)"), Rules()),
        };

        PermissionDecision d = PermissionResolver.ResolveMerged(
            PermissionCandidate.Bash("git push origin main"),
            scopes,
            PermissionDefaultMode.Default,
            Ctx);

        Assert.AreEqual(PermissionOutcome.Deny, d.Outcome);
        Assert.AreEqual(ConfigScope.Managed, d.MatchedScope);
    }

    // ── B8: compound restrictiveness ranking (Ask > Default > Allow) ─────────

    [TestMethod]
    public void Compound_Ask_WhenOneSubAskedAndAnotherAllowed()
    {
        // Ask(2) outranks Allow(0): a compound with one ask'd sub and one allowed
        // sub resolves to Ask.
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("npm test && rm file"),
            allow: ["Bash(npm test)"],
            ask: ["Bash(rm *)"]);
        Assert.AreEqual(PermissionOutcome.Ask, d.Outcome);
        StringAssert.Contains(d.DecidingSubcommand!, "rm");
    }

    [TestMethod]
    public void Compound_Ask_BeatsDefault_WhenOtherSubUnmatched()
    {
        // Ask(2) outranks Default(1): one ask'd sub + one unmatched (Default) sub
        // resolves to Ask, attributed to the ask'd subcommand.
        PermissionDecision d = Resolve(
            PermissionCandidate.Bash("rm file && obscure-cmd"),
            ask: ["Bash(rm *)"]);
        Assert.AreEqual(PermissionOutcome.Ask, d.Outcome);
        StringAssert.Contains(d.DecidingSubcommand!, "rm");
    }
}
