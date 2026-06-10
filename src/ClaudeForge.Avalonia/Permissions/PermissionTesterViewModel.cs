using Bennewitz.Ninja.ClaudeForge.Avalonia.Localization;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// View-model for the dry-run permission tester: the user describes a candidate
/// action (a command, path, URL, MCP tool, or subagent) and sees which rule, in
/// which bucket and scope, would decide it — in either the single editing-scope
/// view or the merged-across-scopes view.
/// </summary>
/// <remarks>
/// Reusable: reads rules through <see cref="IPermissionRuleSource"/> (so it
/// reflects the host's unsaved edits) and resolves with the SDK
/// <see cref="PermissionResolver"/>. Path resolution uses the supplied
/// <see cref="PermissionMatchContext"/>; hosts should pass one with the real
/// project root.
/// </remarks>
public sealed partial class PermissionTesterViewModel : ObservableObject
{
    private readonly IPermissionRuleSource _source;
    private readonly PermissionMatchContext _context;

    public PermissionTesterViewModel(
        IPermissionRuleSource source,
        PermissionMatchContext? context = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _context = context ?? PermissionMatchContext.FromEnvironment();
        Recompute();
    }

    /// <summary>Tool families the tester can model a candidate for.</summary>
    public IReadOnlyList<PermissionBuilderTool> AvailableTools { get; } =
        Enum.GetValues<PermissionBuilderTool>();

    // ── Inputs ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCommandInput))]
    [NotifyPropertyChangedFor(nameof(ShowPathInput))]
    [NotifyPropertyChangedFor(nameof(ShowUrlInput))]
    [NotifyPropertyChangedFor(nameof(ShowMcpInput))]
    [NotifyPropertyChangedFor(nameof(ShowAgentInput))]
    private PermissionBuilderTool _selectedTool = PermissionBuilderTool.Read;

    [ObservableProperty] private string _commandText = string.Empty;
    [ObservableProperty] private string _pathText = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _mcpServer = string.Empty;
    [ObservableProperty] private string _mcpTool = string.Empty;
    [ObservableProperty] private string _agentName = string.Empty;

    /// <summary>False = single editing-scope view; true = merged across all scopes.</summary>
    [ObservableProperty] private bool _useMergedView;

    public bool ShowCommandInput => SelectedTool is PermissionBuilderTool.Bash or PermissionBuilderTool.PowerShell;
    public bool ShowPathInput => SelectedTool is PermissionBuilderTool.Read or PermissionBuilderTool.Edit or PermissionBuilderTool.Write;
    public bool ShowUrlInput => SelectedTool is PermissionBuilderTool.WebFetch;
    public bool ShowMcpInput => SelectedTool is PermissionBuilderTool.Mcp;
    public bool ShowAgentInput => SelectedTool is PermissionBuilderTool.Agent;

    // ── Outputs ──────────────────────────────────────────────────────────────

    /// <summary>True once the inputs describe a testable candidate.</summary>
    [ObservableProperty] private bool _hasResult;

    /// <summary>The resolved outcome (drives coloring via a converter).</summary>
    [ObservableProperty] private PermissionOutcome _outcome;

    /// <summary>Localized one-word outcome label (Allow / Ask / Deny / Default).</summary>
    [ObservableProperty] private string _outcomeLabel = string.Empty;

    /// <summary>
    /// The scope shown in the result's scope chiclet — the scope that owns the
    /// deciding rule in the merged view, or the editing scope otherwise (the scope
    /// is always shown, even when it's the current one). Drives the chiclet color
    /// via <c>ConfigScopeToBrushConverter</c>.
    /// </summary>
    [ObservableProperty] private ConfigScope _matchedScope;

    /// <summary>Short scope name shown inside the chiclet (e.g. "User").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatchedScope))]
    private string _matchedScopeLabel = string.Empty;

    /// <summary>True once there's a result with a scope to show.</summary>
    public bool HasMatchedScope => MatchedScopeLabel.Length > 0;

    /// <summary>Localized sentence explaining the decision.</summary>
    [ObservableProperty] private string _explanation = string.Empty;

    /// <summary>
    /// Informational note when the command is a built-in read-only command Claude
    /// Code never prompts for; empty otherwise.
    /// </summary>
    [ObservableProperty] private string _readOnlyNote = string.Empty;

    partial void OnSelectedToolChanged(PermissionBuilderTool value) => Recompute();
    partial void OnCommandTextChanged(string value) => Recompute();
    partial void OnPathTextChanged(string value) => Recompute();
    partial void OnUrlChanged(string value) => Recompute();
    partial void OnMcpServerChanged(string value) => Recompute();
    partial void OnMcpToolChanged(string value) => Recompute();
    partial void OnAgentNameChanged(string value) => Recompute();
    partial void OnUseMergedViewChanged(bool value) => Recompute();

