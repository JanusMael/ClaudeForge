using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;

/// <summary>
/// Single source of truth for the permission-rule tool-name taxonomy. Mirrors the
/// bundled schema's <c>$defs.permissionRule</c> tool-name alternation.
/// <para>
/// Both the SDK validator (<see cref="PermissionRule"/>) and the GUI editor
/// (<c>PermissionRuleViewModel</c>) derive their regex AND their known-name set
/// from here, so the list can no longer drift across what were three
/// hand-maintained copies — the proven <c>Pwsh</c>/<c>Monitor</c> drift. A guard
/// test (<c>PermissionToolsTests</c>) locks <see cref="Names"/> to the schema's
/// enumerated tool names.
/// </para>
/// </summary>
public static class PermissionTools
{
    /// <summary>
    /// The known tool names in canonical (schema) order. The ordered list drives
    /// the regex alternation; <see cref="NameSet"/> is the ordinal membership set.
    /// </summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "Agent", "Artifact", "Bash", "Cd", "Edit", "EnterWorktree", "ExitPlanMode", "Glob", "Grep",
        "KillShell", "LSP", "Monitor", "MultiEdit", "NotebookEdit", "PowerShell", "Read",
        "ShareOnboardingGuide", "Skill", "TaskCreate", "TaskGet",
        "TaskList", "TaskOutput", "TaskStop", "TaskUpdate", "TodoWrite", "ToolSearch",
        "WebFetch", "WebSearch", "Workflow", "Write",
    ];

    /// <summary>Ordinal membership set built from <see cref="Names"/>.</summary>
    public static readonly IReadOnlySet<string> NameSet =
        new HashSet<string>(Names, StringComparer.Ordinal);

    /// <summary>
    /// The canonical permission-rule regex pattern, built from <see cref="Names"/>.
    /// A valid rule is a known tool name optionally followed by a parenthesised
    /// pattern, OR any <c>mcp__</c>-prefixed identifier.
    /// <para>
    /// The parens content <c>[*?]*[^)*?][^)]*</c> must contain at least one
    /// character other than <c>*</c> and <c>?</c>, which rejects all-wildcard
    /// content (e.g. <c>Bash(*)</c>) — an INTENTIONAL divergence from the schema's
    /// looser <c>permissionRule</c> pattern, which accepts any non-empty parens
    /// content. The SDK and GUI both validate with <see cref="RuleRegex"/>, so they
    /// cannot diverge from each other.
    /// </para>
    /// <para>
    /// ⚠ The pattern must stay lookaround-free: <see cref="RuleRegex"/> runs on the
    /// <see cref="RegexOptions.NonBacktracking"/> engine, which rejects lookarounds.
    /// It replaced the lookahead <c>(?=.*[^)*?])[^)]+</c>, which accepted the same
    /// strings with one exception. That lookahead scanned past the closing paren, so a
    /// trailing newline let all-wildcard content such as <c>"Bash(*)\n"</c> through.
    /// </para>
    /// </summary>
    public static string RulePattern { get; } =
        "^((" + string.Join("|", Names) + @")(\([*?]*[^)*?][^)]*\))?|mcp__.*)$";

    /// <summary>
    /// The <see cref="Regex"/> for <see cref="RulePattern"/>, shared so callers
    /// don't each build the same pattern. Culture-invariant.
    /// <para>
    /// <see cref="RegexOptions.NonBacktracking"/> makes a match linear in the input
    /// length by construction, so it has no match timeout. The GUI validates on every
    /// keystroke, and a wall-clock timeout fires on a busy machine for a 9-character
    /// rule, then reports a valid rule as invalid.
    /// </para>
    /// </summary>
    public static readonly Regex RuleRegex = new(
        RulePattern,
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
}
