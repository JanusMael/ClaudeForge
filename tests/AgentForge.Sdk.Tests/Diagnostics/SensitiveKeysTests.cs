using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Diagnostics;

/// <summary>
/// Locks the contract of <see cref="SensitiveKeys.IsSensitive"/>: values for
/// keys matching this predicate are never written verbatim to logs / shared
/// reports — they are replaced with <see cref="SensitiveKeys.RedactedMarker"/>
/// before being persisted.
/// </summary>
public sealed class SensitiveKeysTests
{
    [Theory]
    [InlineData("env")] // exact: env vars block (holds API keys)
    [InlineData("ENV")] // case-insensitive
    [InlineData("headers")] // exact: MCP HTTP headers (Authorization)
    [InlineData("credentials")] // exact
    [InlineData("apiKey")] // substring: apikey
    [InlineData("ANTHROPIC_API_KEY")] // substring: api_key
    [InlineData("githubAccessToken")] // substring: token
    [InlineData("refreshToken")] // substring: token
    [InlineData("clientSecret")] // substring: secret
    [InlineData("password")] // substring: password
    [InlineData("user_password")] // substring: password
    public void IsSensitive_ReturnsTrue_ForSecretBearingKeys(string key)
    {
        Assert.True(SensitiveKeys.IsSensitive(key),
            $"'{key}' should be treated as sensitive");
    }

    [Theory]
    [InlineData("model")]
    [InlineData("permissions")]
    [InlineData("hooks")]
    [InlineData("mcpServers")]
    [InlineData("includeCoworkScheduledTasks")]
    [InlineData("verbose")]
    [InlineData("availableModels")]
    [InlineData("")]
    public void IsSensitive_ReturnsFalse_ForOrdinaryKeys(string key)
    {
        Assert.False(SensitiveKeys.IsSensitive(key),
            $"'{key}' should not be treated as sensitive");
    }

    /// <summary>
    /// segment-match cases.  Pre-fix, IsSensitive only matched
    /// when the FULL dotted path equalled an entry in the exact-set; nested
    /// paths under env / headers / credentials leaked their leaf values to
    /// the rolling log.  These tests pin the "any segment matches" contract.
    /// </summary>
    [Theory]
    [InlineData("env.ANTHROPIC_API_KEY")]
    [InlineData("env.OPAQUE_TOKEN_FOR_THIRD_PARTY")]
    [InlineData("env.MAX_OUTPUT_TOKENS")] // false-positive but fail-safe
    [InlineData("mcpServers.gh.headers.Authorization")]
    [InlineData("mcpServers.gh.headers.X-API-Key")]
    [InlineData("mcpServers.gh.headers.Cookie")]
    [InlineData("mcpServers.gh.headers.x-api-key")] // hyphen variant
    [InlineData("permissions.allow.0.headers.Authorization")] // pathological-but-possible nested
    [InlineData("credentials.refresh_token")]
    [InlineData("credentials.access_token")]
    [InlineData("auth.bearer")]
    [InlineData("settings.authorization")] // direct authorization segment
    public void IsSensitive_ReturnsTrue_ForNestedPathsUnderSecretSegments(string key)
    {
        Assert.True(SensitiveKeys.IsSensitive(key),
            $"'{key}' has a path segment that should trigger redaction.");
    }

    [Theory]
    [InlineData("permissions.allow")]
    [InlineData("hooks.PreToolUse")]
    [InlineData("mcpServers.gh.command")]
    [InlineData("mcpServers.gh.args")]
    [InlineData("model")]
    [InlineData("modelOverrides.opus")]
    [InlineData("uniqueKey")] // contains "key" but NOT "apikey/api_key/api-key"
    [InlineData("locKey")] // same
    public void IsSensitive_ReturnsFalse_ForBenignNestedPaths(string key)
    {
        Assert.False(SensitiveKeys.IsSensitive(key),
            $"'{key}' has no secret-bearing segment or substring; must not redact.");
    }

    [Fact]
    public void RedactedMarker_IsExpectedString()
    {
        // Lock the public marker text — anything that travels into bug
        // reports relies on this exact spelling.
        // MSTEST0032: const-vs-literal folds to always-true; locking the marker
        // is precisely what this test exists to do.
#pragma warning disable MSTEST0032
        Assert.Equal("[redacted]", SensitiveKeys.RedactedMarker);
#pragma warning restore MSTEST0032
    }
}