using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Recursive JSON redactor used by <see cref="BackupMode.Sanitized"/> to
/// scrub secret-bearing values out of config JSON before it lands in a
/// backup archive intended for sharing.
/// </summary>
/// <remarks>
/// <para>
/// Walks every key in the input <see cref="JsonObject"/> tree (objects
/// AND nested objects inside arrays).  For each key, calls
/// <see cref="IsSensitiveKey"/> on the KEY (not the dotted path) — the
/// classifier mirrors <c>ClaudeForge.Sdk.Diagnostics.SensitiveKeys.IsSensitive</c>
/// so the three redaction surfaces (audit-log live-write, save-diff log,
/// sanitized backup) agree on what "secret" means.  The duplication is
/// intentional: <c>ClaudeForge.Core</c> cannot reference
/// <c>ClaudeForge.Sdk</c> per the layering contract.  Parity is enforced
/// by <c>JsonRedactor_AndSensitiveKeys_AgreeOnClassification</c> in the
/// Sdk test project, which calls both sides against a shared sample set
/// and asserts identical answers.
/// </para>
/// <para>
/// Sensitive values are replaced with the literal string
/// <c>"[redacted]"</c> regardless of their original type — int, bool,
/// null, object, array all become the same placeholder.  This is by
/// design: the resulting JSON is not meant to be machine-readable as
/// config; it's a human-inspectable shape of the user's setup with
/// the secret-bearing leaves stripped.
/// </para>
/// <para>
/// Non-JSON files (markdown agents, hook scripts, etc.) are NOT touched
/// by this helper — the caller decides which files to route through it
/// based on extension.  This keeps the redactor's responsibility narrow:
/// well-formed JSON only.
/// </para>
/// </remarks>
public static class JsonRedactor
{
    /// <summary>Placeholder substituted for any sensitive value.</summary>
    public const string RedactedMarker = "[redacted]";

