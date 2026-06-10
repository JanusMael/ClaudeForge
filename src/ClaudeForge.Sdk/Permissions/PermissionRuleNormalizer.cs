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
/// Two normalizations are applied:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Bash / PowerShell "any arguments" wildcard.</b> The docs write this as the
///     colon suffix (<c>Bash(npm run test:*)</c>). A trailing space-star
///     (<c>Bash(npm run test *)</c>) means the same thing to
///     <see cref="Matching.BashRuleMatcher"/>, so we rewrite a trailing <c> *</c>
///     to <c>:*</c> for a single canonical form. A colon form is left as-is.
///   </item>
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
            case "Bash":
            case "PowerShell":
            {
                // Canonicalize a trailing " *" (space-star) to ":*". Leave an
                // existing ":*" and any non-trailing wildcard alone.
                if (specifier.EndsWith(" *", StringComparison.Ordinal) &&
                    !specifier.EndsWith(":*", StringComparison.Ordinal))
                {
                    string canonical = specifier[..^2] + ":*";
                    return $"{parsed.ToolName}({canonical})";
                }

                return rule;
            }

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
