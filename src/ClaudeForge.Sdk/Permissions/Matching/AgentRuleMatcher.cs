namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches a subagent invocation against an <c>Agent(Name)</c> permission rule.
/// </summary>
/// <remarks>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Agent (subagents)": <c>Agent(Explore)</c>, <c>Agent(Plan)</c>,
/// <c>Agent(my-custom-agent)</c>. A bare <c>Agent</c> rule matches every
/// subagent. The spec lists exact names only (no wildcard syntax documented), so
/// the specifier is compared for equality (ordinal).
/// </remarks>
public static class AgentRuleMatcher
{
    /// <summary>Returns <see langword="true"/> when the agent candidate matches the rule.</summary>
    public static bool Match(ParsedPermissionRule rule, PermissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(candidate);

        if (rule.MatchesAllUses)
        {
            return true;
        }

        return rule.Specifier is { Length: > 0 } spec
               && candidate.AgentName is { Length: > 0 } name
               && string.Equals(spec, name, StringComparison.Ordinal);
    }
}
