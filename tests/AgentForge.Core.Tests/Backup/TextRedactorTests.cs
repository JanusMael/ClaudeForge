using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// locks <see cref="TextRedactor.Redact"/>'s pattern set.
/// Companion to <c>JsonRedactorTests</c>: where the JSON redactor matches
/// on KEY names walked from a parsed object tree, this one matches on
/// token SHAPES in arbitrary text.  Both surfaces back
/// <see cref="BackupMode.Sanitized"/>, so a token must be caught by
/// EITHER the JSON walker (when it's a JSON value) OR the text scanner
/// (when it's inside a hook script / markdown / etc.) to be redacted
/// in a sanitized archive.
/// </summary>
/// <remarks>
/// Tests organised by pattern category.  Each category has a positive
/// case (the pattern matches a representative real-world token shape)
/// and a negative case (an innocent string that resembles the shape
/// but should NOT match — confirms the patterns aren't over-greedy).
/// </remarks>
public sealed class TextRedactorTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Anthropic
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_AnthropicKey_MatchesAndReplaces()
    {
        // Real Anthropic keys are `sk-ant-api03-<93 chars>AA`; 40 chars
        // is the minimum length the redactor accepts, picked to fit
        // every documented variant without false-matching short
        // strings.
        string input = "ANTHROPIC_API_KEY=sk-ant-api03-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA after the key";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("sk-ant-api03"),
            "Raw Anthropic key prefix must not survive the redaction pass.");
        Assert.True(output.Contains(JsonRedactor.RedactedMarker),
            "Output must contain the [redacted] marker.");
    }

    [Fact]
    public void Redact_AnthropicShortLookalike_DoesNotMatch()
    {
        // `sk-ant-help` is too short to be a real key — must NOT match
        // (would otherwise eat innocuous prose that references the
        // SDK by name).
        string input = "See the `sk-ant-help` docs for setup instructions.";
        string output = TextRedactor.Redact(input);
        MessageAssert.Equal(input, output,
            "A short sk-ant-… lookalike must not match the Anthropic pattern.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  OpenAI (legacy + project + OpenRouter)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_OpenAiProjectKey_MatchesAndReplaces()
    {
        string input = "OPENAI_API_KEY=sk-proj-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("sk-proj-AAA"),
            "Raw OpenAI project key must not survive.");
    }

    [Fact]
    public void Redact_OpenRouterKey_MatchesAndReplaces()
    {
        string input = "OPENROUTER_KEY=sk-or-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string output = TextRedactor.Redact(input);
        Assert.DoesNotContain("sk-or-AAA", output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  GitHub
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_GitHubClassicPat_MatchesAndReplaces()
    {
        // Classic PATs are `ghp_` + 36 alphanumerics.
        string input = "GH_TOKEN=ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("ghp_AAA"),
            "Raw GitHub classic PAT must not survive.");
    }

    [Fact]
    public void Redact_GitHubFineGrainedPat_MatchesAndReplaces()
    {
        // Fine-grained PATs have the `github_pat_` prefix and a much
        // longer body with underscores.  Build a body of 82+ chars.
        string body = new string('A', 22) + "_" + new string('B', 59);
        string input = $"GH_PAT=github_pat_{body}";
        string output = TextRedactor.Redact(input);
        Assert.DoesNotContain("github_pat_AA", output);
    }

    [Fact]
    public void Redact_GitHubShortLookalike_DoesNotMatch()
    {
        // `ghp_short` is too short; must NOT match.
        string input = "Sample: ghp_short";
        string output = TextRedactor.Redact(input);
        Assert.Equal(input, output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  AWS
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_AwsAccessKeyId_MatchesAndReplaces()
    {
        string input = "export AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("AKIAIOSFODNN7EXAMPLE"),
            "AWS access key ID must not survive.");
    }

    [Fact]
    public void Redact_AkiaShortLookalike_DoesNotMatch()
    {
        // Exactly 15 chars after AKIA — wrong length, must not match.
        string input = "AKIA123456789012"; // 12 chars after AKIA
        string output = TextRedactor.Redact(input);
        Assert.Equal(input, output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Slack
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_SlackBotToken_MatchesAndReplaces()
    {
        string input = "SLACK_BOT_TOKEN=xoxb-AAAAAAAAAAAAAAAA-real-bot-token";
        string output = TextRedactor.Redact(input);
        Assert.DoesNotContain("xoxb-AAAAAAA", output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  JWT
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_JwtShape_MatchesAndReplaces()
    {
        string input = "auth_token: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature-part-here";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("eyJhbGciOiJIUzI1NiJ9"),
            "JWT header segment must not survive.");
    }

    [Fact]
    public void Redact_NonJwtBase64WithDots_DoesNotMatch()
    {
        // A base64 string with a single dot is NOT a JWT (JWT requires
        // exactly two dots + the eyJ-prefixed header).
        string input = "config_hash: dGhpcy1pcy1ub3QtYS1qd3Q.signature";
        string output = TextRedactor.Redact(input);
        Assert.Equal(input, output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  HTTP Bearer (case-insensitive)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_BearerHeaderValue_MatchesCaseInsensitive()
    {
        string input1 = "Authorization: Bearer abc123XYZ_def-456+ghi/789=jkl";
        string input2 = "authorization: bearer abc123XYZ_def-456+ghi/789=jkl";
        Assert.DoesNotContain("abc123XYZ", TextRedactor.Redact(input1));
        Assert.DoesNotContain("abc123XYZ", TextRedactor.Redact(input2));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Shell-style sensitive assignment (preserves key name)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_ShellExportSensitive_PreservesKeyName()
    {
        // The shell-style pass should preserve the variable name so
        // the user sees WHICH key was redacted, not just an anonymous
        // [redacted].  Use a key shape that does NOT match the
        // Anthropic/OpenAI/AWS prefixes so we exercise the shell
        // assignment regex's MatchEvaluator path specifically.
        string input = "export MY_CUSTOM_TOKEN=plaintextvaluewithoutprefix";
        string output = TextRedactor.Redact(input);
        Assert.True(output.Contains("MY_CUSTOM_TOKEN="),
            "Shell-style redaction must preserve the variable name.");
        Assert.True(output.Contains(JsonRedactor.RedactedMarker),
            "Shell-style redaction must substitute the value with [redacted].");
        Assert.False(output.Contains("plaintextvaluewithoutprefix"),
            "Raw value must not survive.");
    }

    [Fact]
    public void Redact_ShellNonSensitiveAssignment_DoesNotMatch()
    {
        // `PATH` doesn't contain TOKEN/SECRET/PASSWORD/KEY/etc. — must
        // pass through unchanged.
        string input = "export PATH=/usr/local/bin:/usr/bin";
        string output = TextRedactor.Redact(input);
        MessageAssert.Equal(input, output,
            "Non-sensitive shell assignments must not be touched.");
    }

    [Fact]
    public void Redact_BashAssignment_WithoutExport_AlsoMatches()
    {
        // The `export` keyword is optional in the regex — covers
        // plain `VAR=value` in bash / .env files.
        string input = "ANTHROPIC_API_KEY=plaintext";
        string output = TextRedactor.Redact(input);
        Assert.False(output.Contains("plaintext"),
            "Plain VAR=value assignments (no export prefix) must also redact.");
        Assert.Contains("ANTHROPIC_API_KEY=", output);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextRedactor.Redact(string.Empty));
        Assert.Equal(string.Empty, TextRedactor.Redact(null!));
    }

    [Fact]
    public void Redact_NoSecretShapes_PreservesContent()
    {
        // An innocuous markdown paragraph with no token-shaped content
        // must round-trip byte-for-byte.
        string input = "# Agent: code-reviewer\n\nReviews TypeScript code for bugs.";
        string output = TextRedactor.Redact(input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Redact_IsIdempotent()
    {
        // Running the redactor twice on the same input produces the
        // same output as running it once — the [redacted] marker
        // contains no characters that match any pattern.
        string input = "export GH_TOKEN=ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string once = TextRedactor.Redact(input);
        string twice = TextRedactor.Redact(once);
        MessageAssert.Equal(once, twice,
            "Redact must be idempotent: a second pass must produce identical output.");
    }

    [Fact]
    public void Redact_MultiplePatternsInOneFile_AllMatch()
    {
        // A realistic hook script containing multiple secret shapes.
        // The redactor must catch all of them in a single pass.
        string input = """
                       #!/bin/bash
                       export ANTHROPIC_API_KEY=sk-ant-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA
                       export AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
                       curl -H "Authorization: Bearer xyzabc123def456ghi789jklmno"
                       """;
        string output = TextRedactor.Redact(input);

        Assert.False(output.Contains("sk-ant-AAA"), "Anthropic key not redacted");
        Assert.False(output.Contains("AKIAIOSFODNN7"), "AWS access key not redacted");
        Assert.False(output.Contains("xyzabc123def"), "Bearer token not redacted");
        // Bash structure preserved
        Assert.Contains("#!/bin/bash", output);
        Assert.Contains("export ANTHROPIC_API_KEY", output);
    }
}