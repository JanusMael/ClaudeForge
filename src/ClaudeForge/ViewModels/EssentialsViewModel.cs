using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Catalog;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;
using Bennewitz.Ninja.ClaudeForge.Sdk.Models;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Top-of-tree synthetic page that pins the high-importance
/// Claude Code settings as inline-editable cards.  Curated, not schema-driven:
/// each card knows which underlying accessor it talks to (<see cref="IEnvAccessor"/>
/// for the token env vars, <see cref="IClaudeConfigClient.SetValue{T}(string, T)"/> /
/// <see cref="IClaudeConfigClient.GetEffective{T}"/> for everything else) and
/// supplies a tailored "why this matters" body, a danger-banner predicate
/// (where applicable), and a deep-link target for "View in &lt;group&gt;".
/// <para>
/// The list of cards is intentionally hand-built rather than walking a marker
/// attribute on the schema — the criteria for "essential" mix cost, security,
/// quality, and behaviour-stability concerns that can't be expressed
/// declaratively in the JSON schema.  When a setting is promoted or
/// demoted from this list, edit <see cref="BuildCards"/> directly.
/// </para>
/// <para>
/// The view-model is constructed once per app lifetime by MWVM (cached in
/// <c>_essentialsVm</c>) so workspace reloads don't lose any in-flight UI
/// state.  A reload calls <see cref="RefreshAsync"/>, which re-binds every
/// card's read delegate to the (possibly swapped) SDK client and re-reads
/// the values.
/// </para>
/// </summary>
public class EssentialsViewModel : ObservableObject, IDisposable
{
    /// <summary>Group title used for the "View in <c>Permissions</c>" deep-link.</summary>
    internal const string GroupTitlePermissions = "Permissions";

    /// <summary>Group title used for the "View in <c>MCP Servers</c>" deep-link.</summary>
    internal const string GroupTitleMcpServers = "MCP Servers";

    /// <summary>Group title used for the "View in <c>Sandbox</c>" deep-link.</summary>
    internal const string GroupTitleSandbox = "Sandbox";

    /// <summary>Group title used for the "View in <c>Model &amp; Effort</c>" deep-link.</summary>
    internal const string GroupTitleModelEffort = "Model & Effort";

    /// <summary>Group title used for the "View in <c>General</c>" deep-link.</summary>
    internal const string GroupTitleGeneral = "General";

    /// <summary>Group title used for the "View in <c>Environment</c>" deep-link.</summary>
    internal const string GroupTitleEnvironment = "Environment";

    /// <summary>Card identifier used by synthetic search hits to deep-link to a specific card.</summary>
    public const string CardIdMaxThinkingTokens = "MAX_THINKING_TOKENS";

    public const string CardIdMaxOutputTokens = "CLAUDE_CODE_MAX_OUTPUT_TOKENS";
    public const string CardIdEnableAllProjectMcp = "enableAllProjectMcpServers";
    public const string CardIdSandboxEnabled = "sandbox.enabled";
    public const string CardIdSandboxDomains = "sandbox.network.allowedDomains";
    public const string CardIdModel = "model";
    public const string CardIdEffortLevel = "effortLevel";
    public const string CardIdFastMode = "fastMode";
    public const string CardIdAutoUpdatesChannel = "autoUpdatesChannel";
    public const string CardIdAutoMemoryEnabled = "autoMemoryEnabled";
    public const string CardIdDisableBypass = "permissions.disableBypassPermissionsMode";

    /// <summary>
    /// ID for the auto-update-check toggle card.  NOT a JSON path (the
    /// preference is backed by WindowState, not settings.json) — just
    /// a synthetic identifier the deep-link search uses.
    /// </summary>
    public const string CardIdCheckForUpdates = "checkForUpdatesOnLaunch";

    private IClaudeConfigClient? _client;
    private readonly IEnvironmentProvider _envProvider;
    private readonly Func<bool>? _checkForUpdatesRead;
    private readonly Action<bool>? _checkForUpdatesWrite;
    // volatile: read on the RefreshAsync await-continuation thread (which may differ
    // from the dispatcher thread that runs Dispose) to honor the post-dispose contract.
    private volatile bool _disposed;

