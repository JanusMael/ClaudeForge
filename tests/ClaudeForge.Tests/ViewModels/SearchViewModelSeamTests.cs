using System.Collections.ObjectModel;

using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Drives the neutral search machinery with a <em>fabricated</em> product — no
/// Claude table, no Claude editor types — so what is asserted here is the seam
/// itself rather than this app's use of it.
///
/// <para>
/// This is the coverage the suite has repeatedly been shown to lack: every other
/// search test exercises one product, through the concrete view-models it happens
/// to have. Transposing or emptying those would prove nothing about whether a
/// second product can reach the same behaviour. These fixtures can only pass if
/// the walk dispatches on the two editor interfaces and reads the supplied entry
/// list, which is exactly what slice 3 claims.
/// </para>
/// </summary>
[TestClass]
public sealed class SearchViewModelSeamTests
{
    // ── Fabricated product ────────────────────────────────────────────────

    /// <summary>A schema-driven page belonging to no product in this repo.</summary>
    private sealed class FakeGroupEditor(string groupName, IReadOnlyList<SchemaNode> nodes)
        : ISchemaGroupEditor
    {
        public string GroupName { get; } = groupName;

        public IReadOnlyList<SchemaNode> SchemaNodes { get; } = nodes;
    }

    /// <summary>A specialised page rooted at one JSON path, belonging to no product.</summary>
    private sealed class FakeScopedEditor(string prefix) : IJsonPathScopedEditor
    {
        public string OwnedJsonPathPrefix { get; } = prefix;
    }

    /// <summary>An editor that is neither — search may only match its page title.</summary>
    private sealed class FakeOpaqueEditor;

    private static SyntheticSearchEntry Entry(
        string id,
        SearchTrigger trigger,
        string targetTitle,
        string section = "Widget Forge",
        IReadOnlyList<string>? suppresses = null)
    {
        return new SyntheticSearchEntry
        {
            Id = id,
            Trigger = trigger,
            FindTarget = tree => tree.FirstOrDefault(n => n.Title == targetTitle),
            SectionTitle = section,
            GroupTitle = "Widgets",
            DisplayName = id,
            PropertyKey = id,
            Suppresses = suppresses ?? [],
        };
    }

    // ── Editor interfaces drive the walk ──────────────────────────────────

    [TestMethod]
    public void SchemaGroupEditor_IsMatchedPerProperty_ByInterfaceNotByType()
    {
        List<SchemaNode> nodes =
        [
            new("widget.size", "size") { Title = "Size", Description = "How big the widget is." },
            new("widget.colour", "colour") { Title = "Colour", Description = "Widget paint." },
        ];
        NavigationNodeViewModel page = new("Widgets")
        {
            Editor = new FakeGroupEditor("Widgets", nodes),
        };
        NavigationNodeViewModel header = new("Widget Forge");
        header.Children.Add(page);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(() => tree, () => false);
        vm.ExecuteSearch("widget.size");

        Assert.AreEqual(1, vm.SearchResults.Count,
            "A page that merely implements ISchemaGroupEditor must be searched per property.");
        Assert.AreEqual("widget.size", vm.SearchResults[0].PropertyKey);
        Assert.AreEqual("Widgets", vm.SearchResults[0].GroupTitle,
            "The breadcrumb group comes from the interface, not from a nav title.");
    }

