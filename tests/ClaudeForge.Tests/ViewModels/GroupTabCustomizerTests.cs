using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Locks the data-driven tab-strip exception mechanism: the general
/// <see cref="ClaudeGroupTabCustomizer"/> inserts the Permissions group's
/// Common/Build/Lists/Advanced tabs as siblings of (the relabeled) Properties,
/// before Effective/JSON, and leaves every other group's built-in strip
/// untouched.
/// </summary>
[TestClass]
public sealed class GroupTabCustomizerTests
{
    private static List<GroupTab> SeedBuiltIns() =>
    [
        new() { Id = GroupTab.PropertiesId, Header = "Properties", Content = new object() },
        new() { Id = GroupTab.EffectiveId, Header = "Effective", Content = new object() },
        new() { Id = GroupTab.JsonId, Header = "JSON", Content = new object() },
    ];

    private static PermissionsEditorViewModel MakePermissionsEditor() =>
        new(new SchemaNode("permissions", "permissions"), ConfigScope.User);

    [TestMethod]
    public void Customize_NonPermissionsGroup_LeavesBuiltInsUnchanged()
    {
        List<GroupTab> tabs = SeedBuiltIns();

        ClaudeGroupTabCustomizer.Instance.Customize("Model & Effort", tabs, []);

        CollectionAssert.AreEqual(
            new[] { GroupTab.PropertiesId, GroupTab.EffectiveId, GroupTab.JsonId },
            tabs.Select(t => t.Id).ToList(),
            "A non-permissions group must keep exactly the three built-in tabs.");
        Assert.AreEqual("Properties", tabs[0].Header, "Properties must not be relabeled for other groups.");
    }

    [TestMethod]
    public void Customize_PermissionsGroup_NoEditor_LeavesBuiltInsUnchanged()
    {
        // Defensive: if the permissions group somehow has no PermissionsEditorViewModel,
        // the customizer must not insert dangling tabs.
        List<GroupTab> tabs = SeedBuiltIns();

        ClaudeGroupTabCustomizer.Instance.Customize(
            ClaudeGroupTabCustomizer.PermissionsGroupName, tabs, []);

        CollectionAssert.AreEqual(
            new[] { GroupTab.PropertiesId, GroupTab.EffectiveId, GroupTab.JsonId },
            tabs.Select(t => t.Id).ToList());
    }

    [TestMethod]
    public void Customize_PermissionsGroup_InsertsThreeTabsAfterPropertiesBeforeEffective()
    {
        // Advanced is an accordion on the Overview body, not a tab — so the
        // customizer contributes exactly Common / Build / Lists.
        List<GroupTab> tabs = SeedBuiltIns();
        PermissionsEditorViewModel perm = MakePermissionsEditor();

        ClaudeGroupTabCustomizer.Instance.Customize(
            ClaudeGroupTabCustomizer.PermissionsGroupName, tabs, [perm]);

        CollectionAssert.AreEqual(
            new[]
            {
                GroupTab.PropertiesId,
                ClaudeGroupTabCustomizer.PermCommonId,
                ClaudeGroupTabCustomizer.PermBuildId,
                ClaudeGroupTabCustomizer.PermListsId,
                GroupTab.EffectiveId,
                GroupTab.JsonId,
            },
            tabs.Select(t => t.Id).ToList(),
            "Permissions tabs must sit right after Properties and before Effective/JSON.");

        CollectionAssert.DoesNotContain(
            tabs.Select(t => t.Id).ToList(),
            ClaudeGroupTabCustomizer.PermAdvancedId,
            "Advanced is an Overview accordion, not a contributed tab.");
    }

    [TestMethod]
    public void Customize_PermissionsGroup_RelabelsPropertiesToOverview()
    {
        List<GroupTab> tabs = SeedBuiltIns();

        ClaudeGroupTabCustomizer.Instance.Customize(
            ClaudeGroupTabCustomizer.PermissionsGroupName, tabs, [MakePermissionsEditor()]);

        GroupTab properties = tabs.Single(t => t.Id == GroupTab.PropertiesId);
        Assert.AreEqual(Strings.TabPermOverview, properties.Header,
            "The built-in Properties tab is relabeled 'Overview' for the Permissions group.");
    }

    [TestMethod]
    public void Customize_PermissionsGroup_ContributedTabsBindToPermissionsEditor()
    {
        List<GroupTab> tabs = SeedBuiltIns();
        PermissionsEditorViewModel perm = MakePermissionsEditor();

        ClaudeGroupTabCustomizer.Instance.Customize(
            ClaudeGroupTabCustomizer.PermissionsGroupName, tabs, [perm]);

        string[] permIds =
        [
            ClaudeGroupTabCustomizer.PermCommonId,
            ClaudeGroupTabCustomizer.PermBuildId,
            ClaudeGroupTabCustomizer.PermListsId,
        ];
        foreach (string id in permIds)
        {
            GroupTab tab = tabs.Single(t => t.Id == id);
            Assert.AreSame(perm, tab.Content,
                $"Contributed tab '{id}' must bind to the shared PermissionsEditorViewModel.");
        }
    }
}
