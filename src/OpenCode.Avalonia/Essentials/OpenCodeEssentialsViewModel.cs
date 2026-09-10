using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;
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
/// them. The curation itself is in the <c>Cards</c> partial.
/// </para>
/// <para>
/// ⭐⭐ <b>A card's severity is READ FROM THE DANGER TABLE, never written here.</b> Both surfaces
/// show a dot for the same key, and one literal per surface agreed only by vigilance — which had
/// already failed: slice 2's <c>autoupdate</c> card said <c>Neutral</c> while the table said
/// <c>Info</c>, so the Essentials page and the settings page disagreed about the same setting.
/// See <see cref="SeverityFor"/>.
/// </para>
/// </remarks>
public sealed partial class OpenCodeEssentialsViewModel : ObservableObject, INavigablePage
{
    private readonly AgentConfigClientCore? _client;
    private readonly OpenCodeEnvironment _environment;
    private readonly SchemaPageLayout _layout;
    private readonly IDangerClassifier? _danger;
    private readonly IReadOnlyList<EssentialsAppPreference> _appPreferences;

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
    /// ⭐ Passed in rather than a literal here. The page a key lands on is the layout's decision,
    /// and a copy of it in this file would keep pointing at the old page after the table moved the
    /// key — with a button that silently navigates nowhere.
    /// </para>
    /// </param>
    /// <param name="danger">
    /// This document's danger table. Supplies every card's severity; <see langword="null"/> leaves
    /// them all <see cref="AppSeverity.Neutral"/>, which is what a host with no table should show.
    /// </param>
    /// <param name="appPreferences">
    /// App-level booleans the host wants shown alongside the schema-backed cards, or
    /// <see langword="null"/>/empty for none.
    /// <para>
    /// ⚠ These are NOT part of the document being edited — they live in the host's own state and
    /// the host supplies the accessors and the text. See <see cref="EssentialsAppPreference"/> for
    /// why they cannot simply be read from here.
    /// </para>
    /// </param>
    public OpenCodeEssentialsViewModel(
        AgentConfigClientCore? client,
        OpenCodeEnvironment environment,
        SchemaPageLayout layout,
        IDangerClassifier? danger = null,
        IReadOnlyList<EssentialsAppPreference>? appPreferences = null)
    {
        _client = client;
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _danger = danger;
        _appPreferences = appPreferences ?? [];

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
    /// Re-read on arrival, because several of these cards report the filesystem or the environment
    /// rather than the document.
    /// </summary>
    /// <remarks>
    /// A global <c>AGENTS.md</c> can appear while the app is open — the user creating one is a
    /// likely consequence of having just read that card. The environment variables cannot change
    /// within the process, but re-reading them costs nothing and keeps one rule instead of two.
    /// </remarks>
    public void OnNavigatedTo() => _ = RefreshAsync();

    /// <summary>Look up a card by its id.</summary>
    public EssentialsCardViewModel? GetCardById(string id)
        => Cards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>The page title a top-level key's editor lives on.</summary>
    private string PageTitleFor(string key)
        => _layout.PropertyToPage.TryGetValue(key, out string? page) ? page : _layout.FallbackPage;

    /// <summary>
    /// The standing tier the danger table assigns <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Classified with a null scope and a null value on purpose.</b> That asks for the
    /// setting's BASE tier — what this knob is worth in general — rather than an assessment of the
    /// value currently in the file. The card's standing dot must not flicker as the user edits;
    /// the value-sensitive half of the table is what <c>IsDangerNow</c> is for, and the cards
    /// carry their own predicates for the banner. The interface contract guarantees a null scope
    /// never RAISES severity, which is what makes this safe to read as a floor.
    /// </remarks>
    private AppSeverity SeverityFor(string path)
        => _danger?.Classify(path, scope: null, currentValue: null).Severity ?? AppSeverity.Neutral;

    // ── Value plumbing ────────────────────────────────────────────────
    //
    // ⚠ Every delegate below is SYNCHRONOUS, and that is load-bearing: IsLoading must not span an
    // await, or a user edit landing during the continuation is silently discarded. The guard test
    // the plan re-asserts each phase (…_NotSuppressed_WhileReadIsInAsyncPhase) exists for exactly
    // this bug class, and keeping the reads synchronous is how this page stays out of its way.

    private JsonNode? Effective(string path)
        => _client?.GetEffective<JsonNode>(path);

    private Func<EssentialsCardViewModel, Task> ReadBool(string path) => card =>
    {
        card.IsLoading = true;
        try
        {
            card.BoolValue = Effective(path) is JsonValue v && v.TryGetValue(out bool b) ? b : null;
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> WriteBool(string path) => card =>
    {
        if (_client is null)
        {
            return Task.CompletedTask;
        }

        if (card.BoolValue is not { } value)
        {
            // Null is the tri-state CheckBox's "inherit" — remove the key rather than writing a
            // literal, so a lower-priority scope decides again.
            _client.RemoveValue(path, _client.DefaultScope);
        }
        else if (Effective(path) is not JsonValue v || !v.TryGetValue(out bool current) || current != value)
        {
            _client.SetValue(path, value, _client.DefaultScope);
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> ReadInt(string path) => card =>
    {
        card.IsLoading = true;
        try
        {
            card.IntValue = Effective(path) is JsonValue v && v.TryGetValue(out int i) ? i : null;
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> WriteInt(string path) => card =>
    {
        if (_client is null)
        {
            return Task.CompletedTask;
        }

        if (card.IntValue is not { } value)
        {
            _client.RemoveValue(path, _client.DefaultScope);
        }
        else if (Effective(path) is not JsonValue v || !v.TryGetValue(out int current) || current != value)
        {
            _client.SetValue(path, value, _client.DefaultScope);
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> ReadString(string path) => card =>
    {
        card.IsLoading = true;
        try
        {
            card.EnumValue = Effective(path) is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> WriteString(string path) => card =>
    {
        if (_client is null)
        {
            return Task.CompletedTask;
        }

        string? value = card.EnumValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Blank means "unset", never a literal empty string — a free-form box the user cleared
            // must remove the key, not pin model="".
            _client.RemoveValue(path, _client.DefaultScope);
        }
        else if (Effective(path) is not JsonValue v
                 || !v.TryGetValue(out string? current)
                 || !string.Equals(current, value, StringComparison.Ordinal))
        {
            // Ghost-change guard: re-emitting the already-effective value would pin a redundant
            // key and light the Save banner over an empty diff.
            _client.SetValue(path, value, _client.DefaultScope);
        }

        return Task.CompletedTask;
    };

    // ── permission ────────────────────────────────────────────────────

    /// <summary>
    /// The parsed <c>permission</c> value, or <see langword="null"/> when it is a shape OpenCode
    /// itself would reject.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="OpenCodePermissionModel.Parse"/> THROWS on an invalid shape rather than
    /// dropping the offending entry — deliberately, because a permission rule that silently
    /// disappears is one the user believes is protecting them. A summary card must not propagate
    /// that out of a fire-and-forget read, so it is caught here and surfaced as "cannot read this"
    /// on the card instead.
    /// </remarks>
    private OpenCodePermissionModel? ReadPermissionModel()
    {
        try
        {
            return OpenCodePermissionModel.Parse(Effective(PermissionKey));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? WireOrNull(PermissionOutcome? action)
        => action is { } a && a != PermissionOutcome.Default
            ? OpenCodePermissionModel.ToWireString(a)
            : null;

    /// <summary>
    /// Put a permission card into a state where it shows a value but refuses to write it.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>This is a data-loss guard, not a cosmetic one.</b> <c>permission</c> is
    /// <c>anyOf[bare action, per-tool object]</c>. When the file holds the BARE form, writing
    /// <c>permission.bash</c> would replace that string with an object and <b>silently delete the
    /// global rule covering every other tool</b>. When a tool holds per-pattern rules, writing a
    /// bare action would discard the whole ordered rule list — and order is semantics here. Both
    /// are unrecoverable from the UI, so the card explains and stands down.
    /// </remarks>
    private static void StandDown(EssentialsCardViewModel card, string notice)
    {
        card.EnumDisabled = true;
        card.ShowConstraintNotice = true;
        card.ConstraintNoticeText = notice;
    }

    private static void StandUp(EssentialsCardViewModel card)
    {
        card.EnumDisabled = false;
        card.ShowConstraintNotice = false;
        card.ConstraintNoticeText = string.Empty;
    }

    /// <summary>Read the whole-<c>permission</c> card (the bare global action).</summary>
    private Task ReadGlobalPermission(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            OpenCodePermissionModel? model = ReadPermissionModel();
            if (model is null)
            {
                card.EnumValue = null;
                StandDown(card, Strings.EssentialsPermissionUnreadable);
            }
            else if (model.Tools.Count > 0)
            {
                // Per-tool rules are configured, so there is no single global action to show and
                // writing one would delete them.
                card.EnumValue = null;
                StandDown(card, Strings.EssentialsPermissionPerToolConfigured);
            }
            else
            {
                card.EnumValue = WireOrNull(model.GlobalAction);
                StandUp(card);
            }
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    }

    private Task WriteGlobalPermission(EssentialsCardViewModel card)
    {
        if (_client is null || card.EnumDisabled)
        {
            return Task.CompletedTask;
        }

        _ = WriteString(PermissionKey)(card);
        return RefreshPermissionCards();
    }

    /// <summary>
    /// Re-read all six <c>permission</c> cards, because one card's write changes which of the
    /// others may safely write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>Without this the whole interlock is defeatable in one visit, by two ordinary
    /// clicks.</b> The interlock is decided in <see cref="ReadPermissionModel"/> at read time, and
    /// cards are otherwise read on construction and on arrival — so setting the global action left
    /// the five tool cards holding the <c>EnumDisabled</c> they had computed *before* it. Editing
    /// one then replaced the bare string with an object and deleted the global rule covering every
    /// tool without a card. Found by re-reading the finished slice; nothing was asserting it.
    /// </para>
    /// <para>
    /// ⚠ <b>This cannot recurse.</b> Every permission read sets <see cref="EssentialsCardViewModel.IsLoading"/>
    /// around its assignment, and the value-changed routers return early while it is set — so the
    /// re-read that lands on the card currently being written raises no second write. The reads are
    /// synchronous, so the group is consistent before this returns.
    /// </para>
    /// </remarks>
    private Task RefreshPermissionCards()
    {
        foreach (EssentialsCardViewModel card in Cards)
        {
            if (PermissionCardIds.Contains(card.Id, StringComparer.Ordinal))
            {
                _ = card.ReadAsync();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Read one tool's entry under <c>permission</c>.</summary>
    private Func<EssentialsCardViewModel, Task> ReadToolPermission(string tool) => card =>
    {
        card.IsLoading = true;
        try
        {
            OpenCodePermissionModel? model = ReadPermissionModel();
            if (model is null)
            {
                card.EnumValue = null;
                StandDown(card, Strings.EssentialsPermissionUnreadable);
            }
            else if (model.GlobalAction is not null)
            {
                // A bare global action covers this tool. Writing here would replace the string
                // with an object and drop the global rule for every OTHER tool.
                card.EnumValue = null;
                StandDown(card, Strings.EssentialsPermissionGlobalInForce);
            }
            else
            {
                OpenCodeToolPermission? entry = model.Tools
                    .FirstOrDefault(t => string.Equals(t.Key, tool, StringComparison.Ordinal)).Value;

                if (entry is { Rules.Count: > 0 })
                {
                    // Ordered per-pattern rules; a bare action would discard them, and the order
                    // is load-bearing (last match wins).
                    card.EnumValue = null;
                    StandDown(card, Strings.EssentialsPermissionPatternRules);
                }
                else
                {
                    card.EnumValue = WireOrNull(entry?.SingleAction);
                    StandUp(card);
                }
            }
        }
        finally
        {
            card.IsLoading = false;
        }

        return Task.CompletedTask;
    };

    private Func<EssentialsCardViewModel, Task> WriteToolPermission(string tool) => card =>
    {
        if (_client is null || card.EnumDisabled)
        {
            return Task.CompletedTask;
        }

        _ = WriteString($"{PermissionKey}.{tool}")(card);
        return RefreshPermissionCards();
    };

    /// <summary>True when a permission card currently shows the unsafe action.</summary>
    private static bool IsAllow(EssentialsCardViewModel card)
        => string.Equals(card.EnumValue, "allow", StringComparison.Ordinal);

    // ── model / provider gating ───────────────────────────────────────

    /// <summary>
    /// True when the pinned model names a provider this configuration has switched off.
    /// </summary>
    /// <remarks>
    /// Two ways to be off, and both are real: named in <c>disabled_providers</c>, or absent from a
    /// non-empty <c>enabled_providers</c>, which the schema describes as "ONLY these providers
    /// will be enabled". A model whose provider is off does not run, and nothing else on the page
    /// would say so.
    /// </remarks>
    private bool IsProviderDisabled(EssentialsCardViewModel card)
    {
        if (card.EnumValue is not { Length: > 0 } value)
        {
            return false;
        }

        int slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            // Not in provider/model form — nothing to check, and complaining about it is the
            // schema validator's job, not this card's.
            return false;
        }

        string provider = value[..slash];

        if (StringsAt("disabled_providers").Contains(provider, StringComparer.Ordinal))
        {
            return true;
        }

        IReadOnlyList<string> enabled = StringsAt("enabled_providers");
        return enabled.Count > 0 && !enabled.Contains(provider, StringComparer.Ordinal);
    }

    /// <summary>The string elements of an array-valued key, skipping anything that is not one.</summary>
    private IReadOnlyList<string> StringsAt(string path)
    {
        if (Effective(path) is not JsonArray arr)
        {
            return [];
        }

        List<string> result = [];
        foreach (JsonNode? n in arr)
        {
            if (n is JsonValue v && v.TryGetValue(out string? s) && s is not null)
            {
                result.Add(s);
            }
        }

        return result;
    }

    /// <summary>The agent names this configuration defines, for the default-agent suggestions.</summary>
    private IReadOnlyList<string> ConfiguredAgentNames()
        => Effective("agent") is JsonObject obj
            ? [.. obj.Select(kv => kv.Key)]
            : [];

    // ── autoupdate ────────────────────────────────────────────────────

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

        JsonNode? node = Effective(AutoupdatePath);

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

        // Ghost-change guard, as on every other card.
        if (ReadAutoupdateConfig().Mode != mode)
        {
            _client.SetValue(AutoupdatePath, value, _client.DefaultScope);
        }

        return Task.CompletedTask;
    }

    // ── Derived resolver reports ──────────────────────────────────────

    private Task ReadRules(EssentialsCardViewModel card)
    {
        card.IsLoading = true;
        try
        {
            string globalRules = Path.Combine(OpenCodePaths.GlobalDirectory(_environment), "AGENTS.md");
            card.DerivedText = File.Exists(globalRules)
                ? Format(Strings.EssentialsRulesGlobalFmt, globalRules)
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
        // resolved paths rather than trusting the variable keeps
        // OPENCODE_CONFIG_DIR=~/.config/opencode from warning about a file shadowing itself.
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
            return Format(Strings.EssentialsActiveConfigFromVarFmt, explicitPath, "OPENCODE_CONFIG");
        }

        string path = OpenCodePaths.GlobalConfigPath(_environment);

        return _environment.ConfigDir is null
            ? path
            : Format(Strings.EssentialsActiveConfigFromVarFmt, path, "OPENCODE_CONFIG_DIR");
    }

    private static string Format(string format, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, format, args);
}
