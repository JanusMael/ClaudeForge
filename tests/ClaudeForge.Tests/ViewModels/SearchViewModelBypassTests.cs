using System.Collections.ObjectModel;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Covers the synthetic <c>permissions.defaultMode = bypassPermissions</c> search
/// result: it appears for "bypass" queries, deep-links via the
/// <c>permissions.defaultMode</c> property key, and is distinct from both the
/// <c>--dangerouslySkipPermissions</c> CLI-flag synthetic and the "disable bypass"
/// Essentials card.
/// </summary>
[TestClass]
public sealed class SearchViewModelBypassTests
{
    private static SearchViewModel WithPermissionsTree()
    {
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];
        return new SearchViewModel(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            claudeCodeNavTitle: "Claude Code");
    }

    // A tree with BOTH a Permissions node (Claude Code child) and a top-level
    // Essentials node whose Editor is a real EssentialsViewModel — so the
    // "Disable bypass-permissions mode" Essentials card can actually surface and
    // the opposite-intent double-fire is observable (the original fixture had no
    // Essentials node, which is why the conflict went uncaught).
    private static SearchViewModel WithPermissionsAndEssentials()
    {
        JsonObject root = new();
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        ClaudeConfigClientCore client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
        EssentialsViewModel essentials = new(client, new FakeEnvironmentProvider());

        NavigationNodeViewModel essNode = new("Essentials") { Editor = essentials };
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [essNode, ccHeader];
        return new SearchViewModel(() => tree, () => false, "Claude Code");
    }

    private static SearchResultViewModel? BypassRow(SearchViewModel vm)
        => vm.SearchResults.FirstOrDefault(r => r.IsSynthetic && r.PropertyKey == "permissions.defaultMode");

    private static SearchResultViewModel? DisableBypassCard(SearchViewModel vm)
        => vm.SearchResults.FirstOrDefault(
            r => r.IsSynthetic && r.PropertyKey == EssentialsViewModel.CardIdDisableBypass);

    [TestMethod]
    public void ExecuteSearch_Bypass_AddsDefaultModeSynthetic_WhenPermissionsNodePresent()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("bypass");

        SearchResultViewModel? row = BypassRow(vm);
        Assert.IsNotNull(row, "A bypass → defaultMode synthetic row should appear.");
        Assert.IsTrue(row!.IsSynthetic);
        Assert.AreEqual("permissions.defaultMode", row.PropertyKey);
        StringAssert.Contains(row.PropertyDisplayName, "bypassPermissions");
    }

    [TestMethod]
    public void ExecuteSearch_Bypass_OmitsSynthetic_WhenNoPermissionsNode()
    {
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("bypass");

        Assert.IsNull(BypassRow(vm));
    }

    [TestMethod]
    public void ExecuteSearch_DisableBypass_DoesNotAddDefaultModeSynthetic()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("disable bypass");

        Assert.IsNull(BypassRow(vm),
            "'disable bypass' is the opposite intent (lock-out) — must not surface the bypass-select synthetic.");
    }

    [TestMethod]
    public void ExecuteSearch_Danger_DoesNotAddBypassDefaultModeSynthetic()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("danger");

        // The danger synthetic uses an empty PropertyKey; the bypass one uses
        // permissions.defaultMode. "danger" must not also fire the bypass row.
        Assert.IsNull(BypassRow(vm));
    }

    // ── Opposite-intent disambiguation (the double-fire regression) ────────

    [TestMethod]
    public void ExecuteSearch_Bypass_SurfacesEnableDeepLink_NotTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("bypass");

        Assert.IsNotNull(BypassRow(vm), "The enable deep-link should surface.");
        Assert.IsNull(DisableBypassCard(vm),
            "The opposite-intent 'Disable bypass-permissions mode' card must be suppressed for an enable-bypass query.");
    }

    [TestMethod]
    public void ExecuteSearch_BypassPermissions_SurfacesEnableDeepLink_NotTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("bypass permissions");

        Assert.IsNotNull(BypassRow(vm));
        Assert.IsNull(DisableBypassCard(vm));
    }

    [TestMethod]
    public void ExecuteSearch_DisableBypass_SurfacesOnlyTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("disable bypass");

        Assert.IsNull(BypassRow(vm), "'disable bypass' must not surface the enable deep-link.");
        Assert.IsNotNull(DisableBypassCard(vm), "'disable bypass' should surface the lock-out card.");
    }
}
