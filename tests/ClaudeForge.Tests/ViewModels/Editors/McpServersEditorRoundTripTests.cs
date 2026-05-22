using System.Text.Json;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// end-to-end test of the McpServers editor's load+flush
/// cycle through the SDK-backed path. User reported that even after
/// shipping the McpServersAccessor PreservedFields fix, descriptions
/// were STILL being dropped on save. This test reproduces the exact
/// editor flow to find the disconnect.
/// </summary>
[TestClass]
public sealed class McpServersEditorRoundTripTests
{
    private static SchemaNode McpServersSchema()
    {
        return new SchemaNode("mcpServers", "mcpServers") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWith(JsonObject obj)
    {
        ScopeEntry entry = new(ConfigScope.User, obj, "/fake");
        return new LayeredValue("mcpServers", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = ConfigScope.User,
        };
    }

    [TestMethod]
    public void SdkBackedFullSaveFlow_PreservesDescriptionField()
    {
        // End-to-end: editor loads, flushes via ToJsonValue, that JSON gets
        // written to workspace via SDK.SetValue. After the full cycle the
        // workspace.Root.mcpServers must still contain the description
        // field. Mirrors what SaveCoreAsync's ApplyToWorkspace does.
        JsonObject input = new()
        {
            ["omega-memory"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "uvx",
                ["args"] = new JsonArray("omega-memory", "serve"),
                ["description"] = "Persistent agent memory",
            },
            ["sequential-thinking"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray("-y", "@modelcontextprotocol/server-sequential-thinking"),
                ["description"] = "Chain-of-thought reasoning",
            },
        };

        JsonObject initialRoot = new() { ["mcpServers"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        // Editor LOAD via SDK path.
        McpServersEditorViewModel vm = new(McpServersSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        // Editor FLUSH (what ApplyToWorkspace does).
        JsonNode? flushed = vm.ToJsonValue();
        Assert.IsNotNull(flushed);
        client.SetValue("mcpServers", flushed!);

        // Inspect workspace.Root AFTER the flush — this is what would be
        // written to disk on save.
        JsonObject afterFlush = doc.Root["mcpServers"]!.AsObject();

        Assert.IsTrue(afterFlush["omega-memory"]!.AsObject().ContainsKey("description"),
            $"omega-memory description must survive load+flush+SetValue.\n\n" +
            $"After flush:\n{afterFlush.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
        Assert.IsTrue(afterFlush["sequential-thinking"]!.AsObject().ContainsKey("description"),
            "sequential-thinking description must survive load+flush+SetValue.");

        Assert.AreEqual("Persistent agent memory",
            afterFlush["omega-memory"]!.AsObject()["description"]!.GetValue<string>());
    }

    [TestMethod]
    public void SdkBackedFullSaveFlow_UserExactStructure_PreservesDescriptions()
    {
        // Five stdio servers, all with description fields.
        JsonObject input = new()
        {
            ["omega-memory"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "uvx",
                ["args"] = new JsonArray("omega-memory", "serve"),
                ["description"] = "Persistent agent memory with semantic search",
            },
            ["sequential-thinking"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray("-y", "@modelcontextprotocol/server-sequential-thinking"),
                ["description"] = "Chain-of-thought reasoning",
            },
            ["context7"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray("-y", "@upstash/context7-mcp@latest"),
                ["description"] = "Live documentation lookup",
            },
            ["insaits"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "python3",
                ["args"] = new JsonArray("-m", "insa_its.mcp_server"),
                ["description"] = "AI-to-AI security monitoring",
            },
            ["token-optimizer"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray("-y", "token-optimizer-mcp"),
                ["description"] = "Token optimization for context reduction",
            },
        };

        JsonObject initialRoot = new() { ["mcpServers"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        McpServersEditorViewModel vm = new(McpServersSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonNode? flushed = vm.ToJsonValue();
        client.SetValue("mcpServers", flushed!);

        JsonObject afterFlush = doc.Root["mcpServers"]!.AsObject();
        foreach (string serverName in new[]
                     { "omega-memory", "sequential-thinking", "context7", "insaits", "token-optimizer" })
        {
            Assert.IsTrue(afterFlush[serverName]!.AsObject().ContainsKey("description"),
                $"Description must survive for {serverName}.");
        }
    }

    [TestMethod]
    public void SdkBackedLoad_ToJsonValue_PreservesDescriptionField()
    {
        // The user's exact scenario simplified: one stdio server with a
        // description. After load+ToJsonValue, the description must
        // appear in the emitted JSON.
        JsonObject input = new()
        {
            ["omega-memory"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "uvx",
                ["args"] = new JsonArray("omega-memory", "serve"),
                ["description"] = "Persistent agent memory",
            },
        };

        // Wrap in workspace + SDK client.
        JsonObject initialRoot = new() { ["mcpServers"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        // Construct editor WITH the SDK client (production path).
        McpServersEditorViewModel vm = new(McpServersSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        // Round-trip: emit the editor's view of the data.
        JsonObject? output = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(output, "ToJsonValue must produce output for non-empty server list.");

        JsonObject server = output!["omega-memory"]!.AsObject();
        Assert.IsTrue(server.ContainsKey("description"),
            $"Description must be preserved through editor round-trip.\n\n" +
            $"Input:\n{input.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}\n\n" +
            $"Output:\n{output.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
        Assert.AreEqual("Persistent agent memory",
            server["description"]!.GetValue<string>());
    }
}