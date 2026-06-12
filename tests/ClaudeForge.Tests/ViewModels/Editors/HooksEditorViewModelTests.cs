using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

[TestClass]
public class HooksEditorViewModelTests
{
    private static SchemaNode HooksSchema()
    {
        return new SchemaNode("hooks", "hooks") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWithHooks(ConfigScope scope, JsonObject? obj)
    {
        ScopeEntry entry = new(scope, obj, "/fake");
        return new LayeredValue("hooks", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = scope,
        };
    }

    [TestMethod]
    public void LoadFromLayered_CreatesAllKnownEventGroups()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        Assert.AreEqual(HooksEditorViewModel.KnownEventTypes.Count, vm.EventGroups.Count);
    }

    [TestMethod]
    public void LoadFromLayered_PopulatesHooksForMatchedEvent()
    {
        JsonObject obj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "Bash", ["command"] = "echo pre" }
            }
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, obj), ConfigScope.User);

        HookEventGroup preGroup = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.AreEqual(1, preGroup.Hooks.Count);
        Assert.AreEqual("Bash", preGroup.Hooks[0].Matcher);
        Assert.AreEqual("echo pre", preGroup.Hooks[0].CommandValue);
    }

    [TestMethod]
    public void AddHook_CreatesNewEntry()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        group.AddHookCommand.Execute(null);

        Assert.AreEqual(1, group.Hooks.Count);
        Assert.AreEqual(group.Hooks[0], group.SelectedHook);
    }

    [TestMethod]
    public void RemoveHook_RemovesEntry()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "Stop");
        group.AddHookCommand.Execute(null);
        HookEntry hook = group.Hooks[0];
        group.RemoveHookCommand.Execute(hook);

        Assert.AreEqual(0, group.Hooks.Count);
    }

    [TestMethod]
    public void ToJsonValue_ReturnsNull_WhenNoHooks()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void ToJsonValue_IncludesOnlyGroupsWithHooks()
    {
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        group.Hooks[0].Matcher = "Bash";
        group.Hooks[0].CommandValue = "echo hi";

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json);
        Assert.AreEqual(1, json!.Count); // only PreToolUse
        Assert.IsNotNull(json["PreToolUse"]);
    }

    // ── Selection preservation across reload ─────────────────────────────
    //
    // user report (3.10 manual test): "I add a hook, I hit save,
    // changes dialog appears but my 'selection in the hooks list' is changed
    // to the first item which is disorienting and makes it hard to keep
    // adding hooks for the event I was in."
    //
    // Root cause: the Save flow's ApplyToWorkspace flush triggers
    // workspace.Changed → SettingsGroupEditorViewModel.OnWorkspaceChanged
    // → RebuildEditors → HooksEditorViewModel.LoadFromLayered. Pre-fix,
    // LoadFromLayered cleared EventGroups and snapped SelectedGroup to
    // FirstOrDefault(g => g.Hooks.Count > 0), which is "PreToolUse" if any
    // hook lives there — moving the user away from whichever event they
    // were authoring.
    //
    // Fix: capture SelectedGroup?.EventName before clearing, restore by
    // name after rebuilding.

    [TestMethod]
    public void LoadFromLayered_PreservesSelectedGroup_AcrossReload()
    {
        // Mirror the user's scenario: many events have hooks (so the
        // "FirstOrDefault with hooks" pick wouldn't choose ours), and the
        // user has navigated to a specific later event.
        JsonObject loaded = new()
        {
            ["PreToolUse"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["command"] = "echo a",
            }),
            ["Stop"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["command"] = "echo b",
            }),
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, loaded), ConfigScope.User);

        // User navigates to Stop and adds a hook.
        HookEventGroup stop = vm.EventGroups.First(g => g.EventName == "Stop");
        vm.SelectedGroup = stop;
        stop.Hooks.Add(new HookEntry { Matcher = "*", CommandValue = "echo c" });
        Assert.AreEqual("Stop", vm.SelectedGroup?.EventName);

        // Save flow runs, which somewhere along the way reloads the editor
        // (workspace.Changed → RebuildEditors → LoadFromLayered).
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, loaded), ConfigScope.User);

        Assert.IsNotNull(vm.SelectedGroup);
        Assert.AreEqual("Stop", vm.SelectedGroup!.EventName,
            "After a reload, SelectedGroup must remain on the user's previously-chosen event group, "
            + "not snap back to the first non-empty group. See 3.10 user report.");
    }

    [TestMethod]
    public void LoadFromLayered_PicksFirstNonEmptyGroup_OnFirstLoad()
    {
        // Counter-test: on the FIRST load (no prior selection), the
        // historical default still applies — pick the first event with
        // hooks. Without this, the editor would open with no group
        // selected and the right pane would show the placeholder.
        JsonObject loaded = new()
        {
            ["Stop"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["command"] = "echo b",
            }),
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, loaded), ConfigScope.User);

        Assert.IsNotNull(vm.SelectedGroup);
        Assert.AreEqual("Stop", vm.SelectedGroup!.EventName,
            "First load with no prior selection should pick the first non-empty event group.");
    }

    [TestMethod]
    public void LoadFromLayered_FallsBack_WhenPriorEventNoLongerExists()
    {
        // Edge: prior selected event was an unknown name (e.g. user had
        // "PermissionDenied" in their settings; on next load it's still
        // there). Just confirm the re-select-by-name lookup is correct
        // when the event still exists. The other-edge — prior event
        // dropped — is harder to construct because EventGroups always
        // contains every KnownEventTypes entry.
        JsonObject loaded = new()
        {
            ["UnknownEvent"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["command"] = "echo x",
            }),
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, loaded), ConfigScope.User);
        vm.SelectedGroup = vm.EventGroups.First(g => g.EventName == "UnknownEvent");

        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, loaded), ConfigScope.User);

        Assert.AreEqual("UnknownEvent", vm.SelectedGroup?.EventName,
            "Selection by name should match across reloads even for non-KnownEventTypes entries.");
    }

    [TestMethod]
    public void Reset_RestoresToSavedState()
    {
        // Arrange: load one PreToolUse hook from "disk", then add a PostToolUse hook
        // as an unsaved change.
        JsonObject savedObj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo x" }
            }
        };

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, savedObj), ConfigScope.User);

        // Simulate an unsaved change: add a hook to PostToolUse group
        HookEventGroup postGroup = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        postGroup.Hooks.Add(new HookEntry { Matcher = "*", CommandValue = "echo y" });
        Assert.AreEqual(1, postGroup.Hooks.Count, "Setup: PostToolUse should have 1 hook after unsaved add");

        // Act: reset to saved state
        vm.ResetToInheritedCommand.Execute(null);

        // Assert: PostToolUse hook removed, PreToolUse hook restored
        HookEventGroup postAfter = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        Assert.AreEqual(0, postAfter.Hooks.Count, "PostToolUse hook should be removed after reset");

        HookEventGroup preAfter = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        Assert.AreEqual(1, preAfter.Hooks.Count, "PreToolUse hook should be restored from saved state");
        Assert.IsTrue(vm.IsModified, "IsModified should be true because hooks exist at this scope");
    }

    [TestMethod]
    public void Reset_ClearsAll_WhenScopeValueIsNull()
    {
        // When the saved layered value has no entry at the editing scope (scope value == null),
        // reset re-loads from the saved state and clears all hooks.
        // NOTE: _lastLayered is non-null here (set by LoadFromLayered); this tests the
        // reload path, not the bare-VM-no-load fallback path.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        // Load with a null JSON node at the scope: _lastLayered is set but scopeValue is null.
        vm.LoadFromLayered(
            LayeredWithHooks(ConfigScope.User, null),
            ConfigScope.User);

        // Manually add a hook via the group (simulates an unsaved new hook).
        HookEventGroup preGroup = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        preGroup.Hooks.Add(new HookEntry { Matcher = "*", CommandValue = "echo test" });

        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsTrue(vm.EventGroups.All(g => g.Hooks.Count == 0));
        Assert.IsFalse(vm.IsModified);
    }

    [TestMethod]
    public void Reset_ClearsAll_WithoutPriorLoad()
    {
        // Edge case: reset is called on a freshly-constructed VM where LoadFromLayered
        // was never invoked.  _lastLayered is null, so the fallback else-branch runs:
        // all hooks should be cleared and IsModified should be false.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);

        // IsModified starts false and CanReset is false, so the command is a no-op
        // unless we manually set IsModified to enable the command.
        vm.IsModified = true;

        // Directly call OnResetToInherited via the command (IsModified=true enables it).
        vm.ResetToInheritedCommand.Execute(null);

        Assert.IsTrue(vm.EventGroups.Count == 0 || vm.EventGroups.All(g => g.Hooks.Count == 0),
            "All hooks must be cleared by the fallback reset path.");
        Assert.IsFalse(vm.IsModified,
            "IsModified must be false after reset with no prior load.");
    }

    // ── OtherScopesWithData tests ─────────────────────────────────────────────

    [TestMethod]
    public void OtherScopesWithData_Empty_WhenOnlyEditingScopeHasHooks()
    {
        // Only User scope in layered → no OTHER scopes → empty list.
        JsonObject obj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo x" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, obj), ConfigScope.User);
        Assert.AreEqual(0, vm.OtherScopesWithData.Count);
    }

    [TestMethod]
    public void OtherScopesWithData_ListsOtherScopesWithNonEmptyObjects()
    {
        // Both User and Project scopes define hooks — Project should appear as a badge.
        JsonObject userObj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo user" }
            }
        };
        JsonObject projObj = new()
        {
            ["Stop"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo proj" }
            }
        };
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Project, projObj, "/p"),
        ];
        LayeredValue layered = new("hooks", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);
        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        // Step 3a: OtherScopesWithData is now IReadOnlyList<IEditorScope>; compare via Id.
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    [TestMethod]
    public void OtherScopesWithData_ExcludesEmptyObjects()
    {
        // Project scope present but its JSON object is empty — must not appear as a badge.
        JsonObject userObj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "x" }
            }
        };
        JsonObject projObj = new(); // empty
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Project, projObj, "/p"),
        ];
        LayeredValue layered = new("hooks", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);
        Assert.AreEqual(0, vm.OtherScopesWithData.Count);
    }

    [TestMethod]
    public void OtherScopesWithData_RefreshedAfterReset()
    {
        // After adding an unsaved hook and resetting, OtherScopesWithData must
        // still reflect the loaded state (Project badge should survive the reset).
        JsonObject userObj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "x" }
            }
        };
        JsonObject projObj = new()
        {
            ["Stop"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "y" }
            }
        };
        ScopeEntry[] entries =
        [
            new ScopeEntry(ConfigScope.User, userObj, "/u"),
            new ScopeEntry(ConfigScope.Project, projObj, "/p"),
        ];
        LayeredValue layered = new("hooks", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(layered, ConfigScope.User);

        // Add an unsaved hook to PostToolUse.
        vm.EventGroups.First(g => g.EventName == "PostToolUse")
          .Hooks.Add(new HookEntry { Matcher = "*", CommandValue = "z" });

        // Reset → should reload from saved state; Project badge must still be present.
        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.OtherScopesWithData.Count);
        // Step 3a: OtherScopesWithData is now IReadOnlyList<IEditorScope>; compare via Id.
        Assert.AreEqual("project", vm.OtherScopesWithData[0].Id);
    }

    // ── Inline-edit IsModified regression tests ────────────────────────────────

    [TestMethod]
    public void EditingLoadedHookCommandValue_SetsIsModified()
    {
        // Regression: changing CommandValue on a hook that was loaded from disk did not
        // mark the editor as modified because HookEntry.PropertyChanged was not subscribed.
        JsonObject savedObj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "original" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, savedObj), ConfigScope.User);

        // Simulate inline edit: user types a new command string into the text box.
        HookEventGroup preGroup = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        HookEntry entry = preGroup.Hooks[0];

        // Clear IsModified to verify the edit re-sets it.
        vm.IsModified = false;
        entry.CommandValue = "updated-command";

        Assert.IsTrue(vm.IsModified,
            "Editing a loaded hook's CommandValue must mark the editor as modified.");
    }

    [TestMethod]
    public void EditingLoadedHookMatcher_SetsIsModified()
    {
        // Regression counterpart for Matcher field.
        JsonObject savedObj = new()
        {
            ["PostToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "Bash", ["command"] = "echo x" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, savedObj), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        HookEntry entry = group.Hooks[0];

        vm.IsModified = false;
        entry.Matcher = "Write";

        Assert.IsTrue(vm.IsModified,
            "Editing a loaded hook's Matcher must mark the editor as modified.");
    }

    [TestMethod]
    public void EditingLoadedHookMatcher_SubscriptionSurvivesReload()
    {
        // After a second LoadFromLayered the new entries must also be subscribed.
        JsonObject savedObj = new()
        {
            ["Stop"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "*", ["command"] = "echo stop" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, savedObj), ConfigScope.User);

        // Reload with a different value — simulates a workspace reload.
        JsonObject savedObj2 = new()
        {
            ["Stop"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "Bash", ["command"] = "echo stop2" }
            }
        };
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, savedObj2), ConfigScope.User);

        HookEventGroup stopGroup = vm.EventGroups.First(g => g.EventName == "Stop");
        vm.IsModified = false;
        stopGroup.Hooks[0].CommandValue = "modified-after-reload";

        Assert.IsTrue(vm.IsModified,
            "Subscription must be established after each reload, not just the first.");
    }

    // ── ToJsonValue empty-command emission ─────────────────────────────────────

    [TestMethod]
    public void ToJsonValue_EmitsEntriesWithEmptyCommandValue()
    {
        // Behaviour reversed.  Empty-CommandValue entries are
        // now emitted as { "type": "command", "command": "" } rather than
        // dropped, so 'add hook' produces a structural diff against the
        // baseline and the save button enables.  The save-time schema
        // validator surfaces the minLength:1 violation when the user
        // tries to save without filling in the command.
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        group.AddHookCommand.Execute(null);
        group.Hooks[0].Matcher = "Bash";
        group.Hooks[0].CommandValue = string.Empty;

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json,
            "ToJsonValue must emit empty-command entries so 'add hook' triggers a save-button change.");
        Assert.IsTrue(json!.ContainsKey("PreToolUse"));
        JsonArray arr = json["PreToolUse"]!.AsArray();
        Assert.AreEqual(1, arr.Count);
        JsonObject outer = arr[0]!.AsObject();
        JsonObject inner = outer["hooks"]!.AsArray()[0]!.AsObject();
        Assert.AreEqual("command", inner["type"]!.GetValue<string>());
        Assert.AreEqual(string.Empty, inner["command"]!.GetValue<string>(),
            "The command key must be present (even when empty) so the structural diff fires.");
    }

    [TestMethod]
    public void ToJsonValue_PreservesGroupsWithOnlyEmptyEntries()
    {
        // Companion to the above: a group whose entries are all empty IS
        // emitted (each entry shows the user there's a pending hook to fill in).
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(new LayeredValue("hooks", []), ConfigScope.User);

        HookEventGroup group = vm.EventGroups.First(g => g.EventName == "PostToolUse");
        group.AddHookCommand.Execute(null); // entry 1 — empty command
        group.AddHookCommand.Execute(null); // entry 2 — empty command

        JsonObject? json = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(json,
            "ToJsonValue must include groups whose entries are pending fill-in so save enables.");
        Assert.IsTrue(json!.ContainsKey("PostToolUse"),
            "PostToolUse must NOT be omitted when entries are pending fill-in.");
    }

    // ── Force-fire IsModified contract on load → mutate paths ─────────────────
    //
    // These tests cover the same class of bug fixed for the MCP editor: when
    // LoadFromLayered sets IsModified=true and the user then edits or deletes
    // a loaded hook, MarkModified() must FORCE-FIRE PropertyChanged(IsModified)
    // even though the underlying flag was already true. Without that contract,
    // CommunityToolkit.Mvvm's [ObservableProperty] setter elides the equal
    // assignment, the live-write path in SettingsGroupEditorViewModel never
    // runs, and the Save button stays disabled.
    //
    // The earlier inline-edit tests (EditingLoadedHookCommandValue_SetsIsModified,
    // EditingLoadedHookMatcher_SetsIsModified) pre-cleared IsModified before
    // mutating, so they passed in the broken state too. These two tests
    // intentionally do NOT pre-clear, exercising the real load → user-edit
    // path that the user actually hits.

    [TestMethod]
    public void EditingLoadedHook_AfterLoad_FiresIsModifiedPropertyChanged()
    {
        JsonObject obj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "Bash", ["command"] = "echo pre" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, obj), ConfigScope.User);
        Assert.IsTrue(vm.IsModified, "Setup: load with non-null scope value sets IsModified=true.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HooksEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        HookEventGroup preGroup = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        preGroup.Hooks[0].CommandValue = "echo edited";

        Assert.IsTrue(fired >= 1,
            "Editing a loaded hook's CommandValue must fire PropertyChanged(IsModified) " +
            "even when the flag was already true from the prior load — otherwise the " +
            "live-write to the workspace never runs and Save stays disabled.");
    }

    [TestMethod]
    public void DeletingLoadedHook_AfterLoad_FiresIsModifiedPropertyChanged()
    {
        JsonObject obj = new()
        {
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject { ["matcher"] = "Bash", ["command"] = "echo pre" }
            }
        };
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithHooks(ConfigScope.User, obj), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HooksEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        HookEventGroup preGroup = vm.EventGroups.First(g => g.EventName == "PreToolUse");
        preGroup.RemoveHookCommand.Execute(preGroup.Hooks[0]);

        Assert.IsTrue(fired >= 1,
            "Deleting a loaded hook must fire PropertyChanged(IsModified) so the workspace " +
            "live-write runs and Save enables — symmetric to the MCP delete-after-load contract.");
    }

    // ── Validation — HookEntry / HookEventGroup ──────────────────────────────

    [TestMethod]
    public void HookEntry_BlankMatcher_HasValidationWarningTrue()
    {
        HookEntry entry = new() { Matcher = string.Empty, CommandValue = "echo hi" };
        Assert.IsTrue(entry.HasValidationWarning,
            "Blank Matcher must set HasValidationWarning=true.");
    }

    [TestMethod]
    public void HookEntry_StarMatcher_MatcherIsValidTrue()
    {
        HookEntry entry = new() { Matcher = "*", CommandValue = "echo hi" };
        Assert.IsTrue(entry.MatcherIsValid,
            "'*' is the wildcard — it must be valid.");
        Assert.IsFalse(entry.HasValidationWarning,
            "A hook with '*' matcher and non-empty command must have no warning.");
    }

    [TestMethod]
    public void HookEntry_KnownToolMatcher_MatcherIsValidTrue()
    {
        HookEntry entry = new() { Matcher = "Bash", CommandValue = "echo hi" };
        Assert.IsTrue(entry.MatcherIsValid,
            "'Bash' is a known tool — MatcherIsValid must be true.");
    }

    [TestMethod]
    public void HookEntry_UnknownMatcher_MatcherIsValidFalse()
    {
        HookEntry entry = new() { Matcher = "NotATool", CommandValue = "echo hi" };
        Assert.IsFalse(entry.MatcherIsValid,
            "'NotATool' is not a known tool — MatcherIsValid must be false.");
    }

    [TestMethod]
    public void HookEntry_BlankCommandValue_HasValidationWarningTrue()
    {
        HookEntry entry = new() { Matcher = "Bash", CommandValue = string.Empty };
        Assert.IsTrue(entry.HasValidationWarning,
            "Empty CommandValue must set HasValidationWarning=true.");
    }

    [TestMethod]
    public void HookEventGroup_HasAnyHookWithWarning_TrueWhenAnyEntryIsInvalid()
    {
        HookEventGroup group = new("PreToolUse");
        group.Hooks.Add(new HookEntry { Matcher = "Bash", CommandValue = "echo hi" }); // valid
        group.Hooks.Add(new HookEntry { Matcher = string.Empty, CommandValue = "echo" }); // invalid
        Assert.IsTrue(group.HasAnyHookWithWarning,
            "HasAnyHookWithWarning must be true when at least one hook has an empty Matcher.");
    }

    // -----------------------------------------------------------------------
    // SDK-backed read path
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new();
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            client.Hooks.Add(new HookEvent(
                "PreToolUse",
                "Bash",
                HookCommandType.Command,
                "echo from-sdk"));

            // LayeredValue carries a different hook — must be ignored.
            JsonObject divergent = new()
            {
                ["PostToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Edit",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = "echo from-layered",
                            },
                        },
                    },
                },
            };
            LayeredValue layered = LayeredWithHooks(ConfigScope.User, divergent);

            HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            // The PreToolUse group must contain the SDK-backed hook;
            // PostToolUse must NOT have the divergent layered hook.
            HookEventGroup? preToolUse = vm.EventGroups.FirstOrDefault(g => g.EventName == "PreToolUse");
            Assert.IsNotNull(preToolUse);
            Assert.AreEqual(1, preToolUse!.Hooks.Count, "SDK path must populate PreToolUse only.");
            Assert.AreEqual("Bash", preToolUse.Hooks[0].Matcher);
            Assert.AreEqual(HookCommandType.Command, preToolUse.Hooks[0].CommandType);
            Assert.AreEqual("echo from-sdk", preToolUse.Hooks[0].CommandValue);

            HookEventGroup? postToolUse = vm.EventGroups.FirstOrDefault(g => g.EventName == "PostToolUse");
            Assert.IsNotNull(postToolUse);
            Assert.AreEqual(0, postToolUse!.Hooks.Count,
                "Divergent PostToolUse hook in the LayeredValue argument must be ignored when an SDK client is supplied.");
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
            ["PreToolUse"] = new JsonArray
            {
                new JsonObject
                {
                    ["matcher"] = "Edit",
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = "echo legacy",
                        },
                    },
                },
            },
        };
        LayeredValue layered = LayeredWithHooks(ConfigScope.User, legacy);

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client: null);
        vm.LoadFromLayered(layered, ConfigScope.User);

        HookEventGroup? pre = vm.EventGroups.FirstOrDefault(g => g.EventName == "PreToolUse");
        Assert.IsNotNull(pre);
        Assert.AreEqual(1, pre!.Hooks.Count);
        Assert.AreEqual("Edit", pre.Hooks[0].Matcher);
        Assert.AreEqual("echo legacy", pre.Hooks[0].CommandValue);
    }
}