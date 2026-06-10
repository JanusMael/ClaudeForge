namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Matches a candidate URL against a <c>WebFetch</c> permission rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"WebFetch": <c>WebFetch(domain:example.com)</c> matches fetch requests to
/// example.com.
/// </para>
/// <para>
/// <b>Assumption.</b> The spec states exact-domain matching but is silent on
/// subdomains. This matcher treats a rule domain as covering the exact host
/// <i>and</i> its subdomains (<c>example.com</c> matches <c>docs.example.com</c>),
/// which is the least-surprising reading for a domain allowlist. Comparison is
/// case-insensitive. If Claude Code's behavior is later pinned to exact-host
/// only, tighten <see cref="HostMatches"/>.
/// </para>
/// </remarks>
public static class WebFetchRuleMatcher
{
    /// <summary>Returns <see langword="true"/> when <paramref name="url"/> matches the rule.</summary>
    public static bool Match(ParsedPermissionRule rule, string url)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(url))
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

        // Accept "domain:example.com" (canonical) and a bare "example.com" (lenient).
        string domain = spec.StartsWith("domain:", StringComparison.OrdinalIgnoreCase)
            ? spec["domain:".Length..]
            : spec;
        domain = domain.Trim();
        if (domain.Length == 0)
        {
            return false;
        }

        string host = ExtractHost(url);
        return HostMatches(host, domain);
    }

    internal static bool HostMatches(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        // The user may have typed a bare host (no scheme). Strip any path/query.
        string s = url.Trim();
        int slash = s.IndexOf('/');
        if (slash >= 0)
        {
            s = s[..slash];
        }

        int at = s.IndexOf('@');
        if (at >= 0)
        {
            s = s[(at + 1)..];
        }

        int colon = s.IndexOf(':');
        if (colon >= 0)
        {
            s = s[..colon];
        }

        return s;
    }
}
