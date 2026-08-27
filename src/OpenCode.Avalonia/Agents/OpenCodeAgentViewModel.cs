using System.Globalization;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Permissions;
using Bennewitz.Ninja.OpenCode.Sdk.Agents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Agents;

/// <summary>
/// One entry of the <c>agent</c> map.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Every field is nullable, and that is the whole design.</b> An entry is usually a partial
/// override of one of the seven built-ins, so an untouched control must leave its key absent rather
/// than write a default. A <see cref="double"/> temperature box bound to a non-nullable zero would
/// override the model's own temperature on the first save of any unrelated field.
/// </para>
/// <para>
/// ⭐ <b>The <c>permission</c> override is the real permission grid, not a copy.</b> The schema
/// types it as a bare <c>$ref</c> to the same <c>PermissionConfig</c> the top-level key uses, so the
/// child gets shadow detection, the last-match-wins reordering and the live tester for free — and
/// there is no second permission implementation to drift. <c>NestedEditorValue</c> is the small
/// adapter that lets a compound editor load from one value inside another.
/// </para>
/// </remarks>
public sealed partial class OpenCodeAgentViewModel : ObservableObject
{
    /// <summary>The invocation modes, for binding a selector.</summary>
    public static IReadOnlyList<OpenCodeAgentMode> Modes { get; } =
        [OpenCodeAgentMode.Unset, OpenCodeAgentMode.Subagent, OpenCodeAgentMode.Primary, OpenCodeAgentMode.All];

    /// <summary>
    /// The theme role names the schema suggests for <c>color</c>.
    /// </summary>
    /// <remarks>
    /// Suggestions, not a constraint: the union's other arm is a bare <c>string</c> with no
    /// pattern, so a hex value is equally valid. Offered as a dropdown that also accepts free text,
    /// the same shape the <c>model</c> field uses.
    /// </remarks>
    public static IReadOnlyList<string> ColorSuggestions { get; } =
        ["primary", "secondary", "accent", "success", "warning", "error", "info"];

    private readonly Action<OpenCodeAgentViewModel>? _onRemove;

    private IReadOnlyList<KeyValuePair<string, object?>> _extras = [];
    private object? _options;
    private object? _raw;
    private bool _isOpaque;

    /// <summary>Creates an entry named <paramref name="name"/>.</summary>
    public OpenCodeAgentViewModel(string name, Action<OpenCodeAgentViewModel>? onRemove = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _onRemove = onRemove;
        Tools = new OpenCodeAgentToolListViewModel();
    }

    /// <summary>The agent key, exactly as written.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuiltIn))]
    [NotifyPropertyChangedFor(nameof(IsUserDefined))]
    private string _name;

    /// <summary>
    /// True when this name is one of the seven built-ins, so the entry overrides shipped behaviour.
    /// </summary>
    /// <remarks>
    /// Worth showing: overriding <c>build</c> changes how the tool behaves for every session,
    /// whereas adding <c>my-agent</c> only adds a choice. The schema draws the same distinction —
    /// named properties beside an <c>additionalProperties</c> of the same type.
    /// </remarks>
    public bool IsBuiltIn => OpenCodeBuiltInAgents.Names.Contains(Name);

    /// <summary>Inverse of <see cref="IsBuiltIn"/>, for binding.</summary>
    public bool IsUserDefined => !IsBuiltIn;

    /// <summary>True when the entry was not an object and is held verbatim.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isOpaqueEntry;

    /// <summary>True when this entry has editable fields.</summary>
    public bool IsEditable => !IsOpaqueEntry;

    /// <summary>Model id. Free-form.</summary>
    [ObservableProperty] private string _model = string.Empty;

    /// <summary>Model variant. Free-form.</summary>
    [ObservableProperty] private string _variant = string.Empty;

    /// <summary>Sampling temperature as typed. Empty means the key stays absent.</summary>
    /// <remarks>
    /// ⚠ <b>Text, not a numeric control, and the reason is this editor's central property.</b>
    /// "Absent" and "0" are different claims — an agent entry is a partial override, so an
    /// untouched temperature must write nothing, while an explicit <c>0</c> must write zero. A
    /// spinner's empty state is a nullable-to-<see cref="decimal"/> binding conversion whose
    /// runtime behaviour cannot be checked from a view-model test, and the failure mode is a box
    /// that silently reads as empty. Parsing here makes "empty leaves the key out" literal, and
    /// keeps unparseable input visible instead of rounding it to a number.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumberError))]
    private string _temperatureText = string.Empty;