    [TestMethod]
    public void JsonPathScopedEditor_KeepsOnlyHitsInsideItsOwnSubtree()
    {
        NavigationNodeViewModel page = new("Paint")
        {
            Editor = new FakeScopedEditor("paint"),
        };
        NavigationNodeViewModel header = new("Widget Forge");
        header.Children.Add(page);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(
            () => tree,
            () => false,
            getSchemaSearchProviders: () => [new SchemaSearchProvider("Widget Forge", _ =>
            [
                new SchemaSearchResult("paint", "paint", "Paint", "Paint settings.", "Paint settings."),
                new SchemaSearchResult("paint.gloss", "gloss", "Gloss", "Shine level.", "Shine level."),
                new SchemaSearchResult("trim.paint", "paint", "Trim paint", "Not ours.", "Not ours."),
            ])]);

        vm.ExecuteSearch("paint");

        CollectionAssert.AreEquivalent(
            new[] { "paint", "paint.gloss" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray(),
            "The owned prefix matches the node itself and its descendants, but never a path " +
            "that merely ends with the same segment.");
    }

    [TestMethod]
    public void EditorImplementingNeitherInterface_FallsBackToPageTitle()
    {
        NavigationNodeViewModel page = new("Sprockets") { Editor = new FakeOpaqueEditor() };
        NavigationNodeViewModel header = new("Widget Forge");
        header.Children.Add(page);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(() => tree, () => false);
        vm.ExecuteSearch("sprock");

        Assert.AreEqual(1, vm.SearchResults.Count);
        Assert.AreEqual("Sprockets", vm.SearchResults[0].PropertyDisplayName);
        Assert.AreEqual(string.Empty, vm.SearchResults[0].PropertyKey,
            "A title-only hit carries no property key.");
    }

    // ── Synthetic entries ─────────────────────────────────────────────────

    [TestMethod]
    public void NoEntriesSupplied_ProducesNoSyntheticRows()
    {
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false);
        vm.ExecuteSearch("widget");

        Assert.IsFalse(vm.SearchResults.Any(r => r.IsSynthetic),
            "A host that pins no rows gets none — the shell knows no product's phrases.");
    }

