using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Per-token regex redactor for non-JSON file content destined for a
/// <see cref="BackupMode.Sanitized"/> archive.  Companion to
/// <see cref="JsonRedactor"/>: where the JSON redactor walks parsed
/// nodes and matches on KEY names, this redactor scans arbitrary text
/// and matches on token SHAPES.  Together they cover the two ways
/// secrets surface in a Claude footprint — as values inside a JSON
/// config, or as substrings inside a hook script, an agent markdown
/// file, a project <c>CLAUDE.md</c>, etc.
/// </summary>
/// <remarks>
/// <para>
/// **Why a separate redactor.** A user who hardcodes
/// <c>export ANTHROPIC_API_KEY=sk-ant-…</c> inside a hook .sh, or
/// pastes an example bearer token into a <c>CLAUDE.md</c>, would
/// otherwise have that token land verbatim in their "sharing-safe"
/// archive — the JSON-only filter sees the file as opaque bytes.
/// This class catches the common token shapes by pattern.
/// </para>
/// <para>
/// **Regex strategy — source-generated for perf.** All patterns are
/// declared as <see cref="GeneratedRegexAttribute"/> partial methods
/// (introduced in .NET 7, fully supported on .NET 10).  The Roslyn
/// source generator emits fully-compiled regex implementations at
/// BUILD time — zero JIT warm-up, zero first-use latency, trim-safe,
/// and inlinable.  Strictly better than runtime <c>RegexOptions.Compiled</c>
/// (which still pays IL-emission cost on first use) or instance
/// <see cref="Regex"/> fields (which fall back to the interpreter).
/// </para>
/// <para>
/// **Replacement value.** Every match becomes
/// <see cref="JsonRedactor.RedactedMarker"/> (the literal
/// <c>"[redacted]"</c>) so all four redaction surfaces — audit log,
/// save diff, sanitized JSON, sanitized text — emit an identical
/// placeholder.  Support workflows that grep for the marker only need
/// to handle one string.
/// </para>
/// <para>
/// **Best-effort, not exhaustive.** The pattern set targets common
/// shapes (Anthropic, OpenAI, GitHub, GitLab, AWS, Slack, JWT, HTTP
/// Bearer, shell-style sensitive assignments).  A novel proprietary
/// token format will pass through unmasked — that's a deliberate
/// trade-off: tightening the patterns to "anything that looks vaguely
/// like a high-entropy string" produces too many false positives on
/// non-secret content (hashes, base64 image blobs, etc.).  Users
/// sharing a sanitized backup should still review the output.
/// </para>
/// </remarks>
public static partial class TextRedactor
{
    // ─────────────────────────────────────────────────────────────────────
    // Pattern set.  Each one is declared as a partial method with
    // [GeneratedRegex] so the source generator emits the compiled IR
    // at build time.  Order of replacement matters slightly — we run
    // the most specific patterns (vendor-prefixed tokens) BEFORE the
    // generic "shell-style sensitive assignment" pass so a line like
    // `export ANTHROPIC_API_KEY=sk-ant-...` ends up with both the
    // vendor-pattern match (replacing the value) AND the shell-
    // assignment regex's MatchEvaluator preserving the key name.
    // The order is idempotent: running the redactor twice yields the
    // same output.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Anthropic API keys: <c>sk-ant-…</c> with 40+ char tail.</summary>
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{40,}")]
    private static partial Regex AnthropicKeyRegex();

    /// <summary>
    /// OpenAI keys (legacy, project, OpenRouter).  Covers
    /// <c>sk-…</c>, <c>sk-proj-…</c>, <c>sk-or-…</c>.  The 32-char tail
    /// is the minimum legacy length; OpenAI project keys are longer.
    /// </summary>
    [GeneratedRegex(@"sk-(?:proj-|or-)?[A-Za-z0-9_\-]{32,}")]
    private static partial Regex OpenAiKeyRegex();

    /// <summary>
    /// GitHub Personal Access Tokens (classic + OAuth + server-to-
    /// server + user-to-server + refresh).  The five prefixes
    /// <c>ghp_</c> / <c>gho_</c> / <c>ghu_</c> / <c>ghs_</c> /
    /// <c>ghr_</c> all use a 36-char alphanumeric body.
    /// </summary>
    [GeneratedRegex("gh[pousr]_[A-Za-z0-9]{36,}")]
    private static partial Regex GitHubPatRegex();

    /// <summary>
    /// GitHub fine-grained PATs (<c>github_pat_…</c>) — 82+ char body
    /// with underscores allowed.
    /// </summary>
    [GeneratedRegex("github_pat_[A-Za-z0-9_]{82,}")]
    private static partial Regex GitHubFineGrainedPatRegex();

