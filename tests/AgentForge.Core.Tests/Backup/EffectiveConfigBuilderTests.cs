using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Verifies the "//" header stamp is written first and the merged body is reproduced
/// by <see cref="EffectiveConfigBuilder.BuildEffective"/>.
/// </summary>
public sealed class EffectiveConfigBuilderTests
{
    [Fact]
    public void BuildEffective_StampIsFirstKey()
    {
        JsonObject userRoot = new() { ["theme"] = "dark", ["autoSave"] = true };
        SettingsDocument userDoc = new(ConfigScope.User, "/tmp/settings.json", userRoot, isReadOnly: false);
        SettingsWorkspace ws = new([userDoc], TestMergePolicy.Inferring);

        JsonObject result = EffectiveConfigBuilder.BuildEffective(ws, "test stamp");

        List<string> keys = result.Select(kv => kv.Key).ToList();
        MessageAssert.Equal("//", keys[0], "The '//' stamp must be the very first key.");
        Assert.Equal("test stamp", result["//"]!.GetValue<string>());
        Assert.Equal("dark", result["theme"]!.GetValue<string>());
        Assert.True(result["autoSave"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildEffective_EmptyWorkspaceStillProducesStamp()
    {
        SettingsDocument doc = new(ConfigScope.User, "/tmp/settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc], TestMergePolicy.Inferring);

        JsonObject result = EffectiveConfigBuilder.BuildEffective(ws, "empty");
        Assert.Single(result);
        Assert.Equal("empty", result["//"]!.GetValue<string>());
    }

    // 4.3.7 step 12 — Stamp(JsonObject, string) overload.

    [Fact]
    public void Stamp_StampIsFirstKey_PreservesBodyDeepClone()
    {
        // Caller hands in an already-merged JsonObject (typically from
        // SDK.ComputeEffectiveSnapshot in the GUI's export flow).
        JsonObject effective = new() { ["theme"] = "dark", ["autoSave"] = true };

        JsonObject result = EffectiveConfigBuilder.Stamp(effective, "ClaudeForge GUI v1.2");

        List<string> keys = result.Select(kv => kv.Key).ToList();
        MessageAssert.Equal("//", keys[0], "The '//' stamp must be the first key.");
        Assert.Equal("ClaudeForge GUI v1.2", result["//"]!.GetValue<string>());
        Assert.Equal("dark", result["theme"]!.GetValue<string>());
        Assert.True(result["autoSave"]!.GetValue<bool>());
    }

    [Fact]
    public void Stamp_DoesNotMutateInputJsonObject()
    {
        JsonObject effective = new() { ["theme"] = "dark" };
        JsonObject result = EffectiveConfigBuilder.Stamp(effective, "stamp");

        MessageAssert.NotSame(effective, result, "Stamp must return a fresh JsonObject.");
        Assert.False(effective.ContainsKey("//"),
            "Original input must not gain a '//' key — Stamp deep-clones into a new object.");
    }

    [Fact]
    public void Stamp_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EffectiveConfigBuilder.Stamp(null!, "stamp"));
    }
}