using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Permissions;

/// <summary>
/// Editor for OpenCode's <c>permission</c> setting: a tool × pattern grid whose row order is the
/// policy, plus the bare-action mode that applies to every tool.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not the generic object editor.</b> To the schema this value is an object of
/// strings, and the generic dispatch renders it faithfully as such. But the meaning is not in the
/// JSON type: within a tool the <b>last matching rule wins</b>, so the key order <i>is</i> the
/// policy, and a UI that presents an unordered set of key/value pairs gives the user no way to
/// see or change the only thing that decides the outcome. A grid with explicit
/// weaker/stronger movement is the smallest honest presentation.
/// </para>
/// <para>
/// <b>Three things it surfaces that no generic editor can.</b> Which rules are inert because a
/// later, broader rule covers them (the merge-inversion hazard — a narrow <c>deny</c> from a
/// lower-priority file lands before a broad rule from a higher one and quietly stops applying);
/// which tools reject pattern rules outright; and what OpenCode would actually decide for a
/// given command, from <see cref="OpenCodePermissionModel.Resolve"/> rather than from the user's
/// reading of their own globs.
/// </para>
/// <para>
/// ⚠ <b>A value it could not parse is echoed back unchanged.</b> The parser rejects shapes
/// OpenCode itself would reject, and the tempting response — show what parsed and drop the rest —
/// would delete permission rules the user believes are protecting them. So an unparseable value
/// puts the editor in a read-through state, keeps the original, and says why.
/// </para>
/// <para>
/// Follows the compound-editor contract in
/// <c>src/ClaudeForge/ViewModels/Editors/AGENTS.md</c>: force-fire <c>MarkModified</c>, an
/// <c>_isLoading</c> guard (collections are subscribed in the constructor), a value of
/// <see langword="null"/> when empty so the key is removed rather than written empty, and
/// transient input fields filtered out of the modified signal.
/// </para>
/// </remarks>
public sealed partial class OpenCodePermissionEditorViewModel : PropertyEditorViewModel
{
    private bool _isLoading;
    private IEditorValue? _lastValue;
    private IEditorScope? _lastScope;

    /// <summary>
    /// The value as loaded, kept only when it could not be parsed. Non-null means
    /// <see cref="ToValue"/> echoes rather than serialises.
    /// </summary>
    private object? _unparsedValue;

    private bool _hasUnparsedValue;

    /// <summary>Creates the editor for <paramref name="schema"/>.</summary>
    public OpenCodePermissionEditorViewModel(IEditorSchema schema, IEditorScope editingScope)
        : base(schema, editingScope)
    {
        Tools = [];
        Tools.CollectionChanged += OnToolsChanged;
    }

    /// <summary>The three actions, for binding the global-mode selector.</summary>
    public static IReadOnlyList<PermissionOutcome> Actions { get; } =
        [PermissionOutcome.Allow, PermissionOutcome.Ask, PermissionOutcome.Deny];

    /// <summary>Per-tool entries, in file order.</summary>
    public ObservableCollection<OpenCodePermissionToolViewModel> Tools { get; }

    /// <summary>
    /// True when the whole setting is one action applying to every tool
    /// (<c>"permission": "ask"</c>) rather than an object keyed by tool.
    /// </summary>
    /// <remarks>
    /// Toggling this does not discard the other arm. The per-tool rows stay in
    /// <see cref="Tools"/> while global mode is on, so a user who clicks the toggle to see what
    /// the simple form looks like gets their grid back when they click it off.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerToolMode))]
    private bool _isGlobalMode;

    /// <summary>The action applied to every tool while <see cref="IsGlobalMode"/> is set.</summary>
    [ObservableProperty] private PermissionOutcome _globalAction = PermissionOutcome.Ask;

    /// <summary>Convenience inverse of <see cref="IsGlobalMode"/> for binding.</summary>
    public bool IsPerToolMode => !IsGlobalMode;

    /// <summary>Tool name for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolCommand))]
    private string _newToolName = string.Empty;

