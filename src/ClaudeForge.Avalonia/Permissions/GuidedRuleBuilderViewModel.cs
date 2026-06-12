using System.Text;
using Bennewitz.Ninja.ClaudeForge.Avalonia.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// View-model for the guided permission-rule builder: the user picks a tool,
/// fills a tool-specific field, and sees a live preview of the emitted rule plus
/// a plain-English gloss, then adds it to Allow/Deny/Ask.
/// </summary>
/// <remarks>
/// Reusable: it talks to the host only through <see cref="IPermissionRuleSink"/>
/// (where built rules go) and <see cref="IPermissionPathPicker"/> (the
/// file/folder picker for path tools), plus an optional MCP server list. It emits
/// syntax that satisfies <see cref="PermissionRule.TryParse"/>, so the builder
/// cannot produce a shape-invalid rule.
/// </remarks>
public sealed partial class GuidedRuleBuilderViewModel : ObservableObject
{
    private readonly IPermissionRuleSink _sink;
    private readonly IPermissionPathPicker? _pathPicker;

    public GuidedRuleBuilderViewModel(
        IPermissionRuleSink sink,
        IPermissionPathPicker? pathPicker = null,
        IReadOnlyList<string>? mcpServers = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _pathPicker = pathPicker;
        McpServers = mcpServers ?? [];
        _selectedMcpServer = McpServers.Count > 0 ? McpServers[0] : null;
        Recompute();
    }

    /// <summary>The tool families offered, for the selector.</summary>
    public IReadOnlyList<PermissionBuilderTool> AvailableTools { get; } =
        Enum.GetValues<PermissionBuilderTool>();

    /// <summary>MCP server names the host knows about (populates the dropdown).</summary>
    public IReadOnlyList<string> McpServers { get; }

    // ── Inputs ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCommandInput))]
    [NotifyPropertyChangedFor(nameof(ShowPrefixToggle))]
    [NotifyPropertyChangedFor(nameof(ShowPathInput))]
    [NotifyPropertyChangedFor(nameof(ShowRecursiveToggle))]
    [NotifyPropertyChangedFor(nameof(ShowBrowse))]
    [NotifyPropertyChangedFor(nameof(ShowDomainInput))]
    [NotifyPropertyChangedFor(nameof(ShowMcpInput))]
    [NotifyPropertyChangedFor(nameof(ShowAgentInput))]
    [NotifyPropertyChangedFor(nameof(ShowShellWildcardHint))]
    [NotifyPropertyChangedFor(nameof(ShowWebHint))]
    [NotifyPropertyChangedFor(nameof(ShowMcpHint))]
    [NotifyPropertyChangedFor(nameof(ShowAnyHint))]
    [NotifyPropertyChangedFor(nameof(ShowHintToggle))]
    [NotifyPropertyChangedFor(nameof(ShowPathGlobHints))]
    [NotifyPropertyChangedFor(nameof(ShowPathAnchorHints))]
    [NotifyPropertyChangedFor(nameof(GlobGroupOpacity))]
    [NotifyPropertyChangedFor(nameof(AnchorGroupOpacity))]
    private PermissionBuilderTool _selectedTool = PermissionBuilderTool.Read;

    [ObservableProperty] private string _commandText = string.Empty;

    /// <summary>When set, a trailing <c> *</c> is appended so the rule matches the command as a prefix.</summary>
    [ObservableProperty] private bool _matchPrefix = true;

    [ObservableProperty] private string _pathText = string.Empty;

    /// <summary>When set, the path matches recursively (appends <c>/**</c>).</summary>
    [ObservableProperty] private bool _recursive;

    [ObservableProperty] private string _domain = string.Empty;

    [ObservableProperty] private string? _selectedMcpServer;

    [ObservableProperty] private string _mcpTool = string.Empty;

    /// <summary>When set (or no tool typed), the MCP rule covers every tool on the server.</summary>
    [ObservableProperty] private bool _mcpAllTools = true;

    [ObservableProperty] private string _agentName = string.Empty;

    // ── Per-tool input visibility (drives the View) ──────────────────────────

    public bool ShowCommandInput => SelectedTool is PermissionBuilderTool.Bash or PermissionBuilderTool.PowerShell;
    public bool ShowPrefixToggle => ShowCommandInput;
    public bool ShowPathInput => SelectedTool is PermissionBuilderTool.Read or PermissionBuilderTool.Edit or PermissionBuilderTool.Write;
    public bool ShowRecursiveToggle => ShowPathInput;
    public bool ShowBrowse => ShowPathInput && _pathPicker is not null;
    public bool ShowDomainInput => SelectedTool is PermissionBuilderTool.WebFetch;
    public bool ShowMcpInput => SelectedTool is PermissionBuilderTool.Mcp;
    public bool ShowAgentInput => SelectedTool is PermissionBuilderTool.Agent;

