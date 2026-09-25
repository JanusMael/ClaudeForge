using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Tests that HookEventGroup correctly handles the real Claude Code hooks JSON shape,
/// where each outer object contains a nested <c>hooks</c> array of <c>{type, command}</c>
/// pairs. Earlier code assumed <c>command</c> lived on the outer object, leaving the
/// command column blank for every existing user's hooks.
/// </summary>
public class HooksRoundTripTests
{
    private static SchemaNode HooksSchema()
    {
        return new SchemaNode("hooks", "hooks") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWith(JsonObject obj)
    {
        ScopeEntry entry = new(ConfigScope.User, obj, "/fake");
        return new LayeredValue("hooks", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = ConfigScope.User,
        };
    }

    [Fact]
    public void NestedShape_PopulatesCommandValue()
    {
        // Real Claude Code shape per the schema's own `examples`:
        //   { PostToolUse: [ { matcher: "Edit|Write", hooks: [ { type: "command", command: "..." } ] } ] }
        JsonObject obj = new()
        {
            ["PostToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Edit|Write",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = "prettier --write",
                        },
                    },
                },
            },
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        Assert.Single(group.Hooks);
        Assert.Equal("Edit|Write", group.Hooks[0].Matcher);
        Assert.Equal(HookCommandType.Command, group.Hooks[0].CommandType);
        Assert.Equal("prettier --write", group.Hooks[0].CommandValue);
    }

    [Fact]
    public void NestedShape_MultipleInnerHooksPerMatcher()
    {
        JsonObject obj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "command", ["command"] = "echo a" },
                        new JsonObject { ["type"] = "command", ["command"] = "echo b" },
                    },
                },
            },
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.Equal(2, group.Hooks.Count);
        Assert.True(group.Hooks.All(h => h.Matcher == "Bash"));
        Assert.Equal(
            new[] { "echo a", "echo b" },
            group.Hooks.Select(h => h.CommandValue).ToArray());
    }

    [Fact]
    public void NestedShape_PromptAndUrlTypes()
    {
        JsonObject obj = new()
        {
            ["PermissionRequest"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "prompt", ["prompt"] = "Continue?" },
                        new JsonObject { ["type"] = "url", ["url"] = "https://example.com" },
                    },
                },
            },
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PermissionRequest");
        Assert.Equal(2, group.Hooks.Count);
        Assert.Equal(HookCommandType.Prompt, group.Hooks[0].CommandType);
        Assert.Equal("Continue?", group.Hooks[0].CommandValue);
        Assert.Equal(HookCommandType.Url, group.Hooks[1].CommandType);
        Assert.Equal("https://example.com", group.Hooks[1].CommandValue);
    }

    [Fact]
    public void LegacyFlatShape_StillParses()
    {
        // Older/hand-edited settings files may have flat `{matcher, command}` outer objects.
        JsonObject obj = new()
        {
            ["Stop"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo bye" },
            },
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(obj), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "Stop");
        Assert.Single(group.Hooks);
        Assert.Equal("*", group.Hooks[0].Matcher);
        Assert.Equal("echo bye", group.Hooks[0].CommandValue);
    }

    [Fact]
    public void RoundTrip_EmitsNestedShape()
    {
        JsonObject original = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "command", ["command"] = "echo hi" },
                    },
                },
            },
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(original), ConfigScope.User);

        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        Assert.NotNull(emitted);

        JsonArray? pre = emitted!["PreToolUse"] as JsonArray;
        Assert.NotNull(pre);
        JsonObject? outer = pre![0] as JsonObject;
        Assert.NotNull(outer);
        Assert.Equal("Bash", outer!["matcher"]?.GetValue<string>());

        JsonArray? inner = outer["hooks"] as JsonArray;
        Assert.NotNull(inner);
        Assert.Single(inner!);

        JsonObject? innerObj = inner[0] as JsonObject;
        Assert.NotNull(innerObj);
        Assert.Equal("command", innerObj!["type"]?.GetValue<string>());
        Assert.Equal("echo hi", innerObj["command"]?.GetValue<string>());
    }

    [Fact]
    public void RoundTrip_GroupsByMatcher()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        group.Hooks[0].Matcher = "Bash";
        group.Hooks[0].CommandValue = "echo one";
        group.AddHookCommand.Execute(null);
        group.Hooks[1].Matcher = "Bash";
        group.Hooks[1].CommandValue = "echo two";
        group.AddHookCommand.Execute(null);
        group.Hooks[2].Matcher = "Edit";
        group.Hooks[2].CommandValue = "echo edit";

        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        JsonArray? pre = emitted!["PreToolUse"] as JsonArray;
        Assert.NotNull(pre);
        Assert.Equal(2, pre!.Count); // two matcher groups

        JsonObject? bashGroup = pre.Cast<JsonObject>().First(o => o["matcher"]!.GetValue<string>() == "Bash");
        JsonObject? editGroup = pre.Cast<JsonObject>().First(o => o["matcher"]!.GetValue<string>() == "Edit");

        Assert.Equal(2, (bashGroup["hooks"] as JsonArray)!.Count);
        Assert.Single((editGroup["hooks"] as JsonArray)!);
    }

    // ── per-row Headers + AllowedEnvVars round-trip via the GUI flow ──

    /// <summary>
    /// User repro: "add new hook, change to url, fill out
    /// the matcher, command, a single header and header value and a
    /// single variable name, hit save, reload window; the new hook is
    /// there, the headers are NOT, the env var IS".
    ///
    /// This test simulates the exact GUI flow:
    ///   1. AddHook on PreToolUse group
    ///   2. Set CommandType=Url, Matcher, CommandValue
    ///   3. Type into NewHeaderKey/NewHeaderValue and run AddHeaderCommand
    ///   4. Type into NewAllowedEnvVar and run AddAllowedEnvVarCommand
    ///   5. Read editor.ToJsonValue() (this is what the live-write feeds
    ///      to workspace.SetValue("hooks", …))
    /// and asserts BOTH headers and allowedEnvVars survive the editor's
    /// emission path.  Failure on the headers branch is the smoking gun.
    /// </summary>
    [Fact]
    public void AddHook_UrlWithHeaderAndAllowedEnvVar_BothSurviveToJsonValue()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        HookEntry entry = group.Hooks[0];

        entry.CommandType = HookCommandType.Url;
        entry.Matcher = "Bash";
        entry.CommandValue = "https://example.com/hook";

        entry.NewHeaderKey = "Authorization";
        entry.NewHeaderValue = "Bearer xyz";
        entry.AddHeaderCommand.Execute(null);

        entry.NewAllowedEnvVar = "SECRET_TOKEN";
        entry.AddAllowedEnvVarCommand.Execute(null);

        // Sanity — both collections were populated by the AddXxx commands.
        MessageAssert.Equal(1, entry.Headers.Count, "AddHeader did not append to Headers.");
        MessageAssert.Equal(1, entry.AllowedEnvVars.Count, "AddAllowedEnvVar did not append to AllowedEnvVars.");

        // The smoking-gun assertion: emitted JSON must contain both keys.
        JsonObject? emitted = vm.ToJsonValue() as JsonObject;
        Assert.NotNull(emitted);
        JsonObject inner = emitted!["PreToolUse"]!.AsArray()[0]!.AsObject()
            ["hooks"]!.AsArray()[0]!.AsObject();

        string pretty = emitted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Assert.True(inner.ContainsKey("headers"),
            $"Emitted hook MUST include 'headers'. Got:\n{pretty}");
        Assert.Equal("Bearer xyz",
            inner["headers"]!.AsObject()["Authorization"]!.GetValue<string>());

        Assert.True(inner.ContainsKey("allowedEnvVars"),
            $"Emitted hook MUST include 'allowedEnvVars'. Got:\n{pretty}");
        Assert.Equal("SECRET_TOKEN",
            inner["allowedEnvVars"]!.AsArray()[0]!.GetValue<string>());
    }

    /// <summary>
    /// Same scenario as above, but driven through the SDK-backed editor
    /// path that production uses (HooksEditorViewModel constructed with
    /// an IAgentConfigClient).  After the editor "writes" via
    /// ToJsonValue → workspace.SetValue, a fresh editor is constructed
    /// against the same workspace and assertions check that the second
    /// editor sees BOTH the header and the allowedEnvVar — i.e. that the
    /// data survives the full live-write + reload round-trip.
    /// </summary>
    [Fact]
    public void AddHook_UrlWithHeaderAndAllowedEnvVar_SurvivesSdkBackedReload()
    {
        // Build empty workspace + SDK client.
        SettingsDocument doc = new(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, new SchemaRegistry());

        // First editor — populate with a URL hook + header + allowedEnvVar.
        HooksEditorViewModel vm1 = new(HooksSchema(), ConfigScope.User, client);
        vm1.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group1 = vm1.EventGroups.First(g => g.EventName == "PreToolUse");
        group1.AddHookCommand.Execute(null);
        HookEntry entry1 = group1.Hooks[0];
        entry1.CommandType = HookCommandType.Url;
        entry1.Matcher = "Bash";
        entry1.CommandValue = "https://example.com/hook";

        entry1.NewHeaderKey = "Authorization";
        entry1.NewHeaderValue = "Bearer xyz";
        entry1.AddHeaderCommand.Execute(null);

        entry1.NewAllowedEnvVar = "SECRET_TOKEN";
        entry1.AddAllowedEnvVarCommand.Execute(null);

        // Simulate the live-write SettingsGroupEditorViewModel performs.
        JsonNode? emitted = vm1.ToJsonValue();
        MessageAssert.NotNull(emitted, "Editor must emit hooks JSON.");
        client.SetValue("hooks", emitted!, ConfigScope.User);

        // Pretty-printed snapshot for failure diagnostics.
        string pretty = doc.Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Sanity — the workspace's stored hooks include both keys.
        JsonObject stored = doc.Root["hooks"]!.AsObject()["PreToolUse"]!.AsArray()[0]!.AsObject()
            ["hooks"]!.AsArray()[0]!.AsObject();
        Assert.True(stored.ContainsKey("headers"),
            $"workspace.Root must have 'headers' after live-write. Got:\n{pretty}");
        Assert.True(stored.ContainsKey("allowedEnvVars"),
            $"workspace.Root must have 'allowedEnvVars' after live-write. Got:\n{pretty}");

        // Second editor — fresh load from the same workspace (simulates reload).
        HooksEditorViewModel vm2 = new(HooksSchema(), ConfigScope.User, client);
        LayeredValue layered2 = new("hooks",
            [new ScopeEntry(ConfigScope.User, doc.Root["hooks"]!, "/fake")])
        {
            EffectiveValue = doc.Root["hooks"],
            EffectiveScope = ConfigScope.User,
        };
        vm2.LoadFromLayered(layered2, ConfigScope.User);

        HookEventGroup group2 = vm2.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.Single(group2.Hooks);
        HookEntry entry2 = group2.Hooks[0];

        Assert.Equal(HookCommandType.Url, entry2.CommandType);
        Assert.Equal("Bash", entry2.Matcher);
        Assert.Equal("https://example.com/hook", entry2.CommandValue);

        MessageAssert.Equal(1, entry2.Headers.Count,
            $"Reloaded entry MUST have 1 header. Got {entry2.Headers.Count}. JSON:\n{pretty}");
        Assert.Equal("Authorization", entry2.Headers[0].Key);
        Assert.Equal("Bearer xyz", entry2.Headers[0].Value);

        MessageAssert.Equal(1, entry2.AllowedEnvVars.Count,
            $"Reloaded entry MUST have 1 allowedEnvVar. Got {entry2.AllowedEnvVars.Count}. JSON:\n{pretty}");
        Assert.Equal("SECRET_TOKEN", entry2.AllowedEnvVars[0]);
    }

    // =====================================================================
    // Subscription-gap regressions.
    //
    // These tests pin the MCP-pattern subscriptions that close the gap
    // between the add of [HookEntry.Headers + AllowedEnvVars
    // ObservableCollections] and the editor's MarkModified wiring.
    // Without these subscriptions, save-button enablement was silent for
    // edit-existing-cell, remove-header, and remove-env-var mutations.
    // See HooksEditorViewModel.SubscribeEntry / OnNestedCollectionChanged
    // / OnNestedItemChanged + editors AGENTS.md §4 for the contract.
    // =====================================================================

    /// <summary>
    /// Build a hooks JsonObject with one URL hook that already has one header
    /// + one allowed env-var.  Used as the at-load state for the gap tests.
    /// </summary>
    private static JsonObject HooksWithUrlHookAndHeaderAndEnvVar()
    {
        return new JsonObject
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Bash",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "url",
                            ["url"] = "https://example.com/hook",
                            ["headers"] = new JsonObject { ["Authorization"] = "Bearer xyz" },
                            ["allowedEnvVars"] = new JsonArray { "SECRET_TOKEN" },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void EditingExistingHeaderValueCell_FiresMarkModified()
    {
        // Subscription-gap test: editing the Value of an already-loaded
        // HookHeaderEntry must route through OnNestedItemChanged so the
        // Save button enables.  Pre-fix: HookHeaderEntry.PropertyChanged
        // had no subscriber, so the edit was silent.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        MessageAssert.Equal(1, entry.Headers.Count, "precondition: header is loaded");

        // Reset IsModified to simulate the post-save state (no pending changes).
        vm.IsModified = false;

        entry.Headers[0].Value = "Bearer NEW_VALUE";

        Assert.True(vm.IsModified,
            "Editing an existing HookHeaderEntry's Value cell MUST fire MarkModified.");
    }

    [Fact]
    public void EditingExistingHeaderKeyCell_FiresMarkModified()
    {
        // Same gap as the Value test, but for the Key column.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        vm.IsModified = false;

        entry.Headers[0].Key = "X-Custom-Header";

        Assert.True(vm.IsModified,
            "Editing an existing HookHeaderEntry's Key cell MUST fire MarkModified.");
    }

    [Fact]
    public void RemovingHeaderViaCommand_FiresMarkModified()
    {
        // Subscription-gap test: removing a header via the row's × button
        // (RemoveHeaderCommand) must route through OnNestedCollectionChanged
        // so the Save button enables.  Pre-fix: Headers.CollectionChanged
        // had no subscriber, so the removal was silent.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        HookHeaderEntry hdr = entry.Headers[0];
        vm.IsModified = false;

        entry.RemoveHeaderCommand.Execute(hdr);

        MessageAssert.Equal(0, entry.Headers.Count, "precondition: header was actually removed");
        Assert.True(vm.IsModified,
            "Removing a header via × button MUST fire MarkModified.");
    }

    [Fact]
    public void RemovingAllowedEnvVarViaCommand_FiresMarkModified()
    {
        // Same gap as the header-remove test, but for AllowedEnvVars.
        // AllowedEnvVars items are strings (not INotifyPropertyChanged),
        // so the per-item subscription branch is a no-op — the
        // CollectionChanged subscription is what carries the signal.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        string name = entry.AllowedEnvVars[0];
        vm.IsModified = false;

        entry.RemoveAllowedEnvVarCommand.Execute(name);

        MessageAssert.Equal(0, entry.AllowedEnvVars.Count,
            "precondition: env-var was actually removed");
        Assert.True(vm.IsModified,
            "Removing an allowed env-var via × button MUST fire MarkModified.");
    }

    [Fact]
    public void TypingInNewHeaderKeyOrValue_DoesNotFireMarkModified()
    {
        // Transient-field filter test: NewHeaderKey / NewHeaderValue back the
        // input boxes above the + button.  Per editors AGENTS.md §5, those
        // notifications MUST NOT mark the editor modified — otherwise the
        // Save button would flicker on every keystroke and a typed-but-not-
        // committed entry would falsely enable Save.  AddHeader (which
        // commits the buffer to Headers) is what fires the dirty bit.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        vm.IsModified = false;

        entry.NewHeaderKey = "Content-Type";
        entry.NewHeaderValue = "application/json";

        Assert.False(vm.IsModified,
            "Typing in the NewHeader buffer (without clicking +) MUST NOT fire MarkModified.");
    }

    [Fact]
    public void TypingInNewAllowedEnvVar_DoesNotFireMarkModified()
    {
        // Same filter-rule as the NewHeaderKey test but for the env-var input.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        vm.IsModified = false;

        entry.NewAllowedEnvVar = "ANOTHER_TOKEN";

        Assert.False(vm.IsModified,
            "Typing in NewAllowedEnvVar (without clicking +) MUST NOT fire MarkModified.");
    }

    [Fact]
    public void AddHeaderCommand_AfterReset_FiresMarkModifiedOnceViaCollectionChanged()
    {
        // Combined test: AddHeader's full flow exercises both the new
        // CollectionChanged subscription AND the new transient-field filter.
        // The Headers.Add fires MarkModified via OnNestedCollectionChanged;
        // the subsequent NewHeaderKey="" / NewHeaderValue="" are filtered.
        // Either way, the editor ends up modified — but the source of truth
        // is now the CollectionChanged event, not the side-effect of buffer
        // resets.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(HooksWithUrlHookAndHeaderAndEnvVar()), ConfigScope.User);

        HookEntry entry = vm.EventGroups.First(g => g.EventName == "PreToolUse").Hooks[0];
        vm.IsModified = false;

        entry.NewHeaderKey = "X-Trace-Id";
        entry.NewHeaderValue = "abc-123";
        // Filtered: still false.
        Assert.False(vm.IsModified, "buffer typing must stay clean");

        entry.AddHeaderCommand.Execute(null);

        MessageAssert.Equal(2, entry.Headers.Count,
            "AddHeader should append the new entry to Headers");
        Assert.True(vm.IsModified,
            "AddHeader's Headers.Add MUST fire MarkModified via OnNestedCollectionChanged.");
    }
}