    /// <summary>
    /// Why the loaded value could not be parsed, or empty when it parsed. While set, the editor
    /// writes the original value back untouched.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string _loadError = string.Empty;

    /// <summary>True when <see cref="LoadError"/> is set.</summary>
    public bool HasLoadError => LoadError.Length > 0;

    /// <summary>
    /// How many rules can never fire, across every tool. Zero is the ordinary case; anything else
    /// is worth a banner, because the usual cause is a merge rather than a typo.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShadowedRules))]
    private int _shadowedRuleCount;

    /// <summary>True when at least one rule can never fire.</summary>
    public bool HasShadowedRules => ShadowedRuleCount > 0;

    // ── Tester ────────────────────────────────────────────────────────────────

    /// <summary>Tool name to test against, e.g. <c>bash</c>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTestCommand))]
    private string _testTool = string.Empty;

    /// <summary>The command line or path to test.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunTestCommand))]
    private string _testInput = string.Empty;

    /// <summary>What the current editor state would decide, or empty before a test has run.</summary>
    [ObservableProperty] private string _testResult = string.Empty;

    /// <summary>
    /// The outcome of the last test, for colouring the result. <see cref="PermissionOutcome.Default"/>
    /// means no rule matched.
    /// </summary>
    [ObservableProperty] private PermissionOutcome _testOutcome = PermissionOutcome.Default;

    /// <summary>
    /// Resolve <see cref="TestInput"/> against the rules as they stand now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately resolved against the live editor state rather than against what is on disk:
    /// the point is to answer "will the edit I am about to save do what I think" before it is
    /// saved.
    /// </para>
    /// <para>
    /// Uses the row-built model, which is the grid in front of the user. That agrees with what the
    /// file will decide by construction — a repeated pattern collapses to its last action at its
    /// last position, which is what last-match-wins already meant — and a test pins the two
    /// together so the collapse rule cannot quietly change out from under this claim.
    /// </para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRunTest))]
    private void RunTest()
    {
        OpenCodePermissionModel model;
        try
        {
            model = BuildModel();
        }
        catch (ArgumentException ex)
        {
            TestOutcome = PermissionOutcome.Default;
            TestResult = string.Format(
                CultureInfo.CurrentCulture, Strings.PermTestInvalidFmt, ex.Message);
            return;
        }

        OpenCodePermissionDecision decision = model.Resolve(TestTool.Trim(), TestInput);
        TestOutcome = decision.Outcome;

        string action = decision.Outcome == PermissionOutcome.Default
            ? string.Empty
            : OpenCodePermissionModel.ToWireString(decision.Outcome);

        TestResult = decision switch
        {
            { Outcome: PermissionOutcome.Default } => Strings.PermTestNoMatch,
            { MatchedRule: { } rule } => string.Format(
                CultureInfo.CurrentCulture,
                Strings.PermTestMatchedRuleFmt,
                action,
                rule.Pattern,
                decision.MatchedTool),
            { MatchedTool: { } tool } => string.Format(
                CultureInfo.CurrentCulture, Strings.PermTestMatchedToolFmt, action, tool),
            var _ => string.Format(CultureInfo.CurrentCulture, Strings.PermTestGlobalFmt, action),
        };
    }

    private bool CanRunTest() => !string.IsNullOrWhiteSpace(TestTool);

    // ── Tool commands ─────────────────────────────────────────────────────────

    /// <summary>Add a tool entry from <see cref="NewToolName"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanAddTool))]
    private void AddTool()
    {
        string tool = NewToolName.Trim();
        if (tool.Length == 0 || Tools.Any(t => string.Equals(t.Tool, tool, StringComparison.Ordinal)))
        {
            return;
        }

        OpenCodePermissionToolViewModel entry = NewTool(tool);

        // A tool the user just added defaults to whichever arm it can actually use: pattern rules
        // for most tools, a bare action for the five that reject them.
        entry.InitialiseMode(usesPatterns: entry.AcceptsPatterns);
        Tools.Add(entry);
        NewToolName = string.Empty;
    }

