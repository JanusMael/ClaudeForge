using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Essentials;

/// <summary>
/// OpenCode's Essentials page: the curated cards for the settings that most change what the agent
/// is allowed to do, what it costs, and which file it reads.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Composition, not machinery.</b> The card, its danger banner, its deep link and its
/// value plumbing are all <see cref="EssentialsCardViewModel"/>'s, in the shell. What lives here
/// is the product half the plan names: <em>which</em> knobs deserve a card, and how to describe
/// them.
/// </para>
/// <para>
/// ⚠ <b>Three of the plan's seventeen, deliberately.</b> These are exactly the cards that exercise
/// the two new card kinds — the labelled union (<c>autoupdate</c>) and the two derived, read-only
/// resolver reports. The remaining fourteen all reuse kinds that already shipped, so they are
/// additive to a page whose machinery has been seen working.
/// </para>
/// </remarks>
public sealed partial class OpenCodeEssentialsViewModel : ObservableObject, INavigablePage
{
    /// <summary>Card id for the <c>autoupdate</c> union card.</summary>
    public const string CardIdAutoupdate = "autoupdate";

    /// <summary>Card id for the derived "rules in effect" report.</summary>
    /// <remarks>
    /// Not a JSON path — no key holds this. The ids are stable handles for deep links and tests,
    /// and this project has already been bitten once by a guard that assumed a card id WAS its
    /// path.
    /// </remarks>
    public const string CardIdRules = "derived.rules";

    /// <summary>Card id for the derived "active config file" report.</summary>
    public const string CardIdActiveConfig = "derived.activeConfig";

    /// <summary>The <c>autoupdate</c> key, as it appears at the document root.</summary>
    internal const string AutoupdatePath = "autoupdate";

    private readonly AgentConfigClientCore? _client;
    private readonly OpenCodeEnvironment _environment;
    private readonly SchemaPageLayout _layout;

