using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Verifies the "//" header stamp is written first and the merged body is reproduced
/// by <see cref="EffectiveConfigBuilder.BuildEffective"/>.
/// </summary>
[TestClass]
public sealed class EffectiveConfigBuilderTests
{
    [TestMethod]
    public void BuildEffective_StampIsFirstKey()
    {
        JsonObject userRoot = new() { ["theme"] = "dark", ["autoSave"] = true };
        SettingsDocument userDoc = new(ConfigScope.User, "/tmp/settings.json", userRoot, isReadOnly: false);
        SettingsWorkspace ws = new([userDoc]);

        JsonObject result = EffectiveConfigBuilder.BuildEffective(ws, "test stamp");

        List<string> keys = result.Select(kv => kv.Key).ToList();
        Assert.AreEqual("//", keys[0], "The '//' stamp must be the very first key.");
        Assert.AreEqual("test stamp", result["//"]!.GetValue<string>());
        Assert.AreEqual("dark", result["theme"]!.GetValue<string>());
        Assert.IsTrue(result["autoSave"]!.GetValue<bool>());
    }

    [TestMethod]
    public void BuildEffective_EmptyWorkspaceStillProducesStamp()
    {
        SettingsDocument doc = new(ConfigScope.User, "/tmp/settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc]);

        JsonObject result = EffectiveConfigBuilder.BuildEffective(ws, "empty");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("empty", result["//"]!.GetValue<string>());
    }

    // 4.3.7 step 12 — Stamp(JsonObject, string) overload.

    [TestMethod]
    public void Stamp_StampIsFirstKey_PreservesBodyDeepClone()
    {
        // Caller hands in an already-merged JsonObject (typically from
        // SDK.ComputeEffectiveSnapshot in the GUI's export flow).
        JsonObject effective = new() { ["theme"] = "dark", ["autoSave"] = true };

        JsonObject result = EffectiveConfigBuilder.Stamp(effective, "ClaudeForge GUI v1.2");

        List<string> keys = result.Select(kv => kv.Key).ToList();
        Assert.AreEqual("//", keys[0], "The '//' stamp must be the first key.");
        Assert.AreEqual("ClaudeForge GUI v1.2", result["//"]!.GetValue<string>());
        Assert.AreEqual("dark", result["theme"]!.GetValue<string>());
        Assert.IsTrue(result["autoSave"]!.GetValue<bool>());
    }

    [TestMethod]
    public void Stamp_DoesNotMutateInputJsonObject()
    {
        JsonObject effective = new() { ["theme"] = "dark" };
        JsonObject result = EffectiveConfigBuilder.Stamp(effective, "stamp");

        Assert.AreNotSame(effective, result, "Stamp must return a fresh JsonObject.");
        Assert.IsFalse(effective.ContainsKey("//"),
            "Original input must not gain a '//' key — Stamp deep-clones into a new object.");
    }

    [TestMethod]
    public void Stamp_NullInput_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            EffectiveConfigBuilder.Stamp(null!, "stamp"));
    }
}