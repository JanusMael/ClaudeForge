using System.Text.RegularExpressions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using PropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel;
// App-bridge StringPropertyEditorViewModel deleted; alias library leaf.
using StringPropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.StringPropertyEditorViewModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

[TestClass]
public partial class SettingsGroupEditorViewModelTests
{
    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs);
    }

    private static SchemaNode MakeNode(string jsonPath, string name,
                                       SchemaValueType type = SchemaValueType.String)
    {
        return new SchemaNode(jsonPath, name) { ValueType = type };
    }

    [TestMethod]
    public void RebuildEditors_PopulatesEditors()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("maxTokens", "maxTokens", SchemaValueType.Integer),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        Assert.AreEqual(3, vm.Editors.Count);
    }

    [TestMethod]
    public void FilterText_Empty_ReturnsAllEditors()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("maxTokens", "maxTokens", SchemaValueType.Integer),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "";

        // Editors is now IReadOnlyList; CollectionAssert needs a concrete ICollection.
        CollectionAssert.AreEqual(vm.Editors.ToList(), vm.FilteredEditors.ToList());
    }

    [TestMethod]
    public void FilterText_MatchesDisplayName_FiltersCorrectly()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "mod";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("model", filtered[0].Path);
    }

    [TestMethod]
    public void FilterText_MatchesJsonPath_FiltersCorrectly()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("permissions.allow", "allow"),
            MakeNode("permissions.deny", "deny"),
            MakeNode("model", "model"),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "permissions";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.AreEqual(2, filtered.Count);
    }

    [TestMethod]
    public void FilterText_NoMatch_ReturnsEmpty()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "zzznomatch";

        Assert.AreEqual(0, vm.FilteredEditors.Count());
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
    [TestMethod]
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
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "model.notARealSubProperty";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.AreEqual(1, filtered.Count,
            "Filter targeting a sub-path of a non-Object editor must yield that editor whole.");
        Assert.AreEqual("model", filtered[0].Path);
    }

    /// <summary>
    /// Guard the inverse — a sub-path filter must NOT bring back a sibling
    /// editor.  Filter "model.x" should not yield "verbose".
    /// </summary>
    [TestMethod]
    public void FilterText_SubPathOfOneEditor_DoesNotMatchSiblings()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        vm.FilterText = "model.something";

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.IsFalse(filtered.Any(e => e.Path == "verbose"),
            "Sub-path filter must not yield unrelated sibling editors.");
    }

    [TestMethod]
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
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace, ctx);

        Assert.AreEqual("sonnet", ((StringPropertyEditorViewModel)vm.Editors[0]).Value);

        vm.EditingScope = ConfigScope.Project;

        Assert.AreEqual("haiku", ((StringPropertyEditorViewModel)vm.Editors[0]).Value);
    }

    [TestMethod]
    public void ApplyToWorkspace_FlushesEditorValuesToWorkspace()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        ((StringPropertyEditorViewModel)vm.Editors[0]).Value = "opus";
        vm.ApplyToWorkspace();

        LayeredValue layered = workspace.GetLayeredValue("model");
        Assert.AreEqual("opus", layered.EffectiveValue!.GetValue<string>());
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
    [TestMethod]
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
        SettingsGroupEditorViewModel vm = new("Environment", nodes, workspace);

        // Sanity: the editor loaded the existing env value.
        Assert.AreEqual(1, vm.Editors.Count);

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
        Assert.IsNotNull(effective, "Effective env value should be a JsonObject.");
        Assert.IsTrue(effective.ContainsKey("CLAUDE_CODE_MAX_OUTPUT_TOKENS"),
            "Out-of-band write to env.CLAUDE_CODE_MAX_OUTPUT_TOKENS was clobbered by " +
            "ApplyToWorkspace.  The group editor flushed its stale in-memory env snapshot " +
            "(which didn't include the out-of-band key) back over the workspace.  This is " +
            "to make sure the _userEditedPaths gate is intact.");
        Assert.AreEqual("60000",
            effective["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]!.GetValue<string>());
    }

    // ── Shared scope synchronisation ──────────────────────────────────────────

    [TestMethod]
    public void SharedScopeContext_ChangingScope_PropagatesFromContextToAllVMs()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"u"}"""),
            (ConfigScope.Project, """{"model":"p"}"""));

        SharedScopeContext ctx = new();
        SettingsGroupEditorViewModel vm1 = new("G1", nodes, workspace, ctx);
        SettingsGroupEditorViewModel vm2 = new("G2", nodes, workspace, ctx);

        // Both start at User scope
        Assert.AreEqual(ConfigScope.User, vm1.EditingScope);
        Assert.AreEqual(ConfigScope.User, vm2.EditingScope);

        // Changing the shared context should propagate to both VMs
        ctx.EditingScope = ConfigScope.Project;

        Assert.AreEqual(ConfigScope.Project, vm1.EditingScope);
        Assert.AreEqual(ConfigScope.Project, vm2.EditingScope);
    }

    [TestMethod]
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

        SettingsGroupEditorViewModel vm1 = new("G1", nodes, workspace, ctx);
        SettingsGroupEditorViewModel vm2 = new("G2", nodes, workspace, ctx);

        // Changing scope on vm1 should sync vm2 and the shared context
        vm1.EditingScope = ConfigScope.Local;

        Assert.AreEqual(ConfigScope.Local, vm2.EditingScope);
        Assert.AreEqual(ConfigScope.Local, ctx.EditingScope);
    }

    [TestMethod]
    public void SharedScopeContext_NewVMInheritsCurrentScope()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.Project, """{"model":"p"}"""));

        SharedScopeContext ctx = new(ConfigScope.Project);
        SettingsGroupEditorViewModel vm = new("G1", nodes, workspace, ctx);

        Assert.AreEqual(ConfigScope.Project, vm.EditingScope);
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

    [TestMethod]
    public void OnEditingScopeChanged_RejectsUnavailableScope_SnapsBackToValid()
    {
        // Simulate a Desktop-style context: only User scope is available.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, """{"model":"u"}"""));

        SharedScopeContext ctx = new();
        ctx.AvailableScopes = [ConfigScope.User]; // Desktop: single scope

        SettingsGroupEditorViewModel vm = new("MCP Servers", nodes, workspace, ctx);

        // Simulate Avalonia binding pushing a stale "Local" from the previous
        // CC page into this Desktop VM during the DataContext switch.
        vm.EditingScope = ConfigScope.Local;

        // The guard should have rejected Local and snapped back to User.
        Assert.AreEqual(ConfigScope.User, vm.EditingScope,
            "EditingScope should be snapped back to User when Local is not available.");

        // The shared context must NOT have been contaminated.
        Assert.AreEqual(ConfigScope.User, ctx.EditingScope,
            "Shared context EditingScope must not be set to an unavailable scope.");
    }

    [TestMethod]
    public void OnEditingScopeChanged_AcceptsValidScope_PropagatesNormally()
    {
        // Confirm that the guard does NOT block a legitimate scope change.
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"model":"u"}"""),
            (ConfigScope.Local, """{"model":"l"}"""));

        SharedScopeContext ctx = new();
        ctx.AvailableScopes = [ConfigScope.User, ConfigScope.Local];

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace, ctx);
        vm.EditingScope = ConfigScope.Local;

        Assert.AreEqual(ConfigScope.Local, vm.EditingScope);
        Assert.AreEqual(ConfigScope.Local, ctx.EditingScope);
    }

    // ── EffectiveRows: unset rows are filtered ────────────────────────────────

    [TestMethod]
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

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        Assert.AreEqual(1, vm.EffectiveRows.Count,
            "Only properties with at least one scope-set value should appear.");
        Assert.AreEqual("model", vm.EffectiveRows[0].Property);
    }

    [TestMethod]
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

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        Assert.AreEqual(0, vm.EffectiveRows.Count,
            "When nothing is set, the Effective grid should be empty.");
    }

    // ── GroupDescription wiring ───────────────────────────────────────────────

    [TestMethod]
    public void GroupDescription_DefaultsToEmpty()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        Assert.AreEqual(string.Empty, vm.GroupDescription,
            "Default constructor must leave description empty so the description TextBlock collapses.");
    }

    [TestMethod]
    public void GroupDescription_RoundTripsThroughConstructor()
    {
        List<SchemaNode> nodes = [MakeNode("model", "model")];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SharedScopeContext ctx = new();

        SettingsGroupEditorViewModel vm = new(
            "General", nodes, workspace, ctx,
            browseDialog: null, factory: null,
            groupDescription: "Top-level toggles for the section.");

        Assert.AreEqual("Top-level toggles for the section.", vm.GroupDescription);
    }

    // ── JSON placeholder mode ─────────────────────────────────────────────────
    //
    // These tests exercise BuildPlaceholderJson via the public ShowJsonPlaceholders
    // toggle. Regression context: pages whose only schema node was a Complex type
    // (Permissions, EnabledPlugins, Hooks, MCP servers, Marketplaces) used to render
    // {} when "show all / include defaults" was enabled — BuildPlaceholder returned
    // null for Object/Complex, dropping the key entirely. The fix recurses into
    // Object children and emits a key-specific shape (or empty {}) for Complex.

    [TestMethod]
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

        SettingsGroupEditorViewModel vm = new("Plugins", nodes, workspace)
        {
            ShowJsonPlaceholders = true,
        };

        StringAssert.Contains(vm.JsonPreview, "\"enabledPlugins\"",
            "Placeholder JSON must include the Complex node's key, not drop it.");
        StringAssert.Contains(vm.JsonPreview, "{}",
            "Open-ended Complex types render an empty object as the placeholder body.");
    }

    [TestMethod]
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

        SettingsGroupEditorViewModel vm = new("Permissions", nodes, workspace)
        {
            ShowJsonPlaceholders = true,
        };

        StringAssert.Contains(vm.JsonPreview, "\"permissions\"");
        StringAssert.Contains(vm.JsonPreview, "\"defaultMode\"");
        StringAssert.Contains(vm.JsonPreview, "\"allow\"");
        StringAssert.Contains(vm.JsonPreview, "\"deny\"");
        StringAssert.Contains(vm.JsonPreview, "\"ask\"");
    }

    [TestMethod]
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

        SettingsGroupEditorViewModel vm = new("Parent", [parent], workspace)
        {
            ShowJsonPlaceholders = true,
        };

        StringAssert.Contains(vm.JsonPreview, "\"parent\"");
        StringAssert.Contains(vm.JsonPreview, "\"flag\"",
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

    [TestMethod]
    public void RebuildEditors_ReusesExistingEditorInstances_ForSamePaths()
    {
        List<SchemaNode> nodes =
        [
            MakeNode("model", "model"),
            MakeNode("verbose", "verbose", SchemaValueType.Boolean),
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        // Capture references to the editor instances after the first load.
        PropertyEditorViewModel firstModelEditor = vm.Editors.First(e => e.Path == "model");
        PropertyEditorViewModel firstVerboseEditor = vm.Editors.First(e => e.Path == "verbose");

        // Trigger a rebuild via RefreshFromWorkspace (the canonical reload entry point).
        vm.RefreshFromWorkspace();

        PropertyEditorViewModel secondModelEditor = vm.Editors.First(e => e.Path == "model");
        PropertyEditorViewModel secondVerboseEditor = vm.Editors.First(e => e.Path == "verbose");

        Assert.AreSame(firstModelEditor, secondModelEditor,
            "RebuildEditors must reuse the existing editor instance for an unchanged JsonPath. "
            + "Constructing a fresh instance loses internal UI state (selection, expansion, etc.).");
        Assert.AreSame(firstVerboseEditor, secondVerboseEditor,
            "Reuse must apply to every path that already had an editor.");
    }

    [TestMethod]
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
        SettingsGroupEditorViewModel vm = new("General", nodes, workspace);

        PropertyEditorViewModel firstEditor = vm.Editors[0];

        // Simulate an external write that fires workspace.Changed.
        workspace.SetValue("model", "claude-sonnet-4-5", ConfigScope.User);

        PropertyEditorViewModel secondEditor = vm.Editors[0];
        Assert.AreSame(firstEditor, secondEditor,
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
    /// path is sensitive per <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics.SensitiveKeys"/>
    /// and summarises (no contents) for compound values whose nested keys
    /// might also be secret-bearing.
    /// </summary>
    [TestMethod]
    public void FormatValueForAuditLog_RedactsEnvPath()
    {
        JsonObject env = new()
        {
            ["ANTHROPIC_API_KEY"] = "sk-test-secret-12345",
            ["MAX_THINKING_TOKENS"] = "32000",
        };
        string result = SettingsGroupEditorViewModel.FormatValueForAuditLog(env, "env");
        Assert.AreEqual(SensitiveKeys.RedactedMarker, result,
            "Sensitive top-level path (env) must produce only the redacted marker — " +
            "no inlined JSON, no nested keys, no values.");
        StringAssert.DoesNotMatch(result, MyRegex(),
            "The redacted output must not contain any fragment of the secret value.");
    }

    [TestMethod]
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
        StringAssert.StartsWith(result, "(JsonObject",
            "Compound editor values must render as a shape+size summary, not their contents.");
        StringAssert.DoesNotMatch(result, new Regex("leaked-token"),
            "Nested secret values inside a compound editor must not appear in the audit log.");
        StringAssert.DoesNotMatch(result, new Regex("Authorization"),
            "Nested key names inside a compound editor must not appear in the audit log either " +
            "(prevents the reader from inferring presence of specific auth schemes).");
    }

    [TestMethod]
    public void FormatValueForAuditLog_LeafValue_LogsValueAsJson()
    {
        // Scalar leaf editors on non-sensitive paths log their value
        // normally — `model = "haiku"` is the kind of audit info that
        // makes the trail useful for "what did the user actually
        // change?" forensics.
        JsonValue value = JsonValue.Create("haiku");
        Assert.AreEqual("\"haiku\"",
            SettingsGroupEditorViewModel.FormatValueForAuditLog(value, "model"));
    }

    [TestMethod]
    public void FormatValueForAuditLog_NullValue_RendersExplicitNullToken()
    {
        Assert.AreEqual("(null)",
            SettingsGroupEditorViewModel.FormatValueForAuditLog(null, "anything"));
    }

    [GeneratedRegex("sk-test-secret")]
    private static partial Regex MyRegex();
}