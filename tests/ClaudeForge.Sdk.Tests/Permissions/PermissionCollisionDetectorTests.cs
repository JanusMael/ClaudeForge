using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Add-time collision detection: cross-bucket conflicts and same-bucket
/// redundancy via exact match, bare-tool / whole-server coverage, and Bash
/// prefix subsumption — while staying silent on unrelated rules.
/// </summary>
[TestClass]
public sealed class PermissionCollisionDetectorTests
{
    private static List<PermissionRule> Rules(params string[] rules) =>
        rules.Select(PermissionRule.Parse).ToList();

    private static PermissionCollision? Detect(
        string candidate,
        PermissionBucket bucket,
        string[]? allow = null,
        string[]? deny = null,
        string[]? ask = null) =>
        PermissionCollisionDetector.Detect(
            PermissionRule.Parse(candidate),
            bucket,
            Rules(allow ?? []),
            Rules(deny ?? []),
            Rules(ask ?? []));

    [TestMethod]
    public void ExactRuleInDifferentBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, deny: ["Bash(git push:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket);
    }

    [TestMethod]
    public void SpaceFormVsColonForm_NormalizeAndConflict()
    {
        // Candidate space form normalizes to colon form and collides with the
        // colon-form rule already in another bucket.
        PermissionCollision? c = Detect(
            "Bash(git push *)", PermissionBucket.Allow, deny: ["Bash(git push:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
    }

    [TestMethod]
    public void GitStatus_UnderGitStatusStar_SameBucket_IsRedundant()
    {
        // The user's example: adding Bash(git status) when Bash(git status *) exists.
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git status *)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void ShellPrefixSubsumption_SameBucket_IsRedundant()
    {
        // Bash(git:*) covers Bash(git status).
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void ShellPrefixSubsumption_CrossBucket_IsConflict()
    {
        // Deny Bash(git:*) covers a candidate Allow Bash(git push).
        PermissionCollision? c = Detect(
            "Bash(git push)", PermissionBucket.Allow, deny: ["Bash(git:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket);
    }

    [TestMethod]
    public void BareTool_CoversSpecific_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void McpWholeServer_CoversSpecificTool_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "mcp__github__create_issue", PermissionBucket.Allow, allow: ["mcp__github"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void UnrelatedShellCommands_NoCollision()
    {
        Assert.IsNull(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Bash(npm test)"]));
    }

    [TestMethod]
    public void DifferentTool_NoCollision()
    {
        Assert.IsNull(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Read(src/**)"]));
        Assert.IsNull(Detect("mcp__github", PermissionBucket.Allow, allow: ["mcp__slack"]));
    }

    [TestMethod]
    public void ExactSameBucket_NoFinding_DedupeIsCallersJob()
    {
        Assert.IsNull(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git status)"]));
    }

    // ── A5: cross-bucket precedence (Deny > Ask > Allow) ─────────────────────

    [TestMethod]
    public void CrossBucket_DenyAndAskBothOverlap_PrefersDeny()
    {
        // A5 regression: a candidate added to Allow that overlaps BOTH an Ask and a
        // Deny rule must surface the DENY (it hard-blocks), not the milder Ask. Pre-
        // fix the fixed [Allow, Ask, Deny] scan returned the first hit (Ask).
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow,
            deny: ["Bash(git push:*)"], ask: ["Bash(git push:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket,
            "Deny outranks Ask — the higher-impact conflict must be reported.");
    }

    [TestMethod]
    public void Conflict_PreferredOverRedundant()
    {
        // A same-bucket redundant sibling AND a cross-bucket conflict both exist;
        // the conflict (the stronger signal) must win.
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow,
            allow: ["Bash(git:*)"],   // covers the candidate → same-bucket redundant
            deny: ["Bash(git push:*)"]); // exact in another bucket → conflict
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket);
    }

    [TestMethod]
    public void CrossBucket_AskConflict_PreferredOverRedundantAllow()
    {
        // Completes the precedence matrix: a candidate added to Allow overlaps an Ask
        // rule (cross-bucket) AND a redundant Allow sibling, with NO Deny present. The
        // Ask conflict must win — Ask outranks Allow, and a conflict outranks a
        // same-bucket redundant.
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow,
            allow: ["Bash(git:*)"],     // covers the candidate → same-bucket redundant
            ask: ["Bash(git push:*)"]); // exact in another bucket → conflict
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Ask, c.ExistingBucket);
    }

    // ── B5: MCP cross-bucket conflict + server-name mismatch ─────────────────

    [TestMethod]
    public void Mcp_WholeServerDeny_vs_SpecificAllow_IsConflict()
    {
        // Deny mcp__github (whole server) covers a candidate Allow of one tool.
        PermissionCollision? c = Detect(
            "mcp__github__create_issue", PermissionBucket.Allow, deny: ["mcp__github"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket);
    }

    [TestMethod]
    public void Mcp_WholeServerCandidate_vs_SpecificExisting_SameBucket_IsRedundant()
    {
        // Candidate is the whole server; an existing specific-tool rule is subsumed.
        PermissionCollision? c = Detect(
            "mcp__github", PermissionBucket.Allow, allow: ["mcp__github__create_issue"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void Mcp_ServerNameCaseDiffers_NoCollision()
    {
        // MCP server names are compared Ordinal (case-sensitive) and are not
        // lowercased by the normalizer — different case = different server.
        Assert.IsNull(Detect(
            "mcp__GitHub__create_issue", PermissionBucket.Allow, deny: ["mcp__github"]));
    }

    // ── B9: PowerShell case-insensitive subsumption ──────────────────────────

    [TestMethod]
    public void PowerShellPrefixSubsumption_CaseInsensitive_SameBucket_IsRedundant()
    {
        // PowerShell matching is case-insensitive, so PowerShell(Get-ChildItem:*)
        // covers PowerShell(get-childitem) despite the case difference.
        PermissionCollision? c = Detect(
            "PowerShell(get-childitem)", PermissionBucket.Allow, allow: ["PowerShell(Get-ChildItem:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void PowerShellPrefixSubsumption_CaseInsensitive_CrossBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "PowerShell(get-childitem)", PermissionBucket.Allow, deny: ["PowerShell(Get-ChildItem:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
        Assert.AreEqual(PermissionBucket.Deny, c.ExistingBucket);
    }

    // ── B13: space-star ↔ colon-star representative collapse (both directions) ─

    [TestMethod]
    public void SpaceStarCandidate_ColonStarExisting_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git push *)", PermissionBucket.Allow, allow: ["Bash(git push:*)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void ColonStarCandidate_SpaceStarExisting_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, allow: ["Bash(git push *)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Redundant, c.Kind);
    }

    [TestMethod]
    public void ColonStarCandidate_SpaceStarExisting_CrossBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, deny: ["Bash(git push *)"]);
        Assert.IsNotNull(c);
        Assert.AreEqual(PermissionCollisionKind.Conflict, c.Kind);
    }
}
