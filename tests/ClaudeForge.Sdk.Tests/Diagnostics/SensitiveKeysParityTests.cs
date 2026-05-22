using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Diagnostics;

/// <summary>
/// pins parity between the two parallel "is this key
/// sensitive?" classifiers in the codebase:
/// <list type="bullet">
///   <item><see cref="SensitiveKeys.IsSensitive"/> in
///         <c>ClaudeForge.Sdk.Diagnostics</c> — used by the audit-log
///         live-write path and the save-diff log.</item>
///   <item><see cref="JsonRedactor.IsSensitiveKey"/> in
///         <c>ClaudeForge.Core.Backup</c> — used by
///         <see cref="BackupMode.Sanitized"/> archives.</item>
/// </list>
/// The duplication is intentional — <c>ClaudeForge.Core</c> can't reference
/// <c>ClaudeForge.Sdk</c> per the layering contract, so the redactor inlines
/// its own copy of the classifier.  This test is the guard against drift:
/// any new sensitive-token added to one side must be added to the other,
/// otherwise the three redaction surfaces (audit log, save diff, sanitized
/// backup) start disagreeing on what counts as a secret — and the one
/// that's lagging starts leaking secrets the others scrub.
/// </summary>
[TestClass]
public sealed class SensitiveKeysParityTests
{
    /// <summary>
    /// Representative sample covering every classifier branch — segment-exact
    /// entries, every substring token, common edge cases, and innocuous keys.
    /// Both classifiers must agree on every entry.
    /// </summary>
    private static readonly string[] SampleKeys =
    [
        // Segment-exact matches
        "env",
        "ENV",
        "Env",
        "headers",
        "HEADERS",
        "credentials",
        "auth",
        "authorization",

        // Substring matches — token / secret / password / apikey / api_key /
        // api-key / bearer.
        "token",
        "Token",
        "githubAccessToken",
        "refreshToken",
        "secret",
        "clientSecret",
        "password",
        "user_password",
        "apiKey",
        "ANTHROPIC_API_KEY",
        "api-key",
        "bearer",
        "BearerToken",

        // Innocuous keys — both must agree these are NOT sensitive.
        "theme",
        "model",
        "permissions",
        "hooks",
        "mcpServers",
        "uniqueKey",
        "locKey",
        "availableModels",
        "verbose",
        "", // empty string — both should say not sensitive

        // new substring tokens (private,
        // accesskey, access_key, access-key) AND the existing api-key
        // coverage of x-api-key.  Both classifiers must agree on the
        // expanded set.
        "privateKey",
        "rsaPrivate",
        "rsa_private_key",
        "awsAccessKey",
        "aws_access_key_id",
        "aws-access-key-id",
        "AWS_ACCESS_KEY_ID",
        "x-api-key",
        "X-API-Key",

        // dotted-path inputs.  Pre-fix
        // SensitiveKeys.IsSensitive split on '.' and caught dotted
        // segments; JsonRedactor.IsSensitiveKey did not.  After H4
        // both surfaces split-and-segment-match identically.  These
        // entries lock the contract — drop or rename one without
        // updating the other and this test fails immediately.
        "env.ANTHROPIC_API_KEY",
        "env.NOT_A_SECRET", // segment "env" catches → both true
        "mcpServers.gh.headers.Authorization", // segment "headers" + substring "auth*" → true
        "credentials.refresh_token", // segment "credentials" + substring "token" → true
        "settings.theme", // no sensitive segment, no substring → both false
        "permissions.disableBypassPermissionsMode",
    ];

    [TestMethod]
    public void Classifiers_AgreeOnAllSampleKeys()
    {
        List<string> disagreements = new();
        foreach (string key in SampleKeys)
        {
            bool sdkSays = SensitiveKeys.IsSensitive(key);
            bool coreSays = JsonRedactor.IsSensitiveKey(key);
            if (sdkSays != coreSays)
            {
                disagreements.Add(
                    $"'{key}': Sdk={sdkSays}, Core={coreSays}");
            }
        }

        Assert.AreEqual(0, disagreements.Count,
            "JsonRedactor.IsSensitiveKey and SensitiveKeys.IsSensitive must " +
            "agree for every sampled key — drift means one of the three " +
            "redaction surfaces (audit log, save diff, sanitized backup) is " +
            "leaking secrets the others scrub.  Disagreements:\n  " +
            string.Join("\n  ", disagreements));
    }

    [TestMethod]
    public void RedactedMarker_StringIsIdentical()
    {
        // Both surfaces use "[redacted]" as the marker — the literal must
        // be identical so log greps / report templates / support workflows
        // don't have to handle two different placeholders.
        Assert.AreEqual(SensitiveKeys.RedactedMarker, JsonRedactor.RedactedMarker,
            "The two classifiers must use the same redaction marker string.");
        Assert.AreEqual("[redacted]", JsonRedactor.RedactedMarker);
    }
}