    /// <summary>
    /// Full-key matches (case-insensitive segment compare).  Mirror of
    /// <c>SensitiveKeys._segmentExact</c> in the Sdk.
    /// </summary>
    private static readonly HashSet<string> SegmentExact =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "env",
            "headers",
            "credentials",
            "auth",
            "authorization",
        };

    /// <summary>
    /// Substring catchers (case-insensitive).  A key containing any of
    /// these gets flagged.  Mirror of <c>SensitiveKeys._substringTokens</c>
    /// in the Sdk.
    /// </summary>
    /// <remarks>
    /// added <c>private</c> (covers
    /// <c>privateKey</c>, <c>rsa_private</c>, etc.), and three
    /// <c>access_key</c> hyphen/underscore/concatenated variants for
    /// AWS-style identifiers.  Lockstep with
    /// <c>ClaudeForge.Sdk.Diagnostics.SensitiveKeys</c> per invariant
    /// I18.
    /// </remarks>
    private static readonly string[] SubstringTokens =
    [
        "token", "secret", "password",
        "apikey", "api_key", "api-key",
        "bearer",
        "private",
        "accesskey", "access_key", "access-key",
    ];

    /// <summary>
    /// Classify a single key as sensitive.  Mirror of
    /// <c>ClaudeForge.Sdk.Diagnostics.SensitiveKeys.IsSensitive</c>;
    /// parity enforced by the Sdk-side cross test
    /// <c>SensitiveKeysParityTests</c>.
    /// </summary>
    /// <remarks>
    /// now path-segment-aware.  Splits
    /// <paramref name="key"/> on <c>'.'</c> and checks each segment
    /// against the segment-exact set BEFORE the substring pass.  This
    /// mirrors the Sdk classifier so callers passing dotted paths
    /// (e.g. <c>"env.ANTHROPIC_API_KEY"</c>) get the same answer from
    /// both surfaces.  The recursive walker in <see cref="Redact"/>
    /// already passes single segments, so the per-walker behaviour is
    /// unchanged; this fix closes the contract drift for direct
    /// callers of <see cref="IsSensitiveKey"/>.
    /// </remarks>
    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        // Segment pass: split on '.' and check each segment against
        // the exact set.  Catches keys nested under known
        // secret-bearing sections regardless of depth (env.X,
        // mcpServers.gh.headers.Authorization, credentials.refresh_token).
        // Order matters: do this BEFORE the substring pass because
        // segment-exact is a stricter / more specific signal.
        foreach (string segment in key.Split('.'))
        {
            if (SegmentExact.Contains(segment))
            {
                return true;
            }
        }

        foreach (string t in SubstringTokens)
        {
            if (key.Contains(t, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parse <paramref name="json"/>, recursively redact sensitive
    /// values, and serialize back to a pretty-printed UTF-8 string.
    /// Throws <see cref="JsonException"/> if the input is malformed —
    /// callers should guard with try/catch and fall back to emitting
    /// the original file bytes (a malformed JSON file in
    /// <c>~/.claude/</c> isn't config we can sanitize, but it also
    /// isn't likely to leak structured secrets).
    /// </summary>
    /// <remarks>
    /// <para>
    /// **Comment + trailing-comma strip (documented, L4 2026-05-14).**
    /// The parser tolerates JSONC-style line / block comments and
    /// trailing commas (some users hand-edit <c>settings.json</c> with
    /// either), but System.Text.Json does NOT preserve them across a
    /// parse → serialise round-trip — `JsonNode.WriteTo` emits strict
    /// JSON.  Net: a sanitized archive's <c>.json</c> files are NOT
    /// byte-identical to their source — comments and trailing commas
    /// are silently stripped.  This is correct for the mode's
    /// contract: Sanitized backups are "secret-free", not
    /// "byte-identical".  Switching to a comment-preserving JSON
    /// parser would require adding Newtonsoft.Json or writing a
    /// custom scanner; not worth the dependency surface for a
    /// sharing-only artefact.  Documented in CLAUDE.md "Backup modes"
    /// and locked by <c>JsonRedactorTests.Redact_StripsLineComments_*</c>.
    /// </para>
    /// </remarks>
    public static string Redact(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json ?? string.Empty;
        }

        // CommentHandling.Skip lets us parse JSONC-style input but
        // strips comments at parse time (they don't survive the
        // re-serialise).  AllowTrailingCommas is the same story —
        // tolerant on read, gone on write.  Both behaviours are
        // intentional per the L4 contract above.
        JsonNode? node = JsonNode.Parse(json,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        if (node is null)
        {
            return json;
        }

        RedactInPlace(node);
        // WriteIndented = true so the resulting archive is human-readable.
        // No JsonSerializer<T> overload involved — JsonNode.WriteTo + Utf8JsonWriter
        // is trim-safe and predictable.
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Walk <paramref name="node"/> in place.  Sensitive keys' values
    /// are replaced with a <c>"[redacted]"</c> string literal.  Object
    /// and array structure is preserved everywhere else.
    /// </summary>
    private static void RedactInPlace(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot the key list because we mutate the dictionary
                // inside the loop (`obj[key] = …` for sensitive keys).
                foreach (string key in obj.Select(kv => kv.Key).ToList())
                {
                    JsonNode? child = obj[key];
                    if (IsSensitiveKey(key))
                    {
                        // Replace regardless of original type — int, bool,
                        // null, object, array all become the redacted
                        // marker.  The literal matches
                        // ClaudeForge.Sdk.Diagnostics.SensitiveKeys.RedactedMarker
                        // so all three redaction surfaces (audit log, save
                        // diff, sanitized backup) emit an identical string —
                        // parity enforced by the Sdk-side cross test.
                        obj[key] = JsonValue.Create(RedactedMarker);
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        RedactInPlace(child);
                    }
                }

                break;

            case JsonArray arr:
                foreach (JsonNode? item in arr)
                {
                    if (item is JsonObject or JsonArray)
                    {
                        RedactInPlace(item);
                    }
                }

                break;
        }
    }
}