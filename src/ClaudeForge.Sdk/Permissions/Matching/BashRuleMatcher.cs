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
/// <c> *</c> (space-star) are <b>distinct, not equivalent</b>: <c>:*</c> matches
/// the bare prefix, a <c>:</c>-suffixed subcommand, OR space-args, while <c> *</c>
/// matches the bare prefix or space-args only (no colon suffix). So <c>:*</c> is a
/// strict <i>superset</i> of <c> *</c> — <c>Bash(git push:*)</c> matches
/// <c>git push:weird</c> but <c>Bash(git push *)</c> does not; both match the bare
/// <c>git push</c> and <c>git push origin main</c>, and neither matches
/// <c>git pushx</c> (the space boundary is preserved). Because the two forms mean
/// different things, <see cref="PermissionRuleNormalizer"/> preserves each
/// verbatim and never rewrites one into the other. Only the trailing token is
/// special; a mid-pattern <c>*</c> stays an ordinary glob and a mid-pattern colon
/// stays literal.
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
    /// literal except <c>*</c>, which becomes <c>.*</c>. Two trailing forms denote
    /// a prefix with "any arguments":
    /// <list type="bullet">
    ///   <item><b>Trailing <c>:*</c></b> (the canonical Claude Code form) — the
    ///   prefix matches bare, or followed by a <c>:</c>-suffix, OR a space + args.
    ///   <c>npm run test:*</c> matches bare <c>npm run test</c>, <c>npm run test:unit</c>,
    ///   <c>npm run test:e2e</c>, and <c>npm run test --watch</c>, but NOT
    ///   <c>npm run testify</c> (no <c>:</c>/space delimiter → not a real subcommand).</item>
    ///   <item><b>Trailing <c> *</c> (space-star)</b> — bare or a space + args only
    ///   (no colon-suffix): <c>git push *</c> matches <c>git push</c> and
    ///   <c>git push origin</c>, but not <c>git pushx</c>.</item>
    /// </list>
    /// So <c>:*</c> is a strict superset of <c> *</c> (it additionally allows a
    /// colon suffix). A non-trailing <c>*</c>, or a trailing <c>*</c> not preceded
    /// by <c>:</c> or a space (e.g. <c>ls*</c>), stays an ordinary glob.
    /// </summary>
    internal static string GlobToRegex(string glob)
    {
        // Trailing ":*" — prefix + (nothing | ':' suffix | space args). This is the
        // form Claude Code's docs use for subcommands like npm run test:unit.
        if (glob.EndsWith(":*", StringComparison.Ordinal))
        {
            string prefix = glob[..^2]; // drop the ":*"
            string body = Regex.Escape(prefix).Replace("\\*", ".*");
            return $"^{body}(:.*| .*)?$";
        }

        // Trailing " *" — space-star: bare or a space + args (no colon suffix).
        if (glob.EndsWith(" *", StringComparison.Ordinal))
        {
            string prefix = glob[..^2]; // drop the " *"
            string body = Regex.Escape(prefix).Replace("\\*", ".*");
            return $"^{body}( .*)?$";
        }

        // Ordinary glob.
        string ordinary = Regex.Escape(glob).Replace("\\*", ".*");
        return $"^{ordinary}$";
    }
}
