using System.Text;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches a candidate file path against a <c>Read</c> / <c>Edit</c> /
/// <c>Write</c> permission rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Read and Edit". Path rules follow the
/// <see href="https://git-scm.com/docs/gitignore">gitignore specification</see>
/// with four anchor types:
/// </para>
/// <list type="table">
///   <item><term><c>//path</c></term><description>absolute from filesystem root</description></item>
///   <item><term><c>~/path</c></term><description>relative to the home directory</description></item>
///   <item><term><c>/path</c></term><description>relative to the <b>project root</b> (NOT absolute)</description></item>
///   <item><term><c>path</c> / <c>./path</c></term><description>relative to the current directory</description></item>
/// </list>
/// <para>
/// Gitignore semantics: a pattern with no <c>/</c> matches at any depth
/// (<c>Read(.env)</c> ≡ <c>Read(**/.env)</c>); a pattern containing <c>/</c> is
/// anchored to its base. <c>*</c> matches within one path segment; <c>**</c>
/// matches across segments. Windows paths are normalized to POSIX form
/// (<c>C:\Users</c> → <c>/c/Users</c>) before matching.
/// </para>
/// <para>
/// The glob→regex conversion is adapted from
/// <c>src/ClaudeForge.Core/Backup/GitignoreReader.cs</c> (that type is internal,
/// disk-oriented, and backup-specific, so the algorithm is reimplemented here
/// rather than referenced).
/// </para>
/// <para>
/// <b>Not replicated:</b> symlink dual-path resolution (allow needs both
/// symlink+target; deny needs either) — the dry-run tester evaluates the path as
/// given. This is a teaching tool, not the enforcement path.
/// </para>
/// </remarks>
public static class PathRuleMatcher
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidatePath"/>
    /// matches the path <paramref name="rule"/> resolved against
    /// <paramref name="context"/>. A bare <c>Read</c>/<c>Edit</c>/<c>Write</c>
    /// rule matches any path.
    /// </summary>
    public static bool Match(
        ParsedPermissionRule rule,
        string candidatePath,
        PermissionMatchContext context)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(candidatePath))
        {
            return false;
        }

        if (rule.MatchesAllUses)
        {
            return true;
        }

        if (rule.Specifier is not { Length: > 0 } spec)
        {
            return false;
        }

        bool caseInsensitive = context.CaseInsensitivePaths;
        string candidate = ToPosixAbsolute(candidatePath, context.CurrentDirectory);
        (string baseDir, string sub, bool anchored) = ResolveAnchor(spec, context);

        string? rel = RelativeUnder(candidate, baseDir, caseInsensitive);
        if (rel is null)
        {
            return false;
        }

        string regex = BuildRegex(sub, anchored);
        RegexOptions options = RegexOptions.CultureInvariant
                               | (caseInsensitive ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            return Regex.IsMatch(rel, regex, options, s_timeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve the rule specifier into its base directory (absolute POSIX), the
    /// sub-pattern below that base, and whether the sub-pattern is anchored to
    /// the base (vs. matching at any depth per gitignore's "no slash" rule).
    /// </summary>
    internal static (string baseDir, string sub, bool anchored) ResolveAnchor(
        string specifier, PermissionMatchContext context)
    {
        string spec = specifier.Replace('\\', '/');

        // Windows absolute path (e.g. C:\b\d\e → C:/b/d/e). Map to the POSIX drive
        // form (/c/b/d/e) — the same normalization ToPosix applies to candidate
        // paths — and treat it as absolute from the filesystem root. Without this,
        // a drive-letter path falls through to the bare-pattern branch and is
        // wrongly resolved relative to the current directory.
        if (spec.Length >= 2 && char.IsLetter(spec[0]) && spec[1] == ':')
        {
            return ("/", ToPosix(spec).TrimStart('/'), true);
        }

        if (spec.StartsWith("//", StringComparison.Ordinal))
        {
            // Absolute from filesystem root. The remainder may ITSELF be a Windows
            // drive path (//C:/c/cl → /c/c/cl), so run it through ToPosix — the same
            // drive→/c normalization candidate paths receive — before rooting at "/".
            // Without this the drive letter ("C:") leaks into the sub-pattern verbatim
            // and never matches a candidate whose drive was normalized to "/c"; the
            // rule then silently matches nothing (which also makes its globs look inert).
            string remainder = spec[1..].TrimStart('/');
            return ("/", ToPosix(remainder).TrimStart('/'), true);
        }

        if (spec.StartsWith("~/", StringComparison.Ordinal))
        {
            return (ToPosixDir(context.HomeDirectory), spec[2..], true);
        }

        if (spec.StartsWith('/'))
        {
            // Single leading slash = project-root relative (NOT absolute).
            return (ToPosixDir(context.ProjectRoot), spec[1..], true);
        }

        string cwd = ToPosixDir(context.CurrentDirectory);
        if (spec.StartsWith("./", StringComparison.Ordinal))
        {
            return (cwd, spec[2..], true);
        }

        // Bare pattern: gitignore says no-slash matches at any depth; a slash
        // anchors it to the base (cwd).
        bool anchored = spec.Contains('/');
        return (cwd, spec, anchored);
    }

    /// <summary>
    /// Build an anchored regex for a gitignore sub-pattern. When
    /// <paramref name="anchored"/> is false, the pattern may match at any depth
    /// (the "bare filename" gitignore rule).
    /// </summary>
    internal static string BuildRegex(string sub, bool anchored)
    {
        string body = GlobBody(sub);
        return anchored ? $"^{body}$" : $"^(.*/)?{body}$";
    }

    // Adapted from GitignoreReader.PatternToRegex: `**` = any depth (consuming an
    // optional adjoining slash), `*` = within one segment, `?` = one non-slash.
    private static string GlobBody(string pattern)
    {
        StringBuilder sb = new();
        int i = 0;
        while (i < pattern.Length)
        {
            if (i + 1 < pattern.Length && pattern[i] == '*' && pattern[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
                if (i < pattern.Length && pattern[i] == '/')
                {
                    i++;
                }
            }
            else if (pattern[i] == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (pattern[i] == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The portion of <paramref name="candidate"/> below <paramref name="baseDir"/>
    /// (POSIX, no leading slash), or <see langword="null"/> when the candidate is
    /// not at/under the base. An empty string means the candidate IS the base.
    /// <paramref name="caseInsensitive"/> selects the prefix-comparison mode,
    /// matching the target filesystem (see <see cref="PermissionMatchContext.CaseInsensitivePaths"/>).
    /// </summary>
    internal static string? RelativeUnder(string candidate, string baseDir, bool caseInsensitive)
    {
        StringComparison cmp = caseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string b = baseDir.TrimEnd('/');
        if (b.Length == 0)
        {
            // Base is filesystem root "/".
            return candidate.TrimStart('/');
        }

        if (candidate.Equals(b, cmp))
        {
            return string.Empty;
        }

        string prefix = b + "/";
        return candidate.StartsWith(prefix, cmp)
            ? candidate[prefix.Length..]
            : null;
    }

    /// <summary>Normalize a directory path to POSIX form with no trailing slash.</summary>
    internal static string ToPosixDir(string path) => ToPosix(path).TrimEnd('/');

    /// <summary>
    /// Normalize a (possibly relative) path to an absolute POSIX path, resolving
    /// it against <paramref name="cwd"/> when relative and collapsing
    /// <c>.</c>/<c>..</c> segments.
    /// </summary>
    internal static string ToPosixAbsolute(string path, string cwd)
    {
        string p = ToPosix(path);
        bool rooted = p.StartsWith('/')
                      || (p.Length >= 3 && p[0] == '/' && char.IsLetter(p[1]) && p[2] == '/'); // /c/...
        if (!rooted)
        {
            p = ToPosix(cwd).TrimEnd('/') + "/" + p;
        }

        return CollapseDots(p);
    }

    private static string ToPosix(string path)
    {
        string p = path.Replace('\\', '/');

        // Drive-letter form: C:/Users → /c/Users
        if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':')
        {
            string rest = p.Length > 2 ? p[2..] : string.Empty;
            if (!rest.StartsWith('/'))
            {
                rest = "/" + rest;
            }

            p = "/" + char.ToLowerInvariant(p[0]) + rest;
        }

        return p;
    }

    private static string CollapseDots(string posixPath)
    {
        string[] segments = posixPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<string> stack = [];
        foreach (string seg in segments)
        {
            if (seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(seg);
        }

        return "/" + string.Join('/', stack);
    }
}
