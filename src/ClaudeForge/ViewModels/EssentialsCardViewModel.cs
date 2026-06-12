using System.Collections.ObjectModel;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// discriminator for which editor surface a single
/// Essentials-page card renders.  Kept as a flat enum (rather than a
/// type hierarchy of derived view-models) so the AXAML can switch on
/// it via DataTriggers, and so the card collection is one
/// <see cref="ObservableCollection{T}"/> rather than a polymorphic
/// list.  Add a new variant when promoting a setting whose editor
/// shape doesn't fit the existing four.
/// </summary>
public enum EssentialsCardKind
{
    /// <summary>Tri-state CheckBox: null = inherit, true / false = explicit.</summary>
    Bool,

    /// <summary>NumericUpDown for integer values (token budgets).</summary>
    Int,

    /// <summary>ComboBox for one-of-N string enums (model, effortLevel, autoUpdatesChannel).</summary>
    EnumString,

    /// <summary>Add-row TextBox for a string list (sandbox.allowedDomains).</summary>
    StringList,
}

/// <summary>
/// One pinned setting on the Essentials page.  Owns the value binding,
/// the "why this matters" body, the danger-banner predicate, and the
/// callbacks needed to read/write through the SDK or env accessor.
/// <para>
/// Cards are constructed by <see cref="EssentialsViewModel"/> as a
/// curated list — this class does NOT resolve the value itself;
/// it's plumbed via the
/// <see cref="ReadAsync"/> / <see cref="WriteAsync"/> delegates the
/// orchestrator supplies.  Keeps each card type-agnostic about which
/// underlying accessor it talks to (some go through
/// <c>IPermissionsAccessor</c>, some through <c>IEnvAccessor</c>, some
/// through the generic <c>SetValue</c> escape hatch).
/// </para>
/// </summary>
public partial class EssentialsCardViewModel : ObservableObject
{
    /// <summary>
    /// Card identifier — stable across renames.  Used by the search
    /// integration to deep-link from a synthetic search result to a
    /// specific card.
    /// </summary>
    public string Id { get; }

    /// <summary>Localised title shown at the top of the card.</summary>
    public string Title { get; }

    /// <summary>
    /// Localised body text — the "why this matters" explanation.
    /// Rendered below the title in a muted foreground.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Severity dot colour as a hex string (e.g. <c>#D32F2F</c> for red,
    /// <c>#F4B400</c> for amber, <c>#1976D2</c> for blue).  Red marks
    /// security-critical settings, amber marks cost / quality knobs, blue
    /// marks behaviour flags.  Bind <see cref="SeverityBrush"/> from AXAML;
    /// this property is kept for tests and serialisation parity.
    /// </summary>
    public string SeverityColor { get; }

    /// <summary>
    /// AXAML-bindable brush form of <see cref="SeverityColor"/> — drives
    /// the small coloured circle in the card header.  Frozen at construction
    /// so the same brush instance is reused across refreshes.
    /// </summary>
    public IBrush SeverityBrush { get; }

    /// <summary>
    /// Discriminator for which inline editor surface to render —
    /// CheckBox, NumericUpDown, ComboBox, or list.
    /// </summary>
    public EssentialsCardKind Kind { get; }

    /// <summary>
    /// Title of the schema-driven nav node this card's setting lives
    /// in (e.g. "Permissions" for <c>disableBypassPermissionsMode</c>,
    /// "Environment" for env vars).  Empty string when the setting
    /// has no group home (synthetic / env-only).  Used by the
    /// "View in &lt;group&gt;" deep-link button.
    /// </summary>
    public string ViewInGroupTitle { get; }

    /// <summary>
    /// Localised label rendered on the "View in &lt;group&gt;" button —
    /// pre-formatted at construction time so AXAML can bind a single
    /// string property rather than juggling MultiBinding StringFormat.
    /// </summary>
    public string ViewInGroupLabel { get; }

    /// <summary>
    /// env-var-only flag.  When <see langword="true"/>,
    /// the card renders an additional "effective source" row showing
    /// which of (settings.json env, OS user env, OS machine env) is
    /// contributing.  When <see langword="false"/>, only the
    /// settings.json value is shown.
    /// </summary>
    public bool IsEnvVarCard { get; }

    /// <summary>
    /// Predicate evaluated on every value change to decide whether the
    /// standing red danger banner overlay is shown.  Returns
    /// <see langword="true"/> when the current value is in a known-unsafe
    /// state (e.g. <c>sandbox.enabled = false</c>,
    /// <c>enableAllProjectMcpServers = true</c>).  May be null on cards
    /// that have no danger-state.
    /// </summary>
    private readonly Func<EssentialsCardViewModel, bool>? _isDangerPredicate;

