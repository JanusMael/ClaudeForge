using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The Essentials page's place in the window: where it sits, what the app lands on, and whether
/// its deep link goes anywhere.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Written because inserting the page silently broke the landing selection.</b>
/// <c>SelectedNode</c> was <c>Navigation[0].Children.FirstOrDefault()</c> — correct while element
/// 0 was a section header, <see langword="null"/> the moment a childless top-level node went in
/// front of it, leaving a full navigation tree beside an empty page area.
/// </para>
/// <para>
/// ⓘ <b>Measured, not assumed: one existing test does catch it.</b>
/// <c>FirstRunnableBuildTests.Initialize_BuildsSettingsPagesForBothSections</c> ends with
/// <c>Assert.IsNotNull(vm.SelectedNode)</c> and reddens under the old expression — confirmed by
/// reverting it. So the net is a backstop that says "a page is selected" without saying WHICH, and
/// nothing at all on the deep link. These tests name both. <i>The first draft of this comment
/// claimed the whole suite stayed green; that was never run and it was wrong.</i>
/// </para>
/// <para>
/// ⚠ Every test here drives <see cref="MainWindowViewModel.InitializeAsync"/> against a redirected
/// sandbox, so none of them can read or write the developer's own OpenCode configuration.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeEssentialsWiringTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "ocwire-" + Guid.NewGuid().ToString("N"));
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

    private static OpenCodeEssentialsViewModel EssentialsOf(MainWindowViewModel vm)
    {
        NavigationNodeViewModel node =
            vm.Navigation.FirstOrDefault(
                n => string.Equals(n.NodeId, MainWindowViewModel.EssentialsNodeId, StringComparison.Ordinal))
            ?? throw new AssertFailedException("No Essentials node in the navigation tree.");

        return node.Editor as OpenCodeEssentialsViewModel
            ?? throw new AssertFailedException(
                $"The Essentials node's editor is '{node.Editor?.GetType().Name ?? "null"}'.");
    }

    [TestMethod]
    public async Task TheEssentialsPageIsTheFirstNodeAndIsTopLevel()
    {
        MainWindowViewModel vm = await InitializedAsync();

        Assert.IsTrue(vm.Navigation.Count > 1, "Premise: there must be other nodes to be first of.");
        Assert.AreEqual(MainWindowViewModel.EssentialsNodeId, vm.Navigation[0].NodeId);
        Assert.IsTrue(vm.Navigation[0].IsTopLevel);
        Assert.IsInstanceOfType<OpenCodeEssentialsViewModel>(vm.Navigation[0].Editor);
    }

    /// <summary>The window opens on a page, not on nothing.</summary>
    [TestMethod]
    public async Task TheAppLandsOnTheEssentialsPage()
    {
        MainWindowViewModel vm = await InitializedAsync();

        Assert.IsNotNull(vm.SelectedNode, "The window opened with no page selected.");
        Assert.AreEqual(MainWindowViewModel.EssentialsNodeId, vm.SelectedNode.NodeId);
        Assert.IsNotNull(vm.SelectedNode.Editor, "The landing node must actually own a page.");
    }

    /// <summary>
    /// Whatever the first node is, the landing selection is a node with an editor.
    /// </summary>
    /// <remarks>
    /// Deliberately weaker than the test above, and it is the one that survives a future
    /// re-ordering of the tree: the specific defect was a selection expression that silently
    /// produced <see langword="null"/>, not the choice of Essentials as the landing page.
    /// </remarks>
    [TestMethod]
    public async Task TheLandingSelectionAlwaysOwnsAPage()
    {
        MainWindowViewModel vm = await InitializedAsync();

        Assert.IsNotNull(vm.SelectedNode?.Editor);
    }

    /// <summary>
    /// The page carries a client that has finished opening — its editable card reads through one.
    /// </summary>
    [TestMethod]
    public async Task TheEssentialsPageReadsTheRealConfiguration()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeEssentialsViewModel page = EssentialsOf(vm);

        EssentialsCardViewModel card =
            page.GetCardById(OpenCodeEssentialsViewModel.CardIdAutoupdate)
            ?? throw new AssertFailedException("No autoupdate card.");

        Assert.AreEqual("Notify", card.SelectedOption?.Value,
            "The sandbox config sets autoupdate to \"notify\"; the card read something else, so "
            + "it was handed a client that had not opened.");
    }

    /// <summary>
    /// Every card's deep link names a page that exists.
    /// </summary>
    /// <remarks>
    /// ⭐ Resolved through the window's own lookup against the REAL navigation tree, not against a
    /// copy of the layout table. A card pointing at a page title that no longer exists renders a
    /// button that silently does nothing, and the handler no-ops by design so nothing would throw.
    /// </remarks>
    [TestMethod]
    public async Task EveryCardsDeepLinkResolvesToARealPage()
    {
        MainWindowViewModel vm = await InitializedAsync();

        List<string> targets =
        [
            .. EssentialsOf(vm).Cards
                .Select(c => c.ViewInGroupTitle)
                .Where(t => !string.IsNullOrEmpty(t)),
        ];

        Assert.IsTrue(targets.Count > 0,
            "No card declared a deep-link target, so this test checked nothing.");

        foreach (string target in targets)
        {
            Assert.IsNotNull(vm.FindNodeByTitle(target),
                $"An Essentials card links to page '{target}', which is not in the navigation tree.");
        }
    }

    /// <summary>
    /// Clicking "View in …" actually navigates — the button has a subscriber.
    /// </summary>
    /// <remarks>
    /// ⛔ The message is published through <c>WeakReferenceMessenger.Default</c>, a process-global
    /// singleton. Without a registered handler the command is a control that does nothing, and
    /// there is no compiler or runtime signal for that at all.
    /// </remarks>
    [TestMethod]
    public async Task TheDeepLinkCommandNavigates()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeEssentialsViewModel page = EssentialsOf(vm);

        EssentialsCardViewModel card =
            page.GetCardById(OpenCodeEssentialsViewModel.CardIdAutoupdate)
            ?? throw new AssertFailedException("No autoupdate card.");

        Assert.AreEqual(MainWindowViewModel.EssentialsNodeId, vm.SelectedNode?.NodeId,
            "Premise: the test must start somewhere other than the target page.");

        card.ViewInGroupCommand.Execute(null);

        Assert.AreEqual(card.ViewInGroupTitle, vm.SelectedNode?.Title);
    }

    /// <summary>The deep link also filters the target editor to the property it came from.</summary>
    [TestMethod]
    public async Task TheDeepLinkFiltersTheTargetEditor()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeEssentialsViewModel page = EssentialsOf(vm);

        EssentialsCardViewModel card =
            page.GetCardById(OpenCodeEssentialsViewModel.CardIdAutoupdate)
            ?? throw new AssertFailedException("No autoupdate card.");

        Assert.IsFalse(string.IsNullOrEmpty(card.JsonPathFilter),
            "Premise: the card must carry a filter for this to test anything.");

        card.ViewInGroupCommand.Execute(null);

        SettingsGroupEditorViewModel editor =
            vm.SelectedNode?.Editor as SettingsGroupEditorViewModel
            ?? throw new AssertFailedException("The deep link did not land on a settings page.");

        Assert.AreEqual(card.JsonPathFilter, editor.FilterText);
    }

    /// <summary>A message naming no page must not move the selection or throw.</summary>
    [TestMethod]
    public async Task AnUnknownDeepLinkTargetIsIgnored()
    {
        MainWindowViewModel vm = await InitializedAsync();
        NavigationNodeViewModel? before = vm.SelectedNode;

        WeakReferenceMessenger.Default.Send(
            new NavigateToNavGroupMessage("a page that does not exist"));

        Assert.AreSame(before, vm.SelectedNode);
    }

    /// <summary>
    /// Selecting the page runs its arrival hook, so its filesystem-derived cards re-read.
    /// </summary>
    /// <remarks>
    /// ⚠ The interface's members have default no-op bodies, so a window that never dispatches them
    /// compiles and runs identically — the page would simply show whatever was true at startup for
    /// the rest of the session.
    /// </remarks>
    [TestMethod]
    public async Task ArrivingAtThePageRereadsIt()
    {
        MainWindowViewModel vm = await InitializedAsync();
        OpenCodeEssentialsViewModel page = EssentialsOf(vm);

        EssentialsCardViewModel rules =
            page.GetCardById(OpenCodeEssentialsViewModel.CardIdRules)
            ?? throw new AssertFailedException("No rules card.");

        string before = rules.DerivedText;

        // Navigate away, create the file, come back.
        vm.SelectedNode = vm.Navigation.First(n => n.Children.Count > 0).Children[0];
        string agents = Path.Combine(_sandbox, "AGENTS.md");
        await File.WriteAllTextAsync(agents, "rules", TestContext.CancellationTokenSource.Token);

        vm.SelectedNode = vm.Navigation[0];

        Assert.AreNotEqual(before, rules.DerivedText,
            "The rules card did not re-read on arrival, so it still reports the startup state.");
        StringAssert.Contains(rules.DerivedText, agents, StringComparison.Ordinal);
    }

    // ── The danger table reaches the page ─────────────────────────────
    //
    // ⛔⛔ Slice 2 shipped the Essentials dot and the settings-page dot disagreeing about
    // `autoupdate`: the card's severity was a literal and the table said something else. The
    // card now reads IDangerClassifier, which makes them structurally the same answer — but only
    // if the page is actually HANDED the classifier. That is the wiring these two tests guard,
    // and dropping it has no symptom other than every dot quietly turning grey.
    //
    // ⭐ Both use the PRODUCTION constructor. `BuildViewModel` above builds its HostedSections
    // without a danger table, so a test written against it would assert the wiring against a
    // fixture that has already removed the thing under test.

    /// <summary>The real table's tiers reach the real cards.</summary>
    [TestMethod]
    public async Task TheEssentialsCardsCarryTheDangerTablesTiers()
    {
        MainWindowViewModel vm = new();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);
        OpenCodeEssentialsViewModel page = EssentialsOf(vm);

        Assert.IsTrue(
            page.Cards.Any(c => c.Severity != AppSeverity.Neutral),
            "Every card is Neutral, which is what a page with NO classifier shows. The danger "
            + "table is not reaching the Essentials page, so its dots and the settings pages' "
            + "dots now disagree about every key.");

        // Spot-checked against the table itself rather than a literal tier: the point is that the
        // two surfaces give one answer, not that the answer is any particular colour today.
        EssentialsCardViewModel permission =
            page.GetCardById(OpenCodeEssentialsViewModel.CardIdGlobalPermission)
            ?? throw new AssertFailedException("No permission card.");

        Assert.AreEqual(
            OpenCodeDangerTable.Config.Classify("permission", scope: null, currentValue: null).Severity,
            permission.Severity);
    }

    /// <summary>
    /// A malformed configuration does not fail the section open — so the page gets a real client,
    /// and its tiers are the ordinary ones.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>Measured, and it corrected a test I had already written.</b> This started life as
    /// "the tiers survive a section too broken to open", planting malformed JSON to produce that
    /// state. It passed — and a premise assertion on <see cref="MainWindowViewModel.Status"/>
    /// proved it was passing as a duplicate of the test above: <c>ConfigFileLoader</c> catches
    /// <c>JsonException</c> deliberately, loading the file as an empty root and recording
    /// <c>SettingsDocument.LoadFailure</c> instead of throwing. So a parse-broken file opens
    /// <em>successfully</em>, and no amount of bad JSON reaches the failed-section branch.
    /// <para>
    /// The invariant that branch actually carries — a null client still gets real tiers — is a
    /// statement about the page rather than about the window, and is asserted directly in
    /// <c>OpenCodeEssentialsViewModelTests.WithNoClient_TheSeveritiesStillComeFromTheClassifier</c>.
    /// What is left worth checking here is this: the app keeps working on a file it could not
    /// parse, which is the state a user in trouble is actually in.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task AMalformedConfigStillOpens_AndTheCardsKeepTheirTiers()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_sandbox, "opencode.json"), "{ this is not json",
            TestContext.CancellationTokenSource.Token);

        MainWindowViewModel vm = new();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(string.Empty, vm.Status,
            "A malformed file is loaded as empty rather than failing the section. If this now "
            + "reports a failure, that resilience changed and the load-failure banner is the "
            + "surface to check.");

        Assert.IsTrue(
            EssentialsOf(vm).Cards.Any(c => c.Severity != AppSeverity.Neutral),
            "Every dot went grey on an unparseable config — the page lost its danger table in "
            + "exactly the situation a user needs it most.");
    }
}