    /// <summary>GitLab PATs (<c>glpat-…</c>) — 20+ char tail.</summary>
    [GeneratedRegex(@"glpat-[A-Za-z0-9_\-]{20,}")]
    private static partial Regex GitLabPatRegex();

    /// <summary>
    /// AWS Access Key IDs: <c>AKIA</c> prefix followed by exactly 16
    /// uppercase alphanumerics.  AWS Secret Access Keys are
    /// base64-shaped without a fixed prefix; we rely on the shell-
    /// style assignment pattern below to catch them via the
    /// <c>AWS_SECRET_ACCESS_KEY</c> variable name.
    /// </summary>
    [GeneratedRegex("AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKeyRegex();

    /// <summary>
    /// Slack legacy + bot + user + app + refresh tokens: <c>xox[abprs]-</c>
    /// prefix followed by hyphen-separated alphanumeric segments.
    /// </summary>
    [GeneratedRegex(@"xox[abprs]-[A-Za-z0-9\-]{10,}")]
    private static partial Regex SlackTokenRegex();

    /// <summary>
    /// JWT-shaped tokens — three base64url-encoded segments separated
    /// by dots, with the header decoded fragment <c>eyJ</c> at the
    /// start (which is the base64url for <c>{"</c>, the universal JWT
    /// header opener).  Catches Anthropic / OAuth / generic bearer
    /// JWTs irrespective of issuer.
    /// </summary>
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+")]
    private static partial Regex JwtRegex();

    /// <summary>
    /// HTTP <c>Bearer …</c> header values.  Case-insensitive on the
    /// scheme name; the token body is base64-url alphabet plus the
    /// few non-padding URL-safe characters.
    /// </summary>
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=\-]{20,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenRegex();

    /// <summary>
    /// Shell-style assignment whose key name matches the sensitive-
    /// key classifier.  Captures the KEY (group 1) so the
    /// <see cref="MatchEvaluator"/> can preserve it and substitute
    /// <see cref="JsonRedactor.RedactedMarker"/> for the value only —
    /// the user gets diagnostic value from seeing
    /// <c>export ANTHROPIC_API_KEY=[redacted]</c> instead of an
    /// opaque <c>[redacted]</c> that hides which key the script was
    /// setting.
    /// </summary>
    /// <remarks>
    /// IgnoreCase + Multiline are on the attribute (not inline
    /// <c>(?im)</c>) so the source generator can specialise the
    /// state machine for those flags rather than carrying them as
    /// runtime checks.
    /// </remarks>
    [GeneratedRegex(
        """^(?:export\s+)?([A-Z][A-Z0-9_]*(?:TOKEN|SECRET|PASSWORD|KEY|API_KEY|BEARER|AUTH|CREDENTIAL)[A-Z0-9_]*)\s*=\s*['"]?([^'"\s]+)['"]?""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ShellSensitiveAssignmentRegex();

    /// <summary>
    /// Replace every recognised secret-shape match in
    /// <paramref name="content"/> with
    /// <see cref="JsonRedactor.RedactedMarker"/>.  Returns the
    /// original string unchanged when no matches fire.  Empty / null
    /// inputs round-trip as <see cref="string.Empty"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent: <c>Redact(Redact(s)) == Redact(s)</c> because the
    /// marker string contains no characters that match any of the
    /// token patterns above.  Useful for callers that pre-redact at
    /// one stage and re-redact later as a defence-in-depth check.
    /// </para>
    /// <para>
    /// **Order of operations.** Vendor-prefixed patterns run first
    /// (Anthropic / OpenAI / …) so the most specific shapes redact
    /// first; the shell-style assignment pass runs last and operates
    /// on whatever residual <c>VAR=value</c> shapes the vendor passes
    /// didn't catch — typically AWS secret keys (no fixed prefix)
    /// and proprietary tokens whose KEY name signals sensitivity.
    /// </para>
    /// </remarks>
    public static string Redact(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        string s = content;
        s = AnthropicKeyRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = OpenAiKeyRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = GitHubFineGrainedPatRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = GitHubPatRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = GitLabPatRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = AwsAccessKeyRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = SlackTokenRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = JwtRegex().Replace(s, JsonRedactor.RedactedMarker);
        s = BearerTokenRegex().Replace(s, JsonRedactor.RedactedMarker);

        // Shell-style assignment runs last with a MatchEvaluator so
        // the key name survives.  Group 1 = key name, group 2 = value.
        s = ShellSensitiveAssignmentRegex().Replace(s, m =>
        {
            // Recover whatever the original prefix was (export or
            // nothing) by taking the slice from the match start to
            // the start of group 1.
            string prefix = m.Value[..(m.Groups[1].Index - m.Index)];
            return $"{prefix}{m.Groups[1].Value}={JsonRedactor.RedactedMarker}";
        });

        return s;
    }
}