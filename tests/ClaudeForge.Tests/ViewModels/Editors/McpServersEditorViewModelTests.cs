using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class McpServersEditorViewModelTests
{
    private static SchemaNode McpSchema()
    {
        return new SchemaNode("mcpServers", "mcpServers") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWithServers(ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj, "/fake");
        return new LayeredValue("mcpServers", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = scope,
        };
    }

    [TestMethod]
    public void AddServer_AddsToCollection()
    {
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.NewServerName = "my-server";
        vm.AddServerCommand.Execute(null);

        Assert.AreEqual(1, vm.Servers.Count);
        Assert.AreEqual("my-server", vm.Servers[0].Name);
        Assert.AreEqual("my-server", vm.SelectedServer?.Name);
    }

    [TestMethod]
    public void AddServer_NoDuplicateNames()
    {
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.NewServerName = "dup";
        vm.AddServerCommand.Execute(null);
        vm.NewServerName = "dup";
        vm.AddServerCommand.Execute(null);

        Assert.AreEqual(1, vm.Servers.Count);
    }

    [TestMethod]
    public void RemoveServer_RemovesAndUpdateSelection()
    {
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.NewServerName = "a";
        vm.AddServerCommand.Execute(null);
        vm.NewServerName = "b";
        vm.AddServerCommand.Execute(null);

        McpServerEntry serverA = vm.Servers.First(s => s.Name == "a");
        vm.RemoveServerCommand.Execute(serverA);

        Assert.AreEqual(1, vm.Servers.Count);
        Assert.AreEqual("b", vm.Servers[0].Name);
    }

    [TestMethod]
    public void LoadFromLayered_PopulatesServers()
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
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Servers.Count);
        Assert.AreEqual("context7", vm.Servers[0].Name);
        Assert.AreEqual("npx", vm.Servers[0].Command);
        Assert.AreEqual(2, vm.Servers[0].Args.Count);
    }

    [TestMethod]
    public void ToJsonValue_RoundTrips()
    {
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.NewServerName = "myserver";
        vm.AddServerCommand.Execute(null);
        McpServerEntry srv = vm.Servers[0];
        srv.Type = "stdio";
        srv.Command = "node";
        srv.NewArg = "index.js";
        srv.AddArgCommand.Execute(null);

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json);
        Assert.IsNotNull(json!["myserver"]);
        JsonObject? srvJson = json["myserver"] as JsonObject;
        Assert.AreEqual("node", srvJson!["command"]!.GetValue<string>());
        Assert.AreEqual(1, (srvJson["args"] as JsonArray)!.Count);
    }

    // ── Selection preservation across reload ─────────────────────────────
    //
    // same drift class as the Hooks editor's SelectedGroup —
    // workspace.Changed fires during the Save flow's ApplyToWorkspace flush,
    // SettingsGroupEditorViewModel.RebuildEditors → LoadFromLayered, and
    // pre-fix SelectedServer snapped to Servers.FirstOrDefault().
    // Disorienting when authoring multiple servers in a row.

    [TestMethod]
    public void LoadFromLayered_PreservesSelectedServer_AcrossReload()
    {
        // Two servers loaded; user has navigated to the second (i.e., NOT
        // the one FirstOrDefault would pick). Reload must keep them there.
        JsonObject obj = new()
        {
            ["alpha"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "alpha-cmd",
            },
            ["beta"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "beta-cmd",
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        McpServerEntry beta = vm.Servers.First(s => s.Name == "beta");
        vm.SelectedServer = beta;
        Assert.AreEqual("beta", vm.SelectedServer?.Name);

        // Save flow runs → workspace.Changed → RebuildEditors → LoadFromLayered.
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        Assert.IsNotNull(vm.SelectedServer);
        Assert.AreEqual("beta", vm.SelectedServer!.Name,
            "After reload, SelectedServer must remain on the user's previously-selected server, "
            + "not snap back to the first server. Same drift class as the Hooks SelectedGroup fix.");
    }

    [TestMethod]
    public void LoadFromLayered_PicksFirstServer_OnFirstLoad()
    {
        // Counter-test: with no prior selection captured, the historical
        // default still applies — pick the first server.
        JsonObject obj = new()
        {
            ["alpha"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "alpha-cmd",
            },
            ["beta"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "beta-cmd",
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual("alpha", vm.SelectedServer?.Name,
            "First load with no prior selection should pick the first server.");
    }

    [TestMethod]
    public void LoadFromLayered_FallsBack_WhenPriorServerNoLongerExists()
    {
        // Edge: prior selection was a server that's been removed (e.g.
        // external edit deleted it). Selection should fall back to the
        // first remaining server rather than ending up null/stale.
        JsonObject initial = new()
        {
            ["alpha"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "alpha-cmd",
            },
            ["beta"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "beta-cmd",
            },
        };

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, initial), ConfigScope.User);
        vm.SelectedServer = vm.Servers.First(s => s.Name == "beta");

        // beta is gone after external edit.
        JsonObject afterRemoval = new()
        {
            ["alpha"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "alpha-cmd",
            },
        };
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, afterRemoval), ConfigScope.User);

        Assert.AreEqual("alpha", vm.SelectedServer?.Name,
            "When the prior server is gone, selection should fall back to first available, not stay null.");
    }

    [TestMethod]
    public void Reset_ClearsServers()
    {
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.NewServerName = "x";
        vm.AddServerCommand.Execute(null);
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Servers.Count);
        Assert.IsNull(vm.SelectedServer);
    }

    // ── OtherScopesWithData tests ─────────────────────────────────────────────

    private static JsonObject MakeServerObject(string serverName)
    {
        return new JsonObject
        {
            [serverName] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray { "-y", serverName },
            }
        };
    }

    [TestMethod]
    public void OtherScopesWithData_Empty_WhenOnlyEditingScope()
    {
        // Only User scope in layered → no OTHER scopes → empty badge list.
        JsonObject userObj = MakeServerObject("my-server");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, userObj), ConfigScope.User);
        Assert.AreEqual(0, vm.OtherScopesWithData.Count);
    }

    [TestMethod]
    public void OtherScopesWithData_PopulatedFromAdditionalScopes()
    {
        // Both User and Project scopes define MCP servers — Project should appear as a badge.
        JsonObject userObj = MakeServerObject("user-server");
        JsonObject projObj = MakeServerObject("project-server");
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Project, projObj, "/p"),
        ];
        LayeredValue layered = new("mcpServers", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);
        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        // Step 3a: OtherScopesWithData is now IReadOnlyList<IEditorScope>; compare via Id.
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void OtherScopesWithData_DedupesEntriesAtSameScope()
    {
        // Regression: layered.Entries can legitimately contain multiple entries at the
        // same scope (e.g. several ~/.claude/managed-settings.d/*.json drop-ins each
        // defining `mcpServers`). The previous implementation passed those duplicates
        // straight into OtherScopesWithData, so the editor header rendered the same
        // coloured chiclet multiple times in the "Defined in scopes" row. The fix is
        // .Distinct() in McpServersEditorViewModel.LoadFromLayered.
        JsonObject userObj = MakeServerObject("user-server");
        JsonObject managed1 = MakeServerObject("managed-server-1");
        JsonObject managed2 = MakeServerObject("managed-server-2");
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Managed, managed1, "/m1"),
            new ScopeEntry(ConfigScope.Managed, managed2, "/m2"),
        ];
        LayeredValue layered = new("mcpServers", entries)
        {
            EffectiveValue = managed1,
            EffectiveScope = ConfigScope.Managed,
        };
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count,
            "Two Managed-scope entries must collapse into a single Managed badge.");
        Assert.AreEqual("managed", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void OtherScopesWithData_RefreshedAfterReset()
    {
        // Regression: after reset, OtherScopesWithData must reflect the reloaded state,
        // not the pre-reset state (e.g. Project badge must survive the reset).
        JsonObject userObj = MakeServerObject("user-server");
        JsonObject projObj = MakeServerObject("project-server");
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Project, projObj, "/p"),
        ];
        LayeredValue layered = new("mcpServers", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);

        // Add an unsaved server.
        vm.NewServerName = "unsaved-server";
        vm.AddServerCommand.Execute(null);
        Assert.AreEqual(2, vm.Servers.Count, "Setup: both loaded and unsaved server present");

        // Reset → unsaved server is gone; OtherScopesWithData must still show Project badge.
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Servers.Count, "Reset should remove unsaved server");
        Assert.AreEqual(1, vm.OtherScopesWithData.Count,
            "OtherScopesWithData must be refreshed by reset to reflect the loaded state");
        // Step 3a: OtherScopesWithData is now IReadOnlyList<IEditorScope>; compare via Id.
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    // ── IsModified regression tests ────────────────────────────────────────────

    [TestMethod]
    public void LoadFromLayered_SetsIsModified_WhenScopeHasValue()
    {
        // Regression: IsModified must be true after loading when the editing scope has an
        // explicit value — otherwise existing servers can never be saved.
        JsonObject obj = MakeServerObject("my-server");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        Assert.IsTrue(vm.IsModified,
            "IsModified should be true when the scope has an explicit value.");
    }

    [TestMethod]
    public void LoadFromLayered_ClearsIsModified_WhenScopeHasNoValue()
    {
        // Loading with no value at the editing scope should leave IsModified = false.
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("mcpServers", []), ConfigScope.User);

        Assert.IsFalse(vm.IsModified,
            "IsModified should be false when there is no value at the editing scope.");
    }

    [TestMethod]
    public void RemoveAllServers_DoesNotClearIsModified()
    {
        // Regression: removing all servers previously set IsModified = (Servers.Count > 0),
        // which cleared the flag and caused the removal to be silently discarded on save.
        // The fix: user-initiated removals must keep IsModified = true.
        JsonObject obj = MakeServerObject("server-to-remove");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);
        Assert.AreEqual(1, vm.Servers.Count, "Setup: one server loaded");

        vm.RemoveServerCommand.Execute(vm.Servers[0]);

        Assert.AreEqual(0, vm.Servers.Count, "Server should be removed");
        Assert.IsTrue(vm.IsModified,
            "IsModified must stay true after removing all servers so the deletion is persisted.");
    }

    [TestMethod]
    public void RemoveServerAfterLoad_FiresIsModifiedPropertyChanged()
    {
        // Regression: after LoadFromLayered sets IsModified=true (because the scope had
        // an explicit value), a subsequent user-initiated Remove was a no-op assignment
        // (IsModified=true → IsModified=true) which CommunityToolkit.Mvvm's
        // [ObservableProperty] setter elides — no PropertyChanged event was raised, so
        // SettingsGroupEditorViewModel.OnEditorPropertyChanged never re-ran the live
        // workspace-write path. The Save button stayed disabled.
        //
        // The fix in McpServersEditorViewModel's CollectionChanged handler force-fires
        // PropertyChanged(IsModified) on every user mutation when the flag is already
        // true. This test asserts that contract directly.
        JsonObject obj = MakeServerObject("loaded-server");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);
        Assert.IsTrue(vm.IsModified, "Setup: IsModified is already true after load.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.RemoveServerCommand.Execute(vm.Servers[0]);

        Assert.IsTrue(fired >= 1,
            "PropertyChanged(IsModified) must fire on the user remove, even though the " +
            "underlying boolean did not transition false→true. Without this signal the " +
            "live-write path in SettingsGroupEditorViewModel never runs and Save stays " +
            "disabled.");
    }

    [TestMethod]
    public void AddServerAfterLoad_FiresIsModifiedPropertyChanged()
    {
        // Same regression contract as RemoveServerAfterLoad_FiresIsModifiedPropertyChanged,
        // but for the add path — which suffered from the same no-op assignment when
        // loading data that already pre-flipped IsModified.
        JsonObject obj = MakeServerObject("existing");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.NewServerName = "new-one";
        vm.AddServerCommand.Execute(null);

        Assert.IsTrue(fired >= 1,
            "PropertyChanged(IsModified) must fire on the user add, even when IsModified " +
            "was already true from the prior load.");
    }

    // ── Inline-edit + nested-collection contract ────────────────────────────
    //
    // These tests lock the contract that ANY user mutation on a loaded server —
    // including its scalar fields and nested Args/Env/Headers collections —
    // routes through PropertyChanged(IsModified) so the live-write path runs
    // and Save enables. Previously only the outer Servers collection-changed
    // event fired MarkModified, so editing a Command on an existing server
    // silently failed to dirty the editor.

    [TestMethod]
    public void EditingCommandOnLoadedServer_FiresIsModifiedPropertyChanged()
    {
        JsonObject obj = MakeServerObject("loaded");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);
        Assert.AreEqual(1, vm.Servers.Count, "Setup: one server loaded.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Servers[0].Command = "different-command";

        Assert.IsTrue(fired >= 1,
            "Editing the Command on a loaded server must fire PropertyChanged(IsModified). " +
            "Without this contract the user types into the box and Save stays disabled.");
    }

    [TestMethod]
    public void AddingArgToLoadedServer_FiresIsModifiedPropertyChanged()
    {
        JsonObject obj = MakeServerObject("loaded");
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Servers[0].Args.Add(new ArgItem("--new-flag"));

        Assert.IsTrue(fired >= 1,
            "Adding an Arg to a loaded server's nested ObservableCollection must fire " +
            "PropertyChanged(IsModified) via the OnNestedCollectionChanged handler.");
    }

    [TestMethod]
    public void EditingArgValueOnLoadedServer_FiresIsModifiedPropertyChanged()
    {
        // Setup: a server with one pre-existing Arg, loaded from disk.
        JsonObject obj = new()
        {
            ["loaded"] = new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = "npx",
                ["args"] = new JsonArray { "-y", "loaded" },
            },
        };
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, obj), ConfigScope.User);
        Assert.IsTrue(vm.Servers[0].Args.Count >= 1, "Setup: at least one Arg loaded.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        // Edit the Value of an Arg that was loaded from disk (NOT one we just added).
        // This catches the per-existing-item PropertyChanged subscription that the
        // SubscribeEntry helper installs on every nested item already present at
        // entry-add time — earlier implementations only subscribed items added
        // AFTER load via OnNestedCollectionChanged, missing this case entirely.
        vm.Servers[0].Args[0].Value = "edited-value";

        Assert.IsTrue(fired >= 1,
            "Editing an Arg's Value on a server that was loaded from disk must fire " +
            "PropertyChanged(IsModified). The subscription to loaded items must happen " +
            "in SubscribeEntry, not just in OnNestedCollectionChanged.");
    }

    // -----------------------------------------------------------------------
    // SDK-backed read path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor()
    {
        // Divergent SDK vs LayeredValue contents — proves the SDK is the
        // source of truth when the client is supplied.
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            client.McpServers.Set(
                "from-sdk",
                new McpServer(
                    "from-sdk",
                    McpTransport.Stdio,
                    Command: "/usr/bin/node",
                    Args: ["main.js", "--port=3000"]));

            // LayeredValue carries a different server — must be ignored.
            JsonObject divergent = new()
            {
                ["from-layered"] = new JsonObject { ["command"] = "/bin/cat" },
            };
            LayeredValue layered = LayeredWithServers(ConfigScope.User, divergent);

            McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            Assert.AreEqual(1, vm.Servers.Count, "SDK path should yield exactly the SDK-set server.");
            Assert.AreEqual("from-sdk", vm.Servers[0].Name);
            Assert.AreEqual("stdio", vm.Servers[0].Type);
            Assert.AreEqual("/usr/bin/node", vm.Servers[0].Command);
            Assert.AreEqual(2, vm.Servers[0].Args.Count);
            Assert.AreEqual("main.js", vm.Servers[0].Args[0].Value);
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = previousOverride;
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                /* best-effort */
            }
        }
    }

    [TestMethod]
    public void LoadFromLayered_WithoutSdkClient_FallsBackToLegacyJsonPath()
    {
        JsonObject legacy = new()
        {
            ["legacy"] = new JsonObject
            {
                ["command"] = "echo",
                ["args"] = new JsonArray("hello"),
            },
        };
        LayeredValue layered = LayeredWithServers(ConfigScope.User, legacy);

        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User, client: null);
        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.AreEqual(1, vm.Servers.Count);
        Assert.AreEqual("legacy", vm.Servers[0].Name);
        Assert.AreEqual("echo", vm.Servers[0].Command);
        Assert.AreEqual(1, vm.Servers[0].Args.Count);
        Assert.AreEqual("hello", vm.Servers[0].Args[0].Value);
    }

    [TestMethod]
    public void FormatTransport_StreamableHttp_RendersAs_Http_ForCombobox()
    {
        // Regression guard for the asymmetric mapping comment on
        // McpServersEditorViewModel.FormatTransport — the SDK's
        // StreamableHttp must render as the editor's "http" string so the
        // ComboBox bound to McpServerEntry.TransportInfos resolves correctly.
        // Emitting "streamable-http" here would give a blank dropdown.
        // We verify via a round-trip through the SDK rather than reflection.
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-mcp-shttp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            client.OpenAsync(projectRoot: null, ct: CancellationToken.None).GetAwaiter().GetResult();

            client.McpServers.Set("api",
                new McpServer(
                    "api",
                    McpTransport.StreamableHttp,
                    Url: "https://api.example.com"));

            McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(
                LayeredWithServers(ConfigScope.User, new JsonObject()),
                ConfigScope.User);

            Assert.AreEqual(1, vm.Servers.Count);
            Assert.AreEqual("http", vm.Servers[0].Type,
                "StreamableHttp must surface as 'http' for the editor's ComboBox.");
            Assert.AreEqual("https://api.example.com", vm.Servers[0].Url);
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = previousOverride;
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                /* best-effort */
            }
        }
    }

    // ── Force-fire delete-after-load ──────────────

    [TestMethod]
    public void DeleteAfterLoad_FiresIsModified_ForceFireContract()
    {
        // Locks the force-fire contract — see PermissionsEditorViewModelTests
        // for the full rationale.
        JsonObject loaded = new()
        {
            ["alpha"] = new JsonObject { ["command"] = "echo a" },
            ["beta"] = new JsonObject { ["command"] = "echo b" },
        };
        McpServersEditorViewModel vm = new(McpSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithServers(ConfigScope.User, loaded), ConfigScope.User);
        Assert.IsTrue(vm.IsModified, "Precondition: load with two servers flags IsModified=true.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServersEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        // Delete one loaded server — Servers still non-empty so IsModified
        // stays latched true. Without the explicit re-raise the live-write
        // loop never sees the change.
        vm.Servers.RemoveAt(0);

        Assert.IsTrue(fired >= 1,
            "Deleting a loaded server must fire PropertyChanged(IsModified) — locks the force-fire " +
            "contract that was the original symptom on the MCP servers page.");
    }
}