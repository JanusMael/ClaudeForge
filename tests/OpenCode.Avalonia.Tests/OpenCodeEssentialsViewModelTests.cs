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
/// The Essentials page's nineteen cards: what each reads, what each writes, and what each refuses
/// to write.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The permission cards are the reason half this file exists.</b> <c>permission</c> is
/// <c>anyOf[bare action, per-tool object]</c> and the two arms cannot both be written, so
/// whichever one the file holds, the cards that would destroy the other stand down. Every
/// interlock test asserts both halves — the notice the user sees AND that the write is really
/// suppressed — because the notice alone is cosmetic and the view's <c>IsEnabled</c> alone is
/// markup a refactor can drop.
/// </para>
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

    /// <param name="danger">
    /// The classifier the page's severities come from. Defaulted to <see langword="null"/> so the
    /// tests that are not about severity stay short — but ⚠ <b>a null classifier makes every card
    /// Neutral</b>, so a severity assertion under this default is comparing Neutral against Neutral
    /// and cannot fail. The tests that assert a tier pass a <see cref="RecordingClassifier"/>.
    /// </param>
    private async Task<OpenCodeEssentialsViewModel> BuildAsync(
        string? configJson = null,
        OpenCodeEnvironment? env = null,
        IDangerClassifier? danger = null)
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

        return new OpenCodeEssentialsViewModel(_client, environment, Layout, danger);
    }

    private OpenCodeClient Client
        => _client ?? throw new AssertFailedException("BuildAsync has not run.");

    private static EssentialsCardViewModel Card(OpenCodeEssentialsViewModel vm, string id)
        => vm.GetCardById(id) ?? throw new AssertFailedException($"No card '{id}'.");

    // ── Shape ─────────────────────────────────────────────────────────

    /// <summary>
    /// A classifier that answers <see cref="AppSeverity.Info"/> for every path, and records what it
    /// was asked.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>The sentinel is the whole point.</b> <c>Info</c> is not a tier any hand-written card
    /// would plausibly claim for <c>permission</c> or <c>model</c>, so a card still carrying a
    /// literal shows up as itself rather than blending in. A fake returning the <em>real</em> tiers
    /// would pass identically whether the card consulted it or hardcoded the same answer — the
    /// tautological-fixture shape that let slice 2 ship two surfaces disagreeing about
    /// <c>autoupdate</c>.
    /// </remarks>
    private sealed class RecordingClassifier : IDangerClassifier
    {
        public List<(string Path, IEditorScope? Scope, object? Value)> Calls { get; } = [];

        public DangerAssessment Classify(string path, IEditorScope? scope, object? currentValue)
        {
            Calls.Add((path, scope, currentValue));
            return new DangerAssessment(AppSeverity.Info, IsDangerNow: false, Explanation: null);
        }

        public IReadOnlyCollection<string> ClassifiedPaths => [];
    }

    /// <summary>
    /// Card id → the path whose standing tier that card must show.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Not derivable from the card.</b> Most ids happen to be their path, but three are not:
    /// the plugin card is <c>derived.plugins</c> over the <c>plugin</c> key, the rules card is
    /// <c>derived.rules</c> over <c>instructions</c>, and the five per-tool permission cards carry a
    /// <c>JsonPathFilter</c> of the bare <c>permission</c> key (their deep link targets the page,
    /// not the leaf) while their severity is the leaf's. Reading the path back off the card would
    /// therefore assert the code against itself.
    /// </remarks>
    private static Dictionary<string, string> SeverityPaths { get; } = new(StringComparer.Ordinal)
    {
        [OpenCodeEssentialsViewModel.CardIdGlobalPermission] = "permission",
        [OpenCodeEssentialsViewModel.CardIdBash] = "permission.bash",
        [OpenCodeEssentialsViewModel.CardIdEdit] = "permission.edit",
        [OpenCodeEssentialsViewModel.CardIdExternalDirectory] = "permission.external_directory",
        [OpenCodeEssentialsViewModel.CardIdWebFetch] = "permission.webfetch",
        [OpenCodeEssentialsViewModel.CardIdWebSearch] = "permission.websearch",
        [OpenCodeEssentialsViewModel.CardIdShare] = "share",
        [OpenCodeEssentialsViewModel.CardIdSnapshot] = "snapshot",
        [OpenCodeEssentialsViewModel.CardIdPlugins] = "plugin",
        [OpenCodeEssentialsViewModel.CardIdModel] = "model",
        [OpenCodeEssentialsViewModel.CardIdSmallModel] = "small_model",
        [OpenCodeEssentialsViewModel.CardIdSubagentDepth] = "subagent_depth",
        [OpenCodeEssentialsViewModel.CardIdCompactionAuto] = "compaction.auto",
        [OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines] = "tool_output.max_lines",
        [OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes] = "tool_output.max_bytes",
        [OpenCodeEssentialsViewModel.CardIdAutoupdate] = "autoupdate",
        [OpenCodeEssentialsViewModel.CardIdDefaultAgent] = "default_agent",
        [OpenCodeEssentialsViewModel.CardIdRules] = "instructions",
    };

    [TestMethod]
    public async Task ThePageHoldsTheCuratedCardsInOrder()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        CollectionAssert.AreEqual(
            new[]
            {
                // Access — what the agent may do without asking.
                OpenCodeEssentialsViewModel.CardIdGlobalPermission,
                OpenCodeEssentialsViewModel.CardIdBash,
                OpenCodeEssentialsViewModel.CardIdEdit,
                OpenCodeEssentialsViewModel.CardIdExternalDirectory,
                OpenCodeEssentialsViewModel.CardIdWebFetch,
                OpenCodeEssentialsViewModel.CardIdWebSearch,

                // Privacy and recoverability.
                OpenCodeEssentialsViewModel.CardIdShare,
                OpenCodeEssentialsViewModel.CardIdSnapshot,
                OpenCodeEssentialsViewModel.CardIdPlugins,

                // Cost.
                OpenCodeEssentialsViewModel.CardIdModel,
                OpenCodeEssentialsViewModel.CardIdSmallModel,
                OpenCodeEssentialsViewModel.CardIdSubagentDepth,

                // Quality.
                OpenCodeEssentialsViewModel.CardIdCompactionAuto,
                OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines,
                OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes,

                // Behaviour and diagnostics.
                OpenCodeEssentialsViewModel.CardIdAutoupdate,
                OpenCodeEssentialsViewModel.CardIdDefaultAgent,
                OpenCodeEssentialsViewModel.CardIdRules,
                OpenCodeEssentialsViewModel.CardIdActiveConfig,
            },
            vm.Cards.Select(c => c.Id).ToArray(),
            "The curation is ordered by what a user is looking for — access, then privacy, then "
            + "cost, then quality, then behaviour. Reordering it is a product decision; changing "
            + "this list is how it gets made deliberately.");
    }

    /// <summary>Each card renders the surface its value shape needs.</summary>
    [TestMethod]
    public async Task EachCardCarriesTheKindItsValueShapeNeeds()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Dictionary<string, EssentialsCardKind> expected = new(StringComparer.Ordinal)
        {
            [OpenCodeEssentialsViewModel.CardIdGlobalPermission] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdBash] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdEdit] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdExternalDirectory] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdWebFetch] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdWebSearch] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdShare] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdSnapshot] = EssentialsCardKind.Bool,

            // ⛔ Derived, not StringList — a StringList would read a [name, options] tuple as
            // nothing and write the list back without it. See BuildPluginsCard.
            [OpenCodeEssentialsViewModel.CardIdPlugins] = EssentialsCardKind.Derived,
            [OpenCodeEssentialsViewModel.CardIdModel] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdSmallModel] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdSubagentDepth] = EssentialsCardKind.Int,
            [OpenCodeEssentialsViewModel.CardIdCompactionAuto] = EssentialsCardKind.Bool,
            [OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines] = EssentialsCardKind.Int,
            [OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes] = EssentialsCardKind.Int,
            [OpenCodeEssentialsViewModel.CardIdAutoupdate] = EssentialsCardKind.LabelledEnum,
            [OpenCodeEssentialsViewModel.CardIdDefaultAgent] = EssentialsCardKind.EnumString,
            [OpenCodeEssentialsViewModel.CardIdRules] = EssentialsCardKind.Derived,
            [OpenCodeEssentialsViewModel.CardIdActiveConfig] = EssentialsCardKind.Derived,
        };

        Assert.AreEqual(expected.Count, vm.Cards.Count,
            "A card was added or removed without updating this table, so the new one is unchecked.");

        foreach (EssentialsCardViewModel card in vm.Cards)
        {
            Assert.IsTrue(expected.TryGetValue(card.Id, out EssentialsCardKind want),
                $"Card '{card.Id}' is not in this table.");
            Assert.AreEqual(want, card.Kind, $"Card '{card.Id}' renders the wrong surface.");
        }
    }

    /// <summary>
    /// Every card's standing severity is READ FROM THE CLASSIFIER, not written in the curation.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>Slice 2 shipped the bug this replaces.</b> The severities were literals in
    /// <c>BuildCards</c>, and the <c>autoupdate</c> card said <c>Neutral</c> where
    /// <c>OpenCodeDangerTable</c> says <c>Info</c> — so the Essentials dot and the settings-page dot
    /// disagreed about one setting. The old test asserted the literal, which made it a record of the
    /// defect rather than a guard against it.
    /// <para>
    /// ⚠ The one exception is asserted separately below, not skipped silently.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task EveryCardsSeverityComesFromTheClassifier()
    {
        RecordingClassifier danger = new();
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}", danger: danger);

        List<string> literal = [];
        foreach (EssentialsCardViewModel card in vm.Cards)
        {
            if (!SeverityPaths.ContainsKey(card.Id))
            {
                continue;
            }

            if (card.Severity != AppSeverity.Info)
            {
                literal.Add($"  {card.Id}: {card.Severity}");
            }
        }

        Assert.AreEqual(0, literal.Count,
            $"{literal.Count} card(s) did not take the classifier's answer:\n"
            + string.Join('\n', literal)
            + "\n\nThe classifier answered Info for every path, so any other value is a literal "
            + "in the curation — and a literal is how the Essentials dot and the settings dot "
            + "come to disagree about the same key.");
    }

    /// <summary>Each card asks the classifier about its OWN path.</summary>
    /// <remarks>
    /// The test above proves the answer was used; this proves the question was right. Two cards
    /// reading each other's tier would satisfy the first assertion perfectly.
    /// </remarks>
    [TestMethod]
    public async Task EachCardAsksTheClassifierAboutItsOwnPath()
    {
        RecordingClassifier danger = new();
        _ = await BuildAsync("{}", danger: danger);

        HashSet<string> asked = [.. danger.Calls.Select(c => c.Path)];

        List<string> missing = [.. SeverityPaths.Where(p => !asked.Contains(p.Value))
            .Select(p => $"  {p.Key} → {p.Value}")];

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} card(s) never asked about the path they are supposed to tier:\n"
            + string.Join('\n', missing));
    }

    /// <summary>
    /// The classifier is asked for the BASE tier — null scope, null value — not for an assessment
    /// of what the file currently holds.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The standing dot must not flicker as the user edits.</b> Passing the current value
    /// would make the dot a second danger banner, duplicating <c>IsDangerNow</c> while disagreeing
    /// with it whenever the card's own predicate and the table's differ. The interface contract
    /// guarantees a null scope never RAISES severity, which is what makes the base tier safe to
    /// read as a floor.
    /// </remarks>
    [TestMethod]
    public async Task TheClassifierIsAskedForTheBaseTier_NotForTheCurrentValue()
    {
        RecordingClassifier danger = new();
        _ = await BuildAsync("""{ "permission": "allow", "share": "auto" }""", danger: danger);

        Assert.IsTrue(danger.Calls.Count > 0, "The classifier was never consulted at all.");

        List<string> wrong =
        [
            .. danger.Calls
                .Where(c => c.Scope is not null || c.Value is not null)
                .Select(c => $"  {c.Path}: scope={c.Scope?.ToString() ?? "null"}, value={c.Value ?? "null"}"),
        ];

        Assert.AreEqual(0, wrong.Count,
            $"{wrong.Count} classification(s) passed a scope or a value:\n"
            + string.Join('\n', wrong)
            + "\n\nThe config above sets permission=allow and share=auto, so a card asking about "
            + "the current value would be visible here.");
    }

    /// <summary>
    /// With no client the severities still come from the classifier.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The tiers must not depend on the document opening.</b> Tiering is static data about
    /// which keys matter; a page built without a client is the degraded state, and greying out
    /// every dot there would strip the signal from exactly the user who cannot load their
    /// configuration. This is the invariant behind the window taking its table from the section
    /// list rather than from the opened-section local.
    /// </remarks>
    [TestMethod]
    public void WithNoClient_TheSeveritiesStillComeFromTheClassifier()
    {
        OpenCodeEssentialsViewModel vm = new(null, Env(), Layout, new RecordingClassifier());

        List<string> grey =
        [
            .. vm.Cards
                .Where(c => SeverityPaths.ContainsKey(c.Id) && c.Severity != AppSeverity.Info)
                .Select(c => $"  {c.Id}: {c.Severity}"),
        ];

        Assert.AreEqual(0, grey.Count,
            $"{grey.Count} card(s) lost their tier because there was no client:\n"
            + string.Join('\n', grey));
    }

    /// <summary>
    /// With no classifier every card is Neutral — the honest tier for a host with no table.
    /// </summary>
    [TestMethod]
    public void WithNoClassifier_EveryCardIsNeutral()
    {
        OpenCodeEssentialsViewModel vm = new(null, Env(), Layout);

        foreach (EssentialsCardViewModel card in vm.Cards)
        {
            Assert.AreEqual(AppSeverity.Neutral, card.Severity,
                $"Card '{card.Id}' claimed a tier with no table to claim it from.");
        }
    }

    /// <summary>
    /// The active-config card is Neutral even WITH a classifier, and that is deliberate.
    /// </summary>
    /// <remarks>
    /// No JSON key holds "which file is this app editing", so the danger table has nothing to say
    /// about it. Asserted rather than merely excluded from the scan above, so the exception stays a
    /// decision instead of becoming a hole.
    /// </remarks>
    [TestMethod]
    public async Task TheActiveConfigCardIsNeutralByDesign()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}", danger: new RecordingClassifier());

        Assert.AreEqual(AppSeverity.Neutral,
            Card(vm, OpenCodeEssentialsViewModel.CardIdActiveConfig).Severity,
            "A report over no key must not borrow a tier — the classifier answered Info for "
            + "everything, so this card taking that answer would mean it asked about some key.");
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


    // ── permission: the interlock ─────────────────────────────────────
    //
    // ⛔⛔ `permission` is anyOf[bare action, per-tool object], and the two arms cannot both be
    // written. Whichever one the file holds, the cards that would destroy it stand down. Every
    // test below asserts BOTH halves — the notice the user sees, and that the write really is
    // suppressed — because the notice alone is cosmetic and IsEnabled alone is markup.

    /// <summary>A bare global action is shown on the global card and disables the per-tool ones.</summary>
    [TestMethod]
    public async Task BarePermission_ShowsOnTheGlobalCard()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "permission": "allow" }""");
        EssentialsCardViewModel global = Card(vm, OpenCodeEssentialsViewModel.CardIdGlobalPermission);

        Assert.AreEqual("allow", global.EnumValue);
        Assert.IsFalse(global.EnumDisabled);
        Assert.IsFalse(global.ShowConstraintNotice);
        Assert.IsTrue(global.IsDanger, "A bare \"allow\" auto-approves every tool.");
    }

    /// <summary>
    /// ⛔⛔ With a bare global action set, a per-tool card must not write — doing so would replace
    /// the string with an object and delete the rule covering every OTHER tool.
    /// </summary>
    [TestMethod]
    [DataRow(OpenCodeEssentialsViewModel.CardIdBash)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdEdit)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdExternalDirectory)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdWebFetch)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdWebSearch)]
    public async Task BarePermission_StandsTheToolCardsDown_AndTheyCannotWrite(string cardId)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "permission": "deny" }""");
        EssentialsCardViewModel tool = Card(vm, cardId);

        Assert.IsNull(tool.EnumValue,
            "There is no per-tool value to show — the global rule is what is in force.");
        Assert.IsTrue(tool.EnumDisabled);
        Assert.IsTrue(tool.ShowConstraintNotice);
        Assert.AreEqual(Strings.EssentialsPermissionGlobalInForce, tool.ConstraintNoticeText);

        // Drive the value anyway: IsEnabled is markup, and this is the guard underneath it.
        tool.EnumValue = "allow";

        JsonNode? held = Client.GetEffective<JsonNode>("permission");
        Assert.IsNotNull(held);
        Assert.AreEqual("deny", held.GetValue<string>(),
            "Writing a per-tool action replaced the bare global rule, which silently removes the "
            + "protection covering every tool without a card.");
        Assert.IsFalse(Client.HasUnsavedChanges);
    }

    /// <summary>Per-tool rules are shown on their own cards.</summary>
    [TestMethod]
    public async Task PerToolPermission_ShowsOnTheToolCard()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "permission": { "bash": "allow", "edit": "ask" } }""");

        EssentialsCardViewModel bash = Card(vm, OpenCodeEssentialsViewModel.CardIdBash);
        Assert.AreEqual("allow", bash.EnumValue);
        Assert.IsFalse(bash.EnumDisabled);
        Assert.IsTrue(bash.IsDanger);

        EssentialsCardViewModel edit = Card(vm, OpenCodeEssentialsViewModel.CardIdEdit);
        Assert.AreEqual("ask", edit.EnumValue);
        Assert.IsFalse(edit.IsDanger);

        // A tool with no entry is unset, not disabled — it is safe to write one.
        EssentialsCardViewModel fetch = Card(vm, OpenCodeEssentialsViewModel.CardIdWebFetch);
        Assert.IsNull(fetch.EnumValue);
        Assert.IsFalse(fetch.EnumDisabled);
    }

    /// <summary>
    /// ⛔⛔ With per-tool rules present, the GLOBAL card must not write — a bare action would
    /// replace the object and delete every per-tool rule in it.
    /// </summary>
    [TestMethod]
    public async Task PerToolPermission_StandsTheGlobalCardDown_AndItCannotWrite()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "permission": { "bash": "ask" } }""");
        EssentialsCardViewModel global = Card(vm, OpenCodeEssentialsViewModel.CardIdGlobalPermission);

        Assert.IsNull(global.EnumValue, "There is no single global action when tools are configured.");
        Assert.IsTrue(global.EnumDisabled);
        Assert.IsTrue(global.ShowConstraintNotice);
        Assert.AreEqual(Strings.EssentialsPermissionPerToolConfigured, global.ConstraintNoticeText);

        global.EnumValue = "allow";

        JsonNode? held = Client.GetEffective<JsonNode>("permission");
        Assert.IsInstanceOfType<JsonObject>(held,
            "The per-tool object was replaced by a bare action, deleting every rule in it.");
        Assert.AreEqual("ask", held!["bash"]!.GetValue<string>());
        Assert.IsFalse(Client.HasUnsavedChanges);
    }

    /// <summary>
    /// A tool holding ordered per-pattern rules stands down: a single action would discard them,
    /// and their ORDER is semantics (last match wins).
    /// </summary>
    [TestMethod]
    public async Task PatternRules_StandTheToolCardDown_AndItCannotWrite()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "permission": { "bash": { "git *": "allow", "*": "ask" } } }""");
        EssentialsCardViewModel bash = Card(vm, OpenCodeEssentialsViewModel.CardIdBash);

        Assert.IsNull(bash.EnumValue, "A rule list is not a single action and must not be shown as one.");
        Assert.IsTrue(bash.EnumDisabled);
        Assert.IsTrue(bash.ShowConstraintNotice);
        Assert.AreEqual(Strings.EssentialsPermissionPatternRules, bash.ConstraintNoticeText);

        bash.EnumValue = "allow";

        JsonNode? held = Client.GetEffective<JsonNode>("permission.bash");
        Assert.IsInstanceOfType<JsonObject>(held,
            "The ordered rule list was replaced by a bare action — unrecoverable from the UI, and "
            + "the order that decided which rule won is gone with it.");
        Assert.IsFalse(Client.HasUnsavedChanges);
    }

    /// <summary>
    /// A value in a shape OpenCode itself would reject is reported, not edited.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>OpenCodePermissionModel.Parse</c> THROWS on an invalid shape rather than dropping the
    /// offending entry, deliberately — a permission rule that silently disappears is one the user
    /// believes is protecting them. This asserts the page catches that instead of letting it escape
    /// a fire-and-forget read in the constructor.
    /// </remarks>
    [TestMethod]
    [DataRow(OpenCodeEssentialsViewModel.CardIdGlobalPermission)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdBash)]
    public async Task AnUnreadablePermissionValue_IsReportedNotEdited(string cardId)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "permission": 42 }""");
        EssentialsCardViewModel card = Card(vm, cardId);

        Assert.IsNull(card.EnumValue);
        Assert.IsTrue(card.EnumDisabled);
        Assert.AreEqual(Strings.EssentialsPermissionUnreadable, card.ConstraintNoticeText);
    }

    /// <summary>With nothing set, every permission card is editable and blank.</summary>
    [TestMethod]
    public async Task WithNoPermissionSet_EveryPermissionCardIsWritable()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        foreach (string id in new[]
        {
            OpenCodeEssentialsViewModel.CardIdGlobalPermission,
            OpenCodeEssentialsViewModel.CardIdBash,
            OpenCodeEssentialsViewModel.CardIdEdit,
            OpenCodeEssentialsViewModel.CardIdExternalDirectory,
            OpenCodeEssentialsViewModel.CardIdWebFetch,
            OpenCodeEssentialsViewModel.CardIdWebSearch,
        })
        {
            EssentialsCardViewModel card = Card(vm, id);
            Assert.IsNull(card.EnumValue, $"'{id}' showed a value with nothing set.");
            Assert.IsFalse(card.EnumDisabled, $"'{id}' stood down with nothing to protect.");
            Assert.IsFalse(card.ShowConstraintNotice, $"'{id}' explained a constraint that is absent.");
        }
    }

    /// <summary>A per-tool action written into an empty document lands at its own path.</summary>
    [TestMethod]
    public async Task AToolPermissionCard_WritesItsOwnKey()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Card(vm, OpenCodeEssentialsViewModel.CardIdBash).EnumValue = "deny";

        JsonNode? held = Client.GetEffective<JsonNode>("permission.bash");
        Assert.IsNotNull(held);
        Assert.AreEqual("deny", held.GetValue<string>());
    }

    /// <summary>The three actions offered are the schema's, in the safe-first order.</summary>
    [TestMethod]
    public async Task PermissionCardsOfferTheSchemasThreeActions()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        CollectionAssert.AreEqual(
            new[] { "deny", "ask", "allow" },
            Card(vm, OpenCodeEssentialsViewModel.CardIdBash).EnumOptions.ToArray());

        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdBash).AllowsFreeForm,
            "PermissionActionConfig is a closed enum — a typed value would be rejected at load.");
    }

    // ── Bool cards ────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("""{ "snapshot": true }""", true)]
    [DataRow("""{ "snapshot": false }""", false)]
    [DataRow("{}", null)]
    public async Task SnapshotCard_ReadsTheTriState(string json, bool? expected)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);

        Assert.AreEqual(expected, Card(vm, OpenCodeEssentialsViewModel.CardIdSnapshot).BoolValue);
    }

    /// <summary>
    /// The danger is <c>false</c> specifically, not "not true".
    /// </summary>
    /// <remarks>
    /// Absent means the documented default (<c>true</c>) applies, which is the safe state. Treating
    /// unset as dangerous would raise a banner on nearly every configuration and teach users to
    /// ignore it.
    /// </remarks>
    [TestMethod]
    [DataRow("""{ "snapshot": false }""", true)]
    [DataRow("""{ "snapshot": true }""", false)]
    [DataRow("{}", false)]
    public async Task SnapshotCard_WarnsOnlyWhenExplicitlyOff(string json, bool warns)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);

        Assert.AreEqual(warns, Card(vm, OpenCodeEssentialsViewModel.CardIdSnapshot).IsDanger);
    }

    [TestMethod]
    public async Task ABoolCard_WritesAndUnsets()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdSnapshot);

        card.BoolValue = false;
        Assert.IsFalse(Client.GetEffective<JsonNode>("snapshot")!.GetValue<bool>());

        // Null is the tri-state's "inherit": remove the key rather than writing a literal.
        card.BoolValue = null;
        Assert.IsNull(Client.GetEffective<JsonNode>("snapshot"));
    }

    /// <summary>A nested bool card reaches its dotted path.</summary>
    [TestMethod]
    public async Task TheCompactionCard_ReadsAndWritesTheNestedKey()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "compaction": { "auto": false } }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdCompactionAuto);

        Assert.IsFalse(card.BoolValue);
        Assert.IsTrue(card.IsDanger);

        card.BoolValue = true;
        Assert.IsTrue(Client.GetEffective<JsonNode>("compaction.auto")!.GetValue<bool>());
    }

    // ── Int cards ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheSubagentDepthCard_ReadsAndWrites()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "subagent_depth": 3 }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdSubagentDepth);

        Assert.AreEqual(3, card.IntValue);

        card.IntValue = 1;
        Assert.AreEqual(1, Client.GetEffective<JsonNode>("subagent_depth")!.GetValue<int>());

        card.IntValue = null;
        Assert.IsNull(Client.GetEffective<JsonNode>("subagent_depth"));
    }

    /// <summary>
    /// The depth banner fires above the documented default, not at it.
    /// </summary>
    /// <remarks>
    /// The schema's default is <b>1</b> — "prevents subagents from launching subagents" — so the
    /// threshold of &gt; 2 is well clear of it. This is a cost multiplier, not a boundary.
    /// </remarks>
    [TestMethod]
    [DataRow("""{ "subagent_depth": 1 }""", false)]
    [DataRow("""{ "subagent_depth": 2 }""", false)]
    [DataRow("""{ "subagent_depth": 3 }""", true)]
    [DataRow("{}", false)]
    public async Task TheSubagentDepthCard_WarnsOnlyAboveTwo(string json, bool warns)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);

        Assert.AreEqual(warns, Card(vm, OpenCodeEssentialsViewModel.CardIdSubagentDepth).IsDanger);
    }

    [TestMethod]
    public async Task TheToolOutputCards_ReadTheirOwnNestedKeys()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "tool_output": { "max_lines": 500, "max_bytes": 4096 } }""");

        Assert.AreEqual(500, Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines).IntValue);
        Assert.AreEqual(4096, Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes).IntValue);
    }

    /// <summary>
    /// The two truncation limits refuse to offer 0, which the schema rejects.
    /// </summary>
    /// <remarks>
    /// ⛔ <c>exclusiveMinimum: 0</c> in the schema, and a config OpenCode rejects at load bricks
    /// every command rather than falling back. <c>subagent_depth</c> is contrasted deliberately:
    /// its minimum really is 0, so a single shared bound would be wrong for one of them either way.
    /// </remarks>
    [TestMethod]
    public async Task TheIntCardsCarryTheirOwnSchemaBounds()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.AreEqual(1m, Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines).IntMinimum,
            "tool_output.max_lines is exclusiveMinimum 0, so the smallest legal value is 1.");
        Assert.AreEqual(1m, Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes).IntMinimum);
        Assert.AreEqual(0m, Card(vm, OpenCodeEssentialsViewModel.CardIdSubagentDepth).IntMinimum,
            "subagent_depth's minimum really is 0 — \"no subagents at all\".");

        Assert.AreNotEqual(
            Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxLines).IntIncrement,
            Card(vm, OpenCodeEssentialsViewModel.CardIdToolOutputMaxBytes).IntIncrement,
            "A line count and a byte count live on different scales; one shared step leaves one "
            + "of them unusable by arrow.");
    }

    // ── model / small_model ───────────────────────────────────────────

    [TestMethod]
    public async Task TheModelCard_ReadsAndWritesFreeForm()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "model": "anthropic/claude-x" }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdModel);

        Assert.AreEqual("anthropic/claude-x", card.EnumValue);
        Assert.IsTrue(card.AllowsFreeForm);
        Assert.AreEqual(0, card.EnumOptions.Count,
            "A hardcoded subset of models would read as the complete set. The real one comes from "
            + "the configured providers, which is the plan's own \"Providers and models\" work.");

        card.EnumValue = "openai/gpt-x";
        Assert.AreEqual("openai/gpt-x", Client.GetEffective<JsonNode>("model")!.GetValue<string>());
    }

    /// <summary>Blank means unset, never a literal empty string.</summary>
    [TestMethod]
    public async Task ClearingTheModelCard_RemovesTheKey()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "model": "anthropic/claude-x" }""");

        Card(vm, OpenCodeEssentialsViewModel.CardIdModel).EnumValue = "   ";

        Assert.IsNull(Client.GetEffective<JsonNode>("model"),
            "A cleared free-form box must remove the key, not pin model=\"\".");
    }

    /// <summary>
    /// A model whose provider this configuration switched off is flagged — nothing else on the
    /// page would say so, and the model simply will not load.
    /// </summary>
    [TestMethod]
    [DataRow("""{ "model": "openai/gpt-x", "disabled_providers": ["openai"] }""", true)]
    [DataRow("""{ "model": "openai/gpt-x", "enabled_providers": ["anthropic"] }""", true)]
    [DataRow("""{ "model": "openai/gpt-x", "enabled_providers": ["openai"] }""", false)]
    [DataRow("""{ "model": "openai/gpt-x", "enabled_providers": [] }""", false)]
    [DataRow("""{ "model": "openai/gpt-x" }""", false)]
    [DataRow("""{ "model": "bare-name", "disabled_providers": ["openai"] }""", false)]
    public async Task TheModelCard_FlagsADisabledProvider(string json, bool warns)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);

        Assert.AreEqual(warns, Card(vm, OpenCodeEssentialsViewModel.CardIdModel).IsDanger);
    }

    /// <summary>The small-model card runs the same provider check on its own value.</summary>
    [TestMethod]
    public async Task TheSmallModelCard_FlagsItsOwnDisabledProvider()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "model": "anthropic/a", "small_model": "openai/b", "disabled_providers": ["openai"] }""");

        Assert.IsFalse(Card(vm, OpenCodeEssentialsViewModel.CardIdModel).IsDanger);
        Assert.IsTrue(Card(vm, OpenCodeEssentialsViewModel.CardIdSmallModel).IsDanger);
    }

    // ── share ─────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("""{ "share": "auto" }""", true)]
    [DataRow("""{ "share": "manual" }""", false)]
    [DataRow("""{ "share": "disabled" }""", false)]
    [DataRow("{}", false)]
    public async Task TheShareCard_WarnsOnlyOnAuto(string json, bool warns)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(json);

        Assert.AreEqual(warns, Card(vm, OpenCodeEssentialsViewModel.CardIdShare).IsDanger);
    }

    [TestMethod]
    public async Task TheShareCard_OffersTheSchemasEnumAndIsClosed()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdShare);

        CollectionAssert.AreEqual(new[] { "manual", "auto", "disabled" }, card.EnumOptions.ToArray());
        Assert.IsFalse(card.AllowsFreeForm);
    }

    // ── default_agent ─────────────────────────────────────────────────

    /// <summary>
    /// Only the two agents MEASURED as offerable are suggested.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ Measured against the v1.17.9 binary, not read off the schema, which names seven under
    /// <c>agent</c>: <c>general</c> and <c>explore</c> are subagents, <c>summary</c> and
    /// <c>compaction</c> are hidden, and <c>title</c> does not exist in the binary at all.
    /// <c>default_agent</c> must name a PRIMARY agent, so suggesting any of those five would
    /// recommend a value OpenCode rejects.
    /// </remarks>
    [TestMethod]
    public async Task TheDefaultAgentCard_SuggestsOnlyTheOfferableBuiltIns()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdDefaultAgent);

        CollectionAssert.AreEqual(new[] { "build", "plan" }, card.EnumOptions.ToArray());
        Assert.IsTrue(card.AllowsFreeForm,
            "A user may define their own primary agent, so a closed list would reject a valid value.");
    }

    /// <summary>A user's own agents are suggested too; the non-offerable built-ins never are.</summary>
    [TestMethod]
    public async Task TheDefaultAgentCard_AddsTheUsersOwnAgents_ButNotTheHiddenBuiltIns()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """
            {
              "agent": {
                "reviewer": { "description": "d" },
                "general":  { "description": "d" },
                "title":    { "description": "d" }
              }
            }
            """);

        IReadOnlyList<string> options =
            Card(vm, OpenCodeEssentialsViewModel.CardIdDefaultAgent).EnumOptions;

        CollectionAssert.Contains(options.ToArray(), "reviewer");
        CollectionAssert.DoesNotContain(options.ToArray(), "general",
            "\"general\" is a subagent — default_agent would reject it.");
        CollectionAssert.DoesNotContain(options.ToArray(), "title",
            "\"title\" is not in the binary at all.");
    }

    // ── plugin: the tuple ─────────────────────────────────────────────

    [TestMethod]
    public async Task ThePluginCard_ReportsNoPlugins_WhenNoneAreConfigured()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");

        Assert.AreEqual(Strings.EssentialsPluginsNone,
            Card(vm, OpenCodeEssentialsViewModel.CardIdPlugins).DerivedText);
    }

    /// <summary>
    /// ⛔⛔ A tuple entry keeps its name AND is marked as carrying options.
    /// </summary>
    /// <remarks>
    /// This is the assertion that justifies the card being read-only. The schema types each element
    /// as <c>anyOf[string, [string, object]]</c>; a <c>StringList</c> card would read the tuple as
    /// nothing and write the list back without it, silently deleting a configured plugin's options.
    /// </remarks>
    [TestMethod]
    public async Task ThePluginCard_NamesATupleEntryAndItsOptions()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "plugin": ["plain-one", ["configured-one", { "token": "x" }]] }""");
        string text = Card(vm, OpenCodeEssentialsViewModel.CardIdPlugins).DerivedText;

        StringAssert.Contains(text, "plain-one", StringComparison.Ordinal);
        StringAssert.Contains(text, "configured-one", StringComparison.Ordinal);
        StringAssert.Contains(
            text,
            Format(Strings.EssentialsPluginsWithOptionsFmt, "configured-one"),
            StringComparison.Ordinal);
    }

    /// <summary>An entry matching neither arm is named as unreadable rather than skipped.</summary>
    /// <remarks>
    /// A plugin missing from a list headed "loaded into the agent process" is the one a user most
    /// needs to see.
    /// </remarks>
    [TestMethod]
    public async Task ThePluginCard_NamesAnUnreadableEntryRatherThanDroppingIt()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "plugin": ["ok", 42] }""");
        string text = Card(vm, OpenCodeEssentialsViewModel.CardIdPlugins).DerivedText;

        StringAssert.Contains(text, Strings.EssentialsPluginsUnreadableEntry, StringComparison.Ordinal);
        StringAssert.Contains(text, "ok", StringComparison.Ordinal);
    }

    /// <summary>The plugin card is read-only, so it can never write the list back.</summary>
    [TestMethod]
    public async Task ThePluginCard_HasNoWriter()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync(
            """{ "plugin": [["configured-one", { "token": "x" }]] }""");
        EssentialsCardViewModel card = Card(vm, OpenCodeEssentialsViewModel.CardIdPlugins);

        Assert.AreEqual(EssentialsCardKind.Derived, card.Kind);

        // Derived cards complete silently rather than throwing — the constructor has already
        // rejected the only way a writer could be attached.
        await card.WriteAsync();

        Assert.IsFalse(Client.HasUnsavedChanges,
            "The plugin inventory is a report. Writing through it would drop the options tuple.");
    }

    private static string Format(string format, params object?[] args)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// ⛔⛔ Writing the global action stands the tool cards down <b>immediately</b>, not on the
    /// next visit to the page.
    /// </summary>
    /// <remarks>
    /// <b>Found by re-reading the finished slice, not by a test.</b> The interlock is computed in
    /// each card's <c>ReadAsync</c>, and a card's write does not re-read its siblings — cards are
    /// refreshed on construction and on arrival. So the whole guard was defeatable inside one
    /// visit: set the global card to an action, and the five tool cards were still enabled with
    /// their <c>EnumDisabled</c> from before the write. Editing one then replaced the bare string
    /// with an object and deleted the global rule covering every tool without a card — the exact
    /// data loss the interlock exists to prevent, reached by two ordinary clicks.
    /// </remarks>
    [TestMethod]
    [DataRow(OpenCodeEssentialsViewModel.CardIdBash)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdEdit)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdExternalDirectory)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdWebFetch)]
    [DataRow(OpenCodeEssentialsViewModel.CardIdWebSearch)]
    public async Task WritingTheGlobalAction_StandsTheToolCardsDownAtOnce(string toolCardId)
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel global = Card(vm, OpenCodeEssentialsViewModel.CardIdGlobalPermission);
        EssentialsCardViewModel tool = Card(vm, toolCardId);

        Assert.IsFalse(tool.EnumDisabled,
            "Premise: with no permission set, the tool card must start out writable.");

        global.EnumValue = "deny";

        Assert.IsTrue(tool.EnumDisabled,
            "The tool card is still enabled after a bare global rule was written. Editing it now "
            + "replaces that string with an object and deletes the rule for every other tool.");
        Assert.AreEqual(Strings.EssentialsPermissionGlobalInForce, tool.ConstraintNoticeText);

        // And the guard underneath the markup, in the same visit.
        tool.EnumValue = "allow";

        JsonNode? held = Client.GetEffective<JsonNode>("permission");
        Assert.IsNotNull(held);
        Assert.AreEqual("deny", held.GetValue<string>(),
            "The bare global rule was destroyed by a per-tool write in the same visit.");
    }

    /// <summary>The mirror: writing a tool action stands the global card down at once.</summary>
    [TestMethod]
    public async Task WritingAToolAction_StandsTheGlobalCardDownAtOnce()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("{}");
        EssentialsCardViewModel global = Card(vm, OpenCodeEssentialsViewModel.CardIdGlobalPermission);
        EssentialsCardViewModel bash = Card(vm, OpenCodeEssentialsViewModel.CardIdBash);

        Assert.IsFalse(global.EnumDisabled, "Premise: the global card must start out writable.");

        bash.EnumValue = "ask";

        Assert.IsTrue(global.EnumDisabled,
            "The global card is still enabled after a per-tool rule was written. Editing it now "
            + "replaces the object and deletes every per-tool rule in it.");
        Assert.AreEqual(Strings.EssentialsPermissionPerToolConfigured, global.ConstraintNoticeText);

        global.EnumValue = "allow";

        JsonNode? held = Client.GetEffective<JsonNode>("permission");
        Assert.IsInstanceOfType<JsonObject>(held,
            "The per-tool object was replaced by a bare action in the same visit.");
        Assert.AreEqual("ask", held!["bash"]!.GetValue<string>());
    }

    /// <summary>
    /// Clearing the last rule stands the cards back UP, so the interlock is not a one-way latch.
    /// </summary>
    /// <remarks>
    /// The refresh must run on removal as well as on write. Without this the page would be
    /// permanently read-only after the first edit, which is a different bug of the same shape and
    /// would have looked like "the interlock works".
    /// </remarks>
    [TestMethod]
    public async Task RemovingTheGlobalAction_StandsTheToolCardsBackUp()
    {
        OpenCodeEssentialsViewModel vm = await BuildAsync("""{ "permission": "deny" }""");
        EssentialsCardViewModel global = Card(vm, OpenCodeEssentialsViewModel.CardIdGlobalPermission);
        EssentialsCardViewModel bash = Card(vm, OpenCodeEssentialsViewModel.CardIdBash);

        Assert.IsTrue(bash.EnumDisabled, "Premise: the global rule must start out in force.");

        // Blank means unset, which removes the key.
        global.EnumValue = string.Empty;

        Assert.IsNull(Client.GetEffective<JsonNode>("permission"));
        Assert.IsFalse(bash.EnumDisabled,
            "With the global rule gone there is nothing left to protect, so the tool cards must "
            + "become writable again rather than staying latched off.");
        Assert.IsFalse(bash.ShowConstraintNotice);
    }
}
