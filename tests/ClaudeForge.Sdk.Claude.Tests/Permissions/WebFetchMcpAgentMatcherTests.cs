using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Permissions;

/// <summary>WebFetch domain, MCP server/tool, and Agent/bare-tool matching.</summary>
public sealed class WebFetchMcpAgentMatcherTests
{
    // ── WebFetch ─────────────────────────────────────────────────────────────

    private static bool Web(string rule, string url) =>
        WebFetchRuleMatcher.Match(ParsedPermissionRule.Parse(rule), url);

    [Fact]
    public void WebFetch_MatchesExactDomain()
    {
        Assert.True(Web("WebFetch(domain:example.com)", "https://example.com/page"));
    }

    [Fact]
    public void WebFetch_MatchesSubdomain()
    {
        Assert.True(Web("WebFetch(domain:example.com)", "https://docs.example.com/x"));
    }

    [Fact]
    public void WebFetch_RejectsDifferentDomain()
    {
        Assert.False(Web("WebFetch(domain:example.com)", "https://example.org/x"));
        Assert.False(Web("WebFetch(domain:example.com)", "https://notexample.com/x"));
    }

    [Fact]
    public void WebFetch_BareTool_MatchesAnyUrl()
    {
        Assert.True(Web("WebFetch", "https://anything.test/x"));
    }

    [Fact]
    public void WebFetch_BareHostCandidate_NoScheme()
    {
        Assert.True(Web("WebFetch(domain:example.com)", "example.com/path"));
    }

    // ── MCP ──────────────────────────────────────────────────────────────────

    private static bool Mcp(string rule, string server, string? tool) =>
        McpRuleMatcher.Match(
            ParsedPermissionRule.Parse(rule), PermissionCandidate.Mcp(server, tool));

    [Fact]
    public void Mcp_ServerOnly_MatchesAnyTool()
    {
        Assert.True(Mcp("mcp__github", "github", "create_issue"));
        Assert.True(Mcp("mcp__github", "github", null));
    }

    [Fact]
    public void Mcp_Wildcard_MatchesAnyTool()
    {
        Assert.True(Mcp("mcp__github__*", "github", "create_issue"));
    }

    [Fact]
    public void Mcp_SpecificTool_MatchesOnlyThatTool()
    {
        Assert.True(Mcp("mcp__github__create_issue", "github", "create_issue"));
        Assert.False(Mcp("mcp__github__create_issue", "github", "delete_repo"));
        Assert.False(Mcp("mcp__github__create_issue", "github", null));
    }

    [Fact]
    public void Mcp_DifferentServer_NoMatch()
    {
        Assert.False(Mcp("mcp__github", "gitlab", "create_issue"));
    }

    // ── Agent ────────────────────────────────────────────────────────────────

    private static bool Agent(string rule, string name) =>
        AgentRuleMatcher.Match(ParsedPermissionRule.Parse(rule), PermissionCandidate.Agent(name));

    [Fact]
    public void Agent_MatchesNamedSubagent()
    {
        Assert.True(Agent("Agent(Explore)", "Explore"));
        Assert.False(Agent("Agent(Explore)", "Plan"));
    }

    [Fact]
    public void Agent_BareTool_MatchesAny()
    {
        Assert.True(Agent("Agent", "AnyAgent"));
    }

    // ── Bare tools ───────────────────────────────────────────────────────────

    private static bool Bare(string rule, string tool) =>
        BareToolMatcher.Match(ParsedPermissionRule.Parse(rule), PermissionCandidate.Tool(tool));

    [Fact]
    public void BareTool_MatchesSameToolName()
    {
        Assert.True(Bare("Grep", "Grep"));
        Assert.True(Bare("WebSearch", "WebSearch"));
        Assert.False(Bare("Grep", "Glob"));
    }
}
