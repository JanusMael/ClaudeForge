using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Search in the second app: the shell does the work, this app supplies a tree, a synthetic table,
/// and one schema provider per section.
/// </summary>
/// <remarks>
/// The synthetic entries are the part worth testing hardest. Schema hits come from the SDK and are
/// covered there; the table here encodes judgement about what users type, and a trigger that does
/// not fire is invisible — the feature just quietly fails to help.
/// </remarks>
[TestClass]
public sealed class SearchWiringTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocf-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), "{}");
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static MainWindowViewModel BuildViewModel() => new(
        new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
            OpenCodePageLayout.Config, () => Strings.SectionOpenCode),
        new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
            OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui));

    private async Task<MainWindowViewModel> LoadedViewModelAsync()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);
        return vm;
    }

    // ── the wiring ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Searching_ReturnsSchemaResults()
    {
        MainWindowViewModel vm = await LoadedViewModelAsync();

        // ExecuteSearch rather than setting SearchQuery: the property is debounced 200 ms and
        // dispatched to the UI thread, so a unit test that sets it observes nothing.
        vm.Search.ExecuteSearch("model");

        Assert.IsTrue(vm.Search.SearchResults.Count > 0,
            "A query matching real schema keys returned nothing, so either the schema providers "
            + "are not wired or the tree was empty when the search ran.");
    }

    /// <summary>
    /// One schema provider per section, each searching its OWN client.
    /// </summary>
    /// <remarks>
    /// ⚠ This asserts the providers directly rather than through a query, and the reason is a
    /// canary. A first version searched "keybinds" — a terminal-UI-only key — and expected
    /// results; it stayed green with only ONE provider wired, because the navigation-tree walk
    /// already covers every section's pages and found the key by itself. The test could not
    /// distinguish the thing it was named after.
    /// </remarks>
    [TestMethod]
    public async Task SchemaProviders_AreBuiltPerSection_EachSearchingItsOwnClient()
    {
        MainWindowViewModel vm = await LoadedViewModelAsync();

        IReadOnlyList<SchemaSearchProvider> providers = vm.BuildSchemaSearchProviders();

        Assert.AreEqual(vm.Sections.Count, providers.Count,
            "One provider per hosted section. Fewer means a section's schema is unsearchable, "
            + "which the tree walk hides because it finds pages regardless.");

        // Each provider must answer for its own product: the terminal-UI one knows 'keybinds',
        // the main one does not, and vice versa for 'username'.
        SchemaSearchProvider tui = providers.Single(p => p.SectionTitle == Strings.SectionOpenCodeTui);
        SchemaSearchProvider main = providers.Single(p => p.SectionTitle == Strings.SectionOpenCode);

        Assert.IsTrue(tui.Search("keybinds").Any(),
            "The terminal-UI provider found nothing for 'keybinds', a key only its schema has.");
        Assert.IsFalse(main.Search("keybinds").Any(),
            "The main provider returned a terminal-UI-only key, so the two providers are "
            + "searching the same client — which is what capturing the loop variable wrongly does.");
        Assert.IsTrue(main.Search("username").Any(),
            "The main provider found nothing for 'username', a key only its schema has.");
    }

    [TestMethod]
    public async Task SelectingAResult_NavigatesAndClosesTheSearch()
    {
        MainWindowViewModel vm = await LoadedViewModelAsync();

        // ⚠ BOTH calls are needed, and a canary proved it: ExecuteSearch populates results
        // synchronously but does NOT set SearchQuery, so asserting the query cleared was
        // vacuously true — it had never been set. Setting the property is what makes the
        // clear observable.
        vm.Search.SearchQuery = "username";
        vm.Search.ExecuteSearch("username");
        SearchResultViewModel first = vm.Search.SearchResults.First();

        vm.SelectedSearchResult = first;

        Assert.AreSame(first.Node, vm.SelectedNode, "Choosing a result must navigate to its page.");
        Assert.AreEqual(string.Empty, vm.Search.SearchQuery,
            "The query must clear, or the result list keeps covering the page just navigated to.");
        Assert.IsNull(vm.SelectedSearchResult,
            "The selection must reset, or choosing the same result twice navigates only once.");
    }

    /// <summary>
    /// Search must not answer "no results" while the tree is still being built.
    /// </summary>
    [TestMethod]
    public void BeforeLoading_SearchReportsLoadingRatherThanEmpty()
    {
        MainWindowViewModel vm = BuildViewModel();

        Assert.IsTrue(vm.IsLoading,
            "A freshly constructed view-model must report loading: the window is shown before "
            + "InitializeAsync finishes, and a query typed in that gap would otherwise be "
            + "answered against an empty tree.");
    }

    // ── the synthetic table ──────────────────────────────────────────────────

    /// <summary>
    /// Every gotcha entry fires on the symptom a user would actually type. These are the entries
    /// that earn the table: someone whose config is ignored searches for the symptom, not for the
    /// setting.
    /// </summary>
    [TestMethod]
    [DataRow("config not loading", OpenCodeSyntheticSearch.EntryIdProjectConfigDisabled)]
    [DataRow("settings ignored", OpenCodeSyntheticSearch.EntryIdProjectConfigDisabled)]
    [DataRow("opencode_config_dir", OpenCodeSyntheticSearch.EntryIdConfigDirMoved)]
    [DataRow("where is my config", OpenCodeSyntheticSearch.EntryIdConfigDirMoved)]
    [DataRow("deny not working", OpenCodeSyntheticSearch.EntryIdPermissionOrder)]
    [DataRow("rule order", OpenCodeSyntheticSearch.EntryIdPermissionOrder)]
    public void GotchaEntries_FireOnTheSymptom(string query, string expectedId)
    {
        SyntheticSearchEntry? hit = OpenCodeSyntheticSearch
            .Build("OpenCode")
            .FirstOrDefault(e => e.Trigger.Matches(query));

        Assert.IsNotNull(hit, $"'{query}' matched no synthetic entry.");
        Assert.AreEqual(expectedId, hit.Id,
            $"'{query}' matched '{hit.Id}' rather than '{expectedId}'.");
    }

    /// <summary>
    /// An entry whose target page is absent must yield nothing rather than inventing a
    /// destination — a page one install lacks cannot hide a page it has.
    /// </summary>
    [TestMethod]
    public void AnEntryWhoseTargetIsMissing_ResolvesToNothing()
    {
        foreach (SyntheticSearchEntry entry in OpenCodeSyntheticSearch.Build("OpenCode"))
        {
            Assert.IsNull(entry.FindTarget([]),
                $"'{entry.Id}' resolved a target against an EMPTY tree, which means it is not "
                + "actually looking the page up.");
        }
    }

    /// <summary>
    /// Every entry resolves against the real tree. An entry pointing at a page title that does not
    /// exist is dead weight that never appears, and nothing else would report it.
    /// </summary>
    /// <summary>
    /// A gotcha query produces a synthetic row through the VIEW-MODEL, not just from the table.
    /// </summary>
    /// <remarks>
    /// ⚠ Also a canary fix. The table tests below call OpenCodeSyntheticSearch.Build directly, so
    /// they stayed green when the view-model stopped supplying entries to search at all. Testing a
    /// table is not testing that anything consults it.
    /// </remarks>
    [TestMethod]
    public async Task SyntheticEntries_ReachSearchThroughTheViewModel()
    {
        MainWindowViewModel vm = await LoadedViewModelAsync();

        vm.Search.ExecuteSearch("deny not working");

        Assert.IsTrue(vm.Search.SearchResults.Any(r => r.IsSynthetic),
            "A gotcha query produced no synthetic result, so the view-model is not supplying the "
            + "synthetic table to search. Schema keys alone never match this phrasing.");
    }

    [TestMethod]
    public async Task EveryEntryResolves_AgainstTheRealTree()
    {
        MainWindowViewModel vm = await LoadedViewModelAsync();

        List<string> unresolved =
        [
            .. from e in OpenCodeSyntheticSearch.Build(Strings.SectionOpenCode)
               where e.FindTarget(vm.Navigation) is null
               select e.Id
        ];

        Assert.IsTrue(unresolved.Count == 0,
            "These entries name a page that does not exist in the real tree, so they can never "
            + "appear: " + string.Join(", ", unresolved));
    }

    /// <summary>
    /// A short unrelated query must not drag in a gotcha. Over-firing is worse than not firing:
    /// it pushes real schema hits down the list.
    /// </summary>
    [TestMethod]
    [DataRow("a")]
    [DataRow("xyzzy")]
    [DataRow("theme")]
    public void UnrelatedQueries_MatchNoSyntheticEntry(string query)
    {
        List<string> fired =
        [
            .. from e in OpenCodeSyntheticSearch.Build("OpenCode")
               where e.Trigger.Matches(query)
               select e.Id
        ];

        Assert.IsTrue(fired.Count == 0, $"'{query}' should match nothing, but fired: {string.Join(", ", fired)}");
    }

    /// <summary>Required by MSTest for the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;
}
