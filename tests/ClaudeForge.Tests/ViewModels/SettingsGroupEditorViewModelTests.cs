using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using PropertyEditorViewModel = Bennewitz.Ninja.ScopedEditors.ViewModels.PropertyEditorViewModel;
// App-bridge StringPropertyEditorViewModel deleted; alias library leaf.
using StringPropertyEditorViewModel = Bennewitz.Ninja.ScopedEditors.ViewModels.StringPropertyEditorViewModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

public partial class SettingsGroupEditorViewModelTests
{
    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs, ClaudeMergePolicy.Instance);
    }

    private static SchemaNode MakeNode(string jsonPath, string name,
                                       SchemaValueType type = SchemaValueType.String)
    {
        return new SchemaNode(jsonPath, name) { ValueType = type };
    }

    [Fact]
    public void RebuildEditors_PopulatesEditors()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("maxTokens", "maxTokens", SchemaValueType.Integer),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        Assert.Equal(3, vm.Editors.Count);
    }

    [Fact]
    public void SelectedTab_DefaultsToFirstTab()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        Assert.NotNull(vm.SelectedTab);
        Assert.Equal(GroupTab.PropertiesId, vm.SelectedTab.Id);
    }

    [Fact]
    public void SelectTab_SelectsById()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.SelectTab(GroupTab.JsonId);

        Assert.Equal(GroupTab.JsonId, vm.SelectedTab?.Id);
    }

    [Fact]
    public void SelectedTab_PreservedAcrossRebuild()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        vm.Activate(); // so EditingScope changes rebuild immediately

        vm.SelectTab(GroupTab.JsonId);
        vm.EditingScope = ConfigScope.Project; // triggers RebuildEditors → RebuildTabs

        // The remembered tab survives the rebuild (Tabs is rebuilt with fresh
        // instances, matched back by Id).
        Assert.Equal(GroupTab.JsonId, vm.SelectedTab?.Id);
    }

    [Fact]
    public void FilterText_Empty_ReturnsAllEditors()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("maxTokens", "maxTokens", SchemaValueType.Integer),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "";

        // Editors is now IReadOnlyList; CollectionAssert needs a concrete ICollection.
        Assert.Equal(vm.Editors.ToList(), vm.FilteredEditors.ToList());
    }

    [Fact]
    public void FilterText_MatchesDisplayName_FiltersCorrectly()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "mod";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.Single(filtered);
        Assert.Equal("model", filtered[0].Path);
    }

    [Fact]
    public void FilterText_MatchesJsonPath_FiltersCorrectly()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("permissions.allow", "allow"),
            MakeNode("permissions.deny", "deny"),
            MakeNode("model", "model"),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "permissions";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void FilterText_NoMatch_ReturnsEmpty()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "zzznomatch";

        Assert.Empty(vm.FilteredEditors);
    }

    /// <summary>
    /// searching for a sub-path of a specialized editor
    /// (like <c>"permissions.additionalDirectories"</c> when the page hosts a
    /// non-Object specialized editor at path <c>"permissions"</c>) used to
    /// return zero results because the substring check is one-directional
    /// (<c>editor.Path.Contains(filter)</c> is false when the filter is longer).
    /// The fix added an "is sub-path of editor.Path" branch so a click on a
    /// search hit deep-links to the right page even for nested properties.
    /// </summary>
    [Fact]
    public void FilterText_SubPathOfEditorPath_YieldsThatEditor()
    {
        // Single editor at path "model".  Filter "model.subProp" should match
        // the editor whole because the filter targets a sub-property of an
        // editor that doesn't decompose further.  (Real-world case is the
        // Permissions specialized editor + a filter like
        // "permissions.additionalDirectories".)
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "model.notARealSubProperty";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        MessageAssert.Equal(1, filtered.Count,
            "Filter targeting a sub-path of a non-Object editor must yield that editor whole.");
        Assert.Equal("model", filtered[0].Path);
    }

    /// <summary>
    /// Guard the inverse — a sub-path filter must NOT bring back a sibling
    /// editor.  Filter "model.x" should not yield "verbose".
    /// </summary>
    [Fact]
    public void FilterText_SubPathOfOneEditor_DoesNotMatchSiblings()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "model.something";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.False(filtered.Any(e => e.Path == "verbose"),
            "Sub-path filter must not yield unrelated sibling editors.");
    }

    [Fact]
    public void OnEditingScopeChanged_RebuildsEditors()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"sonnet"}"""),
            (ConfigScope.Project, """{"model":"haiku"}"""));

        // Both Project and User must be in AvailableScopes before the scope change:
        // the guard in OnEditingScopeChanged rejects scopes not in the available set
        // (to defend against Avalonia binding artefacts during DataContext switches).
        SharedScopeContext ctx = new();
        ctx.AvailableScopes = [ConfigScope.User, ConfigScope.Project];
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        Assert.Equal("sonnet", ((StringPropertyEditorViewModel)vm.Editors[0]).Value);

        vm.EditingScope = ConfigScope.Project;

        Assert.Equal("haiku", ((StringPropertyEditorViewModel)vm.Editors[0]).Value);
    }

    [Fact]
    public void ApplyToWorkspace_FlushesEditorValuesToWorkspace()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        ((StringPropertyEditorViewModel)vm.Editors[0]).Value = "opus";
        vm.ApplyToWorkspace();

        LayeredValue layered = workspace.GetLayeredValue("model");
        Assert.Equal("opus", layered.EffectiveValue!.GetValue<string>());
    }

    /// <summary>
    /// user-reported bug where an Essentials-page
    /// write to <c>env.CLAUDE_CODE_MAX_OUTPUT_TOKENS</c> vanished on save.
    /// Root cause: the schema-driven Environment group editor was loaded
    /// with <c>IsModified=true</c> (its scope had data — the existing env
    /// map), and <see cref="SettingsGroupEditorViewModel.ApplyToWorkspace"/>
    /// flushed the editor's in-memory env snapshot — which didn't know
    /// about the Essentials write — back to the SDK, clobbering it.  The
    /// fix gates the flush on a per-path user-touched set populated only
    /// by <c>OnEditorPropertyChanged</c> (which fires only on post-load
    /// user edits, not on load).
    /// <para>
    /// This test simulates the exact race: load the group editor over a
    /// workspace whose User doc already has data (so all editors load
    /// with IsModified=true), then write to a key the group editor knows
    /// about via the workspace directly (simulating an out-of-band SDK
    /// write from a different VM like Essentials), then call
    /// <see cref="SettingsGroupEditorViewModel.ApplyToWorkspace"/> and
    /// assert the out-of-band write SURVIVES — i.e. the group editor did
    /// NOT flush its stale snapshot.
    /// </para>
    /// </summary>
    [Fact]
    public void ApplyToWorkspace_DoesNotClobberOutOfBandWrites_OnUntouchedEditors()
    {
        // Use "env" as the schema path with a Complex type so the factory
        // produces a JsonRawPropertyEditorViewModel (or similar map-shaped
        // editor) that aggregates the whole env object as a single value —
        // mirrors the production scenario.
        List<SchemaNode> nodes = [MakeNode("env", "env", SchemaValueType.Complex)];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User,
            """{"env":{"EXISTING":"keep"}}"""));

        // Construct the group editor — this loads the env editor, which
        // (per the compound-editor convention) sets IsModified=true
        // because the User scope has data.
        SettingsGroupEditorViewModel vm = new("Environment", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        // Sanity: the editor loaded the existing env value.
        Assert.Single(vm.Editors);

        // Simulate an out-of-band SDK write (e.g. EssentialsViewModel
        // writing env.CLAUDE_CODE_MAX_OUTPUT_TOKENS while this group
        // editor's in-memory snapshot still has only EXISTING).
        JsonObject newEnv = new()
        {
            ["EXISTING"] = "keep",
            ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = "60000",
        };
        workspace.SetValue("env", newEnv, ConfigScope.User);

        // ApplyToWorkspace must NOT flush the env editor's stale snapshot
        // back over our out-of-band write.
        vm.ApplyToWorkspace();

        // The out-of-band write must survive.  Before the fix this assertion
        // failed: the group editor flushed its stale snapshot back, dropping
        // CLAUDE_CODE_MAX_OUTPUT_TOKENS.
        LayeredValue layered = workspace.GetLayeredValue("env");
        JsonObject? effective = layered.EffectiveValue as JsonObject;
        MessageAssert.NotNull(effective, "Effective env value should be a JsonObject.");
        Assert.True(effective.ContainsKey("CLAUDE_CODE_MAX_OUTPUT_TOKENS"),
            "Out-of-band write to env.CLAUDE_CODE_MAX_OUTPUT_TOKENS was clobbered by " +
            "ApplyToWorkspace.  The group editor flushed its stale in-memory env snapshot " +
            "(which didn't include the out-of-band key) back over the workspace.  This is " +
            "to make sure the _userEditedPaths gate is intact.");
        Assert.Equal("60000",
            effective["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]!.GetValue<string>());
    }

    /// <summary>
    /// <b>F9 regression, end to end through the flush the defect actually used.</b>
    /// Editing ONE modelled env key deleted every env key the schema does not model:
    /// the object editor rebuilt <c>env</c> from its children, and a key with no child
    /// came out the other side as a key the user had removed.
    /// <para>
    /// The sibling test above covers the opposite gate — an UNTOUCHED editor must not
    /// flush at all. This one covers the touched case, which that gate deliberately lets
    /// through, and which is where the keys were lost.
    /// </para>
    /// </summary>
    [Fact]
    public void ApplyToWorkspace_TouchedObjectEditor_KeepsEnvKeysTheSchemaDoesNotModel()
    {
        // env as a real object node with exactly one modelled child, mirroring the
        // production shape: the schema names some variables and knows nothing of the rest.
        SchemaNode env = new("env", "env")
        {
            ValueType = SchemaValueType.Object,
            Properties = [MakeNode("env.ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY")],
        };

        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User,
            """{"env":{"ANTHROPIC_API_KEY":"old","MY_CUSTOM_TOOL_PATH":"/opt/thing","RETEST_MARKER":"marker"}}"""));

        SettingsGroupEditorViewModel vm = new("Environment", [env], workspace,
            ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        ObjectPropertyEditorViewModel objectEditor =
            (ObjectPropertyEditorViewModel)vm.Editors[0];
        MessageAssert.Equal(1, objectEditor.Children.Count,
            "Premise: exactly one key is modelled, so the other two are the unmodelled ones "
            + "this test is about. Model them all and the test proves nothing.");

        // A real user edit on the modelled child — this is what populates _userEditedPaths
        // and so opens the flush gate. Anything weaker and ApplyToWorkspace skips the
        // editor entirely, which would make the assertions below vacuous.
        ((StringPropertyEditorViewModel)objectEditor.Children[0]).Value = "new";
        vm.ApplyToWorkspace();

        JsonObject? after = workspace.GetLayeredValue("env").EffectiveValue as JsonObject;
        MessageAssert.NotNull(after, "env must still be an object after the flush.");
        MessageAssert.Equal("new", after["ANTHROPIC_API_KEY"]!.GetValue<string>(),
            "The edit must actually reach the workspace, or nothing below was measured.");
        MessageAssert.Equal("/opt/thing", after["MY_CUSTOM_TOOL_PATH"]?.GetValue<string>(),
            "An env key the schema does not model must survive an edit to one that it does. "
            + "This is F9: the user's own variables were deleted by editing a sibling.");
        MessageAssert.Equal("marker", after["RETEST_MARKER"]?.GetValue<string>(),
            "Every unmodelled key survives, not just the first.");
    }

    /// <summary>
    /// A3 regression: the SDK-routed WriteEditorValue ghost-guard must compare the
    /// value at the TARGET (editing) scope, not the cross-scope effective value.
    /// Project shadows User with model="opus"; the user explicitly pins "opus" at
    /// User scope. Comparing effective ("opus") would see no diff and silently drop
    /// the legitimate User-scope pin; comparing the User scope (empty) writes it.
    /// </summary>
    [Fact]
    public void WriteEditorValue_SdkBranch_DoesNotDropShadowedScopePin()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace ws = MakeWorkspace(
            (ConfigScope.Project, """{"model":"opus"}"""),
            (ConfigScope.User, "{}"));
        using AgentConfigClientCore sdk = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());

        SharedScopeContext ctx = new(ConfigScope.User);
        SettingsGroupEditorViewModel vm = new("General", nodes, ws, ctx, ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create(),sdkClient: sdk);

        StringPropertyEditorViewModel editor = (StringPropertyEditorViewModel)vm.Editors[0];
        MessageAssert.Equal(string.Empty, editor.Value ?? string.Empty,
            "Precondition: the editor shows the empty User-scope value, not the shadowing Project value.");

        editor.Value = "opus"; // user pins the inherited value explicitly at User scope
        vm.ApplyToWorkspace();

        MessageAssert.Equal("opus", sdk.GetScopeValue("model", ConfigScope.User)?.GetValue<string>(),
            "The explicit User-scope pin must survive even though Project shadows it with an equal value.");
    }

    /// <summary>
    /// Model-dropdown ghost repro: a blank ("") combo value must be treated as "unset",
    /// never pinned as model="". Pick a value, then clear it to blank; the field must
    /// end UNSET. Without the empty-string normalization in WriteEditorValue, the blank
    /// flush pinned model="" (the reported ghost, surfaced in the Save dialog).
    /// </summary>
    [Fact]
    public void WriteEditorValue_SdkBranch_BlankString_DoesNotPinEmptyValue()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace ws = MakeWorkspace((ConfigScope.User, "{}"));
        using AgentConfigClientCore sdk = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());

        SharedScopeContext ctx = new(ConfigScope.User);
        SettingsGroupEditorViewModel vm = new("General", nodes, ws, ctx, ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create(),sdkClient: sdk);

        StringPropertyEditorViewModel editor = (StringPropertyEditorViewModel)vm.Editors[0];
        editor.Value = "opus"; // user picks a model...
        editor.Value = "";     // ...then clears it back to blank
        vm.ApplyToWorkspace();

        MessageAssert.Null(sdk.GetScopeValue("model", ConfigScope.User),
            "A blank selection must leave model unset, never pin model=\"\".");
    }

    // ── Shared scope synchronisation ──────────────────────────────────────────

    [Fact]
    public void SharedScopeContext_ChangingScope_PropagatesFromContextToAllVMs()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"u"}"""),
            (ConfigScope.Project, """{"model":"p"}"""));

        SharedScopeContext ctx = new();
        SettingsGroupEditorViewModel vm1 = new("G1", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        SettingsGroupEditorViewModel vm2 = new("G2", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        // Both start at User scope
        Assert.Equal(ConfigScope.User, vm1.EditingScope);
        Assert.Equal(ConfigScope.User, vm2.EditingScope);

        // Changing the shared context should propagate to both VMs
        ctx.EditingScope = ConfigScope.Project;

        Assert.Equal(ConfigScope.Project, vm1.EditingScope);
        Assert.Equal(ConfigScope.Project, vm2.EditingScope);
    }

    [Fact]
    public void SharedScopeContext_ChangingVMScope_PropagatesToSiblingVM()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"u"}"""),
            (ConfigScope.Local, """{"model":"l"}"""));

        SharedScopeContext ctx = new();
        // Reflect real-world state: AvailableScopes must include Local before the
        // user can select it (set by UpdateScopeContextScopes when a project is open).
        ctx.AvailableScopes = [ConfigScope.User, ConfigScope.Local];

        SettingsGroupEditorViewModel vm1 = new("G1", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        SettingsGroupEditorViewModel vm2 = new("G2", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        // Changing scope on vm1 should sync vm2 and the shared context
        vm1.EditingScope = ConfigScope.Local;

        Assert.Equal(ConfigScope.Local, vm2.EditingScope);
        Assert.Equal(ConfigScope.Local, ctx.EditingScope);
    }

    [Fact]
    public void SharedScopeContext_NewVMInheritsCurrentScope()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.Project, """{"model":"p"}"""));

        SharedScopeContext ctx = new(ConfigScope.Project);
        SettingsGroupEditorViewModel vm = new("G1", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        Assert.Equal(ConfigScope.Project, vm.EditingScope);
    }

    // ── Cross-product scope-bleed regression ──────────────────────────────────
    //
    // Repro: Avalonia's ContentControl reuses the same SettingsGroupEditorView
    // when navigating between two SettingsGroupEditorViewModel pages (same
    // DataTemplate type).  During the DataContext switch, ItemsSource and
    // SelectedItem bindings are not updated atomically: the ComboBox may see
    // its ItemsSource shrink (e.g. [User,Project,Local] → [User]) while the
    // old SelectedItem "Local" is still in place, fire a selection-cleared
    // event, and push the stale/default scope back through the TwoWay binding
    // to the NEW VM's EditingScope — setting a Desktop VM to Local and causing
    // "No document loaded for scope Local." on the next write.

    [Fact]
    public void OnEditingScopeChanged_RejectsUnavailableScope_SnapsBackToValid()
    {
        // Simulate a Desktop-style context: only User scope is available.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, """{"model":"u"}"""));

        SharedScopeContext ctx = new();
        ctx.AvailableScopes = [ConfigScope.User]; // Desktop: single scope

        SettingsGroupEditorViewModel vm = new("MCP Servers", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        // Simulate Avalonia binding pushing a stale "Local" from the previous
        // CC page into this Desktop VM during the DataContext switch.
        vm.EditingScope = ConfigScope.Local;

        // The guard should have rejected Local and snapped back to User.
        MessageAssert.Equal(ConfigScope.User, vm.EditingScope,
            "EditingScope should be snapped back to User when Local is not available.");

        // The shared context must NOT have been contaminated.
        MessageAssert.Equal(ConfigScope.User, ctx.EditingScope,
            "Shared context EditingScope must not be set to an unavailable scope.");
    }

    [Fact]
    public void OnEditingScopeChanged_AcceptsValidScope_PropagatesNormally()
    {
        // Confirm that the guard does NOT block a legitimate scope change.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"u"}"""),
            (ConfigScope.Local, """{"model":"l"}"""));

        SharedScopeContext ctx = new();
        ctx.AvailableScopes = [ConfigScope.User, ConfigScope.Local];

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace, ctx,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        vm.EditingScope = ConfigScope.Local;

        Assert.Equal(ConfigScope.Local, vm.EditingScope);
        Assert.Equal(ConfigScope.Local, ctx.EditingScope);
    }

    // ── EffectiveRows: unset rows are filtered ────────────────────────────────

    [Fact]
    public void EffectiveRows_HidesPropertiesWithNoValueAtAnyScope()
    {
        // Two schema nodes; only one has a value at any scope. The Effective
        // tab is meant to answer "what value does Claude actually see?", and
        // a property that is unset everywhere has no answer worth showing.
        // RebuildEffectiveRows filters those out.
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"), // set at User
            MakeNode("verbose", "verbose", SchemaValueType.Boolean), // unset everywhere
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, """{"model":"sonnet"}"""));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        MessageAssert.Equal(1, vm.EffectiveRows.Count,
            "Only properties with at least one scope-set value should appear.");
        Assert.Equal("model", vm.EffectiveRows[0].Property);
    }

    [Fact]
    public void EffectiveRows_AllUnset_RendersEmpty()
    {
        // Edge case: every schema node is unset → grid is empty (instead of
        // a grid full of "(not set)" rows that convey no information).
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        MessageAssert.Equal(0, vm.EffectiveRows.Count,
            "When nothing is set, the Effective grid should be empty.");
    }

    // ── GroupDescription wiring ───────────────────────────────────────────────

    [Fact]
    public void GroupDescription_DefaultsToEmpty()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        MessageAssert.Equal(string.Empty, vm.GroupDescription,
            "Default constructor must leave description empty so the description TextBlock collapses.");
    }

    [Fact]
    public void GroupDescription_RoundTripsThroughConstructor()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SharedScopeContext ctx = new();

        SettingsGroupEditorViewModel vm = new(
            "General", nodes, workspace, ctx, ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create(),
            browseDialog: null,            groupDescription: "Top-level toggles for the section.");

        Assert.Equal("Top-level toggles for the section.", vm.GroupDescription);
    }

    // ── JSON placeholder mode ─────────────────────────────────────────────────
    //
    // These tests exercise BuildPlaceholderJson via the public ShowJsonPlaceholders
    // toggle. Regression context: pages whose only schema node was a Complex type
    // (Permissions, EnabledPlugins, Hooks, MCP servers, Marketplaces) used to render
    // {} when "show all / include defaults" was enabled — BuildPlaceholder returned
    // null for Object/Complex, dropping the key entirely. The fix recurses into
    // Object children and emits a key-specific shape (or empty {}) for Complex.

    [Fact]
    public void JsonPreview_ShowAll_EmitsKeyForComplexNode()
    {
        // Mimics the "Plugins" page: one Complex schema node, no value set anywhere.
        // The placeholder JSON must include the property as a key with an empty {}
        // so the user sees something other than "{}" wrapping the whole document.
        List<SchemaNode> nodes =
        [
            MakeNode("enabledPlugins", "enabledPlugins", SchemaValueType.Complex),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("Plugins", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create())
        {
            ShowJsonPlaceholders = true,
        };

        MessageAssert.Contains("\"enabledPlugins\"", vm.JsonPreview,
            "Placeholder JSON must include the Complex node's key, not drop it.");
        MessageAssert.Contains("{}", vm.JsonPreview,
            "Open-ended Complex types render an empty object as the placeholder body.");
    }

    [Fact]
    public void JsonPreview_ShowAll_EmitsRichShapeForPermissions()
    {
        // The "permissions" Complex node has a fixed, well-known sub-schema
        // (defaultMode / allow / deny / ask) — the placeholder must emit that
        // skeleton so the user sees the expected runtime shape.
        List<SchemaNode> nodes =
        [
            MakeNode("permissions", "permissions", SchemaValueType.Complex),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("Permissions", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create())
        {
            ShowJsonPlaceholders = true,
        };

        OrdinalAssert.Contains("\"permissions\"", vm.JsonPreview);
        OrdinalAssert.Contains("\"defaultMode\"", vm.JsonPreview);
        OrdinalAssert.Contains("\"allow\"", vm.JsonPreview);
        OrdinalAssert.Contains("\"deny\"", vm.JsonPreview);
        OrdinalAssert.Contains("\"ask\"", vm.JsonPreview);
    }

    [Fact]
    public void JsonPreview_ShowAll_RecursesIntoObjectProperties()
    {
        // An Object node with declared Properties must contribute its children
        // as nested placeholder keys, not collapse to {}.
        SchemaNode child = MakeNode("parent.flag", "flag", SchemaValueType.Boolean);
        SchemaNode parent = new("parent", "parent")
        {
            ValueType = SchemaValueType.Object,
            Properties = [child],
        };
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("Parent", [parent], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create())
        {
            ShowJsonPlaceholders = true,
        };

        OrdinalAssert.Contains("\"parent\"", vm.JsonPreview);
        MessageAssert.Contains("\"flag\"", vm.JsonPreview,
            "Object placeholders must recurse into their schema children, not collapse to empty.");
    }

    // ── Editor instance reuse across RebuildEditors ──────────────────────
    //
    // SettingsGroupEditorViewModel.RebuildEditors used to
    // construct fresh editor instances on every workspace.Changed (or
    // EditingScope change). Compound editors (Hooks, McpServers) lost
    // internal UI state — selected list item, expansion, scroll, in-progress
    // new-row text — across every reload, even though those editors had
    // per-instance "preserve selection across LoadFromLayered" logic.
    // The preservation only ran on the SAME instance; once the instance
    // was discarded and a fresh one constructed, prior state was gone.
    //
    // Fix: RebuildEditors now reuses existing editors by JsonPath, only
    // constructing new ones for paths that didn't have an editor before
    // (i.e. genuine schema additions).

    [Fact]
    public void RebuildEditors_ReusesExistingEditorInstances_ForSamePaths()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        // Capture references to the editor instances after the first load.
        PropertyEditorViewModel firstModelEditor = vm.Editors.First(e => e.Path == "model");
        PropertyEditorViewModel firstVerboseEditor = vm.Editors.First(e => e.Path == "verbose");

        // Trigger a rebuild via RefreshFromWorkspace (the canonical reload entry point).
        vm.RefreshFromWorkspace();

        PropertyEditorViewModel secondModelEditor = vm.Editors.First(e => e.Path == "model");
        PropertyEditorViewModel secondVerboseEditor = vm.Editors.First(e => e.Path == "verbose");

        MessageAssert.Same(firstModelEditor, secondModelEditor,
            "RebuildEditors must reuse the existing editor instance for an unchanged JsonPath. "
            + "Constructing a fresh instance loses internal UI state (selection, expansion, etc.).");
        MessageAssert.Same(firstVerboseEditor, secondVerboseEditor,
            "Reuse must apply to every path that already had an editor.");
    }

    [Fact]
    public void RebuildEditors_PreservesEditorState_AcrossExternalReload()
    {
        // The user-level scenario: a compound editor with internal state
        // (here represented by the editor instance reference itself, since
        // unit-test fixtures don't bind into Avalonia controls). Trigger
        // an external reload via workspace.SetValue from another scope —
        // this fires workspace.Changed → OnWorkspaceChanged → RebuildEditors.
        // The instance must survive.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        PropertyEditorViewModel firstEditor = vm.Editors[0];

        // Simulate an external write that fires workspace.Changed.
        workspace.SetValue("model", "claude-sonnet-4-5", ConfigScope.User);

        PropertyEditorViewModel secondEditor = vm.Editors[0];
        MessageAssert.Same(firstEditor, secondEditor,
            "External workspace.Changed must reuse the existing editor instance, not recreate it. "
            + "Recreation loses internal UI state — see HooksEditor SelectedGroup / "
            + "McpServersEditor SelectedServer drift bugs reported 2026-05-01.");
    }

    // ── Audit-log redaction (PII / secrets) ────────────────────────────────────

    /// <summary>
    /// the permanent <c>[Editor.UserEdit]</c>
    /// log site was emitting <c>value?.ToJsonString()</c> unconditionally,
    /// which leaks the WHOLE <c>env</c> JSON object (including any
    /// <c>ANTHROPIC_API_KEY</c>) into the rolling log file.  The fix routes
    /// values through <c>FormatValueForAuditLog</c> which redacts when the
    /// path is sensitive per <see cref="Bennewitz.Ninja.AgentForge.Sdk.Diagnostics.SensitiveKeys"/>
    /// and summarises (no contents) for compound values whose nested keys
    /// might also be secret-bearing.
    /// </summary>
    [Fact]
    public void FormatValueForAuditLog_RedactsEnvPath()
    {
        JsonObject env = new()
        {
            ["ANTHROPIC_API_KEY"] = "sk-test-secret-12345",
            ["MAX_THINKING_TOKENS"] = "32000",
        };
        string result = SettingsGroupEditorViewModel.FormatValueForAuditLog(env, "env");
        MessageAssert.Equal(SensitiveKeys.RedactedMarker, result,
            "Sensitive top-level path (env) must produce only the redacted marker — " +
            "no inlined JSON, no nested keys, no values.");
        MessageAssert.DoesNotMatch(MyRegex(), result,
            "The redacted output must not contain any fragment of the secret value.");
    }

    [Fact]
    public void FormatValueForAuditLog_CompoundValue_ReturnsStructuralSummaryNotContents()
    {
        // mcpServers is NOT in SensitiveKeys segments — but its nested
        // entries (e.g. mcpServers.foo.headers.Authorization) ARE
        // secret-bearing.  Compound values must therefore be summarised
        // structurally, never inlined.
        JsonObject mcp = new()
        {
            ["foo"] = new JsonObject
            {
                ["headers"] = new JsonObject
                {
                    ["Authorization"] = "Bearer leaked-token-xyz",
                },
            },
        };
        string result = SettingsGroupEditorViewModel.FormatValueForAuditLog(mcp, "mcpServers");
        MessageAssert.StartsWith("(JsonObject", result,
            "Compound editor values must render as a shape+size summary, not their contents.");
        MessageAssert.DoesNotMatch(new Regex("leaked-token"), result,
            "Nested secret values inside a compound editor must not appear in the audit log.");
        MessageAssert.DoesNotMatch(new Regex("Authorization"), result,
            "Nested key names inside a compound editor must not appear in the audit log either " +
            "(prevents the reader from inferring presence of specific auth schemes).");
    }

    [Fact]
    public void FormatValueForAuditLog_LeafValue_LogsValueAsJson()
    {
        // Scalar leaf editors on non-sensitive paths log their value
        // normally — `model = "haiku"` is the kind of audit info that
        // makes the trail useful for "what did the user actually
        // change?" forensics.
        JsonValue value = JsonValue.Create("haiku");
        Assert.Equal("\"haiku\"",
            SettingsGroupEditorViewModel.FormatValueForAuditLog(value, "model"));
    }

    [Fact]
    public void FormatValueForAuditLog_NullValue_RendersExplicitNullToken()
    {
        Assert.Equal("(null)",
            SettingsGroupEditorViewModel.FormatValueForAuditLog(null, "anything"));
    }

    // ── Deep-link filter "navigated" frame (FilterFromNavigation) ──────────

    [Fact]
    public void ApplyNavigationFilter_SetsFilterAndNavFlag()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.ApplyNavigationFilter("model");

        Assert.Equal("model", vm.FilterText);
        Assert.True(vm.FilterFromNavigation, "A deep-link filter must flag the orange nav frame.");
    }

    [Fact]
    public void UserEditingFilter_DropsNavFlag()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        vm.ApplyNavigationFilter("model");
        Assert.True(vm.FilterFromNavigation);

        vm.FilterText = "modelX"; // simulates the user typing into the filter box

        Assert.False(vm.FilterFromNavigation, "A user edit must drop the nav frame.");
    }

    [Fact]
    public void ClearingFilter_DropsNavFlag()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());
        vm.ApplyNavigationFilter("model");

        vm.ClearFilterCommand.Execute(null);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.False(vm.FilterFromNavigation);
    }

    [Fact]
    public void ApplyNavigationFilter_NullOrEmpty_DoesNotFlag()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.ApplyNavigationFilter(null);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.False(vm.FilterFromNavigation, "An empty nav filter must not draw the frame.");
    }

    [GeneratedRegex("sk-test-secret")]
    private static partial Regex MyRegex();

    // ── Phase 8b-4 seams: three behaviours that 1,462 tests did not notice ────
    //
    // Each of these was found by canarying a seam introduced when the group editor became
    // neutral. Breaking any of them left the entire suite green, which is why they are here.

    /// <summary>
    /// Filtering must descend into object editors, so typing a nested property name shows that
    /// child rather than its collapsed parent.
    /// </summary>
    /// <remarks>
    /// ⚠ The editor this exercises is the APP's <c>ObjectPropertyEditorViewModel</c>, which does
    /// NOT derive from the library type of the same name. The neutral filter therefore tests
    /// against <c>IChildEditorHost</c>; a type test against either class would match only half
    /// the object editors in play and silently stop descending into the other half. Removing the
    /// interface from the app's editor failed ZERO tests before this one existed.
    /// </remarks>
    [Fact]
    public void Filter_DescendsIntoObjectEditors_ViaTheChildHostInterface()
    {
        // ⚠ NOT a node name the product specialises (e.g. "permissions"): those produce a
        // compound editor, which is deliberately not a child-editor host, and the test would
        // then be measuring the specialised-editor fallback instead of the object descent.
        SchemaNode parent = new("statusLine", "statusLine")
        {
            ValueType = SchemaValueType.Object,
            Properties =
            [
                MakeNode("statusLine.allowUnsandboxedCommands", "allowUnsandboxedCommands",
                    SchemaValueType.Boolean),
                MakeNode("statusLine.somethingElse", "somethingElse", SchemaValueType.Boolean),
            ],
        };
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [parent], workspace,
            ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        vm.FilterText = "allowUnsandboxedCommands";

        List<string> shown = [.. vm.FilteredEditors.Select(e => e.Path)];
        MessageAssert.Contains("statusLine.allowUnsandboxedCommands", shown,
            "The filter must descend into the object and surface the matching child. If the "
            + "object editor no longer advertises IChildEditorHost, the descent stops and the "
            + "user sees the collapsed parent — or nothing — instead of the property they typed."
            + $" Shown: {string.Join(", ", shown)}");
        MessageAssert.DoesNotContain("statusLine.somethingElse", shown,
            "Only matching descendants should surface, not every sibling in the object.");
    }

    /// <summary>
    /// Clearing the filter asks every hint-bearing editor to drop its transient banners, so the
    /// page returns to its default state.
    /// </summary>
    /// <remarks>
    /// Uses the real Permissions editor rather than a double, because the seam and its one
    /// implementation are separately breakable: making <c>DismissTransientHints</c> a no-op
    /// failed zero tests.
    /// </remarks>
    [Fact]
    public void ClearingTheFilter_DismissesTransientHints()
    {
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("Permissions",
            [MakeNode("permissions", "permissions", SchemaValueType.Object)], workspace,
            ClaudeEditorFactoryConfig.CreateDefault(), ClaudeSettingsGroupText.Create());

        List<ITransientHintHost> hosts = [.. vm.Editors.OfType<ITransientHintHost>()];
        Assert.True(hosts.Count > 0,
            "Precondition: the permissions node must produce an editor that carries transient "
            + "hints, or this test proves nothing.");

        foreach (PermissionsEditorViewModel p in vm.Editors.OfType<PermissionsEditorViewModel>())
        {
            p.ShowDangerCliHint = true;
        }

        vm.FilterText = "something";
        vm.FilterText = string.Empty;   // the clear is what must dismiss the hint

        foreach (PermissionsEditorViewModel p in vm.Editors.OfType<PermissionsEditorViewModel>())
        {
            Assert.False(p.ShowDangerCliHint,
                "Clearing the filter must dismiss transient hints — a banner raised by what the "
                + "filter surfaced would otherwise point at something no longer on screen.");
        }
    }

    /// <summary>
    /// The JSON tab header tracks the placeholder mode, and comes from supplied text rather than
    /// a string literal.
    /// </summary>
    /// <remarks>
    /// Both headers were hardcoded English inline before the group editor became neutral.
    /// Inverting the two left the suite green.
    /// </remarks>
    [Fact]
    public void JsonTabHeader_TracksPlaceholderMode_FromSuppliedText()
    {
        SettingsGroupText text = new()
        {
            TabProperties = "P", TabEffective = "E",
            TabJsonAll = "ALL-marker", TabJsonActive = "ACTIVE-marker",
        };
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", [MakeNode("model", "model")], workspace,
            ClaudeEditorFactoryConfig.CreateDefault(), text);

        vm.ShowJsonPlaceholders = true;
        Assert.Equal("ALL-marker", vm.JsonTabHeader);
        MessageAssert.Equal("ALL-marker", vm.Tabs.Single(t => t.Id == GroupTab.JsonId).Header,
            "The seeded tab must carry the same header the property reports.");

        vm.ShowJsonPlaceholders = false;
        Assert.Equal("ACTIVE-marker", vm.JsonTabHeader);
    }
}