    /// <summary>Construct the page.</summary>
    /// <param name="client">
    /// The opened config client, or <see langword="null"/> when the section failed to load — the
    /// derived cards still answer, because they read the environment and the filesystem rather
    /// than the document. That is deliberate: a user whose config is too broken to open is exactly
    /// the user who needs to be told which file the app is trying to open.
    /// </param>
    /// <param name="environment">The four <c>OPENCODE_*</c> variables, captured as a value.</param>
    /// <param name="layout">
    /// This product's key→page table, used to label the "View in …" deep link.
    /// <para>
    /// ⭐ Passed in rather than a literal <c>"General"</c> here. The page a key lands on is the
    /// layout's decision, and a copy of it in this file would keep pointing at the old page after
    /// the table moved the key — with a button that silently navigates nowhere.
    /// </para>
    /// </param>
    public OpenCodeEssentialsViewModel(
        AgentConfigClientCore? client,
        OpenCodeEnvironment environment,
        SchemaPageLayout layout)
    {
        _client = client;
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));

        Cards = new ObservableCollection<EssentialsCardViewModel>(BuildCards());

        // Initial read. Every delegate here is synchronous, so this completes before the
        // constructor returns and the first render already shows real values.
        _ = RefreshAsync();
    }

    /// <summary>The curated cards, in display order.</summary>
    public ObservableCollection<EssentialsCardViewModel> Cards { get; }

    /// <summary>The page heading.</summary>
    public string Title => Strings.EssentialsPageTitle;

    /// <summary>One sentence saying what the page is for.</summary>
    public string Subtitle => Strings.EssentialsPageSubtitle;

    /// <summary>Re-read every card.</summary>
    public Task RefreshAsync() => Task.WhenAll(Cards.Select(c => c.ReadAsync()));

    /// <summary>
    /// Re-read on arrival, because two of these three cards report the filesystem rather than the
    /// document.
    /// </summary>
    /// <remarks>
    /// A global <c>AGENTS.md</c> can appear while the app is open — the user creating one is a
    /// likely consequence of having just read this card. The environment variables cannot change
    /// within the process, but re-reading them costs nothing and keeps one rule instead of two.
    /// </remarks>
    public void OnNavigatedTo() => _ = RefreshAsync();

    /// <summary>Look up a card by its id.</summary>
    public EssentialsCardViewModel? GetCardById(string id)
        => Cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>The page title a top-level key's editor lives on.</summary>
    private string PageTitleFor(string key)
        => _layout.PropertyToPage.TryGetValue(key, out string? page) ? page : _layout.FallbackPage;

    // ── Curation ──────────────────────────────────────────────────────

    private IEnumerable<EssentialsCardViewModel> BuildCards()
    {
        yield return BuildAutoupdateCard();
        yield return BuildRulesCard();
        yield return BuildActiveConfigCard();
    }

    /// <summary>
    /// Card 14 — <c>autoupdate</c>: <c>true</c> | <c>false</c> | <c>"notify"</c>, plus absent.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The option labels are the full editor's own strings</b>, not a second set written for
    /// this surface. Two pickers for one key that disagree about what its values are called is a
    /// drift this repo has paid for elsewhere; sharing the resource makes agreement structural.
    /// The same argument applies to the ordering, which runs least-to-most automatic here because
    /// that is the axis the user moves along.
    /// </remarks>
    private EssentialsCardViewModel BuildAutoupdateCard() => new(new EssentialsCardOptions
    {
        Id = CardIdAutoupdate,
        Title = Strings.EssentialsCardAutoupdateTitle,
        Body = Strings.EssentialsCardAutoupdateBody,

        // Behaviour, not a hazard: no value of this key weakens a safety boundary, so a coloured
        // dot here would spend the user's attention where nothing is wrong. The plan's own tier.
        Severity = AppSeverity.Neutral,
        Kind = EssentialsCardKind.LabelledEnum,
        LabelledOptions =
        [
            new(nameof(OpenCodeAutoupdateMode.NotSet), Strings.AutoupdateNotSet, Strings.AutoupdateNotSetHelp),
            new(nameof(OpenCodeAutoupdateMode.Disabled), Strings.AutoupdateDisabled, Strings.AutoupdateDisabledHelp),
            new(nameof(OpenCodeAutoupdateMode.Notify), Strings.AutoupdateNotify, Strings.AutoupdateNotifyHelp),
            new(nameof(OpenCodeAutoupdateMode.Automatic), Strings.AutoupdateAutomatic, Strings.AutoupdateAutomaticHelp),
        ],
        ReadAsync = ReadAutoupdate,
        WriteAsync = WriteAutoupdate,
        ViewInGroupTitle = PageTitleFor(AutoupdatePath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = AutoupdatePath,
    });

    /// <summary>
    /// Card 16 — which global <c>AGENTS.md</c> is in force, and the one case where that answer may
    /// be a lie.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠⚠ <b>Global rules only.</b> The plan's two example messages are both about the global
    /// file, and project rules cannot be resolved without a project directory — which the
    /// artifacts page asks for and pointedly refuses to guess, because a GUI launched from a
    /// shortcut has an arbitrary working directory. A card that quietly used it would describe a
    /// project the user is not in.
    /// </para>
    /// <para>
    /// ⚠ <b>The caution is SOURCED, not measured, and is worded to say so.</b> OpenCode's issue
    /// tracker reports that the global-files loop stops after the first hit, so an
    /// <c>AGENTS.md</c> under <c>$OPENCODE_CONFIG_DIR</c> is ignored when the default directory
    /// also has one. The probes this repo has cannot confirm it: <c>debug config</c> does not
    /// carry rules and <c>debug agent</c> does not include their text — both tried, neither
    /// showed a marker planted in either file. So the card reports the observable precondition
    /// (both files exist, the directory is redirected) and says OpenCode <em>may</em> be ignoring
    /// one. Same treatment, for the same reason, as
    /// <c>OpenCodeArtifactIssue.SkillHasNoDescription</c>.
    /// </para>
    /// </remarks>
    private EssentialsCardViewModel BuildRulesCard() => new(new EssentialsCardOptions
    {
        Id = CardIdRules,
        Title = Strings.EssentialsCardRulesTitle,
        Body = Strings.EssentialsCardRulesBody,
        Severity = AppSeverity.Neutral,
        Kind = EssentialsCardKind.Derived,
        ReadAsync = ReadRules,
        IsDangerPredicate = _ => HasShadowedGlobalRules(),
        DangerBannerText = Strings.EssentialsRulesShadowedBanner,
    });

    /// <summary>
    /// Card 17 — which configuration file the app is actually editing.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The card exists because OpenCode's own tooling answers this wrongly.</b>
    /// <c>opencode debug paths</c> prints <c>~/.config/opencode</c> even when
    /// <c>$OPENCODE_CONFIG_DIR</c> points elsewhere — measured against v1.17.9 — while
    /// <c>debug config</c> proves the redirected file is the one that loads. A user checking the
    /// obvious command gets the wrong answer, which is exactly the confusion this card defuses.
    /// </remarks>
    private EssentialsCardViewModel BuildActiveConfigCard() => new(new EssentialsCardOptions
    {
        Id = CardIdActiveConfig,
        Title = Strings.EssentialsCardActiveConfigTitle,
        Body = Strings.EssentialsCardActiveConfigBody,
        Severity = AppSeverity.Neutral,
        Kind = EssentialsCardKind.Derived,
        ReadAsync = ReadActiveConfig,

        // Inline content is the one state where editing a file changes nothing the agent reads.
        IsDangerPredicate = _ => _environment.InlineContent is not null,
        DangerBannerText = Strings.EssentialsActiveConfigInlineBanner,
    });

    // ── Read / write delegates ────────────────────────────────────────

    /// <remarks>
    /// ⚠ <c>IsLoading</c> must not span an <c>await</c> — a suppression flag held across a
    /// continuation lets a user edit land while writes are still suppressed, and the edit is
    /// silently discarded. Every delegate here is synchronous for that reason, and the guard test
    /// this plan re-asserts each phase (<c>…_NotSuppressed_WhileReadIsInAsyncPhase</c>) is what
    /// keeps it that way.
    /// </remarks>
    private Task ReadAutoupdate(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            OpenCodeAutoupdateConfig config = ReadAutoupdateConfig();

            // Unrecognised is a state a value ARRIVES in, never one a user picks — so it is
            // absent from the options, and the picker is disabled beside a notice rather than
            // silently sitting on some arm that would overwrite the held value on the next edit.
            bool unrecognised = config.Mode == OpenCodeAutoupdateMode.Unrecognised;
            card.EnumDisabled = unrecognised;
            card.ShowConstraintNotice = unrecognised;
            card.ConstraintNoticeText = unrecognised ? Strings.AutoupdateUnrecognisedBanner : string.Empty;

            card.SelectedOption = unrecognised
                ? null
                : card.LabelledOptions.FirstOrDefault(
                    o => string.Equals(o.Value, config.Mode.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    private OpenCodeAutoupdateConfig ReadAutoupdateConfig()
    {
        if (_client is null)
        {
            return new OpenCodeAutoupdateConfig { Mode = OpenCodeAutoupdateMode.NotSet };
        }

        JsonNode? node = _client.GetEffective<JsonNode>(AutoupdatePath);

        // ⚠ The effective value cannot distinguish "absent everywhere" from "explicitly null":
        // both arrive as a null node. The codec is told "defined" only when a node came back, so
        // an explicit null reads as NotSet here where the full editor reports it Unrecognised.
        // That editor is the surface that can act on it — it holds the raw value and offers the
        // replace command — and a summary card claiming a state it cannot repair helps nobody.
        return OpenCodeAutoupdateCodec.Read(JsonCurrency.FromJsonNode(node), node is not null);
    }

    private Task WriteAutoupdate(EssentialsCardViewModel card)
    {
        if (_client is null || card.SelectedOption is not { } option)
        {
            return Task.CompletedTask;
        }

        if (!Enum.TryParse(option.Value, out OpenCodeAutoupdateMode mode))
        {
            return Task.CompletedTask;
        }

        object? value = OpenCodeAutoupdateCodec.Write(new OpenCodeAutoupdateConfig { Mode = mode });
        if (value is null)
        {
            // NotSet writes nothing — it REMOVES the key, which is a different act from writing
            // false. RemoveValue is a no-op when the scope does not define it, so re-selecting
            // "Not set" on an already-unset key cannot dirty the document.
            _client.RemoveValue(AutoupdatePath, _client.DefaultScope);
            return Task.CompletedTask;
        }

        // Ghost-change guard, as on every other card: only persist when this actually changes the
        // effective value, so a picker reasserting its selection cannot pin a redundant key and
        // light the Save banner over an empty diff.
        if (ReadAutoupdateConfig().Mode != mode)
        {
            _client.SetValue(AutoupdatePath, value, _client.DefaultScope);
        }

        return Task.CompletedTask;
    }

    private Task ReadRules(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            string globalRules = Path.Combine(OpenCodePaths.GlobalDirectory(_environment), "AGENTS.md");
            card.DerivedText = File.Exists(globalRules)
                ? string.Format(CultureInfo.CurrentCulture, Strings.EssentialsRulesGlobalFmt, globalRules)
                : Strings.EssentialsRulesNone;
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True when the config directory is redirected AND both it and the default directory hold an
    /// <c>AGENTS.md</c> — the observable precondition of the documented gotcha.
    /// </summary>
    private bool HasShadowedGlobalRules()
    {
        if (_environment.ConfigDir is null)
        {
            return false;
        }

        string redirected = Path.Combine(OpenCodePaths.GlobalDirectory(_environment), "AGENTS.md");
        string fallback = Path.Combine(OpenCodePaths.DefaultGlobalDirectory(), "AGENTS.md");

        // A redirect that resolves to the default directory is not a redirect; comparing the
        // resolved paths rather than trusting the variable keeps OPENCODE_CONFIG_DIR=~/.config/opencode
        // from raising a warning about a file shadowing itself.
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(redirected),
                Path.TrimEndingDirectorySeparator(fallback),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(redirected) && File.Exists(fallback);
    }

    private Task ReadActiveConfig(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            card.DerivedText = DescribeActiveConfig();
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    /// <remarks>
    /// Ordered by the precedence the SDK's own discovery uses: inline content outranks an explicit
    /// path, which outranks a redirected directory. Reporting a lower layer while a higher one is
    /// live is the exact confusion the card exists to remove.
    /// </remarks>
    private string DescribeActiveConfig()
    {
        if (_environment.InlineContent is not null)
        {
            return Strings.EssentialsActiveConfigInline;
        }

        if (_environment.ConfigPath is { } explicitPath)
        {
            return string.Format(
                CultureInfo.CurrentCulture, Strings.EssentialsActiveConfigFromVarFmt,
                explicitPath, "OPENCODE_CONFIG");
        }

        string path = OpenCodePaths.GlobalConfigPath(_environment);

        return _environment.ConfigDir is null
            ? path
            : string.Format(
                CultureInfo.CurrentCulture, Strings.EssentialsActiveConfigFromVarFmt,
                path, "OPENCODE_CONFIG_DIR");
    }
}
