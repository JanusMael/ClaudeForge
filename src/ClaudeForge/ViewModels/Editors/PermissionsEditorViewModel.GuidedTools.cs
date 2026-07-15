using Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Guided-builder + dry-run-tester integration for the Permissions page.
/// Kept in its own partial so the large main editor file stays focused on the
/// taxonomy/list editing surface.
/// <para>
/// The editor itself plays both host roles for the reusable
/// <c>ClaudeForge.Avalonia</c> controls: it is the <see cref="IPermissionRuleSink"/>
/// the builder adds rules into (routing through the existing
/// <c>AddTo{Allow,Deny,Ask}</c> path so dedupe + dirty-tracking are unchanged)
/// and the <see cref="IPermissionRuleSource"/> the tester reads from (so the
/// dry run reflects unsaved in-memory edits, not just persisted state).
/// </para>
/// </summary>
public partial class PermissionsEditorViewModel : IPermissionRuleSink, IPermissionRuleSource
{
    private GuidedRuleBuilderViewModel? _guidedBuilder;
    private PermissionTesterViewModel? _tester;

    /// <summary>
    /// The guided rule builder shown alongside the taxonomy. Lazily created on
    /// first bind so editors that never expand the section pay nothing.
    /// </summary>
    public GuidedRuleBuilderViewModel GuidedBuilder =>
        _guidedBuilder ??= new GuidedRuleBuilderViewModel(this, pathPicker: null, mcpServers: McpServerNames());

    /// <summary>The dry-run tester shown alongside the taxonomy. Lazily created.</summary>
    public PermissionTesterViewModel Tester => _tester ??= new PermissionTesterViewModel(this);

    /// <summary>
    /// Drives the Overview tab's "Advanced" accordion (disableBypass +
    /// additionalDirectories). Normally collapsed; the synthetic search /
    /// Essentials deep-link to an advanced setting expands it via
    /// <c>MainWindowViewModel.OnNavigateToNavGroup</c>.
    /// </summary>
    [ObservableProperty] private bool _isAdvancedExpanded;

    /// <summary>
    /// Drives the Build tab's collapsed "Test a rule (dry run)" accordion. Stays
    /// false (collapsed) until the user clicks "Test this rule" on the builder,
    /// which seeds the tester and reveals it.
    /// </summary>
    [ObservableProperty] private bool _isTesterExpanded;

    /// <summary>
    /// "Test this rule" on the guided builder: seed the dry-run tester with a
    /// candidate derived from the builder's current inputs (the same tool, plus
    /// the literal command / path / domain / server text the user entered), then
    /// reveal the tester accordion.
    /// <para>
    /// The tester still runs the FULL-ruleset simulation — deny &gt; ask &gt; allow
    /// precedence across every scope, then the default mode — so the verdict is
    /// honest: a rule you just built can still be overridden by a deny elsewhere,
    /// and this surfaces that rather than implying the drafted rule wins in
    /// isolation.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void TestBuiltRule()
    {
        GuidedRuleBuilderViewModel b = GuidedBuilder;
        PermissionTesterViewModel t = Tester;

        t.SelectedTool = b.SelectedTool;
        switch (b.SelectedTool)
        {
            case PermissionBuilderTool.Bash:
            case PermissionBuilderTool.PowerShell:
                t.CommandText = b.CommandText;
                break;
            case PermissionBuilderTool.Read:
            case PermissionBuilderTool.Edit:
            case PermissionBuilderTool.Write:
                t.PathText = b.PathText;
                break;
            case PermissionBuilderTool.WebFetch:
                t.Url = b.Domain;
                break;
            case PermissionBuilderTool.Mcp:
                t.McpServer = b.SelectedMcpServer ?? string.Empty;
                t.McpTool = b.McpAllTools ? string.Empty : b.McpTool;
                break;
            case PermissionBuilderTool.Agent:
                t.AgentName = b.AgentName;
                break;
        }

        // Force a re-resolve even when the seeded values equal the tester's
        // current ones (CommunityToolkit elides equal [ObservableProperty]
        // assignments, so the per-field OnChanged → Recompute wouldn't fire —
        // e.g. clicking Test twice, or a bare-tool rule seeding an empty field).
        t.Recompute();
        IsTesterExpanded = true;
    }

