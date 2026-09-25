using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// pins <see cref="JsonRedactor"/>'s redaction contract.  The
/// redactor backs <see cref="BackupMode.Sanitized"/>: every *.json file in
/// the archive is parsed and any value whose key matches the sensitive-keys
/// classifier is replaced with the literal string <c>"[redacted]"</c>.
/// </summary>
/// <remarks>
/// Parity with the live Sdk classifier (<c>AgentForge.Sdk.Diagnostics.SensitiveKeys</c>)
/// is enforced separately by the cross-project test in
/// <c>tests/AgentForge.Sdk.Tests/Diagnostics/SensitiveKeysParityTests.cs</c>.
/// This file focuses on the redactor's behaviour against representative
/// config shapes.
/// </remarks>
public sealed class JsonRedactorTests
{
    [Fact]
    public void IsSensitiveKey_FlagsSegmentExactKeys()
    {
        Assert.True(JsonRedactor.IsSensitiveKey("env"));
        Assert.True(JsonRedactor.IsSensitiveKey("headers"));
        Assert.True(JsonRedactor.IsSensitiveKey("credentials"));
        Assert.True(JsonRedactor.IsSensitiveKey("auth"));
        Assert.True(JsonRedactor.IsSensitiveKey("authorization"));
        // Case-insensitive
        Assert.True(JsonRedactor.IsSensitiveKey("ENV"));
        Assert.True(JsonRedactor.IsSensitiveKey("Headers"));
    }

    [Fact]
    public void IsSensitiveKey_FlagsSubstringTokens()
    {
        // Substring matches — designed to catch schema additions named e.g.
        // githubAccessToken, clientSecret, password without an allowlist update.
        Assert.True(JsonRedactor.IsSensitiveKey("githubAccessToken"));
        Assert.True(JsonRedactor.IsSensitiveKey("clientSecret"));
        Assert.True(JsonRedactor.IsSensitiveKey("password"));
        Assert.True(JsonRedactor.IsSensitiveKey("apiKey"));
        Assert.True(JsonRedactor.IsSensitiveKey("api_key"));
        Assert.True(JsonRedactor.IsSensitiveKey("api-key"));
        Assert.True(JsonRedactor.IsSensitiveKey("BearerToken"));
    }

    [Fact]
    public void IsSensitiveKey_DoesNotOverMatchInnocentKeys()
    {
        // Conservative — won't mask innocent fields that merely contain "key".
        Assert.False(JsonRedactor.IsSensitiveKey("uniqueKey"));
        Assert.False(JsonRedactor.IsSensitiveKey("locKey"));
        Assert.False(JsonRedactor.IsSensitiveKey("theme"));
        Assert.False(JsonRedactor.IsSensitiveKey("model"));
        Assert.False(JsonRedactor.IsSensitiveKey(""));
    }

