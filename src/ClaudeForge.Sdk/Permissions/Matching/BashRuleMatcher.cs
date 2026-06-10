using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches a single Bash/PowerShell command string against a Bash-family
/// permission rule's specifier.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Bash" and §"Wildcard patterns". Matching is <b>glob-based, not prefix</b>:
/// <c>*</c> may appear at any position and matches any sequence of characters
/// including spaces. Because non-wildcard characters match literally, a space
/// before a trailing <c>*</c> naturally enforces a word boundary —
/// <c>Bash(ls *)</c> matches <c>ls -la</c> but not <c>lsof</c>, while
/// <c>Bash(ls*)</c> matches both.
/// </para>
/// <para>
/// <b>Trailing wildcard = optional arguments.</b> The canonical Claude form is the
/// colon suffix — <c>Bash(npm run test:*)</c> means "<c>npm run test</c> with any
/// flags/args, <i>including none</i>". A trailing <c>:*</c> and a trailing
/// <c> *</c> (space-star) are treated as equivalent and both mean "the prefix,
/// optionally followed by a space and more text". So <c>Bash(git push:*)</c> and
/// <c>Bash(git push *)</c> both match the bare command <c>git push</c> as well as
/// <c>git push origin main</c>, but neither matches <c>git pushx</c> (the space
/// boundary is preserved). Only the trailing token is special; a mid-pattern
/// <c>*</c> stays an ordinary glob and a mid-pattern colon stays literal.
/// </para>
/// <para>
/// <b>Not a prefix matcher, not a security boundary.</b> The old guidance that a
/// Bash allow rule is trivially bypassed by <c>&amp;&amp;</c> chaining is
/// <i>obsolete</i> — Claude Code splits compound commands and strips wrappers
/// (see <see cref="BashCommandSplitter"/>) so each subcommand must match. The
/// real, current caveat is narrower: rules that try to constrain command
/// <i>arguments</i> are fragile (option reordering, alternate protocols,
/// redirects, variables, extra spaces all defeat them — see the spec's curl
/// example). This matcher faithfully models the glob; it does not attempt to be
/// a security control.
/// </para>
/// <para>
/// This matcher evaluates ONE simple command. Compound-splitting and
/// wrapper-stripping are applied by <see cref="PermissionResolver"/> via
/// <see cref="BashCommandSplitter"/> before calling here.
/// </para>
/// </remarks>
public static class BashRuleMatcher
{
    // Defensive cap against a pathological user pattern; a timeout is treated as
    // "no match" rather than throwing into the UI. Mirrors GitignoreReader's
    // stance (src/ClaudeForge.Core/Backup/GitignoreReader.cs).
    private static readonly TimeSpan s_timeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="command"/> matches the
    /// Bash-family <paramref name="rule"/>. A bare-tool or <c>Tool(*)</c> rule
    /// matches any command. <paramref name="caseInsensitive"/> should be set for
    /// PowerShell (the spec states PowerShell matching is case-insensitive;
    /// Bash is case-sensitive).
    /// </summary>
    public static bool Match(ParsedPermissionRule rule, string command, bool caseInsensitive = false)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (command is null)
        {
            return false;
        }

        if (rule.MatchesAllUses)
        {
            return true;
        }

        if (rule.Specifier is not { Length: > 0 } specifier)
        {
            return false;
        }

        string regex = GlobToRegex(specifier);
        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return Regex.IsMatch(command.Trim(), regex, options, s_timeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Convert a Bash glob specifier to an anchored regex. Every character is
    /// literal except <c>*</c>, which becomes <c>.*</c>. A <b>trailing</b>
    /// <c>:*</c> or <c> *</c> (space-star) is special: it denotes optional
    /// arguments and compiles to <c>( .*)?</c>, so the prefix matches with or
    /// without a following space + text (e.g. <c>git push:*</c> and
    /// <c>git push *</c> both match bare <c>git push</c> and <c>git push origin</c>,
    /// but not <c>git pushx</c>). A non-trailing <c>*</c>, or a trailing <c>*</c>
    /// not preceded by a space (e.g. <c>ls*</c>), stays an ordinary glob.
    /// </summary>
    internal static string GlobToRegex(string glob)
    {
        bool optionalArgs =
            glob.EndsWith(":*", StringComparison.Ordinal) ||
            glob.EndsWith(" *", StringComparison.Ordinal);

        string prefix = optionalArgs ? glob[..^2] : glob;

        // Escape everything (turns '*' into '\*'), then re-open the wildcard so
        // any interior '*' remains a glob.
        string body = Regex.Escape(prefix).Replace("\\*", ".*");
        return optionalArgs ? $"^{body}( .*)?$" : $"^{body}$";
    }
}