    // ── IPermissionRuleSink — guided-builder "Add to …" targets ───────────────
    // Route through the existing private add path (AddTo*, which normalizes +
    // dedupes) so behavior matches the manual + taxonomy add buttons. Before
    // adding, detect any collision against the CURRENT lists so the candidate
    // itself isn't compared against, and return it for the builder's note.

    PermissionCollision? IPermissionRuleSink.AddAllow(PermissionRule rule) =>
        AddViaSink(rule, PermissionBucket.Allow, AddToAllow);

    PermissionCollision? IPermissionRuleSink.AddDeny(PermissionRule rule) =>
        AddViaSink(rule, PermissionBucket.Deny, AddToDeny);

    PermissionCollision? IPermissionRuleSink.AddAsk(PermissionRule rule) =>
        AddViaSink(rule, PermissionBucket.Ask, AddToAsk);

    // Detect a non-exact overlap for the builder's advisory note, then add via the
    // shared path (which also auto-resolves EXACT cross-bucket conflicts and sets
    // ConflictResolutionMessage). When the add itself resolved a conflict, return
    // null so the builder shows only the (more specific) resolution message rather
    // than a now-stale "conflicts with…" advisory.
    private PermissionCollision? AddViaSink(PermissionRule rule, PermissionBucket bucket, Action<string> add)
    {
        PermissionCollision? collision = DetectCollision(rule, bucket);
        add(rule.Value);
        return string.IsNullOrEmpty(ConflictResolutionMessage) ? collision : null;
    }

    private PermissionCollision? DetectCollision(PermissionRule rule, PermissionBucket bucket) =>
        PermissionCollisionDetector.Detect(
            rule, bucket, ToRules(AllowList), ToRules(DenyList), ToRules(AskList));

    // ── IPermissionRuleSource — what the tester evaluates ─────────────────────

    ConfigScope IPermissionRuleSource.EditingScope => _lastScope;

    PermissionDefaultMode? IPermissionRuleSource.DefaultMode =>
        Enum.TryParse(DefaultMode, ignoreCase: true, out PermissionDefaultMode mode) ? mode : null;

    /// <summary>
    /// Editing-scope rules reflect the live in-memory lists (unsaved edits), so
    /// the tester mirrors exactly what the user is building right now.
    /// </summary>
    ScopedPermissionRules IPermissionRuleSource.GetEditingScopeRules() =>
        new(_lastScope, ToRules(AllowList), ToRules(DenyList), ToRules(AskList));

    /// <summary>
    /// All scopes for the merged view: the editing scope from the in-memory
    /// lists; every other scope from the SDK accessor's persisted per-scope rules.
    /// Scopes with no client (legacy test fixtures) are simply omitted.
    /// </summary>
    IReadOnlyList<ScopedPermissionRules> IPermissionRuleSource.GetAllScopeRules()
    {
        List<ScopedPermissionRules> result = [];
        foreach (ConfigScope scope in Enum.GetValues<ConfigScope>())
        {
            if (scope == _lastScope)
            {
                result.Add(new ScopedPermissionRules(
                    scope, ToRules(AllowList), ToRules(DenyList), ToRules(AskList)));
            }
            else if (_client is not null)
            {
                result.Add(new ScopedPermissionRules(
                    scope,
                    _client.Permissions.AllowAt(scope),
                    _client.Permissions.DenyAt(scope),
                    _client.Permissions.AskAt(scope)));
            }
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // The resolver re-parses each rule string (skipping shape-invalid entries),
    // so wrapping the raw list text in PermissionRule without re-validating here
    // is safe and avoids dropping rules the user is mid-edit on.
    private static IReadOnlyList<PermissionRule> ToRules(IEnumerable<PermissionRuleViewModel> vms) =>
        vms.Select(v => new PermissionRule(v.Rule)).ToList();

    // MVP: MCP server discovery is not wired yet. The builder's server field is a
    // type-or-pick AutoCompleteBox, so an empty suggestion list is still usable.
    // Follow-up: populate from the workspace's configured mcpServers.
    private static IReadOnlyList<string> McpServerNames() => [];

    // Called from the list/default-mode change hooks in the main partial so the
    // tester verdict re-resolves as the user edits rules. Null until the tester
    // section is first bound.
    private void RefreshTester() => _tester?.Recompute();
}
