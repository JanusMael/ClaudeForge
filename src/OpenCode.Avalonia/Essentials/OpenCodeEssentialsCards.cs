using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Avalonia.Updates;
using Bennewitz.Ninja.OpenCode.Sdk.Plugins;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Essentials;

/// <summary>
/// The curation: which OpenCode settings earn a card, in what order, and what each one says.
/// </summary>
/// <remarks>
/// <para>
/// Split from the page's plumbing because this half is the part that changes for product reasons
/// rather than technical ones — promoting or demoting a setting is a one-method edit here, and the
/// file it lives in should say so.
/// </para>
/// <para>
/// ⚠ <b>Nineteen cards, where the plan's table lists seventeen.</b> Two of its rows name two keys
/// each — <c>permission.webfetch</c> · <c>permission.websearch</c>, and
/// <c>tool_output.max_lines</c> · <c>max_bytes</c> — and a card binds exactly one value. Splitting
/// them keeps each independently editable and independently dangerous, which grouping would have
/// cost. The plan anticipates this: curation lives in one place precisely so the set can be
/// adjusted after seeing it rendered.
/// </para>
/// </remarks>
public sealed partial class OpenCodeEssentialsViewModel
{
    // ── Paths ─────────────────────────────────────────────────────────

    /// <summary>The <c>permission</c> key at the document root.</summary>
    internal const string PermissionKey = "permission";

    internal const string AutoupdatePath = "autoupdate";
    internal const string SharePath = "share";
    internal const string SnapshotPath = "snapshot";
    internal const string PluginPath = "plugin";
    internal const string ModelPath = "model";
    internal const string SmallModelPath = "small_model";
    internal const string SubagentDepthPath = "subagent_depth";
    internal const string CompactionAutoPath = "compaction.auto";
    internal const string ToolOutputMaxLinesPath = "tool_output.max_lines";
    internal const string ToolOutputMaxBytesPath = "tool_output.max_bytes";
    internal const string DefaultAgentPath = "default_agent";

    // ── Card ids ──────────────────────────────────────────────────────

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

    /// <summary>Card id for the derived plugin inventory.</summary>
    public const string CardIdPlugins = "derived.plugins";

    public const string CardIdGlobalPermission = "permission";
    public const string CardIdBash = "permission.bash";
    public const string CardIdEdit = "permission.edit";
    public const string CardIdExternalDirectory = "permission.external_directory";
    public const string CardIdWebFetch = "permission.webfetch";
    public const string CardIdWebSearch = "permission.websearch";
    /// <summary>
    /// The six cards that share the <c>permission</c> key, and therefore interlock.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>These have to be refreshed as a group after any one of them writes.</b> Which form
    /// the file holds decides which of them can safely write, and that is decided in
    /// <c>ReadAsync</c> — so a card whose sibling has just changed the form is stale, and stale
    /// here means enabled when it should be standing down. See <c>RefreshPermissionCards</c>.
    /// </remarks>
    internal static readonly string[] PermissionCardIds =
    [
        CardIdGlobalPermission, CardIdBash, CardIdEdit,
        CardIdExternalDirectory, CardIdWebFetch, CardIdWebSearch,
    ];

    public const string CardIdShare = "share";
    public const string CardIdSnapshot = "snapshot";
    public const string CardIdModel = "model";
    public const string CardIdSmallModel = "small_model";
    public const string CardIdSubagentDepth = "subagent_depth";
    public const string CardIdCompactionAuto = "compaction.auto";
    public const string CardIdToolOutputMaxLines = "tool_output.max_lines";
    public const string CardIdToolOutputMaxBytes = "tool_output.max_bytes";
    public const string CardIdDefaultAgent = "default_agent";

    /// <summary>
    /// The three actions a permission value may hold.
    /// </summary>
    /// <remarks>
    /// Ordered most-to-least restrictive, so the safe end of the range is where the eye starts.
    /// Read off <c>PermissionActionConfig</c> in the bundled schema, not invented.
    /// </remarks>
    private static readonly string[] PermissionActions = ["deny", "ask", "allow"];

    /// <summary>
    /// The values <c>share</c> admits.
    /// </summary>
    /// <remarks>Schema enum, in the schema's own order.</remarks>
    private static readonly string[] ShareModes = ["manual", "auto", "disabled"];

