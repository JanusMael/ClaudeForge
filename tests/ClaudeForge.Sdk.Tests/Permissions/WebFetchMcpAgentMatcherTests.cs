using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>WebFetch domain, MCP server/tool, and Agent/bare-tool matching.</summary>
[TestClass]
public sealed class WebFetchMcpAgentMatcherTests
{
    // ── WebFetch ─────────────────────────────────────────────────────────────

    private static bool Web(string rule, string url) =>
        WebFetchRuleMatcher.Match(ParsedPermissionRule.Parse(rule), url);

    [TestMethod]
    public void WebFetch_MatchesExactDomain()
    {
        Assert.IsTrue(Web("WebFetch(domain:example.com)", "https://example.com/page"));
    }

    [TestMethod]
    public void WebFetch_MatchesSubdomain()
    {
        Assert.IsTrue(Web("WebFetch(domain:example.com)", "https://docs.example.com/x"));
    }

    [TestMethod]
    public void WebFetch_RejectsDifferentDomain()
    {
        Assert.IsFalse(Web("WebFetch(domain:example.com)", "https://example.org/x"));
        Assert.IsFalse(Web("WebFetch(domain:example.com)", "https://notexample.com/x"));
    }

    [TestMethod]
    public void WebFetch_BareTool_MatchesAnyUrl()
    {
        Assert.IsTrue(Web("WebFetch", "https://anything.test/x"));
    }

    [TestMethod]
    public void WebFetch_BareHostCandidate_NoScheme()
    {
        Assert.IsTrue(Web("WebFetch(domain:example.com)", "example.com/path"));
    }

    // ── MCP ──────────────────────────────────────────────────────────────────

    private static bool Mcp(string rule, string server, string? tool) =>
        McpRuleMatcher.Match(
            ParsedPermissionRule.Parse(rule), PermissionCandidate.Mcp(server, tool));

    [TestMethod]
    public void Mcp_ServerOnly_MatchesAnyTool()
    {
        Assert.IsTrue(Mcp("mcp__github", "github", "create_issue"));
        Assert.IsTrue(Mcp("mcp__github", "github", null));
    }

    [TestMethod]
    public void Mcp_Wildcard_MatchesAnyTool()
    {
        Assert.IsTrue(Mcp("mcp__github__*", "github", "create_issue"));
    }

    [TestMethod]
    public void Mcp_SpecificTool_MatchesOnlyThatTool()
    {
        Assert.IsTrue(Mcp("mcp__github__create_issue", "github", "create_issue"));
        Assert.IsFalse(Mcp("mcp__github__create_issue", "github", "delete_repo"));
        Assert.IsFalse(Mcp("mcp__github__create_issue", "github", null));
    }

    [TestMethod]
    public void Mcp_DifferentServer_NoMatch()
    {
        Assert.IsFalse(Mcp("mcp__github", "gitlab", "create_issue"));
    }

    // ── Agent ────────────────────────────────────────────────────────────────

    private static bool Agent(string rule, string name) =>
        AgentRuleMatcher.Match(ParsedPermissionRule.Parse(rule), PermissionCandidate.Agent(name));

    [TestMethod]
    public void Agent_MatchesNamedSubagent()
    {
        Assert.IsTrue(Agent("Agent(Explore)", "Explore"));
        Assert.IsFalse(Agent("Agent(Explore)", "Plan"));
    }

    [TestMethod]
    public void Agent_BareTool_MatchesAny()
    {
        Assert.IsTrue(Agent("Agent", "AnyAgent"));
    }

    // ── Bare tools ───────────────────────────────────────────────────────────

    private static bool Bare(string rule, string tool) =>
        BareToolMatcher.Match(ParsedPermissionRule.Parse(rule), PermissionCandidate.Tool(tool));

    [TestMethod]
    public void BareTool_MatchesSameToolName()
    {
        Assert.IsTrue(Bare("Grep", "Grep"));
        Assert.IsTrue(Bare("WebSearch", "WebSearch"));
        Assert.IsFalse(Bare("Grep", "Glob"));
    }
}
