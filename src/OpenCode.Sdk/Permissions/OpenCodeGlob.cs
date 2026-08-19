using System.Text;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.OpenCode.Sdk.Permissions;

/// <summary>
/// The pattern matcher behind OpenCode's permission rules: <c>*</c>, <c>?</c>, and home
/// expansion for <c>~</c> / <c>$HOME</c>.
/// </summary>
/// <remarks>
/// <para>
/// Patterns are matched against whole strings — a command line such as
/// <c>git commit -m "x"</c>, or a path. <c>*</c> stands for any run of characters including
/// none, and <c>?</c> for exactly one. Everything else is literal, which matters more than it
/// sounds: a pattern like <c>npm install --save-dev</c> contains regex metacharacters that
/// would otherwise change its meaning entirely.
/// </para>
/// <para>
/// ⚠ <b>An interpretation, not a measurement.</b> <c>*</c> here crosses directory separators,
/// so <c>~/.ssh/*</c> matches <c>~/.ssh/nested/key</c>. No spike measured whether OpenCode
/// distinguishes a path context from a command context, and the two plausible readings differ
/// only for nested paths. The permissive reading is the one that fails <i>safe</i> for a deny
/// rule and <i>unsafe</i> for an allow rule, so it is worth measuring before anyone leans on
/// <c>allow</c> patterns containing separators.
/// </para>
/// <para>
/// Matching is case-sensitive on every platform. OpenCode's config is not
/// Windows-specific, and a matcher that quietly case-folded would make
/// <c>"Rm -rf *": "deny"</c> block <c>rm -rf</c> on one platform and not another.
/// </para>
/// </remarks>
public static class OpenCodeGlob
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Whether <paramref name="value"/> matches <paramref name="pattern"/>.
    /// </summary>
    /// <remarks>
    /// An empty pattern matches only the empty string. It is never treated as a wildcard —
    /// the wildcard is <c>"*"</c>, and silently promoting <c>""</c> to it would let a blank
    /// row in an editor apply to everything.
    /// </remarks>
    public static bool IsMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        string expanded = ExpandHome(pattern);

        // The overwhelmingly common case, and worth not building a regex for.
        if (expanded == "*")
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(value, ToRegex(expanded), RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern pathological enough to time out must not be treated as a match: for
            // an allow rule that would grant access on the strength of a hang.
            return false;
        }
    }

    /// <summary>
    /// Expand a leading <c>~</c> or <c>$HOME</c> to the user's home directory.
    /// </summary>
    /// <remarks>
    /// Leading only. A <c>~</c> in the middle of a pattern is a literal character — it is
    /// legal in a filename and common in shell text, and expanding it there would rewrite
    /// patterns the user did not mean as paths.
    /// <para>
    /// Resolved through <see cref="PlatformPaths.UserProfile"/>, so it honours the test
    /// sandbox rather than reading the real home directory.
    /// </para>
    /// </remarks>
    public static string ExpandHome(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        string home = PlatformPaths.UserProfile;

        if (pattern == "~" || pattern == "$HOME")
        {
            return home;
        }

        if (pattern.StartsWith("~/", StringComparison.Ordinal)
            || pattern.StartsWith("~\\", StringComparison.Ordinal))
        {
            return home + pattern[1..];
        }

        if (pattern.StartsWith("$HOME/", StringComparison.Ordinal)
            || pattern.StartsWith("$HOME\\", StringComparison.Ordinal))
        {
            return home + pattern["$HOME".Length..];
        }

        return pattern;
    }

    /// <summary>
    /// Translate a glob to an anchored regex, escaping everything that is not a wildcard.
    /// </summary>
    private static string ToRegex(string pattern)
    {
        StringBuilder sb = new(pattern.Length * 2 + 4);
        sb.Append('^');

        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
