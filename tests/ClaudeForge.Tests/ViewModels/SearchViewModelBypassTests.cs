using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Collections.ObjectModel;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ScopedEditors.ViewModels;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Covers the synthetic <c>permissions.defaultMode = bypassPermissions</c> search
/// result: it appears for "bypass" queries, deep-links via the
/// <c>permissions.defaultMode</c> property key, and is distinct from both the
/// <c>--dangerouslySkipPermissions</c> CLI-flag synthetic and the "disable bypass"
/// Essentials card.
/// </summary>
public sealed class SearchViewModelBypassTests
{
    /// <summary>This app's synthetic-row table — see <see cref="SearchViewModelTests"/>.</summary>
    private static readonly Func<IReadOnlyList<SyntheticSearchEntry>> ClaudeEntries =
        () => ClaudeSyntheticSearch.Build("Claude Code");

    private static SearchViewModel WithPermissionsTree()
    {
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];
        return new SearchViewModel(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            getSyntheticEntries: ClaudeEntries);
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
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        ClaudeConfigClientBase client = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
        EssentialsViewModel essentials = new(client, new FakeEnvironmentProvider());

        NavigationNodeViewModel essNode = new("Essentials") { Editor = essentials };
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [essNode, ccHeader];
        return new SearchViewModel(() => tree, () => false, ClaudeEntries);
    }

    private static SearchResultViewModel? BypassRow(SearchViewModel vm)
        => vm.SearchResults.FirstOrDefault(r => r.IsSynthetic && r.PropertyKey == "permissions.defaultMode");

    private static SearchResultViewModel? DisableBypassCard(SearchViewModel vm)
        => vm.SearchResults.FirstOrDefault(
            r => r.IsSynthetic && r.PropertyKey == EssentialsViewModel.CardIdDisableBypass);

    [Fact]
    public void ExecuteSearch_Bypass_AddsDefaultModeSynthetic_WhenPermissionsNodePresent()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("bypass");

        SearchResultViewModel? row = BypassRow(vm);
        MessageAssert.NotNull(row, "A bypass → defaultMode synthetic row should appear.");
        Assert.True(row!.IsSynthetic);
        Assert.Equal("permissions.defaultMode", row.PropertyKey);
        OrdinalAssert.Contains("bypassPermissions", row.PropertyDisplayName);
    }

    [Fact]
    public void ExecuteSearch_Bypass_OmitsSynthetic_WhenNoPermissionsNode()
    {
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];
        SearchViewModel vm = new(() => tree, () => false, ClaudeEntries);

        vm.ExecuteSearch("bypass");

        Assert.Null(BypassRow(vm));
    }

    [Fact]
    public void ExecuteSearch_DisableBypass_DoesNotAddDefaultModeSynthetic()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("disable bypass");

        MessageAssert.Null(BypassRow(vm),
            "'disable bypass' is the opposite intent (lock-out) — must not surface the bypass-select synthetic.");
    }

    [Fact]
    public void ExecuteSearch_Danger_DoesNotAddBypassDefaultModeSynthetic()
    {
        SearchViewModel vm = WithPermissionsTree();
        vm.ExecuteSearch("danger");

        // The danger synthetic uses an empty PropertyKey; the bypass one uses
        // permissions.defaultMode. "danger" must not also fire the bypass row.
        Assert.Null(BypassRow(vm));
    }

    // ── Opposite-intent disambiguation (the double-fire regression) ────────

    [Fact]
    public void ExecuteSearch_Bypass_SurfacesEnableDeepLink_NotTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("bypass");

        MessageAssert.NotNull(BypassRow(vm), "The enable deep-link should surface.");
        MessageAssert.Null(DisableBypassCard(vm),
            "The opposite-intent 'Disable bypass-permissions mode' card must be suppressed for an enable-bypass query.");
    }

    [Fact]
    public void ExecuteSearch_BypassPermissions_SurfacesEnableDeepLink_NotTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("bypass permissions");

        Assert.NotNull(BypassRow(vm));
        Assert.Null(DisableBypassCard(vm));
    }

    [Fact]
    public void ExecuteSearch_DisableBypass_SurfacesOnlyTheDisableCard()
    {
        SearchViewModel vm = WithPermissionsAndEssentials();
        vm.ExecuteSearch("disable bypass");

        MessageAssert.Null(BypassRow(vm), "'disable bypass' must not surface the enable deep-link.");
        MessageAssert.NotNull(DisableBypassCard(vm), "'disable bypass' should surface the lock-out card.");
    }
}
