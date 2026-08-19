using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The permission model: both config shapes, the five action-only tools, last-match-wins
/// resolution, and the merge-inversion hazard S1 measured.
/// </summary>
[TestClass]
public class OpenCodePermissionModelTests
{
    private static OpenCodePermissionModel Parse(string json)
        => OpenCodePermissionModel.Parse(JsonNode.Parse(json));

    // ── shapes ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The bare-string form applies one action to every tool. It is the "apply one rule to
    /// everything" mode, and <c>"allow"</c> here is the direct analogue of Claude's
    /// bypass-permissions switch.
    /// </summary>
    [TestMethod]
    public void BareAction_AppliesToEveryTool()
    {
        OpenCodePermissionModel model = Parse("\"deny\"");

        Assert.AreEqual(PermissionOutcome.Deny, model.GlobalAction);
        Assert.AreEqual(PermissionOutcome.Deny, model.Resolve("bash", "anything").Outcome);
        Assert.AreEqual(PermissionOutcome.Deny, model.Resolve("read", "/etc/passwd").Outcome);
    }

    [TestMethod]
    public void PerToolBareAction_IsHonoured()
    {
        OpenCodePermissionModel model = Parse("""{ "bash": "ask", "read": "allow" }""");

        Assert.AreEqual(PermissionOutcome.Ask, model.Resolve("bash", "ls").Outcome);
        Assert.AreEqual(PermissionOutcome.Allow, model.Resolve("read", "/tmp/x").Outcome);
    }

    /// <summary>
    /// An arbitrary key — an MCP tool name — is as valid as a named one. The schema allows it
    /// through <c>additionalProperties</c>, so a model that only knew the 15 named tools would
    /// silently ignore every MCP rule.
    /// </summary>
    [TestMethod]
    public void ArbitraryToolKeys_AreSupported()
    {
        OpenCodePermissionModel model = Parse("""{ "mcp__github__create_issue": "deny" }""");

        Assert.AreEqual(
            PermissionOutcome.Deny,
            model.Resolve("mcp__github__create_issue", "anything").Outcome);
    }

    [TestMethod]
    public void NoPermissionKey_ResolvesToDefault_NotToAllowed()
    {
        OpenCodePermissionDecision decision = OpenCodePermissionModel.Empty.Resolve("bash", "rm -rf /");

        Assert.AreEqual(PermissionOutcome.Default, decision.Outcome,
            "No rule matched says only that nothing decided it. Reporting Allow here would "
            + "turn 'unconfigured' into 'permitted'.");
    }

    // ── the five action-only tools ───────────────────────────────────────────

    /// <summary>
    /// ⚠ Five, not the four the plan lists. <c>doom_loop</c> is typed action-only by the
    /// bundled schema too, which is why this list is read from the schema rather than prose.
    /// </summary>
    [TestMethod]
    [DataRow("todowrite")]
    [DataRow("question")]
    [DataRow("webfetch")]
    [DataRow("websearch")]
    [DataRow("doom_loop")]
    public void ActionOnlyTools_RejectThePatternObjectForm(string tool)
    {
        Assert.ThrowsExactly<FormatException>(
            () => Parse($$"""{ "{{tool}}": { "*": "allow" } }"""),
            $"'{tool}' takes an action string only; the schema gives it no object form.");
    }

    [TestMethod]
    [DataRow("todowrite")]
    [DataRow("doom_loop")]
    public void ActionOnlyTools_StillAcceptABareAction(string tool)
    {
        Assert.AreEqual(
            PermissionOutcome.Ask,
            Parse($$"""{ "{{tool}}": "ask" }""").Resolve(tool, "").Outcome);
    }

    // ── invalid input is rejected, never dropped ─────────────────────────────

    /// <summary>
    /// A malformed rule throws rather than being skipped. A permission entry that silently
    /// disappears is one the user believes is still protecting them.
    /// </summary>
    [TestMethod]
    [DataRow("""{ "bash": "sometimes" }""")]
    [DataRow("""{ "bash": 42 }""")]
    [DataRow("""{ "bash": { "git *": "maybe" } }""")]
    [DataRow("""{ "bash": [] }""")]
    [DataRow("\"nope\"")]
    public void MalformedRules_Throw(string json)
    {
        Assert.ThrowsExactly<FormatException>(() => Parse(json));
    }

    // ── ordering: the whole point ────────────────────────────────────────────

