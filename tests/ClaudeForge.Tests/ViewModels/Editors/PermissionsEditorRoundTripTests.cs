using System.Text.Json;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// regression tests for the Permissions editor's preservation
/// of sub-fields the editor doesn't natively render
/// (<c>disableBypassPermissionsMode</c>, <c>additionalDirectories</c>,
/// future schema additions).
/// </summary>
/// <remarks>
/// Same bug class as the McpServer description fix: the editor's
/// <c>ToJsonValue</c> built a fresh JsonObject containing only modeled
/// fields, dropping unknowns on every save flush. Fix: capture
/// non-modeled keys during <c>LoadFromLayered</c>, replay them during
/// <c>ToJsonValue</c>.
/// </remarks>
[TestClass]
public sealed class PermissionsEditorRoundTripTests
{
    private static SchemaNode PermissionsSchema()
    {
        return new SchemaNode("permissions", "permissions") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWith(JsonObject obj)
    {
        ScopeEntry entry = new(ConfigScope.User, obj, "/fake");
        return new LayeredValue("permissions", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = ConfigScope.User,
        };
    }

    [TestMethod]
    public void RoundTrip_PreservesAdditionalDirectories()
    {
        JsonObject input = new()
        {
            ["defaultMode"] = "default",
            ["allow"] = new JsonArray("Read"),
            ["additionalDirectories"] = new JsonArray("/Users/alice/projects", "~/work"),
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);
        JsonObject output = (JsonObject)vm.ToJsonValue()!;

        Assert.IsTrue(output.ContainsKey("additionalDirectories"),
            "additionalDirectories must round-trip. Output:\n" +
            output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        JsonArray dirs = output["additionalDirectories"]!.AsArray();
        Assert.AreEqual(2, dirs.Count);
        Assert.AreEqual("/Users/alice/projects", dirs[0]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_PreservesDisableBypassPermissionsMode()
    {
        // disableBypassPermissionsMode is now a typed
        // bool property on the editor (was opaque-bag preservation).
        // Schema defines it as boolean; test now uses the correct type.
        JsonObject input = new()
        {
            ["defaultMode"] = "default",
            ["disableBypassPermissionsMode"] = true,
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);
        JsonObject output = (JsonObject)vm.ToJsonValue()!;

        Assert.IsTrue(
            output["disableBypassPermissionsMode"]!.GetValue<bool>());
    }

    [TestMethod]
    public void RoundTrip_TypedPropertyEditsApply_UnknownsPreserved()
    {
        // Lock the contract: the editor's modeled fields ARE the source of
        // truth (user can edit them), and preserved fields are the fallback
        // for unknowns. Both must coexist correctly.
        JsonObject input = new()
        {
            ["defaultMode"] = "default",
            ["allow"] = new JsonArray("Read"),
            ["additionalDirectories"] = new JsonArray("/old/path"),
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        // Simulate user editing the modeled DefaultMode.
        vm.DefaultMode = "acceptEdits";

        JsonObject output = (JsonObject)vm.ToJsonValue()!;
        Assert.AreEqual("acceptEdits", output["defaultMode"]!.GetValue<string>());
        Assert.AreEqual("/old/path",
            output["additionalDirectories"]!.AsArray()[0]!.GetValue<string>());
    }
}