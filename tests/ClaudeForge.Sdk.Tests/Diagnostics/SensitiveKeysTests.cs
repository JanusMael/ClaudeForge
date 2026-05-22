using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Diagnostics;

/// <summary>
/// Locks the contract of <see cref="SensitiveKeys.IsSensitive"/>: values for
/// keys matching this predicate are never written verbatim to logs / shared
/// reports — they are replaced with <see cref="SensitiveKeys.RedactedMarker"/>
/// before being persisted.
/// </summary>
[TestClass]
public sealed class SensitiveKeysTests
{
    [TestMethod]
    [DataRow("env")] // exact: env vars block (holds API keys)
    [DataRow("ENV")] // case-insensitive
    [DataRow("headers")] // exact: MCP HTTP headers (Authorization)
    [DataRow("credentials")] // exact
    [DataRow("apiKey")] // substring: apikey
    [DataRow("ANTHROPIC_API_KEY")] // substring: api_key
    [DataRow("githubAccessToken")] // substring: token
    [DataRow("refreshToken")] // substring: token
    [DataRow("clientSecret")] // substring: secret
    [DataRow("password")] // substring: password
    [DataRow("user_password")] // substring: password
    public void IsSensitive_ReturnsTrue_ForSecretBearingKeys(string key)
    {
        Assert.IsTrue(SensitiveKeys.IsSensitive(key),
            $"'{key}' should be treated as sensitive");
    }

    [TestMethod]
    [DataRow("model")]
    [DataRow("permissions")]
    [DataRow("hooks")]
    [DataRow("mcpServers")]
    [DataRow("includeCoworkScheduledTasks")]
    [DataRow("verbose")]
    [DataRow("availableModels")]
    [DataRow("")]
    public void IsSensitive_ReturnsFalse_ForOrdinaryKeys(string key)
    {
        Assert.IsFalse(SensitiveKeys.IsSensitive(key),
            $"'{key}' should not be treated as sensitive");
    }

    /// <summary>
    /// segment-match cases.  Pre-fix, IsSensitive only matched
    /// when the FULL dotted path equalled an entry in the exact-set; nested
    /// paths under env / headers / credentials leaked their leaf values to
    /// the rolling log.  These tests pin the "any segment matches" contract.
    /// </summary>
    [TestMethod]
    [DataRow("env.ANTHROPIC_API_KEY")]
    [DataRow("env.OPAQUE_TOKEN_FOR_THIRD_PARTY")]
    [DataRow("env.MAX_OUTPUT_TOKENS")] // false-positive but fail-safe
    [DataRow("mcpServers.gh.headers.Authorization")]
    [DataRow("mcpServers.gh.headers.X-API-Key")]
    [DataRow("mcpServers.gh.headers.Cookie")]
    [DataRow("mcpServers.gh.headers.x-api-key")] // hyphen variant
    [DataRow("permissions.allow.0.headers.Authorization")] // pathological-but-possible nested
    [DataRow("credentials.refresh_token")]
    [DataRow("credentials.access_token")]
    [DataRow("auth.bearer")]
    [DataRow("settings.authorization")] // direct authorization segment
    public void IsSensitive_ReturnsTrue_ForNestedPathsUnderSecretSegments(string key)
    {
        Assert.IsTrue(SensitiveKeys.IsSensitive(key),
            $"'{key}' has a path segment that should trigger redaction.");
    }

    [TestMethod]
    [DataRow("permissions.allow")]
    [DataRow("hooks.PreToolUse")]
    [DataRow("mcpServers.gh.command")]
    [DataRow("mcpServers.gh.args")]
    [DataRow("model")]
    [DataRow("modelOverrides.opus")]
    [DataRow("uniqueKey")] // contains "key" but NOT "apikey/api_key/api-key"
    [DataRow("locKey")] // same
    public void IsSensitive_ReturnsFalse_ForBenignNestedPaths(string key)
    {
        Assert.IsFalse(SensitiveKeys.IsSensitive(key),
            $"'{key}' has no secret-bearing segment or substring; must not redact.");
    }

    [TestMethod]
    public void RedactedMarker_IsExpectedString()
    {
        // Lock the public marker text — anything that travels into bug
        // reports relies on this exact spelling.
        Assert.AreEqual("[redacted]", SensitiveKeys.RedactedMarker);
    }
}