using System.Collections.ObjectModel;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
// NavigationNodeViewModel lives in the reusable Avalonia editor library —
// the GlobalUsings imports the namespace explicitly so this file can use
// the unqualified name throughout.

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SearchViewModel"/>'s deterministic helpers and
/// the synthetic-result branch of <see cref="SearchViewModel.ExecuteSearch"/>.
/// The full schema-walk path is exercised indirectly by manual smoke testing
/// — wiring up a real SettingsGroupEditorViewModel as a Navigation tree
/// child requires a full SettingsWorkspace, which would push these tests
/// into integration territory.
/// </summary>
[TestClass]
public class SearchViewModelTests
{
    // ── BuildSnippet ─────────────────────────────────────────────────────

    [TestMethod]
    public void BuildSnippet_EmptyText_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, SearchViewModel.BuildSnippet(string.Empty, "x", 50));
    }

    [TestMethod]
    public void BuildSnippet_NoMatch_TruncatesToLeadingMaxLen()
    {
        // No occurrence of the query — return the leading maxLen characters
        // with an ellipsis suffix when truncation occurred.
        string text = new('a', 200);
        string snip = SearchViewModel.BuildSnippet(text, "needle", 50);

        Assert.IsTrue(snip.EndsWith("…"), "Truncated no-match snippet must end with ellipsis.");
        Assert.AreEqual(50 + 1, snip.Length, "Length = maxLen + ellipsis char.");
    }

    [TestMethod]
    public void BuildSnippet_NoMatch_ShortText_ReturnsAsIs()
    {
        string snip = SearchViewModel.BuildSnippet("short", "needle", 50);
        Assert.AreEqual("short", snip, "When text fits in maxLen, no truncation or ellipsis.");
    }

    [TestMethod]
    public void BuildSnippet_MatchInMiddle_PadsBothSides()
    {
        // Long text with the match in the middle — both leading and trailing
        // ellipses appear because the match is bounded away from both ends.
        string text =
            "This is a long line of text containing the special needle word inside the middle of the surrounding context window.";
        string snip = SearchViewModel.BuildSnippet(text, "needle", 70);

        Assert.IsTrue(snip.StartsWith("…"), $"Expected leading ellipsis, got: {snip}");
        Assert.IsTrue(snip.EndsWith("…"), $"Expected trailing ellipsis, got: {snip}");
        StringAssert.Contains(snip, "needle");
    }

    [TestMethod]
    public void BuildSnippet_MatchAtStart_NoLeadingEllipsis()
    {
        string snip = SearchViewModel.BuildSnippet("needle is the first word here", "needle", 70);
        Assert.IsFalse(snip.StartsWith("…"),
            "Match at index 0 has no preceding text to elide; no leading ellipsis.");
        StringAssert.Contains(snip, "needle");
    }

    [TestMethod]
    public void BuildSnippet_CaseInsensitiveMatch()
    {
        // Query in upper case must locate the lower-case occurrence.
        string snip = SearchViewModel.BuildSnippet("descriptive text mentioning Claude here", "CLAUDE", 70);
        StringAssert.Contains(snip, "Claude");
    }

    // ── FlattenSchemaNodes ────────────────────────────────────────────────

    [TestMethod]
    public void FlattenSchemaNodes_EmptyInput_YieldsEmpty()
    {
        List<SchemaNode> result = SearchViewModel.FlattenSchemaNodes([]).ToList();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FlattenSchemaNodes_FlatList_YieldsEachNodeOnce()
    {
        SchemaNode[] nodes =
        [
            new SchemaNode("a", "a"),
            new SchemaNode("b", "b"),
            new SchemaNode("c", "c"),
        ];

        List<SchemaNode> result = SearchViewModel.FlattenSchemaNodes(nodes).ToList();

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            result.Select(n => n.Name).ToArray());
    }

    [TestMethod]
    public void FlattenSchemaNodes_NestedTree_YieldsDepthFirst()
    {
        // sandbox
        //   └─ allowUnsandboxedCommands
        //   └─ networkAccess
        //         └─ allowedHosts
        SchemaNode sandbox = new("sandbox", "sandbox")
        {
            Properties =
            [
                new SchemaNode("sandbox.allowUnsandboxedCommands", "allowUnsandboxedCommands"),
                new SchemaNode("sandbox.networkAccess", "networkAccess")
                {
                    Properties =
                    [
                        new SchemaNode("sandbox.networkAccess.allowedHosts", "allowedHosts"),
                    ],
                },
            ],
        };
        SchemaNode top = new("permissions", "permissions");

        List<SchemaNode> result = SearchViewModel.FlattenSchemaNodes([top, sandbox]).ToList();

        // Depth-first order: each parent appears before its children.
        CollectionAssert.AreEqual(
            new[]
            {
                "permissions",
                "sandbox",
                "allowUnsandboxedCommands",
                "networkAccess",
                "allowedHosts",
            },
            result.Select(n => n.Name).ToArray());
    }

    // ── ExecuteSearch — synthetic-result path ─────────────────────────────

    [TestMethod]
    public void ExecuteSearch_DangerouslySkip_AddsSyntheticRow_WhenPermissionsNodePresent()
    {
        // Build a minimal navigation tree that contains the Claude Code → Permissions
        // node so the synthetic --dangerouslySkipPermissions row's "permNode" lookup
        // succeeds. The Permissions node's Editor is null — that's fine; the synthetic
        // row only navigates to the node, it doesn't activate the editor here.
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);

        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            claudeCodeNavTitle: "Claude Code");

        vm.ExecuteSearch("danger");

        Assert.AreEqual(1, vm.SearchResults.Count, "Synthetic --dangerouslySkipPermissions row should appear.");
        Assert.IsTrue(vm.SearchResults[0].IsSynthetic);
        Assert.AreEqual("--dangerouslySkipPermissions", vm.SearchResults[0].PropertyDisplayName);
        Assert.AreSame(permNode, vm.SearchResults[0].Node,
            "Synthetic row must navigate to the Permissions node when present.");
        Assert.IsTrue(vm.IsSearchOpen, "Popup should open whenever results are produced.");
    }

    [TestMethod]
    public void ExecuteSearch_DangerouslySkip_OmitsSyntheticRow_WhenNoPermissionsNode()
    {
        // Tree without a Permissions node — the synthetic row's lookup falls
        // through to null and the row is not added.
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            claudeCodeNavTitle: "Claude Code");

        vm.ExecuteSearch("danger");

        Assert.AreEqual(0, vm.SearchResults.Count);
        Assert.IsFalse(vm.IsSearchOpen);
    }

    [TestMethod]
    public void ExecuteSearch_QueryTooShort_OmitsSyntheticRow()
    {
        // The synthetic row only fires when query.Length >= 3 (guards against
        // a single-letter "d" producing the row prematurely).
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("da");

        Assert.AreEqual(0, vm.SearchResults.Count,
            "Query shorter than 3 chars must not produce the synthetic row.");
    }

    [TestMethod]
    public void ExecuteSearch_NonPrefixOfDanger_OmitsSyntheticRow()
    {
        // The synthetic row only fires when the query is a prefix of
        // "dangerouslyskippermissions" (case-insensitive). A non-prefix
        // string must not trip it.
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("zzz");

        Assert.AreEqual(0, vm.SearchResults.Count);
    }

    [TestMethod]
    public void ExecuteSearch_WhileLoading_ReturnsEarly_WithoutAddingResults()
    {
        // _isLoadingProbe() returning true means the parent VM is in the
        // middle of LoadAllWorkspacesAsync — the navigation tree may be
        // half-built. Search results would be misleading, so the pass is
        // skipped entirely.
        NavigationNodeViewModel permNode = new("Permissions");
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ccHeader.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(() => tree, isLoadingProbe: () => true, "Claude Code");

        vm.ExecuteSearch("danger");

        Assert.AreEqual(0, vm.SearchResults.Count,
            "ExecuteSearch must return early while the parent is mid-load.");
    }

    // ── ExecuteSearch — JsonPath matching ─────────────────────────────────

    /// <summary>
    /// Regression test: searching by dotted JSON path (e.g. "permissions.allow")
    /// must return the matching editor node.
    /// <para>
    /// Bug: the match only checked <c>name</c>, <c>title</c>, and <c>desc</c>.
    /// Typing "permissions.allow" never matched because the individual property
    /// name is just "allow" and the description doesn't contain the path string.
    /// Fix: also match against <c>schema.JsonPath</c>.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ExecuteSearch_SpecializedEditor_MatchedByPageTitle()
    {
        // Specialized editors (Permissions, Hooks, MCP Servers) do not expose
        // individual schema nodes — they use custom editor VMs, not
        // SettingsGroupEditorViewModel. Search must still find them by page title.
        //
        // Two match forms:
        //   "perm"               → "Permissions".Contains("perm")     (title contains query)
        //   "permissions.allow"  → query.Contains("Permissions")      (query contains title)
        NavigationNodeViewModel permNode = new("Permissions")
        {
            // Any non-null, non-SettingsGroupEditorViewModel editor triggers the
            // specialized-editor branch. We use a minimal stand-in here.
            Editor = new PermissionsEditorViewModel(
                new SchemaNode("permissions", "permissions"),
                ConfigScope.User),
        };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [header];
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        // ── partial title match ──
        vm.ExecuteSearch("perm");
        Assert.IsTrue(vm.SearchResults.Any(r => r.Node == permNode),
            "'perm' must match the Permissions page via title prefix.");

        // ── dotted-path query containing the title ──
        vm.ExecuteSearch("permissions.allow");
        Assert.IsTrue(vm.SearchResults.Any(r => r.Node == permNode),
            "'permissions.allow' must match the Permissions page because the query " +
            "contains the page title 'Permissions' (case-insensitive).");

        // ── unrelated query must not match ──
        vm.ExecuteSearch("model");
        Assert.IsFalse(vm.SearchResults.Any(r => r.Node == permNode),
            "'model' must not match the Permissions page.");
    }

    /// <summary>
    /// When a <see cref="SchemaSearchProvider"/> is supplied, specialized editors
    /// (Permissions, Hooks, MCP Servers) surface property-level search results
    /// rather than just a page-title match — the JsonPath returned by the SDK is
    /// mapped to the editor whose owned subtree contains it.
    /// </summary>
    [TestMethod]
    public void ExecuteSearch_SpecializedEditor_WithSdkProvider_ReturnsPropertyHit()
    {
        NavigationNodeViewModel permNode = new("Permissions")
        {
            Editor = new PermissionsEditorViewModel(
                new SchemaNode("permissions", "permissions"),
                ConfigScope.User),
        };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            claudeCodeNavTitle: "Claude Code",
            getSchemaSearchProviders: () =>
                [new SchemaSearchProvider("Claude Code", FakeSearch)]);

        vm.ExecuteSearch("permissions.allow");

        Assert.IsTrue(vm.SearchResults.Any(r => r.PropertyKey == "permissions.allow"),
            "Specialized editor search must surface the SDK-returned JsonPath as a property-level result.");
        Assert.IsTrue(vm.SearchResults.All(r => r.Node == permNode),
            "Every result for permissions.* must navigate to the Permissions specialized editor.");
        Assert.IsFalse(vm.SearchResults.Any(r =>
                r.Node == permNode && string.IsNullOrEmpty(r.PropertyKey)),
            "Page-title fallback must be suppressed when a property-level hit was added.");
        return;

        // Fake SDK that returns a single permissions.allow hit for the matching query.
        IReadOnlyList<SchemaSearchResult> FakeSearch(string query)
        {
            return query.Contains("allow", StringComparison.OrdinalIgnoreCase)
                ?
                [
                    new SchemaSearchResult(
                        "permissions.allow", "allow", "Allow",
                        "Tools that are always allowed.",
                        "Tools that are always allowed.")
                ]
                : [];
        }
    }

    /// <summary>
    /// Without a <see cref="SchemaSearchProvider"/>, specialized editors continue
    /// to fall back to title-only matching — the prior behaviour stays intact for
    /// callers that don't yet wire the SDK delegate.
    /// </summary>
    [TestMethod]
    public void ExecuteSearch_SpecializedEditor_NoSdkProvider_FallsBackToTitleMatch()
    {
        NavigationNodeViewModel permNode = new("Permissions")
        {
            Editor = new PermissionsEditorViewModel(
                new SchemaNode("permissions", "permissions"),
                ConfigScope.User),
        };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        // No getSchemaSearchProviders supplied.
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("perm");
        Assert.IsTrue(vm.SearchResults.Any(r => r.Node == permNode),
            "Without an SDK provider, page-title fallback must still match 'perm' to Permissions.");
    }

    // ── StripPhraseQuotes ────────────────────────────────────────────────

    [TestMethod]
    public void StripPhraseQuotes_StraightDoubleQuotes_StripsPair()
    {
        Assert.AreEqual("permissions.allow",
            SearchViewModel.StripPhraseQuotes("\"permissions.allow\""));
    }

    [TestMethod]
    public void StripPhraseQuotes_StraightSingleQuotes_StripsPair()
    {
        Assert.AreEqual("permissions.allow",
            SearchViewModel.StripPhraseQuotes("'permissions.allow'"));
    }

    [TestMethod]
    public void StripPhraseQuotes_CurlyDoubleQuotes_StripsPair()
    {
        Assert.AreEqual("permissions.allow",
            SearchViewModel.StripPhraseQuotes("“permissions.allow”"));
    }

    [TestMethod]
    public void StripPhraseQuotes_MismatchedQuotes_LeftIntact()
    {
        // Opening quote only — user mid-typing — must not strip; the substring
        // search will then find no results until the closing quote arrives.
        Assert.AreEqual("\"permissions.allow",
            SearchViewModel.StripPhraseQuotes("\"permissions.allow"));
        Assert.AreEqual("permissions.allow\"",
            SearchViewModel.StripPhraseQuotes("permissions.allow\""));
        Assert.AreEqual("\"permissions.allow'",
            SearchViewModel.StripPhraseQuotes("\"permissions.allow'"));
    }

    [TestMethod]
    public void StripPhraseQuotes_NoQuotes_LeftIntact()
    {
        Assert.AreEqual("permissions.allow",
            SearchViewModel.StripPhraseQuotes("permissions.allow"));
    }

    [TestMethod]
    public void StripPhraseQuotes_TooShort_LeftIntact()
    {
        Assert.AreEqual("\"", SearchViewModel.StripPhraseQuotes("\""));
        Assert.AreEqual("", SearchViewModel.StripPhraseQuotes(""));
    }

    [TestMethod]
    public void ExecuteSearch_QuotedQuery_StripsQuotesBeforeMatching()
    {
        // Same fixture as the SDK-provider test, but the query arrives wrapped
        // in straight double quotes — must still produce the property-level hit.
        NavigationNodeViewModel permNode = new("Permissions")
        {
            Editor = new PermissionsEditorViewModel(
                new SchemaNode("permissions", "permissions"),
                ConfigScope.User),
        };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(permNode);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(
            getNavigationTree: () => tree,
            isLoadingProbe: () => false,
            claudeCodeNavTitle: "Claude Code",
            getSchemaSearchProviders: () =>
                [new SchemaSearchProvider("Claude Code", FakeSearch)]);

        vm.ExecuteSearch("\"permissions.allow\"");

        Assert.IsTrue(vm.SearchResults.Any(r => r.PropertyKey == "permissions.allow"),
            "Quoted query must produce the same property-level hit as the unquoted form.");
        return;

        IReadOnlyList<SchemaSearchResult> FakeSearch(string query)
        {
            // Defensive assertion: the SDK delegate must receive the unquoted
            // query — otherwise the substring match against schema content fails.
            Assert.IsFalse(query.StartsWith('"'), "Query reaching SDK must have quotes stripped.");
            return query.Contains("allow", StringComparison.OrdinalIgnoreCase)
                ?
                [
                    new SchemaSearchResult(
                        "permissions.allow", "allow", "Allow",
                        "Tools that are always allowed.",
                        "Tools that are always allowed.")
                ]
                : [];
        }
    }

    [TestMethod]
    public void ExecuteSearch_DottedJsonPath_MatchesSchemaNode()
    {
        // Build a minimal SettingsGroupEditorViewModel containing a schema node
        // whose JsonPath is "permissions.allow" but whose Name is just "allow".
        // Searching by the full dotted path must find it.
        List<SchemaNode> nodes =
        [
            new("permissions.allow", "allow")
            {
                ValueType = SchemaValueType.Array,
                Title = "Allow",
                Description = "Tools that are always allowed.",
            },

            new("permissions.deny", "deny")
            {
                ValueType = SchemaValueType.Array,
                Title = "Deny",
                Description = "Tools that are always denied.",
            },

        ];
        SettingsWorkspace workspace = new([
            new SettingsDocument(ConfigScope.User, "user.json",
                new JsonObject(), isReadOnly: false)
        ]);
        SettingsGroupEditorViewModel groupVm = new(
            "Permissions", nodes, workspace);

        NavigationNodeViewModel child = new("Permissions") { Editor = groupVm };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(child);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        // Query by full dotted path — must find the "allow" node.
        vm.ExecuteSearch("permissions.allow");

        Assert.IsTrue(vm.SearchResults.Count > 0,
            "Searching 'permissions.allow' must match the schema node whose JsonPath " +
            "is 'permissions.allow', even though its Name is only 'allow'.");
        Assert.IsTrue(vm.SearchResults.Any(r => r.PropertyKey == "permissions.allow"),
            "The matched result must carry the JsonPath 'permissions.allow' as PropertyKey.");

        // "permissions.deny" should NOT appear — it has a different path.
        Assert.IsFalse(vm.SearchResults.Any(r => r.PropertyKey == "permissions.deny"),
            "'permissions.deny' must not appear in results for a 'permissions.allow' query.");
    }

    [TestMethod]
    public void ExecuteSearch_PartialDottedPath_MatchesBothAllowAndDeny()
    {
        // Searching for just "permissions" (no dot) must match ALL nodes whose
        // JsonPath starts with "permissions." — both allow and deny.
        List<SchemaNode> nodes =
        [
            new("permissions.allow", "allow") { ValueType = SchemaValueType.Array },
            new("permissions.deny", "deny") { ValueType = SchemaValueType.Array },
            new("model", "model") { ValueType = SchemaValueType.String },
        ];
        SettingsWorkspace workspace = new([
            new SettingsDocument(ConfigScope.User, "user.json",
                new JsonObject(), isReadOnly: false)
        ]);
        SettingsGroupEditorViewModel groupVm = new(
            "General", nodes, workspace);

        NavigationNodeViewModel child = new("General") { Editor = groupVm };
        NavigationNodeViewModel header = new("Claude Code");
        header.Children.Add(child);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("permissions");

        List<string> keys = vm.SearchResults.Select(r => r.PropertyKey).ToList();
        CollectionAssert.Contains(keys, "permissions.allow", "allow node must match.");
        CollectionAssert.Contains(keys, "permissions.deny", "deny node must match.");
        Assert.IsFalse(keys.Contains("model"), "'model' must not appear in a 'permissions' search.");
    }

    // ── ExecuteSearch — Essentials synthetic hits ──────────

    /// <summary>
    /// Helper to build a tree carrying just the Essentials node + a real
    /// EssentialsViewModel (over an empty in-memory client).  Every test
    /// in this section uses the same shape; factor it out so the synthetic
    /// trigger walk stays the focus.
    /// </summary>
    private static (ObservableCollection<NavigationNodeViewModel> Tree,
        NavigationNodeViewModel EssentialsNode,
        EssentialsViewModel Vm)
        BuildEssentialsOnlyTree()
    {
        JsonObject root = new();
        SettingsDocument doc = new(ConfigScope.User, "user.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
        EssentialsViewModel essentialsVm = new(client, new FakeEnvironmentProvider());

        NavigationNodeViewModel node = new("Essentials") { Editor = essentialsVm };
        ObservableCollection<NavigationNodeViewModel> tree = [node];
        return (tree, node, essentialsVm);
    }

    [TestMethod]
    public void EssentialsTriggers_TableCovers_EveryCardId()
    {
        // If a future commit adds a new EssentialsCardKind / card without
        // wiring its trigger phrases, this test surfaces the gap.
        SettingsWorkspace ws = new([
            new SettingsDocument(ConfigScope.User, "u.json", new JsonObject(), isReadOnly: false)
        ]);
        ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
        EssentialsViewModel vm = new(client, new FakeEnvironmentProvider());

        HashSet<string> ids = vm.Cards.Select(c => c.Id).ToHashSet();
        foreach (string triggerKey in SearchViewModel.EssentialsTriggers.Keys)
        {
            CollectionAssert.Contains(ids.ToList(), triggerKey,
                $"Trigger key '{triggerKey}' must reference a real card id.");
        }

        foreach (string id in ids)
        {
            Assert.IsTrue(SearchViewModel.EssentialsTriggers.ContainsKey(id),
                $"Card '{id}' must have at least one trigger phrase to be searchable.");
        }
    }

    [TestMethod]
    public void ExecuteSearch_EssentialsTriggers_ProduceSyntheticHit_Thinking()
    {
        (ObservableCollection<NavigationNodeViewModel> tree, NavigationNodeViewModel essentialsNode, EssentialsViewModel _) = BuildEssentialsOnlyTree();
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("thinking");

        Assert.IsTrue(vm.SearchResults.Any(r =>
                r.IsSynthetic
                && r.Node == essentialsNode
                && r.PropertyKey == EssentialsViewModel.CardIdMaxThinkingTokens),
            "'thinking' must produce a synthetic hit that deep-links to the MAX_THINKING_TOKENS card.");
    }

    [TestMethod]
    public void ExecuteSearch_EssentialsTriggers_PartialMatch_Sandbox()
    {
        // The trigger contains rule means typing the start of a phrase
        // ("san") still surfaces the sandbox cards before the user finishes.
        (ObservableCollection<NavigationNodeViewModel> tree, NavigationNodeViewModel essentialsNode, EssentialsViewModel _) = BuildEssentialsOnlyTree();
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("san");

        List<string> hits = vm.SearchResults
                              .Where(r => r.IsSynthetic && r.Node == essentialsNode)
                              .Select(r => r.PropertyKey)
                              .ToList();
        CollectionAssert.Contains(hits, EssentialsViewModel.CardIdSandboxEnabled);
        CollectionAssert.Contains(hits, EssentialsViewModel.CardIdSandboxDomains);
    }

    [TestMethod]
    public void ExecuteSearch_EssentialsTriggers_QueryTooShort_NoHits()
    {
        (ObservableCollection<NavigationNodeViewModel> tree, NavigationNodeViewModel _, EssentialsViewModel _) = BuildEssentialsOnlyTree();
        SearchViewModel vm = new(() => tree, () => false, "Claude Code");

        vm.ExecuteSearch("t");

        Assert.IsFalse(vm.SearchResults.Any(r => r.IsSynthetic && r.PropertyKey.Length > 0),
            "Single-character queries must not surface synthetic Essentials hits.");
    }

    [TestMethod]
    public void ExecuteSearch_NoEssentialsNode_NoSyntheticHits()
    {
        // Tree without an Essentials node — synthetic-hit walk silently bails.
        NavigationNodeViewModel ccHeader = new("Claude Code");
        ObservableCollection<NavigationNodeViewModel> tree = [ccHeader];

        SearchViewModel vm = new(() => tree, () => false, "Claude Code");
        vm.ExecuteSearch("thinking tokens");

        Assert.IsFalse(vm.SearchResults.Any(r => r.IsSynthetic),
            "Without an Essentials node in the tree, no synthetic Essentials hits should appear.");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        SearchViewModel vm = new(() => [], () => false, "Claude Code");
        vm.Dispose();
        vm.Dispose(); // second call must not throw
    }
}