    /// <summary>
    /// Localised body shown inside the standing red danger banner.
    /// Empty when <see cref="_isDangerPredicate"/> is null.
    /// </summary>
    public string DangerBannerText { get; }

    private readonly Func<EssentialsCardViewModel, Task> _readAsync;
    private readonly Func<EssentialsCardViewModel, Task> _writeAsync;

    /// <summary>One-time amber callout shown on first arrival from a synthetic search hit.</summary>
    [ObservableProperty] private bool _showAmberCallout;

    /// <summary>Localised body for the amber callout.</summary>
    public string AmberCalloutText { get; }

    [ObservableProperty] private bool? _boolValue;
    [ObservableProperty] private int? _intValue;
    [ObservableProperty] private string? _enumValue;

    // env-var source attribution (only meaningful when
    // <see cref="IsEnvVarCard"/> is true; on non-env cards these stay
    // at their defaults and the AXAML hides the row entirely).

    /// <summary>True when settings.json's <c>env</c> map contributes a value for this var.</summary>
    [ObservableProperty] private bool _hasSettingsJsonSource;

    /// <summary>True when the OS user-scope env var is set for this var.</summary>
    [ObservableProperty] private bool _hasOsUserSource;

    /// <summary>True when the OS machine-scope env var is set for this var.</summary>
    [ObservableProperty] private bool _hasOsMachineSource;

    /// <summary>
    /// Localised label for the effective contributing source — one of
    /// <c>LabelEssentialsEnvSource{Settings,User,Machine,None}</c>.  Computed by
    /// the orchestrator after each read.
    /// </summary>
    [ObservableProperty] private string _effectiveEnvSourceLabel = string.Empty;

    /// <summary>Available options for <see cref="EssentialsCardKind.EnumString"/>.</summary>
    public IReadOnlyList<string> EnumOptions { get; }

    /// <summary>
    /// When true the EnumString card is editable — the options are
    /// <em>suggestions</em>, not a closed set (e.g. the model card, where a user
    /// may type any custom model id). Strict enums (effortLevel, autoUpdatesChannel)
    /// leave this false.
    /// </summary>
    public bool AllowsFreeForm { get; }

    /// <summary>EnumString card with a closed option set → render a (selection-only) ComboBox.</summary>
    public bool IsStrictEnumString => Kind == EssentialsCardKind.EnumString && !AllowsFreeForm;

    /// <summary>EnumString card whose options are suggestions → render an editable AutoCompleteBox.</summary>
    public bool IsFreeFormEnumString => Kind == EssentialsCardKind.EnumString && AllowsFreeForm;

    /// <summary>
    /// The dropdown's <em>currently offered</em> options. Seeded from
    /// <see cref="EnumOptions"/> and narrowed at runtime for inter-related cards
    /// (e.g. the effort card filtered to the effective model's supported levels).
    /// The strict ComboBox binds to this; other cards leave it == EnumOptions.
    /// </summary>
    public ObservableCollection<string> FilteredOptions { get; }

    /// <summary>True when the editor should be disabled (e.g. effort on a model that exposes none).</summary>
    [ObservableProperty] private bool _enumDisabled;

    /// <summary>A persistent (not auto-dismissing) inline notice — e.g. "effort coerced" / "session-only" / "not applicable".</summary>
    [ObservableProperty] private bool _showConstraintNotice;

    /// <summary>Text for <see cref="ShowConstraintNotice"/>.</summary>
    [ObservableProperty] private string _constraintNoticeText = string.Empty;

    /// <summary>Read-only "current model — supports: …" indicator shown beside an inter-related editor. Empty = hidden.</summary>
    [ObservableProperty] private string _modelSupportSummary = string.Empty;

    /// <summary>
    /// Replace the offered options (used by the orchestrator to narrow an
    /// inter-related dropdown). Guards <see cref="IsLoading"/> across the
    /// mutation because clearing the bound <c>ItemsSource</c> synchronously nulls
    /// a bound ComboBox <c>SelectedItem</c> — without the guard that would fire a
    /// spurious <see cref="EnumValue"/>=null write. Preserves the current
    /// selection when it is still offered.
    /// </summary>
    internal void SetFilteredOptions(IEnumerable<string> options)
    {
        string? keep = EnumValue;
        bool wasLoading = IsLoading;
        IsLoading = true;
        try
        {
            FilteredOptions.Clear();
            foreach (string o in options)
            {
                FilteredOptions.Add(o);
            }

            if (keep is not null && FilteredOptions.Contains(keep, StringComparer.OrdinalIgnoreCase))
            {
                EnumValue = keep; // re-select the value the Clear() nulled, since it's still valid
            }
        }
        finally
        {
            IsLoading = wasLoading;
        }
    }

