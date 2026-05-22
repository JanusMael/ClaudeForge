using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// regression tests for <see cref="McpServersAccessor"/>'s
/// preservation of fields the SDK doesn't natively model.
/// </summary>
/// <remarks>
/// <para>
/// User-reported bug (manual testing follow-up): every Save
/// silently removed the <c>description</c> property from every MCP
/// server entry, even when the user only edited an unrelated field
/// (e.g. <c>permissions.defaultMode</c>). The mechanism mirrored the
/// hooks-merge bug fixed in commit 835a123: the editor's
/// <c>IsModified=true</c> parity contract makes ApplyToWorkspace flush
/// on every save, and pre-fix the SDK round-trip dropped any field not
/// in the typed <see cref="McpServer"/> record (<c>description</c>,
/// future fields).
/// </para>
/// <para>
/// Fix: <see cref="McpServer.PreservedFields"/> captures unknown
/// fields verbatim during <see cref="McpServersAccessor"/>'s read,
/// re-emitted unchanged on write. The internal property keeps
/// <see cref="JsonObject"/> out of the SDK public API surface.
/// </para>
/// </remarks>
[TestClass]
public sealed class McpServersAccessorRoundTripTests
{
    private static SettingsWorkspace MakeWorkspace(JsonObject mcpServersBlock)
    {
        JsonObject root = new() { ["mcpServers"] = (JsonObject)mcpServersBlock.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", root, isReadOnly: false);
        return new SettingsWorkspace([doc]);
    }

    private static ClaudeCodeClient MakeClient(SettingsWorkspace ws)
    {
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));
    }

    [TestMethod]
    public void Get_ExposesDescriptionAsTypedProperty()
    {
        // The user's exact scenario: a stdio MCP server with a description.
        // the description
        // was opaque-bagged in PreservedFields; it is now a first-class typed
        // property on McpServer. This test pins the post-Stop-A contract:
        // the typed property is populated, and the field is NOT also
        // duplicated in PreservedFields (which would leave two sources of
        // truth and risk drift on Set).
        JsonObject input = new()
        {
            ["omega-memory"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "uvx",
                ["args"] = new JsonArray("omega-memory", "serve"),
                ["description"] = "Persistent agent memory with semantic search",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        McpServer? server = client.McpServers.Get("omega-memory");

        Assert.IsNotNull(server);
        Assert.AreEqual("Persistent agent memory with semantic search", server!.Description);
        Assert.IsTrue(server.PreservedFields is null || !server.PreservedFields.ContainsKey("description"),
            "Typed Description must be the single source of truth — description must not also appear in PreservedFields.");
    }

    [TestMethod]
    public void Set_RoundTrip_PreservesDescriptionField()
    {
        // Get → Set without modification: description must survive intact.
        JsonObject input = new()
        {
            ["omega-memory"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "uvx",
                ["args"] = new JsonArray("omega-memory", "serve"),
                ["description"] = "Persistent agent memory with semantic search",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        McpServer server = client.McpServers.Get("omega-memory")!;
        client.McpServers.Set("omega-memory", server);

        JsonObject output = (JsonObject)client.GetScopeValue("mcpServers", ConfigScope.User)!;
        JsonObject entry = output["omega-memory"]!.AsObject();

        Assert.IsTrue(entry.ContainsKey("description"),
            "After Get→Set round-trip, description must still be on disk.");
        Assert.AreEqual("Persistent agent memory with semantic search",
            entry["description"]!.GetValue<string>());
    }

    [TestMethod]
    public void Set_TypedPropertyTakesPrecedenceOverPreservedField()
    {
        // Defensive: if a preserved field somehow collides with a typed
        // property (e.g. a future SDK update adds Description as a typed
        // property AND a server still has stale PreservedFields with the
        // same key), the typed property must win.
        JsonObject input = new()
        {
            ["s"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "real-command",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        // Construct an McpServer manually with a PreservedFields entry
        // that collides with the typed Command property.
        JsonObject preserved = new() { ["command"] = "stale-command" };
        McpServer server = new("s", McpTransport.Stdio, Command: "real-command")
        {
            PreservedFields = preserved,
        };

        client.McpServers.Set("s", server);

        JsonObject output = (JsonObject)client.GetScopeValue("mcpServers", ConfigScope.User)!;
        JsonObject entry = output["s"]!.AsObject();

        Assert.AreEqual("real-command", entry["command"]!.GetValue<string>(),
            "Typed property must win; preserved field with same key must be skipped.");
    }

    [TestMethod]
    public void RoundTrip_MultipleServersWithDescriptions_PreservesAll()
    {
        // The user's actual config has 5 MCP servers, all with descriptions.
        // The bug dropped ALL of them. This test reproduces the multi-server
        // pattern.
        JsonObject input = new()
        {
            ["a"] = new JsonObject { ["type"] = "stdio", ["command"] = "ca", ["description"] = "desc a" },
            ["b"] = new JsonObject { ["type"] = "stdio", ["command"] = "cb", ["description"] = "desc b" },
            ["c"] = new JsonObject { ["type"] = "stdio", ["command"] = "cc", ["description"] = "desc c" },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        // Materialise everything, then write each back (simulating the
        // editor's load+flush cycle).
        List<KeyValuePair<string, McpServer>> snapshot = client.McpServers.GetAt(ConfigScope.User).ToList();
        foreach ((string name, McpServer server) in snapshot)
        {
            client.McpServers.Set(name, server);
        }

        JsonObject output = (JsonObject)client.GetScopeValue("mcpServers", ConfigScope.User)!;

        Assert.AreEqual("desc a", output["a"]!.AsObject()["description"]!.GetValue<string>());
        Assert.AreEqual("desc b", output["b"]!.AsObject()["description"]!.GetValue<string>());
        Assert.AreEqual("desc c", output["c"]!.AsObject()["description"]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_ArbitraryUnknownFields_AllPreserved()
    {
        // Generalisation: ANY unknown field must round-trip, not just
        // "description". Future schema additions (timeout, retries, etc.)
        // must survive without an SDK code change.
        JsonObject input = new()
        {
            ["s"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "x",
                ["description"] = "a description",
                ["customField"] = 42,
                ["nestedCustom"] = new JsonObject { ["a"] = 1, ["b"] = "two" },
                ["arrayCustom"] = new JsonArray("x", "y", "z"),
                ["futureSchemaAddition"] = "hello",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        McpServer server = client.McpServers.Get("s")!;
        client.McpServers.Set("s", server);

        JsonObject output = client.GetScopeValue("mcpServers", ConfigScope.User)!.AsObject()["s"]!.AsObject();

        Assert.AreEqual("a description", output["description"]!.GetValue<string>());
        Assert.AreEqual(42, output["customField"]!.GetValue<int>());
        Assert.AreEqual("hello", output["futureSchemaAddition"]!.GetValue<string>());
        Assert.AreEqual(1, output["nestedCustom"]!.AsObject()["a"]!.GetValue<int>());
        Assert.AreEqual(3, output["arrayCustom"]!.AsArray().Count);
    }
}