using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;
using MarketplaceEntry = Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces.MarketplaceEntry;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class MarketplacesEditorViewModelTests
{
    private static SchemaNode MarketplacesSchema()
    {
        return new SchemaNode("extraKnownMarketplaces", "extraKnownMarketplaces")
            { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWithMarketplaces(ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj, "/fake");
        return new LayeredValue("extraKnownMarketplaces", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = scope,
        };
    }

    // -----------------------------------------------------------------------
    // LoadFromLayered
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LoadFromLayered_EmptyObject_LeavesMarketplacesEmpty()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, new JsonObject()), ConfigScope.User);

        Assert.AreEqual(0, vm.Marketplaces.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromLayered_Format1_CanonicalShape()
    {
        // Format 1 (schema-canonical): { source: { source: "url", url: "..." } }
        JsonObject obj = new()
        {
            ["myM"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "url",
                    ["url"] = "https://x.com",
                },
            },
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("myM", vm.Marketplaces[0].Name);
        Assert.AreEqual("url", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://x.com", vm.Marketplaces[0].SourceValue);
        Assert.IsTrue(vm.IsModified);
    }

    [TestMethod]
    public void LoadFromLayered_Format1_GithubType()
    {
        JsonObject obj = new()
        {
            ["m"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "github",
                    ["repository"] = "user/repo",
                },
            },
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("github", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("user/repo", vm.Marketplaces[0].SourceValue);
    }

    [TestMethod]
    public void LoadFromLayered_Format2_FlatShape()
    {
        // Format 2 (flat): { url: "...", type: "url" } — no nested source object
        JsonObject obj = new()
        {
            ["m"] = new JsonObject
            {
                ["url"] = "https://x.com",
                ["type"] = "url",
            },
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("url", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://x.com", vm.Marketplaces[0].SourceValue);
    }

    [TestMethod]
    public void LoadFromLayered_Format3_StringShorthand()
    {
        // Format 3 (string shorthand): just "https://..." as the value
        JsonObject obj = new()
        {
            ["m"] = JsonValue.Create("https://x.com"),
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("url", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://x.com", vm.Marketplaces[0].SourceValue);
    }

    // -----------------------------------------------------------------------
    // AddMarketplace
    // -----------------------------------------------------------------------

    [TestMethod]
    public void AddMarketplace_AddsEntry()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("M", vm.Marketplaces[0].Name);
        Assert.AreEqual("url", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://x.com", vm.Marketplaces[0].SourceValue);
    }

    [TestMethod]
    public void AddMarketplace_RejectsEmptyName()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "   ";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.AreEqual(0, vm.Marketplaces.Count);
    }

    [TestMethod]
    public void AddMarketplace_RejectsEmptyValue()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "   ";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.AreEqual(0, vm.Marketplaces.Count);
    }

    [TestMethod]
    public void AddMarketplace_RejectsDuplicateName()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://y.com";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.AreEqual(1, vm.Marketplaces.Count);
    }

    [TestMethod]
    public void AddMarketplace_ClearsFields()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.AreEqual(string.Empty, vm.NewName);
        Assert.AreEqual(string.Empty, vm.NewSourceValue);
    }

    // -----------------------------------------------------------------------
    // RemoveMarketplace
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RemoveMarketplace_RemovesEntry()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        vm.RemoveMarketplaceCommand.Execute(vm.Marketplaces[0]);

        Assert.AreEqual(0, vm.Marketplaces.Count);
    }

    // -----------------------------------------------------------------------
    // ResetToInherited
    // -----------------------------------------------------------------------

    [TestMethod]
    public void OnResetToInherited_ClearsAndSetsIsModifiedFalse()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "M";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://x.com";
        vm.AddMarketplaceCommand.Execute(null);

        Assert.IsTrue(vm.IsModified, "Precondition: IsModified should be true after adding a marketplace.");

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Marketplaces.Count);
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void OnResetToInherited_AfterLoad_RestoresOnDiskMarketplaces_NotClearsThem()
    {
        // Regression: prior to the fix, OnResetToInherited called Marketplaces.Clear()
        // unconditionally, silently destroying the user's saved marketplace list when
        // they hit Reset after editing. The fix introduces _lastLayered/_lastScope
        // caching (mirrors McpServers / Permissions / Hooks pattern). Locks the contract
        // that "Reset = undo unsaved edits, not wipe everything".
        JsonObject loaded = new()
        {
            ["alpha"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "url",
                    ["url"] = "https://alpha.example.com",
                },
            },
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, loaded), ConfigScope.User);
        Assert.AreEqual(1, vm.Marketplaces.Count, "Precondition: load populated 1 marketplace.");
        Assert.IsTrue(vm.IsModified);

        // User edits: add a second marketplace.
        vm.NewName = "beta";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://beta.example.com";
        vm.AddMarketplaceCommand.Execute(null);
        Assert.AreEqual(2, vm.Marketplaces.Count);

        // User clicks Reset: must restore the original 1-entry state, NOT clear.
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Marketplaces.Count,
            "Reset must restore the on-disk state, not wipe to empty.");
        Assert.AreEqual("alpha", vm.Marketplaces[0].Name);
        Assert.AreEqual("https://alpha.example.com", vm.Marketplaces[0].SourceValue);
    }

    [TestMethod]
    public void ToJsonValue_GitType_EmitsUrlSourceKey_RoundTripsCleanly()
    {
        // Regression: prior to the fix, ToJsonValue had no "git" branch in the source-key
        // switch, so a "git" entry round-tripped to {source:"git", url:""} on the first
        // load (because ExtractSourceValue maps "git" → obj["url"], and ToJsonValue's
        // _ default emitted source-key = "url"). The shape was self-consistent on the
        // INPUT side; the round-trip break was a missing explicit "git" arm that left
        // the contract relying on the default fall-through. Add the explicit arm so
        // future readers don't have to trace fallthrough behavior to verify "git" works.
        JsonObject loaded = new()
        {
            ["g"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "git",
                    ["url"] = "https://git.example.com/repo.git",
                },
            },
        };

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, loaded), ConfigScope.User);

        Assert.AreEqual("git", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://git.example.com/repo.git", vm.Marketplaces[0].SourceValue);

        // Round-trip: ToJsonValue produces a structure that LoadFromLayered would
        // re-load to the same in-memory state.
        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(emitted);
        JsonObject? entry = emitted!["g"] as JsonObject;
        Assert.IsNotNull(entry);
        JsonObject? sourceObj = entry!["source"] as JsonObject;
        Assert.IsNotNull(sourceObj);
        Assert.AreEqual("git", sourceObj!["source"]?.GetValue<string>(),
            "The 'source' discriminator must round-trip as 'git'.");
        Assert.AreEqual("https://git.example.com/repo.git", sourceObj["url"]?.GetValue<string>(),
            "Git source value must serialise under the 'url' key (matches ExtractSourceValue's reverse mapping).");

        // And feeding the emitted shape back into LoadFromLayered must reproduce the entry.
        MarketplacesEditorViewModel vm2 = new(MarketplacesSchema(), ConfigScope.User);
        vm2.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, emitted), ConfigScope.User);
        Assert.AreEqual(1, vm2.Marketplaces.Count);
        Assert.AreEqual("git", vm2.Marketplaces[0].SourceType);
        Assert.AreEqual("https://git.example.com/repo.git", vm2.Marketplaces[0].SourceValue);
    }

    // -----------------------------------------------------------------------
    // ToJsonValue
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToJsonValue_ReturnsNull_WhenEmpty()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);

        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void ToJsonValue_UrlType_EmitsCorrectShape()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "myMarket";
        vm.NewSourceType = "url";
        vm.NewSourceValue = "https://example.com";
        vm.AddMarketplaceCommand.Execute(null);

        JsonObject? json = vm.ToJsonValue() as JsonObject;

        Assert.IsNotNull(json);
        JsonObject? entry = json!["myMarket"] as JsonObject;
        Assert.IsNotNull(entry);
        JsonObject? source = entry!["source"] as JsonObject;
        Assert.IsNotNull(source);
        Assert.AreEqual("url", source!["source"]!.GetValue<string>());
        Assert.AreEqual("https://example.com", source["url"]!.GetValue<string>());
    }

    [TestMethod]
    public void ToJsonValue_GithubType_UsesRepositoryKey()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "ghMarket";
        vm.NewSourceType = "github";
        vm.NewSourceValue = "user/repo";
        vm.AddMarketplaceCommand.Execute(null);

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        JsonObject? source = (json!["ghMarket"] as JsonObject)!["source"] as JsonObject;

        Assert.IsNotNull(source);
        Assert.AreEqual("github", source!["source"]!.GetValue<string>());
        Assert.AreEqual("user/repo", source["repository"]!.GetValue<string>());
        Assert.IsNull(source["url"], "github type must use 'repository', not 'url'");
    }

    [TestMethod]
    public void ToJsonValue_NpmType_UsesPackageKey()
    {
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.NewName = "npmMarket";
        vm.NewSourceType = "npm";
        vm.NewSourceValue = "@scope/package";
        vm.AddMarketplaceCommand.Execute(null);

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        JsonObject? source = (json!["npmMarket"] as JsonObject)!["source"] as JsonObject;

        Assert.IsNotNull(source);
        Assert.AreEqual("npm", source!["source"]!.GetValue<string>());
        Assert.AreEqual("@scope/package", source["package"]!.GetValue<string>());
        Assert.IsNull(source["url"], "npm type must use 'package', not 'url'");
    }

    // -----------------------------------------------------------------------
    // SDK-backed read path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor()
    {
        // Same pattern as EnabledPlugins (4.3.6b): make the SDK and the
        // LayeredValue argument disagree, then assert the SDK wins. This
        // proves the editor reads through client.Marketplaces.GetAt(scope)
        // when a client is supplied.

        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-mkt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            client.Marketplaces.Set(new MarketplaceEntry(
                "from-sdk",
                MarketplaceSourceKind.Github,
                "owner/repo"));

            // Divergent layered argument — must be ignored.
            JsonObject divergent = new()
            {
                ["from-layered"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["source"] = "url",
                        ["url"] = "https://layered.example/m",
                    },
                },
            };
            LayeredValue layered = LayeredWithMarketplaces(ConfigScope.User, divergent);

            MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            Assert.AreEqual(1, vm.Marketplaces.Count, "SDK path should yield exactly the SDK-set entry.");
            Assert.AreEqual("from-sdk", vm.Marketplaces[0].Name);
            Assert.AreEqual("github", vm.Marketplaces[0].SourceType,
                "SDK MarketplaceSourceKind.Github must round-trip to the editor's 'github' string.");
            Assert.AreEqual("owner/repo", vm.Marketplaces[0].SourceValue);
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
        // When no client is passed, the editor's initial state must come
        // from the LayeredValue argument (the pre-4.3.6c behaviour).
        JsonObject legacy = new()
        {
            ["legacy-only"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "url",
                    ["url"] = "https://legacy.example/m",
                },
            },
        };
        LayeredValue layered = LayeredWithMarketplaces(ConfigScope.User, legacy);

        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User, client: null);
        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.AreEqual(1, vm.Marketplaces.Count);
        Assert.AreEqual("legacy-only", vm.Marketplaces[0].Name);
        Assert.AreEqual("url", vm.Marketplaces[0].SourceType);
        Assert.AreEqual("https://legacy.example/m", vm.Marketplaces[0].SourceValue);
    }

    // ── Force-fire delete-after-load ──────────────

    [TestMethod]
    public void DeleteAfterLoad_FiresIsModified_ForceFireContract()
    {
        // Locks the force-fire contract — see PermissionsEditorViewModelTests
        // for the full rationale.
        JsonObject loaded = new()
        {
            ["alpha"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "url",
                    ["url"] = "https://alpha.example.com",
                },
            },
            ["beta"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["source"] = "url",
                    ["url"] = "https://beta.example.com",
                },
            },
        };
        MarketplacesEditorViewModel vm = new(MarketplacesSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithMarketplaces(ConfigScope.User, loaded), ConfigScope.User);
        Assert.IsTrue(vm.IsModified, "Precondition: load with two entries flags IsModified=true.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplacesEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        // Delete one loaded entry — collection still non-empty so IsModified
        // stays latched true; the [ObservableProperty] setter would elide.
        vm.Marketplaces.RemoveAt(0);

        Assert.IsTrue(fired >= 1,
            "Deleting a loaded marketplace must fire PropertyChanged(IsModified) so the live-write " +
            "runs and Save enables — even though IsModified stays latched true.");
    }
}