    /// <summary>Nucleus-sampling probability as typed. Empty means the key stays absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumberError))]
    private string _topPText = string.Empty;

    /// <summary>System prompt.</summary>
    [ObservableProperty] private string _prompt = string.Empty;

    /// <summary>Human-readable description.</summary>
    [ObservableProperty] private string _description = string.Empty;

    /// <summary>How the agent may be invoked.</summary>
    [ObservableProperty] private OpenCodeAgentMode _mode = OpenCodeAgentMode.Unset;

    /// <summary>Whether the agent is disabled; null leaves the key out.</summary>
    [ObservableProperty] private bool? _disable;

    /// <summary>Whether the agent is hidden; null leaves the key out.</summary>
    [ObservableProperty] private bool? _hidden;

    /// <summary>Display colour: a theme role name or any other string.</summary>
    [ObservableProperty] private string _color = string.Empty;

    /// <summary>Step budget as typed. Empty means the key stays absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumberError))]
    private string _stepsText = string.Empty;

    /// <summary>Maximum steps as typed. Empty means the key stays absent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumberError))]
    private string _maxStepsText = string.Empty;

    /// <summary>
    /// True when a numeric box holds something that is not a number.
    /// </summary>
    /// <remarks>
    /// ⚠ While set, <see cref="ToModel"/> keeps the field's LAST GOOD value rather than dropping
    /// it. Half-typed input is normal — "0." on the way to "0.5" — and treating every intermediate
    /// keystroke as "unset" would delete the user's setting on a live-write host before they
    /// finished typing it.
    /// </remarks>
    public bool HasNumberError =>
        !TryNumber(TemperatureText, out _)
        || !TryNumber(TopPText, out _)
        || !TryInteger(StepsText, out _)
        || !TryInteger(MaxStepsText, out _);

    private double? _lastGoodTemperature;
    private double? _lastGoodTopP;
    private long? _lastGoodSteps;
    private long? _lastGoodMaxSteps;

