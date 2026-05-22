using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// locks the overlay-merge mechanism that lets hand-curated
/// additions survive an upstream-schema refresh.  Hand-curated bits live in
/// <c>claude-code-settings.overlay.json</c>; <see cref="SchemaRegistry"/>
/// merges them onto the verbatim upstream schema at load time via RFC 7396
/// JSON Merge Patch.  Pre-fix, every <c>scripts/refresh-schema.{sh,ps1}</c>
/// run silently wiped the additions and required manual re-application.
/// </summary>
[TestClass]
public sealed class SchemaRegistryOverlayTests
{
    // ── ApplyMergePatch — RFC 7396 unit tests ────────────────────────

    [TestMethod]
    public void ApplyMergePatch_PatchReplacesPrimitive_WhenSameKey()
    {
        JsonNode? target = JsonNode.Parse("""{"k":"old"}""");
        JsonNode? patch = JsonNode.Parse("""{"k":"new"}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.AreEqual("new", merged?["k"]?.GetValue<string>());
    }

    [TestMethod]
    public void ApplyMergePatch_PatchAddsKey_WhenAbsentInTarget()
    {
        JsonNode? target = JsonNode.Parse("""{"a":1}""");
        JsonNode? patch = JsonNode.Parse("""{"b":2}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.AreEqual(1, merged?["a"]?.GetValue<int>());
        Assert.AreEqual(2, merged?["b"]?.GetValue<int>());
    }

    [TestMethod]
    public void ApplyMergePatch_PatchRecursesIntoNestedObjects()
    {
        // RFC 7396: when both target and patch have an object at the same
        // key, the patch's object recursively merges onto the target's.
        JsonNode? target = JsonNode.Parse("""{"outer":{"x":1,"y":2}}""");
        JsonNode? patch = JsonNode.Parse("""{"outer":{"y":99,"z":3}}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.AreEqual(1, merged?["outer"]?["x"]?.GetValue<int>(), "Original 'x' preserved.");
        Assert.AreEqual(99, merged?["outer"]?["y"]?.GetValue<int>(), "Patch overwrites 'y'.");
        Assert.AreEqual(3, merged?["outer"]?["z"]?.GetValue<int>(), "Patch adds 'z'.");
    }

    [TestMethod]
    public void ApplyMergePatch_NullInPatch_DeletesKey()
    {
        // RFC 7396 §2: explicit null in the patch removes the key from target.
        JsonNode? target = JsonNode.Parse("""{"keep":1,"drop":2}""");
        JsonNode? patch = JsonNode.Parse("""{"drop":null}""");

        JsonObject? merged = SchemaRegistry.ApplyMergePatch(target, patch) as JsonObject;

        Assert.IsNotNull(merged);
        Assert.IsTrue(merged!.ContainsKey("keep"));
        Assert.IsFalse(merged.ContainsKey("drop"), "null in patch must remove the key.");
    }

    [TestMethod]
    public void ApplyMergePatch_ArrayInPatch_ReplacesWholesale()
    {
        // RFC 7396 explicitly does NOT merge arrays — patch array replaces
        // target array.  This is the intuitive shape for schema 'examples'
        // (you want overlay's list, not concatenated lists).
        JsonNode? target = JsonNode.Parse("""{"examples":["a","b","c"]}""");
        JsonNode? patch = JsonNode.Parse("""{"examples":["x","y"]}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        JsonArray? arr = merged?["examples"] as JsonArray;
        Assert.IsNotNull(arr);
        Assert.AreEqual(2, arr!.Count);
        Assert.AreEqual("x", arr[0]?.GetValue<string>());
        Assert.AreEqual("y", arr[1]?.GetValue<string>());
    }

    [TestMethod]
    public void ApplyMergePatch_PrimitivePatch_ReplacesObjectTarget()
    {
        // RFC 7396 §1: if patch is not an object, patch replaces target.
        JsonNode? target = JsonNode.Parse("""{"nested":{"a":1}}""");
        JsonNode? patch = JsonNode.Parse("\"replaced\"");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.AreEqual("replaced", merged?.GetValue<string>());
    }

    [TestMethod]
    public void ApplyMergePatch_NullTarget_PatchObjectBecomesResult()
    {
        // Null target + object patch → patch's keys become the result.
        JsonNode? patch = JsonNode.Parse("""{"a":1,"b":2}""");

        JsonObject? merged = SchemaRegistry.ApplyMergePatch(null, patch) as JsonObject;

        Assert.IsNotNull(merged);
        Assert.AreEqual(1, merged!["a"]?.GetValue<int>());
        Assert.AreEqual(2, merged["b"]?.GetValue<int>());
    }

    // ── TryReadBundledBytesMerged — production path E2E ──────────────

    [TestMethod]
    public void TryReadBundledBytesMerged_AppliesClaudeCodeOverlay()
    {
        // Reading the production bundled schema through the merged loader
        // must surface the overlay's `model.examples` + `default` even
        // though the base file no longer carries them.
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged("claude-code-settings.json");
        Assert.IsNotNull(bytes);

        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
        JsonObject? model = node?["properties"]?["model"] as JsonObject;
        Assert.IsNotNull(model, "model property must survive the merge.");

        Assert.AreEqual("sonnet", model!["default"]?.GetValue<string>(),
            "Overlay's `default` must surface on the merged result.");

        JsonArray? examples = model["examples"] as JsonArray;
        Assert.IsNotNull(examples, "Overlay's `examples` must surface.");
        Assert.IsTrue(examples!.Count >= 4, $"At least 4 examples expected; got {examples.Count}.");
        string?[] values = examples.Select(e => e?.GetValue<string>()).ToArray();
        CollectionAssert.Contains(values, "sonnet");
        CollectionAssert.Contains(values, "opus");
        CollectionAssert.Contains(values, "haiku");
    }

    [TestMethod]
    public void TryReadBundledBytesMerged_NoOverlay_ReturnsBaseUnchanged()
    {
        // claude-desktop-config.json has no .overlay.json sibling — the
        // merged helper must return the base bytes verbatim.
        byte[]? merged = SchemaRegistry.TryReadBundledBytesMerged("claude-desktop-config.json");
        Assert.IsNotNull(merged);

        // Sanity: the base file must parse as JSON (the merge is a no-op
        // here, so the resulting bytes are just the base file's content).
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(merged!));
        Assert.IsNotNull(node);
    }

    [TestMethod]
    public void TryReadBundledBytesMerged_NoSuchSchema_ReturnsNull()
    {
        byte[]? merged = SchemaRegistry.TryReadBundledBytesMerged("definitely-not-a-real-schema.json");
        Assert.IsNull(merged);
    }

    [TestMethod]
    public void ApplyMergePatch_PreservesKeyOrder_WhenPatchedKeyAlreadyExists()
    {
        // Regression lock (2026-05-19): the first implementation called
        // `result.Remove(key)` before re-adding the patched value, which
        // silently moved the key to the END of the underlying OrderedDictionary.
        // For the Claude Code schema this caused `model` to jump from its
        // original mid-list position to the bottom of the editor's property
        // list when the overlay was introduced.  Fix: direct indexer
        // assignment preserves the existing position.
        JsonNode? target = JsonNode.Parse("""{"a":1,"b":2,"c":3,"d":4}""");
        JsonNode? patch = JsonNode.Parse("""{"b":99}""");

        JsonObject? merged = SchemaRegistry.ApplyMergePatch(target, patch) as JsonObject;

        Assert.IsNotNull(merged);
        string[] keysInOrder = merged!.Select(kvp => kvp.Key).ToArray();
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c", "d" },
            keysInOrder,
            "Patched key must keep its original position; previously it moved to the end.");
        Assert.AreEqual(99, merged["b"]?.GetValue<int>());
    }