    private bool CanAddTool() => !string.IsNullOrWhiteSpace(NewToolName);

    private OpenCodePermissionToolViewModel NewTool(string tool) =>
        new(tool, onRemove: entry => Tools.Remove(entry));

    // ── Value contract ────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Returns value-currency types only — a <see cref="string"/> for the global arm, an
    /// insertion-ordered map for the per-tool arm. ⚠ Never a <c>JsonNode</c>: the currency
    /// conversion has no case for one, so it would be stringified and the whole permission block
    /// would land in the file as a single quoted string.
    /// </remarks>
    public override object? ToValue()
    {
        if (_hasUnparsedValue)
        {
            return _unparsedValue;
        }

        return BuildValue();
    }

    /// <summary>
    /// The live editor state as a model, for resolution and shadow detection.
    /// </summary>
    /// <remarks>
    /// Goes straight from rows to the model rather than through <see cref="BuildValue"/>, because
    /// the serialised form cannot express two rows with the same pattern and the model can. Empty
    /// tools are skipped so a half-added row does not read as a tool set to nothing.
    /// </remarks>
    private OpenCodePermissionModel BuildModel()
    {
        if (IsGlobalMode)
        {
            return OpenCodePermissionModel.ForTools(GlobalAction, []);
        }

        List<KeyValuePair<string, OpenCodeToolPermission>> tools = [];
        foreach (OpenCodePermissionToolViewModel entry in Tools)
        {
            string tool = entry.Tool.Trim();
            if (tool.Length == 0 || entry.IsEmpty)
            {
                continue;
            }

            tools.Add(new KeyValuePair<string, OpenCodeToolPermission>(tool, entry.ToPermission()));
        }

        return OpenCodePermissionModel.ForTools(null, tools);
    }

    private object? BuildValue()
    {
        if (IsGlobalMode)
        {
            return OpenCodePermissionModel.ToWireString(GlobalAction);
        }

        OrderedPropertyMap map = new();
        foreach (OpenCodePermissionToolViewModel entry in Tools)
        {
            string tool = entry.Tool.Trim();
            if (tool.Length == 0 || entry.IsEmpty)
            {
                continue;
            }

            map.Set(tool, entry.ToValue());
        }

        // An empty object would persist a permission key that constrains nothing; null is how the
        // workspace is told to remove the key from this scope instead.
        return map.Count == 0 ? null : map;
    }