    // Hint box visibility. The box appears for tools where special tokens apply:
    // shell tools (glob `*`) and path tools (gitignore wildcards + path anchors).
    // Path tools toggle between two groups — Wildcards (default) and Anchors —
    // via ShowAnchorHints so they aren't all shown at once.
    public bool ShowShellWildcardHint => ShowCommandInput;
    public bool ShowWebHint => ShowDomainInput;
    public bool ShowMcpHint => ShowMcpInput;
    public bool ShowAnyHint => ShowPathInput || ShowCommandInput || ShowDomainInput || ShowMcpInput;
    public bool ShowHintToggle => ShowPathInput;
    public bool ShowPathGlobHints => ShowPathInput && !ShowAnchorHints;
    public bool ShowPathAnchorHints => ShowPathInput && ShowAnchorHints;

    /// <summary>Which path-hint group is shown (false = Wildcards, the default; true = Anchors).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPathGlobHints))]
    [NotifyPropertyChangedFor(nameof(ShowPathAnchorHints))]
    [NotifyPropertyChangedFor(nameof(GlobGroupOpacity))]
    [NotifyPropertyChangedFor(nameof(AnchorGroupOpacity))]
    private bool _showAnchorHints;

    // Both path-hint groups are laid out at once (overlaid) and shown/hidden via
    // opacity rather than collapsing, so the box stays the same (larger) size when
    // the user switches groups — no reflow that would jar the layout or confuse the
    // surrounding ScrollViewer.
    public double GlobGroupOpacity => ShowPathGlobHints ? 1.0 : 0.0;
    public double AnchorGroupOpacity => ShowPathAnchorHints ? 1.0 : 0.0;

    // ── Live outputs ─────────────────────────────────────────────────────────

    /// <summary>The rule string the current inputs would produce.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAllowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddDenyCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAskCommand))]
    private string _previewRule = string.Empty;

    /// <summary>A plain-English description of what the previewed rule matches.</summary>
    [ObservableProperty] private string _plainEnglishGloss = string.Empty;

    /// <summary>True when <see cref="PreviewRule"/> is a shape-valid rule.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAllowCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddDenyCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAskCommand))]
    private bool _isValid;

    /// <summary>Validation hint shown when <see cref="IsValid"/> is false; empty when valid.</summary>
    [ObservableProperty] private string _validationMessage = string.Empty;

    /// <summary>
    /// Transient confirmation shown after an Add ("✓ Added `…` to Allow"); auto-clears
    /// a few seconds later. Empty when nothing was added recently.
    /// </summary>
    [ObservableProperty] private string _lastAddMessage = string.Empty;

    /// <summary>
    /// Non-blocking collision note from the last Add (cross-bucket conflict or
    /// same-bucket redundancy), or empty when the rule sat cleanly. Cleared with
    /// <see cref="LastAddMessage"/>.
    /// </summary>
    [ObservableProperty] private string _collisionWarning = string.Empty;

    // Cancels a pending auto-clear when a new Add arrives before the previous
    // confirmation has faded.
    private CancellationTokenSource? _clearCts;

    // Recompute on every input change.
    partial void OnSelectedToolChanged(PermissionBuilderTool value)
    {
        // Each tool defaults its hint box to the Wildcards group.
        ShowAnchorHints = false;
        Recompute();
    }

    /// <summary>Show the Wildcards hint group (left segment of the switch).</summary>
    [RelayCommand]
    private void SelectWildcardHints() => ShowAnchorHints = false;

    /// <summary>Show the Anchors hint group (right segment of the switch).</summary>
    [RelayCommand]
    private void SelectAnchorHints() => ShowAnchorHints = true;
    partial void OnCommandTextChanged(string value) => Recompute();
    partial void OnMatchPrefixChanged(bool value) => Recompute();
    partial void OnPathTextChanged(string value) => Recompute();
    partial void OnRecursiveChanged(bool value) => Recompute();
    partial void OnDomainChanged(string value) => Recompute();
    partial void OnSelectedMcpServerChanged(string? value) => Recompute();
    partial void OnMcpToolChanged(string value) => Recompute();
    partial void OnMcpAllToolsChanged(bool value) => Recompute();
    partial void OnAgentNameChanged(string value) => Recompute();

