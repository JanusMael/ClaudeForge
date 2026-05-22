using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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
/// </remarks>
public sealed partial record PermissionRule(string Value)
{
    // Mirrors src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json
    //   $defs.permissionRule.pattern
    // Kept here as the canonical SDK-side regex so the SDK does not depend on
    // the bundled schema asset at validation time. If the schema's tool-name
    // list grows, this regex must be updated alongside it.
    [GeneratedRegex(
        @"^((Agent|Bash|Edit|ExitPlanMode|Glob|Grep|KillShell|LSP|NotebookEdit|PowerShell|Pwsh|Read|Skill|TaskCreate|TaskGet|TaskList|TaskOutput|TaskStop|TaskUpdate|TodoWrite|ToolSearch|WebFetch|WebSearch|Write)(\((?=.*[^)*?])[^)]+\))?|mcp__.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuleRegex();

    /// <summary>
    /// Parses <paramref name="s"/> into a <see cref="PermissionRule"/>.
    /// Throws <see cref="FormatException"/> for inputs that do not match
    /// the documented permission-rule grammar.
    /// </summary>
    public static PermissionRule Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (!RuleRegex().IsMatch(s))
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
        if (s is not null && RuleRegex().IsMatch(s))
        {
            rule = new PermissionRule(s);
            return true;
        }

        rule = null;
        return false;
    }
}