    [TestMethod]
    public void Entries_EmitInListOrder()
    {
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("first", new SearchTrigger { Phrases = ["widget"] }, "Widgets"),
            Entry("second", new SearchTrigger { Phrases = ["widget"] }, "Widgets"),
        ]);

        vm.ExecuteSearch("widget");

        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray(),
            "Row order is the product's declaration order.");
    }

    [TestMethod]
    public void Entry_WhoseTargetIsAbsent_EmitsNothingAndSuppressesNothing()
    {
        // The suppressor's target page does not exist in this tree. It must drop
        // out silently AND leave the row it would have displaced in place —
        // otherwise a page one install lacks would hide a page it has.
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("victim", new SearchTrigger { Phrases = ["widget"] }, "Widgets"),
            Entry("suppressor", new SearchTrigger { Phrases = ["widget"] }, "Missing Page",
                suppresses: ["victim"]),
        ]);

        vm.ExecuteSearch("widget");

        CollectionAssert.AreEqual(
            new[] { "victim" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray(),
            "An entry that produced no row must not suppress one either.");
    }

    /// <summary>
    /// Suppression is resolved after the whole list is walked, so an entry
    /// displaces its opposite whether it was declared before or after it. The
    /// pre-slice implementation could only remove a row already added.
    /// </summary>
    [TestMethod]
    public void Suppression_WorksWhenTheSuppressorIsDeclaredFirst()
    {
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("suppressor", new SearchTrigger { Phrases = ["widget"] }, "Widgets",
                suppresses: ["victim"]),
            Entry("victim", new SearchTrigger { Phrases = ["widget"] }, "Widgets"),
        ]);

        vm.ExecuteSearch("widget");

        CollectionAssert.AreEqual(
            new[] { "suppressor" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray());
    }

    /// <summary>
    /// One normalisation for every rule kind. Before the slice the trigger checks
    /// disagreed: a leading space defeated the prefix rules while leaving a
    /// contains rule on the same row firing.
    /// </summary>
    [TestMethod]
    public void Query_IsTrimmedAndLowered_BeforeTriggersSeeIt()
    {
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("flag", new SearchTrigger { PrefixOf = ["dangerzone"], MinQueryLength = 3 }, "Widgets"),
        ]);

        vm.ExecuteSearch("  DANG  ");

        Assert.AreEqual(1, vm.SearchResults.Count,
            "Leading/trailing space and case must not change which rules fire.");
    }

    [TestMethod]
    public void SyntheticRows_PrecedeSchemaRows_AndAreNotSubjectToTheResultCap()
    {
        // 60 matching properties — 10 more than the cap — plus one pinned row.
        List<SchemaNode> nodes = [.. Enumerable.Range(0, 60)
            .Select(i => new SchemaNode($"widget.p{i}", $"p{i}") { Description = "widget property" })];
        NavigationNodeViewModel page = new("Widgets")
        {
            Editor = new FakeGroupEditor("Widgets", nodes),
        };
        NavigationNodeViewModel header = new("Widget Forge");
        header.Children.Add(page);
        ObservableCollection<NavigationNodeViewModel> tree = [header];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("pinned", new SearchTrigger { Phrases = ["widget"] }, "Widget Forge"),
        ]);

        vm.ExecuteSearch("widget");

        Assert.AreEqual("pinned", vm.SearchResults[0].PropertyKey,
            "Pinned rows sit at the top of the list.");
        Assert.AreEqual(51, vm.SearchResults.Count,
            "The cap of 50 applies to schema rows only; the pinned row is extra.");
    }

    /// <summary>
    /// The multi-product case. Every other search fixture in the suite holds one
    /// product, which is why a one-product-only walk could go unnoticed for as
    /// long as it did elsewhere in this refactor.
    /// </summary>
    [TestMethod]
    public void TwoProducts_BothContributeRowsInOnePass()
    {
        NavigationNodeViewModel widgets = new("Widget Forge");
        NavigationNodeViewModel sprockets = new("Sprocket Forge");
        ObservableCollection<NavigationNodeViewModel> tree = [widgets, sprockets];

        SearchViewModel vm = new(() => tree, () => false, () =>
        [
            Entry("widget-row", new SearchTrigger { Phrases = ["forge"] }, "Widget Forge",
                section: "Widget Forge"),
            Entry("sprocket-row", new SearchTrigger { Phrases = ["forge"] }, "Sprocket Forge",
                section: "Sprocket Forge"),
        ]);

        vm.ExecuteSearch("forge");

        CollectionAssert.AreEqual(
            new[] { "widget-row", "sprocket-row" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Widget Forge", "Sprocket Forge" },
            vm.SearchResults.Select(r => r.SectionTitle).ToArray(),
            "Each row is filed under the section its own entry names, not a single global one.");
    }

    /// <summary>
    /// The same multi-product check for the schema walk rather than the pinned
    /// rows. Two sections, each with its own page and its own SDK provider — the
    /// second section must be reached, and its hits must be attributed to it.
    /// </summary>
    [TestMethod]
    public void TwoProducts_SchemaWalkReachesTheSecondSection()
    {
        NavigationNodeViewModel widgetPage = new("Parts")
        {
            Editor = new FakeGroupEditor("Parts",
                [new SchemaNode("widget.bolt", "bolt") { Description = "A forge part." }]),
        };
        NavigationNodeViewModel widgets = new("Widget Forge");
        widgets.Children.Add(widgetPage);

        NavigationNodeViewModel sprocketPage = new("Teeth")
        {
            Editor = new FakeGroupEditor("Teeth",
                [new SchemaNode("sprocket.tooth", "tooth") { Description = "A forge part." }]),
        };
        NavigationNodeViewModel sprockets = new("Sprocket Forge");
        sprockets.Children.Add(sprocketPage);

        ObservableCollection<NavigationNodeViewModel> tree = [widgets, sprockets];

        SearchViewModel vm = new(() => tree, () => false);
        vm.ExecuteSearch("forge part");

        CollectionAssert.AreEqual(
            new[] { "widget.bolt", "sprocket.tooth" },
            vm.SearchResults.Select(r => r.PropertyKey).ToArray(),
            "A walk that stopped after the first section would silently return only half the hits.");
        CollectionAssert.AreEqual(
            new[] { "Widget Forge", "Sprocket Forge" },
            vm.SearchResults.Select(r => r.SectionTitle).ToArray());
    }

    [TestMethod]
    public void Entries_AreRebuiltEveryPass_SoLocalizedTextIsReadLate()
    {
        // The Claude table reads localized card titles at search time because the
        // culture is applied after startup. That only holds if the delegate is
        // re-invoked per pass rather than cached at construction.
        int builds = 0;
        NavigationNodeViewModel node = new("Widgets");
        ObservableCollection<NavigationNodeViewModel> tree = [node];

        SearchViewModel vm = new(() => tree, () => false, () =>
        {
            builds++;
            return [Entry("row", new SearchTrigger { Phrases = ["widget"] }, "Widgets")];
        });

        vm.ExecuteSearch("widget");
        vm.ExecuteSearch("widget");

        Assert.AreEqual(2, builds, "The entry list must be rebuilt on every search pass.");
    }
}