    private void Recompute()
    {
        PreviewRule = BuildPreviewRule();
        bool parses = PermissionRule.TryParse(PreviewRule, out _);

        // A path made of only wildcards (e.g. "*", "**", "*/**") matches everything
        // and is almost certainly a mistake — the bare tool already means "all
        // paths". Flag it with a specific message instead of a generic invalid one.
        bool bareWildcard = ShowPathInput && IsBareWildcardPath(BuildPathSpecifier());

        IsValid = parses && !bareWildcard;
        ValidationMessage = bareWildcard
            ? Strings.PermBuilderBareWildcardPath
            : IsValid ? string.Empty : Strings.PermBuilderInvalid;
        PlainEnglishGloss = IsValid ? BuildGloss() : Strings.PermBuilderGlossInvalid;
    }

    // Trim ends AND collapse runs of whitespace to a single space — but ONLY
    // outside quotes. A double space *between* tokens is almost always accidental
    // and would silently break matching against the real (single-spaced) command,
    // so it is collapsed; whitespace *inside* a quoted argument (e.g.
    // grep "foo   bar") is part of the argument and is preserved verbatim, or the
    // rule would never match.
    private string CleanCommand() => CollapseUnquotedWhitespace(CommandText.Trim());

    internal static string CollapseUnquotedWhitespace(string command)
    {
        StringBuilder sb = new(command.Length);
        char quote = '\0';         // '\0' = outside quotes; else the open quote char
        bool pendingSpace = false; // a collapsed run of unquoted whitespace is pending
        foreach (char c in command)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
                quote = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    pendingSpace = true; // defer; drops any leading run
                }

                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString(); // a trailing pending space is intentionally dropped
    }

    // The final path specifier shared by the preview and the gloss: trims, and
    // appends "/**" when Recursive is set (unless the path is already recursive).
    // Empty when no path is entered (→ the bare tool form, "all paths").
    private string BuildPathSpecifier()
    {
        string path = PathText.Trim();
        if (path.Length == 0)
        {
            return string.Empty;
        }

        if (Recursive && !path.EndsWith("**", StringComparison.Ordinal))
        {
            path = path.TrimEnd('/') + "/**";
        }

        return path;
    }

    // True when the specifier consists solely of wildcard/separator characters —
    // it has no literal segment to anchor on, so it matches everything.
    internal static bool IsBareWildcardPath(string spec) =>
        spec.Length > 0 && spec.Trim('*', '/', ' ').Length == 0;

    private string BuildPreviewRule()
    {
        switch (SelectedTool)
        {
            case PermissionBuilderTool.Bash:
            case PermissionBuilderTool.PowerShell:
            {
                string tool = SelectedTool.ToString();
                string cmd = CleanCommand();
                if (cmd.Length == 0)
                {
                    return tool; // bare tool = all commands
                }

                // Colon form is the canonical "any arguments (including none)"
                // wildcard per Claude's docs (Bash(npm run test:*)).
                return MatchPrefix ? $"{tool}({cmd}:*)" : $"{tool}({cmd})";
            }

            case PermissionBuilderTool.Read:
            case PermissionBuilderTool.Edit:
            case PermissionBuilderTool.Write:
            {
                string tool = SelectedTool.ToString();
                string spec = BuildPathSpecifier();
                return spec.Length == 0 ? tool : $"{tool}({spec})";
            }

            case PermissionBuilderTool.WebFetch:
            {
                string d = Domain.Trim();
                return d.Length == 0 ? "WebFetch" : $"WebFetch(domain:{d})";
            }

            case PermissionBuilderTool.Mcp:
            {
                string server = (SelectedMcpServer ?? string.Empty).Trim();
                if (server.Length == 0)
                {
                    return string.Empty; // invalid until a server is chosen
                }

                string tool = McpTool.Trim();
                return McpAllTools || tool.Length == 0
                    ? $"mcp__{server}"
                    : $"mcp__{server}__{tool}";
            }

            case PermissionBuilderTool.Agent:
            {
                string name = AgentName.Trim();
                return name.Length == 0 ? "Agent" : $"Agent({name})";
            }

            default:
                return string.Empty;
        }
    }

    private string BuildGloss()
    {
        switch (SelectedTool)
        {
            case PermissionBuilderTool.Bash:
            case PermissionBuilderTool.PowerShell:
            {
                string cmd = CleanCommand();
                if (cmd.Length == 0)
                {
                    return Strings.PermBuilderGlossShellAll;
                }

                return MatchPrefix
                    ? string.Format(Strings.PermBuilderGlossShellPrefix, cmd)
                    : string.Format(Strings.PermBuilderGlossShellExact, cmd);
            }

            case PermissionBuilderTool.Read:
            case PermissionBuilderTool.Edit:
            case PermissionBuilderTool.Write:
            {
                // Gloss the ACTUAL specifier, not the Recursive toggle: a path the
                // user typed as "src/**" is recursive even with the toggle off, so
                // the gloss must agree with the previewed rule.
                string spec = BuildPathSpecifier();
                if (spec.Length == 0)
                {
                    return Strings.PermBuilderGlossPathAll;
                }

                if (spec.EndsWith("**", StringComparison.Ordinal))
                {
                    string baseDir = spec[..^2].TrimEnd('/');
                    return string.Format(
                        Strings.PermBuilderGlossPathRecursive,
                        baseDir.Length == 0 ? spec : baseDir);
                }

                return string.Format(Strings.PermBuilderGlossPathExact, spec);
            }

            case PermissionBuilderTool.WebFetch:
            {
                string d = Domain.Trim();
                return d.Length == 0
                    ? Strings.PermBuilderGlossWebAll
                    : string.Format(Strings.PermBuilderGlossWebDomain, d);
            }

            case PermissionBuilderTool.Mcp:
            {
                string server = (SelectedMcpServer ?? string.Empty).Trim();
                string tool = McpTool.Trim();
                return McpAllTools || tool.Length == 0
                    ? string.Format(Strings.PermBuilderGlossMcpServer, server)
                    : string.Format(Strings.PermBuilderGlossMcpTool, tool, server);
            }

            case PermissionBuilderTool.Agent:
            {
                string name = AgentName.Trim();
                return name.Length == 0
                    ? Strings.PermBuilderGlossAgentAll
                    : string.Format(Strings.PermBuilderGlossAgentNamed, name);
            }

            default:
                return string.Empty;
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    private bool CanAdd() => IsValid && PreviewRule.Length > 0;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddAllow() => Add(_sink.AddAllow, PermissionBucket.Allow);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddDeny() => Add(_sink.AddDeny, PermissionBucket.Deny);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddAsk() => Add(_sink.AddAsk, PermissionBucket.Ask);

    /// <summary>
    /// Route the previewed rule to the sink, surface a transient confirmation +
    /// any collision the sink reported, and schedule the confirmation to fade.
    /// </summary>
    private void Add(Func<PermissionRule, PermissionCollision?> sinkAdd, PermissionBucket bucket)
    {
        if (!PermissionRule.TryParse(PreviewRule, out PermissionRule? rule))
        {
            return;
        }

        PermissionCollision? collision = sinkAdd(rule);
        LastAddMessage = string.Format(Strings.PermBuilderAddedFmt, rule.Value, BucketLabel(bucket));
        CollisionWarning = CollisionText(collision);
        ScheduleClear();
    }

    private static string BucketLabel(PermissionBucket bucket) => bucket switch
    {
        PermissionBucket.Allow => Strings.PermOutcomeAllow,
        PermissionBucket.Ask => Strings.PermOutcomeAsk,
        PermissionBucket.Deny => Strings.PermOutcomeDeny,
        var _ => bucket.ToString(),
    };

    private static string CollisionText(PermissionCollision? collision)
    {
        if (collision is null)
        {
            return string.Empty;
        }

        string existing = collision.ExistingRule.Value;
        string bucket = BucketLabel(collision.ExistingBucket);
        return collision.Kind == PermissionCollisionKind.Conflict
            ? string.Format(Strings.PermBuilderCollisionConflict, existing, bucket)
            : string.Format(Strings.PermBuilderCollisionRedundant, existing, bucket);
    }

    private void ScheduleClear()
    {
        _clearCts?.Cancel();
        _clearCts = new CancellationTokenSource();
        _ = ClearAfterDelayAsync(_clearCts.Token);
    }

    private async Task ClearAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            LastAddMessage = string.Empty;
            CollisionWarning = string.Empty;
        }
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (_pathPicker is null)
        {
            return;
        }

        string? picked = await _pathPicker.PickFileAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(picked))
        {
            PathText = picked;
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (_pathPicker is null)
        {
            return;
        }

        string? picked = await _pathPicker.PickFolderAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(picked))
        {
            PathText = picked;
        }
    }
}