    /// <summary>
    /// The built-in agents that may legitimately be a <c>default_agent</c>.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>Measured against v1.17.9, not read off the schema.</b> The schema names seven agents
    /// under <c>agent</c> — <c>plan build general explore title summary compaction</c> — and
    /// <c>default_agent</c>'s own description says it "must be a primary agent". Probing each with
    /// <c>opencode debug agent &lt;name&gt;</c>: <c>general</c> and <c>explore</c> report
    /// <c>"mode": "subagent"</c>; <c>summary</c> and <c>compaction</c> are primary but
    /// <c>"hidden": true</c>; <c>title</c> <b>does not exist in the binary at all</b>. Exactly two
    /// are offerable, and neither the schema nor the plan says so.
    /// <para>
    /// Suggestions only — the picker stays free-form because a user may define their own primary
    /// agent, and a closed list would reject a perfectly valid value.
    /// </para>
    /// </remarks>
    private static readonly string[] OfferableBuiltInAgents = ["build", "plan"];

    /// <summary>Built-ins that must NOT be offered — measured as subagent or hidden.</summary>
    private static readonly string[] NonOfferableBuiltInAgents =
        ["general", "explore", "title", "summary", "compaction"];

    // ── Curation ──────────────────────────────────────────────────────

    private IEnumerable<EssentialsCardViewModel> BuildCards()
    {
        // Access first: what the agent may do without asking.
        yield return BuildGlobalPermissionCard();
        yield return BuildToolPermissionCard(
            CardIdBash, "bash", Strings.EssentialsCardBashTitle, Strings.EssentialsCardBashBody,
            Strings.EssentialsCardBashDanger);
        yield return BuildToolPermissionCard(
            CardIdEdit, "edit", Strings.EssentialsCardEditTitle, Strings.EssentialsCardEditBody,
            Strings.EssentialsCardEditDanger);
        yield return BuildToolPermissionCard(
            CardIdExternalDirectory, "external_directory",
            Strings.EssentialsCardExternalDirTitle, Strings.EssentialsCardExternalDirBody,
            Strings.EssentialsCardExternalDirDanger);
        yield return BuildToolPermissionCard(
            CardIdWebFetch, "webfetch", Strings.EssentialsCardWebFetchTitle,
            Strings.EssentialsCardWebFetchBody, Strings.EssentialsCardWebFetchDanger);
        yield return BuildToolPermissionCard(
            CardIdWebSearch, "websearch", Strings.EssentialsCardWebSearchTitle,
            Strings.EssentialsCardWebSearchBody, Strings.EssentialsCardWebSearchDanger);

        // Privacy and recoverability.
        yield return BuildShareCard();
        yield return BuildSnapshotCard();
        yield return BuildPluginsCard();

        // Cost.
        yield return BuildModelCard();
        yield return BuildSmallModelCard();
        yield return BuildSubagentDepthCard();

        // Quality.
        yield return BuildCompactionCard();
        yield return BuildToolOutputCard(
            CardIdToolOutputMaxLines, ToolOutputMaxLinesPath,
            Strings.EssentialsCardMaxLinesTitle, Strings.EssentialsCardMaxLinesBody,
            increment: 100);
        yield return BuildToolOutputCard(
            CardIdToolOutputMaxBytes, ToolOutputMaxBytesPath,
            Strings.EssentialsCardMaxBytesTitle, Strings.EssentialsCardMaxBytesBody,
            increment: 1024);

        // Behaviour and diagnostics.
        yield return BuildAutoupdateCard();
        yield return BuildDefaultAgentCard();
        yield return BuildRulesCard();
        yield return BuildActiveConfigCard();
    }

    // ── Access ────────────────────────────────────────────────────────

    /// <summary>
    /// Card 1 — the whole <c>permission</c> value as a single action.
    /// </summary>
    /// <remarks>
    /// A bare <c>"allow"</c> here auto-approves <em>every</em> tool, which is the single widest
    /// thing this configuration can say. The card stands down when per-tool rules exist, because
    /// writing a bare action would delete them.
    /// </remarks>
    private EssentialsCardViewModel BuildGlobalPermissionCard() => new(new EssentialsCardOptions
    {
        Id = CardIdGlobalPermission,
        Title = Strings.EssentialsCardGlobalPermissionTitle,
        Body = Strings.EssentialsCardGlobalPermissionBody,
        Severity = SeverityFor(PermissionKey),
        Kind = EssentialsCardKind.EnumString,
        EnumOptions = PermissionActions,
        ReadAsync = ReadGlobalPermission,
        WriteAsync = WriteGlobalPermission,
        IsDangerPredicate = IsAllow,
        DangerBannerText = Strings.EssentialsCardGlobalPermissionDanger,
        ViewInGroupTitle = PageTitleFor(PermissionKey),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = PermissionKey,
    });

