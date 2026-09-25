using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Settings;

public sealed class SettingsDocumentTests
{
    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        JsonObject root = new() { ["key"] = "value" };
        SettingsDocument doc = new(ConfigScope.User, "/fake/path.json", root, isReadOnly: false);

        Assert.Equal(ConfigScope.User, doc.Scope);
        Assert.Equal("/fake/path.json", doc.FilePath);
        Assert.Same(root, doc.Root);
        Assert.False(doc.IsReadOnly);
        Assert.False(doc.IsDirty);
        Assert.Null(doc.LastModified);
    }

    [Fact]
    public void Constructor_SnapshotsBaselineRoot()
    {
        JsonObject root = new() { ["key"] = "original" };
        SettingsDocument doc = new(ConfigScope.User, "/fake/path.json", root, isReadOnly: false);

        // Baseline is a deep clone — mutating Root must not affect the baseline.
        root["key"] = "mutated";

        Assert.Equal("original", doc.BaselineRoot?["key"]?.GetValue<string>());
    }

    [Fact]
    public void Constructor_AcceptsReadOnlyFlag()
    {
        SettingsDocument doc = new(ConfigScope.Managed, "/sys/policy.json",
            new JsonObject(), isReadOnly: true);

        Assert.True(doc.IsReadOnly);
    }

    // -----------------------------------------------------------------------
    // MarkDirty / MarkClean
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkDirty_SetsIsDirtyTrue()
    {
        SettingsDocument doc = MakeDoc();
        doc.MarkDirty();

        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void MarkClean_ClearsIsDirtyFlag()
    {
        SettingsDocument doc = MakeDoc();
        doc.MarkDirty();
        doc.MarkClean();

        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void MarkClean_AdvancesBaselineToCurrentRoot()
    {
        JsonObject root = new() { ["key"] = "original" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        // Simulate an in-memory edit followed by save.
        root["key"] = "edited";
        doc.MarkDirty();
        doc.MarkClean();

        // Baseline must now reflect the post-save state.
        Assert.Equal("edited", doc.BaselineRoot?["key"]?.GetValue<string>());
    }

    [Fact]
    public void MarkClean_BaselineIsDeepClone_FurtherMutationDoesNotCorruptBaseline()
    {
        JsonObject root = new() { ["key"] = "saved" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        doc.MarkClean();

        // Mutate Root after MarkClean — baseline must remain unchanged.
        root["key"] = "after-clean";

        Assert.Equal("saved", doc.BaselineRoot?["key"]?.GetValue<string>());
    }

    // -----------------------------------------------------------------------
    // UpdateRoot
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateRoot_ReplacesRootWithNewObject()
    {
        JsonObject original = new() { ["a"] = 1 };
        SettingsDocument doc = new(ConfigScope.User, "/p", original, false);
        doc.MarkDirty();

        JsonObject reloaded = new() { ["b"] = 2 };
        doc.UpdateRoot(reloaded);

        Assert.Same(reloaded, doc.Root);
    }

    [Fact]
    public void UpdateRoot_ClearsDirtyFlag()
    {
        SettingsDocument doc = MakeDoc();
        doc.MarkDirty();
        doc.UpdateRoot(new JsonObject());

        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void UpdateRoot_SetsLastModifiedToApproximatelyNow()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        SettingsDocument doc = MakeDoc();
        doc.UpdateRoot(new JsonObject());
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.NotNull(doc.LastModified);
        Assert.True(doc.LastModified >= before);
        Assert.True(doc.LastModified <= after);
    }

    [Fact]
    public void UpdateRoot_AdvancesBaselineToNewRoot()
    {
        JsonObject original = new() { ["k"] = "old" };
        SettingsDocument doc = new(ConfigScope.User, "/p", original, false);

        JsonObject incoming = new() { ["k"] = "new" };
        doc.UpdateRoot(incoming);

        // Baseline should now match the incoming root.
        Assert.Equal("new", doc.BaselineRoot?["k"]?.GetValue<string>());
    }

    [Fact]
    public void UpdateRoot_BaselineIsIndependentDeepClone()
    {
        SettingsDocument doc = MakeDoc();
        JsonObject incoming = new() { ["k"] = "reloaded" };
        doc.UpdateRoot(incoming);

        // Mutate the incoming object after UpdateRoot — baseline must not change.
        incoming["k"] = "mutated-after";

        Assert.Equal("reloaded", doc.BaselineRoot?["k"]?.GetValue<string>());
    }

    // -----------------------------------------------------------------------
    // Round-trip: dirty → clean → dirty
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleMarkDirtyMarkCleanCycles_StateRemainsConsistent()
    {
        JsonObject root = new() { ["x"] = 0 };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        for (int i = 1; i <= 3; i++)
        {
            root["x"] = i;
            doc.MarkDirty();
            Assert.True(doc.IsDirty, $"cycle {i}: expected dirty");

            doc.MarkClean();
            Assert.False(doc.IsDirty, $"cycle {i}: expected clean");
            MessageAssert.Equal(i, doc.BaselineRoot?["x"]?.GetValue<int>(), $"cycle {i}: baseline mismatch");
        }
    }

    // -----------------------------------------------------------------------
    // HasActualChanges
    // -----------------------------------------------------------------------

    [Fact]
    public void HasActualChanges_FreshDoc_ReturnsFalse()
    {
        SettingsDocument doc = new(ConfigScope.User, "/p", new JsonObject { ["k"] = "v" }, false);
        Assert.False(doc.HasActualChanges());
    }

    [Fact]
    public void HasActualChanges_AfterMutation_ReturnsTrue()
    {
        JsonObject root = new() { ["k"] = "original" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        root["k"] = "changed";

        Assert.True(doc.HasActualChanges());
    }

    /// <summary>
    /// pin the "//" metadata-key strip behaviour.  HasActualChanges
    /// must agree with JsonDiff.Compute's metadata-strip so the GUI's Save button
    /// (driven by the former) and the per-property change preview (built by the
    /// latter) never disagree on whether content is dirty.
    /// <para>
    /// Repro: Root has "//" timestamp A, BaselineRoot has "//" timestamp B,
    /// everything else identical.  JsonNode.DeepEquals would say "different",
    /// causing a structurally-clean document to look dirty.  HasActualChanges
    /// strips "//" from both sides and reports clean.
    /// </para>
    /// </summary>
    [Fact]
    public void HasActualChanges_OnlyMetadataKeyDiffers_ReturnsFalse()
    {
        JsonObject root = new()
        {
            ["//"] = "ClaudeForge v1.0 last saved 2026-05-08 10:00:00 AM",
            ["k"] = "v",
        };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        // Mutate the metadata key only — content is structurally unchanged
        // from the user's perspective.  HasActualChanges must report clean.
        root["//"] = "ClaudeForge v1.0 last saved 2026-05-08 11:30:00 AM";

        Assert.False(doc.HasActualChanges(),
            "Timestamp-only mutation in the '//' header-comment key must not flag the document dirty.");
    }

    /// <summary>
    /// Inverse: a real content change is still flagged dirty even when the
    /// metadata key happens to also differ between Root and BaselineRoot.
    /// </summary>
    [Fact]
    public void HasActualChanges_RealChangePlusMetadataChange_ReturnsTrue()
    {
        JsonObject root = new()
        {
            ["//"] = "old timestamp",
            ["k"] = "v",
        };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        root["//"] = "new timestamp";
        root["k"] = "different value";

        Assert.True(doc.HasActualChanges(),
            "Real value change must still report dirty even when the metadata key also differs.");
    }

    /// <summary>
    /// One side has the metadata key, the other doesn't — but content is
    /// otherwise identical.  Strip-then-compare on both sides should still
    /// report clean.  This is the post-MarkClean shape: BaselineRoot was
    /// cloned BEFORE the save flow added "//" to the on-disk file, so
    /// in-memory baseline lacks "//" while a fresh load (or mid-session
    /// mutation) might have it.
    /// </summary>
    [Fact]
    public void HasActualChanges_OneSideHasMetadataKey_OtherDoesNot_StillClean()
    {
        // Document loaded from disk without "//" header.
        JsonObject root = new() { ["k"] = "v" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        // Add "//" after construction — Root has metadata, BaselineRoot
        // (captured at construction) does not.
        root["//"] = "stamped just now";

        Assert.False(doc.HasActualChanges(),
            "Adding only the '//' metadata key must not flag the document dirty.");
    }

    [Fact]
    public void HasActualChanges_AfterSetThenReset_ReturnsFalse()
    {
        // Simulates: user changes a value, then presses Reset — net content is unchanged.
        JsonObject root = new() { ["k"] = "original" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        // "Set" — add a new key
        root["newKey"] = "added";
        Assert.True(doc.HasActualChanges(), "should be dirty after set");

        // "Reset" — remove the key we just added
        root.Remove("newKey");
        Assert.False(doc.HasActualChanges(), "should be clean after reset (back to baseline)");
    }

    [Fact]
    public void HasActualChanges_AfterMarkClean_ReturnsFalse()
    {
        JsonObject root = new() { ["k"] = "v" };
        SettingsDocument doc = new(ConfigScope.User, "/p", root, false);

        root["k"] = "mutated";
        Assert.True(doc.HasActualChanges());

        doc.MarkClean();
        Assert.False(doc.HasActualChanges(), "MarkClean should advance baseline to current state");
    }

    [Fact]
    public void HasActualChanges_EmptyDoc_ReturnsFalse()
    {
        SettingsDocument doc = new(ConfigScope.User, "/p", new JsonObject(), false);
        Assert.False(doc.HasActualChanges());
    }

    // -----------------------------------------------------------------------
    // Constructor guard — non-object root
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_WhenDeepCloneReturnsNonObject_FallsBackToEmptyBaseline()
    {
        // The SettingsDocument constructor parameter is typed `JsonObject root`, so a
        // JsonArray cannot be passed at compile time. The defensive branch in the body
        // handles the impossible-by-the-STJ-contract case where DeepClone() returns a
        // non-JsonObject (e.g. a hypothetical JsonObject subclass overriding DeepClone).
        //
        // Previously this threw InvalidOperationException — which would abort workspace
        // load and crash the app launch (loading runs synchronously before the UI is up,
        // so the user would see a closed window with no error dialog). The constructor
        // now logs a warning and falls back to an empty JsonObject baseline so the
        // workspace remains usable.
        //
        // We exercise the branch by force-casting a JsonArray into a JsonObject
        // reference via Unsafe.As — intentionally unsound, but the only way to reach
        // the post-DeepClone fallback at runtime.
        JsonNode jsonArray = JsonNode.Parse("[\"a\",\"b\"]")!;
        JsonObject asObject = Unsafe.As<JsonObject>(jsonArray);

        SettingsDocument doc = new(
            ConfigScope.User, "/fake/path.json", asObject, isReadOnly: false);

        MessageAssert.NotNull(doc.BaselineRoot,
            "Even with an unsound root, the constructor must establish a non-null baseline.");
        MessageAssert.Equal(0, doc.BaselineRoot!.Count,
            "Baseline should fall back to an empty JsonObject when DeepClone does not return a JsonObject.");
    }

    [Fact]
    public void Constructor_WhenRootIsObject_SetsBaselineRoot()
    {
        JsonObject root = new() { ["env"] = new JsonObject { ["API_KEY"] = "val" } };
        SettingsDocument doc = new(ConfigScope.User, "/fake/path.json", root, isReadOnly: false);
        MessageAssert.NotNull(doc.BaselineRoot, "BaselineRoot must be non-null for a JsonObject root.");
        Assert.True(doc.BaselineRoot.ContainsKey("env"),
            "BaselineRoot must contain a deep-cloned copy of the root object.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SettingsDocument MakeDoc()
    {
        return new SettingsDocument(ConfigScope.User, "/fake/settings.json", new JsonObject(), isReadOnly: false);
    }
}