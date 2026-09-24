using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using LibVm = Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

public class PermissionsEditorViewModelTests : IDisposable
{
    private static SchemaNode PermissionsSchema()
    {
        return new SchemaNode("permissions", "permissions") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWithPermissions(ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj, "/fake");
        return new LayeredValue("permissions", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = scope,
        };
    }

    /// <summary>
    /// Returns the flat list of rule strings across every Common Actions tool +
    /// operation group.
    /// </summary>
    private static List<string> FlattenRules(PermissionsEditorViewModel vm)
    {
        return vm.ToolActionGroups
                 .SelectMany(t => t.OperationGroups)
                 .SelectMany(g => g.Items)
                 .Select(i => i.Rule)
                 .ToList();
    }

    [Fact]
    public void InitialState_AllListsEmpty()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        Assert.Empty(vm.AllowList);
        Assert.Empty(vm.DenyList);
        Assert.Empty(vm.AskList);
        Assert.Null(vm.DefaultMode);
    }

    [Fact]
    public void AddAllow_AddsToList()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.NewAllowText = "Bash(git status)";
        vm.AddAllowCommand.Execute(null);

        Assert.Single(vm.AllowList);
        Assert.Equal("Bash(git status)", vm.AllowList[0].Rule);
        Assert.Equal(string.Empty, vm.NewAllowText);
    }

    [Fact]
    public void AddDeny_NoDuplicates()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.NewDenyText = "Bash(rm -rf *)";
        vm.AddDenyCommand.Execute(null);
        vm.NewDenyText = "Bash(rm -rf *)";
        vm.AddDenyCommand.Execute(null);

        Assert.Single(vm.DenyList);
    }

    [Fact]
    public void AddAsk_AddsToList_AndClearsInputAndError()
    {
        // Mirrors the AddAllow / AddDeny coverage so the three
        // commands have explicit tests now that they share a body.
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        // Seed a stale error so we can confirm a successful add clears it.
        vm.NewAskError = "previous error";
        vm.NewAskText = "Bash(git push *)";
        vm.AddAskCommand.Execute(null);

        Assert.Single(vm.AskList);
        // The specifier is preserved verbatim — a trailing " *" (optional args) is
        // NOT rewritten to ":*" (which would change match semantics).
        Assert.Equal("Bash(git push *)", vm.AskList[0].Rule);
        MessageAssert.Equal(string.Empty, vm.NewAskText, "Input must be cleared after a successful add.");
        MessageAssert.Equal(string.Empty, vm.NewAskError, "Error must be cleared after a successful add.");
    }

    [Fact]
    public void AddAllow_InvalidRule_PopulatesErrorAndDoesNotAdd()
    {
        // Validates the error-path branch of the shared TryAddRule helper:
        // a malformed rule must populate NewAllowError and not insert into the list.
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.NewAllowText = "NotAToolName(whatever)";
        vm.AddAllowCommand.Execute(null);

        Assert.Empty(vm.AllowList);
        Assert.False(string.IsNullOrEmpty(vm.NewAllowError),
            "Diagnose() must populate NewAllowError for a syntactically invalid rule.");
        MessageAssert.Equal("NotAToolName(whatever)", vm.NewAllowText,
            "Input is preserved on validation failure so the user can edit and retry.");
    }

    [Fact]
    public void RemoveAllow_RemovesEntry()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.NewAllowText = "Edit(*.cs)";
        vm.AddAllowCommand.Execute(null);
        vm.RemoveAllowCommand.Execute(vm.AllowList[0]);

        Assert.Empty(vm.AllowList);
    }

    [Fact]
    public void EditingRuleInline_MarksModifiedAndRoundTripsThroughJson()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.NewAllowText = "Bash(git status)";
        vm.AddAllowCommand.Execute(null);

        // Simulate a user edit on the inline TextBox.
        vm.AllowList[0].Rule = "Bash(git log)";

        Assert.True(vm.IsModified,
            "Editing a rule inline must flag the editor as modified so ApplyToWorkspace picks it up.");

        JsonArray? arr = (vm.ToJsonValue() as JsonObject)?["allow"] as JsonArray;
        Assert.NotNull(arr);
        Assert.Single(arr!);
        MessageAssert.Equal("Bash(git log)", arr[0]!.GetValue<string>(),
            "ToJsonValue must reflect the edited rule text, not the original.");
    }

    [Fact]
    public void LoadFromLayered_PopulatesAllLists()
    {
        JsonObject obj = new()
        {
            ["defaultMode"] = "default",
            ["allow"] = new JsonArray { "Bash(*)" },
            ["deny"] = new JsonArray { "rm *", "sudo *" },
            ["ask"] = new JsonArray { "WebSearch(*)" },
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, obj), ConfigScope.User);

        Assert.Equal("default", vm.DefaultMode);
        Assert.Single(vm.AllowList);
        Assert.Equal(2, vm.DenyList.Count);
        Assert.Single(vm.AskList);
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void ToJsonValue_ReturnsNull_WhenEmpty()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        Assert.Null(vm.ToJsonValue());
    }

    [Fact]
    public void ToJsonValue_IncludesAllLists()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.DefaultMode = "allow";
        vm.NewAllowText = "Bash(git status)";
        vm.AddAllowCommand.Execute(null);
        vm.NewDenyText = "Bash(rm -rf *)";
        vm.AddDenyCommand.Execute(null);

        JsonObject? node = vm.ToJsonValue() as JsonObject;
        Assert.NotNull(node);
        Assert.Equal("allow", node!["defaultMode"]!.GetValue<string>());
        Assert.Single((node["allow"] as JsonArray)!);
        Assert.Single((node["deny"] as JsonArray)!);
    }

    [Fact]
    public void Reset_ClearsAllLists()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.DefaultMode = "deny";
        vm.NewAllowText = "Bash(ls)";
        vm.AddAllowCommand.Execute(null);
        vm.ResetToInheritedCommand.Execute(null);

        Assert.Null(vm.DefaultMode);
        Assert.Empty(vm.AllowList);
        Assert.False(vm.IsModified);
    }

    // -----------------------------------------------------------------------
    // Common Actions — new tests
    // -----------------------------------------------------------------------

    [Fact]
    public void CommonActions_ExcludesRulesAlreadyInAllowList()
    {
        // A rule already present in the editing scope must not appear in Common Actions.
        JsonObject obj = new() { ["allow"] = new JsonArray { "Read" } };
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, obj), ConfigScope.User);

        List<string> rules = FlattenRules(vm);
        MessageAssert.DoesNotContain("Read", rules,
            "Read is already in allow — must be hidden from Common Actions.");
        MessageAssert.Contains("Glob", rules, "Glob is not in any list — must remain visible in Common Actions.");
    }

    [Fact]
    public void CommonActions_ExcludesRulesInheritedFromAncestorScope()
    {
        // A rule set in an ancestor (User) scope must be excluded from Common Actions
        // even when the editing (Local) scope has no explicit value for that rule.
        JsonObject userObj = new() { ["allow"] = new JsonArray { "Bash" } };

        // Local scope has no value; User scope has allow:["Bash"].
        ScopeEntry[] entries =
        [
            new(ConfigScope.Local, null, "/fake-local"),
            new(ConfigScope.User, userObj, "/fake-user"),
        ];
        LayeredValue layered = new("permissions", entries)
        {
            EffectiveValue = userObj,
            EffectiveScope = ConfigScope.User,
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.Local);
        vm.LoadFromLayered(layered, ConfigScope.Local);

        // AllowList is empty because Local has no value — but "Bash" must still be hidden.
        MessageAssert.Equal(0, vm.AllowList.Count, "Editing scope (Local) has no allow entries.");
        List<string> rules = FlattenRules(vm);
        MessageAssert.DoesNotContain("Bash", rules,
            "Bash is set in an ancestor scope and must not appear in Common Actions.");
        // A rule not set in any scope should still be present.
        MessageAssert.Contains("Glob", rules,
            "Glob is not set in any scope — it must remain visible in Common Actions.");
    }

    [Fact]
    public void AddToAllow_ViaCommonActionsCommand_AppendsRuleAndRemovesFromCommonActions()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.AddToAllowCommand.Execute("Read");

        Assert.Single(vm.AllowList);
        Assert.Equal("Read", vm.AllowList[0].Rule);
        MessageAssert.DoesNotContain("Read", FlattenRules(vm),
            "After adding Read to Allow, it must be removed from Common Actions.");
    }

    [Fact]
    public void AddToDeny_ViaCommonActionsCommand_AppendsRuleAndRemovesFromCommonActions()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.AddToDenyCommand.Execute("Write");

        Assert.Single(vm.DenyList);
        Assert.Equal("Write", vm.DenyList[0].Rule);
        MessageAssert.DoesNotContain("Write", FlattenRules(vm),
            "After adding Write to Deny, it must be removed from Common Actions.");
    }

    [Fact]
    public void AddToAsk_ViaCommonActionsCommand_AppendsRuleAndRemovesFromCommonActions()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.AddToAskCommand.Execute("WebFetch");

        Assert.Single(vm.AskList);
        Assert.Equal("WebFetch", vm.AskList[0].Rule);
        MessageAssert.DoesNotContain("WebFetch", FlattenRules(vm),
            "After adding WebFetch to Ask, it must be removed from Common Actions.");
    }

    [Fact]
    public void RemoveRule_ReappearsInCommonActions()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        // Add "Read" so it disappears from Common Actions.
        vm.AddToAllowCommand.Execute("Read");
        MessageAssert.DoesNotContain("Read", FlattenRules(vm),
            "Read must be absent from Common Actions immediately after being added to Allow.");

        // Remove it — it should reappear in Common Actions.
        PermissionRuleViewModel entry = vm.AllowList[0];
        vm.RemoveAllowCommand.Execute(entry);

        Assert.Empty(vm.AllowList);
        MessageAssert.Contains("Read", FlattenRules(vm),
            "After removing Read from Allow, it must reappear in Common Actions.");
    }

    [Fact]
    public void CommonActions_AllGroupsHidden_WhenAllRulesSet()
    {
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        // Add every candidate rule; duplicates are silently ignored by AddAllowCommand.
        foreach (string rule in PermissionsEditorViewModel.AllToolGroups
                                                          .SelectMany(t => t.OperationGroups)
                                                          .SelectMany(g => g.Items)
                                                          .Select(i => i.Rule))
        {
            vm.NewAllowText = rule;
            vm.AddAllowCommand.Execute(null);
        }

        MessageAssert.Equal(0, vm.ToolActionGroups.Count,
            "With every candidate rule set, all Common Actions tool groups must be hidden.");
        Assert.False(vm.HasCommonActions,
            "HasCommonActions must be false when ToolActionGroups is empty.");
    }

    // -----------------------------------------------------------------------
    // SDK-backed read path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadFromLayered_WithSdkClient_ReadsThroughTypedAccessor()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-edit-perm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

            client.Permissions.AddAllow(new PermissionRule("Bash(git status)"));
            client.Permissions.AddDeny(new PermissionRule("Bash(rm *)"));
            client.Permissions.DefaultMode = PermissionDefaultMode.AcceptEdits;

            // Divergent layered argument — must be ignored.
            JsonObject divergent = new()
            {
                ["allow"] = new JsonArray("from-layered/x"),
                ["defaultMode"] = "plan",
            };
            LayeredValue layered = LayeredWithPermissions(ConfigScope.User, divergent);

            PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User, client);
            vm.LoadFromLayered(layered, ConfigScope.User);

            MessageAssert.Equal(1, vm.AllowList.Count, "SDK path should yield exactly the SDK-set allow rule.");
            Assert.Equal("Bash(git status)", vm.AllowList[0].Rule);
            Assert.Single(vm.DenyList);
            Assert.Equal("Bash(rm *)", vm.DenyList[0].Rule);
            MessageAssert.Equal("acceptEdits", vm.DefaultMode,
                "SDK PermissionDefaultMode.AcceptEdits must surface as the camelCase 'acceptEdits' string.");
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

    [Fact]
    public void LoadFromLayered_WithoutSdkClient_FallsBackToLegacyJsonPath()
    {
        JsonObject legacy = new()
        {
            ["allow"] = new JsonArray("Bash(echo *)"),
            ["defaultMode"] = "plan",
        };
        LayeredValue layered = LayeredWithPermissions(ConfigScope.User, legacy);

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User, client: null);
        vm.LoadFromLayered(layered, ConfigScope.User);

        Assert.Single(vm.AllowList);
        Assert.Equal("Bash(echo *)", vm.AllowList[0].Rule);
        Assert.Equal("plan", vm.DefaultMode);
    }

    // ── Force-fire delete-after-load ──────────────

    [Fact]
    public void DeleteAfterLoad_FiresIsModified_ForceFireContract()
    {
        // CommunityToolkit's [ObservableProperty]-generated setter elides
        // equal assignments. After LoadFromLayered() flips IsModified=true
        // (because the scope had an explicit value), a subsequent user
        // delete that DOESN'T flip IsModified back to false would never
        // re-raise PropertyChanged(IsModified), leaving SettingsGroupEditor's
        // live-write loop unwired. The base-class MarkModified explicitly
        // re-raises in that case. This test locks
        // that behavior across delete after load.
        JsonObject loaded = new()
        {
            ["allow"] = new JsonArray("Bash(git status)", "Read"),
        };
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, loaded), ConfigScope.User);
        Assert.True(vm.IsModified, "Precondition: load with non-empty allow flags IsModified=true.");

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PermissionsEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        // Delete a loaded rule. IsModified stays true (still non-empty),
        // so the [ObservableProperty] setter elides the assignment — but
        // the explicit re-raise in MarkModified must fire.
        vm.AllowList.RemoveAt(0);

        Assert.True(fired >= 1,
            "Deleting a loaded rule must fire PropertyChanged(IsModified) so the live-write " +
            "runs and Save enables — even though IsModified stays latched true.");
    }

    // ── Reset-bug regression (smoke) ──────

    [Fact]
    public void OnResetToInherited_AfterLoad_RestoresOnDiskRules_NotClearsThem()
    {
        // Regression: prior to the fix, OnResetToInherited called Clear() on
        // AllowList / DenyList / AskList unconditionally, silently destroying
        // the user's saved rules when they hit Reset after editing.  The fix
        // captures _baselinePermissionsValue at LoadFromLayered and reloads
        // from _lastLayered in OnResetToInherited (mirrors HooksEditorViewModel
        // commit 6861748 + MarketplacesEditorViewModel reset-restore pattern).
        // Locks the contract that "Reset = undo unsaved edits, not wipe everything"
        // — the symptom smoke surfaced ("deletion does
        // enable save, but then reset button clears the UI of all the
        // permissions").
        JsonObject loaded = new()
        {
            ["allow"] = new JsonArray("Bash(git status)", "Read", "Glob"),
            ["deny"] = new JsonArray("Bash(rm -rf *)"),
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, loaded), ConfigScope.User);
        MessageAssert.Equal(3, vm.AllowList.Count, "precondition: 3 allow rules loaded");
        MessageAssert.Equal(1, vm.DenyList.Count, "precondition: 1 deny rule loaded");
        Assert.True(vm.IsModified);

        // User edits: delete one allow rule + add a new ask rule.
        vm.AllowList.RemoveAt(0);
        Assert.Equal(2, vm.AllowList.Count);

        vm.NewAskText = "Bash(npm install *)";
        vm.AddAskCommand.Execute(null);
        Assert.Single(vm.AskList);

        // User clicks Reset: must restore the original on-disk state — 3 allow
        // rules, 1 deny rule, 0 ask rules — NOT wipe to empty.
        vm.ResetToInheritedCommand.Execute(null);

        MessageAssert.Equal(3, vm.AllowList.Count,
            "Reset must restore on-disk allow rules, not wipe to empty.");
        MessageAssert.Equal(1, vm.DenyList.Count,
            "Reset must restore on-disk deny rules.");
        MessageAssert.Equal(0, vm.AskList.Count,
            "Reset must drop the unsaved ask addition.");
        MessageAssert.SameElements(
            new[] { "Bash(git status)", "Read", "Glob" },
            vm.AllowList.Select(r => r.Rule).ToList());
    }

    [Fact]
    public void OnResetToInherited_AfterLoad_ClearsAskListUnsavedAdditions()
    {
        // Companion test: load with Allow + Deny only, user adds Ask, hits Reset.
        // Reset must drop the Ask addition AND keep Allow/Deny intact (the
        // baseline didn't have an "ask" key, so it stays absent on restore).
        JsonObject loaded = new()
        {
            ["allow"] = new JsonArray("Read"),
        };

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, loaded), ConfigScope.User);
        Assert.Single(vm.AllowList);

        vm.NewAskText = "Bash(git push *)";
        vm.AddAskCommand.Execute(null);
        Assert.Single(vm.AskList);

        vm.ResetToInheritedCommand.Execute(null);

        MessageAssert.Equal(1, vm.AllowList.Count, "Reset must keep the original allow rule.");
        MessageAssert.Equal(0, vm.AskList.Count, "Reset must drop the unsaved ask addition.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  WSL Common Actions group — Windows-only gating (2026-05-20)
    //
    //  See plan: ~/.claude/plans/some-features-of-this-flickering-starlight.md
    //  Tests lock both: (a) the static AllToolGroups contains the WSL entry,
    //  (b) the platform filter (VisibleToolGroupsForPlatform) hides it on
    //  non-Windows hosts AND keeps it hidden across rule-edit rebuilds.
    // ═══════════════════════════════════════════════════════════════════════

    private void Cleanup()
    {
        PlatformInfo.ResetForTesting();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void BuildToolGroups_IncludesWslGroup_WithExpectedOperationGroups()
    {
        // Pure static-data assertion: AllToolGroups is unconditional, the
        // platform filter is applied later when assigning ToolActionGroups.
        ToolActionGroup? wsl = PermissionsEditorViewModel.AllToolGroups
                                                         .SingleOrDefault(g => g.Tool == "WSL");

        MessageAssert.NotNull(wsl, "AllToolGroups must include a tool group named 'WSL'.");
        MessageAssert.Equal(5, wsl!.OperationGroups.Count,
            "WSL group must have 5 operation groups (SearchView, GitRead, GitWrite, Runtimes, Network).");

        List<string> allRules = wsl.OperationGroups.SelectMany(g => g.Items).Select(i => i.Rule).ToList();
        MessageAssert.Contains("Bash(wsl ls *)", allRules,
            "SearchView operation must include `Bash(wsl ls *)`.");
        MessageAssert.Contains("Bash(wsl git status)", allRules,
            "GitRead operation must include `Bash(wsl git status)`.");
        MessageAssert.Contains("Bash(wsl git add *)", allRules,
            "GitWrite operation must include `Bash(wsl git add *)`.");
        MessageAssert.Contains("Bash(wsl npm *)", allRules,
            "Runtimes operation must include `Bash(wsl npm *)`.");
        MessageAssert.Contains("Bash(wsl curl *)", allRules,
            "Network operation must include `Bash(wsl curl *)`.");
    }

    [Theory]
    [InlineData("linux")]
    [InlineData("macos")]
    public void ToolActionGroups_OnNonWindows_OmitsWslGroup(string platformId)
    {
        // Emulate a non-Windows host BEFORE constructing the VM so the ctor
        // path (VisibleToolGroupsForPlatform at the first assignment site)
        // sees the override.  PlatformInfo.ResetForTesting in TestCleanup
        // restores the real host platform.
        PlatformInfo.OverrideForDebug(
            EmulatedPlatformInfo.ForId(platformId));

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        Assert.False(vm.ToolActionGroups.Any(g => g.Tool == "WSL"),
            $"WSL group must NOT appear in ToolActionGroups when emulating {platformId}.");
        Assert.True(vm.ToolActionGroups.Any(g => g.Tool == "PowerShell"),
            "Other groups (PowerShell here) must still appear — only WSL is platform-gated.");
    }

    [Fact]
    public void ToolActionGroups_OnWindows_IncludesWslGroup()
    {
        PlatformInfo.OverrideForDebug(
            EmulatedPlatformInfo.ForId("windows"));

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        ToolActionGroup? wsl = vm.ToolActionGroups.SingleOrDefault(g => g.Tool == "WSL");
        MessageAssert.NotNull(wsl, "WSL group must appear in ToolActionGroups when emulating Windows.");
        MessageAssert.Equal(5, wsl!.OperationGroups.Count,
            "All 5 WSL operation groups must be present on Windows.");
    }

    [Fact]
    public void RebuildToolGroups_OnNonWindows_KeepsWslGroupHidden_AfterRuleEdit()
    {
        // Locks the SECOND filter site (the one in RebuildToolGroups, fired
        // by OnListChanged whenever the user adds/removes a rule).  Without
        // the filter in that second site, a non-WSL rule edit would re-
        // assign ToolActionGroups from raw AllToolGroups and the WSL group
        // would silently reappear on non-Windows hosts.
        PlatformInfo.OverrideForDebug(
            EmulatedPlatformInfo.ForId("linux"));

        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);
        Assert.False(vm.ToolActionGroups.Any(g => g.Tool == "WSL"),
            "Baseline: WSL group absent at construction time on Linux.");

        // Trigger a rule-list edit — any non-WSL rule will do.
        vm.NewAllowText = "Bash(git status)";
        vm.AddAllowCommand.Execute(null);

        Assert.False(vm.ToolActionGroups.Any(g => g.Tool == "WSL"),
            "WSL group must remain absent after a non-WSL rule edit triggers "
            + "RebuildToolGroups.  If this fails, the second VisibleToolGroupsForPlatform "
            + "call is missing in RebuildToolGroups.");
    }

    [Fact]
    public void CommonActions_AddingRule_PreservesExpandedAccordion()
    {
        // Regression: clicking an Allow/Deny/Ask button on the Common tab triggers
        // RebuildCommonActions, which recreates the ToolActionGroup objects. The
        // accordion the user had expanded must survive the rebuild — previously the
        // freshly-built (collapsed) Expander replaced the open one, collapsing it and
        // preventing easy clicks on the category's other buttons.
        PermissionsEditorViewModel vm = new(PermissionsSchema(), ConfigScope.User);

        ToolActionGroup bash = vm.ToolActionGroups.First(t => t.Tool == "Bash" && !t.IsCatchAll);
        bash.IsExpanded = true;

        // Add a rule from a DIFFERENT tool (File/Read) so the Bash group survives the
        // rebuild intact — this isolates expansion preservation from group removal.
        vm.AddToAllowCommand.Execute("Read");

        ToolActionGroup? bashAfter = vm.ToolActionGroups.FirstOrDefault(t => t.Tool == "Bash" && !t.IsCatchAll);
        MessageAssert.NotNull(bashAfter, "Precondition: the Bash group must still exist after adding an unrelated rule.");
        Assert.True(bashAfter!.IsExpanded,
            "The expanded Bash accordion must stay expanded after adding a rule — it must not collapse.");

        // A group the user never expanded must NOT spuriously expand across the rebuild.
        ToolActionGroup? file = vm.ToolActionGroups.FirstOrDefault(t => t.Tool == "File" && !t.IsCatchAll);
        MessageAssert.NotNull(file, "Precondition: the File group survives (keeps Glob/Grep after Read is added).");
        Assert.False(file!.IsExpanded, "A never-expanded group must remain collapsed across the rebuild.");
    }

    [Theory]
    [InlineData("Bash(wsl ls *)")]
    [InlineData("Bash(wsl find *)")]
    [InlineData("Bash(wsl grep *)")]
    [InlineData("Bash(wsl cat *)")]
    [InlineData("Bash(wsl git status)")]
    [InlineData("Bash(wsl git log *)")]
    [InlineData("Bash(wsl git diff *)")]
    [InlineData("Bash(wsl git add *)")]
    [InlineData("Bash(wsl git commit *)")]
    [InlineData("Bash(wsl git push *)")]
    [InlineData("Bash(wsl dotnet *)")]
    [InlineData("Bash(wsl npm *)")]
    [InlineData("Bash(wsl npm run *)")]
    [InlineData("Bash(wsl node *)")]
    [InlineData("Bash(wsl python *)")]
    [InlineData("Bash(wsl python3 *)")]
    [InlineData("Bash(wsl curl *)")]
    [InlineData("Bash(wsl wget *)")]
    public void WslSampleRules_PassPermissionRuleValidation(string rule)
    {
        // Locks the rule-pattern contract: if the $defs.permissionRule
        // regex ever tightens in a way that breaks `Bash(wsl …)`, this
        // test fails first and points at the WSL canonical-rule set as
        // the right thing to revisit.
        Assert.True(PermissionRuleViewModel.IsValid(rule),
            $"Canonical WSL rule '{rule}' must pass PermissionRuleViewModel.IsValid. "
            + $"Diagnose: {PermissionRuleViewModel.Diagnose(rule)}");
    }

    // -----------------------------------------------------------------------
    // Schema-coverage guarantee — every permissions schema key is editable.
    //
    // The bespoke editor renders allow/deny/ask/defaultMode/disableBypass/
    // additionalDirectories; every OTHER schema key (here: disableAutoMode)
    // must be auto-surfaced via the generic factory, NOT dropped into the
    // invisible preserved bag. These tests fail on the pre-feature editor,
    // which routed disableAutoMode through the load switch's
    // default: -> _preservedFields (round-tripped but invisible / uneditable).
    // -----------------------------------------------------------------------

    /// <summary>
    /// A realistic (subset) permissions schema: two keys the editor covers
    /// bespoke-ly (allow, defaultMode) plus one it does NOT (disableAutoMode,
    /// an enum). Real schema declares <c>additionalProperties:false</c> + an
    /// explicit <c>properties</c> map, so SchemaNode.Properties is exhaustive.
    /// </summary>
    private static SchemaNode PermissionsSchemaWithChildren()
    {
        return new SchemaNode("permissions", "permissions")
        {
            ValueType = SchemaValueType.Complex,
            Properties =
            [
                new SchemaNode("permissions.allow", "allow") { ValueType = SchemaValueType.Array },
                new SchemaNode("permissions.defaultMode", "defaultMode")
                {
                    ValueType = SchemaValueType.Enum,
                    EnumValues = ["default", "acceptEdits"],
                },
                new SchemaNode("permissions.disableAutoMode", "disableAutoMode")
                {
                    ValueType = SchemaValueType.Enum,
                    EnumValues = ["disable"],
                },
            ],
        };
    }

    [Fact]
    public void UncoveredSchemaKey_IsAutoSurfacedEditably_NotDroppedToPreservedBag()
    {
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User, client: null, factory);

        // disableAutoMode is not a bespoke-rendered key, so it must be auto-surfaced.
        LibVm.PropertyEditorViewModel? surfaced =
            vm.AutoSurfacedEditors.SingleOrDefault(e => e.Schema.Name == "disableAutoMode");
        MessageAssert.NotNull(surfaced, "disableAutoMode must be auto-surfaced as an editable editor.");

        // ...and as the by-TYPE editor (enum -> dropdown), never the String/raw fallback.
        MessageAssert.Equal("EnumPropertyEditorViewModel", surfaced!.GetType().Name,
            "An enum schema key must auto-surface as an enum dropdown, not a string/raw editor.");

        // NO bespoke-covered key may be auto-surfaced (else it double-renders).
        // Check the full covered set, not just a sample, so dropping any one key
        // from CoveredKeys is caught.
        IReadOnlySet<string> coveredKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "defaultMode", "allow", "deny", "ask",
            "disableBypassPermissionsMode", "additionalDirectories",
        };
        Assert.False(vm.AutoSurfacedEditors.Any(e => coveredKeys.Contains(e.Schema.Name)),
            "No bespoke-covered key may be auto-surfaced (would double-render).");
    }

    [Fact]
    public void UncoveredSchemaKey_RoundTripsThroughAutoSurfacedEditor()
    {
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User, client: null, factory);

        JsonObject perms = new() { ["disableAutoMode"] = "disable" };
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, perms), ConfigScope.User);

        // Prove the value loaded INTO the auto-surfaced editor — not silently into
        // _preservedFields, which would ALSO round-trip and mask a broken route.
        // This is the real regression guard; the ToJsonValue check below alone
        // would pass via the preserved bag.
        LibVm.EnumPropertyEditorViewModel enumEditor =
            (LibVm.EnumPropertyEditorViewModel)vm.AutoSurfacedEditors.Single(e => e.Schema.Name == "disableAutoMode");
        MessageAssert.Equal("disable", enumEditor.SelectedValue,
            "disableAutoMode must load into its auto-surfaced editor, not the opaque preserved bag.");

        JsonObject? result = vm.ToJsonValue() as JsonObject;
        MessageAssert.NotNull(result, "A permissions object with a set key must serialize to a JsonObject.");
        MessageAssert.Equal("disable", (string?)result!["disableAutoMode"],
            "disableAutoMode must round-trip through its auto-surfaced editor, not be dropped.");
    }

    [Fact]
    public void EditingAutoSurfacedChild_MarksEditorModified()
    {
        // The Save-enable payoff: a user edit to an auto-surfaced child must
        // bubble up so SettingsGroupEditorViewModel re-serializes the group.
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User, client: null, factory);

        // Baseline: no permissions value at this scope -> not modified.
        vm.LoadFromLayered(new LayeredValue("permissions", []), ConfigScope.User);
        Assert.False(vm.IsModified, "Baseline: an unset scope must not be modified.");

        // Simulate the user picking the enum value in the auto-surfaced dropdown.
        LibVm.EnumPropertyEditorViewModel enumEditor =
            (LibVm.EnumPropertyEditorViewModel)vm.AutoSurfacedEditors.Single(e => e.Schema.Name == "disableAutoMode");
        enumEditor.SelectedValue = "disable";

        Assert.True(enumEditor.IsModified,
            "Setting the child's value must mark the child modified (precondition for the bubble).");
        Assert.True(vm.IsModified,
            "Editing an auto-surfaced child must bubble up and mark the permissions editor modified.");
    }

    [Fact]
    public void NoFactory_AutoSurfacedEditorsEmpty_BackCompatPreserved()
    {
        // Legacy / test fixtures construct without a factory; nothing is
        // auto-surfaced and behaviour matches the pre-feature editor (the
        // 27 existing fixtures above all use this no-factory path).
        PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User);
        MessageAssert.Equal(0, vm.AutoSurfacedEditors.Count,
            "Without an editor factory the editor must not auto-surface any keys.");
    }

    [Fact]
    public void UncoveredSchemaKey_UnsupportedShape_IsStillSurfaced_NotSilentlyDropped()
    {
        // The no-silent-drop guarantee must hold even for a key whose shape the
        // factory can't classify (Complex with no properties): it falls back to
        // the validated raw-JSON editor (editable), never a silent drop.
        SchemaNode schema = new SchemaNode("permissions", "permissions")
        {
            ValueType = SchemaValueType.Complex,
            Properties =
            [
                new SchemaNode("permissions.futureBlob", "futureBlob") { ValueType = SchemaValueType.Complex },
            ],
        };
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PermissionsEditorViewModel vm = new(schema, ConfigScope.User, client: null, factory);

        LibVm.PropertyEditorViewModel? surfaced =
            vm.AutoSurfacedEditors.SingleOrDefault(e => e.Schema.Name == "futureBlob");
        MessageAssert.NotNull(surfaced, "An unsupported-shape uncovered key must still be surfaced (editable), not dropped.");
        MessageAssert.Equal("JsonRawPropertyEditorViewModel", surfaced!.GetType().Name,
            "An unclassifiable shape must fall back to the validated raw-JSON editor.");
    }

    [Fact]
    public void ToJsonValue_NativeAndAutoSurfacedKeys_CoexistWithCorrectValues()
    {
        // A covered key (defaultMode, native path) and an uncovered key
        // (disableAutoMode, auto-surfaced path) must both round-trip with the
        // right values — the native field owns its key, the auto-surfaced field
        // contributes its own, neither clobbers the other.
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User, client: null, factory);
        Assert.False(vm.AutoSurfacedEditors.Any(e => e.Schema.Name == "defaultMode"),
            "defaultMode is covered; it must not also be auto-surfaced.");

        JsonObject perms = new() { ["defaultMode"] = "default", ["disableAutoMode"] = "disable" };
        vm.LoadFromLayered(LayeredWithPermissions(ConfigScope.User, perms), ConfigScope.User);

        JsonObject? result = vm.ToJsonValue() as JsonObject;
        Assert.NotNull(result);
        MessageAssert.Equal("default", (string?)result!["defaultMode"],
            "Covered key must round-trip via the native path.");
        MessageAssert.Equal("disable", (string?)result["disableAutoMode"],
            "Uncovered key must round-trip via the auto-surfaced editor.");
    }

    [Fact]
    public async Task SdkPath_AutoSurfacedKey_RoundTripsAlongsideSdkSourcedRules()
    {
        // Production config: an SDK client is present (rules read through the
        // typed accessor) AND an auto-surfaced key (disableAutoMode) is read from
        // the layered value. Both must compose — the rule from the SDK, the
        // auto-surfaced key from layered — and ToJsonValue must emit both.
        string tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-autosurface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string? previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = tempDir;
        try
        {
            using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
            await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
            client.Permissions.AddAllow(new PermissionRule("Bash(git status)"));

            JsonObject perms = new()
            {
                ["allow"] = new JsonArray("Bash(git status)"),
                ["disableAutoMode"] = "disable",
            };
            LayeredValue layered = LayeredWithPermissions(ConfigScope.User, perms);

            // The factory only needs to build the generic child editor; pass the
            // SDK client as the VM's read client.
            CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
            PermissionsEditorViewModel vm = new(PermissionsSchemaWithChildren(), ConfigScope.User, client, factory);
            vm.LoadFromLayered(layered, ConfigScope.User);

            MessageAssert.Equal(1, vm.AllowList.Count, "Rule must come from the SDK accessor.");
            Assert.True(vm.AutoSurfacedEditors.Any(e => e.Schema.Name == "disableAutoMode"),
                "disableAutoMode must be auto-surfaced even in the SDK-backed path.");

            JsonObject? result = vm.ToJsonValue() as JsonObject;
            Assert.NotNull(result);
            MessageAssert.Equal("disable", (string?)result!["disableAutoMode"],
                "Auto-surfaced disableAutoMode must round-trip in the SDK-backed configuration.");
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
}