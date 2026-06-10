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
}