    /// <summary>
    /// Key order survives parsing. If it did not, everything below would be meaningless.
    /// </summary>
    /// <remarks>
    /// ⚠ The fixture is chosen so that declared order and alphabetical order <b>differ</b>.
    /// A first version used <c>"*"</c>, <c>"git *"</c>, <c>"git push *"</c> — already in
    /// alphabetical order — so a canary that re-sorted the rules left this test green. A test
    /// whose fixture cannot distinguish the two orderings is not testing ordering.
    /// </remarks>
    [TestMethod]
    public void KeyOrder_IsPreservedExactly()
    {
        OpenCodePermissionModel model = Parse(
            """{ "bash": { "npm *": "deny", "*": "ask", "git *": "allow" } }""");

        string[] patterns = model.Tools.Single().Value.Rules.Select(r => r.Pattern).ToArray();

        CollectionAssert.AreEqual(
            new[] { "npm *", "*", "git *" },
            patterns,
            "Rule order is the semantics here — the LAST match wins. Re-ordering this map "
            + "rewrites the user's policy without changing a single value.");

        CollectionAssert.AreNotEqual(
            patterns.Order(StringComparer.Ordinal).ToArray(),
            patterns,
            "Precondition: this fixture must not already be in alphabetical order, or it "
            + "cannot detect a re-sort.");
    }

    /// <summary>
    /// The vendor's documented idiom: broad first, narrow last, last match wins. A
    /// first-match implementation returns <c>ask</c> for every one of these.
    /// </summary>
    [TestMethod]
    [DataRow("ls -la", "Ask")]
    [DataRow("git status", "Allow")]
    [DataRow("git push --force", "Deny")]
    public void LastMatchWins(string command, string expected)
    {
        OpenCodePermissionModel model = Parse(
            """{ "bash": { "*": "ask", "git *": "allow", "git push *": "deny" } }""");

        Assert.AreEqual(
            expected,
            model.Resolve("bash", command).Outcome.ToString(),
            $"'{command}' resolved wrongly. With broad-first/narrow-last ordering, a "
            + "first-match scan returns the broad rule for everything.");
    }

    /// <summary>The decision names the rule that produced it, for the dry-run tester.</summary>
    [TestMethod]
    public void TheDecisionNamesTheRuleThatDecided()
    {
        OpenCodePermissionModel model = Parse(
            """{ "bash": { "*": "ask", "git push *": "deny" } }""");

        OpenCodePermissionDecision decision = model.Resolve("bash", "git push --force");

        Assert.AreEqual("bash", decision.MatchedTool);
        Assert.AreEqual("git push *", decision.MatchedRule?.Pattern);
    }

    // ── S1's merge-inversion table, exactly ──────────────────────────────────

    /// <summary>
    /// The regression test the plan asks for, using its own measured table. A lower layer's
    /// <c>{"npm *": "deny"}</c> merged under a project's <c>{"*": "ask", "git *": "allow"}</c>
    /// puts the lower keys first, so the broad <c>"*": "ask"</c> lands after the narrow deny
    /// and wins. The user's deny is defeated by ordering alone, with no edit to either file.
    /// </summary>
    [TestMethod]
    public void MergeInversion_TheBroadRuleFromAHigherLayerDefeatsTheNarrowDeny()
    {
        OpenCodePermissionModel merged = Parse(
            """{ "bash": { "npm *": "deny", "*": "ask", "git *": "allow" } }""");

        Assert.AreEqual(
            PermissionOutcome.Ask,
            merged.Resolve("bash", "npm install").Outcome,
            "This is the hazard, not a bug in the model: after merging, npm install really "
            + "does resolve to ask. The model must report what OpenCode would actually do.");
    }

    /// <summary>
    /// …and the model has to be able to say so. The shadowed-rule report is what turns that
    /// silent inversion into something a user can see.
    /// </summary>
    [TestMethod]
    public void ShadowedRules_AreReported_SoTheInversionIsVisible()
    {
        OpenCodePermissionModel merged = Parse(
            """{ "bash": { "npm *": "deny", "*": "ask", "git *": "allow" } }""");

        IReadOnlyList<OpenCodeShadowedRule> shadowed = merged.FindShadowedRules();

        OpenCodeShadowedRule inverted = shadowed.Single(s => s.Rule.Pattern == "npm *");
        Assert.AreEqual("*", inverted.ShadowedBy.Pattern);
        Assert.AreEqual("bash", inverted.Tool);
        Assert.AreEqual(PermissionOutcome.Deny, inverted.Rule.Action,
            "The shadowed rule is a deny — which is exactly why this warning matters.");
    }

    /// <summary>A well-ordered config shadows nothing.</summary>
    [TestMethod]
    public void BroadFirstNarrowLast_ReportsNoShadowing()
    {
        OpenCodePermissionModel model = Parse(
            """{ "bash": { "*": "ask", "git *": "allow", "git push *": "deny" } }""");

        Assert.AreEqual(0, model.FindShadowedRules().Count,
            "Broad-first/narrow-last is the documented idiom and must not be flagged.");
    }
}
