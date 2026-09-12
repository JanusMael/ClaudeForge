using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Memory;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.Services;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The disk-footprint page's place in this window, and the four things about it that fail
/// silently.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The catalog is the one that matters.</b> A page reading through a client whose footprint
/// service was never given OpenCode's catalog renders Claude's seven <c>~/.claude</c> rows under
/// an OpenCode heading — real directories, real sizes, wrong product, nothing thrown and nothing
/// logged. That was the shipped state until 2026-09-12; <c>OpenCodeClientFootprintTests</c> guards
/// the client, and this guards that the PAGE goes through it.
/// </para>
/// <para>
/// ⚠ <b>Three properties of this page are omissions</b> — no sortable columns, no delete button,
/// no literal colours — and an omission cannot be observed from a running view-model, so they are
/// asserted against the markup. A future copy-paste from the sibling app's memory page would
/// restore all three and nothing else would notice.
/// </para>
/// <para>
/// ⚠ Every test that drives <see cref="MainWindowViewModel.InitializeAsync"/> does so against a
/// redirected sandbox, so none of them reads the developer's own OpenCode configuration — and none
/// of them deletes anything, because the page cannot.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeFootprintWiringTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocfp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), """{ "autoupdate": "notify" }""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
        DebugFlags.ResetForTesting();
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

    private async Task<MainWindowViewModel> InitializedAsync()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);
        return vm;
    }

    private static NavigationNodeViewModel FootprintNodeOf(MainWindowViewModel vm) =>
        vm.Navigation.FirstOrDefault(
            n => string.Equals(n.NodeId, MainWindowViewModel.FootprintNodeId, StringComparison.Ordinal))
        ?? throw new AssertFailedException("No footprint node in the navigation tree.");

    // ── The page exists and is reachable ────────────────────────────────────

    [TestMethod]
    public async Task TheFootprintPageIsATopLevelNodeWithItsOwnViewModel()
    {
        MainWindowViewModel vm = await InitializedAsync();
        NavigationNodeViewModel node = FootprintNodeOf(vm);

        Assert.IsTrue(node.IsTopLevel, "The footprint page is a tool page, not a child of a section.");
        Assert.IsInstanceOfType<OpenCodeFootprintViewModel>(node.Editor);
        Assert.AreEqual(Strings.HeadingFootprint, node.Title);
    }

    [TestMethod]
    public async Task TheFootprintPageViewModelIsCachedRatherThanRebuilt()
    {
        MainWindowViewModel vm = await InitializedAsync();
        object? first = FootprintNodeOf(vm).Editor;

        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreSame(first, FootprintNodeOf(vm).Editor,
            "A second initialise built a new footprint page, blanking rows already measured.");
    }

    // ── ⛔ The catalog ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ThePageMeasuresOpenCodesCategories_NotClaudes()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeFootprintViewModel page = (OpenCodeFootprintViewModel)FootprintNodeOf(vm).Editor!;

        await page.RefreshCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            OpenCodeFootprint.Catalog.All.Select(c => c.Id).ToArray(),
            page.Rows.Select(r => r.Category.Id).ToArray(),
            "The page must render OpenCode's catalog, in its deliberate most-disposable-first "
            + "order. Claude's seven would look just as healthy.");
    }

    /// <summary>
    /// ⛔ Catalog order, never a size sort.
    /// </summary>
    /// <remarks>
    /// The largest row regenerates from <c>package.json</c>; the smallest meaningful one is the
    /// only irreplaceable thing on the page. Any ordering that puts big things first puts the row
    /// that must not be touched at the top, which is why this asserts the page does NOT sort.
    /// </remarks>
    [TestMethod]
    public async Task TheRowsKeepCatalogOrderRatherThanSortingBySize()
    {
        // Give two categories real, inverted sizes: a large disposable one and a small
        // irreplaceable one — the exact shape a size sort would reorder.
        string cache = OpenCodePaths.CacheDirectory();
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "models.json"), new string('m', 5000));

        string data = OpenCodePaths.DataDirectory();
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "opencode.db"), "tiny");

        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeFootprintViewModel page = (OpenCodeFootprintViewModel)FootprintNodeOf(vm).Editor!;
        await page.RefreshCommand.ExecuteAsync(null);

        int catalogIndex = page.Rows.ToList().FindIndex(r => r.Category.Id == "model-catalog");
        int databaseIndex = page.Rows.ToList().FindIndex(r => r.Category.Id == "session-database");

        Assert.IsTrue(catalogIndex < databaseIndex,
            "Catalog order puts the disposable 5 KB cache above the 4-byte database. A size sort "
            + "would invert exactly this pair, which is the whole reason the order is guidance.");
    }

    [TestMethod]
    public async Task OnlyTheDatabaseRowIsMarkedIrreplaceable()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeFootprintViewModel page = (OpenCodeFootprintViewModel)FootprintNodeOf(vm).Editor!;
        await page.RefreshCommand.ExecuteAsync(null);

        string[] flagged =
            [.. page.Rows.Where(r => r.IsIrreplaceable).Select(r => r.Category.Id)];

        CollectionAssert.AreEqual(new[] { "session-database" }, flagged,
            "The badge exists because this row's SIZE says the opposite of its importance. "
            + "Spreading it to other rows makes it mean nothing.");
    }

    /// <summary>
    /// Every category in the catalog has a localised label and a tooltip.
    /// </summary>
    /// <remarks>
    /// The row view-model falls back to the PascalCase id, so a missing pair ships as a visibly
    /// unlocalised row rather than a crash — visible, but only to someone who opens the page.
    /// </remarks>
    /// <remarks>
    /// ⚠ <b>Asserted against the resx KEYS, not against the rendered label.</b> The obvious test —
    /// "the label differs from the PascalCase id" — passes for five categories and fails for
    /// <c>logs</c>, whose correct English label is the word "Logs". Keys named
    /// <c>LabelFootprint{Category}</c> / <c>TipFootprint{Category}</c> make the pairing mechanical,
    /// so a category added to the catalog is a test failure naming the two keys it needs.
    /// </remarks>
    [TestMethod]
    public void EveryCategoryHasALabelKeyAndATooltipKey()
    {
        foreach (FootprintCategory category in OpenCodeFootprint.Catalog.All)
        {
            foreach (string prefix in new[] { "LabelFootprint", "TipFootprint" })
            {
                string key = prefix + category.ToString();
                Assert.IsNotNull(
                    typeof(Strings).GetProperty(key),
                    $"Category '{category.Id}' has no {key}. Without it the row renders its "
                    + "PascalCase id, or carries no explanation of what losing it costs.");
            }
        }
    }

    [TestMethod]
    public async Task EveryRenderedRowCarriesBothOfThem()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeFootprintViewModel page = (OpenCodeFootprintViewModel)FootprintNodeOf(vm).Editor!;
        await page.RefreshCommand.ExecuteAsync(null);

        foreach (OpenCodeFootprintRowViewModel row in page.Rows)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.HumanLabel));
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.Tooltip),
                $"Category '{row.Category.Id}' has no tooltip, so the page cannot say whether "
                + "losing it costs anything.");
        }
    }

    [TestMethod]
    public async Task TheTotalCountsEveryCategory_IncludingTheEmptyOnes()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeFootprintViewModel page = (OpenCodeFootprintViewModel)FootprintNodeOf(vm).Editor!;
        await page.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual(OpenCodeFootprint.Catalog.Count, page.Rows.Count,
            "A category that measured zero was still checked; dropping it moves the count for a "
            + "reason the user cannot see.");
        Assert.IsTrue(page.HasMeasured, "The total line stays hidden until a walk completes.");

        // ⚠ Asserted because the first screenshot of this page showed a dimmed Re-measure button
        // and there are two possible explanations — a stuck IsBusy, or the theme's ordinary
        // secondary-button fill. Only one of them is a bug, and the screenshot cannot tell them
        // apart. A walk that has produced rows must leave the button usable.
        Assert.IsFalse(page.IsBusy, "IsBusy stayed set after the walk finished.");
        Assert.IsTrue(page.RefreshCommand.CanExecute(null),
            "Re-measure is disabled after a completed walk, so the page can never be refreshed.");
        StringAssert.Contains(page.TotalLine, page.Rows.Count.ToString(
            System.Globalization.CultureInfo.CurrentCulture));
    }

    // ── ⚠ The omissions, asserted as markup ────────────────────────────────

    [TestMethod]
    public void TheViewOffersNoDeleteAndNoSort()
    {
        string markup = MarkupWithoutComments();

        Assert.IsFalse(markup.Contains("DeleteCommand", StringComparison.Ordinal),
            "A delete button appeared on a page whose catalog's regeneration behaviour is "
            + "unmeasured and one of whose rows is in no backup. See the page view-model.");

        Assert.IsFalse(markup.Contains("DataGrid", StringComparison.Ordinal),
            "A DataGrid brings click-to-sort, and sorting by size inverts the page's guidance.");

        Assert.IsFalse(markup.Contains("SortMemberPath", StringComparison.Ordinal),
            "Row order is the page's guidance and must not be user-reorderable.");
    }

    [TestMethod]
    public void TheViewUsesThemeTokensRatherThanLiteralColours()
    {
        string markup = MarkupWithoutComments();

        List<string> literals =
        [
            .. System.Text.RegularExpressions.Regex
                .Matches(markup, "\"#[0-9A-Fa-f]{3,8}\"")
                .Select(m => m.Value)
        ];

        Assert.AreEqual(0, literals.Count,
            "Literal colours in the footprint view: " + string.Join(", ", literals));
    }

    [TestMethod]
    public void TheViewUsesNoAncestorBindings()
    {
        string markup = MarkupWithoutComments();

        Assert.IsFalse(markup.Contains("$parent[", StringComparison.Ordinal),
            "Ancestor bindings resolve by reflection and trip IL2026 under PublishTrimmed. The "
            + "sibling app's memory view uses four of them; the idiom here is a Click handler.");
    }

    [TestMethod]
    public void TheViewIsRegisteredAsAPageTemplate()
    {
        // Parsed attributes rather than file text, so a type named in a comment cannot count.
        XDocument doc = XDocument.Load(
            Path.Combine(RepoRoot(), "src", "OpenCodeForge", "App.axaml"));
        XNamespace ns = "https://github.com/avaloniaui";

        bool registered = doc.Descendants(ns + "DataTemplate")
            .Any(t => ((string?)t.Attribute("DataType") ?? string.Empty)
                .EndsWith("OpenCodeFootprintViewModel", StringComparison.Ordinal));

        Assert.IsTrue(registered,
            "Without an App.axaml template the page renders as its type name — it compiles, it "
            + "runs, and it logs nothing.");
    }

    // ── ⭐ --deep-link, the flag that makes any of this observable ──────────

    [TestMethod]
    public async Task TheDeepLinkFlagSelectsTheFootprintPage()
    {
        DebugFlags.Initialize(["--deep-link", MainWindowViewModel.FootprintNodeId]);

        MainWindowViewModel vm = await InitializedAsync();

        Assert.AreEqual(MainWindowViewModel.FootprintNodeId, vm.SelectedNode?.NodeId,
            "The flag exists so a page can be LOOKED at. If it does not land, the page is back to "
            + "being unobservable and the verification gap reopens.");
    }

    [TestMethod]
    public async Task AnUnresolvableDeepLinkLeavesTheUsualLandingPage()
    {
        DebugFlags.Initialize(["--deep-link", "no-such-node"]);

        MainWindowViewModel vm = await InitializedAsync();

        Assert.AreEqual(MainWindowViewModel.EssentialsNodeId, vm.SelectedNode?.NodeId,
            "A typo must leave a working window, not an empty editor area.");
    }

    [TestMethod]
    public void ADeepLinkWithNoValueIsRejectedRatherThanEatingTheNextFlag()
    {
        DebugFlags.Initialize(["--deep-link", "--simulate-update"]);

        Assert.IsNull(DebugFlags.DeepLinkNodeId,
            "A leading '--' can never be a node id; recording one would report an unresolved node "
            + "for a mistake the user made one argument earlier.");
        Assert.IsTrue(DebugFlags.SimulateUpdate,
            "Flags PEEK rather than consume in this app, so the following flag must still parse.");
    }

    [TestMethod]
    public void TheDeepLinkFlagIsVisibleToSomeoneLookingForIt()
    {
        DebugFlags.Initialize(["--deep-link", MainWindowViewModel.FootprintNodeId]);

        StringAssert.Contains(DebugFlags.ListActive(), "--deep-link",
            "A flag missing from ListActive works but is invisible in the startup log.");

        string helpText = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "OpenCodeForge", "Services", "DebugFlags.cs"));
        StringAssert.Contains(helpText, "--deep-link <nodeId>",
            "--debug-help must list the flag, or nobody finds it.");
    }

    /// <summary>
    /// The view's markup with every XML comment removed.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Parsed, not line-filtered.</b> This page's header comment explains at length that it
    /// carries no <c>DataGrid</c> and no delete button — so a plain text scan for those tokens
    /// fails on the prose that documents their absence, which is how these three assertions first
    /// reported a page that was already correct. Dropping <see cref="XComment"/> nodes cannot
    /// mistake a mention for a use, and cannot mistake a use for a mention either.
    /// </remarks>
    private static string MarkupWithoutComments()
    {
        XDocument doc = XDocument.Load(ViewPath());
        foreach (XComment comment in doc.DescendantNodes().OfType<XComment>().ToList())
        {
            comment.Remove();
        }

        return doc.ToString();
    }

    private static string ViewPath() =>
        Path.Combine(RepoRoot(), "src", "OpenCodeForge", "Views", "FootprintView.axaml");

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new AssertFailedException("Could not locate the repository root.");
    }
}
