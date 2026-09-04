using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Avalonia.Updates;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The three Essentials cards: the labelled union, and the two derived resolver reports.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Nothing here mutates process environment.</b> Both the client and the derived cards take
/// an <see cref="OpenCodeEnvironment"/> value, and the "default" global directory is redirected
/// through <see cref="PlatformPaths.TestUserProfileOverride"/>, which is <c>AsyncLocal</c>. A test
/// that set <c>OPENCODE_CONFIG_DIR</c> would leak into whatever ran alongside it and the failure
/// would read as a flake.
/// </para>
/// <para>
/// ⭐ The option-label test asserts against the <b>full editor's own</b> option list rather than a
/// copy of the expected words. A test carrying its own strings passes just as happily when the two
/// surfaces have drifted apart, which is the exact thing it exists to prevent.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeEssentialsViewModelTests
{
    private string _root = string.Empty;
    private string _configDir = string.Empty;
    private string _home = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ocess-" + Guid.NewGuid().ToString("N"));
        _configDir = Path.Combine(_root, "cfg");
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(Path.Combine(_home, ".config", "opencode"));
        PlatformPaths.TestUserProfileOverride = _home;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    /// <summary>
    /// The layout is the real one's shape, with a page name the production code could not have
    /// guessed.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Deliberately NOT <c>"General"</c>, which is where the real table puts
    /// <c>autoupdate</c>.</b> A fixture echoing the production value cannot fail when the code
    /// stops consulting the table and hardcodes that value instead — the assertion would compare a
    /// literal against the same literal and pass. Naming the page something no implementation
    /// would invent is what makes "it read the table" observable.
    /// </remarks>
    private const string AutoupdatePage = "A page only this fixture names";

    private static SchemaPageLayout Layout { get; } = new()
    {
        PropertyToPage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["autoupdate"] = AutoupdatePage,
        },
        PageOrder = [AutoupdatePage],
        FallbackPage = "Advanced",
    };

    /// <summary>The redirected environment these tests use by default.</summary>
    /// <remarks>
    /// ⚠ <c>ProjectConfigDisabled</c> so the walk cannot climb out of the sandbox and pick up a
    /// real <c>opencode.json</c> from an ancestor of the test runner's working directory.
    /// </remarks>
    private OpenCodeEnvironment Env(string? configPath = null, string? inline = null)
        => new(_configDir, configPath, inline, ProjectConfigDisabled: true);

    /// <summary>An environment with <c>OPENCODE_CONFIG_DIR</c> genuinely unset.</summary>
    /// <remarks>
    /// A separate helper rather than a nullable argument on <see cref="Env"/>: an optional
    /// parameter defaulting to null cannot distinguish "not specified" from "explicitly none",
    /// and quietly falling back to the redirected directory would have made every no-redirect
    /// assertion below test the redirected case instead.
    /// </remarks>
    private static OpenCodeEnvironment EnvWithoutRedirect(string? configDir = null)
        => new(configDir, ConfigPath: null, InlineContent: null, ProjectConfigDisabled: true);

    /// <summary>The client the most recent <see cref="BuildAsync"/> handed to the page.</summary>
    private OpenCodeClient? _client;

    private async Task<OpenCodeEssentialsViewModel> BuildAsync(
        string? configJson = null, OpenCodeEnvironment? env = null)
    {
        OpenCodeEnvironment environment = env ?? Env();
        if (configJson is not null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_configDir, "opencode.json"), configJson,
                TestContext.CancellationTokenSource.Token);
        }

        _client = new OpenCodeClient(OpenCodeClient.GlobalScope, environment);
        await _client.OpenAsync(projectRoot: null, TestContext.CancellationTokenSource.Token);

        return new OpenCodeEssentialsViewModel(_client, environment, Layout);
    }

    private OpenCodeClient Client
        => _client ?? throw new AssertFailedException("BuildAsync has not run.");

    private static EssentialsCardViewModel Card(OpenCodeEssentialsViewModel vm, string id)
        => vm.GetCardById(id) ?? throw new AssertFailedException($"No card '{id}'.");

    // ── Shape ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ThePageHoldsTheThreeCardsThatExerciseTheNewKinds()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        CollectionAssert.AreEqual(
            new[]
            {
                OpenCodeEssentialsViewModel.CardIdAutoupdate,
                OpenCodeEssentialsViewModel.CardIdRules,
                OpenCodeEssentialsViewModel.CardIdActiveConfig,
            },
            vm.Cards.Select(c => c.Id).ToArray());

        Assert.AreEqual(EssentialsCardKind.LabelledEnum,
            Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate).Kind);
        Assert.AreEqual(EssentialsCardKind.Derived,
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).Kind);
        Assert.AreEqual(EssentialsCardKind.Derived,
            Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).Kind);
    }

    /// <summary>
    /// Each card carries the severity the plan assigns it.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>Written because a canary with a deliberately empty prediction found nothing guarding
    /// this.</b> Changing the autoupdate card from <c>Neutral</c> to <c>Critical</c> reddened zero
    /// tests. Severity drives the coloured dot, so the defect it hides is a card that shouts about
    /// a setting nothing is wrong with — plausible enough on screen to survive review, and the
    /// tier is a documented product decision rather than an implementation detail.
    /// <para>
    /// All three are Neutral today <em>on purpose</em>: none of these three keys can hold a value
    /// that weakens a safety boundary. Their warnings are conditional and ride the danger banner
    /// instead, which is a different signal from a standing tier.
    /// </para>
    /// </remarks>
    [TestMethod]
    [DataRow(OpenCodeEssentialsViewModel.CardIdAutoupdate)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdRules)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdActiveConfig)]
    public async Task EachCardCarriesItsAssignedSeverity(string cardId)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.AreEqual(AppSeverity.Neutral, Card(vm, cardId).Severity);
    }

    /// <summary>
    /// The card's picker offers the same words, in the same order, as the full editor for the same
    /// key.
    /// </summary>
    [TestMethod]
    public async Task AutoupdateOptions_AreTheFullEditorsOwnOptions()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        CollectionAssert.AreEqual(
            OpenCodeAutoupdateEditorViewModel.Options.Select(o => o.Label).ToArray(),
            card.LabelledOptions.Select(o => o.Label).ToArray(),
            "The Essentials card and the full editor must not describe one key with two vocabularies.");

        CollectionAssert.AreEqual(
            OpenCodeAutoupdateEditorViewModel.Options.Select(o => o.Mode.ToString()).ToArray(),
            card.LabelledOptions.Select(o => o.Value).ToArray(),
            "The card's tokens must name the modes the codec understands.");
    }

    /// <summary>The deep-link target comes from the layout table, not from a literal.</summary>
    [TestMethod]
    public async Task AutoupdateCard_LinksToThePageTheLayoutAssignsIt()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        Assert.AreEqual(
            Layout.PropertyToPage[OpenCodeEssentialsViewModel.AutoupdatePath],
            card.ViewInGroupTitle);
        Assert.AreEqual(OpenCodeEssentialsViewModel.AutoupdatePath, card.JsonPathFilter);
    }

    /// <summary>A derived card has no deep link, so its button must not render.</summary>
    [TestMethod]
    public async Task DerivedCards_HaveNoDeepLink()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.AreEqual(string.Empty, Card(vm, OpenCodeEssentialsViewModel.CardIdRules).ViewInGroupTitle);
        Assert.AreEqual(string.Empty, Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).ViewInGroupTitle);
    }

    // ── autoupdate: read ──────────────────────────────────────────────

    [TestMethod]
    [DataRow("""{ "autoupdate": true }""", nameof(OpenCodeAutoupdateMode.Automatic))]
    [DataRow("""{ "autoupdate": false }""", nameof(OpenCodeAutoupdateMode.Disabled))]
    [DataRow("""{ "autoupdate": "notify" }""", nameof(OpenCodeAutoupdateMode.Notify))]
    [DataRow("{}", nameof(OpenCodeAutoupdateMode.NotSet))]
    public async Task AutoupdateCard_ReadsEachArm(string json, string expected)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        Assert.IsNotNull(card.SelectedOption);
        Assert.AreEqual(expected, card.SelectedOption.Value);
        Assert.IsFalse(card.EnumDisabled);
        Assert.IsFalse(card.ShowConstraintNotice);
    }

    /// <summary>
    /// A value matching no arm is shown as unpickable rather than silently coerced.
    /// </summary>
    /// <remarks>
    /// ⚠ Leaving the picker on some arm would mean the next edit overwrites a value the app could
    /// not read. Disabling it AND saying why is the same call the full editor makes.
    /// </remarks>
    [TestMethod]
    public async Task AutoupdateCard_HoldsAnUnreadableValue_WithoutSelectingAnArm()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "autoupdate": "Notify" }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        Assert.IsNull(card.SelectedOption, "\"Notify\" is not \"notify\" — the enum arm is exact.");
        Assert.IsTrue(card.EnumDisabled);
        Assert.IsTrue(card.ShowConstraintNotice);
        Assert.AreEqual(Strings.AutoupdateUnrecognisedBanner, card.ConstraintNoticeText);
    }

    // ── autoupdate: write ─────────────────────────────────────────────

    [TestMethod]
    [DataRow(nameof(OpenCodeAutoupdateMode.Automatic), true)]
    [DataRow(nameof(OpenCodeAutoupdateMode.Disabled), false)]
    public async Task AutoupdateCard_WritesTheBooleanArms(string token, bool expected)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        card.SelectedOption = card.LabelledOptions.Single(o => o.Value == token);

        JsonNode? written = Client.GetEffective<JsonNode>("autoupdate");
        Assert.IsNotNull(written);
        Assert.AreEqual(expected, written.GetValue<bool>());
    }

    [TestMethod]
    public async Task AutoupdateCard_WritesTheStringArm()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        card.SelectedOption = card.LabelledOptions.Single(
            o => o.Value == nameof(OpenCodeAutoupdateMode.Notify));

        JsonNode? written = Client.GetEffective<JsonNode>("autoupdate");
        Assert.IsNotNull(written);
        Assert.AreEqual(OpenCodeAutoupdateCodec.NotifyLiteral, written.GetValue<string>());
    }

    /// <summary>"Not set" removes the key — a different act from writing <c>false</c>.</summary>
    [TestMethod]
    public async Task AutoupdateCard_NotSet_RemovesTheKey()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "autoupdate": true }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);

        Assert.AreEqual(nameof(OpenCodeAutoupdateMode.Automatic), card.SelectedOption?.Value,
            "Premise: the key must start out set, or this test cannot observe a removal.");

        card.SelectedOption = card.LabelledOptions.Single(
            o => o.Value == nameof(OpenCodeAutoupdateMode.NotSet));

        Assert.IsNull(Client.GetEffective<JsonNode>("autoupdate"));
    }

    /// <summary>
    /// Re-selecting the value already in the file must not dirty the document.
    /// </summary>
    /// <remarks>
    /// A ComboBox reasserts its selection on more occasions than a user changes it — an
    /// <c>ItemsSource</c> swap, a re-read, a focus change — and each one would otherwise pin a
    /// redundant key and light the Save banner over an empty diff.
    /// </remarks>
    [TestMethod]
    public async Task AutoupdateCard_ReSelectingTheEffectiveValue_DoesNotDirty()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "autoupdate": "notify" }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate);
        

        Assert.IsFalse(Client.HasUnsavedChanges, "Premise: loading a file must not dirty it.");

        card.SelectedOption = card.LabelledOptions.Single(
            o => o.Value == nameof(OpenCodeAutoupdateMode.Notify));

        Assert.IsFalse(Client.HasUnsavedChanges);
    }

    // ── Derived: rules ────────────────────────────────────────────────

    [TestMethod]
    public async Task RulesCard_ReportsNoGlobalRules_WhenThereIsNoAgentsFile()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.AreEqual(Strings.EssentialsRulesNone,
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).DerivedText);
        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdRules).IsDanger);
    }

    [TestMethod]
    public async Task RulesCard_NamesTheGlobalAgentsFile()
    {
        string agents = Path.Combine(_configDir, "AGENTS.md");
        await File.WriteAllTextAsync(agents, "rules", TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        StringAssert.Contains(
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).DerivedText,
            agents,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both directories holding an <c>AGENTS.md</c> raises the caution — the observable
    /// precondition of the documented gotcha.
    /// </summary>
    [TestMethod]
    public async Task RulesCard_WarnsWhenBothDirectoriesHoldRules()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_configDir, "AGENTS.md"), "redirected",
            TestContext.CancellationTokenSource.Token);
        await File.WriteAllTextAsync(
            Path.Combine(_home, ".config", "opencode", "AGENTS.md"), "default",
            TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.IsTrue(Card(vm, OpenCodeEssentialsViewModel.CardIdRules).IsDanger);
    }

    /// <summary>Only the redirected file existing is not a conflict.</summary>
    [TestMethod]
    public async Task RulesCard_DoesNotWarn_WhenOnlyTheRedirectedFileExists()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_configDir, "AGENTS.md"), "redirected",
            TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdRules).IsDanger);
    }

    /// <summary>
    /// A redirect that points AT the default directory is not a redirect, and must not warn about
    /// a file shadowing itself.
    /// </summary>
    [TestMethod]
    public async Task RulesCard_DoesNotWarn_WhenTheRedirectIsTheDefaultDirectory()
    {
        string defaultDir = Path.Combine(_home, ".config", "opencode");
        await File.WriteAllTextAsync(
            Path.Combine(defaultDir, "AGENTS.md"), "default",
            TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync(env: EnvWithoutRedirect(defaultDir));

        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdRules).IsDanger);
    }

    /// <summary>With no redirect at all there is nothing that could be shadowed.</summary>
    [TestMethod]
    public async Task RulesCard_DoesNotWarn_WithNoRedirect()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_home, ".config", "opencode", "AGENTS.md"), "default",
            TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync(env: EnvWithoutRedirect());

        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdRules).IsDanger);
    }

    // ── Derived: active config ────────────────────────────────────────

    [TestMethod]
    public async Task ActiveConfigCard_NamesTheRedirectedFileAndTheVariable()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        string text = Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).DerivedText;

        StringAssert.Contains(text, _configDir, StringComparison.Ordinal);
        StringAssert.Contains(text, "OPENCODE_CONFIG_DIR", StringComparison.Ordinal);
        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).IsDanger);
    }

    [TestMethod]
    public async Task ActiveConfigCard_NamesTheDefaultFile_WithNoVariableSet()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(env: EnvWithoutRedirect());
        string text = Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).DerivedText;

        StringAssert.Contains(text, Path.Combine(_home, ".config", "opencode"), StringComparison.Ordinal);
        Assert.IsFalse(text.Contains("OPENCODE_CONFIG", StringComparison.Ordinal),
            "With nothing set there is no variable to attribute the path to.");
    }

    /// <summary>An explicit path outranks a redirected directory.</summary>
    [TestMethod]
    public async Task ActiveConfigCard_PrefersTheExplicitPath()
    {
        string custom = Path.Combine(_root, "custom.json");
        await File.WriteAllTextAsync(custom, "{}", TestContext.CancellationTokenSource.Token);

        OpenCodeEssentialsViewModel vm = await BuildAsync(env: Env(configPath: custom));
        string text = Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).DerivedText;

        StringAssert.Contains(text, custom, StringComparison.Ordinal);
        StringAssert.Contains(text, "OPENCODE_CONFIG", StringComparison.Ordinal);
    }

    /// <summary>
    /// Inline content is the one state where saving a file changes nothing the agent reads, so it
    /// outranks everything and raises the banner.
    /// </summary>
    [TestMethod]
    public async Task ActiveConfigCard_FlagsInlineContentAsDangerous()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(env: Env(inline: "{}"));
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig);

        Assert.AreEqual(Strings.EssentialsActiveConfigInline, card.DerivedText);
        Assert.IsTrue(card.IsDanger);
        Assert.AreEqual(Strings.EssentialsActiveConfigInlineBanner, card.DangerBannerText);
    }

    // ── Degraded ──────────────────────────────────────────────────────

    /// <summary>
    /// With no client the derived cards still answer — the user whose configuration is too broken
    /// to open is exactly the one who needs to know which file the app was reading.
    /// </summary>
    [TestMethod]
    public void WithNoClient_TheDerivedCardsStillReport()
    {
        OpenCodeEssentialsViewModel vm = new(null, Env(), Layout);

        Assert.AreEqual(Strings.EssentialsRulesNone,
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).DerivedText);
        StringAssert.Contains(
            Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).DerivedText,
            _configDir,
            StringComparison.Ordinal);

        // And the editable card degrades to "unset" rather than throwing out of a fire-and-forget
        // read in the constructor.
        Assert.AreEqual(nameof(OpenCodeAutoupdateMode.NotSet),
            Card(vm, OpenCodeEssentialsViewModel.CardIdAutoupdate).SelectedOption?.Value);
    }

    /// <summary>Arriving at the page re-reads, so a file created meanwhile is picked up.</summary>
    [TestMethod]
    public async Task NavigatingToThePage_RereadsTheDerivedCards()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        Assert.AreEqual(Strings.EssentialsRulesNone,
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).DerivedText);

        string agents = Path.Combine(_configDir, "AGENTS.md");
        await File.WriteAllTextAsync(agents, "rules", TestContext.CancellationTokenSource.Token);

        ((INavigablePage)vm).OnNavigatedTo();

        StringAssert.Contains(
            Card(vm, OpenCodeEssentialsViewModel.CardIdRules).DerivedText,
            agents,
            StringComparison.Ordinal);
    }

}
