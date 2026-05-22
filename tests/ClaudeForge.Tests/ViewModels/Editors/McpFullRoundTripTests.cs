// (MCP): comprehensive disk → SDK →
// editor → user-mutation → SDK → disk round-trip suite for the MCP
// servers editor.  Mirrors HooksFullRoundTripTests' fixture pattern.

using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class McpFullRoundTripTests
{
    // ─── Fixture ─────────────────────────────────────────────────────────────

    private sealed class McpFixture : IDisposable
    {
        public SettingsDocument Doc { get; }
        public SettingsWorkspace Workspace { get; }
        public ClaudeCodeClient Client { get; }
        public McpServersEditorViewModel Editor { get; private set; } = null!;

        private McpFixture(SettingsDocument doc, SettingsWorkspace ws, ClaudeCodeClient client)
        {
            Doc = doc;
            Workspace = ws;
            Client = client;
        }

        public static McpFixture From(JsonObject? mcpServers)
        {
            JsonObject rootObj = new();
            if (mcpServers is not null)
            {
                rootObj["mcpServers"] = mcpServers.DeepClone();
            }

            SettingsDocument doc = new(ConfigScope.User, "settings.json", rootObj, isReadOnly: false);
            SettingsWorkspace ws = new([doc]);
            ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
                ws, ConfigScope.User,
                new SchemaRegistry(new HttpClient()));
            McpFixture fx = new(doc, ws, client);
            fx.RebuildEditor();
            return fx;
        }

        private void RebuildEditor()
        {
            Editor = new McpServersEditorViewModel(McpSchema(), ConfigScope.User, Client);
            Editor.LoadFromLayered(BuildLayered(Doc.Root["mcpServers"]), ConfigScope.User);
        }

        public void SaveAndReload()
        {
            JsonNode? emitted = Editor.ToJsonValue();
            if (emitted is not null)
            {
                Client.SetValue("mcpServers", emitted, ConfigScope.User);
            }
            else
            {
                Client.RemoveValue("mcpServers", ConfigScope.User);
            }

            RebuildEditor();
        }

        public McpServerEntry Server(string name)
        {
            return Editor.Servers.First(s => s.Name == name);
        }

        public int ServerCount => Editor.Servers.Count;

        public McpServerEntry AddServer(string name)
        {
            Editor.NewServerName = name;
            Editor.AddServerCommand.Execute(null);
            return Editor.Servers.First(s => s.Name == name);
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }

    private static SchemaNode McpSchema()
    {
        return new SchemaNode("mcpServers", "mcpServers") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue BuildLayered(JsonNode? mcpNode)
    {
        if (mcpNode is null)
        {
            return new LayeredValue("mcpServers", []);
        }

        ScopeEntry entry = new(ConfigScope.User, mcpNode, "/fake");
        return new LayeredValue("mcpServers", [entry])
        {
            EffectiveValue = mcpNode,
            EffectiveScope = ConfigScope.User,
        };
    }

    // ─── JSON builders ──────────────────────────────────────────────────────

    private static JsonObject StdioServer(
        string name,
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyDictionary<string, JsonNode?>? extras = null)
    {
        JsonObject inner = new() { ["command"] = command };
        if (args is { Count: > 0 } a)
        {
            JsonArray arr = new();
            foreach (string v in a)
            {
                arr.Add(v);
            }

            inner["args"] = arr;
        }

        if (env is { Count: > 0 } e)
        {
            JsonObject eo = new();
            foreach ((string k, string v) in e)
            {
                eo[k] = v;
            }

            inner["env"] = eo;
        }

        if (extras is not null)
        {
            foreach ((string k, JsonNode? v) in extras)
            {
                inner[k] = v?.DeepClone();
            }
        }

        return new JsonObject { [name] = inner };
    }

    private static JsonObject HttpServer(
        string name,
        string url,
        string? transportType = "http",
        IReadOnlyDictionary<string, string>? headers = null)
    {
        JsonObject inner = new() { ["url"] = url };
        if (transportType is not null)
        {
            inner["type"] = transportType;
        }

        if (headers is { Count: > 0 } h)
        {
            JsonObject ho = new();
            foreach ((string k, string v) in h)
            {
                ho[k] = v;
            }

            inner["headers"] = ho;
        }

        return new JsonObject { [name] = inner };
    }

    private static JsonObject InnerOnDisk(SettingsDocument doc, string serverName)
    {
        return doc.Root["mcpServers"]!.AsObject()[serverName]!.AsObject();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: stdio (× mutation)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Stdio_AddNewServer_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(null);
        McpServerEntry entry = fx.AddServer("alpha");
        entry.Type = "stdio";
        entry.Command = "/usr/bin/node";
        entry.Args.Add(new ArgItem("main.js"));

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.ServerCount);
        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual("stdio", reloaded.Type);
        Assert.AreEqual("/usr/bin/node", reloaded.Command);
        Assert.AreEqual(1, reloaded.Args.Count);
        Assert.AreEqual("main.js", reloaded.Args[0].Value);
    }

    [TestMethod]
    public void Stdio_EditCommand_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "/old/path"));
        fx.Server("alpha").Command = "/new/path";

        fx.SaveAndReload();

        Assert.AreEqual("/new/path", fx.Server("alpha").Command);
    }

    [TestMethod]
    public void Stdio_AddArg_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "/usr/bin/node",
            args: ["main.js"]));
        McpServerEntry entry = fx.Server("alpha");
        entry.NewArg = "--port=3000";
        entry.AddArgCommand.Execute(null);

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual(2, reloaded.Args.Count);
        CollectionAssert.AreEquivalent(
            new[] { "main.js", "--port=3000" },
            reloaded.Args.Select(a => a.Value).ToList());
    }

    [TestMethod]
    public void Stdio_RemoveArg_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            args: ["a.js", "b.js"]));
        McpServerEntry entry = fx.Server("alpha");
        ArgItem argA = entry.Args.First(a => a.Value == "a.js");
        entry.RemoveArgCommand.Execute(argA);

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual(1, reloaded.Args.Count);
        Assert.AreEqual("b.js", reloaded.Args[0].Value);
    }

    [TestMethod]
    public void Stdio_EditExistingArgValue_RoundTrips()
    {
        // Edits the typed Value of an existing ArgItem row — exercises the
        // per-nested-item PropertyChanged subscription wired by SubscribeEntry.
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node", args: ["old.js"]));
        McpServerEntry entry = fx.Server("alpha");
        entry.Args[0].Value = "new.js";

        fx.SaveAndReload();

        Assert.AreEqual("new.js", fx.Server("alpha").Args[0].Value);
    }

    [TestMethod]
    public void Stdio_AddEnv_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node"));
        McpServerEntry entry = fx.Server("alpha");
        entry.NewEnvKey = "API_KEY";
        entry.NewEnvValue = "abc";
        entry.AddEnvCommand.Execute(null);

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual(1, reloaded.Env.Count);
        Assert.AreEqual("API_KEY", reloaded.Env[0].Key);
        Assert.AreEqual("abc", reloaded.Env[0].Value);
    }

    [TestMethod]
    public void Stdio_EditExistingEnvValue_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            env: new Dictionary<string, string> { ["TOKEN"] = "old" }));
        fx.Server("alpha").Env[0].Value = "new";

        fx.SaveAndReload();

        Assert.AreEqual("new", fx.Server("alpha").Env[0].Value);
    }

    [TestMethod]
    public void Stdio_EditExistingEnvKey_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            env: new Dictionary<string, string> { ["OLD_KEY"] = "v" }));
        fx.Server("alpha").Env[0].Key = "NEW_KEY";

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual(1, reloaded.Env.Count);
        Assert.AreEqual("NEW_KEY", reloaded.Env[0].Key);
        Assert.AreEqual("v", reloaded.Env[0].Value);
    }

    [TestMethod]
    public void Stdio_RemoveEnv_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            env: new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }));
        McpServerEntry entry = fx.Server("alpha");
        EnvVar envA = entry.Env.First(e => e.Key == "A");
        entry.RemoveEnvCommand.Execute(envA);

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("alpha");
        Assert.AreEqual(1, reloaded.Env.Count);
        Assert.AreEqual("B", reloaded.Env[0].Key);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Variant: HTTP (transport-typed) (× mutation)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Http_AddNewServer_WithUrlAndHeader_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(null);
        McpServerEntry entry = fx.AddServer("api");
        entry.Type = "http";
        entry.Url = "https://api.example.com";
        entry.Headers.Add(new EnvVar("Authorization", "Bearer xyz"));

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.ServerCount);
        McpServerEntry reloaded = fx.Server("api");
        Assert.AreEqual("http", reloaded.Type);
        Assert.AreEqual("https://api.example.com", reloaded.Url);
        Assert.AreEqual(1, reloaded.Headers.Count);
        Assert.AreEqual("Authorization", reloaded.Headers[0].Key);
        Assert.AreEqual("Bearer xyz", reloaded.Headers[0].Value);
    }

    [TestMethod]
    public void Http_EditUrl_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(HttpServer("api", "https://old.example.com"));
        fx.Server("api").Url = "https://new.example.com";

        fx.SaveAndReload();

        Assert.AreEqual("https://new.example.com", fx.Server("api").Url);
    }

    [TestMethod]
    public void Http_EditExistingHeaderValue_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(HttpServer("api", "https://x",
            headers: new Dictionary<string, string> { ["A"] = "old" }));
        fx.Server("api").Headers[0].Value = "new";

        fx.SaveAndReload();

        Assert.AreEqual("new", fx.Server("api").Headers[0].Value);
    }

    [TestMethod]
    public void Http_RemoveHeader_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(HttpServer("api", "https://x",
            headers: new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" }));
        McpServerEntry entry = fx.Server("api");
        EnvVar hdrA = entry.Headers.First(h => h.Key == "A");
        entry.Headers.Remove(hdrA);

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("api");
        Assert.AreEqual(1, reloaded.Headers.Count);
        Assert.AreEqual("B", reloaded.Headers[0].Key);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Cross-variant: transport change (stdio ↔ http)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void ChangeTransportFromStdioToHttp_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("api", "/usr/bin/node",
            args: ["main.js"]));
        McpServerEntry entry = fx.Server("api");
        entry.Type = "http";
        entry.Url = "https://api.example.com";
        // Args / Command are stale post-transport-change; the editor doesn't
        // auto-strip but ToJson should respect the typed Type discriminator.

        fx.SaveAndReload();

        McpServerEntry reloaded = fx.Server("api");
        Assert.AreEqual("http", reloaded.Type);
        Assert.AreEqual("https://api.example.com", reloaded.Url);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Description (typed, Stop A surface)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void EditDescription_RoundTrips()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            extras: new Dictionary<string, JsonNode?> { ["description"] = "old" }));
        McpServerEntry entry = fx.Server("alpha");
        Assert.AreEqual("old", entry.Description, "precondition: description loaded");
        entry.Description = "new";

        fx.SaveAndReload();

        Assert.AreEqual("new", fx.Server("alpha").Description);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PreservedFields — replay across multiple round-trips
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void PreservedFields_UnknownSubKeys_SurviveSingleRoundTrip()
    {
        // The SDK does not model arbitrary future server-config keys.  A
        // user with `customExtension: { ... }` set must not lose it on
        // save.
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            extras: new Dictionary<string, JsonNode?>
            {
                ["customExtension"] = new JsonObject { ["x"] = "y" },
            }));
        // Mutate an unrelated typed field.
        fx.Server("alpha").Command = "node-new";

        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "alpha");
        Assert.IsTrue(inner.ContainsKey("customExtension"),
            "PreservedFields must replay 'customExtension' on save.");
        JsonObject ext = inner["customExtension"]!.AsObject();
        Assert.AreEqual("y", ext["x"]!.GetValue<string>());
    }

    [TestMethod]
    public void PreservedFields_UnknownSubKeys_SurviveDoubleRoundTrip()
    {
        // The 2026-04-30 PreservedFields-replay bug class — fields survive
        // ONE round-trip but vanish on the SECOND.
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node",
            extras: new Dictionary<string, JsonNode?>
            {
                ["customExtension"] = new JsonObject { ["x"] = "y" },
            }));
        fx.Server("alpha").Command = "node-new";
        fx.SaveAndReload();

        // Second mutation + reload.
        fx.Server("alpha").Command = "node-newer";
        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "alpha");
        Assert.IsTrue(inner.ContainsKey("customExtension"),
            "PreservedFields must SURVIVE a SECOND round-trip — this is the 2026-04-30 bug class.");
        Assert.AreEqual("y", inner["customExtension"]!.AsObject()["x"]!.GetValue<string>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Multiple servers
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void RemoveServer_DropsFromOnDisk_OthersUnaffected()
    {
        JsonObject loaded = new()
        {
            ["alpha"] = new JsonObject { ["command"] = "node-a" },
            ["beta"] = new JsonObject { ["command"] = "node-b" },
        };
        using McpFixture fx = McpFixture.From(loaded);
        Assert.AreEqual(2, fx.ServerCount);

        fx.Editor.RemoveServerCommand.Execute(fx.Server("alpha"));

        fx.SaveAndReload();

        Assert.AreEqual(1, fx.ServerCount);
        Assert.AreEqual("beta", fx.Editor.Servers[0].Name);
        JsonObject disk = fx.Doc.Root["mcpServers"]!.AsObject();
        Assert.IsFalse(disk.ContainsKey("alpha"));
        Assert.IsTrue(disk.ContainsKey("beta"));
    }

    [TestMethod]
    public void RemoveLastServer_DropsMcpServersKeyFromOnDisk()
    {
        using McpFixture fx = McpFixture.From(StdioServer("alpha", "node"));
        fx.Editor.RemoveServerCommand.Execute(fx.Server("alpha"));

        fx.SaveAndReload();

        // ToJsonValue returns null when no servers remain → live-write
        // RemoveValue → workspace document loses the "mcpServers" key.
        Assert.IsFalse(fx.Doc.Root.ContainsKey("mcpServers"),
            "Removing the last server must drop the entire 'mcpServers' key from disk.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Empty containers contract
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void Stdio_EmptyArgsArray_OmittedFromOnDisk()
    {
        // Server with no args — the on-disk shape must NOT emit `"args": []`
        // (would be schema-invalid noise).
        using McpFixture fx = McpFixture.From(null);
        McpServerEntry entry = fx.AddServer("alpha");
        entry.Type = "stdio";
        entry.Command = "node";

        fx.SaveAndReload();

        JsonObject inner = InnerOnDisk(fx.Doc, "alpha");
        Assert.IsFalse(inner.ContainsKey("args"),
            "Empty args array MUST NOT appear in on-disk JSON.");
        Assert.IsFalse(inner.ContainsKey("env"),
            "Empty env object MUST NOT appear in on-disk JSON.");
    }
}