    [Fact]
    public void Redact_ScrubsEnvVarMapAtAnyDepth()
    {
        // Matches the typical Claude Code settings.json shape with an
        // `env` map containing API-key variables.
        const string json = """
                            {
                              "theme": "dark",
                              "env": {
                                "ANTHROPIC_API_KEY": "sk-ant-abc123",
                                "OPENAI_API_KEY":    "sk-openai-xyz",
                                "NOT_A_SECRET":      "fine"
                              }
                            }
                            """;

        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();

        // The whole `env` subtree is replaced with the marker — segment-exact
        // matches don't recurse, the top-level value is the placeholder.
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)parsed["env"]);
        // Non-sensitive sibling is preserved verbatim.
        Assert.Equal("dark", (string?)parsed["theme"]);
    }

    [Fact]
    public void Redact_ScrubsHeadersUnderNestedMcpServer()
    {
        // Matches the structure that motivated the segment-match upgrade:
        // mcpServers.<name>.headers.<header> values were leaking before.
        const string json = """
                            {
                              "mcpServers": {
                                "github": {
                                  "url": "https://api.github.com/mcp",
                                  "headers": {
                                    "Authorization": "Bearer ghp_abc123",
                                    "X-Api-Key":     "secret-value"
                                  }
                                }
                              }
                            }
                            """;

        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();

        JsonNode? headers = parsed["mcpServers"]!["github"]!["headers"];
        // The whole `headers` subtree is the redacted marker — the segment
        // classifier triggers on `headers` and we replace the value wholesale.
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)headers);
        // Non-sensitive sibling under the same mcp server survives.
        Assert.Equal("https://api.github.com/mcp",
            (string?)parsed["mcpServers"]!["github"]!["url"]);
    }

    [Fact]
    public void Redact_ScrubsSubstringMatchedScalarLeaves()
    {
        // Schema additions named e.g. `githubAccessToken` aren't covered by
        // segment-exact but are caught by the substring pass.
        const string json = """
                            {
                              "githubAccessToken": "ghp_xxx",
                              "clientSecret":      "shh",
                              "regularField":      "ok"
                            }
                            """;

        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();

        Assert.Equal(JsonRedactor.RedactedMarker, (string?)parsed["githubAccessToken"]);
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)parsed["clientSecret"]);
        Assert.Equal("ok", (string?)parsed["regularField"]);
    }

    [Fact]
    public void Redact_PreservesArrayStructureButRedactsObjectsInside()
    {
        // Arrays of objects must be walked so nested headers / env / etc. land
        // in the redaction net.  The array length stays the same.
        const string json = """
                            {
                              "servers": [
                                { "name": "a", "auth": "token-a" },
                                { "name": "b", "auth": "token-b" }
                              ]
                            }
                            """;

        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();
        JsonArray arr = parsed["servers"]!.AsArray();

        Assert.Equal(2, arr.Count);
        Assert.Equal("a", (string?)arr[0]!["name"]);
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)arr[0]!["auth"]);
        Assert.Equal("b", (string?)arr[1]!["name"]);
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)arr[1]!["auth"]);
    }

    [Fact]
    public void Redact_AcceptsCommentsAndTrailingCommas()
    {
        // settings.json files occasionally contain JSONC-style comments; the
        // redactor should tolerate them rather than blowing up the whole
        // backup.  Trailing commas similarly.
        const string json = """
                            {
                              // comment line
                              "env": { "API_KEY": "x", },
                              "theme": "dark",
                            }
                            """;

        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();
        Assert.Equal(JsonRedactor.RedactedMarker, (string?)parsed["env"]);
        Assert.Equal("dark", (string?)parsed["theme"]);
    }

    [Fact]
    public void Redact_NullOrEmptyInput_ReturnsInputUnchanged()
    {
        Assert.Equal(string.Empty, JsonRedactor.Redact(string.Empty));
        Assert.Equal(string.Empty, JsonRedactor.Redact(null!));
    }

    [Fact]
    public void Redact_MalformedJson_ThrowsJsonException()
    {
        // The redactor surfaces parse errors to the caller — BackupEngine
        // wraps the call in a try/catch and substitutes a placeholder so a
        // malformed file in ~/.claude/ doesn't take down the whole backup,
        // and crucially doesn't leak the original (possibly secret-bearing)
        // bytes.  Accept any JsonException subclass (JsonReaderException
        // is derived) so the contract isn't pinned to an exact runtime
        // type that may change between BCL releases.
        bool thrown = false;
        try
        {
            JsonRedactor.Redact("{not json");
        }
        catch (JsonException)
        {
            thrown = true;
        }

        Assert.True(thrown, "Redact() must surface a JsonException on malformed input.");
    }

    [Fact]
    public void Redact_NonObjectRoot_LeavesItAlone()
    {
        // Root-level arrays and bare scalars are valid JSON but have no
        // object keys to walk — the redactor returns them parsed-and-
        // re-serialised, unchanged in content.
        string arrayResult = JsonRedactor.Redact("[1, 2, 3]");
        JsonArray arr = JsonNode.Parse(arrayResult)!.AsArray();
        Assert.Equal(3, arr.Count);
    }

    // ─────────────────────────────────────────────────────────────────
    //  L4 — Documented JSONC strip behaviour (comments + trailing commas)
    //
    //  These tests pin a DOCUMENTED behaviour, not a bug: sanitized
    //  archives are not byte-identical to their source — comments and
    //  trailing commas are stripped by the System.Text.Json parser as
    //  it produces a strict-JSON output.  Future readers find this
    //  contract via the test names so they don't file a "the redactor
    //  is eating my comments" issue.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_PreservesNonComment_NonTrailingCommaContent()
    {
        // Strict-JSON input (no comments, no trailing commas) round-
        // trips through the redactor without semantic change — only
        // sensitive values are touched.
        const string json = """
                            {
                              "theme": "dark",
                              "permissions": { "allow": ["Bash"] }
                            }
                            """;
        string redacted = JsonRedactor.Redact(json);
        JsonObject parsed = JsonNode.Parse(redacted)!.AsObject();

        Assert.Equal("dark", (string?)parsed["theme"]);
        JsonArray allow = parsed["permissions"]!["allow"]!.AsArray();
        Assert.Single(allow);
        Assert.Equal("Bash", (string?)allow[0]);
    }

    [Fact]
    public void Redact_StripsLineComments_DocumentedBehaviour()
    {
        // Input WITH a line comment — by design, the comment does not
        // survive the redaction pass.  Locked by L4 contract.
        const string json = """
                            {
                              // user-edited comment
                              "theme": "dark"
                            }
                            """;
        string redacted = JsonRedactor.Redact(json);

        Assert.False(redacted.Contains("// user-edited", StringComparison.Ordinal),
            "Line comment must be stripped by Redact (documented L4 behaviour).");
        Assert.True(redacted.Contains("\"theme\""),
            "Non-comment content (theme key) must survive.");
    }

    [Fact]
    public void Redact_StripsTrailingCommas_DocumentedBehaviour()
    {
        // Trailing comma tolerated on parse, dropped on serialise.
        const string json = """{ "theme": "dark", }""";
        string redacted = JsonRedactor.Redact(json);

        // Parse the output as STRICT JSON (no AllowTrailingCommas) to
        // confirm the trailing comma was dropped — would throw if not.
        JsonNode? parsed = JsonNode.Parse(redacted);
        MessageAssert.NotNull(parsed,
            "Redact output must be strict JSON (no trailing comma).");
    }
}