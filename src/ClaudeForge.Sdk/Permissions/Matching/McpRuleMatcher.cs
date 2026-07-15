namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches a candidate MCP tool call against an <c>mcp__</c> permission rule.
/// </summary>
/// <remarks>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"MCP":
/// <list type="bullet">
///   <item><c>mcp__server</c> matches any tool from <c>server</c></item>
///   <item><c>mcp__server__*</c> wildcard — also matches every tool from <c>server</c></item>
///   <item><c>mcp__server__tool</c> matches the one named tool</item>
/// </list>
/// Server and tool names are compared with the ordinal comparer (MCP identifiers
/// are case-sensitive).
/// </remarks>
public static class McpRuleMatcher
{
    /// <summary>
    /// Returns <see langword="true"/> when the MCP <paramref name="candidate"/>
    /// matches the MCP <paramref name="rule"/>.
    /// </summary>
    public static bool Match(ParsedPermissionRule rule, PermissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!rule.IsMcp || !candidate.IsMcp)
        {
            return false;
        }

        if (!string.Equals(rule.McpServer, candidate.McpServer, StringComparison.Ordinal))
        {
            return false;
        }

        // Whole-server rule (mcp__server or mcp__server__*) matches any tool.
        if (rule.McpAllTools)
        {
            return true;
        }

        // Specific-tool rule: the candidate must name the same tool.
        return candidate.McpTool is not null
               && string.Equals(rule.McpTool, candidate.McpTool, StringComparison.Ordinal);
    }
}
