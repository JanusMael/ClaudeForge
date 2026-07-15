namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches tools whose permission decision turns only on the tool name —
/// specifier-less tools such as <c>Grep</c>, <c>Glob</c>, <c>WebSearch</c>,
/// <c>TodoWrite</c>, and the read-only/utility tools.
/// </summary>
/// <remarks>
/// Per
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Match all uses of a tool", a bare tool name matches every use of that tool.
/// These tools take no specifier, so a rule only matches when it names the tool
/// and carries no (meaningful) specifier.
/// </remarks>
public static class BareToolMatcher
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="rule"/> names the same
    /// tool as <paramref name="candidate"/> and matches all uses of it.
    /// </summary>
    public static bool Match(ParsedPermissionRule rule, PermissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);

        return !rule.IsMcp
               && string.Equals(rule.ToolName, candidate.ToolName, StringComparison.Ordinal)
               && rule.MatchesAllUses;
    }
}
