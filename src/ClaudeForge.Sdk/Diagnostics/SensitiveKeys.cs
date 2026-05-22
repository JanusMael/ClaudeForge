namespace Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

/// <summary>
/// Heuristic classifier for top-level config keys whose values are likely
/// to contain secrets (API keys, OAuth tokens, passwords).  Used by audit
/// loggers and any consumer that wants to redact secret-bearing values
/// before they travel into bug reports, log files, or shared diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Combines an exact-match list (case-insensitive) with substring heuristics
/// so future schema additions named e.g. <c>githubAccessToken</c> or
/// <c>clientSecret</c> are masked without an explicit allowlist update.
/// Conservative: will NOT mask key names that merely contain "key", since
/// that over-matches innocent fields like <c>uniqueKey</c> or <c>locKey</c>.
/// </para>
/// <para>
/// the classifier is a pure function with
/// no UI or Serilog dependency, and out-of-process SDK consumers (MCP
/// servers, CLI tools) need the same redaction policy.
/// </para>
/// </remarks>
public static class SensitiveKeys
{
    /// <summary>
    /// Marker substituted for sensitive values when rendering save-time
    /// diffs into logs / reports.  Any key flagged by
    /// <see cref="IsSensitive"/> has its old/new JSON replaced by this
    /// string so secrets that travel with bug reports stay scrubbed.
    /// </summary>
    public const string RedactedMarker = "[redacted]";

    /// <summary>
    /// Path SEGMENTS whose subtree values are always treated as
    /// secret-bearing.  When <see cref="IsSensitive"/> receives a dotted
    /// JSON-path, any segment in this set causes the leaf to be redacted.
    /// Examples that match: <c>"env"</c>, <c>"env.ANTHROPIC_API_KEY"</c>,
    /// <c>"mcpServers.gh.headers.Authorization"</c>,
    /// <c>"credentials.refresh_token"</c>.  Examples that DO NOT match the
    /// segment-set (and rely on the substring heuristics below):
    /// <c>"githubAccessToken"</c>, <c>"clientSecret"</c>.
    /// </summary>
    /// <remarks>
    /// promoted from "exact full-path match" to
    /// "any-segment match" after a PII audit found that values UNDER
    /// <c>mcpServers.&lt;name&gt;.headers.&lt;header&gt;</c> were not
    /// redacted: the previous <c>_exact.Contains(fullPath)</c> check
    /// only matched a top-level <c>"headers"</c> property (rare in real
    /// configs), and headers nested under MCP server entries leaked
    /// Authorization / X-API-Key / Cookie values into the rolling log.
    /// Splitting the path and checking each segment closes that gap.
    /// </remarks>
    private static readonly HashSet<string> _segmentExact =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "env", // env-var map: {"env": {"ANTHROPIC_API_KEY": "..."}}
            "headers", // HTTP headers (MCP server config) — Authorization etc.
            "credentials", // any credentials sub-tree
            "auth", // common alias for credentials
            "authorization", // direct header name (defensive — usually under "headers")
        };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="key"/>'s value
    /// is likely to carry a secret and should be redacted before logging
    /// or sharing.  Two-pass match:
    /// <list type="number">
    ///   <item>
    ///     Path-segment match: split <paramref name="key"/> on
    ///     <c>'.'</c> and check if any segment is in
    ///     <see cref="_segmentExact"/>.  Catches anything under <c>env</c>,
    ///     <c>headers</c>, <c>credentials</c>, etc. regardless of nesting.
    ///   </item>
    ///   <item>
    ///     Full-path substring match: covers schema additions named e.g.
    ///     <c>githubAccessToken</c>, <c>clientSecret</c>, <c>password</c>.
    ///     Conservative — won't mask innocent fields containing "key"
    ///     (which would over-match <c>uniqueKey</c> / <c>locKey</c>).
    ///   </item>
    /// </list>
    /// </summary>
    public static bool IsSensitive(string key)
    {
        // Path-segment pass: split on "." and check membership.
        // Avoids substring false-positives (e.g. "headerSize" matching
        // "headers") and catches arbitrarily-nested sub-trees.
        foreach (string segment in key.Split('.'))
        {
            if (_segmentExact.Contains(segment))
            {
                return true;
            }
        }

        // Full-path substring pass — for keys NAMED with secret-bearing
        // terminology even when they're not under a known section.
        //
        // added `private` (covers
        // privateKey / rsa_private) and three access_key
        // hyphen/underscore/concatenated variants for AWS-style
        // identifiers.  Lockstep with
        // ClaudeForge.Core.Backup.JsonRedactor.SubstringTokens
        return key.Contains("token", StringComparison.OrdinalIgnoreCase)
               || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || key.Contains("password", StringComparison.OrdinalIgnoreCase)
               || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
               || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
               || key.Contains("api-key", StringComparison.OrdinalIgnoreCase) // hyphen variant (X-Api-Key)
               || key.Contains("bearer", StringComparison.OrdinalIgnoreCase)
               || key.Contains("private", StringComparison.OrdinalIgnoreCase) // privateKey / rsa_private
               || key.Contains("accesskey", StringComparison.OrdinalIgnoreCase) // AWS-style accesskey
               || key.Contains("access_key", StringComparison.OrdinalIgnoreCase)
               || key.Contains("access-key", StringComparison.OrdinalIgnoreCase);
    }
}