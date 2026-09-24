using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>
/// Structural decomposition of permission rules into tool / specifier / MCP
/// parts. Permissive by design — it accepts forms the strict editor gate
/// (<see cref="PermissionRule.TryParse"/>) rejects, because it must faithfully
/// decompose whatever is already in a user's settings.
/// </summary>
public sealed class ParsedPermissionRuleTests
{
    [Fact]
    public void BareTool_MatchesAllUses()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Read");
        Assert.Equal("Read", p.ToolName);
        Assert.True(p.IsBareTool);
        Assert.Null(p.Specifier);
        Assert.True(p.MatchesAllUses);
        Assert.False(p.IsMcp);
    }

    [Fact]
    public void ToolWithSpecifier_SplitsToolAndContent()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(git push *)");
        Assert.Equal("Bash", p.ToolName);
        Assert.Equal("git push *", p.Specifier);
        Assert.False(p.IsBareTool);
        Assert.False(p.MatchesAllUses);
    }

    [Fact]
    public void StarSpecifier_MatchesAllUses()
    {
        // Bash(*) is equivalent to bare Bash per the spec — even though the
        // strict editor gate rejects it, evaluation must treat it as all-uses.
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(*)");
        Assert.Equal("Bash", p.ToolName);
        Assert.Equal("*", p.Specifier);
        Assert.True(p.MatchesAllUses);
    }

    [Fact]
    public void WebFetchDomain_KeepsDomainSpecifier()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("WebFetch(domain:example.com)");
        Assert.Equal("WebFetch", p.ToolName);
        Assert.Equal("domain:example.com", p.Specifier);
    }

    [Fact]
    public void Mcp_ServerOnly_MeansAllTools()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer");
        Assert.True(p.IsMcp);
        Assert.Equal("puppeteer", p.McpServer);
        Assert.Null(p.McpTool);
        Assert.True(p.McpAllTools);
        Assert.True(p.MatchesAllUses);
    }

    [Fact]
    public void Mcp_WildcardTool_MeansAllTools()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer__*");
        Assert.True(p.IsMcp);
        Assert.Equal("puppeteer", p.McpServer);
        Assert.Null(p.McpTool);
        Assert.True(p.McpAllTools);
    }

    [Fact]
    public void Mcp_SpecificTool_CapturesServerAndTool()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer__navigate");
        Assert.True(p.IsMcp);
        Assert.Equal("puppeteer", p.McpServer);
        Assert.Equal("navigate", p.McpTool);
        Assert.False(p.McpAllTools);
        Assert.False(p.MatchesAllUses);
    }

    [Fact]
    public void Agent_CapturesName()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Agent(Explore)");
        Assert.Equal("Agent", p.ToolName);
        Assert.Equal("Explore", p.Specifier);
    }

    [Fact]
    public void HalfTyped_MissingCloseParen_StillDecomposes()
    {
        // Live-preview robustness: a rule the user is mid-typing should still
        // decompose so the gloss/preview can update.
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(git push");
        Assert.Equal("Bash", p.ToolName);
        Assert.Equal("git push", p.Specifier);
    }

    [Fact]
    public void EmptyOrWhitespace_FailsToParse()
    {
        Assert.False(ParsedPermissionRule.TryParse("", out _));
        Assert.False(ParsedPermissionRule.TryParse("   ", out _));
        Assert.False(ParsedPermissionRule.TryParse(null, out _));
        Assert.Throws<ArgumentException>(() => ParsedPermissionRule.Parse("  "));
    }

    [Fact]
    public void PermissionRule_Decompose_DelegatesToParsed()
    {
        PermissionRule rule = PermissionRule.Parse("Bash(npm *)");
        ParsedPermissionRule p = rule.Decompose();
        Assert.Equal("Bash", p.ToolName);
        Assert.Equal("npm *", p.Specifier);
    }
}
