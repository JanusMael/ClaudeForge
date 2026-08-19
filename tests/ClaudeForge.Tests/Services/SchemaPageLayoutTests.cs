using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// The neutral half of the page-layout seam: turning a flat schema into an ordered
/// set of editor pages.
///
/// <para>
/// The arrangement rules were previously inline in this app's
/// <see cref="NavigationTreeBuilder"/>, where the only thing exercising them was the
/// real Claude schema — so a rule could only be observed through whichever pages
/// Claude happens to declare. These fixtures declare a product that does not exist,
/// which is the only way to tell an arrangement rule apart from the data it was
/// arranging.
/// </para>
/// </summary>
[TestClass]
public sealed class SchemaPageLayoutTests
{
    private static SchemaNode Node(string name)
    {
        return new SchemaNode(name, name);
    }

    private static SchemaPageLayout Layout(
        Dictionary<string, string> map,
        string[] order,
        Dictionary<string, string>? descriptions = null,
        string fallback = "Everything Else")
    {
        return new SchemaPageLayout
        {
            PropertyToPage = map,
            PageOrder = order,
            FallbackPage = fallback,
            PageDescriptions = descriptions
                               ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    [TestMethod]
    public void Arrange_EmitsPagesInTheDeclaredOrder_NotSchemaOrderAndNotAlphabetically()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bolt"] = "Parts",
                ["paint"] = "Finish",
            },
            ["Parts", "Finish"]);

        // The titles are chosen so the declared answer differs from BOTH of the ways
        // this could accidentally come out right: schema order here is paint-then-bolt
        // (so "Finish" first), and alphabetical is also "Finish" first. Only reading
        // PageOrder produces Parts-then-Finish.
        //
        // The first version of this test used the reverse declaration, where the
        // expected answer happened to equal the alphabetical one — so a canary that
        // ignored PageOrder entirely left it green.
        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("paint"), Node("bolt")]);

        CollectionAssert.AreEqual(
            new[] { "Parts", "Finish" },
            pages.Select(p => p.Title).ToArray());
    }

    [TestMethod]
    public void Arrange_SkipsDeclaredPagesThatGotNoProperties()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["bolt"] = "Parts" },
            ["Parts", "Finish", "Safety"]);

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("bolt")]);

        CollectionAssert.AreEqual(
            new[] { "Parts" },
            pages.Select(p => p.Title).ToArray(),
            "A page with nothing on it must not become an empty nav node.");
    }

    [TestMethod]
    public void Arrange_SendsUnmappedPropertiesToTheFallbackPage()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["bolt"] = "Parts" },
            ["Parts", "Everything Else"]);

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("bolt"), Node("mystery")]);

        SchemaPage fallback = pages.Single(p => p.Title == "Everything Else");
        CollectionAssert.AreEqual(
            new[] { "mystery" },
            fallback.Nodes.Select(n => n.Name).ToArray(),
            "A schema property nobody filed must still be reachable.");
    }

    /// <summary>
    /// The fallback page is ordinary once it appears in the order list — it must not
    /// get shunted to the end just because it is the catch-all.
    /// </summary>
    [TestMethod]
    public void Arrange_KeepsTheFallbackPageInItsDeclaredPosition()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["paint"] = "Finish" },
            ["Everything Else", "Finish"]);

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("paint"), Node("mystery")]);

        CollectionAssert.AreEqual(
            new[] { "Everything Else", "Finish" },
            pages.Select(p => p.Title).ToArray());
    }

    [TestMethod]
    public void Arrange_AppendsUndeclaredPagesAlphabetically_AfterTheOrderedOnes()
    {
        // "Zebra" and "Alpha" are named by the map but missing from the order list —
        // the shape a typo in either table produces.
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bolt"] = "Parts",
                ["stripe"] = "Zebra",
                ["ant"] = "Alpha",
            },
            ["Parts"]);

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("stripe"), Node("ant"), Node("bolt")]);

        CollectionAssert.AreEqual(
            new[] { "Parts", "Alpha", "Zebra" },
            pages.Select(p => p.Title).ToArray(),
            "Declared pages first in declared order, then the rest sorted — never dropped.");
    }

    [TestMethod]
    public void Arrange_PreservesSchemaOrderWithinAPage()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["c"] = "Parts",
                ["a"] = "Parts",
                ["b"] = "Parts",
            },
            ["Parts"]);

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("c"), Node("a"), Node("b")]);

        CollectionAssert.AreEqual(
            new[] { "c", "a", "b" },
            pages.Single().Nodes.Select(n => n.Name).ToArray(),
            "Property order on a page is the schema's, not alphabetical.");
    }

    [TestMethod]
    public void Arrange_AttachesTheDeclaredDescription_AndEmptyWhenThereIsNone()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bolt"] = "Parts",
                ["paint"] = "Finish",
            },
            ["Parts", "Finish"],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Parts"] = "Nuts and bolts." });

        IReadOnlyList<SchemaPage> pages = layout.Arrange([Node("bolt"), Node("paint")]);

        Assert.AreEqual("Nuts and bolts.", pages.Single(p => p.Title == "Parts").Description);
        Assert.AreEqual(string.Empty, pages.Single(p => p.Title == "Finish").Description,
            "A page with no declared description gets an empty one, not a missing key throw.");
    }

    [TestMethod]
    public void Arrange_EmptySchema_ProducesNoPages()
    {
        SchemaPageLayout layout = Layout(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["bolt"] = "Parts" },
            ["Parts"]);

        Assert.AreEqual(0, layout.Arrange([]).Count);
    }

    // ── This app's own layout ─────────────────────────────────────────────

    /// <summary>
    /// Every page the property map files something onto must also appear in the order
    /// list. A page named in only one of the two tables still renders — it is appended
    /// alphabetically after the ordered pages — so a typo moves a whole page to the
    /// bottom of the tree and changes nothing else. Nothing else would catch that.
    /// </summary>
    [TestMethod]
    public void ClaudeLayout_EveryMappedPageIsAlsoOrdered()
    {
        SchemaPageLayout layout = NavigationTreeBuilder.Layout;

        foreach (string page in layout.PropertyToPage.Values.Distinct())
        {
            CollectionAssert.Contains(layout.PageOrder.ToList(), page,
                $"Page '{page}' is filed onto by the property map but missing from the page order.");
        }

        CollectionAssert.Contains(layout.PageOrder.ToList(), layout.FallbackPage,
            "The catch-all page must be ordered too, or unmapped settings land at the bottom of the tree.");
    }

    /// <summary>
    /// The other direction: a description keyed to a page title that no longer exists
    /// is dead text nobody will ever see, and reads as coverage that is not there.
    /// </summary>
    [TestMethod]
    public void ClaudeLayout_EveryDescriptionKeyIsARealPage()
    {
        SchemaPageLayout layout = NavigationTreeBuilder.Layout;

        foreach (string title in layout.PageDescriptions.Keys)
        {
            CollectionAssert.Contains(layout.PageOrder.ToList(), title,
                $"Description keyed to '{title}', which is not a page in the order list.");
        }
    }
}
