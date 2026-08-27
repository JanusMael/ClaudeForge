using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Specialized;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Permissions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Permissions;

/// <summary>
/// One tool's entry in the permission map: either a single action for every invocation, or an
/// <b>ordered</b> list of pattern rules.
/// </summary>
/// <remarks>
/// <para>
/// The two arms are held simultaneously rather than exclusively. Flipping a tool from a single
/// action to pattern rules and back must not destroy whichever arm is not currently written —
/// the same reason the marketplace-list editor preserves per-variant fields across a variant
/// switch, and what makes the mode toggle safe to click out of curiosity.
/// </para>
/// <para>
/// ⚠ <b>Rule order is the policy.</b> The last matching rule wins, so moving a rule down
/// strengthens it and moving it up weakens it. Nothing here sorts, and
/// <see cref="AddRuleCommand"/> appends rather than inserts because a rule the user just typed
/// is the one they mean to take effect.
/// </para>
/// </remarks>
public sealed partial class OpenCodePermissionToolViewModel : ObservableObject
{
    /// <summary>The actions a tool-wide setting may take, for binding a selector.</summary>
    public static IReadOnlyList<PermissionOutcome> Actions { get; } =
        [PermissionOutcome.Allow, PermissionOutcome.Ask, PermissionOutcome.Deny];

    private readonly Action<OpenCodePermissionToolViewModel>? _onRemove;
    private bool _patternsRequested;

    /// <summary>Creates an entry for <paramref name="tool"/>.</summary>
    /// <param name="tool">The tool key.</param>
    /// <param name="onRemove">
    /// Invoked by <see cref="RemoveCommand"/>. Optional so the type stays constructible in tests
    /// and on the load path, where the owning editor adds the entry after building it.
    /// </param>
    public OpenCodePermissionToolViewModel(
        string tool,
        Action<OpenCodePermissionToolViewModel>? onRemove = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _onRemove = onRemove;
        Rules = [];
        Rules.CollectionChanged += OnRulesChanged;
    }

    /// <summary>The tool key, exactly as written in the file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcceptsPatterns))]
    [NotifyPropertyChangedFor(nameof(UsesPatterns))]
    private string _tool;

    /// <summary>The action applied to every invocation, when <see cref="UsesPatterns"/> is false.</summary>
    [ObservableProperty] private PermissionOutcome _singleAction = PermissionOutcome.Ask;

    /// <summary>This tool's rules, in file order. The last match wins.</summary>
    public ObservableCollection<OpenCodePermissionRowViewModel> Rules { get; }

    /// <summary>Pattern text for the add box. Transient — never marks the editor modified.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuleCommand))]
    private string _newRulePattern = string.Empty;

    /// <summary>
    /// Set when this tool's rules are being held but will not be written, because the tool name is
    /// one the schema types as action-only. Empty when there is nothing to warn about.
    /// </summary>
    /// <remarks>
    /// The alternative was to drop the rules when the name changes, which is exactly the silent
    /// data loss this editor exists to prevent. Holding them and saying so lets the user undo the
    /// rename and get their rules back.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModeNotice))]
    private string _modeNotice = string.Empty;

    /// <summary>True when <see cref="ModeNotice"/> is set.</summary>
    public bool HasModeNotice => ModeNotice.Length > 0;

    /// <summary>
    /// False for the five tools the schema types as a bare action, which reject a pattern object.
    /// </summary>
    public bool AcceptsPatterns => !OpenCodePermissionModel.ActionOnlyTools.Contains(Tool);

    /// <summary>
    /// True when this tool writes an ordered rule map rather than one action.
    /// </summary>
    /// <remarks>
    /// Reads through <see cref="AcceptsPatterns"/>, so an action-only tool can never report the
    /// shape OpenCode would reject — however the tool name came to be that.
    /// </remarks>
    public bool UsesPatterns
    {
        get => _patternsRequested && AcceptsPatterns;
        set
        {
            if (_patternsRequested == value)
            {
                return;
            }

            _patternsRequested = value;
            OnPropertyChanged();
            RefreshModeNotice();
        }
    }

    /// <summary>Append a rule from <see cref="NewRulePattern"/>, at the strongest position.</summary>
    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private void AddRule()
    {
        string pattern = NewRulePattern.Trim();
        if (pattern.Length == 0)
        {
            return;
        }

        Rules.Add(new OpenCodePermissionRowViewModel
        {
            Pattern = pattern,
            Action = PermissionOutcome.Ask,
        });

        NewRulePattern = string.Empty;
    }

    private bool CanAddRule() => !string.IsNullOrWhiteSpace(NewRulePattern);

    /// <summary>Remove this whole tool entry from the editor.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>Remove <paramref name="rule"/> from this tool.</summary>
    internal void RemoveRule(OpenCodePermissionRowViewModel rule) => Rules.Remove(rule);

