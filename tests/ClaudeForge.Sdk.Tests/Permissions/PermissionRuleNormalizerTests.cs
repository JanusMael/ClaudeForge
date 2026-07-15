using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Add-time canonicalization: Bash/PowerShell trailing-wildcard specifiers
/// (<c> *</c> and <c>:*</c>) are preserved VERBATIM because they are distinct
/// match semantics (<c>:*</c> is a strict superset of <c> *</c>); backslash →
/// forward-slash for Read/Edit/Write path specifiers (anchors preserved);
/// everything else untouched; never throws.
/// </summary>
[TestClass]
public sealed class PermissionRuleNormalizerTests
{
    [TestMethod]
    public void Bash_TrailingSpaceStar_IsPreservedVerbatim()
    {
        // " *" (optional args) and ":*" (literal colon + remainder) are DISTINCT
        // match semantics, so the normalizer must NOT rewrite one into the other.
        Assert.AreEqual("Bash(git push *)", PermissionRuleNormalizer.Normalize("Bash(git push *)"));
    }

    [TestMethod]
    public void PowerShell_TrailingSpaceStar_IsPreservedVerbatim()
    {
        Assert.AreEqual(
            "PowerShell(Get-ChildItem *)",
            PermissionRuleNormalizer.Normalize("PowerShell(Get-ChildItem *)"));
    }

    [TestMethod]
    public void ColonStar_IsPreservedVerbatim()
    {
        Assert.AreEqual("Bash(git push:*)", PermissionRuleNormalizer.Normalize("Bash(git push:*)"));
        Assert.AreEqual(
            "PowerShell(Get-ChildItem:*)",
            PermissionRuleNormalizer.Normalize("PowerShell(Get-ChildItem:*)"));
    }

    [TestMethod]
    public void Shell_NonTrailingOrNoStar_Untouched()
    {
        Assert.AreEqual("Bash(npm run build)", PermissionRuleNormalizer.Normalize("Bash(npm run build)"));
        Assert.AreEqual("Bash(ls*)", PermissionRuleNormalizer.Normalize("Bash(ls*)"));
        Assert.AreEqual("Bash(* install)", PermissionRuleNormalizer.Normalize("Bash(* install)"));
    }

    [TestMethod]
    public void Shell_CommandBackslashes_NotPathNormalized()
    {
        // Backslashes in a shell command are literal text matched against the real
        // command line — must NOT be rewritten to forward slashes.
        Assert.AreEqual(
            @"PowerShell(Get-Content .\src\a.txt)",
            PermissionRuleNormalizer.Normalize(@"PowerShell(Get-Content .\src\a.txt)"));
    }

    [TestMethod]
    public void Path_Backslashes_BecomeForwardSlashes()
    {
        Assert.AreEqual("Read(src/app/**)", PermissionRuleNormalizer.Normalize(@"Read(src\app\**)"));
        Assert.AreEqual("Edit(src/main.ts)", PermissionRuleNormalizer.Normalize(@"Edit(src\main.ts)"));
        Assert.AreEqual("Write(out/gen/**)", PermissionRuleNormalizer.Normalize(@"Write(out\gen\**)"));
    }

    [TestMethod]
    public void Path_ForwardSlashAnchors_Preserved()
    {
        // The four anchors use forward slashes already; only backslashes change,
        // so these pass through verbatim.
        Assert.AreEqual("Read(//etc/hosts)", PermissionRuleNormalizer.Normalize("Read(//etc/hosts)"));
        Assert.AreEqual("Read(~/.ssh/**)", PermissionRuleNormalizer.Normalize("Read(~/.ssh/**)"));
        Assert.AreEqual("Read(/src/**)", PermissionRuleNormalizer.Normalize("Read(/src/**)"));
        Assert.AreEqual("Read(./local/**)", PermissionRuleNormalizer.Normalize("Read(./local/**)"));
    }

    [TestMethod]
    public void Path_WindowsAbsolute_SeparatorsNormalized()
    {
        Assert.AreEqual("Read(C:/Users/me/**)", PermissionRuleNormalizer.Normalize(@"Read(C:\Users\me\**)"));
    }

    [TestMethod]
    public void WebMcpAgentBare_Untouched()
    {
        Assert.AreEqual("WebFetch(domain:example.com)", PermissionRuleNormalizer.Normalize("WebFetch(domain:example.com)"));
        Assert.AreEqual("mcp__github", PermissionRuleNormalizer.Normalize("mcp__github"));
        Assert.AreEqual("mcp__github__create_issue", PermissionRuleNormalizer.Normalize("mcp__github__create_issue"));
        Assert.AreEqual("Agent(Explore)", PermissionRuleNormalizer.Normalize("Agent(Explore)"));
        Assert.AreEqual("Bash", PermissionRuleNormalizer.Normalize("Bash"));
        Assert.AreEqual("Read", PermissionRuleNormalizer.Normalize("Read"));
    }

    [TestMethod]
    public void NullOrEmpty_ReturnedAsIs()
    {
        Assert.AreEqual("", PermissionRuleNormalizer.Normalize(""));
        Assert.AreEqual("   ", PermissionRuleNormalizer.Normalize("   "));
    }
}
