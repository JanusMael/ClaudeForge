using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// regression tests for <see cref="MarketplacesAccessor"/>'s
/// preservation of fields the SDK doesn't natively model. Same bug class
/// as the McpServersAccessor fix in commit e39d97d.
/// </summary>
[TestClass]
public sealed class MarketplacesAccessorRoundTripTests
{
    private static SettingsWorkspace MakeWorkspace(JsonObject marketplacesBlock)
    {
        JsonObject root = new() { ["extraKnownMarketplaces"] = (JsonObject)marketplacesBlock.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", root, isReadOnly: false);
        return new SettingsWorkspace([doc]);
    }

    private static ClaudeCodeClient MakeClient(SettingsWorkspace ws)
    {
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));
    }

    [TestMethod]
    public void Get_PreservesOuterDescriptionField()
    {
        JsonObject input = new()
        {
            ["everything-claude-code"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "github",
                    ["repository"] = "affaan-m/everything-claude-code",
                },
                ["description"] = "Plugin marketplace for ECC",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        MarketplaceEntry? entry = client.Marketplaces.Get("everything-claude-code");
        Assert.IsNotNull(entry);
        Assert.IsNotNull(entry!.PreservedFields);
        Assert.AreEqual("Plugin marketplace for ECC",
            entry.PreservedFields!["description"]!.GetValue<string>());
    }

    [TestMethod]
    public void Set_RoundTrip_PreservesDescriptionField()
    {
        JsonObject input = new()
        {
            ["mp1"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "github",
                    ["repository"] = "user/repo",
                },
                ["description"] = "A description",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        MarketplaceEntry entry = client.Marketplaces.Get("mp1")!;
        client.Marketplaces.Set(entry);

        JsonObject output = (JsonObject)client.GetScopeValue("extraKnownMarketplaces", ConfigScope.User)!;
        JsonObject mp = output["mp1"]!.AsObject();
        Assert.IsTrue(mp.ContainsKey("description"),
            "Description must round-trip. Output:\n" +
            mp.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.AreEqual("A description", mp["description"]!.GetValue<string>());
    }

    [TestMethod]
    public void Set_RoundTrip_PreservesUnknownInnerSourceField()
    {
        // A future schema addition might add fields to the source object
        // (e.g. branch, ref). Verify those round-trip too.
        JsonObject input = new()
        {
            ["mp1"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "github",
                    ["repository"] = "user/repo",
                    ["futureField"] = "value",
                },
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        MarketplaceEntry entry = client.Marketplaces.Get("mp1")!;
        client.Marketplaces.Set(entry);

        JsonObject output = (JsonObject)client.GetScopeValue("extraKnownMarketplaces", ConfigScope.User)!;
        JsonObject sourceObj = output["mp1"]!.AsObject()["source"]!.AsObject();
        Assert.IsTrue(sourceObj.ContainsKey("futureField"));
        Assert.AreEqual("value", sourceObj["futureField"]!.GetValue<string>());
    }
}