    /// <summary>Cards 2–5 — one tool's entry under <c>permission</c>.</summary>
    private EssentialsCardViewModel BuildToolPermissionCard(
        string id, string tool, string title, string body, string danger)
        => new(new EssentialsCardOptions
        {
            Id = id,
            Title = title,
            Body = body,
            Severity = SeverityFor($"{PermissionKey}.{tool}"),
            Kind = EssentialsCardKind.EnumString,
            EnumOptions = PermissionActions,
            ReadAsync = ReadToolPermission(tool),
            WriteAsync = WriteToolPermission(tool),
            IsDangerPredicate = IsAllow,
            DangerBannerText = danger,
            ViewInGroupTitle = PageTitleFor(PermissionKey),
            ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
            JsonPathFilter = PermissionKey,
        });

    // ── Privacy and recoverability ────────────────────────────────────

    /// <summary>Card 6 — <c>share</c>. No Claude analogue, and the highest-impact privacy knob.</summary>
    private EssentialsCardViewModel BuildShareCard() => new(new EssentialsCardOptions
    {
        Id = CardIdShare,
        Title = Strings.EssentialsCardShareTitle,
        Body = Strings.EssentialsCardShareBody,
        Severity = SeverityFor(SharePath),
        Kind = EssentialsCardKind.EnumString,
        EnumOptions = ShareModes,
        ReadAsync = ReadString(SharePath),
        WriteAsync = WriteString(SharePath),
        IsDangerPredicate = c => string.Equals(c.EnumValue, "auto", StringComparison.Ordinal),
        DangerBannerText = Strings.EssentialsCardShareDanger,
        ViewInGroupTitle = PageTitleFor(SharePath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = SharePath,
    });

    /// <summary>Card 7 — <c>snapshot</c>. Filesystem snapshots are the undo.</summary>
    /// <remarks>
    /// ⚠ The danger is <c>false</c> specifically, not "not true". Absent means the documented
    /// default (<c>true</c>) applies, which is the safe state — treating unset as dangerous would
    /// raise a red banner on the overwhelming majority of configurations and teach users to
    /// ignore it.
    /// </remarks>
    private EssentialsCardViewModel BuildSnapshotCard() => new(new EssentialsCardOptions
    {
        Id = CardIdSnapshot,
        Title = Strings.EssentialsCardSnapshotTitle,
        Body = Strings.EssentialsCardSnapshotBody,
        Severity = SeverityFor(SnapshotPath),
        Kind = EssentialsCardKind.Bool,
        ReadAsync = ReadBool(SnapshotPath),
        WriteAsync = WriteBool(SnapshotPath),
        IsDangerPredicate = c => c.BoolValue == false,
        DangerBannerText = Strings.EssentialsCardSnapshotDanger,
        ViewInGroupTitle = PageTitleFor(SnapshotPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = SnapshotPath,
    });

    /// <summary>
    /// Card 8 — the <c>plugin</c> inventory, read-only.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>Derived rather than a StringList, and that is a data-loss decision.</b> The schema
    /// types each element as <c>anyOf[string, [string, object]]</c> — a plugin may carry an
    /// options object as a two-element tuple. A <c>StringList</c> card holds
    /// <c>ObservableCollection&lt;string&gt;</c>, so it would read a tuple entry as nothing and
    /// write the list back <b>without it</b>, silently deleting a configured plugin's options. The
    /// plan already tiers this card as informational, so reporting is the whole job; the Plugins
    /// page edits them properly.
    /// </remarks>
    private EssentialsCardViewModel BuildPluginsCard() => new(new EssentialsCardOptions
    {
        Id = CardIdPlugins,
        Title = Strings.EssentialsCardPluginsTitle,
        Body = Strings.EssentialsCardPluginsBody,
        Severity = SeverityFor(PluginPath),
        Kind = EssentialsCardKind.Derived,
        ReadAsync = ReadPlugins,
        ViewInGroupTitle = PageTitleFor(PluginPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = PluginPath,
    });

    private Task ReadPlugins(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            IReadOnlyList<OpenCodePluginEntry> entries =
                OpenCodePluginCodec.ReadList(JsonCurrency.FromJsonNode(Effective(PluginPath)));

            card.DerivedText = entries.Count == 0
                ? Strings.EssentialsPluginsNone
                : Format(
                    Strings.EssentialsPluginsListFmt,
                    entries.Count,
                    string.Join(", ", entries.Select(DescribePlugin)));
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    /// <remarks>
    /// An entry that matched neither schema arm is named as unreadable rather than skipped — a
    /// plugin missing from a list headed "loaded into the agent process" is the one a user most
    /// needs to see.
    /// </remarks>
    private static string DescribePlugin(OpenCodePluginEntry entry)
    {
        if (entry.IsOpaque)
        {
            return Strings.EssentialsPluginsUnreadableEntry;
        }

        string name = entry.Name ?? Strings.EssentialsPluginsUnreadableEntry;
        return entry.HasOptions ? Format(Strings.EssentialsPluginsWithOptionsFmt, name) : name;
    }

    // ── Cost ──────────────────────────────────────────────────────────

    private EssentialsCardViewModel BuildModelCard() => new(new EssentialsCardOptions
    {
        Id = CardIdModel,
        Title = Strings.EssentialsCardModelTitle,
        Body = Strings.EssentialsCardModelBody,
        Severity = SeverityFor(ModelPath),
        Kind = EssentialsCardKind.EnumString,

        // ⚠ Free-form, with no suggestion list: the values are provider/model pairs that come from
        // the configured providers, and building that picker is the plan's own "Providers and
        // models" work. Offering a hardcoded subset would be worse than offering none — it would
        // read as the complete set.
        AllowsFreeForm = true,
        ReadAsync = ReadString(ModelPath),
        WriteAsync = WriteString(ModelPath),
        IsDangerPredicate = IsProviderDisabled,
        DangerBannerText = Strings.EssentialsCardModelDanger,
        ViewInGroupTitle = PageTitleFor(ModelPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = ModelPath,
    });

    private EssentialsCardViewModel BuildSmallModelCard() => new(new EssentialsCardOptions
    {
        Id = CardIdSmallModel,
        Title = Strings.EssentialsCardSmallModelTitle,
        Body = Strings.EssentialsCardSmallModelBody,
        Severity = SeverityFor(SmallModelPath),
        Kind = EssentialsCardKind.EnumString,
        AllowsFreeForm = true,
        ReadAsync = ReadString(SmallModelPath),
        WriteAsync = WriteString(SmallModelPath),
        IsDangerPredicate = IsProviderDisabled,
        DangerBannerText = Strings.EssentialsCardModelDanger,
        ViewInGroupTitle = PageTitleFor(SmallModelPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = SmallModelPath,
    });

    /// <summary>
    /// Card 11 — <c>subagent_depth</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ The threshold is the plan's (&gt; 2), and the schema's documented default is <b>1</b> —
    /// "prevents subagents from launching subagents". So the banner fires only well above default,
    /// which is the point: this is a cost multiplier, not a boundary.
    /// </remarks>
    private EssentialsCardViewModel BuildSubagentDepthCard() => new(new EssentialsCardOptions
    {
        Id = CardIdSubagentDepth,
        Title = Strings.EssentialsCardSubagentDepthTitle,
        Body = Strings.EssentialsCardSubagentDepthBody,
        Severity = SeverityFor(SubagentDepthPath),
        Kind = EssentialsCardKind.Int,

        // Schema: minimum 0 — zero is valid here and means "no subagents at all".
        IntMinimum = 0,
        IntIncrement = 1,
        ReadAsync = ReadInt(SubagentDepthPath),
        WriteAsync = WriteInt(SubagentDepthPath),
        IsDangerPredicate = c => c.IntValue > 2,
        DangerBannerText = Strings.EssentialsCardSubagentDepthDanger,
        ViewInGroupTitle = PageTitleFor(SubagentDepthPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = SubagentDepthPath,
    });

    // ── Quality ───────────────────────────────────────────────────────

    private EssentialsCardViewModel BuildCompactionCard() => new(new EssentialsCardOptions
    {
        Id = CardIdCompactionAuto,
        Title = Strings.EssentialsCardCompactionTitle,
        Body = Strings.EssentialsCardCompactionBody,
        Severity = SeverityFor(CompactionAutoPath),
        Kind = EssentialsCardKind.Bool,
        ReadAsync = ReadBool(CompactionAutoPath),
        WriteAsync = WriteBool(CompactionAutoPath),
        IsDangerPredicate = c => c.BoolValue == false,
        DangerBannerText = Strings.EssentialsCardCompactionDanger,

        // ⚠ The deep link targets the TOP-LEVEL key: the layout buckets pages by root property,
        // so "compaction.auto" is not a page and asking for one would navigate nowhere.
        ViewInGroupTitle = PageTitleFor("compaction"),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = CompactionAutoPath,
    });

    private EssentialsCardViewModel BuildToolOutputCard(
        string id, string path, string title, string body, decimal increment)
        => new(new EssentialsCardOptions
        {
            Id = id,
            Title = title,
            Body = body,
            Severity = SeverityFor(path),
            Kind = EssentialsCardKind.Int,

            // ⚠ Schema: exclusiveMinimum 0, so the smallest legal value is 1 — NOT 0. Offering 0
            // would write a config OpenCode rejects at load, and a rejected config bricks every
            // command rather than falling back.
            IntMinimum = 1,

            // The step is per card because the two limits live on different scales — a line count
            // sits in the thousands, a byte count in the tens of thousands — so one shared step
            // would leave one of them unusable by arrow.
            IntIncrement = increment,
            ReadAsync = ReadInt(path),
            WriteAsync = WriteInt(path),
            ViewInGroupTitle = PageTitleFor("tool_output"),
            ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
            JsonPathFilter = path,
        });

    // ── Behaviour ─────────────────────────────────────────────────────

    /// <summary>
    /// Card 14 — <c>autoupdate</c>: <c>true</c> | <c>false</c> | <c>"notify"</c>, plus absent.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The option labels are the full editor's own strings</b>, not a second set written for
    /// this surface. Two pickers for one key that disagree about what its values are called is
    /// drift this repo has paid for elsewhere; sharing the resource makes agreement structural.
    /// The same argument applies to the ordering, which runs least-to-most automatic here because
    /// that is the axis the user moves along.
    /// </remarks>
    private EssentialsCardViewModel BuildAutoupdateCard() => new(new EssentialsCardOptions
    {
        Id = CardIdAutoupdate,
        Title = Strings.EssentialsCardAutoupdateTitle,
        Body = Strings.EssentialsCardAutoupdateBody,
        Severity = SeverityFor(AutoupdatePath),
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

    /// <summary>Card 15 — <c>default_agent</c>.</summary>
    private EssentialsCardViewModel BuildDefaultAgentCard() => new(new EssentialsCardOptions
    {
        Id = CardIdDefaultAgent,
        Title = Strings.EssentialsCardDefaultAgentTitle,
        Body = Strings.EssentialsCardDefaultAgentBody,
        Severity = SeverityFor(DefaultAgentPath),
        Kind = EssentialsCardKind.EnumString,

        // Free-form: a user may define their own primary agent, and a closed list would reject it.
        AllowsFreeForm = true,
        EnumOptions = DefaultAgentSuggestions(),
        ReadAsync = ReadString(DefaultAgentPath),
        WriteAsync = WriteString(DefaultAgentPath),
        ViewInGroupTitle = PageTitleFor(DefaultAgentPath),
        ViewInGroupLabelFormat = Strings.EssentialsViewInGroupFmt,
        JsonPathFilter = DefaultAgentPath,
    });

    // ── Diagnostics ───────────────────────────────────────────────────

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
    /// and says OpenCode <em>may</em> be ignoring one. Same treatment, for the same reason, as
    /// <c>OpenCodeArtifactIssue.SkillHasNoDescription</c>.
    /// </para>
    /// </remarks>
    private EssentialsCardViewModel BuildRulesCard() => new(new EssentialsCardOptions
    {
        Id = CardIdRules,
        Title = Strings.EssentialsCardRulesTitle,
        Body = Strings.EssentialsCardRulesBody,
        Severity = SeverityFor("instructions"),
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

        // No JSON key holds this, so the danger table has nothing to say — Neutral is the honest
        // standing tier for a report, and the inline-config banner carries the actual warning.
        Severity = AppSeverity.Neutral,
        Kind = EssentialsCardKind.Derived,
        ReadAsync = ReadActiveConfig,

        // Inline content is the one state where editing a file changes nothing the agent reads.
        IsDangerPredicate = _ => _environment.InlineContent is not null,
        DangerBannerText = Strings.EssentialsActiveConfigInlineBanner,
    });

    /// <summary>
    /// The two offerable built-ins, plus whatever agents this configuration defines itself.
    /// </summary>
    /// <remarks>
    /// The user's own agents are included because one they defined is more likely to be primary
    /// than not; the built-ins measured as subagent or hidden are excluded by name so a suggestion
    /// list cannot recommend a value <c>default_agent</c> would reject.
    /// </remarks>
    private IReadOnlyList<string> DefaultAgentSuggestions()
    {
        List<string> result = [.. OfferableBuiltInAgents];

        foreach (string name in ConfiguredAgentNames())
        {
            if (!result.Contains(name, StringComparer.Ordinal)
                && !NonOfferableBuiltInAgents.Contains(name, StringComparer.Ordinal))
            {
                result.Add(name);
            }
        }

        return result;
    }
}
