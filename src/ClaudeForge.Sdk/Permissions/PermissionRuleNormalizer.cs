namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// Rewrites a permission-rule string into ClaudeForge's canonical on-disk form
/// at add time, so the saved rule matches the syntax Claude Code's own docs use
/// and so semantically-equivalent inputs converge on one representation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>.
/// One normalization is applied:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Read / Edit / Write path separators.</b> Claude's path rules are
///     gitignore-style with forward slashes. A Windows user typing
///     <c>Read(src\app\**)</c> is normalized to <c>Read(src/app/**)</c>. Only
///     backslashes are converted — the forward-slash anchors <c>//absolute</c>,
///     <c>~/home</c>, <c>/project-root</c> and <c>./current</c> are preserved
///     verbatim (we never collapse or rewrite forward slashes).
///   </item>
/// </list>
/// <para>
/// Web / MCP / Agent rules and bare tool names pass through unchanged. The method
/// never throws: an unparseable input is returned as-is.
/// </para>
/// </remarks>
public static class PermissionRuleNormalizer
{
    /// <summary>
    /// Returns the canonical form of <paramref name="rule"/> (see the type remarks),
    /// or the input unchanged when no normalization applies.
    /// </summary>
    public static string Normalize(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return rule;
        }

        if (!ParsedPermissionRule.TryParse(rule, out ParsedPermissionRule? parsed))
        {
            return rule;
        }

        // Bare tools and MCP rules carry no specifier to normalize.
        if (parsed.IsBareTool || parsed.IsMcp || parsed.Specifier is not { Length: > 0 } specifier)
        {
            return rule;
        }

        switch (parsed.ToolName)
        {
            // NOTE: Bash/PowerShell specifiers are intentionally NOT normalized.
            // A trailing " *" (optional space-args) and ":*" (literal-colon +
            // remainder) are DISTINCT match semantics in BashRuleMatcher
            // (`npm run test *` ≠ `npm run test:*` against `npm run test:unit`),
            // so rewriting one to the other would change what a rule matches.
            // The specifier is preserved exactly as the user wrote it.
            case "Read":
            case "Edit":
            case "Write":
            {
                if (specifier.Contains('\\', StringComparison.Ordinal))
                {
                    string forward = specifier.Replace('\\', '/');
                    return $"{parsed.ToolName}({forward})";
                }

                return rule;
            }

            default:
                return rule;
        }
    }
}
