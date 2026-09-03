using System.Collections.ObjectModel;

using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Whether a search hit carries the severity of the knob it points at — on every branch that can
/// produce a hit.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A missing dot is not a neutral omission, it is a claim.</b> The danger surface says "no
/// dot means nobody needs to worry about this", so a branch that forgets to ask reports a
/// Critical key as unremarkable. There are four branches that add rows —
/// schema-driven pages, specialised editors matched through the SDK, the page-title fallback, and
/// the product's synthetic rows — which is why each gets a test here rather than one test through
/// whichever branch happened to be convenient.
/// </para>
/// <para>
/// These use a stub page so the assertion is about the SEARCH wiring. That the real group editor
/// and the real danger table agree with the real settings row is asserted separately, against
/// OpenCode's actual policy, in <c>OpenCodeForge.Tests</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class SearchResultDangerTests
{
    private const string Section = "Widget Forge";

    private static readonly DangerAssessment CriticalNow =
        new(AppSeverity.Critical, true, "auto-approves every tool");

    /// <summary>
    /// A page that renders schema properties AND can be asked about danger — the shape the real
    /// <c>SettingsGroupEditorViewModel</c> has.
    /// </summary>
    private sealed class StubPage : ISchemaGroupEditor, IDangerAnnotatedEditor
    {
        public string GroupName { get; init; } = "General";

        public IReadOnlyList<SchemaNode> SchemaNodes { get; init; } = [];

        /// <summary>Paths this page claims, and what it says about each.</summary>
        public Dictionary<string, DangerAssessment> Assessments { get; init; } = [];

        public List<string> Asked { get; } = [];

        public DangerAssessment? AssessDanger(string jsonPath)
        {
            Asked.Add(jsonPath);
            return Assessments.TryGetValue(jsonPath, out DangerAssessment? a) ? a : null;
        }
    }

    /// <summary>A page search can match by title but cannot ask about danger.</summary>
    private sealed class UnannotatedPage : ISchemaGroupEditor
    {
        public string GroupName => "General";

        public IReadOnlyList<SchemaNode> SchemaNodes { get; init; } = [];
    }

    /// <summary>A specialised page owning one JSON subtree, which can also be asked.</summary>
    private sealed class StubSpecialisedPage : IJsonPathScopedEditor, IDangerAnnotatedEditor
    {
        public string OwnedJsonPathPrefix { get; init; } = "permission";

        public Dictionary<string, DangerAssessment> Assessments { get; init; } = [];

        public DangerAssessment? AssessDanger(string jsonPath) =>
            Assessments.TryGetValue(jsonPath, out DangerAssessment? a) ? a : null;
    }

    private static SchemaNode Node(string path, string name) =>
        new(path, name) { ValueType = SchemaValueType.String, Title = name, Description = $"about {name}" };

    private static (ObservableCollection<NavigationNodeViewModel> Tree, NavigationNodeViewModel Child)
        Tree(object editor, string childTitle = "General")
    {
        NavigationNodeViewModel child = new(childTitle) { Editor = editor };
        NavigationNodeViewModel header = new(Section);
        header.Children.Add(child);
        return ([header], child);
    }

    // ── Schema-driven pages ───────────────────────────────────────────────────

    [TestMethod]
    public void SchemaHitCarriesTheAssessmentOfThePathItPointsAt()
    {
        StubPage page = new()
        {
            SchemaNodes = [Node("permission", "permission")],
            Assessments = { ["permission"] = CriticalNow },
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page);
        SearchViewModel vm = new(() => tree, () => false);

        vm.ExecuteSearch("permission");

        SearchResultViewModel hit = vm.SearchResults.Single(r => r.PropertyKey == "permission");
        Assert.AreEqual(AppSeverity.Critical, hit.Danger.Severity);
        Assert.IsTrue(hit.Danger.IsDangerNow, "The held value's danger must survive the hop.");
        Assert.IsTrue(hit.HasDangerSeverity, "A hit with an explanation must render a dot.");
        Assert.AreEqual("Critical: auto-approves every tool", hit.DangerAccessibleText,
            "The dot is a coloured glyph; this string is the only thing a screen reader gets.");
    }

    [TestMethod]
    public void NestedSchemaHitIsAskedByItsOwnFullPath()
    {
        // Search flattens nested schema nodes, so the hit's path is the nested one. Asking with
        // anything else (the parent's path, or the leaf name) would label the wrong knob.
        StubPage page = new()
        {
            SchemaNodes =
            [
                new SchemaNode("permission", "permission")
                {
                    ValueType = SchemaValueType.Object,
                    Properties = [Node("permission.bash", "bash")],
                },
            ],
            Assessments = { ["permission.bash"] = CriticalNow },
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page);
        SearchViewModel vm = new(() => tree, () => false);

        vm.ExecuteSearch("bash");

        SearchResultViewModel hit = vm.SearchResults.Single(r => r.PropertyKey == "permission.bash");
        Assert.AreEqual(AppSeverity.Critical, hit.Danger.Severity,
            "A nested hit must be assessed by its own dotted path.");
        CollectionAssert.Contains(page.Asked, "permission.bash");
    }

    [TestMethod]
    public void APathThePageDoesNotClaimRendersNoDot()
    {
        StubPage page = new()
        {
            SchemaNodes = [Node("theme", "theme")],
            // No assessment for "theme" — the page owns the row but says nothing about it.
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page);
        SearchViewModel vm = new(() => tree, () => false);

        vm.ExecuteSearch("theme");

        SearchResultViewModel hit = vm.SearchResults.Single();
        Assert.AreEqual(DangerAssessment.Unremarkable, hit.Danger);
        Assert.IsFalse(hit.HasDangerSeverity, "No dot for a knob the product did not triage.");
        Assert.AreEqual(string.Empty, hit.DangerAccessibleText,
            "An empty announcement, not the word 'Neutral' — there is nothing to say.");
    }

    [TestMethod]
    public void APageThatCannotBeAskedStillProducesHits()
    {
        // A product with no danger table is a missing feature, not a wrong answer: search must
        // keep working and simply render no dots.
        UnannotatedPage page = new() { SchemaNodes = [Node("theme", "theme")] };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page);
        SearchViewModel vm = new(() => tree, () => false);

        vm.ExecuteSearch("theme");

        SearchResultViewModel hit = vm.SearchResults.Single();
        Assert.AreEqual(DangerAssessment.Unremarkable, hit.Danger);
        Assert.IsFalse(hit.HasDangerSeverity);
    }

    // ── Specialised editors, matched through the SDK ──────────────────────────

    [TestMethod]
    public void SpecialisedEditorHitCarriesTheAssessmentToo()
    {
        StubSpecialisedPage page = new()
        {
            OwnedJsonPathPrefix = "permission",
            Assessments = { ["permission.bash"] = CriticalNow },
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page, "Permissions");

        SearchViewModel vm = new(
            () => tree,
            () => false,
            getSchemaSearchProviders: () =>
            [
                new SchemaSearchProvider(Section, _ =>
                    [new SchemaSearchResult("permission.bash", "bash", "Bash", "Shell access", "Shell access")]),
            ]);

        vm.ExecuteSearch("bash");

        SearchResultViewModel hit = vm.SearchResults.Single(r => r.PropertyKey == "permission.bash");
        Assert.AreEqual(AppSeverity.Critical, hit.Danger.Severity,
            "The specialised-editor branch is where the most dangerous keys live (permissions, "
            + "MCP servers); a dot missing here would be missing on exactly the wrong page.");
    }

    [TestMethod]
    public void APageTitleFallbackRowCarriesNoAssessment()
    {
        // This row points at a PAGE, not a property — its PropertyKey is empty. Asking with an
        // empty path could match an editor whose own path is empty and label a page with one
        // property's severity.
        StubSpecialisedPage page = new()
        {
            Assessments = { [string.Empty] = CriticalNow },
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page, "Permissions");
        SearchViewModel vm = new(() => tree, () => false);

        vm.ExecuteSearch("Permissions");

        SearchResultViewModel hit = vm.SearchResults.Single();
        Assert.AreEqual(string.Empty, hit.PropertyKey, "Guard the premise: this is the fallback row.");
        Assert.AreEqual(DangerAssessment.Unremarkable, hit.Danger,
            "A page-level row must not borrow a property's severity.");
    }

    // ── Synthetic rows ────────────────────────────────────────────────────────

    [TestMethod]
    public void SyntheticRowCarriesTheAssessmentOfThePropertyItMapsOnto()
    {
        // A synthetic row exists to map a CLI flag or a card onto a real config property, so it
        // lands the user on a knob — and should label it the same way the knob is labelled.
        StubPage page = new()
        {
            GroupName = "General",
            SchemaNodes = [Node("permission", "permission")],
            Assessments = { ["permission"] = CriticalNow },
        };
        (ObservableCollection<NavigationNodeViewModel> tree, _) = Tree(page, "Permissions");

        SearchViewModel vm = new(
            () => tree,
            () => false,
            () =>
            [
                new SyntheticSearchEntry
                {
                    Id = "yolo",
                    SectionTitle = Section,
                    GroupTitle = "Permissions",
                    DisplayName = "Skip all permission prompts",
                    PropertyKey = "permission",
                    Snippet = "the --dangerously-skip-permissions flag",
                    Description = "the --dangerously-skip-permissions flag",
                    Trigger = new SearchTrigger { Phrases = ["yolo"] },
                    FindTarget = tree => tree.SelectMany(n => n.Children)
                                             .FirstOrDefault(n => n.Title == "Permissions"),
                },
            ]);

        vm.ExecuteSearch("yolo");

        SearchResultViewModel row = vm.SearchResults.Single(r => r.IsSynthetic);
        Assert.AreEqual(AppSeverity.Critical, row.Danger.Severity,
            "A synthetic row points at a real property and must carry its severity.");
    }
}
