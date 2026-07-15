namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Single dispatch point that routes a (rule, candidate) pair to the correct
/// per-tool matcher. Used by <see cref="PermissionResolver"/>; also handy
/// directly when you only need a yes/no for one rule.
/// </summary>
/// <remarks>
/// <para>
/// Routing follows the tool families in
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>.
/// Note two documented cross-tool behaviors:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>Edit</c> rules apply to all file-editing tools, so an <c>Edit(...)</c>
///     rule also matches a <c>Write</c> candidate (§"Read and Edit": "<c>Edit</c>
///     rules apply to all built-in tools that edit files").
///   </item>
///   <item>
///     A bare tool name or <c>Tool(*)</c> matches every use of that tool,
///     handled uniformly via <see cref="ParsedPermissionRule.MatchesAllUses"/>.
///   </item>
/// </list>
/// <para>
/// This facade matches a single (simple) command. Compound Bash commands are
/// split and wrapper-stripped by <see cref="PermissionResolver"/> before they
/// reach here.
/// </para>
/// </remarks>
public static class PermissionRuleMatcher
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="rule"/> matches
    /// <paramref name="candidate"/> in <paramref name="context"/>.
    /// </summary>
    public static bool Matches(
        ParsedPermissionRule rule,
        PermissionCandidate candidate,
        PermissionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        // MCP is matched structurally on both sides.
        if (candidate.IsMcp || rule.IsMcp)
        {
            return McpRuleMatcher.Match(rule, candidate);
        }

        // A candidate whose specifier field is null is a "bare-tool probe" — the
        // caller is asking what happens when the tool is used with no specific
        // argument (e.g. the dry-run tester's "Test this rule" on a bare Read).
        // Only a bare-tool / Tool(*) rule (MatchesAllUses) can answer that;
        // argument-specific rules can't match an unknown argument.
        switch (candidate.ToolName)
        {
            case "Bash":
            case "PowerShell":
                if (!string.Equals(rule.ToolName, candidate.ToolName, StringComparison.Ordinal))
                {
                    return false;
                }

                return candidate.CommandText is null
                    ? rule.MatchesAllUses
                    : BashRuleMatcher.Match(
                        rule, candidate.CommandText, caseInsensitive: candidate.ToolName == "PowerShell");

            case "Read":
            case "Edit":
            case "Write":
                if (!PathRuleApplies(rule.ToolName, candidate.ToolName))
                {
                    return false;
                }

                return candidate.Path is null
                    ? rule.MatchesAllUses
                    : PathRuleMatcher.Match(rule, candidate.Path, context);

            case "WebFetch":
                if (rule.ToolName != "WebFetch")
                {
                    return false;
                }

                return candidate.Url is null
                    ? rule.MatchesAllUses
                    : WebFetchRuleMatcher.Match(rule, candidate.Url);

            case "Agent":
                if (rule.ToolName != "Agent")
                {
                    return false;
                }

                return candidate.AgentName is null
                    ? rule.MatchesAllUses
                    : AgentRuleMatcher.Match(rule, candidate);

            default:
                return BareToolMatcher.Match(rule, candidate);
        }
    }

    /// <summary>
    /// Whether a path rule for <paramref name="ruleTool"/> governs a candidate
    /// using <paramref name="candidateTool"/>. <c>Edit</c> rules cover both
    /// <c>Edit</c> and <c>Write</c> candidates (per spec); <c>Read</c> and
    /// <c>Write</c> rules cover only their own tool.
    /// </summary>
    internal static bool PathRuleApplies(string ruleTool, string candidateTool)
    {
        if (ruleTool == candidateTool)
        {
            return true;
        }

        // Edit rules apply to all file-editing tools, including Write.
        return ruleTool == "Edit" && candidateTool == "Write";
    }
}