    /// <inheritdoc />
    public override void LoadFromValue(IEditorValue value, IEditorScope editingScope)
    {
        ArgumentNullException.ThrowIfNull(value);

        _isLoading = true;
        try
        {
            _lastValue = value;
            _lastScope = editingScope;

            EditingScope = editingScope;
            EffectiveScope = value.EffectiveScope;
            IsOverridden = value.IsOverridden;

            foreach (OpenCodePermissionToolViewModel entry in Tools)
            {
                UnsubscribeTool(entry);
            }

            Tools.Clear();
            LoadError = string.Empty;
            _unparsedValue = null;
            _hasUnparsedValue = false;
            IsGlobalMode = false;
            GlobalAction = PermissionOutcome.Ask;

            object? scopeValue = value.GetValueAt(editingScope);

            OpenCodePermissionModel model;
            try
            {
                model = OpenCodePermissionModel.FromValue(scopeValue);
            }
            catch (FormatException ex)
            {
                // Keep the original and say why. Rendering the parseable subset would silently
                // delete whatever did not parse the next time the user saves.
                _unparsedValue = scopeValue;
                _hasUnparsedValue = true;
                LoadError = ex.Message;
                IsModified = value.IsDefinedAt(editingScope);
                UpdateOtherScopesWithData(value, editingScope);
                UpdateInheritedDisplay(value, editingScope);
                return;
            }

            if (model.GlobalAction is { } global)
            {
                IsGlobalMode = true;
                GlobalAction = global;
            }

            foreach ((string tool, OpenCodeToolPermission permission) in model.Tools)
            {
                OpenCodePermissionToolViewModel entry = NewTool(tool);
                if (permission.SingleAction is { } single)
                {
                    entry.SingleAction = single;
                    entry.InitialiseMode(usesPatterns: false);
                }
                else
                {
                    foreach (OpenCodePermissionRule rule in permission.Rules)
                    {
                        entry.Rules.Add(new OpenCodePermissionRowViewModel
                        {
                            Pattern = rule.Pattern,
                            Action = rule.Action,
                        });
                    }

                    entry.InitialiseMode(usesPatterns: true);
                }

                Tools.Add(entry);
            }

            IsModified = value.IsDefinedAt(editingScope);
            UpdateOtherScopesWithData(value, editingScope);
            UpdateInheritedDisplay(value, editingScope);
        }
        finally
        {
            _isLoading = false;
        }

        RefreshShadowing();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reloads the last-loaded value rather than clearing, so reset restores what is on disk.
    /// The guard is re-used here so the rebuild's collection churn cannot re-set
    /// <see cref="PropertyEditorViewModel.IsModified"/> after the base class cleared it.
    /// </remarks>
    protected override void OnResetToInherited()
    {
        if (_lastValue is { } value && _lastScope is { } scope)
        {
            LoadFromValue(value, scope);
            IsModified = false;
        }
        else
        {
            _isLoading = true;
            try
            {
                Tools.Clear();
                IsGlobalMode = false;
                LoadError = string.Empty;
                _unparsedValue = null;
                _hasUnparsedValue = false;
            }
            finally
            {
                _isLoading = false;
            }

            RefreshShadowing();
        }
    }

    // ── Modification plumbing ─────────────────────────────────────────────────

    /// <summary>
    /// Force-fire <c>PropertyChanged(IsModified)</c> on every user mutation, even when the flag
    /// was already true from the prior load.
    /// </summary>
    /// <remarks>
    /// <c>[ObservableProperty]</c>'s generated setter elides equal assignments, so a bare
    /// <c>IsModified = true</c> after a load that already set it is a no-op — and the live-write
    /// and save-enable subscriptions both watch the event rather than the value. Without the
    /// re-raise, editing an already-populated scope writes nothing and leaves Save disabled.
    /// </remarks>
    private void MarkModified()
    {
        if (_isLoading)
        {
            return;
        }

        RefreshShadowing();

        if (IsModified)
        {
            OnPropertyChanged(nameof(IsModified));
        }
        else
        {
            IsModified = true;
        }
    }

    private void OnToolsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodePermissionToolViewModel entry in e.OldItems)
            {
                UnsubscribeTool(entry);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodePermissionToolViewModel entry in e.NewItems)
            {
                SubscribeTool(entry);
            }
        }

        MarkModified();
    }

    /// <remarks>
    /// ⚠ Subscribes the rules already present as well as the collection itself. The load path
    /// fills a tool's rules <i>before</i> adding it to <see cref="Tools"/>, so hooking only
    /// future additions leaves every loaded row silent and inline edits do not reach Save.
    /// </remarks>
    private void SubscribeTool(OpenCodePermissionToolViewModel entry)
    {
        entry.PropertyChanged += OnToolPropertyChanged;
        entry.Rules.CollectionChanged += OnRulesChanged;
        foreach (OpenCodePermissionRowViewModel rule in entry.Rules)
        {
            rule.PropertyChanged += OnRulePropertyChanged;
        }
    }

    private void UnsubscribeTool(OpenCodePermissionToolViewModel entry)
    {
        entry.PropertyChanged -= OnToolPropertyChanged;
        entry.Rules.CollectionChanged -= OnRulesChanged;
        foreach (OpenCodePermissionRowViewModel rule in entry.Rules)
        {
            rule.PropertyChanged -= OnRulePropertyChanged;
        }
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OpenCodePermissionRowViewModel rule in e.OldItems)
            {
                rule.PropertyChanged -= OnRulePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (OpenCodePermissionRowViewModel rule in e.NewItems)
            {
                rule.PropertyChanged += OnRulePropertyChanged;
            }
        }

        MarkModified();
    }

    /// <remarks>
    /// The filtered names are input state and derived state, not policy. <c>NewRulePattern</c>
    /// backs the add box, and marking the editor modified per keystroke makes the Save button
    /// flicker; the other three are computed from <c>Tool</c>, whose own notification already
    /// marks the edit, so reacting to them re-runs the shadow scan two extra times per keystroke.
    /// </remarks>
    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePermissionToolViewModel.NewRulePattern)
            or nameof(OpenCodePermissionToolViewModel.ModeNotice)
            or nameof(OpenCodePermissionToolViewModel.HasModeNotice)
            or nameof(OpenCodePermissionToolViewModel.AcceptsPatterns))
        {
            return;
        }

        MarkModified();
    }

    /// <remarks>
    /// <c>IsShadowed</c> and <c>ShadowReason</c> are written by <see cref="RefreshShadowing"/>,
    /// which <see cref="MarkModified"/> calls. Without this filter each mutation would recurse:
    /// mark → recompute → row changed → mark.
    /// </remarks>
    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpenCodePermissionRowViewModel.IsShadowed)
            or nameof(OpenCodePermissionRowViewModel.ShadowReason))
        {
            return;
        }

        MarkModified();
    }

    /// <summary>
    /// Recompute which rules can never fire, and say why on each affected row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved through <see cref="OpenCodePermissionModel.FindShadowedRules"/> rather than a
    /// second glob comparison here, so the editor's idea of "covered by" cannot drift from the
    /// matcher that decides it at run time. Rows are matched back by position, which is why the
    /// SDK reports one — two rows may legitimately carry the same pattern.
    /// </para>
    /// <para>
    /// ⚠ <b>Built from the rows, not from <see cref="BuildValue"/>.</b> Serialising first collapses
    /// a repeated pattern into a single key, so the duplicate — the very thing worth warning about
    /// — vanishes on the way in and the scan reports no conflict at all. That was a real bug here,
    /// caught by a test asserting which of two identical rows gets flagged.
    /// </para>
    /// </remarks>
    private void RefreshShadowing()
    {
        foreach (OpenCodePermissionToolViewModel entry in Tools)
        {
            foreach (OpenCodePermissionRowViewModel rule in entry.Rules)
            {
                rule.IsShadowed = false;
                rule.ShadowReason = string.Empty;
            }
        }

        if (IsGlobalMode || _hasUnparsedValue)
        {
            ShadowedRuleCount = 0;
            return;
        }

        IReadOnlyList<OpenCodeShadowedRule> shadowed;
        try
        {
            shadowed = BuildModel().FindShadowedRules();
        }
        catch (ArgumentException)
        {
            // Mid-edit states are legitimately invalid — a half-renamed tool, a duplicated key on
            // an action-only tool. Shadowing is advisory, so it goes quiet rather than throwing out
            // of a property setter.
            ShadowedRuleCount = 0;
            return;
        }

        int count = 0;
        foreach (OpenCodeShadowedRule hit in shadowed)
        {
            OpenCodePermissionToolViewModel? entry = Tools.FirstOrDefault(
                t => string.Equals(t.Tool.Trim(), hit.Tool, StringComparison.Ordinal));

            if (entry is null || hit.RuleIndex >= entry.Rules.Count)
            {
                continue;
            }

            OpenCodePermissionRowViewModel row = entry.Rules[hit.RuleIndex];
            row.IsShadowed = true;
            row.ShadowReason = string.Format(
                CultureInfo.CurrentCulture,
                Strings.PermShadowReasonFmt,
                hit.ShadowedBy.Pattern,
                hit.ShadowedByIndex + 1);
            count++;
        }

        ShadowedRuleCount = count;
    }

    partial void OnIsGlobalModeChanged(bool value)
    {
        MarkModified();
    }

    partial void OnGlobalActionChanged(PermissionOutcome value)
    {
        MarkModified();
    }
}