    /// <summary>Re-run the resolver and refresh the displayed verdict. Public so the host can refresh after rule edits.</summary>
    public void Recompute()
    {
        PermissionCandidate? candidate = BuildCandidate();
        if (candidate is null)
        {
            HasResult = false;
            Explanation = string.Empty;
            OutcomeLabel = string.Empty;
            MatchedScopeLabel = string.Empty;
            ReadOnlyNote = string.Empty;
            return;
        }

        PermissionDecision decision = UseMergedView
            ? PermissionResolver.ResolveMerged(candidate, _source.GetAllScopeRules(), _source.DefaultMode, _context)
            : ResolveSingle(candidate);

        Outcome = decision.Outcome;
        OutcomeLabel = OutcomeLabelFor(decision.Outcome);
        // Always show a scope. Merged resolution attributes the winning rule to a
        // specific scope; single-scope resolution (and the no-match Default case)
        // has no attributed scope, so fall back to the scope being edited — the
        // user wants the scope shown even when it's the current one.
        MatchedScope = decision.MatchedScope ?? _source.EditingScope;
        MatchedScopeLabel = MatchedScope.ToString();
        Explanation = Explain(decision);
        ReadOnlyNote = BuildReadOnlyNote(candidate);
        HasResult = true;
    }

    private PermissionDecision ResolveSingle(PermissionCandidate candidate)
    {
        ScopedPermissionRules r = _source.GetEditingScopeRules();
        return PermissionResolver.Resolve(candidate, r.Allow, r.Deny, r.Ask, _source.DefaultMode, _context);
    }

    private PermissionCandidate? BuildCandidate()
    {
        // Empty input → a bare-tool probe (Tool(name)): "what happens when this
        // tool is used at all?", which resolves against bare-tool / Tool(*) rules.
        // So a standalone tool (or a rule built with no argument) still yields a
        // verdict immediately. MCP is the one exception — it has no meaning
        // without at least a server name.
        switch (SelectedTool)
        {
            case PermissionBuilderTool.Bash:
                return Empty(CommandText) ? PermissionCandidate.Tool("Bash") : PermissionCandidate.Bash(CommandText.Trim());
            case PermissionBuilderTool.PowerShell:
                return Empty(CommandText) ? PermissionCandidate.Tool("PowerShell") : PermissionCandidate.PowerShell(CommandText.Trim());
            case PermissionBuilderTool.Read:
                return Empty(PathText) ? PermissionCandidate.Tool("Read") : PermissionCandidate.Read(PathText.Trim());
            case PermissionBuilderTool.Edit:
                return Empty(PathText) ? PermissionCandidate.Tool("Edit") : PermissionCandidate.Edit(PathText.Trim());
            case PermissionBuilderTool.Write:
                return Empty(PathText) ? PermissionCandidate.Tool("Write") : PermissionCandidate.Write(PathText.Trim());
            case PermissionBuilderTool.WebFetch:
                return Empty(Url) ? PermissionCandidate.Tool("WebFetch") : PermissionCandidate.WebFetch(Url.Trim());
            case PermissionBuilderTool.Mcp:
                return Empty(McpServer)
                    ? null
                    : PermissionCandidate.Mcp(McpServer.Trim(), Empty(McpTool) ? null : McpTool.Trim());
            case PermissionBuilderTool.Agent:
                return Empty(AgentName) ? PermissionCandidate.Tool("Agent") : PermissionCandidate.Agent(AgentName.Trim());
            default:
                return null;
        }
    }

    private string Explain(PermissionDecision decision)
    {
        string baseText;
        if (decision.Outcome == PermissionOutcome.Default)
        {
            string mode = decision.DefaultMode?.ToString() ?? Strings.PermTesterDefaultModeUnset;
            baseText = string.Format(Strings.PermTesterExplainDefault, mode);
        }
        else
        {
            string bucket = decision.MatchedBucket?.ToString() ?? string.Empty;
            string rule = decision.MatchedRule?.Value ?? string.Empty;
            baseText = UseMergedView && decision.MatchedScope is { } scope
                ? string.Format(Strings.PermTesterExplainMatchedScoped, bucket, rule, scope)
                : string.Format(Strings.PermTesterExplainMatched, bucket, rule);
        }

        if (!string.IsNullOrEmpty(decision.DecidingSubcommand))
        {
            baseText += " " + string.Format(Strings.PermTesterDecidingPart, decision.DecidingSubcommand);
        }

        return baseText;
    }

    private string BuildReadOnlyNote(PermissionCandidate candidate)
    {
        if (candidate.ToolName != "Bash" || string.IsNullOrWhiteSpace(candidate.CommandText))
        {
            return string.Empty;
        }

        string first = candidate.CommandText.TrimStart().Split(' ', 2)[0];
        return BashCommandSplitter.ReadOnlyCommandNames.Contains(first)
            ? string.Format(Strings.PermTesterReadOnlyNote, first)
            : string.Empty;
    }

    private static string OutcomeLabelFor(PermissionOutcome outcome) => outcome switch
    {
        PermissionOutcome.Allow => Strings.PermOutcomeAllow,
        PermissionOutcome.Ask => Strings.PermOutcomeAsk,
        PermissionOutcome.Deny => Strings.PermOutcomeDeny,
        PermissionOutcome.Default => Strings.PermOutcomeDefault,
        var _ => string.Empty,
    };

    private static bool Empty(string? s) => string.IsNullOrWhiteSpace(s);
}