    // Serializes RefreshAsync so overlapping refreshes can't interleave their
    // per-card IsLoading toggles. Without this, the constructor's fire-and-forget
    // refresh racing a reload (or a test's explicit RefreshAsync) lets the
    // model-card change subscription observe a transient IsLoading state and
    // either fire a spurious load-time coercion (phantom dirty) or skip a genuine
    // user-change coercion. See EssentialsModelEffortConstraintTests.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// Construct an EssentialsViewModel.
    /// </summary>
    /// <param name="client">SDK client for the settings.json-backed cards.</param>
    /// <param name="envProvider">Environment-variable provider for the env-var cards.</param>
    /// <param name="checkForUpdatesRead">
    /// Optional read delegate for the auto-update-check Essentials card.
    /// Typically <c>() =&gt; mwvm.CheckForUpdatesOnLaunch</c>.  When
    /// non-null (and paired with <paramref name="checkForUpdatesWrite"/>),
    /// the card is added to <see cref="Cards"/>; when null, the card is
    /// omitted (tests / non-MWVM-rooted callers see the rest of the
    /// page unaffected).
    /// </param>
    /// <param name="checkForUpdatesWrite">
    /// Optional write delegate paired with <paramref name="checkForUpdatesRead"/>.
    /// Typically <c>v =&gt; mwvm.CheckForUpdatesOnLaunch = v</c>.  Routes
    /// the user's toggle through MWVM's ObservableProperty so
    /// persistence + <c>_cachedState</c> stay consistent.
    /// </param>
    public EssentialsViewModel(
        IClaudeConfigClient? client,
        IEnvironmentProvider envProvider,
        Func<bool>? checkForUpdatesRead = null,
        Action<bool>? checkForUpdatesWrite = null)
    {
        _client = client;
        _envProvider = envProvider ?? throw new ArgumentNullException(nameof(envProvider));
        _checkForUpdatesRead = checkForUpdatesRead;
        _checkForUpdatesWrite = checkForUpdatesWrite;
        Cards = new ObservableCollection<EssentialsCardViewModel>(BuildCards());

        // When the user changes the model, re-reconcile the effort card
        // (filter + coerce + indicator). The model card's own change handler
        // writes the model synchronously before this fires, so GetEffective
        // already reflects the new value. Only the model card drives this — the
        // effort card never re-enters it, so there is no update loop.
        if (GetCardById(CardIdModel) is { } modelCard)
        {
            modelCard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EssentialsCardViewModel.EnumValue) && !modelCard.IsLoading)
                {
                    // Genuine user model change → coerce + persist the effort override.
                    ApplyModelEffortConstraint(persist: true);
                }
            };
        }

        // Initial read; ignore the Task — the cards render with default
        // (empty / null) values until the read populates them.  Any read
        // failure is silently swallowed by the per-card delegate so a
        // missing schema property doesn't block the page.
        _ = RefreshAsync();
    }

    /// <summary>
    /// Curated list of pinned cards, in display order.  Use
    /// <see cref="GetCardById"/> to look up a specific card (synthetic
    /// search-result deep-links).
    /// </summary>
    public ObservableCollection<EssentialsCardViewModel> Cards { get; }

    /// <summary>
    /// Re-bind every card to the current SDK client (callers should pass
    /// the post-reload client) and re-read all values.
    /// </summary>
    /// <remarks>
    /// Clients can call this with <see langword="null"/> after disposal or
    /// during shutdown — the cards will read empty values without throwing.
    /// </remarks>
    public async Task RefreshAsync(IClaudeConfigClient? client = null)
    {
        // Honor the documented post-disposal contract (see remarks): a refresh
        // requested after disposal — or racing shutdown — is a safe no-op. The gate
        // is intentionally NOT disposed (see Dispose), so even the rare
        // check-then-dispose interleaving resolves to a harmless extra acquire that
        // bails at the in-lock _disposed checks below — no WaitAsync/Release throws.
        if (_disposed)
        {
            return;
        }

        if (client is not null)
        {
            _client = client;
        }

        // Serialize: a concurrent refresh (constructor's fire-and-forget vs a
        // reload, or a test's explicit call) must not interleave the per-card
        // IsLoading toggles, or the model-change subscription can fire a spurious
        // load-time coercion / miss a genuine one.
        await _refreshGate.WaitAsync();
        try
        {
            // Disposal can run while this refresh is suspended (a prior in-flight
            // refresh may hold the gate, parked at the env-var registry probe). After
            // disposal _client is null and the cards are torn down — bail before and
            // after the awaited reads rather than do throwaway work.
            if (_disposed)
            {
                return;
            }

            await Task.WhenAll(Cards.Select(c => c.ReadAsync()));

            if (_disposed)
            {
                return;
            }

            // After values are read, reconcile the effort card against the effective
            // model: narrow its options + set the indicator/advisory. Load is
            // non-persisting (persist:false) — it never writes, so opening the app
            // never lights the Save banner. Coercion only happens on a user model change.
            ApplyModelEffortConstraint(persist: false);
        }
        finally
        {
            // Safe even when Dispose ran during the gated section: the gate is never
            // disposed (see Dispose), so Release never throws ObjectDisposedException.
            _refreshGate.Release();
        }
    }

    /// <summary>Look up a card by its <see cref="EssentialsCardViewModel.Id"/>.</summary>
    public EssentialsCardViewModel? GetCardById(string id)
    {
        return Cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Activate the one-time amber callout on a specific card — used by
    /// MWVM when the user arrives via a synthetic search result.
    /// </summary>
    public void ActivateAmberCalloutFor(string cardId)
    {
        GetCardById(cardId)?.ActivateAmberCallout();
    }

    /// <summary>
    /// Reconcile the effort card against the effective model (the model ↔ effort
    /// inter-relationship from the catalog): narrow the dropdown to the model's
    /// persistable levels, refresh the read-only "current model — supports …"
    /// indicator, and surface a notice when the effective effort is unsupported.
    /// <para>
    /// <paramref name="persist"/> controls whether an invalidated effort is
    /// <b>coerced and written</b>. On a genuine user model change
    /// (<c>persist: true</c>) an unsupported effort is coerced to the nearest
    /// analog and written as an editing-scope override (shown in the Save
    /// preview). On load (<c>persist: false</c>) NOTHING is written — the
    /// dropdown is filtered, the persisted value stays visible, and the advisory
    /// explains it; this prevents a phantom dirty state on app open / reload.
    /// </para>
    /// The catalog/coercion rules come from the SDK (<c>client.Models</c>); only
    /// the reactive application lives here.
    /// </summary>
    private void ApplyModelEffortConstraint(bool persist)
    {
        if (GetCardById(CardIdEffortLevel) is not { } effortCard)
        {
            return;
        }

        IModelCatalogAccessor catalog = _client?.Models ?? ModelCatalogProvider.Default;
        string? effectiveModel = _client?.GetEffective<string>("model");
        IReadOnlyList<string> persistable = catalog.PersistableEffortLevels(effectiveModel);

        // Capture the effective effort BEFORE narrowing — SetFilteredOptions clears
        // the bound collection, which would otherwise reset the captured selection.
        string? currentEffort = effortCard.EnumValue;
        effortCard.ModelSupportSummary = BuildModelSupportSummary(catalog, effectiveModel, persistable);

        if (persistable.Count == 0)
        {
            // Model exposes no effort level (e.g. Haiku): disable + notice.
            effortCard.SetFilteredOptions(persistable);
            SetEffortDisplay(effortCard, null); // clear the disabled control's display, no write
            effortCard.EnumDisabled = true;
            effortCard.ConstraintNoticeText = Strings.LabelEffortNotApplicable;
            effortCard.ShowConstraintNotice = true;
            // Only DROP a persisted explicit effort on a genuine user model change —
            // never silently on load (an inherited value can't be unset anyway, and
            // Claude ignores effort for such a model).
            if (persist && _client is not null && !string.IsNullOrEmpty(_client.GetEffective<string>("effortLevel")))
            {
                _client.RemoveValue("effortLevel", _client.DefaultScope);
            }

            return;
        }

        effortCard.EnumDisabled = false;
        bool invalid = !string.IsNullOrEmpty(currentEffort)
                       && !persistable.Contains(currentEffort, StringComparer.OrdinalIgnoreCase);

        if (!invalid)
        {
            effortCard.SetFilteredOptions(persistable); // keeps the (valid) selection
            effortCard.ShowConstraintNotice = false;
            return;
        }

        string? analog = catalog.NearestAnalogEffort(effectiveModel, currentEffort);
        if (persist)
        {
            // User changed the model → narrow + coerce the override (writes).
            effortCard.SetFilteredOptions(persistable);
            effortCard.EnumValue = analog; // not loading → routes through the write path
            effortCard.ConstraintNoticeText = string.Format(
                CultureInfo.CurrentCulture, Strings.LabelEffortCoercedFmt, analog, currentEffort);
        }
        else
        {
            // Load: keep the now-unsupported persisted value visible, advise, do NOT write.
            effortCard.SetFilteredOptions(persistable.Append(currentEffort!));
            SetEffortDisplay(effortCard, currentEffort);
            effortCard.ConstraintNoticeText = string.Format(
                CultureInfo.CurrentCulture, Strings.LabelEffortUnsupportedFmt, currentEffort);
        }

        effortCard.ShowConstraintNotice = true;
    }

    /// <summary>Set the effort card's displayed value without triggering its auto-write (load path).</summary>
    private static void SetEffortDisplay(EssentialsCardViewModel card, string? value)
    {
        bool wasLoading = card.IsLoading;
        card.IsLoading = true;
        try
        {
            card.EnumValue = value;
        }
        finally
        {
            card.IsLoading = wasLoading;
        }
    }

    private static string BuildModelSupportSummary(
        IModelCatalogAccessor catalog, string? effectiveModel, IReadOnlyList<string> persistable)
    {
        if (string.IsNullOrEmpty(effectiveModel) || persistable.Count == 0)
        {
            return string.Empty;
        }

        string label = catalog.Resolve(effectiveModel)?.Label ?? effectiveModel;
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.LabelModelEffortSummaryFmt,
            label,
            string.Join(" / ", persistable));
    }

    // ── Card construction ─────────────────────────────────────────────────

    private IReadOnlyList<EssentialsCardViewModel> BuildCards()
    {
        // The danger-state predicates are factored out so they can be
        // unit-tested directly via card reflection (and so the BuildCards
        // body doesn't grow another 50 lines of inline lambdas).
        bool EnableAllMcpDanger(EssentialsCardViewModel c)
        {
            return c.BoolValue == true;
        }

        bool SandboxEnabledDanger(EssentialsCardViewModel c)
        {
            return c.BoolValue == false;
        }

        // The amber-callout text is the same on every card (it just tells
        // the user "this is the setting your search matched"); applied
        // uniformly so a future translator only needs to translate one
        // string instead of 11.
        string? amberText = Strings.LabelEssentialsAmberCallout;

        // Model / effort allowed-value lists come from the SDK model catalog (the
        // single source of truth) rather than hardcoded arrays. Effort is narrowed
        // to the effective model's persistable levels (omits session-only "max",
        // empty for models with no effort); Phase 2 makes this recompute reactively
        // when the model changes. Falls back to the shared bundled catalog when
        // constructed without a client (tests).
        IModelCatalogAccessor catalog = _client?.Models ?? ModelCatalogProvider.Default;
        string? effectiveModel = _client?.GetEffective<string>("model");
        IReadOnlyList<string> modelOptions = catalog.ModelSuggestions();
        IReadOnlyList<ModelSuggestionItem> modelSuggestions = ModelSuggestionCatalog.Build(catalog);
        IReadOnlyList<string> effortOptions = catalog.PersistableEffortLevels(effectiveModel);

        // Display order set per user spec: groups cards by
        // user-mental-model rather than by severity tier.  Day-to-day
        // tunables (memory + token budgets + effort + speed + model)
        // first, then security knobs (bypass / MCP trust / sandbox), then
        // the rarely-touched update-channel knob last.  Severity colour
        // is preserved on each card; only the position in the list moves.
        List<EssentialsCardViewModel> list =
        [
            new(
                    id: CardIdAutoMemoryEnabled,
                    title: Strings.EssentialsCardAutoMemoryEnabledTitle,
                    body: Strings.EssentialsCardAutoMemoryEnabledBody,
                    severityColor: "#1976D2", // blue — behaviour
                    kind: EssentialsCardKind.Bool,
                    viewInGroupTitle: GroupTitleGeneral,
                    isEnvVarCard: false,
                    readAsync: ReadBoolAsync("autoMemoryEnabled"),
                    writeAsync: WriteBoolAsync("autoMemoryEnabled"),
                    amberCalloutText: amberText)
                { JsonPathFilter = "autoMemoryEnabled" },
            // 2 — Max Output Tokens (env-var)

            new(
                    id: CardIdMaxOutputTokens,
                    title: Strings.EssentialsCardMaxOutputTokensTitle,
                    body: Strings.EssentialsCardMaxOutputTokensBody,
                    severityColor: "#F4B400", // amber — quality
                    kind: EssentialsCardKind.Int,
                    viewInGroupTitle: GroupTitleEnvironment,
                    isEnvVarCard: true,
                    readAsync: ReadEnvIntAsync(EnvVarKey.MaxOutputTokens),
                    writeAsync: WriteEnvIntAsync(EnvVarKey.MaxOutputTokens),
                    amberCalloutText: amberText)
                { JsonPathFilter = EnvVarKey.MaxOutputTokens },
            // 3 — Max Thinking Tokens (env-var)

            new(
                    id: CardIdMaxThinkingTokens,
                    title: Strings.EssentialsCardMaxThinkingTokensTitle,
                    body: Strings.EssentialsCardMaxThinkingTokensBody,
                    severityColor: "#F4B400",
                    kind: EssentialsCardKind.Int,
                    viewInGroupTitle: GroupTitleEnvironment,
                    isEnvVarCard: true,
                    readAsync: ReadEnvIntAsync(EnvVarKey.MaxThinkingTokens),
                    writeAsync: WriteEnvIntAsync(EnvVarKey.MaxThinkingTokens),
                    amberCalloutText: amberText)
                { JsonPathFilter = EnvVarKey.MaxThinkingTokens },
            // 4 — Effort level

            new(
                    id: CardIdEffortLevel,
                    title: Strings.EssentialsCardEffortLevelTitle,
                    body: Strings.EssentialsCardEffortLevelBody,
                    severityColor: "#F4B400",
                    kind: EssentialsCardKind.EnumString,
                    viewInGroupTitle: GroupTitleModelEffort,
                    isEnvVarCard: false,
                    readAsync: ReadStringAsync("effortLevel"),
                    writeAsync: WriteStringAsync("effortLevel"),
                    enumOptions: effortOptions,
                    amberCalloutText: amberText)
                { JsonPathFilter = "effortLevel" },
            // 5 — Fast mode

            new(
                    id: CardIdFastMode,
                    title: Strings.EssentialsCardFastModeTitle,
                    body: Strings.EssentialsCardFastModeBody,
                    severityColor: "#F4B400",
                    kind: EssentialsCardKind.Bool,
                    viewInGroupTitle: GroupTitleModelEffort,
                    isEnvVarCard: false,
                    readAsync: ReadBoolAsync("fastMode"),
                    writeAsync: WriteBoolAsync("fastMode"),
                    amberCalloutText: amberText)
                { JsonPathFilter = "fastMode" },
            // 6 — Model

            new(
                    id: CardIdModel,
                    title: Strings.EssentialsCardModelTitle,
                    body: Strings.EssentialsCardModelBody,
                    severityColor: "#D32F2F", // red — cost
                    kind: EssentialsCardKind.EnumString,
                    viewInGroupTitle: GroupTitleModelEffort,
                    isEnvVarCard: false,
                    readAsync: ReadStringAsync("model"),
                    writeAsync: WriteStringAsync("model"),
                    // Suggestions from the model catalog (aliases + pinned ids +
                    // [1m] variants). Free-form ComboBox (AllowsFreeForm → IsEditable)
                    // so users can still type any custom model id.
                    enumOptions: modelOptions,
                    amberCalloutText: amberText,
                    allowsFreeForm: true)
                { JsonPathFilter = "model", ModelSuggestions = modelSuggestions },
            // 7 — Disable bypass-permissions mode

            new(
                    id: CardIdDisableBypass,
                    title: Strings.EssentialsCardDisableBypassTitle,
                    body: Strings.EssentialsCardDisableBypassBody,
                    severityColor: "#D32F2F",
                    kind: EssentialsCardKind.Bool,
                    viewInGroupTitle: GroupTitlePermissions,
                    isEnvVarCard: false,
                    readAsync: ReadStringFlagAsync("permissions.disableBypassPermissionsMode", DisableBypassValue),
                    writeAsync: WriteStringFlagAsync("permissions.disableBypassPermissionsMode", DisableBypassValue),
                    amberCalloutText: amberText)
                { JsonPathFilter = "permissions.disableBypassPermissionsMode" },
            // 8 — Auto-trust project MCP servers

            new(
                    id: CardIdEnableAllProjectMcp,
                    title: Strings.EssentialsCardEnableAllMcpTitle,
                    body: Strings.EssentialsCardEnableAllMcpBody,
                    severityColor: "#D32F2F", // red — security
                    kind: EssentialsCardKind.Bool,
                    viewInGroupTitle: GroupTitleMcpServers,
                    isEnvVarCard: false,
                    readAsync: ReadBoolAsync("enableAllProjectMcpServers"),
                    writeAsync: WriteBoolAsync("enableAllProjectMcpServers"),
                    isDangerPredicate: EnableAllMcpDanger,
                    dangerBannerText: Strings.EssentialsCardEnableAllMcpDanger,
                    amberCalloutText: amberText)
                { JsonPathFilter = "enableAllProjectMcpServers" },
            // 9 — Sandbox enabled

            new(
                    id: CardIdSandboxEnabled,
                    title: Strings.EssentialsCardSandboxEnabledTitle,
                    body: Strings.EssentialsCardSandboxEnabledBody,
                    severityColor: "#D32F2F",
                    kind: EssentialsCardKind.Bool,
                    viewInGroupTitle: GroupTitleSandbox,
                    isEnvVarCard: false,
                    readAsync: ReadBoolAsync("sandbox.enabled"),
                    writeAsync: WriteBoolAsync("sandbox.enabled"),
                    isDangerPredicate: SandboxEnabledDanger,
                    dangerBannerText: Strings.EssentialsCardSandboxEnabledDanger,
                    amberCalloutText: amberText)
                { JsonPathFilter = "sandbox.enabled" },
            // 10 — Sandbox allowed domains (sandbox.network.allowedDomains per schema)

            new(
                    id: CardIdSandboxDomains,
                    title: Strings.EssentialsCardSandboxDomainsTitle,
                    body: Strings.EssentialsCardSandboxDomainsBody,
                    severityColor: "#D32F2F",
                    kind: EssentialsCardKind.StringList,
                    viewInGroupTitle: GroupTitleSandbox,
                    isEnvVarCard: false,
                    readAsync: ReadStringListAsync("sandbox.network.allowedDomains"),
                    writeAsync: WriteStringListAsync("sandbox.network.allowedDomains"),
                    amberCalloutText: amberText)
                { JsonPathFilter = "sandbox.network.allowedDomains" },
            // 11 — Auto-updates channel

            new(
                    id: CardIdAutoUpdatesChannel,
                    title: Strings.EssentialsCardAutoUpdatesChannelTitle,
                    body: Strings.EssentialsCardAutoUpdatesChannelBody,
                    severityColor: "#1976D2",
                    kind: EssentialsCardKind.EnumString,
                    viewInGroupTitle: GroupTitleGeneral,
                    isEnvVarCard: false,
                    readAsync: ReadStringAsync("autoUpdatesChannel"),
                    writeAsync: WriteStringAsync("autoUpdatesChannel"),
                    enumOptions: ["stable", "latest"],
                    amberCalloutText: amberText)
                { JsonPathFilter = "autoUpdatesChannel" },
        ];

        // 1 — Auto-memory enabled

        // 2 — Max Output Tokens (env-var)

        // 3 — Max Thinking Tokens (env-var)

        // 4 — Effort level

        // 5 — Fast mode

        // 6 — Model

        // 7 — Disable bypass-permissions mode

        // 8 — Auto-trust project MCP servers

        // 9 — Sandbox enabled

        // 10 — Sandbox allowed domains (sandbox.network.allowedDomains per schema)

        // 11 — Auto-updates channel

        // 12 — Check for updates on launch.  ClaudeForge-APP preference,
        //      not a Claude Code setting — backed by WindowState, not
        //      settings.json.  Only added when the constructor was given
        //      both read + write delegates (typically MWVM passes them;
        //      tests omit them, which keeps the rest of the page testable
        //      in isolation).
        if (_checkForUpdatesRead is not null && _checkForUpdatesWrite is not null)
        {
            list.Add(new EssentialsCardViewModel(
                id: CardIdCheckForUpdates,
                title: Strings.EssentialsCardCheckForUpdatesTitle,
                body: Strings.EssentialsCardCheckForUpdatesBody,
                severityColor: "#1976D2", // blue — behaviour (same as auto-memory / channel)
                kind: EssentialsCardKind.Bool,
                viewInGroupTitle: GroupTitleGeneral,
                isEnvVarCard: false,
                readAsync: ReadWindowStateBoolAsync(_checkForUpdatesRead),
                writeAsync: WriteWindowStateBoolAsync(_checkForUpdatesWrite),
                amberCalloutText: amberText));
        }

        return list;
    }

    /// <summary>
    /// Build a readAsync delegate that reads a bool out of an external
    /// store (e.g. WindowState) via the supplied <paramref name="read"/>
    /// closure rather than from the SDK client.  Used by the
    /// "Check for updates on launch" card.
    /// </summary>
    private static Func<EssentialsCardViewModel, Task> ReadWindowStateBoolAsync(Func<bool> read)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                card.BoolValue = read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                // Defensive: a stale closure (e.g. MWVM disposed) should
                // not crash the page.  Leave BoolValue at null so the
                // ToggleSwitch shows its indeterminate state until a
                // subsequent refresh picks up a valid read.
                Log.Information(
                    ex,
                    "[Essentials] ReadWindowStateBool delegate threw; card stays in null state.");
            }
            finally
            {
                card.IsLoading = false;
            }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Build a writeAsync delegate that pushes a bool to an external
    /// store via the supplied <paramref name="write"/> closure.  Used
    /// by the "Check for updates on launch" card.
    /// </summary>
    private static Func<EssentialsCardViewModel, Task> WriteWindowStateBoolAsync(Action<bool> write)
    {
        return card =>
        {
            try
            {
                // Card binds a ToggleSwitch to BoolValue; the toggle
                // produces non-null values when user-driven, so a null
                // here is unexpected.  Coerce defensively to true (the
                // initialiser default) rather than crash.
                write(card.BoolValue ?? true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                Log.Information(
                    ex,
                    "[Essentials] WriteWindowStateBool delegate threw; user toggle dropped.");
            }
            return Task.CompletedTask;
        };
    }

    // ── Read / write delegate factories ──────────────────────────────────
    //
    // Each helper builds a closure over (this, varName / jsonPath) so the
    // card stays type-agnostic about which accessor it uses.  The IsLoading
    // flag is set on the card around every read so the partial-method
    // OnXChanged routers don't fire a stale write back.

    private Func<EssentialsCardViewModel, Task> ReadEnvIntAsync(string varName)
    {
        return async card =>
        {
            // ── Synchronous phase ─────────────────────────────────────────────
            // The IsLoading guard scope MUST stay tight around the IntValue
            // assignment (which fires OnIntValueChanged → would otherwise
            // recurse through WriteAsync → SDK Set → recursive read).  Before
            // 89bdb68 (Task.Run wrap for the registry probe) this entire body
            // was synchronous, so a wider scope was harmless.  After the
            // wrap, the body became truly async — if the guard scope spans
            // the await, the user can type into the NumericUpDown during the
            // registry probe and have their OnIntValueChanged short-circuited
            // by the still-true IsLoading flag, silently dropping the write.
            // Bug reported 2026-05-13 on the MaxOutputTokens card after a
            // profile switch (registry probe slow on that machine — gave the
            // user plenty of time to type before IsLoading cleared).
            try
            {
                card.IsLoading = true;
                int? value = _client?.Env.Get(varName) is { } raw &&
                             int.TryParse(raw, NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out int n)
                    ? n
                    : null;
                card.IntValue = value;
            }
            finally
            {
                card.IsLoading = false;
            }

            // ── Asynchronous phase ────────────────────────────────────────────
            // Env source labels (HasSettingsJsonSource / HasOsUserSource /
            // HasOsMachineSource / EffectiveEnvSourceLabel) are descriptive
            // sub-text that don't feed any partial-method router — no need
            // for the IsLoading guard.  A transient stale label is acceptable;
            // a silently-dropped user write is not.
            await UpdateEnvSourceLabelsAsync(card, varName).ConfigureAwait(true);
        };
    }

    private Func<EssentialsCardViewModel, Task> WriteEnvIntAsync(string varName)
    {
        return async card =>
        {
            if (_client is null)
            {
                Log.Information("[Essentials.Write] varName={VarName} skipped (client=null)", varName);
                return;
            }

            int? value = card.IntValue;
            string? str = value?.ToString(CultureInfo.InvariantCulture);

            // Ghost-change guard: only write when the value differs from the
            // current effective env value. A spurious NumericUpDown event that
            // re-emits the existing value would otherwise pin a redundant env
            // entry — dirtying the workspace with no visible Save-preview diff.
            if (string.Equals(str, _client.Env.Get(varName), StringComparison.Ordinal))
            {
                Log.Information("[Essentials.Write] varName={VarName} skipped (effective value unchanged)", varName);
            }
            else
            {
                _client.Env.Set(varName, str);
                // Permanent audit log: pairs with the [Essentials.UserEdit] line
                // fired at OnIntValueChanged, capturing the moment the user's
                // intent reaches the SDK env map.  hasUnsaved=true after the write
                // confirms the workspace was marked dirty (otherwise the save flow
                // wouldn't pick it up).
                Log.Information("[Essentials.Write] varName={VarName} wrote=\"{Str}\" hasUnsavedAfter={Has}",
                    varName, str ?? "(null)", _client.HasUnsavedChanges);
            }

            await UpdateEnvSourceLabelsAsync(card, varName).ConfigureAwait(true);
        };
    }

    private Func<EssentialsCardViewModel, Task> ReadBoolAsync(string jsonPath)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                // GetEffective<bool?> returns null for an unset path (ConvertFromJsonNode
                // returns default), which the tri-state Bool card treats as inherit; an
                // explicit true/false comes back as itself.
                card.BoolValue = _client is null ? null : _client.GetEffective<bool?>(jsonPath);
            }
            finally
            {
                card.IsLoading = false;
            }

            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> WriteBoolAsync(string jsonPath)
    {
        return card =>
        {
            if (_client is null)
            {
                return Task.CompletedTask;
            }

            if (card.BoolValue is bool b)
            {
                // Ghost-change guard (see WriteStringAsync): only persist when the
                // value differs from the current effective value. EqualityComparer
                // honours the nullable-bool semantics (true/false/inherit).
                if (!EqualityComparer<bool?>.Default.Equals(b, _client.GetEffective<bool?>(jsonPath)))
                {
                    _client.SetValue(jsonPath, b, _client.DefaultScope);
                }
            }
            else
            {
                _client.RemoveValue(jsonPath, _client.DefaultScope);
            }

            return Task.CompletedTask;
        };
    }

    // permissions.disableBypassPermissionsMode is a STRING enum ["disable"] (NOT a
    // boolean) — present (= "disable") means disabled, absent means not disabled. A
    // Bool card surfaces it as a checkbox; these helpers map the string enum to/from
    // that checkbox. Writing a raw bool here is a schema-validation error (the bug
    // this fixes).
    private const string DisableBypassValue = "disable";

    private Func<EssentialsCardViewModel, Task> ReadStringFlagAsync(string jsonPath, string onValue)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                // Present (= onValue) -> checked; absent -> null (inherit/indeterminate).
                // GetEffective<bool?> can't read a string enum, so compare the string.
                card.BoolValue = _client is not null
                    && string.Equals(_client.GetEffective<string>(jsonPath), onValue, StringComparison.Ordinal)
                        ? true
                        : (bool?)null;
            }
            finally
            {
                card.IsLoading = false;
            }

            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> WriteStringFlagAsync(string jsonPath, string onValue)
    {
        return card =>
        {
            if (_client is null)
            {
                return Task.CompletedTask;
            }

            if (card.BoolValue == true)
            {
                // Ghost-change guard (mirrors WriteBoolAsync): only persist when the
                // effective value isn't already the on-value.
                if (!string.Equals(_client.GetEffective<string>(jsonPath), onValue, StringComparison.Ordinal))
                {
                    _client.SetValue(jsonPath, onValue, _client.DefaultScope);
                }
            }
            else
            {
                // Unchecked / inherit -> not set (there is no on-disk "false" for a
                // string-enum flag); mirror WriteBoolAsync's unconditional remove.
                _client.RemoveValue(jsonPath, _client.DefaultScope);
            }

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Read helper for app-level (non-Claude-config) boolean preferences
    /// stored in <c>WindowState</c>.  Wraps a plain <see cref="Func{Boolean}"/>
    /// in the card-expected delegate shape.  When <paramref name="getter"/>
    /// is <see langword="null"/> (no MWVM plumbing supplied), the card reads
    /// as <see langword="null"/> / inherit — same as a missing Claude
    /// setting.
    /// </summary>
    private static Func<EssentialsCardViewModel, Task> ReadAppBoolAsync(Func<bool>? getter)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                card.BoolValue = getter?.Invoke();
            }
            finally
            {
                card.IsLoading = false;
            }

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Write helper for app-level boolean preferences.  When the user
    /// clears the tri-state to null/inherit on a card whose target has no
    /// inherit concept (app prefs are either on or off), we default to
    /// <see langword="true"/> — matches the documented default in
    /// <see cref="WindowState.CheckForUpdatesOnLaunch"/>.
    /// </summary>
    private static Func<EssentialsCardViewModel, Task> WriteAppBoolAsync(Action<bool>? setter)
    {
        return card =>
        {
            if (setter is null)
            {
                return Task.CompletedTask;
            }

            // App-level prefs don't have an "inherit" tier — collapse null
            // to the documented default (true) so the toggle remains
            // semantically two-state from the user's perspective even though
            // the underlying tri-state Bool card supports null.
            bool value = card.BoolValue ?? true;
            setter(value);
            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> ReadStringAsync(string jsonPath)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                card.EnumValue = _client is null ? null : _client.GetEffective<string>(jsonPath);
            }
            finally
            {
                card.IsLoading = false;
            }

            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> WriteStringAsync(string jsonPath)
    {
        return card =>
        {
            if (_client is null)
            {
                return Task.CompletedTask;
            }

            string? value = card.EnumValue;
            if (string.IsNullOrWhiteSpace(value))
            {
                // Empty/whitespace = "inherit": a free-form combo (e.g. the model
                // AutoCompleteBox) left blank or holding only whitespace is "unset",
                // never a literal value like model=" ". RemoveValue is a no-op (and
                // raises no Changed) when nothing is explicitly set, so a spurious
                // clear can't dirty. Mirrors the empty-string normalization in
                // SettingsGroupEditorViewModel.WriteEditorValue.
                _client.RemoveValue(jsonPath, _client.DefaultScope);
            }
            else if (!string.Equals(value, _client.GetEffective<string>(jsonPath), StringComparison.Ordinal))
            {
                // Ghost-change guard: only persist when the value actually changes
                // the effective value. Re-emitting the already-effective value
                // (e.g. an AutoCompleteBox reasserting its Text on focus, or a
                // ComboBox after an ItemsSource swap) would otherwise pin a
                // redundant explicit key — dirtying the doc with an empty Save
                // preview. StringComparison.Ordinal matches the SDK's storage.
                _client.SetValue(jsonPath, value, _client.DefaultScope);
            }

            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> ReadStringListAsync(string jsonPath)
    {
        return card =>
        {
            card.IsLoading = true;
            try
            {
                card.StringListValues.Clear();
                // The SDK's typed-T escape hatch refuses arrays — pull the raw
                // JsonArray instead and pluck out string children manually.  Non-
                // string entries (e.g. a hand-edited number) are skipped silently
                // so a bad config doesn't crash the page.
                if (_client?.GetEffective<JsonArray>(jsonPath) is { } arr)
                {
                    foreach (JsonNode? n in arr)
                    {
                        if (n is JsonValue jv &&
                            jv.TryGetValue(out string? s))
                        {
                            card.StringListValues.Add(s);
                        }
                    }
                }
            }
            finally
            {
                card.IsLoading = false;
            }

            return Task.CompletedTask;
        };
    }

    private Func<EssentialsCardViewModel, Task> WriteStringListAsync(string jsonPath)
    {
        return card =>
        {
            if (_client is null)
            {
                return Task.CompletedTask;
            }

            if (card.StringListValues.Count == 0)
            {
                _client.RemoveValue(jsonPath, _client.DefaultScope);
            }
            else
            {
                // Build a fresh JsonArray — the SDK's SetValue<T> only accepts
                // primitives + JsonNode subclasses, so we hand it the JsonArray
                // directly rather than a string[] (which raises NotSupported).
                JsonArray arr = [];
                foreach (string s in card.StringListValues)
                    // Cast to JsonNode? to force the non-generic Add(JsonNode?)
                    // overload — JsonArray.Add<T>(T) is IL2026-flagged because
                    // it can serialise non-primitive T at runtime.  The trim
                    // analyzer rejects the generic; the cast disambiguates.
                {
                    arr.Add((JsonNode?)
                        JsonValue.Create(s));
                }

                // Ghost-change guard: only persist when the list differs from the
                // current effective value (JsonNode.DeepEquals = structural array
                // comparison), so re-emitting the inherited set adds no ghost key.
                if (!JsonNode.DeepEquals(arr, _client.GetEffective<JsonArray>(jsonPath)))
                {
                    _client.SetValue(jsonPath, arr, _client.DefaultScope);
                }
            }

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Refresh the "effective source" sub-row labels on env-var cards.
    /// Walks (settings.json env, OS user, OS machine) in that order and
    /// records which source is contributing to the effective value.
    /// </summary>
    /// <remarks>
    /// The User / Machine reads route through <see cref="ReadOsEnvVar"/>
    /// which calls <c>_envProvider.GetVariables(target)</c>.  On Windows,
    /// Machine scope hits the registry
    /// (<c>HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment</c>) —
    /// a synchronous IO call that, on a slow / hung registry, would freeze
    /// the dispatcher.  Each env-var card runs UpdateEnvSourceLabels once
    /// per refresh and there are two env-var cards (MAX_THINKING_TOKENS,
    /// CLAUDE_CODE_MAX_OUTPUT_TOKENS), so up to four registry reads land
    /// on every workspace reload (via the fire-and-forget
    /// <c>_ = _essentialsVm.RefreshAsync(...)</c> kicked from
    /// LoadAllWorkspacesAsync).  Pushing the registry reads off the UI
    /// thread via Task.Run is the same pattern Memory page's Tier-1 scan
    /// uses (see <see cref="MemoryEditorViewModel.RefreshAsync"/>) — the
    /// continuation comes back to the dispatcher to mutate the card
    /// properties safely.  Pinned by I14 in AGENTS.md.
    /// </remarks>
    private async Task UpdateEnvSourceLabelsAsync(EssentialsCardViewModel card, string varName)
    {
        if (!card.IsEnvVarCard)
        {
            return;
        }

        // Fast in-memory read — stays on the dispatcher thread.
        string? settingsValue = _client?.Env.Get(varName);

        // OS env-var probes (registry on Windows Machine scope) — push to
        // the thread pool so the dispatcher stays responsive.
        (string? userValue, string? machineValue) = await Task.Run(() =>
        {
            string? u = ReadOsEnvVar(varName, EnvironmentVariableTarget.User);
            string? m = ReadOsEnvVar(varName, EnvironmentVariableTarget.Machine);
            return (u, m);
        }).ConfigureAwait(true);

        card.HasSettingsJsonSource = !string.IsNullOrEmpty(settingsValue);
        card.HasOsUserSource = !string.IsNullOrEmpty(userValue);
        card.HasOsMachineSource = !string.IsNullOrEmpty(machineValue);

        // settings.json env wins per Claude Code's runtime resolution order
        // (project-shareable settings take precedence over OS env), so list
        // them in that order in the effective-source label.
        card.EffectiveEnvSourceLabel = card.HasSettingsJsonSource
            ? Strings.LabelEssentialsEnvSourceSettings
            : card.HasOsUserSource
                ? Strings.LabelEssentialsEnvSourceUser
                : card.HasOsMachineSource
                    ? Strings.LabelEssentialsEnvSourceMachine
                    : Strings.LabelEssentialsEnvSourceNone;
    }

    private string? ReadOsEnvVar(string varName, EnvironmentVariableTarget target)
    {
        try
        {
            IDictionary dict = _envProvider.GetVariables(target);
            return dict[varName] as string;
        }
        catch (Exception ex) when (
            ex is SecurityException or
                PlatformNotSupportedException or
                UnauthorizedAccessException)
        {
            // Machine-scope reads on Windows can fail when the registry
            // hive isn't readable to the current user; treat as "not set"
            // rather than crashing the page.
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client = null;
        // Intentionally NOT disposing _refreshGate. A fire-and-forget refresh (from
        // the ctor / nav-tree rebuild) can be parked at the env-var probe holding the
        // gate when the window closes; disposing the semaphore would make that
        // refresh's pending Release (or a later WaitAsync) throw ObjectDisposedException
        // and fault the unobserved task — breaking the documented "safe after disposal"
        // contract. A SemaphoreSlim whose AvailableWaitHandle is never accessed owns no
        // unmanaged handle, so leaving it for GC is the correct, race-free choice.
    }
}