    /// <summary>Parse a nullable number; blank is a valid "absent".</summary>
    private static bool TryNumber(string text, out double? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Parse a nullable integer; blank is a valid "absent".</summary>
    private static bool TryInteger(string text, out long? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out long parsed)
            || long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string Format(double? value) =>
        value is { } d ? d.ToString(CultureInfo.CurrentCulture) : string.Empty;

    private static string Format(long? value) =>
        value is { } l ? l.ToString(CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>The deprecated per-tool enable flags.</summary>
    public OpenCodeAgentToolListViewModel Tools { get; }

    /// <summary>
    /// True when this entry still carries the deprecated <c>tools</c> map.
    /// </summary>
    /// <remarks>
    /// The schema's own description reads "@deprecated Use 'permission' field instead". Shown only
    /// when the user already has one — an empty deprecated section invites filling it in.
    /// </remarks>
    [ObservableProperty] private bool _hasDeprecatedTools;

    /// <summary>
    /// The agent's permission override, edited by the real permission grid.
    /// </summary>
    [ObservableProperty] private OpenCodePermissionEditorViewModel? _permission;

    /// <summary>True when a permission override is being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPermissionOverride))]
    private bool _hasPermissionOverride;

    /// <summary>Inverse of <see cref="HasPermissionOverride"/>, for binding.</summary>
    public bool HasNoPermissionOverride => !HasPermissionOverride;

    /// <summary>
    /// The global <c>permission</c> value this agent's override sits on top of, rendered read-only.
    /// </summary>
    /// <remarks>
    /// Empty when there is no global value or it could not be summarised. The point is that an
    /// agent override is <i>partial</i> — a user reading only the agent's grid cannot tell what the
    /// agent will actually be allowed to do.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalPermissionContext))]
    private string _globalPermissionSummary = string.Empty;

    /// <summary>True when <see cref="GlobalPermissionSummary"/> has something to show.</summary>
    public bool HasGlobalPermissionContext => GlobalPermissionSummary.Length > 0;

    /// <summary>Remove this agent from the map.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>Start editing a permission override for this agent.</summary>
    [RelayCommand]
    private void AddPermissionOverride()
    {
        if (HasPermissionOverride)
        {
            return;
        }

        BuildPermissionChild(value: null, defined: false);
        HasPermissionOverride = true;
    }

    /// <summary>
    /// Drop this agent's permission override entirely, so it inherits the global rules.
    /// </summary>
    /// <remarks>
    /// Distinct from clearing the grid to an empty object: an empty <c>permission</c> is a stated
    /// override of nothing, whereas an absent key means "whatever the global rules say".
    /// </remarks>
    [RelayCommand]
    private void RemovePermissionOverride()
    {
        Permission = null;
        HasPermissionOverride = false;
    }

    private IEditorScope? _scope;

    /// <summary>Populate from a parsed entry.</summary>
    /// <param name="agent">The parsed config.</param>
    /// <param name="scope">The scope being edited, for the nested permission grid.</param>
    /// <param name="globalPermissionSummary">
    /// A read-only summary of the global permission value, or empty when there is none.
    /// </param>
    internal void Load(
        OpenCodeAgentConfig agent,
        IEditorScope scope,
        string globalPermissionSummary)
    {
        _scope = scope;
        _extras = agent.Extras;
        _options = agent.Options;
        _raw = agent.Raw;
        _isOpaque = agent.IsOpaque;

        IsOpaqueEntry = agent.IsOpaque;
        Model = agent.Model ?? string.Empty;
        Variant = agent.Variant ?? string.Empty;
        TemperatureText = Format(agent.Temperature);
        TopPText = Format(agent.TopP);
        _lastGoodTemperature = agent.Temperature;
        _lastGoodTopP = agent.TopP;
        Prompt = agent.Prompt ?? string.Empty;
        Description = agent.Description ?? string.Empty;
        Mode = agent.Mode;
        Disable = agent.Disable;
        Hidden = agent.Hidden;
        Color = agent.Color ?? string.Empty;
        StepsText = Format(agent.Steps);
        MaxStepsText = Format(agent.MaxSteps);
        _lastGoodSteps = agent.Steps;
        _lastGoodMaxSteps = agent.MaxSteps;

        Tools.Reset(agent.Tools);
        HasDeprecatedTools = agent.Tools.Count > 0;

        GlobalPermissionSummary = globalPermissionSummary;

        if (agent.Permission is not null)
        {
            BuildPermissionChild(agent.Permission, defined: true);
            HasPermissionOverride = true;
        }
        else
        {
            Permission = null;
            HasPermissionOverride = false;
        }
    }

    private void BuildPermissionChild(object? value, bool defined)
    {
        if (_scope is not { } scope)
        {
            return;
        }

        OpenCodePermissionEditorViewModel child = new(
            new NestedEditorSchema(
                name: "permission",
                path: $"agent.{Name}.permission",
                title: "Permission override",
                description: "Applies on top of the global permission rules for this agent only."),
            scope);

        child.LoadFromValue(
            new NestedEditorValue($"agent.{Name}.permission", scope, value, defined),
            scope);

        Permission = child;
    }

    /// <summary>The model form of this entry.</summary>
    internal OpenCodeAgentConfig ToModel()
    {
        if (_isOpaque)
        {
            return new OpenCodeAgentConfig { IsOpaque = true, Raw = _raw };
        }

        return new OpenCodeAgentConfig
        {
            Model = NullIfBlank(Model),
            Variant = NullIfBlank(Variant),
            Temperature = Resolve(TemperatureText, ref _lastGoodTemperature),
            TopP = Resolve(TopPText, ref _lastGoodTopP),
            Prompt = NullIfBlank(Prompt),
            Tools = Tools.ToPairs(),
            Disable = Disable,
            Description = NullIfBlank(Description),
            Mode = Mode,
            Hidden = Hidden,
            Options = _options,
            Color = NullIfBlank(Color),
            Steps = Resolve(StepsText, ref _lastGoodSteps),
            MaxSteps = Resolve(MaxStepsText, ref _lastGoodMaxSteps),
            // The child grid returns null when it holds nothing, which is exactly the signal to
            // leave the key out — so an override cleared to empty stops overriding.
            Permission = HasPermissionOverride ? Permission?.ToValue() : null,
            Extras = _extras,
        };
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <remarks>
    /// Unparseable text keeps the last good value instead of writing null. See
    /// <see cref="HasNumberError"/> for why: mid-typing states are normal, and treating them as
    /// "unset" deletes the setting on a live-write host.
    /// </remarks>
    private static double? Resolve(string text, ref double? lastGood)
    {
        if (TryNumber(text, out double? parsed))
        {
            lastGood = parsed;
            return parsed;
        }

        return lastGood;
    }

    private static long? Resolve(string text, ref long? lastGood)
    {
        if (TryInteger(text, out long? parsed))
        {
            lastGood = parsed;
            return parsed;
        }

        return lastGood;
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsBuiltIn));
        OnPropertyChanged(nameof(IsUserDefined));
    }
}
