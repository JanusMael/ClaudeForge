using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

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
        "Agent", "Bash", "Cd", "Edit", "ExitPlanMode", "Glob", "Grep", "KillShell", "LSP",
        "Monitor", "MultiEdit", "NotebookEdit", "PowerShell", "Read", "Skill", "TaskCreate", "TaskGet",
        "TaskList", "TaskOutput", "TaskStop", "TaskUpdate", "TodoWrite", "ToolSearch",
        "WebFetch", "WebSearch", "Write",
    ];

    /// <summary>Ordinal membership set built from <see cref="Names"/>.</summary>
    public static readonly IReadOnlySet<string> NameSet =
        new HashSet<string>(Names, StringComparer.Ordinal);

    /// <summary>
    /// The canonical permission-rule regex pattern, built from <see cref="Names"/>.
    /// A valid rule is a known tool name optionally followed by a parenthesised
    /// pattern, OR any <c>mcp__</c>-prefixed identifier.
    /// <para>
    /// The strict lookahead <c>(?=.*[^)*?])</c> rejects all-wildcard parens content
    /// (e.g. <c>Bash(*)</c>) — an INTENTIONAL divergence from the schema's looser
    /// <c>permissionRule</c> pattern, which accepts any non-empty parens content.
    /// The SDK and GUI regexes are both built from this one string, so they cannot
    /// diverge from each other.
    /// </para>
    /// </summary>
    public static string RulePattern { get; } =
        "^((" + string.Join("|", Names) + @")(\((?=.*[^)*?])[^)]+\))?|mcp__.*)$";

    /// <summary>
    /// A compiled <see cref="Regex"/> for <see cref="RulePattern"/>, shared so
    /// callers don't each recompile the same pattern. Culture-invariant.
    /// </summary>
    public static readonly Regex RuleRegex = new(
        RulePattern,
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
