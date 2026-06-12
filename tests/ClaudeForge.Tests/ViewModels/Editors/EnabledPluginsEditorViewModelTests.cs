using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class EnabledPluginsEditorViewModelTests
{
    private static SchemaNode PluginsSchema()
    {
        return new SchemaNode("enabledPlugins", "enabledPlugins") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWithPlugins(ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj, "/fake");
        return new LayeredValue("enabledPlugins", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = scope,
        };
    }

    // -----------------------------------------------------------------------
    // LoadFromLayered
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LoadFromLayered_EmptyObject_LeavesPluginsEmpty()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, new JsonObject()), ConfigScope.User);

        Assert.AreEqual(0, vm.Plugins.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromLayered_PopulatesEnabledAndDisabledPlugins()
    {
        JsonObject obj = new()
        {
            ["a@m"] = true,
            ["b@m"] = false,
        };

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(2, vm.Plugins.Count);

        PluginEntry pluginA = vm.Plugins.First(p => p.PluginRef == "a@m");
        PluginEntry pluginB = vm.Plugins.First(p => p.PluginRef == "b@m");

        Assert.IsTrue(pluginA.Enabled);
        Assert.IsFalse(pluginB.Enabled);
        Assert.IsTrue(vm.IsModified);
    }

    // -----------------------------------------------------------------------
    // AddPlugin
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AddPlugin_AddsEntryWithEnabledTrue()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.NewPluginRef = "p@market";
        vm.AddPluginCommand.Execute(null);

        Assert.AreEqual(1, vm.Plugins.Count);
        Assert.AreEqual("p@market", vm.Plugins[0].PluginRef);
        Assert.IsTrue(vm.Plugins[0].Enabled);
    }

    [TestMethod]
    public void AddPlugin_DoesNotAddEmpty()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.NewPluginRef = "  ";
        vm.AddPluginCommand.Execute(null);

        Assert.AreEqual(0, vm.Plugins.Count);
    }

    [TestMethod]
    public void AddPlugin_DoesNotAddDuplicate()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);

        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        Assert.AreEqual(1, vm.Plugins.Count);
    }

    [TestMethod]
    public void AddPlugin_ClearsNewPluginRef()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.NewPluginRef);
    }

    [TestMethod]
    public void AddPlugin_SetsIsModified()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);

        Assert.IsFalse(vm.IsModified);

        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        Assert.IsTrue(vm.IsModified);
    }

    // -----------------------------------------------------------------------
    // RemovePlugin
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RemovePlugin_RemovesEntry()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        vm.RemovePluginCommand.Execute(vm.Plugins[0]);

        Assert.AreEqual(0, vm.Plugins.Count);
    }

    // -----------------------------------------------------------------------
    // ResetToInherited — regression test for the HIGH bug that was fixed
    // -----------------------------------------------------------------------

    [TestMethod]
    public void OnResetToInherited_ClearsPlugins_AndSetsIsModifiedFalse()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.NewPluginRef = "p@m";
        vm.AddPluginCommand.Execute(null);

        Assert.IsTrue(vm.IsModified, "Precondition: IsModified should be true after adding a plugin.");

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Plugins.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void OnResetToInherited_AfterLoad_RestoresOnDiskPlugins_NotClearsThem()
    {
        // Regression: prior to the fix, OnResetToInherited called Plugins.Clear()
        // unconditionally, silently destroying the user's saved plugin list when they
        // hit Reset after editing. The fix introduces _lastLayered/_lastScope caching
        // (mirrors McpServers / Permissions / Hooks pattern) so Reset reloads the
        // on-disk state instead. Locks the contract that "Reset = undo unsaved edits,
        // not wipe everything".
        JsonObject loaded = new()
        {
            ["x@m"] = true,
            ["y@m"] = false,
        };

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, loaded), ConfigScope.User);
        Assert.AreEqual(2, vm.Plugins.Count, "Precondition: load populated 2 plugins.");
        Assert.IsTrue(vm.IsModified);

        // User edits: add a third plugin.
        vm.NewPluginRef = "z@m";
        vm.AddPluginCommand.Execute(null);
        Assert.AreEqual(3, vm.Plugins.Count);

        // User clicks Reset: must restore the original 2-plugin state, NOT clear.
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(2, vm.Plugins.Count,
            "Reset must restore the on-disk state, not wipe to empty. The two original " +
            "plugins (x@m, y@m) must be back.");
        HashSet<string> loadedRefs = vm.Plugins.Select(p => p.PluginRef).ToHashSet();
        Assert.IsTrue(loadedRefs.Contains("x@m"));
        Assert.IsTrue(loadedRefs.Contains("y@m"));
        Assert.IsFalse(loadedRefs.Contains("z@m"),
            "The unsaved 'z@m' addition must have been discarded.");
    }

    // -----------------------------------------------------------------------
    // ToJsonValue
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToJsonValue_ReturnsNull_WhenEmpty()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);

        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void ToJsonValue_IncludesEnabledAndDisabledEntries()
    {
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);

        // Add an enabled entry via the command
        vm.NewPluginRef = "enabled@m";
        vm.AddPluginCommand.Execute(null);

        // Add a disabled entry by manipulating the collection directly
        vm.NewPluginRef = "disabled@m";
        vm.AddPluginCommand.Execute(null);
        vm.Plugins.First(p => p.PluginRef == "disabled@m").Enabled = false;

        JsonObject? json = vm.ToJsonValue() as JsonObject;

        Assert.IsNotNull(json);
        Assert.IsTrue(json!["enabled@m"]!.GetValue<bool>());
        Assert.IsFalse(json["disabled@m"]!.GetValue<bool>());
    }

    // -----------------------------------------------------------------------
    // SDK-backed read path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor()
    {
        // Verifies that when a client is supplied, the editor's initial state
        // comes from client.Plugins.GetAt(scope) — not from the LayeredValue
        // argument. We make the two diverge intentionally so the test can
        // tell which path won: the SDK has "from-sdk@m"; the layered value
        // has "from-layered@m". The SDK path must be the source of truth.

        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            client.Plugins.Set(new EnabledPlugin("from-sdk@m", Enabled: true));

            // A LayeredValue carrying a DIFFERENT plugin — the editor should
            // ignore this and read from the SDK instead.
            JsonObject divergent = new() { ["from-layered@m"] = false };
            LayeredValue layered = LayeredWithPlugins(ConfigScope.User, divergent);

            EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            Assert.AreEqual(1, vm.Plugins.Count, "SDK path should yield exactly 1 plugin (the SDK-set entry).");
            Assert.AreEqual("from-sdk@m", vm.Plugins[0].PluginRef,
                "Editor must read from the SDK accessor, not the LayeredValue argument.");
            Assert.IsTrue(vm.Plugins[0].Enabled);
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
        // Inverse of the previous test: when no client is passed, the
        // editor's initial state must come from the LayeredValue argument
        // (the pre-4.3.6b behaviour). This guards the fallback path that
        // unit-test fixtures and any non-migrated call site rely on.
        JsonObject legacy = new() { ["legacy-only@m"] = true };
        LayeredValue layered = LayeredWithPlugins(ConfigScope.User, legacy);

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User, client: null);
        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.AreEqual(1, vm.Plugins.Count);
        Assert.AreEqual("legacy-only@m", vm.Plugins[0].PluginRef);
        Assert.IsTrue(vm.Plugins[0].Enabled);
    }

    [TestMethod]
    public void ToJsonValue_RoundTrip()
    {
        JsonObject original = new()
        {
            ["a@m"] = true,
            ["b@m"] = false,
        };

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, original), ConfigScope.User);

        JsonObject? result = vm.ToJsonValue() as JsonObject;

        Assert.IsNotNull(result);
        Assert.IsTrue(result!.ContainsKey("a@m"));
        Assert.IsTrue(result.ContainsKey("b@m"));
        Assert.IsTrue(result["a@m"]!.GetValue<bool>());
        Assert.IsFalse(result["b@m"]!.GetValue<bool>());
    }

    // -----------------------------------------------------------------------
    // Non-bool (array) value preservation — schema allows anyOf[array, bool]
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LoadFromLayered_ArrayValuePlugin_PreservedVerbatimOnRoundTrip()
    {
        // The enabledPlugins schema allows an array-of-strings value (enable
        // specific components) alongside the bool form. The editor has no typed
        // affordance for it, but must NOT coerce it to a bool on save — doing so
        // silently destroyed the array (and flipped the plugin's meaning to false).
        JsonObject loaded = new()
        {
            ["bool@m"] = true,
            ["array@m"] = new JsonArray("comp-a", "comp-b"),
        };

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, loaded), ConfigScope.User);

        JsonObject? result = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(result);

        // The plain bool round-trips as a bool.
        Assert.IsTrue(result!["bool@m"] is JsonValue bv && bv.GetValue<bool>());

        // The array round-trips verbatim — NOT coerced to `false`.
        Assert.IsInstanceOfType(result["array@m"], typeof(JsonArray),
            "An array-valued plugin must survive the round-trip as an array, not be coerced to a bool.");
        JsonArray arr = (JsonArray)result["array@m"]!;
        Assert.AreEqual(2, arr.Count);
        Assert.AreEqual("comp-a", arr[0]!.GetValue<string>());
        Assert.AreEqual("comp-b", arr[1]!.GetValue<string>());
    }

    [TestMethod]
    public void ToJsonValue_ArrayValuePlugin_PreservedEvenWhenToggled()
    {
        // Re-audit HIGH regression lock: toggling the checkbox must NOT silently
        // destroy the component array. The array always wins on save (the view
        // disables the checkbox for such rows); a net-zero toggle-and-back — which
        // leaves the UI visually unchanged — must not lose data either.
        JsonObject loaded = new() { ["array@m"] = new JsonArray("comp-a") };

        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, loaded), ConfigScope.User);

        PluginEntry entry = vm.Plugins.Single(p => p.PluginRef == "array@m");
        Assert.IsTrue(entry.HasPreservedValue, "An array-valued row must carry its preserved value.");
        Assert.IsTrue(entry.Enabled, "An array-valued plugin surfaces as enabled (checked, disabled checkbox).");

        entry.Enabled = !entry.Enabled; // one toggle
        entry.Enabled = !entry.Enabled; // and back — net-zero, UI looks untouched

        JsonObject? result = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result!["array@m"], typeof(JsonArray),
            "The array must survive checkbox toggling — no silent coercion to bool.");
        Assert.AreEqual("comp-a", ((JsonArray)result["array@m"]!)[0]!.GetValue<string>());
    }

    [TestMethod]
    public async Task LoadFromLayered_WithSdkClient_NonBoolValue_RecoveredFromRawScope()
    {
        // The SDK accessor's entry list (PluginRef + Enabled) does not carry the array
        // payload to the editor, so the editor recovers non-bool values from the raw scope
        // JSON and round-trips them verbatim. Here the accessor (empty) and the raw scope
        // (has the array) diverge intentionally — same technique as
        // LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor — to prove the editor
        // recovers a key the accessor's list omits.
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-arr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            // Accessor sees no plugins; the raw scope carries an array-valued one.
            JsonObject rawScope = new() { ["array@m"] = new JsonArray("comp-a", "comp-b") };
            LayeredValue layered = LayeredWithPlugins(ConfigScope.User, rawScope);

            EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            Assert.IsTrue(vm.Plugins.Any(p => p.PluginRef == "array@m"),
                "An array-valued plugin omitted by the SDK accessor must be recovered from the raw scope JSON.");

            JsonObject? result = vm.ToJsonValue() as JsonObject;
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result!["array@m"], typeof(JsonArray),
                "The recovered array must round-trip verbatim — not be dropped or coerced.");
            Assert.AreEqual(2, ((JsonArray)result["array@m"]!).Count);
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
    public async Task LoadFromLayered_WithSdkClient_ArrayInWorkspace_AttachesPreservedAndRoundTrips()
    {
        // Companion to the recovery test: here the SDK workspace genuinely holds an
        // array-valued plugin (Set with Components), so GetAt surfaces it and the
        // SDK-path loop creates the entry — then the reconcile loop must ATTACH
        // PreservedValue from the raw scope so ToJsonValue re-emits the array. Guards the
        // reconcile loop against removal (its job is to attach the verbatim value, since
        // the SDK-path entry carries only PluginRef + Enabled).
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-arr2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
            client.Plugins.Set(new EnabledPlugin("comp/plugin", Enabled: true, Components: ["a", "b"]));

            JsonObject rawScope = new() { ["comp/plugin"] = new JsonArray("a", "b") };
            LayeredValue layered = LayeredWithPlugins(ConfigScope.User, rawScope);

            EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            PluginEntry entry = vm.Plugins.Single(p => p.PluginRef == "comp/plugin");
            Assert.IsTrue(entry.HasPreservedValue, "The SDK-surfaced array row must get PreservedValue attached.");

            JsonObject? result = vm.ToJsonValue() as JsonObject;
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result!["comp/plugin"], typeof(JsonArray),
                "An array-valued plugin in the SDK workspace must round-trip as an array through the editor.");
            Assert.AreEqual(2, ((JsonArray)result["comp/plugin"]!).Count);
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
            ["x@m"] = true,
            ["y@m"] = false,
        };
        EnabledPluginsEditorViewModel vm = new(PluginsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPlugins(ConfigScope.User, loaded), ConfigScope.User);
        Assert.IsTrue(vm.IsModified, "Precondition: load with two plugins flags IsModified=true.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EnabledPluginsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        // Delete one loaded plugin — collection still non-empty so
        // IsModified stays latched true.
        vm.Plugins.RemoveAt(0);

        Assert.IsTrue(fired >= 1,
            "Deleting a loaded plugin must fire PropertyChanged(IsModified) — locks the force-fire " +
            "contract symmetric to the MCP delete-after-load contract.");
    }
}