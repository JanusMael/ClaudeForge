using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Schema;

/// <summary>
/// Claude Code's overlay: its hand-curated additions reach the merged schema, and it strips none
/// of upstream's structural constraints.
/// </summary>
/// <remarks>
/// Hand-curated bits live in <c>claude-code-settings.overlay.json</c>, merged onto the verbatim
/// upstream schema at load time via RFC 7396 JSON Merge Patch. The merge itself is tested in
/// <c>AgentForge.Core.Tests</c>, under the same class name; these tests check Claude's files.
/// </remarks>
public sealed class SchemaRegistryOverlayTests
{
    private const string ClaudeCodeSchemaFile = "claude-code-settings.json";

    [Fact]
    public void TryReadBundledBytesMerged_AppliesClaudeCodeOverlay()
    {
        // Reading the production bundled schema through the merged loader
        // must surface the overlay's `model.examples` + `default` even
        // though the base file no longer carries them.
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named(ClaudeCodeSchemaFile));
        Assert.NotNull(bytes);

        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
        JsonObject? model = node?["properties"]?["model"] as JsonObject;
        MessageAssert.NotNull(model, "model property must survive the merge.");

        MessageAssert.Equal("sonnet", model!["default"]?.GetValue<string>(),
            "Overlay's `default` must surface on the merged result.");

        JsonArray? examples = model["examples"] as JsonArray;
        MessageAssert.NotNull(examples, "Overlay's `examples` must surface.");
        Assert.True(examples!.Count >= 4, $"At least 4 examples expected; got {examples.Count}.");
        string?[] values = examples.Select(e => e?.GetValue<string>()).ToArray();
        Assert.Contains("sonnet", values);
        Assert.Contains("opus", values);
        Assert.Contains("haiku", values);
    }

    [Fact]
    public void TryReadBundledBytesMerged_PreservesModelPropertyPosition_InMergedResult()
    {
        // End-to-end variant of the order-preservation regression: the model
        // property must keep its mid-list position in `properties` after the
        // overlay merge.  Pre-fix, the overlay's `model` patch moved the key
        // to the end of the properties OrderedDictionary, and the editor's
        // property list rendered `model` as the last entry.
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named(ClaudeCodeSchemaFile));
        Assert.NotNull(bytes);
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
        JsonObject? properties = node?["properties"] as JsonObject;
        Assert.NotNull(properties);

        string[] keys = properties!.Select(kvp => kvp.Key).ToArray();
        int modelIdx = Array.IndexOf(keys, "model");
        int lastIdx = keys.Length - 1;
        Assert.True(modelIdx >= 0, "model property must exist in merged schema.");
        MessageAssert.NotEqual(lastIdx, modelIdx,
            $"model must NOT be the last property after the overlay merge "
            + $"(was at index {modelIdx} of {keys.Length}; last is {lastIdx}). "
            + $"If this regresses, ApplyMergePatch is re-adding instead of "
            + $"assigning in place.");
    }

    // ── Constraint-preservation guards (post-review MEDIUM #1, 2026-05-19) ──
    //
    // RFC 7396 makes overlay keys unconditionally win — a null in the overlay
    // deletes the key from the merged result.  These tests lock that the
    // overlay HASN'T accidentally stripped a security-relevant structural
    // constraint from the upstream schema.  If a future contributor adds an
    // overlay entry that nulls one of these out, the guard fires and points
    // them at the must-not-touch comment in the overlay file itself.

    [Fact]
    public void Overlay_PreservesPermissionRulePattern()
    {
        // $defs.permissionRule.pattern is a regex locking valid permission
        // rule names (e.g. "Bash:*", "Edit", "Read(/etc/passwd)").  If a
        // future overlay strips this pattern, an attacker-crafted profile
        // import could land arbitrary action names that the schema validator
        // no longer rejects.  Test guards against accidental removal.
        JsonNode? merged = LoadMerged();
        string? pattern = merged?["$defs"]?["permissionRule"]?["pattern"]?.GetValue<string>();

        Assert.False(string.IsNullOrEmpty(pattern),
            "$defs.permissionRule.pattern must survive the overlay merge. "
            + "If this fails, an overlay entry has stripped the regex that locks "
            + "permission-rule action names.  See claude-code-settings.overlay.json's "
            + "$comment-must-not-touch block.");
        MessageAssert.Contains("Bash", pattern!,
            "Pattern must enumerate the known action names — Bash is the canary.");
    }

    [Fact]
    public void Overlay_PreservesHookCommandTypeConst()
    {
        // $defs.hookCommand.anyOf[0].properties.type.const = "command" is the
        // discriminator that disambiguates the command hook variant from
        // agent / http variants.  Stripping the const would let the schema
        // accept arbitrary `type` values, breaking the opaque-preservation
        // contract that protects unknown hook types from silent downcast.
        JsonNode? merged = LoadMerged();
        JsonNode? hookCommand = merged?["$defs"]?["hookCommand"];
        MessageAssert.NotNull(hookCommand, "$defs.hookCommand must exist in the merged schema.");

        JsonArray? anyOf = hookCommand!["anyOf"] as JsonArray;
        MessageAssert.NotNull(anyOf, "hookCommand must use anyOf to discriminate variants.");
        Assert.True(anyOf!.Count > 0, "hookCommand.anyOf must have at least one variant.");

        string? typeConst = anyOf[0]?["properties"]?["type"]?["const"]?.GetValue<string>();
        MessageAssert.Equal("command", typeConst,
            "hookCommand.anyOf[0].properties.type.const must remain \"command\" — "
            + "the variant discriminator MUST NOT be stripped by the overlay.");
    }

    [Fact]
    public void Overlay_PreservesAutoUpdatesChannelEnum()
    {
        // properties.autoUpdatesChannel.enum = ["stable", "latest"] locks the
        // update channel selector.  Stripping the enum would let a malformed
        // profile import land an arbitrary string here, which the auto-updater
        // would then dereference.
        JsonNode? merged = LoadMerged();
        JsonArray? channelEnum = merged?["properties"]?["autoUpdatesChannel"]?["enum"] as JsonArray;

        MessageAssert.NotNull(channelEnum, "autoUpdatesChannel.enum must survive the overlay merge.");
        string?[] values = channelEnum!.Select(v => v?.GetValue<string>()).ToArray();
        Assert.Contains("stable", values);
        Assert.Contains("latest", values);
    }

    private static JsonNode? LoadMerged()
    {
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named(ClaudeCodeSchemaFile));
        Assert.NotNull(bytes);
        return JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public void BaseSchema_ModelProperty_DoesNotCarryHandCuratedAdditions()
    {
        // Regression lock: the BASE schema (before overlay) must NOT carry
        // the hand-curated additions.  If a future contributor re-applies
        // them to the base file by accident, this test fails and points at
        // the right fix: move the addition into the overlay instead.
        byte[]? baseBytes = BundledProduct.ClaudeFiles.TryRead(ClaudeCodeSchemaFile);
        MessageAssert.NotNull(baseBytes, "Claude's bundled files must carry the base schema.");
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(baseBytes!));
        JsonObject? model = node?["properties"]?["model"] as JsonObject;
        Assert.NotNull(model);

        Assert.False(model!.ContainsKey("default"),
            "Base schema must NOT carry `model.default` — that lives in the overlay. "
            + "If this test fails after `refresh-schema`, check that the overlay file "
            + "wasn't accidentally folded into the base.");
        Assert.False(model.ContainsKey("examples"),
            "Base schema must NOT carry `model.examples` — that lives in the overlay.");
    }
}