    /// <summary>Items for <see cref="EssentialsCardKind.StringList"/>.</summary>
    public ObservableCollection<string> StringListValues { get; } = new();

    /// <summary>Bound to the "Add" textbox for <see cref="EssentialsCardKind.StringList"/>.</summary>
    [ObservableProperty] private string _newStringListEntry = string.Empty;

    /// <summary>
    /// <see langword="true"/> when the danger predicate evaluates to true at
    /// the current value.  Drives the red banner's <c>IsVisible</c> binding
    /// in AXAML.
    /// <para>
    /// Maintained imperatively by <see cref="RecomputeIsDanger"/> rather
    /// than as a computed getter, so the notification flows through CTK's
    /// generated <see cref="ObservableProperty"/> infrastructure (which
    /// Avalonia's compiled binding pipeline reliably subscribes to).  An
    /// earlier shape used <c>public bool IsDanger =&gt; predicate(this);</c>
    /// + manual <c>OnPropertyChanged(nameof(IsDanger))</c> calls — that
    /// pattern updated the underlying value but the AXAML didn't re-render
    /// on Linux until a workspace reload (cause unconfirmed; the
    /// imperative flag is more obviously correct).
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isDanger;

    /// <summary>
    /// Recompute <see cref="IsDanger"/> from the predicate at the current
    /// value.  Called from every value-changed partial below + once at
    /// the end of construction so the initial render reflects on-disk state.
    /// </summary>
    private void RecomputeIsDanger()
    {
        IsDanger = _isDangerPredicate?.Invoke(this) ?? false;
    }

    /// <summary>
    /// Suppression flag — set during ReadAsync so writes triggered by
    /// the value-changed partial methods don't recurse through the
    /// accessor on every UI binding update.
    /// </summary>
    internal bool IsLoading { get; set; }

    public EssentialsCardViewModel(
        string id,
        string title,
        string body,
        string severityColor,
        EssentialsCardKind kind,
        string viewInGroupTitle,
        bool isEnvVarCard,
        Func<EssentialsCardViewModel, Task> readAsync,
        Func<EssentialsCardViewModel, Task> writeAsync,
        IReadOnlyList<string>? enumOptions = null,
        Func<EssentialsCardViewModel, bool>? isDangerPredicate = null,
        string dangerBannerText = "",
        string amberCalloutText = "",
        bool allowsFreeForm = false)
    {
        Id = id;
        Title = title;
        Body = body;
        SeverityColor = severityColor;
        // Parse once at construction; SolidColorBrush is immutable enough for our use.
        // Falls back to neutral grey if the hex string can't be parsed (e.g. a future
        // hand-edited palette change accidentally drops a "#" prefix).
        SeverityBrush = Color.TryParse(severityColor, out Color c)
            ? new SolidColorBrush(c)
            : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        Kind = kind;
        ViewInGroupTitle = viewInGroupTitle;
        ViewInGroupLabel = string.IsNullOrEmpty(viewInGroupTitle)
            ? string.Empty
            : string.Format(
                Strings.LabelEssentialsViewInGroupFmt,
                viewInGroupTitle);
        IsEnvVarCard = isEnvVarCard;
        _readAsync = readAsync;
        _writeAsync = writeAsync;
        EnumOptions = enumOptions ?? Array.Empty<string>();
        FilteredOptions = new ObservableCollection<string>(EnumOptions);
        AllowsFreeForm = allowsFreeForm;
        _isDangerPredicate = isDangerPredicate;
        DangerBannerText = dangerBannerText;
        AmberCalloutText = amberCalloutText;

        // Hook collection-changed on the StringList so adds/removes
        // route to the writer + danger-predicate refresh.
        StringListValues.CollectionChanged += (_, _) =>
        {
            if (IsLoading)
            {
                return;
            }

            RecomputeIsDanger();
            _ = WriteAsync();
        };

        // Initial recompute — the value-changed partials only fire on
        // subsequent mutations, so without this the IsDanger flag would
        // start out stale (always false) until the first user edit.
        RecomputeIsDanger();
    }

    /// <summary>
    /// Refresh the card's value bindings from the underlying accessor.
    /// Called once at construction time and again on workspace reload.
    /// </summary>
    public Task ReadAsync()
    {
        return _readAsync(this);
    }

