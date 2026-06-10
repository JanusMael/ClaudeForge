using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Permissions;

/// <summary>
/// Structural decomposition of permission rules into tool / specifier / MCP
/// parts. Permissive by design — it accepts forms the strict editor gate
/// (<see cref="PermissionRule.TryParse"/>) rejects, because it must faithfully
/// decompose whatever is already in a user's settings.
/// </summary>
[TestClass]
public sealed class ParsedPermissionRuleTests
{
    [TestMethod]
    public void BareTool_MatchesAllUses()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Read");
        Assert.AreEqual("Read", p.ToolName);
        Assert.IsTrue(p.IsBareTool);
        Assert.IsNull(p.Specifier);
        Assert.IsTrue(p.MatchesAllUses);
        Assert.IsFalse(p.IsMcp);
    }

    [TestMethod]
    public void ToolWithSpecifier_SplitsToolAndContent()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(git push *)");
        Assert.AreEqual("Bash", p.ToolName);
        Assert.AreEqual("git push *", p.Specifier);
        Assert.IsFalse(p.IsBareTool);
        Assert.IsFalse(p.MatchesAllUses);
    }

    [TestMethod]
    public void StarSpecifier_MatchesAllUses()
    {
        // Bash(*) is equivalent to bare Bash per the spec — even though the
        // strict editor gate rejects it, evaluation must treat it as all-uses.
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(*)");
        Assert.AreEqual("Bash", p.ToolName);
        Assert.AreEqual("*", p.Specifier);
        Assert.IsTrue(p.MatchesAllUses);
    }

    [TestMethod]
    public void WebFetchDomain_KeepsDomainSpecifier()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("WebFetch(domain:example.com)");
        Assert.AreEqual("WebFetch", p.ToolName);
        Assert.AreEqual("domain:example.com", p.Specifier);
    }

    [TestMethod]
    public void Mcp_ServerOnly_MeansAllTools()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer");
        Assert.IsTrue(p.IsMcp);
        Assert.AreEqual("puppeteer", p.McpServer);
        Assert.IsNull(p.McpTool);
        Assert.IsTrue(p.McpAllTools);
        Assert.IsTrue(p.MatchesAllUses);
    }

    [TestMethod]
    public void Mcp_WildcardTool_MeansAllTools()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer__*");
        Assert.IsTrue(p.IsMcp);
        Assert.AreEqual("puppeteer", p.McpServer);
        Assert.IsNull(p.McpTool);
        Assert.IsTrue(p.McpAllTools);
    }

    [TestMethod]
    public void Mcp_SpecificTool_CapturesServerAndTool()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("mcp__puppeteer__navigate");
        Assert.IsTrue(p.IsMcp);
        Assert.AreEqual("puppeteer", p.McpServer);
        Assert.AreEqual("navigate", p.McpTool);
        Assert.IsFalse(p.McpAllTools);
        Assert.IsFalse(p.MatchesAllUses);
    }

    [TestMethod]
    public void Agent_CapturesName()
    {
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Agent(Explore)");
        Assert.AreEqual("Agent", p.ToolName);
        Assert.AreEqual("Explore", p.Specifier);
    }

    [TestMethod]
    public void HalfTyped_MissingCloseParen_StillDecomposes()
    {
        // Live-preview robustness: a rule the user is mid-typing should still
        // decompose so the gloss/preview can update.
        ParsedPermissionRule p = ParsedPermissionRule.Parse("Bash(git push");
        Assert.AreEqual("Bash", p.ToolName);
        Assert.AreEqual("git push", p.Specifier);
    }

    [TestMethod]
    public void EmptyOrWhitespace_FailsToParse()
    {
        Assert.IsFalse(ParsedPermissionRule.TryParse("", out _));
        Assert.IsFalse(ParsedPermissionRule.TryParse("   ", out _));
        Assert.IsFalse(ParsedPermissionRule.TryParse(null, out _));
        Assert.ThrowsException<ArgumentException>(() => ParsedPermissionRule.Parse("  "));
    }

    [TestMethod]
    public void PermissionRule_Decompose_DelegatesToParsed()
    {
        PermissionRule rule = PermissionRule.Parse("Bash(npm *)");
        ParsedPermissionRule p = rule.Decompose();
        Assert.AreEqual("Bash", p.ToolName);
        Assert.AreEqual("npm *", p.Specifier);
    }
}