    [TestMethod]
    public void TryReadBundledBytesMerged_PreservesModelPropertyPosition_InMergedResult()
    {
        // End-to-end variant of the order-preservation regression: the model
        // property must keep its mid-list position in `properties` after the
        // overlay merge.  Pre-fix, the overlay's `model` patch moved the key
        // to the end of the properties OrderedDictionary, and the editor's
        // property list rendered `model` as the last entry.
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged("claude-code-settings.json");
        Assert.IsNotNull(bytes);
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
        JsonObject? properties = node?["properties"] as JsonObject;
        Assert.IsNotNull(properties);

        string[] keys = properties!.Select(kvp => kvp.Key).ToArray();
        int modelIdx = Array.IndexOf(keys, "model");
        int lastIdx = keys.Length - 1;
        Assert.IsTrue(modelIdx >= 0, "model property must exist in merged schema.");
        Assert.AreNotEqual(lastIdx, modelIdx,
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

    [TestMethod]
    public void Overlay_PreservesPermissionRulePattern()
    {
        // $defs.permissionRule.pattern is a regex locking valid permission
        // rule names (e.g. "Bash:*", "Edit", "Read(/etc/passwd)").  If a
        // future overlay strips this pattern, an attacker-crafted profile
        // import could land arbitrary action names that the schema validator
        // no longer rejects.  Test guards against accidental removal.
        JsonNode? merged = LoadMerged();
        string? pattern = merged?["$defs"]?["permissionRule"]?["pattern"]?.GetValue<string>();

        Assert.IsFalse(string.IsNullOrEmpty(pattern),
            "$defs.permissionRule.pattern must survive the overlay merge. "
            + "If this fails, an overlay entry has stripped the regex that locks "
            + "permission-rule action names.  See claude-code-settings.overlay.json's "
            + "$comment-must-not-touch block.");
        StringAssert.Contains(pattern!, "Bash",
            "Pattern must enumerate the known action names — Bash is the canary.");
    }

    [TestMethod]
    public void Overlay_PreservesHookCommandTypeConst()
    {
        // $defs.hookCommand.anyOf[0].properties.type.const = "command" is the
        // discriminator that disambiguates the command hook variant from
        // agent / http variants.  Stripping the const would let the schema
        // accept arbitrary `type` values, breaking the opaque-preservation
        // contract that protects unknown hook types from silent downcast.
        JsonNode? merged = LoadMerged();
        JsonNode? hookCommand = merged?["$defs"]?["hookCommand"];
        Assert.IsNotNull(hookCommand, "$defs.hookCommand must exist in the merged schema.");

        JsonArray? anyOf = hookCommand!["anyOf"] as JsonArray;
        Assert.IsNotNull(anyOf, "hookCommand must use anyOf to discriminate variants.");
        Assert.IsTrue(anyOf!.Count > 0, "hookCommand.anyOf must have at least one variant.");

        string? typeConst = anyOf[0]?["properties"]?["type"]?["const"]?.GetValue<string>();
        Assert.AreEqual("command", typeConst,
            "hookCommand.anyOf[0].properties.type.const must remain \"command\" — "
            + "the variant discriminator MUST NOT be stripped by the overlay.");
    }

    [TestMethod]
    public void Overlay_PreservesAutoUpdatesChannelEnum()
    {
        // properties.autoUpdatesChannel.enum = ["stable", "latest"] locks the
        // update channel selector.  Stripping the enum would let a malformed
        // profile import land an arbitrary string here, which the auto-updater
        // would then dereference.
        JsonNode? merged = LoadMerged();
        JsonArray? channelEnum = merged?["properties"]?["autoUpdatesChannel"]?["enum"] as JsonArray;

        Assert.IsNotNull(channelEnum, "autoUpdatesChannel.enum must survive the overlay merge.");
        string?[] values = channelEnum!.Select(v => v?.GetValue<string>()).ToArray();
        CollectionAssert.Contains(values, "stable");
        CollectionAssert.Contains(values, "latest");
    }

    private static JsonNode? LoadMerged()
    {
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged("claude-code-settings.json");
        Assert.IsNotNull(bytes);
        return JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
    }

    [TestMethod]
    public void BaseSchema_ModelProperty_DoesNotCarryHandCuratedAdditions()
    {
        // Regression lock: the BASE schema (before overlay) must NOT carry
        // the hand-curated additions.  If a future contributor re-applies
        // them to the base file by accident, this test fails and points at
        // the right fix: move the addition into the overlay instead.
        byte[]? baseBytes = ReadBaseResource("claude-code-settings.json");
        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(baseBytes!));
        JsonObject? model = node?["properties"]?["model"] as JsonObject;
        Assert.IsNotNull(model);

        Assert.IsFalse(model!.ContainsKey("default"),
            "Base schema must NOT carry `model.default` — that lives in the overlay. "
            + "If this test fails after `refresh-schema`, check that the overlay file "
            + "wasn't accidentally folded into the base.");
        Assert.IsFalse(model.ContainsKey("examples"),
            "Base schema must NOT carry `model.examples` — that lives in the overlay.");
    }

    private static byte[]? ReadBaseResource(string cacheFileName)
    {
        Assembly assembly = typeof(SchemaRegistry).Assembly;
        string resourceName = ResourceHelper.ResourcePrefix + $".Core.Assets.Schemas.{cacheFileName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}