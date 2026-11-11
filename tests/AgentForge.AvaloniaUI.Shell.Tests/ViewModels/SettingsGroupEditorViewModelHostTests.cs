using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Danger;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Settings;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;
using Bennewitz.Ninja.ScopedEditors.Abstractions;
using PropertyEditorViewModel = Bennewitz.Ninja.ScopedEditors.ViewModels.PropertyEditorViewModel;
using StringPropertyEditorViewModel = Bennewitz.Ninja.ScopedEditors.ViewModels.StringPropertyEditorViewModel;

namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.ViewModels;

/// <summary>
/// The settings page's contract with its host and its navigator, beyond the copied suite in
/// <see cref="SettingsGroupEditorViewModelTests"/> (plans/00008 step 4).
/// </summary>
/// <remarks>
/// On <c>main</c> these paths were reached only through the app — its navigation tree, its danger
/// table and its window — so <c>ClaudeForge.Tests</c> covered them and nothing that ships with the
/// shell did. Each test here drives one through a neutral host and asserts what the page does.
/// </remarks>
public sealed class SettingsGroupEditorViewModelHostTests
{
    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
            new SettingsDocument(e.Scope, $"{e.Scope}.json", (JsonObject)JsonNode.Parse(e.Json)!, isReadOnly: false));
        return new SettingsWorkspace(docs, TestMergePolicy.Instance);
    }

    private static SchemaNode Node(string path, SchemaValueType type = SchemaValueType.String) =>
        new(path, path) { ValueType = type };

    private static SettingsGroupEditorViewModel Page(
        IReadOnlyList<SchemaNode> nodes, SettingsWorkspace workspace, SharedScopeContext? scope = null,
        ISchemaEditorFactory? factory = null) =>
        new("Group", nodes, workspace, scope ?? new SharedScopeContext(ConfigScope.User),
            factory ?? new TestEditorFactory(), TestSettingsGroupText.Create());

    // ── Danger ───────────────────────────────────────────────────────────────

    [Fact]
    public void AssessDanger_AnswersFromTheEditorThatOwnsThePath_AndNullForAPathNoneOwns()
    {
        SettingsGroupEditorViewModel vm = Page([Node("alpha"), Node("beta")], MakeWorkspace((ConfigScope.User, "{}")));

        PropertyEditorViewModel beta = vm.Editors.Single(e => e.Path == "beta");
        Assert.Equal(beta.AssessDanger("beta"), vm.AssessDanger("beta"));
        Assert.Null(vm.AssessDanger("not-on-this-page"));
    }

    [Fact]
    public void EffectiveRows_CarryTheHostClassifiersAssessment_AtTheWinningScope()
    {
        // Critical only where the value comes from Project: if the page passed the EDITING scope
        // (User) instead of the winning one, the row would read Info, and if it passed no
        // classifier at all it would read Unremarkable.
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>
        {
            ["risky"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "risky setting",
                EscalatesAt = id => id == ConfigScope.Project.Id,
                EscalatedTier = AppSeverity.Critical,
            },
        });
        SettingsWorkspace ws = MakeWorkspace(
            (ConfigScope.Project, """{"risky":"on"}"""),
            (ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = Page([Node("risky")], ws,
            factory: new TestEditorFactory { Danger = classifier });

        EffectivePropertyRow row = vm.EffectiveRows.Single(r => r.Property == "risky");
        Assert.Equal(AppSeverity.Critical, row.Danger.Severity);
        Assert.True(row.HasDangerSeverity);
    }

    [Fact]
    public void EffectiveRows_WithNoHostClassifier_AreUnremarkable()
    {
        SettingsGroupEditorViewModel vm = Page([Node("risky")], MakeWorkspace((ConfigScope.User, """{"risky":"on"}""")));

        EffectivePropertyRow row = vm.EffectiveRows.Single(r => r.Property == "risky");
        Assert.Equal(DangerAssessment.Unremarkable, row.Danger);
        Assert.False(row.HasDangerSeverity);
    }

    // ── Scopes ───────────────────────────────────────────────────────────────

    [Fact]
    public void AvailableScopes_FollowTheSharedContext_AndRaiseTheirChange()
    {
        SharedScopeContext shared = new(ConfigScope.User);
        SettingsGroupEditorViewModel vm = Page([Node("alpha")], MakeWorkspace((ConfigScope.User, "{}")), shared);
        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shared.AvailableScopes = [ConfigScope.User, ConfigScope.Project];

        Assert.Equal([ConfigScope.User, ConfigScope.Project], vm.AvailableScopes);
        Assert.Contains(nameof(SettingsGroupEditorViewModel.AvailableScopes), raised);
    }

    [Fact]
    public void RemovingTheScopeBeingEdited_FallsBackToTheFirstAvailable()
    {
        // Closing a project removes Project from the list while a page may be editing it.
        SharedScopeContext shared = new(ConfigScope.Project) { AvailableScopes = [ConfigScope.User, ConfigScope.Project] };
        SettingsGroupEditorViewModel vm = Page([Node("alpha")],
            MakeWorkspace((ConfigScope.Project, "{}"), (ConfigScope.User, "{}")), shared);
        vm.Activate();
        Assert.Equal(ConfigScope.Project, vm.EditingScope);

        shared.AvailableScopes = [ConfigScope.User];

        Assert.Equal(ConfigScope.User, shared.EditingScope);
        Assert.Equal(ConfigScope.User, vm.EditingScope);
    }

    // ── Navigation lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void LeavingThePage_ClearsTheFilter()
    {
        SettingsGroupEditorViewModel vm = Page([Node("alpha"), Node("beta")], MakeWorkspace((ConfigScope.User, "{}")));
        vm.OnNavigatedTo();
        vm.FilterText = "alp";
        Assert.Single(vm.FilteredEditors);

        vm.OnNavigatedFrom(replaced: false);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Equal(2, vm.FilteredEditors.Count());
    }

    [Fact]
    public void AScopeChangeWhileAway_IsAppliedOnReturn()
    {
        SharedScopeContext shared = new(ConfigScope.User) { AvailableScopes = [ConfigScope.User, ConfigScope.Project] };
        SettingsGroupEditorViewModel vm = Page([Node("alpha")],
            MakeWorkspace((ConfigScope.Project, """{"alpha":"from-project"}"""),
                          (ConfigScope.User, """{"alpha":"from-user"}""")), shared);
        vm.OnNavigatedTo();
        StringPropertyEditorViewModel editor = Assert.IsType<StringPropertyEditorViewModel>(vm.Editors[0]);
        Assert.Equal("from-user", editor.Value);
        vm.OnNavigatedFrom(replaced: false);

        // Editors are reused by path and RELOADED on a rebuild, so what a deferred rebuild changes
        // is the value an editor shows, not its identity.
        shared.EditingScope = ConfigScope.Project;
        Assert.Equal("from-user", editor.Value);   // deferred while the page is not shown

        vm.OnNavigatedTo();

        Assert.Same(editor, vm.Editors[0]);
        Assert.Equal("from-project", editor.Value);
    }

    // ── Disposal ─────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_StopsFollowingTheSharedScope_AndIsIdempotent()
    {
        SharedScopeContext shared = new(ConfigScope.User) { AvailableScopes = [ConfigScope.User, ConfigScope.Project] };
        SettingsGroupEditorViewModel vm = Page([Node("alpha")],
            MakeWorkspace((ConfigScope.Project, "{}"), (ConfigScope.User, "{}")), shared);
        vm.Activate();

        vm.Dispose();
        vm.Dispose();
        shared.EditingScope = ConfigScope.Project;

        Assert.Equal(ConfigScope.User, vm.EditingScope);
    }

    // ── JSON placeholders ────────────────────────────────────────────────────

    [Fact]
    public void JsonPlaceholders_StubEveryValueType_WithItsDefaultOrAnEmptyValue()
    {
        SchemaNode enumWithValues = new("mode", "mode") { ValueType = SchemaValueType.Enum, EnumValues = ["fast", "slow"] };
        SettingsGroupEditorViewModel vm = Page(
            [
                Node("ratio", SchemaValueType.Number),
                enumWithValues,
                Node("home", SchemaValueType.Path),
                Node("list", SchemaValueType.Array),
            ],
            MakeWorkspace((ConfigScope.User, "{}")));

        vm.ShowJsonPlaceholders = true;

        JsonObject preview = (JsonObject)JsonNode.Parse(vm.JsonPreview)!;
        Assert.Equal(0.0, preview["ratio"]!.GetValue<double>());
        Assert.Equal("fast", preview["mode"]!.GetValue<string>());
        Assert.Equal(string.Empty, preview["home"]!.GetValue<string>());
        Assert.Empty(Assert.IsType<JsonArray>(preview["list"]));
    }
}
