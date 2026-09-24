using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Add-time canonicalization: Bash/PowerShell trailing-wildcard specifiers
/// (<c> *</c> and <c>:*</c>) are preserved VERBATIM because they are distinct
/// match semantics (<c>:*</c> is a strict superset of <c> *</c>); backslash →
/// forward-slash for Read/Edit/Write path specifiers (anchors preserved);
/// everything else untouched; never throws.
/// </summary>
public sealed class PermissionRuleNormalizerTests
{
    [Fact]
    public void Bash_TrailingSpaceStar_IsPreservedVerbatim()
    {
        // " *" (optional args) and ":*" (literal colon + remainder) are DISTINCT
        // match semantics, so the normalizer must NOT rewrite one into the other.
        Assert.Equal("Bash(git push *)", PermissionRuleNormalizer.Normalize("Bash(git push *)"));
    }

    [Fact]
    public void PowerShell_TrailingSpaceStar_IsPreservedVerbatim()
    {
        Assert.Equal(
            "PowerShell(Get-ChildItem *)",
            PermissionRuleNormalizer.Normalize("PowerShell(Get-ChildItem *)"));
    }

    [Fact]
    public void ColonStar_IsPreservedVerbatim()
    {
        Assert.Equal("Bash(git push:*)", PermissionRuleNormalizer.Normalize("Bash(git push:*)"));
        Assert.Equal(
            "PowerShell(Get-ChildItem:*)",
            PermissionRuleNormalizer.Normalize("PowerShell(Get-ChildItem:*)"));
    }

    [Fact]
    public void Shell_NonTrailingOrNoStar_Untouched()
    {
        Assert.Equal("Bash(npm run build)", PermissionRuleNormalizer.Normalize("Bash(npm run build)"));
        Assert.Equal("Bash(ls*)", PermissionRuleNormalizer.Normalize("Bash(ls*)"));
        Assert.Equal("Bash(* install)", PermissionRuleNormalizer.Normalize("Bash(* install)"));
    }

    [Fact]
    public void Shell_CommandBackslashes_NotPathNormalized()
    {
        // Backslashes in a shell command are literal text matched against the real
        // command line — must NOT be rewritten to forward slashes.
        Assert.Equal(
            @"PowerShell(Get-Content .\src\a.txt)",
            PermissionRuleNormalizer.Normalize(@"PowerShell(Get-Content .\src\a.txt)"));
    }

    [Fact]
    public void Path_Backslashes_BecomeForwardSlashes()
    {
        Assert.Equal("Read(src/app/**)", PermissionRuleNormalizer.Normalize(@"Read(src\app\**)"));
        Assert.Equal("Edit(src/main.ts)", PermissionRuleNormalizer.Normalize(@"Edit(src\main.ts)"));
        Assert.Equal("Write(out/gen/**)", PermissionRuleNormalizer.Normalize(@"Write(out\gen\**)"));
    }

    [Fact]
    public void Path_ForwardSlashAnchors_Preserved()
    {
        // The four anchors use forward slashes already; only backslashes change,
        // so these pass through verbatim.
        Assert.Equal("Read(//etc/hosts)", PermissionRuleNormalizer.Normalize("Read(//etc/hosts)"));
        Assert.Equal("Read(~/.ssh/**)", PermissionRuleNormalizer.Normalize("Read(~/.ssh/**)"));
        Assert.Equal("Read(/src/**)", PermissionRuleNormalizer.Normalize("Read(/src/**)"));
        Assert.Equal("Read(./local/**)", PermissionRuleNormalizer.Normalize("Read(./local/**)"));
    }

    [Fact]
    public void Path_WindowsAbsolute_SeparatorsNormalized()
    {
        Assert.Equal("Read(C:/Users/me/**)", PermissionRuleNormalizer.Normalize(@"Read(C:\Users\me\**)"));
    }

    [Fact]
    public void WebMcpAgentBare_Untouched()
    {
        Assert.Equal("WebFetch(domain:example.com)", PermissionRuleNormalizer.Normalize("WebFetch(domain:example.com)"));
        Assert.Equal("mcp__github", PermissionRuleNormalizer.Normalize("mcp__github"));
        Assert.Equal("mcp__github__create_issue", PermissionRuleNormalizer.Normalize("mcp__github__create_issue"));
        Assert.Equal("Agent(Explore)", PermissionRuleNormalizer.Normalize("Agent(Explore)"));
        Assert.Equal("Bash", PermissionRuleNormalizer.Normalize("Bash"));
        Assert.Equal("Read", PermissionRuleNormalizer.Normalize("Read"));
    }

    [Fact]
    public void NullOrEmpty_ReturnedAsIs()
    {
        Assert.Equal("", PermissionRuleNormalizer.Normalize(""));
        Assert.Equal("   ", PermissionRuleNormalizer.Normalize("   "));
    }
}
