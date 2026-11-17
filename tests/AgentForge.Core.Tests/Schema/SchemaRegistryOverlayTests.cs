using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// locks the overlay-merge mechanism that lets hand-curated
/// additions survive an upstream-schema refresh.  Hand-curated bits live in
/// a product's <c>*.overlay.json</c>; <see cref="SchemaRegistry"/>
/// merges them onto the verbatim upstream schema at load time via RFC 7396
/// JSON Merge Patch.  Pre-fix, every <c>scripts/refresh-schema.{sh,ps1}</c>
/// run silently wiped the additions and required manual re-application.
/// </summary>
/// <remarks>
/// The tests of Claude Code's own overlay live in <c>ClaudeForge.Sdk.Claude.Tests</c> under the
/// same class name. OpenCode's TUI overlay is still tested here until <c>plans/00008</c> step 7.
/// </remarks>
public sealed class SchemaRegistryOverlayTests
{
    // ── ApplyMergePatch — RFC 7396 unit tests ────────────────────────

    [Fact]
    public void ApplyMergePatch_PatchReplacesPrimitive_WhenSameKey()
    {
        JsonNode? target = JsonNode.Parse("""{"k":"old"}""");
        JsonNode? patch = JsonNode.Parse("""{"k":"new"}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.Equal("new", merged?["k"]?.GetValue<string>());
    }

    [Fact]
    public void ApplyMergePatch_PatchAddsKey_WhenAbsentInTarget()
    {
        JsonNode? target = JsonNode.Parse("""{"a":1}""");
        JsonNode? patch = JsonNode.Parse("""{"b":2}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.Equal(1, merged?["a"]?.GetValue<int>());
        Assert.Equal(2, merged?["b"]?.GetValue<int>());
    }

    [Fact]
    public void ApplyMergePatch_PatchRecursesIntoNestedObjects()
    {
        // RFC 7396: when both target and patch have an object at the same
        // key, the patch's object recursively merges onto the target's.
        JsonNode? target = JsonNode.Parse("""{"outer":{"x":1,"y":2}}""");
        JsonNode? patch = JsonNode.Parse("""{"outer":{"y":99,"z":3}}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        MessageAssert.Equal(1, merged?["outer"]?["x"]?.GetValue<int>(), "Original 'x' preserved.");
        MessageAssert.Equal(99, merged?["outer"]?["y"]?.GetValue<int>(), "Patch overwrites 'y'.");
        MessageAssert.Equal(3, merged?["outer"]?["z"]?.GetValue<int>(), "Patch adds 'z'.");
    }

    [Fact]
    public void ApplyMergePatch_NullInPatch_DeletesKey()
    {
        // RFC 7396 §2: explicit null in the patch removes the key from target.
        JsonNode? target = JsonNode.Parse("""{"keep":1,"drop":2}""");
        JsonNode? patch = JsonNode.Parse("""{"drop":null}""");

        JsonObject? merged = SchemaRegistry.ApplyMergePatch(target, patch) as JsonObject;

        Assert.NotNull(merged);
        Assert.True(merged!.ContainsKey("keep"));
        Assert.False(merged.ContainsKey("drop"), "null in patch must remove the key.");
    }

    [Fact]
    public void ApplyMergePatch_ArrayInPatch_ReplacesWholesale()
    {
        // RFC 7396 explicitly does NOT merge arrays — patch array replaces
        // target array.  This is the intuitive shape for schema 'examples'
        // (you want overlay's list, not concatenated lists).
        JsonNode? target = JsonNode.Parse("""{"examples":["a","b","c"]}""");
        JsonNode? patch = JsonNode.Parse("""{"examples":["x","y"]}""");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        JsonArray? arr = merged?["examples"] as JsonArray;
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Count);
        Assert.Equal("x", arr[0]?.GetValue<string>());
        Assert.Equal("y", arr[1]?.GetValue<string>());
    }

    [Fact]
    public void ApplyMergePatch_PrimitivePatch_ReplacesObjectTarget()
    {
        // RFC 7396 §1: if patch is not an object, patch replaces target.
        JsonNode? target = JsonNode.Parse("""{"nested":{"a":1}}""");
        JsonNode? patch = JsonNode.Parse("\"replaced\"");

        JsonNode? merged = SchemaRegistry.ApplyMergePatch(target, patch);

        Assert.Equal("replaced", merged?.GetValue<string>());
    }

    [Fact]
    public void ApplyMergePatch_NullTarget_PatchObjectBecomesResult()
    {
        // Null target + object patch → patch's keys become the result.
        JsonNode? patch = JsonNode.Parse("""{"a":1,"b":2}""");

        JsonObject? merged = SchemaRegistry.ApplyMergePatch(null, patch) as JsonObject;

        Assert.NotNull(merged);
        Assert.Equal(1, merged!["a"]?.GetValue<int>());
        Assert.Equal(2, merged["b"]?.GetValue<int>());
    }

    // ── TryReadBundledBytesMerged — production path E2E ──────────────

    /// <summary>
    /// The OpenCode TUI overlay's only job: give <c>theme</c> the <c>examples</c> and
    /// <c>description</c> its upstream declaration lacks.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream declares <c>theme</c> as the bare <c>{"type":"string"}</c> — no description, no
    /// examples — so without the overlay the field renders as an unlabelled text box and the user
    /// has no way to learn what a valid theme name is. Asserted against the MERGED loader, because
    /// the base file deliberately still carries neither key.
    /// </remarks>
    [Fact]
    public void TryReadBundledBytesMerged_AppliesOpenCodeTuiOverlay()
    {
        byte[]? bytes = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named("opencode-tui.json"));
        Assert.NotNull(bytes);

        JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(bytes!));
        JsonObject? theme = node?["properties"]?["theme"] as JsonObject;
        MessageAssert.NotNull(theme, "the theme property must survive the merge.");

        Assert.False(
            string.IsNullOrWhiteSpace(theme!["description"]?.GetValue<string>()),
            "the overlay's description must surface — upstream has none.");

        JsonArray? examples = theme["examples"] as JsonArray;
        MessageAssert.NotNull(examples, "the overlay's examples must surface.");
        MessageAssert.SequenceEqual(
            new[] { "opencode" },
            examples!.Select(e => e?.GetValue<string>()).ToArray(),
            "only the built-in theme is asserted here; the rest are discovered at runtime.");

        // The merge must not have cost the schema its other properties.
        MessageAssert.NotNull(node?["properties"]?["keybinds"], "keybinds must survive the merge.");
    }

    /// <summary>
    /// ⭐⭐ The end-to-end consequence, and the only assertion that proves the overlay is worth
    /// having: with it, <c>theme</c> reaches the editor factory as an <b>Enum with examples</b> —
    /// which is what the generic dispatch turns into a picker that still accepts typed values.
    /// </summary>
    /// <remarks>
    /// ⚠ Built from the MERGED bytes on purpose. The sibling tree tests read
    /// <c>BundledResource.TryRead</c>, i.e. the raw base file, so none of them can see an overlay —
    /// which is exactly why adding this overlay broke no existing test and why this assertion had to
    /// be written rather than assumed.
    /// </remarks>
    [Fact]
    public void OpenCodeTuiTheme_IsPromotedToAFreeFormEnum_ByTheOverlay()
    {
        // An isolated Json.Schema registry per parse, the same way the sibling tree tests do, so
        // one parse's $defs anchors cannot collide with another's in the global registry.
        static SchemaNode ThemeFrom(byte[] bytes)
        {
            Json.Schema.BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
            Json.Schema.JsonSchemaNode root =
                Json.Schema.JsonSchema.FromText(Encoding.UTF8.GetString(bytes), opts).Root!;
            return SchemaTreeBuilder.BuildTopLevel(root).Single(n => n.Name == "theme");
        }

        byte[]? baseBytes = BundledResource.TryRead("Schemas", "opencode-tui.json");
        byte[]? merged = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named("opencode-tui.json"));
        Assert.NotNull(baseBytes);
        Assert.NotNull(merged);

        SchemaNode withoutOverlay = ThemeFrom(baseBytes!);
        SchemaNode withOverlay = ThemeFrom(merged!);

        MessageAssert.Equal(
            SchemaValueType.String,
            withoutOverlay.ValueType,
            "the bare upstream declaration is a plain string — a text box with no suggestions.");

        MessageAssert.Equal(
            SchemaValueType.Enum,
            withOverlay.ValueType,
            "with the overlay it must classify as an enum, which is what renders the picker.");
        MessageAssert.NotEqual(
            0,
            withOverlay.Examples.Count,
            "a non-empty Examples is what keeps the picker free-form rather than closed — "
                + "without it the editor would refuse every theme this build has not heard of.");
    }

    [Fact]
    public void TryReadBundledBytesMerged_NoOverlay_ReturnsBaseUnchanged()
    {
        // The sample's second schema has no .overlay.json sibling — the
        // merged helper must return the base bytes verbatim.
        ProductDescriptor noOverlay = BundledProduct.For(
            "https://example.invalid/sample-other.json", BundledProduct.SampleOtherSchemaFileName);
        byte[]? merged = SchemaRegistry.TryReadBundledBytesMerged(noOverlay);
        Assert.NotNull(merged);

        MessageAssert.SequenceEqual(
            BundledProduct.SampleFiles.TryRead(BundledProduct.SampleOtherSchemaFileName)!,
            merged!,
            "With no overlay the merged bytes must be the base file's bytes, unchanged.");
    }

    [Fact]
    public void TryReadBundledBytesMerged_NoSuchSchema_ReturnsNull()
    {
        byte[]? merged = SchemaRegistry.TryReadBundledBytesMerged(BundledProduct.Named("definitely-not-a-real-schema.json"));
        Assert.Null(merged);
    }

    [Fact]
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

        Assert.NotNull(merged);
        string[] keysInOrder = merged!.Select(kvp => kvp.Key).ToArray();
        MessageAssert.SequenceEqual(
            new[] { "a", "b", "c", "d" },
            keysInOrder,
            "Patched key must keep its original position; previously it moved to the end.");
        Assert.Equal(99, merged["b"]?.GetValue<int>());
    }
}