    /// <summary>
    /// Move <paramref name="rule"/> by <paramref name="offset"/> positions, clamped to the list.
    /// </summary>
    internal void MoveRule(OpenCodePermissionRowViewModel rule, int offset)
    {
        int index = Rules.IndexOf(rule);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Rules.Count)
        {
            return;
        }

        Rules.Move(index, target);
    }

    /// <summary>
    /// Set the requested mode and take ownership of the rules already added, for the load path.
    /// </summary>
    internal void InitialiseMode(bool usesPatterns)
    {
        _patternsRequested = usesPatterns;
        RefreshModeNotice();
        AdoptRules();
    }

    /// <summary>
    /// This tool's setting as the model sees it, built from the rows rather than from a serialised
    /// form so a repeated pattern is still two rules.
    /// </summary>
    internal OpenCodeToolPermission ToPermission()
    {
        if (!UsesPatterns)
        {
            return OpenCodeToolPermission.Single(SingleAction);
        }

        return OpenCodeToolPermission.Patterns(
        [
            .. Rules
                .Where(r => !string.IsNullOrWhiteSpace(r.Pattern))
                .Select(r => new OpenCodePermissionRule(r.Pattern, r.Action)),
        ]);
    }

    /// <summary>The value this tool contributes, obeying the value-currency contract.</summary>
    /// <remarks>
    /// <para>
    /// Returns an insertion-ordered map for the pattern arm, never a
    /// <see cref="Dictionary{TKey,TValue}"/>: an unspecified enumeration order here is a policy
    /// that changes without an edit.
    /// </para>
    /// <para>
    /// ⚠ <b>A repeated pattern collapses to its LAST occurrence, at that occurrence's position.</b>
    /// A JSON object cannot hold the same key twice, so a collapse is unavoidable — but which one
    /// survives, and where, decides the policy. Keeping the last action is what last-match-wins
    /// already means. Keeping the last <i>position</i> is the part that is easy to get wrong:
    /// rows <c>[git *=allow, *=ask, git *=deny]</c> resolve <c>git status</c> to <b>deny</b>, and
    /// writing the survivor at the first occurrence's slot gives <c>{"git *":"deny","*":"ask"}</c>,
    /// whose last match for the same input is the broad <c>*</c> — <b>ask</b>. The user's rule
    /// would be inverted by the act of saving it. The duplicate is separately flagged as shadowed,
    /// so this is a faithful write rather than a silent repair.
    /// </para>
    /// <para>
    /// A blank pattern is skipped rather than written as an empty key: a row can only be blank
    /// mid-edit, and <c>"": "deny"</c> is a rule that matches nothing and looks like corruption.
    /// Patterns are otherwise written verbatim — not trimmed — because the glob is the user's.
    /// </para>
    /// </remarks>
    internal object ToValue()
    {
        if (!UsesPatterns)
        {
            return OpenCodePermissionModel.ToWireString(SingleAction);
        }

        List<string> order = [];
        Dictionary<string, PermissionOutcome> actions = new(StringComparer.Ordinal);

        foreach (OpenCodePermissionRowViewModel rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            if (actions.ContainsKey(rule.Pattern))
            {
                order.Remove(rule.Pattern);
            }

            order.Add(rule.Pattern);
            actions[rule.Pattern] = rule.Action;
        }

        OrderedPropertyMap map = new();
        foreach (string pattern in order)
        {
            map.Set(pattern, OpenCodePermissionModel.ToWireString(actions[pattern]));
        }

        return map;
    }

    /// <summary>
    /// True when this tool would write nothing — the pattern arm with no usable rules. Writing an
    /// empty object here would persist a tool key that constrains nothing.
    /// </summary>
    /// <remarks>
    /// Counts rules the way <see cref="ToValue"/> writes them, so a tool holding only a half-typed
    /// blank row is empty rather than "has one rule". Deriving this from
    /// <c>Rules.Count</c> instead would emit <c>{}</c> for exactly that case.
    /// </remarks>
    internal bool IsEmpty =>
        UsesPatterns && !Rules.Any(r => !string.IsNullOrWhiteSpace(r.Pattern));

    partial void OnToolChanged(string value)
    {
        RefreshModeNotice();
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AdoptRules();
        RefreshModeNotice();
    }

    /// <remarks>
    /// Ownership is (re)stamped on every structural change rather than only on insert: a row's
    /// reorder commands ask the owner for its index, so a row added by the load path before this
    /// entry existed would otherwise keep disabled buttons for the life of the editor.
    /// </remarks>
    private void AdoptRules()
    {
        foreach (OpenCodePermissionRowViewModel rule in Rules)
        {
            rule.Owner = this;
            rule.RefreshMoveCommands();
        }
    }

    private void RefreshModeNotice()
    {
        ModeNotice = _patternsRequested && !AcceptsPatterns && Rules.Count > 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.PermToolActionOnlyHeldFmt,
                Tool,
                Rules.Count)
            : string.Empty;
    }
}
