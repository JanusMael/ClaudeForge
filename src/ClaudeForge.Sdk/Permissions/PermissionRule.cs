using System.Diagnostics.CodeAnalysis;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// A permission rule string in the format Claude Code accepts — e.g.
/// <c>"Bash(git status)"</c>, <c>"WebFetch(domain:docs.anthropic.com)"</c>,
/// or a bare tool name like <c>"Read"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Validation parity with the bundled JSON-schema <c>permissionRule</c> regex
/// is enforced by <see cref="Parse"/> / <see cref="TryParse"/>. A parsed rule
/// is guaranteed to round-trip through the schema validator without
/// producing an error from the rule shape itself (the schema may still
/// reject a rule for other reasons — e.g. a managed-policy denylist).
/// </para>
/// <para>
/// The validation regex is <see cref="PermissionTools.RuleRegex"/> — the single
/// source of truth for the tool-name taxonomy, shared with the GUI editor's regex
/// so the two cannot drift (the proven <c>Pwsh</c>/<c>Monitor</c> drift). A guard
/// test locks that list to the schema's <c>$defs.permissionRule</c> alternation.
/// </para>
/// </remarks>
public sealed record PermissionRule(string Value)
{
    /// <summary>
    /// Parses <paramref name="s"/> into a <see cref="PermissionRule"/>.
    /// Throws <see cref="FormatException"/> for inputs that do not match
    /// the documented permission-rule grammar.
    /// </summary>
    public static PermissionRule Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!PermissionTools.RuleRegex.IsMatch(s))
        {
            throw new FormatException(
                $"'{s}' is not a valid permission rule. Expected a known tool name optionally " +
                "followed by '(...)' with non-empty content, or a 'mcp__' prefixed identifier.");
        }

        return new PermissionRule(s);
    }

    /// <summary>
    /// Attempts to parse <paramref name="s"/> into a <see cref="PermissionRule"/>.
    /// Returns <see langword="true"/> on success and emits the rule via
    /// <paramref name="rule"/>; otherwise returns <see langword="false"/> with
    /// <paramref name="rule"/> set to <see langword="null"/>.
    /// </summary>
    public static bool TryParse(string s, [NotNullWhen(true)] out PermissionRule? rule)
    {
        if (s is not null && PermissionTools.RuleRegex.IsMatch(s))
        {
            rule = new PermissionRule(s);
            return true;
        }

        rule = null;
        return false;
    }

    /// <summary>
    /// Decompose this (shape-valid) rule into its structural parts for matching.
    /// See <see cref="ParsedPermissionRule"/> for the parsed shape and the
    /// rationale for keeping the strict shape gate (this type) separate from the
    /// permissive evaluation parser.
    /// </summary>
    public ParsedPermissionRule Decompose() => ParsedPermissionRule.Parse(Value);
}
