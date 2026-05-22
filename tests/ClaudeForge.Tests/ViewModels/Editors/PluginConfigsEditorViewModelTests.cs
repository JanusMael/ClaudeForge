using PropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the 3-level pluginConfigs editor's contract:
///   • hydrates plugin → server → config string-typed entries from disk
///   • non-string config values (number / boolean / array) preserve
///     opaquely via per-server ExtraConfigs and round-trip unchanged
///   • round-trip emits the schema-correct {plugin: {mcpServers: {server: {key: value}}}} shape
///   • blank ids / names / keys skip on save
///   • Add / Remove flows propagate IsModified through the parent VM
///   • duplicate add is a no-op (we don't silently corrupt by clobbering)
///   • Reset clears everything
/// </summary>
[TestClass]
public class PluginConfigsEditorViewModelTests
{
    private static SchemaNode ComplexSchema(string name = "pluginConfigs")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue Empty(string key = "pluginConfigs")
    {
        return new LayeredValue(key, []);
    }

    private static LayeredValue WithObject(string key, ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj.DeepClone(), "/fake");
        return new LayeredValue(key, [entry])
        {
            EffectiveValue = obj.DeepClone(),
            EffectiveScope = scope,
        };
    }

    private static PluginConfigsEditorViewModel NewVm()
    {
        return new PluginConfigsEditorViewModel(ComplexSchema(), ConfigScope.User);
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initial_Empty_NotModified()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.AreEqual(0, vm.Plugins.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void Hydrate_PluginWithMcpServerWithConfigs_PopulatesAllThreeLevels()
    {
        JsonObject disk = new()
        {
            ["everything-claude-code@anthropic-plugins"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["github"] = new JsonObject
                    {
                        ["githubToken"] = "ghp_xxx",
                        ["defaultRepo"] = "owner/repo",
                    },
                    ["exa"] = new JsonObject
                    {
                        ["exaApiKey"] = "exa_xxx",
                    },
                },
            },
        };

        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);

        Assert.AreEqual(1, vm.Plugins.Count);
        PluginConfigEntryViewModel plugin = vm.Plugins[0];
        Assert.AreEqual("everything-claude-code@anthropic-plugins", plugin.PluginId);
        Assert.AreEqual(2, plugin.Servers.Count);

        PluginServerConfigViewModel gh = plugin.Servers.Single(s => s.ServerName == "github");
        Assert.AreEqual(2, gh.Configs.Count);
        Assert.AreEqual("ghp_xxx", gh.Configs.Single(c => c.Key == "githubToken").Value);
        Assert.AreEqual("owner/repo", gh.Configs.Single(c => c.Key == "defaultRepo").Value);

        PluginServerConfigViewModel exa = plugin.Servers.Single(s => s.ServerName == "exa");
        Assert.AreEqual("exa_xxx", exa.Configs.Single(c => c.Key == "exaApiKey").Value);
    }

    [TestMethod]
    public void RoundTrip_Lossless_ForStringTypedConfigs()
    {
        JsonObject disk = new()
        {
            ["my-plugin@market"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["server-a"] = new JsonObject { ["k1"] = "v1" },
                },
            },
        };

        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        Assert.AreEqual(
            disk.ToJsonString(),
            written.ToJsonString(),
            "Round-trip of string-typed configs must be byte-identical to disk shape.");
    }

    [TestMethod]
    public void NonStringConfigValues_PreservedOpaquely_AcrossRoundTrip()
    {
        // The schema allows number / boolean / array<string> for leaf
        // values. v1 of the editor surfaces only string-typed values; the
        // others must round-trip unchanged via the server's ExtraConfigs.
        JsonObject disk = new()
        {
            ["my-plugin@market"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["multi-typed-server"] = new JsonObject
                    {
                        ["stringValue"] = "abc",
                        ["numericValue"] = 42,
                        ["boolValue"] = true,
                        ["arrayValue"] = new JsonArray { "x", "y", "z" },
                    },
                },
            },
        };

        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);

        PluginServerConfigViewModel server = vm.Plugins.Single().Servers.Single();
        // Only the string-typed config row was surfaced.
        Assert.AreEqual(1, server.Configs.Count);
        Assert.AreEqual("stringValue", server.Configs[0].Key);

        JsonObject writtenServer = (JsonObject)((JsonObject)((JsonObject)vm.ToJsonValue()!)
            ["my-plugin@market"]!)["mcpServers"]!["multi-typed-server"]!;

        // String row replayed.
        Assert.AreEqual("abc", writtenServer["stringValue"]?.GetValue<string>());
        // Non-string opaque values replayed at their original types.
        Assert.AreEqual(42, writtenServer["numericValue"]?.GetValue<int>());
        Assert.IsTrue(writtenServer["boolValue"]?.GetValue<bool>());
        JsonArray arr = (JsonArray)writtenServer["arrayValue"]!;
        CollectionAssert.AreEqual(
            new[] { "x", "y", "z" },
            arr.Select(n => n!.GetValue<string>()).ToArray());
    }

    [TestMethod]
    public void Add_Plugin_AppendsAndFiresModified()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginConfigsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.NewPluginId = "new-plugin@market";
        vm.AddPluginCommand.Execute(null);

        Assert.AreEqual(1, vm.Plugins.Count);
        Assert.AreEqual("new-plugin@market", vm.Plugins[0].PluginId);
        Assert.AreEqual(string.Empty, vm.NewPluginId);
        Assert.IsTrue(fired > 0, "Adding a plugin must fire IsModified.");
    }

    [TestMethod]
    public void Add_Server_FiresModified_ThroughForwarding()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);

        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginConfigsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        plugin.NewServerName = "server-a";
        plugin.AddServerCommand.Execute(null);

        Assert.AreEqual(1, plugin.Servers.Count);
        Assert.IsTrue(fired > 0,
            "Adding a server must propagate IsModified up through the "
            + "plugin's Servers PropertyChanged forwarding.");
    }

    [TestMethod]
    public void Add_Config_FiresModified_ThroughTwoLevelForwarding()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        plugin.NewServerName = "s";
        plugin.AddServerCommand.Execute(null);
        PluginServerConfigViewModel server = plugin.Servers.Single();

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginConfigsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        server.NewConfigKey = "apiKey";
        server.NewConfigValue = "secret";
        server.AddConfigCommand.Execute(null);

        Assert.AreEqual(1, server.Configs.Count);
        Assert.IsTrue(fired > 0,
            "Adding a config row must propagate IsModified all the way up "
            + "through server's Configs → plugin's Servers → editor's MarkModified.");
    }

    [TestMethod]
    public void EditConfigValue_FiresModified_AtAllThreeLevels()
    {
        JsonObject disk = new()
        {
            ["p@m"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["s"] = new JsonObject { ["k"] = "old" },
                },
            },
        };
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginConfigsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Plugins[0].Servers[0].Configs[0].Value = "new";
        Assert.IsTrue(fired > 0);

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        Assert.AreEqual("new",
            ((JsonObject)((JsonObject)((JsonObject)written["p@m"]!)["mcpServers"]!)["s"]!)["k"]?.GetValue<string>());
    }

    [TestMethod]
    public void Add_DuplicatePluginId_NoOps()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        Assert.AreEqual(1, vm.Plugins.Count);
    }

    [TestMethod]
    public void Add_DuplicateServerName_NoOps()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);

        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        plugin.NewServerName = "s";
        plugin.AddServerCommand.Execute(null);
        plugin.NewServerName = "s";
        plugin.AddServerCommand.Execute(null);
        Assert.AreEqual(1, plugin.Servers.Count);
    }

    [TestMethod]
    public void Add_DuplicateConfigKey_OverwritesValue()
    {
        // Different from plugin / server: a duplicate config key is a
        // legitimate "update the value" gesture (matches StringMap and
        // headers semantics).
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        plugin.NewServerName = "s";
        plugin.AddServerCommand.Execute(null);
        PluginServerConfigViewModel server = plugin.Servers.Single();

        server.NewConfigKey = "k";
        server.NewConfigValue = "first";
        server.AddConfigCommand.Execute(null);

        server.NewConfigKey = "k";
        server.NewConfigValue = "second";
        server.AddConfigCommand.Execute(null);

        Assert.AreEqual(1, server.Configs.Count);
        Assert.AreEqual("second", server.Configs[0].Value);
    }

    [TestMethod]
    public void BlankPluginId_SkippedOnSave()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "real-plugin@m";
        vm.AddPluginCommand.Execute(null);
        // Direct mutation: simulate a blanked id.
        vm.Plugins[0].PluginId = string.Empty;

        Assert.IsNull(vm.ToJsonValue(),
            "All-blank-pluginId entries reduce to an empty map and the "
            + "editor returns null (RemoveValue) rather than `{}`.");
    }

    [TestMethod]
    public void BlankServerName_SkippedOnSave()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        plugin.NewServerName = "real-server";
        plugin.AddServerCommand.Execute(null);
        plugin.Servers[0].ServerName = string.Empty;

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        JsonObject mcp = (JsonObject)((JsonObject)written["p@m"]!)["mcpServers"]!;
        Assert.AreEqual(0, mcp.Count,
            "Blank server name must be skipped — schema requires a non-empty key.");
    }

    [TestMethod]
    public void BlankConfigKey_SkippedOnSave()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewPluginId = "p@m";
        vm.AddPluginCommand.Execute(null);
        PluginConfigEntryViewModel plugin = vm.Plugins.Single();
        plugin.NewServerName = "s";
        plugin.AddServerCommand.Execute(null);
        PluginServerConfigViewModel server = plugin.Servers.Single();
        server.NewConfigKey = "real-key";
        server.NewConfigValue = "v";
        server.AddConfigCommand.Execute(null);
        server.Configs[0].Key = string.Empty;

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        JsonObject serverObj = (JsonObject)((JsonObject)((JsonObject)written["p@m"]!)["mcpServers"]!)["s"]!;
        Assert.AreEqual(0, serverObj.Count,
            "Blank config key must be skipped — would round-trip as `\"\":\"v\"`.");
    }

    [TestMethod]
    public void Remove_AtEachLevel_PropagatesAndUpdatesOnDiskShape()
    {
        JsonObject disk = new()
        {
            ["p1@m"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["s1"] = new JsonObject { ["k"] = "v" },
                    ["s2"] = new JsonObject { ["k"] = "v" },
                },
            },
            ["p2@m"] = new JsonObject { ["mcpServers"] = new JsonObject() },
        };
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);

        // Remove a server from p1.
        PluginConfigEntryViewModel p1 = vm.Plugins.Single(p => p.PluginId == "p1@m");
        PluginServerConfigViewModel s1 = p1.Servers.Single(s => s.ServerName == "s1");
        p1.RemoveServerCommand.Execute(s1);
        Assert.AreEqual(1, p1.Servers.Count);

        // Remove a plugin (p2 — no servers).
        vm.RemovePluginCommand.Execute(vm.Plugins.Single(p => p.PluginId == "p2@m"));

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        Assert.AreEqual(1, written.Count);
        Assert.IsTrue(written.ContainsKey("p1@m"));
        Assert.IsFalse(written.ContainsKey("p2@m"));

        JsonObject p1Servers = (JsonObject)((JsonObject)written["p1@m"]!)["mcpServers"]!;
        Assert.AreEqual(1, p1Servers.Count);
        Assert.IsTrue(p1Servers.ContainsKey("s2"));
    }

    [TestMethod]
    public void BareStringScope_HydratesEmpty_NoCrash()
    {
        // Repro of the modelOverrides-style on-disk corruption: the
        // schema demands an object, on-disk has a bare string. Editor
        // must tolerate without crashing.
        ScopeEntry entry = new(ConfigScope.User, JsonValue.Create("test"), "/fake");
        LayeredValue bad = new("pluginConfigs", [entry])
        {
            EffectiveValue = JsonValue.Create("test"),
            EffectiveScope = ConfigScope.User,
        };

        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(bad, ConfigScope.User);

        Assert.AreEqual(0, vm.Plugins.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void Reset_AfterLoad_RestoresOnDiskPlugins_NotClearsThem()
    {
        // Reset semantic consistency.  See
        // McpServerListEditorViewModelTests for the rationale and pattern.
        JsonObject disk = new()
        {
            ["p@m"] = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["s"] = new JsonObject { ["k"] = "v" },
                },
            },
        };
        PluginConfigsEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithObject("pluginConfigs", ConfigScope.User, disk), ConfigScope.User);
        Assert.AreEqual(1, vm.Plugins.Count, "precondition: load populated 1 plugin");
        vm.NewPluginId = "buffered";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Plugins.Count,
            "Reset must restore the at-load plugin entry, not wipe to empty.");
        Assert.AreEqual("p@m", vm.Plugins[0].PluginId);
        Assert.AreEqual(string.Empty, vm.NewPluginId, "Reset clears transient input.");
    }

    [TestMethod]
    public void Reset_WithoutPriorLoad_FallsBackToClear()
    {
        PluginConfigsEditorViewModel vm = NewVm();
        vm.IsModified = true;
        vm.NewPluginId = "buffered";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Plugins.Count);
        Assert.AreEqual(string.Empty, vm.NewPluginId);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    // -----------------------------------------------------------------------
    // Factory dispatch (ensures pluginConfigs routes to the new typed
    // editor, not the JsonRaw fallback)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Factory_DispatchesPluginConfigsToTypedEditor()
    {
        SchemaNode schema = new("pluginConfigs", "pluginConfigs")
        {
            ValueType = SchemaValueType.Complex,
        };
        PropertyEditorViewModel vm = PropertyEditorFactory.Create(schema, ConfigScope.User);
        Assert.IsInstanceOfType<PluginConfigsEditorViewModel>(vm);
    }
}