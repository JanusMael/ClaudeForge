namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Shape-tolerance tests for <see cref="McpServersEditorViewModel"/> covering both
/// Claude Desktop (<c>{command, args, env}</c>) and Claude Code (<c>{type, command|url, headers}</c>)
/// variants, plus a pass-through bag for unknown fields so saves don't drop data.
/// </summary>
[TestClass]
public class McpServersRoundTripTests
{
    private static SchemaNode McpSchema()
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
    public void DesktopShape_NoTypeField_DefaultsToStdio()
    {
        // Claude Desktop's claude_desktop_config.json typically omits "type":
        //   { "my-server": { "command": "npx", "args": ["-y", "@foo/bar"] } }
        JsonObject obj = new()
        {
            ["my-server"] = new JsonObject
            {
                ["command"] = "npx",
                ["args"] = new JsonArray { "-y", "@foo/bar" },
                ["env"] = new JsonObject { ["FOO"] = "bar" },
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Servers.Count);
        McpServerEntry srv = vm.Servers[0];
        Assert.AreEqual("my-server", srv.Name);
        Assert.AreEqual("stdio", srv.Type, "Missing type must default to stdio.");
        Assert.AreEqual("npx", srv.Command);
        Assert.AreEqual(2, srv.Args.Count);
        Assert.AreEqual(1, srv.Env.Count);
        Assert.AreEqual("FOO", srv.Env[0].Key);
    }

    [TestMethod]
    public void CodeShape_ExplicitStdio_RoundTrips()
    {
        JsonObject obj = new()
        {
            ["context7"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray { "-y", "@context7/mcp-server" },
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(emitted);
        JsonObject? srvJson = emitted!["context7"] as JsonObject;
        Assert.IsNotNull(srvJson);
        Assert.AreEqual("stdio", srvJson!["type"]?.GetValue<string>());
        Assert.AreEqual("npx", srvJson["command"]?.GetValue<string>());
        Assert.AreEqual(2, (srvJson["args"] as JsonArray)!.Count);
    }

    [TestMethod]
    public void CodeShape_SseWithUrlAndHeaders()
    {
        JsonObject obj = new()
        {
            ["remote-sse"] = new JsonObject
            {
                ["type"] = "sse",
                ["url"] = "https://example.com/sse",
                ["headers"] = new JsonObject
                {
                    ["Authorization"] = "Bearer xyz",
                    ["X-Custom"] = "v1",
                },
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Servers.Count);
        McpServerEntry srv = vm.Servers[0];
        Assert.AreEqual("sse", srv.Type);
        Assert.AreEqual("https://example.com/sse", srv.Url);
        Assert.AreEqual(2, srv.Headers.Count);

        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        JsonObject? srvJson = emitted!["remote-sse"] as JsonObject;
        Assert.AreEqual("sse", srvJson!["type"]?.GetValue<string>());
        Assert.AreEqual("https://example.com/sse", srvJson["url"]?.GetValue<string>());
        JsonObject? headers = srvJson["headers"] as JsonObject;
        Assert.IsNotNull(headers);
        Assert.AreEqual("Bearer xyz", headers!["Authorization"]?.GetValue<string>());
    }

    [TestMethod]
    public void CodeShape_HttpType()
    {
        JsonObject obj = new()
        {
            ["remote-http"] = new JsonObject
            {
                ["type"] = "http",
                ["url"] = "https://example.com/api",
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Servers.Count);
        Assert.AreEqual("http", vm.Servers[0].Type);
        Assert.AreEqual("https://example.com/api", vm.Servers[0].Url);
    }

    [TestMethod]
    public void UnknownFields_RoundTripThroughPassThroughBag()
    {
        // Forward-compat: any unknown key (e.g., a new "timeout" field) must survive
        // a load → save cycle so users don't lose data when opening in an older GUI.
        JsonObject obj = new()
        {
            ["weird"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "node",
                ["timeout"] = 30000,
                ["experimental"] = new JsonObject { ["flag"] = true },
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        JsonObject? srvJson = emitted!["weird"] as JsonObject;
        Assert.AreEqual(30000, srvJson!["timeout"]?.GetValue<int>());
        Assert.IsNotNull(srvJson["experimental"] as JsonObject);
        Assert.IsTrue((srvJson["experimental"] as JsonObject)!["flag"]?.GetValue<bool>());
    }

    [TestMethod]
    public void MultipleServers_AllRender()
    {
        JsonObject obj = new()
        {
            ["alpha"] = new JsonObject { ["command"] = "a" },
            ["beta"] = new JsonObject { ["command"] = "b" },
            ["gamma"] = new JsonObject { ["command"] = "c" },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        Assert.AreEqual(3, vm.Servers.Count);
        CollectionAssert.AreEquivalent(
            new[] { "alpha", "beta", "gamma" },
            vm.Servers.Select(s => s.Name).ToArray());
    }
}