    /// <summary>
    /// Persist the card's current value to the underlying accessor.
    /// Called on every value change (after the IsLoading guard clears).
    /// </summary>
    public Task WriteAsync()
    {
        return _writeAsync(this);
    }

    // ── Value-changed routers (auto-write) ────────────────────────────

    partial void OnBoolValueChanged(bool? value)
    {
        // Permanent audit log — see OnIntValueChanged for rationale.
        Log.Information("[Essentials.UserEdit] card={Id} kind=Bool value={Value} suppressed={Suppressed}",
            Id, value?.ToString() ?? "(null)", IsLoading);
        // RecomputeIsDanger fires unconditionally — the read path also
        // benefits from a fresh danger flag after each load, and
        // RecomputeIsDanger is cheap (predicate invocation + property
        // change with equality check).  WriteAsync stays guarded by
        // IsLoading so the read-path doesn't recurse through the SDK.
        RecomputeIsDanger();
        if (IsLoading)
        {
            return;
        }

        _ = WriteAsync();
    }

    partial void OnIntValueChanged(int? value)
    {
        // Permanent audit log of every user-driven card mutation on the
        // Essentials page, mirroring the [Editor.UserEdit] log line emitted
        // by SettingsGroupEditorViewModel.OnEditorPropertyChanged for the
        // schema-driven group editors.  Together these give a complete
        // pre-save trail of "what did the user actually change?" so that
        // post-mortems of disappeared-edit reports have the data without
        // requiring a repro under the debugger.  IsLoading is logged so
        // suppressed write-backs from the read-path guard are visible.
        Log.Information("[Essentials.UserEdit] card={Id} kind=Int value={Value} suppressed={Suppressed}",
            Id, value, IsLoading);
        RecomputeIsDanger();
        if (IsLoading)
        {
            return;
        }

        _ = WriteAsync();
    }

    partial void OnEnumValueChanged(string? value)
    {
        Log.Information("[Essentials.UserEdit] card={Id} kind=Enum value={Value} suppressed={Suppressed}",
            Id, value ?? "(null)", IsLoading);
        RecomputeIsDanger();
        if (IsLoading)
        {
            return;
        }

        _ = WriteAsync();
    }

    // ── StringList add/remove commands ────────────────────────────────

    [RelayCommand]
    private void AddStringListEntry()
    {
        string v = (NewStringListEntry ?? string.Empty).Trim();
        if (v.Length == 0 || StringListValues.Contains(v))
        {
            return;
        }

        Log.Information("[Essentials.UserEdit] card={Id} kind=StringList action=Add entry=\"{Entry}\"", Id, v);
        StringListValues.Add(v);
        NewStringListEntry = string.Empty;
    }

    [RelayCommand]
    private void RemoveStringListEntry(string? entry)
    {
        if (entry is null)
        {
            return;
        }

        Log.Information("[Essentials.UserEdit] card={Id} kind=StringList action=Remove entry=\"{Entry}\"", Id, entry);
        StringListValues.Remove(entry);
    }

    /// <summary>
    /// Activate the one-time amber callout — called by MWVM when the
    /// user arrives at this card via a synthetic search result.  The
    /// callout auto-dismisses on the first value mutation.
    /// </summary>
    public void ActivateAmberCallout()
    {
        ShowAmberCallout = true;
    }

    /// <summary>
    /// "View in &lt;group&gt;" deep-link — publishes a
    /// <see cref="NavigateToNavGroupMessage"/> that MWVM receives and
    /// translates into a nav-tree selection change.  The optional
    /// <see cref="JsonPathFilter"/> (set by the orchestrator at card
    /// construction) carries the dotted JSON path so the target editor
    /// can highlight / filter to the matched property on arrival.
    /// </summary>
    [RelayCommand]
    private void ViewInGroup()
    {
        if (string.IsNullOrEmpty(ViewInGroupTitle))
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(
            new NavigateToNavGroupMessage(ViewInGroupTitle, JsonPathFilter));
    }

    /// <summary>
    /// Optional dotted JSON path (e.g. <c>"sandbox.network.allowedDomains"</c>)
    /// applied as a filter on the target editor when the user clicks
    /// "View in &lt;group&gt;".  Set by the orchestrator at construction
    /// for cards whose home group is a schema-driven settings page; left
    /// empty for cards that target a non-schema editor (Environment).
    /// </summary>
    public string JsonPathFilter { get; init; } = string.Empty;
}