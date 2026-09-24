using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Add-time collision detection: cross-bucket conflicts and same-bucket
/// redundancy via exact match, bare-tool / whole-server coverage, and Bash
/// prefix subsumption — while staying silent on unrelated rules.
/// </summary>
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

    [Fact]
    public void ExactRuleInDifferentBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, deny: ["Bash(git push:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Deny, c.ExistingBucket);
    }

    [Fact]
    public void SpaceFormVsColonForm_NormalizeAndConflict()
    {
        // Candidate space form normalizes to colon form and collides with the
        // colon-form rule already in another bucket.
        PermissionCollision? c = Detect(
            "Bash(git push *)", PermissionBucket.Allow, deny: ["Bash(git push:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
    }

    [Fact]
    public void GitStatus_UnderGitStatusStar_SameBucket_IsRedundant()
    {
        // The user's example: adding Bash(git status) when Bash(git status *) exists.
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git status *)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void ShellPrefixSubsumption_SameBucket_IsRedundant()
    {
        // Bash(git:*) covers Bash(git status).
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void ShellPrefixSubsumption_CrossBucket_IsConflict()
    {
        // Deny Bash(git:*) covers a candidate Allow Bash(git push).
        PermissionCollision? c = Detect(
            "Bash(git push)", PermissionBucket.Allow, deny: ["Bash(git:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Deny, c.ExistingBucket);
    }

    [Fact]
    public void BareTool_CoversSpecific_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git status)", PermissionBucket.Allow, allow: ["Bash"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void McpWholeServer_CoversSpecificTool_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "mcp__github__create_issue", PermissionBucket.Allow, allow: ["mcp__github"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void UnrelatedShellCommands_NoCollision()
    {
        Assert.Null(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Bash(npm test)"]));
    }

    [Fact]
    public void DifferentTool_NoCollision()
    {
        Assert.Null(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Read(src/**)"]));
        Assert.Null(Detect("mcp__github", PermissionBucket.Allow, allow: ["mcp__slack"]));
    }

    [Fact]
    public void ExactSameBucket_NoFinding_DedupeIsCallersJob()
    {
        Assert.Null(Detect("Bash(git status)", PermissionBucket.Allow, allow: ["Bash(git status)"]));
    }

    // ── A5: cross-bucket precedence (Deny > Ask > Allow) ─────────────────────

    [Fact]
    public void CrossBucket_DenyAndAskBothOverlap_PrefersDeny()
    {
        // A5 regression: a candidate added to Allow that overlaps BOTH an Ask and a
        // Deny rule must surface the DENY (it hard-blocks), not the milder Ask. Pre-
        // fix the fixed [Allow, Ask, Deny] scan returned the first hit (Ask).
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow,
            deny: ["Bash(git push:*)"], ask: ["Bash(git push:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        MessageAssert.Equal(PermissionBucket.Deny, c.ExistingBucket,
            "Deny outranks Ask — the higher-impact conflict must be reported.");
    }

    [Fact]
    public void Conflict_PreferredOverRedundant()
    {
        // A same-bucket redundant sibling AND a cross-bucket conflict both exist;
        // the conflict (the stronger signal) must win.
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow,
            allow: ["Bash(git:*)"],   // covers the candidate → same-bucket redundant
            deny: ["Bash(git push:*)"]); // exact in another bucket → conflict
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Deny, c.ExistingBucket);
    }

    [Fact]
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
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Ask, c.ExistingBucket);
    }

    // ── B5: MCP cross-bucket conflict + server-name mismatch ─────────────────

    [Fact]
    public void Mcp_WholeServerDeny_vs_SpecificAllow_IsConflict()
    {
        // Deny mcp__github (whole server) covers a candidate Allow of one tool.
        PermissionCollision? c = Detect(
            "mcp__github__create_issue", PermissionBucket.Allow, deny: ["mcp__github"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Deny, c.ExistingBucket);
    }

    [Fact]
    public void Mcp_WholeServerCandidate_vs_SpecificExisting_SameBucket_IsRedundant()
    {
        // Candidate is the whole server; an existing specific-tool rule is subsumed.
        PermissionCollision? c = Detect(
            "mcp__github", PermissionBucket.Allow, allow: ["mcp__github__create_issue"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void Mcp_ServerNameCaseDiffers_NoCollision()
    {
        // MCP server names are compared Ordinal (case-sensitive) and are not
        // lowercased by the normalizer — different case = different server.
        Assert.Null(Detect(
            "mcp__GitHub__create_issue", PermissionBucket.Allow, deny: ["mcp__github"]));
    }

    // ── B9: PowerShell case-insensitive subsumption ──────────────────────────

    [Fact]
    public void PowerShellPrefixSubsumption_CaseInsensitive_SameBucket_IsRedundant()
    {
        // PowerShell matching is case-insensitive, so PowerShell(Get-ChildItem:*)
        // covers PowerShell(get-childitem) despite the case difference.
        PermissionCollision? c = Detect(
            "PowerShell(get-childitem)", PermissionBucket.Allow, allow: ["PowerShell(Get-ChildItem:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void PowerShellPrefixSubsumption_CaseInsensitive_CrossBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "PowerShell(get-childitem)", PermissionBucket.Allow, deny: ["PowerShell(Get-ChildItem:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
        Assert.Equal(PermissionBucket.Deny, c.ExistingBucket);
    }

    // ── B13: space-star ↔ colon-star representative collapse (both directions) ─

    [Fact]
    public void SpaceStarCandidate_ColonStarExisting_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git push *)", PermissionBucket.Allow, allow: ["Bash(git push:*)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void ColonStarCandidate_SpaceStarExisting_SameBucket_IsRedundant()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, allow: ["Bash(git push *)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Redundant, c.Kind);
    }

    [Fact]
    public void ColonStarCandidate_SpaceStarExisting_CrossBucket_IsConflict()
    {
        PermissionCollision? c = Detect(
            "Bash(git push:*)", PermissionBucket.Allow, deny: ["Bash(git push *)"]);
        Assert.NotNull(c);
        Assert.Equal(PermissionCollisionKind.Conflict, c.Kind);
    }
}
