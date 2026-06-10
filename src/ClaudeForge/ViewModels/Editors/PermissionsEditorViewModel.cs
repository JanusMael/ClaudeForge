using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using PermissionBucket = Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching.PermissionBucket;
using PermissionDefaultMode = Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.PermissionDefaultMode;
using PermissionRule = Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.PermissionRule;
using PermissionRuleNormalizer = Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.PermissionRuleNormalizer;

// Alias the SDK to disambiguate ConfigScope and reach the typed Permissions
// accessor at every call site. Mirrors the previous editor migrations.

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Operation-kind classifier for a Common Actions rule.  Drives the
/// coloured chiclet rendered to the left of each rule's label and the
/// safe-first ordering inside a tool's sub-section list.
/// </summary>
public enum CommonActionKind
{
    /// <summary>Non-mutating reads (cat, ls, git status, Read).</summary>
    Read,

    /// <summary>Local-state mutations (edit, git add, git commit).</summary>
    Write,

    /// <summary>External I/O — installs, fetches, web requests (npm, curl, WebFetch).</summary>
    Network,

    /// <summary>Irreversible or broad-blast-radius (git push, wildcard Bash).</summary>
    Destructive,
}

/// <summary>A single rule item shown inside a Common Actions group.</summary>
/// <remarks>
/// <see cref="Kind"/> drives the chiclet colour rendered alongside
/// <see cref="Rule"/> in <c>PermissionsEditorView.axaml</c>.  Every entry
/// in <see cref="PermissionsEditorViewModel.AllToolGroups"/> must classify
/// its kind explicitly — no default — so a future contributor adding a
/// rule must consciously decide where it sits on the safe-vs-dangerous
/// spectrum.
/// </remarks>
public sealed record CommonActionItem(string Rule, CommonActionKind Kind);

/// <summary>
/// One <c>defaultMode</c> option with its canonical JSON value and a human-readable
/// description sourced from the schema.  Exposed on the ComboBox so the UI can show
/// descriptions and tooltips without changing the underlying <c>string?</c> serialisation.
/// </summary>
public sealed record DefaultModeInfo(
    string Value,
    string ClaudeLabel,
    string Description,
    bool IsExperimental = false)
{
    /// <summary>
    /// Friendly wording (Claude's own label, e.g. "Bypass Permission Checks")
    /// plus the description, shown in the ComboBox item tooltip alongside the
    /// raw <see cref="Value"/>. Pre-joined for a single-binding tooltip.
    /// </summary>
    public string TooltipText => $"{ClaudeLabel}\n{Description}";

    /// <summary>
    /// Value + friendly label + description used as the
    /// <c>AutomationProperties.Name</c> so screen readers announce both the raw
    /// mode key and what it actually does.
    /// </summary>
    public string AccessibleName => IsExperimental
        ? $"{Value} — {ClaudeLabel}: {Description} (experimental)"
        : $"{Value} — {ClaudeLabel}: {Description}";
}

/// <summary>
/// A named group of Common Actions rules within a single tool, e.g.
/// <c>"Git (read)"</c>, <c>"Search &amp; View"</c>, <c>"Write"</c>.
/// Rendered as a bold sub-section heading inside the parent
/// <see cref="ToolActionGroup"/>'s expander body — not as its own Expander
/// (Phase 4 design: section headers, not nested accordions).
/// </summary>
public sealed record CommonActionGroup(string Header, IReadOnlyList<CommonActionItem> Items);

/// <summary>
/// Top-level Common Actions container: one expander per tool
/// (<c>"File"</c>, <c>"Bash"</c>, <c>"PowerShell"</c>, <c>"Web"</c>),
/// or — for the wildcard catch-all tier — a styled border with
/// <see cref="IsCatchAll"/> set to <see langword="true"/> rendered at the
/// bottom of the list outside the per-tool accordion stack.
/// </summary>
/// <param name="Tool">Display label (also the discriminator).</param>
/// <param name="OperationGroups">
/// The tool's operation groups, ordered safe-first
/// (Read kinds first, then Write, then Network, then Destructive at the
/// group level — see <see cref="PermissionsEditorViewModel.BuildToolGroups"/>).
/// </param>
/// <param name="IsCatchAll">
/// <see langword="true"/> for the single wildcard tier ("All Tools");
/// <see langword="false"/> for every concrete tool.  The View renders the
/// catch-all tier in catch-all styling so users don't confuse it with a
/// real tool accordion.
/// </param>
public sealed record ToolActionGroup(
    string Tool,
    IReadOnlyList<CommonActionGroup> OperationGroups,
    bool IsCatchAll = false);

/// <summary>
/// Editor for the "permissions" object.
/// Manages allow/deny/ask lists and the defaultMode enum.
/// Rules are wrapped in <see cref="PermissionRuleViewModel"/> so each row is
/// editable inline (two-way binding to a string element is not supported in Avalonia).
/// </summary>
public partial class PermissionsEditorViewModel : PropertyEditorViewModel
{
    // Stored so OnResetToInherited can restore the on-disk state (same pattern as
    // McpServersEditorViewModel and HooksEditorViewModel).
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    // Reset-bug fix (mirrors HooksEditorViewModel._baselineHooksValue,
    // commit 6861748).  Captured at LoadFromLayered as a deep clone of the at-load
    // 'permissions' value, so OnResetToInherited can RESTORE the workspace to
    // baseline after the base class's IsModified=false setter triggered a
    // destructive RemoveValue("permissions") through the live-write path.  Without
    // this, the SDK-backed read in LoadFromLayered sees the post-removal empty
    // state and the UI rebuilds with zero rules — exactly what the user reported
    // when smoking 3.10: "deletion does enable save, but then reset button clears
    // the UI of all the permissions."
    private JsonNode? _baselinePermissionsValue;

    /// <summary>
    /// permissions sub-fields the editor doesn't natively
    /// render (e.g. <c>disableBypassPermissionsMode</c>,
    /// <c>additionalDirectories</c>, future schema additions). Captured
    /// during load and re-emitted by ToJsonValue so save round-trips
    /// don't silently drop user data. Mirrors <c>McpServerEntry._extraFields</c>.
    /// </summary>
    private readonly JsonObject _preservedFields = new();

    // Set to true while LoadFromLayered populates lists so MarkModified and
    // RebuildCommonActions do not fire spuriously on every intermediate collection change.
    private bool _isLoading;

    /// <summary>
    /// All <c>defaultMode</c> options with schema-sourced descriptions.
    /// Ordered from safest / most common → most permissive / specialised.
    /// <para>
    /// DefaultMode is the <em>fallback</em> for tool calls that do not match any explicit
    /// Allow, Deny, or Ask rule.  The three rule lists take priority over this setting.
    /// </para>
    /// </summary>
    /// <summary>
    /// Builds the default-mode catalog with localized friendly labels +
    /// descriptions. Built per-instance (see <see cref="DefaultModeInfos"/>)
    /// rather than a <c>static readonly</c> so the <see cref="Strings"/> lookups
    /// resolve at the correct UI culture — a static initializer can run before
    /// <c>Program.Main</c> applies the culture, capturing the wrong language.
    /// The raw <c>Value</c> strings are the camelCase keys Claude Code persists;
    /// the schema/SDK enum are the source of truth for which modes exist.
    /// </summary>
    private static IReadOnlyList<DefaultModeInfo> BuildDefaultModeInfos() =>
    [
        new("default", Strings.DefaultModeClaudeDefault, Strings.DefaultModeDescDefault),
        new("acceptEdits", Strings.DefaultModeClaudeAcceptEdits, Strings.DefaultModeDescAcceptEdits),
        new("plan", Strings.DefaultModeClaudePlan, Strings.DefaultModeDescPlan),
        new("auto", Strings.DefaultModeClaudeAuto, Strings.DefaultModeDescAuto),
        new("dontAsk", Strings.DefaultModeClaudeDontAsk, Strings.DefaultModeDescDontAsk),
        new("bypassPermissions", Strings.DefaultModeClaudeBypass, Strings.DefaultModeDescBypass),
        new("delegate", Strings.DefaultModeClaudeDelegate, Strings.DefaultModeDescDelegate, IsExperimental: true),
    ];

    // -----------------------------------------------------------------------
    // Static example text (permission rule syntax is technical; not localised)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Representative Allow rules shown in the expander's read-only examples block.
    /// Chosen to illustrate safe, read-only, low-consequence operations.
    /// </summary>
    public static readonly string AllowExamples =
        "Read                – read any file (no writes)\n" +
        "Glob                – list and search files by pattern\n" +
        "Grep                – search file contents\n" +
        "Bash(git status)    – check working-tree state\n" +
        "Bash(git log *)     – browse commit history\n" +
        "Bash(ls *)          – list directory contents\n" +
        "Bash(cat *)         – print a file to stdout\n" +
        "Bash(diff *)        – compare files or revisions\n" +
        "WebFetch(domain:docs.anthropic.com)  – fetch from a specific trusted domain\n" +
        "\n" +
        "PowerShell(Get-ChildItem *)  – list directory contents (PS)\n" +
        "PowerShell(Get-Content *)    – print a file to stdout (PS)\n" +
        "Pwsh(git status)             – check working-tree state (PS 7)";

    /// <summary>
    /// Representative Deny rules shown in the expander's read-only examples block.
    /// Chosen to illustrate destructive, irreversible, or high-blast-radius operations.
    /// </summary>
    public static readonly string DenyExamples =
        "Write               – create or overwrite any file\n" +
        "Edit                – modify any existing file\n" +
        "Bash(rm *)          – delete files or directories\n" +
        "Bash(git push *)    – push commits to remote\n" +
        "Bash(git reset --hard *)  – discard uncommitted changes\n" +
        "Bash(sudo *)        – escalate to root\n" +
        "Bash(curl * | *)    – pipe remote content into a command\n" +
        "WebFetch            – block all outbound HTTP requests\n" +
        "\n" +
        "PowerShell(Remove-Item *)       – delete files or directories (PS)\n" +
        "PowerShell(git push *)          – push commits to remote (PS)\n" +
        "Pwsh(Invoke-WebRequest *)       – make outbound HTTP requests (PS 7)";

    /// <summary>
    /// Representative Ask rules shown in the expander's read-only examples block.
    /// Chosen to illustrate consequential but frequently needed grey-area operations.
    /// </summary>
    public static readonly string AskExamples =
        "Bash(git commit *)  – commit staged changes (intentional but irreversible)\n" +
        "Bash(git add *)     – stage files for commit\n" +
        "Bash(npm install *) – install packages (modifies node_modules)\n" +
        "Bash(npm run *)     – run arbitrary npm scripts\n" +
        "Bash(dotnet *)      – build, test, or publish .NET projects\n" +
        "Bash(python *)      – execute Python scripts\n" +
        "Edit(./**/*.ts)     – modify any TypeScript source file\n" +
        "Write(./**/*.json)  – create or overwrite JSON files\n" +
        "WebFetch            – any outbound web request (prompt before each)\n" +
        "\n" +
        "PowerShell(git commit *)  – commit staged changes (PS)\n" +
        "PowerShell(dotnet *)      – build, test, or publish .NET projects (PS)\n" +
        "Pwsh(npm run *)           – run npm scripts via PowerShell 7";

    // -----------------------------------------------------------------------
    // Common Actions candidate groups (static; filtered at runtime)
    // -----------------------------------------------------------------------

    /// <summary>
    /// All candidate rules for the Common Actions panel, organised tool-first
    /// with operation sub-groups in safe-first order (Read kinds first, then
    /// Write, then Network, with Destructive variants annotated per item).
    /// <see cref="RebuildCommonActions"/> filters this list at runtime to
    /// exclude any rule already present in any scope (current editing scope
    /// or inherited ancestors).
    /// </summary>
    /// <remarks>
    /// Phase 4 refactor: the previous flat <c>CommonActionGroup</c> list (one
    /// row per tool×operation pair) is replaced with a nested
    /// <see cref="ToolActionGroup"/> structure so the View can render one
    /// Expander per tool with operation groups as inner sections, and so
    /// every item carries an explicit <see cref="CommonActionKind"/> for
    /// chiclet rendering.
    /// </remarks>
    public static readonly IReadOnlyList<ToolActionGroup> AllToolGroups = BuildToolGroups();

    private static IReadOnlyList<ToolActionGroup> BuildToolGroups()
    {
        return
        [
            new ToolActionGroup("File", [
                G(Strings.LabelOperationReadOnly,
                    I("Read", CommonActionKind.Read),
                    I("Glob", CommonActionKind.Read),
                    I("Grep", CommonActionKind.Read),
                    I("Read(./**/*)", CommonActionKind.Read),
                    I("Read(~/*)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationWrite,
                    I("Edit", CommonActionKind.Write),
                    I("Write", CommonActionKind.Write),
                    I("Edit(./**/*.ts)", CommonActionKind.Write),
                    I("Edit(./**/*.cs)", CommonActionKind.Write),
                    I("Write(./**/*.json)", CommonActionKind.Write)
                ),
            ]),
            new ToolActionGroup("Bash", [
                G(Strings.LabelOperationSearchView,
                    I("Bash(cat *)", CommonActionKind.Read),
                    I("Bash(head *)", CommonActionKind.Read),
                    I("Bash(tail *)", CommonActionKind.Read),
                    I("Bash(diff *)", CommonActionKind.Read),
                    I("Bash(ls *)", CommonActionKind.Read),
                    I("Bash(find *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitRead,
                    I("Bash(git status)", CommonActionKind.Read),
                    I("Bash(git log *)", CommonActionKind.Read),
                    I("Bash(git diff *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitWrite,
                    I("Bash(git add *)", CommonActionKind.Write),
                    I("Bash(git commit *)", CommonActionKind.Write),
                    I("Bash(git push *)", CommonActionKind.Destructive)
                ),
                G(Strings.LabelOperationRuntimes,
                    I("Bash(dotnet *)", CommonActionKind.Network),
                    I("Bash(npm *)", CommonActionKind.Network),
                    I("Bash(npm run *)", CommonActionKind.Network),
                    I("Bash(node *)", CommonActionKind.Network),
                    I("Bash(python *)", CommonActionKind.Network),
                    I("Bash(python3 *)", CommonActionKind.Network)
                ),
                G(Strings.LabelOperationNetwork,
                    I("Bash(curl *)", CommonActionKind.Network),
                    I("Bash(wget *)", CommonActionKind.Network)
                ),
            ]),
            new ToolActionGroup("PowerShell", [
                G(Strings.LabelOperationSearchView,
                    I("PowerShell(Get-ChildItem *)", CommonActionKind.Read),
                    I("PowerShell(Get-Content *)", CommonActionKind.Read),
                    I("PowerShell(Select-String *)", CommonActionKind.Read),
                    I("Pwsh(Get-ChildItem *)", CommonActionKind.Read),
                    I("Pwsh(Get-Content *)", CommonActionKind.Read),
                    I("Pwsh(Select-String *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitRead,
                    I("PowerShell(git status)", CommonActionKind.Read),
                    I("PowerShell(git log *)", CommonActionKind.Read),
                    I("PowerShell(git diff *)", CommonActionKind.Read),
                    I("Pwsh(git status)", CommonActionKind.Read),
                    I("Pwsh(git log *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitWrite,
                    I("PowerShell(git add *)", CommonActionKind.Write),
                    I("PowerShell(git commit *)", CommonActionKind.Write),
                    I("PowerShell(git push *)", CommonActionKind.Destructive),
                    I("Pwsh(git add *)", CommonActionKind.Write),
                    I("Pwsh(git commit *)", CommonActionKind.Write)
                ),
                G(Strings.LabelOperationRuntimes,
                    I("PowerShell(dotnet *)", CommonActionKind.Network),
                    I("PowerShell(npm *)", CommonActionKind.Network),
                    I("PowerShell(npm run *)", CommonActionKind.Network),
                    I("PowerShell(node *)", CommonActionKind.Network),
                    I("PowerShell(python *)", CommonActionKind.Network),
                    I("Pwsh(dotnet *)", CommonActionKind.Network),
                    I("Pwsh(npm *)", CommonActionKind.Network)
                ),
                G(Strings.LabelOperationNetwork,
                    I("PowerShell(Invoke-WebRequest *)", CommonActionKind.Network),
                    I("PowerShell(Invoke-RestMethod *)", CommonActionKind.Network),
                    I("Pwsh(Invoke-WebRequest *)", CommonActionKind.Network),
                    I("Pwsh(Invoke-RestMethod *)", CommonActionKind.Network)
                ),
            ]),
            // WSL group, Windows-only via the
            // VisibleToolGroupsForPlatform filter applied below.
            // Curated `Bash(wsl …)` rules so the user can quickly allow-list
            // the WSL-wrapped variants of common commands.  Claude's `Bash`
            // tool on Windows defaults to Git Bash / MSYS, which mangles
            // forward-slash paths and exotic flags; nudging Claude to use
            // `wsl <cmd>` avoids the path-translation layer entirely.  Hooks
            // can NOT rewrite a Bash invocation (only allow/deny/log) — the
            // canonical lever is allow-listing the wsl-prefixed form so
            // Claude reads the rule pattern and chooses that path.  Soft
            // fence: no deny / ask entries by design (user choice — keep
            // deliberate Git Bash control for the rare commands where the
            // user actually wants it).
            new ToolActionGroup("WSL", [
                G(Strings.LabelOperationSearchView,
                    I("Bash(wsl ls *)", CommonActionKind.Read),
                    I("Bash(wsl find *)", CommonActionKind.Read),
                    I("Bash(wsl grep *)", CommonActionKind.Read),
                    I("Bash(wsl cat *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitRead,
                    I("Bash(wsl git status)", CommonActionKind.Read),
                    I("Bash(wsl git log *)", CommonActionKind.Read),
                    I("Bash(wsl git diff *)", CommonActionKind.Read)
                ),
                G(Strings.LabelOperationGitWrite,
                    I("Bash(wsl git add *)", CommonActionKind.Write),
                    I("Bash(wsl git commit *)", CommonActionKind.Write),
                    I("Bash(wsl git push *)", CommonActionKind.Destructive)
                ),
                G(Strings.LabelOperationRuntimes,
                    I("Bash(wsl dotnet *)", CommonActionKind.Network),
                    I("Bash(wsl npm *)", CommonActionKind.Network),
                    I("Bash(wsl npm run *)", CommonActionKind.Network),
                    I("Bash(wsl node *)", CommonActionKind.Network),
                    I("Bash(wsl python *)", CommonActionKind.Network),
                    I("Bash(wsl python3 *)", CommonActionKind.Network)
                ),
                G(Strings.LabelOperationNetwork,
                    I("Bash(wsl curl *)", CommonActionKind.Network),
                    I("Bash(wsl wget *)", CommonActionKind.Network)
                ),
            ]),
            new ToolActionGroup("Web", [
                G(Strings.LabelOperationFetchSearch,
                    I("WebFetch", CommonActionKind.Network),
                    I("WebSearch", CommonActionKind.Network),
                    I("WebFetch(domain:*)", CommonActionKind.Network),
                    I("WebSearch(site:*)", CommonActionKind.Network)
                ),
            ]),
            // Catch-all wildcard tier — pinned at the bottom, rendered as a
            // styled border (NOT a tool Expander) so users don't mistake
            // these broad rules for a per-tool surface. Each entry is
            // classified by the broadest kind the underlying tool supports
            // (raw Bash can do anything → Destructive).
            // The Tool field "All Tools" is never rendered here (the catch-all
            // border uses LabelCommonActionsCatchAll instead), so it stays as
            // a string identifier for consumers' convenience.
            //
            // PowerShell and Pwsh are intentionally absent: they have their own
            // dedicated tool expander above with correctly classified per-command
            // entries (Read/Write/Network). Duplicating them here as Destructive
            // (the "worst-case bare-name" heuristic) produces a misleading label
            // and creates confusing redundancy.
            new ToolActionGroup("All Tools", [
                // Items ordered safe-first (Read → Write → Network → Destructive)
                // so the user's eye lands on safe rules first and has to scroll
                // PAST the safer affordances to reach the dangerous shell-access
                // wildcards. Within each kind the items follow conventional
                // command-utility order (Read before Glob/Grep, Edit before Write,
                // WebFetch before WebSearch). Coloured chiclets remain so the kind
                // is still visually obvious — the rearrangement just discourages
                // dangerous mutations from being the first thing the user clicks.
                G(Strings.LabelOperationWildcards,
                    // Read kind — list / search / inspect
                    I("Read", CommonActionKind.Read),
                    I("Glob", CommonActionKind.Read),
                    I("Grep", CommonActionKind.Read),
                    // Write kind — modify or create files
                    I("Edit", CommonActionKind.Write),
                    I("Write", CommonActionKind.Write),
                    // Network kind — outbound calls
                    I("WebFetch", CommonActionKind.Network),
                    I("WebSearch", CommonActionKind.Network),
                    I("mcp__*", CommonActionKind.Network),
                    // Destructive kind — bare Bash wildcard (any shell command)
                    I("Bash", CommonActionKind.Destructive)
                ),
            ], IsCatchAll: true),
        ];

        // IMPORTANT — schema validation note:
        // The permissionRule regex requires that when parentheses are present, the
        // content contains at least one character that is NOT '*', ')', or '?'
        // (lookahead: (?=.*[^)*?])).  "Bash(*)" is INVALID — the correct form to allow
        // all uses of a tool is the bare name without parentheses: "Bash".
        //
        // Kind-classification rules (every CommonActionItem must opt in explicitly):
        //   Read         — non-mutating: cat, ls, git status/log/diff, Read, Glob, Grep
        //   Write        — local mutation: edit, write, git add, git commit
        //   Network      — external I/O: npm/dotnet (download), curl, WebFetch, mcp__*
        //   Destructive  — irreversible / broad blast: git push, raw Bash, raw PowerShell
        //
        // Group order inside a tool: safe-first by the FIRST item's kind
        // (Read groups before Write groups before Network groups). Within a
        // single group the legacy command-utility order is preserved
        // (status, log, diff — not alphabetical).
        static CommonActionItem I(string rule, CommonActionKind kind)
        {
            return new CommonActionItem(rule, kind);
        }

        static CommonActionGroup G(string header, params CommonActionItem[] items)
        {
            return new CommonActionGroup(header, items);
        }
    }

    /// <summary>
    /// filter <see cref="AllToolGroups"/> down to those visible
    /// on the current platform.  Today: the "WSL" group is Windows-only;
    /// every other group is universal.  Uses
    /// <see cref="PlatformInfo.Current"/>.IsWindows (not
    /// <see cref="OperatingSystem.IsWindows"/>) so the
    /// <c>--windows</c> / <c>--macos</c> / <c>--linux</c> debug-flag
    /// emulation works — per the CLAUDE.md "Platform abstraction" decision
    /// tree this is a UI-gating surface, not a platform-intrinsic API call.
    /// </summary>
    private static IEnumerable<ToolActionGroup> VisibleToolGroupsForPlatform(
        IEnumerable<ToolActionGroup> groups)
    {
        return PlatformInfo.Current.IsWindows
            ? groups
            : groups.Where(g => g.Tool != "WSL");
    }

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    // SDK client for typed reads. Optional: when null, fall back to the
    // legacy JsonObject-based load path so unit-test fixtures continue to
    // work unchanged. Mirrors the EnabledPlugins / Marketplaces / McpServers
    // / Hooks editor migrations.
    private readonly IClaudeConfigClient? _client;

    public PermissionsEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : this(schema, editingScope, client: null)
    {
    }

    public PermissionsEditorViewModel(
        SchemaNode schema,
        ConfigScope editingScope,
        IClaudeConfigClient? client)
        : base(schema, editingScope)
    {
        _client = client;
        DefaultModeInfos = BuildDefaultModeInfos();
        AllowList = [];
        DenyList = [];
        AskList = [];
        AllowList.CollectionChanged += OnListChanged;
        DenyList.CollectionChanged += OnListChanged;
        AskList.CollectionChanged += OnListChanged;
        // Initial state: all lists empty → show every tool group unfiltered
        // (subject to the Windows-only filter for the WSL group; see
        // VisibleToolGroupsForPlatform).
        ToolActionGroups = VisibleToolGroupsForPlatform(AllToolGroups).ToList();
    }

    // -----------------------------------------------------------------------
    // Observable properties
    // -----------------------------------------------------------------------

    /// <summary>
    /// The default-mode catalog bound by the AXAML ComboBox <c>ItemsSource</c>.
    /// Built once per instance in the constructor (localized at the correct
    /// culture — see <see cref="BuildDefaultModeInfos"/>). Stable reference so
    /// <see cref="SelectedModeInfo"/> identity comparisons hold.
    /// </summary>
    public IReadOnlyList<DefaultModeInfo> DefaultModeInfos { get; }

    [ObservableProperty] private string? _defaultMode;

    /// <summary>
    /// Computed ComboBox selection: the <see cref="DefaultModeInfo"/> whose
    /// <see cref="DefaultModeInfo.Value"/> equals <see cref="DefaultMode"/>.
    /// Setting this property updates <see cref="DefaultMode"/> and marks the editor modified.
    /// </summary>
    public DefaultModeInfo? SelectedModeInfo
    {
        get => DefaultModeInfos.FirstOrDefault(o => o.Value == DefaultMode);
        set
        {
            string? newMode = value?.Value;
            if (DefaultMode == newMode)
            {
                return;
            }

            DefaultMode = newMode; // triggers OnDefaultModeChanged → MarkModified + PropertyChanged(SelectedModeInfo)
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _newAllowText = string.Empty;
    [ObservableProperty] private string _newDenyText = string.Empty;
    [ObservableProperty] private string _newAskText = string.Empty;
    [ObservableProperty] private bool _commonActionsExpanded = true;

    /// <summary>
    /// typed UI surface for
    /// <c>permissions.disableBypassPermissionsMode</c> (boolean, normally
    /// set in Managed scope by org policy).  <see langword="null"/> means
    /// the key is absent; the editor renders that as "not set".
    /// </summary>
    [ObservableProperty] private bool? _disableBypassPermissionsMode;

    /// <summary>
    /// typed UI surface for
    /// <c>permissions.additionalDirectories</c>.  Editable via the
    /// add-row textbox + per-row remove buttons.
    /// </summary>
    public ObservableCollection<string> AdditionalDirectories { get; } = new();

    [ObservableProperty] private string _newAdditionalDirectory = string.Empty;

    /// <summary>Validation message shown beneath the Allow add-row when the entered rule is invalid.</summary>
    [ObservableProperty] private string _newAllowError = string.Empty;

    /// <summary>Validation message shown beneath the Deny add-row when the entered rule is invalid.</summary>
    [ObservableProperty] private string _newDenyError = string.Empty;

    /// <summary>Validation message shown beneath the Ask add-row when the entered rule is invalid.</summary>
    [ObservableProperty] private string _newAskError = string.Empty;

    // Clear the per-list error as soon as the user starts editing the text box.
    partial void OnNewAllowTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            NewAllowError = string.Empty;
        }
    }

    partial void OnNewDenyTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            NewDenyError = string.Empty;
        }
    }

    partial void OnNewAskTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            NewAskError = string.Empty;
        }
    }

    public ObservableCollection<PermissionRuleViewModel> AllowList { get; }
    public ObservableCollection<PermissionRuleViewModel> DenyList { get; }
    public ObservableCollection<PermissionRuleViewModel> AskList { get; }

    /// <summary>
    /// Formatted "(N rule(s))" caption for the Allow expander header.
    /// Exposed as a computed string so the Avalonia XAML IL compiler emits a compiled
    /// binding — a <c>StringFormat</c> path-chain binding falls back to reflection and
    /// produces an IL2026 trim warning.
    /// </summary>
    public string AllowCountLabel => $"({AllowList.Count} rule(s))";

    /// <inheritdoc cref="AllowCountLabel"/>
    public string DenyCountLabel => $"({DenyList.Count} rule(s))";

    /// <inheritdoc cref="AllowCountLabel"/>
    public string AskCountLabel => $"({AskList.Count} rule(s))";

    /// <summary>
    /// The filtered Common Actions tool tree: <see cref="AllToolGroups"/> minus
    /// any rule already set in any scope.  Operation groups whose every item
    /// is filtered are dropped; tool groups whose every operation group is
    /// dropped are themselves dropped.  Rebuilt by
    /// <see cref="RebuildCommonActions"/> after every load, add, or remove.
    /// </summary>
    public IReadOnlyList<ToolActionGroup> ToolActionGroups { get; private set; }

    /// <summary>
    /// <c>true</c> when at least one tool's at least one operation group still
    /// has remaining items after filtering.
    /// </summary>
    public bool HasCommonActions =>
        ToolActionGroups.Any(t => t.OperationGroups.Any(g => g.Items.Count > 0));

    /// <summary>
    /// drives the Windows-only contextual hint above the
    /// Common Actions tree that points users at the WSL group as a
    /// solution to Git Bash / MSYS path-mangling friction.  Uses
    /// <see cref="PlatformInfo.Current"/>.IsWindows (not
    /// <see cref="OperatingSystem.IsWindows"/>) so the
    /// <c>--linux</c> / <c>--macos</c> debug-flag emulation cleanly
    /// hides the hint — same convention as
    /// <see cref="VisibleToolGroupsForPlatform"/> which gates the
    /// WSL group itself.
    /// </summary>
    public bool IsWindowsPlatform => PlatformInfo.Current.IsWindows;

    // -----------------------------------------------------------------------
    // Change tracking
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>true</c> when <see cref="DefaultMode"/> is <c>"bypassPermissions"</c>.
    /// Drives the danger banner in the AXAML that explains the relationship to
    /// the <c>--dangerouslySkipPermissions</c> CLI flag.
    /// </summary>
    public bool IsInBypassMode => DefaultMode == "bypassPermissions";

    /// <summary>
    /// <c>true</c> when the user navigated to this page via the synthetic
    /// <c>--dangerouslySkipPermissions</c> search result.  Shows an amber contextual
    /// hint banner near the <c>defaultMode</c> ComboBox explaining the relationship
    /// between the CLI flag and this setting.
    /// Cleared automatically when the dropdown is changed or the property filter is cleared.
    /// </summary>
    [ObservableProperty] private bool _showDangerCliHint;

    /// <summary>
    /// Activates the contextual <c>--dangerouslySkipPermissions</c> hint banner.
    /// Called by <see cref="MainWindowViewModel.SelectSearchResult"/> when the user
    /// arrives on this page via the synthetic search result.
    /// </summary>
    public void ActivateDangerHint()
    {
        ShowDangerCliHint = true;
    }

    partial void OnDefaultModeChanged(string? value)
    {
        // Keep the ComboBox SelectedItem in sync whenever DefaultMode is set externally
        // (e.g. during LoadFromLayered or from test code via the string property directly).
        OnPropertyChanged(nameof(SelectedModeInfo));
        OnPropertyChanged(nameof(IsInBypassMode));
        // User interacted with the dropdown — the hint has served its purpose; dismiss it.
        ShowDangerCliHint = false;
        // Default mode feeds the tester's "no rule matched" branch — re-resolve.
        RefreshTester();
        MarkModified();

        // Log user-driven changes only (the partial also fires during the bulk
        // LoadFromLayered, which would otherwise be noise).
        if (!_isLoading)
        {
            Log.Information("[Permissions.DefaultMode] set to {Mode}", value ?? "(unset)");
        }
    }

    // mark dirty on the new typed properties.
    partial void OnDisableBypassPermissionsModeChanged(bool? value)
    {
        if (_isLoading)
        {
            return;
        }

        MarkModified();
        Log.Information("[Permissions.DisableBypass] set to {Value}", value?.ToString() ?? "(unset)");
    }

    /// <summary>
    /// Append <see cref="NewAdditionalDirectory"/> to <see cref="AdditionalDirectories"/>
    /// (no-op when the input is empty / already present).  Bound to the "+ Add"
    /// button next to the input row.
    /// </summary>
    [RelayCommand]
    private void AddAdditionalDirectory()
    {
        string path = (NewAdditionalDirectory ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            return;
        }

        if (AdditionalDirectories.Contains(path))
        {
            Log.Information("[Permissions.AdditionalDir] add path=\"{Path}\" skipped=duplicate", path);
            return;
        }

        AdditionalDirectories.Add(path);
        NewAdditionalDirectory = string.Empty;
        if (!_isLoading)
        {
            MarkModified();
        }

        Log.Information("[Permissions.AdditionalDir] added path=\"{Path}\"", path);
    }

    /// <summary>
    /// Remove <paramref name="path"/> from <see cref="AdditionalDirectories"/>.
    /// Bound to the per-row × button.
    /// </summary>
    [RelayCommand]
    private void RemoveAdditionalDirectory(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (AdditionalDirectories.Remove(path) && !_isLoading)
        {
            MarkModified();
            Log.Information("[Permissions.AdditionalDir] removed path=\"{Path}\"", path);
        }
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Hook new items so inline edits mark the editor modified.
        // Subscriptions are wired regardless of _isLoading so post-load edits are tracked.
        if (e.NewItems != null)
        {
            foreach (PermissionRuleViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnRuleChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (PermissionRuleViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnRuleChanged;
            }
        }

        // Skip the count-caption + RebuildCommonActions notifications during the bulk
        // load. LoadFromLayered fires once per item it adds (3 lists × N items), and the
        // header captions ultimately settle on a single value once the load finishes —
        // every intermediate notification is wasted work. RebuildCommonActions is called
        // explicitly at the end of LoadFromLayered. MarkModified itself is _isLoading-aware
        // (line ~330) so the final IsModified=… assignment in LoadFromLayered remains
        // authoritative.
        if (_isLoading)
        {
            return;
        }

        // Refresh the count captions so expander headers stay current.
        if (ReferenceEquals(sender, AllowList))
        {
            OnPropertyChanged(nameof(AllowCountLabel));
        }
        else if (ReferenceEquals(sender, DenyList))
        {
            OnPropertyChanged(nameof(DenyCountLabel));
        }
        else if (ReferenceEquals(sender, AskList))
        {
            OnPropertyChanged(nameof(AskCountLabel));
        }

        // Rebuild the filtered candidate list (LoadFromLayered calls it explicitly at the end).
        RebuildCommonActions();

        // Keep the dry-run tester verdict current as rules are added/removed.
        RefreshTester();

        MarkModified();
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PermissionRuleViewModel.Rule))
        {
            MarkModified();
        }
    }

    /// <inheritdoc/>
    /// <remarks>Routes through the base <see cref="PropertyEditorViewModel.IsLoading"/>
    /// hook so the canonical <c>MarkModified</c> implementation
    /// suppresses spurious flagging during the <c>LoadFromLayered</c> bulk-load.</remarks>
    protected override bool IsLoading => _isLoading;

    // -----------------------------------------------------------------------
    // Commands — manual input row (TextBox + Add button)
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddAllow()
    {
        TryAddRule(
            () => NewAllowText, v => NewAllowText = v, e => NewAllowError = e, AllowList, "Allow");
    }

    [RelayCommand]
    private void RemoveAllow(PermissionRuleViewModel item)
    {
        AllowList.Remove(item);
        Log.Information("[Permissions.Remove] bucket=Allow rule=\"{Rule}\"", item.Rule);
    }

    [RelayCommand]
    private void AddDeny()
    {
        TryAddRule(
            () => NewDenyText, v => NewDenyText = v, e => NewDenyError = e, DenyList, "Deny");
    }

    [RelayCommand]
    private void RemoveDeny(PermissionRuleViewModel item)
    {
        DenyList.Remove(item);
        Log.Information("[Permissions.Remove] bucket=Deny rule=\"{Rule}\"", item.Rule);
    }

    [RelayCommand]
    private void AddAsk()
    {
        TryAddRule(
            () => NewAskText, v => NewAskText = v, e => NewAskError = e, AskList, "Ask");
    }

    [RelayCommand]
    private void RemoveAsk(PermissionRuleViewModel item)
    {
        AskList.Remove(item);
        Log.Information("[Permissions.Remove] bucket=Ask rule=\"{Rule}\"", item.Rule);
    }

    /// <summary>
    /// Shared body of <see cref="AddAllow"/> / <see cref="AddDeny"/> / <see cref="AddAsk"/>.
    /// Trims the input, short-circuits on empty/duplicate, validates via
    /// <see cref="PermissionRuleViewModel.Diagnose"/>, then either reports an error
    /// or appends a new rule and clears the input.
    /// </summary>
    /// <param name="getText">Reader for the per-list input property (NewAllowText etc.).</param>
    /// <param name="setText">Writer for the input property — used to clear on success/duplicate.</param>
    /// <param name="setError">Writer for the per-list error property — populated on validation failure, cleared on success.</param>
    /// <param name="list">Target observable collection to receive a new <see cref="PermissionRuleViewModel"/>.</param>
    private static void TryAddRule(
        Func<string> getText,
        Action<string> setText,
        Action<string> setError,
        ObservableCollection<PermissionRuleViewModel> list,
        string bucket)
    {
        string raw = getText().Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        // Canonicalize on add (colon-form wildcard, forward-slash paths) so the
        // stored list matches Claude's documented syntax regardless of how it
        // was typed.
        string t = PermissionRuleNormalizer.Normalize(raw);

        if (list.Any(e => e.Rule == t))
        {
            Log.Information(
                "[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=manual skipped=duplicate", bucket, t);
            setText(string.Empty);
            return;
        }

        string err = PermissionRuleViewModel.Diagnose(t);
        if (!string.IsNullOrEmpty(err))
        {
            Log.Information(
                "[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=manual skipped=invalid error=\"{Error}\"",
                bucket, t, err);
            setError(err);
            return;
        }

        setError(string.Empty);
        list.Add(new PermissionRuleViewModel(t));
        setText(string.Empty);

        // Log the canonical form, and the raw input too when normalization changed
        // it — so a user puzzled by "I typed X but the list shows Y" can see why.
        if (string.Equals(raw, t, StringComparison.Ordinal))
        {
            Log.Information("[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=manual", bucket, t);
        }
        else
        {
            Log.Information(
                "[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=manual normalizedFrom=\"{Raw}\"", bucket, t, raw);
        }
    }

    // -----------------------------------------------------------------------
    // Commands — Common Actions panel (direct one-click add)
    // -----------------------------------------------------------------------

    /// <summary>Appends <paramref name="rule"/> to the Allow list (no-op if already present).</summary>
    [RelayCommand]
    private void AddToAllow(string rule) => AddRuleToBucket(rule, PermissionBucket.Allow);

    /// <summary>Appends <paramref name="rule"/> to the Deny list (no-op if already present).</summary>
    [RelayCommand]
    private void AddToDeny(string rule) => AddRuleToBucket(rule, PermissionBucket.Deny);

    /// <summary>Appends <paramref name="rule"/> to the Ask list (no-op if already present).</summary>
    [RelayCommand]
    private void AddToAsk(string rule) => AddRuleToBucket(rule, PermissionBucket.Ask);

    /// <summary>
    /// Transient note describing an automatic cross-bucket conflict resolution
    /// (a lower-precedence duplicate was pruned, or the add was skipped because a
    /// higher-precedence bucket already holds the identical rule). Empty when the
    /// last add sat cleanly. Surfaced on the Build tab.
    /// </summary>
    [ObservableProperty]
    private string _conflictResolutionMessage = string.Empty;

    /// <summary>
    /// Shared add path for all three buckets. Normalizes, de-dupes within the
    /// target bucket, and resolves an EXACT cross-bucket conflict by the
    /// deny &gt; ask &gt; allow precedence: an identical rule may live in only the
    /// highest-precedence bucket, so adding to a higher bucket prunes the
    /// lower-precedence copy, and adding to a lower bucket is skipped (the
    /// higher one already decides). Non-exact overlaps are left to the collision
    /// detector's advisory note.
    /// </summary>
    private void AddRuleToBucket(string rule, PermissionBucket target)
    {
        ConflictResolutionMessage = string.Empty;
        string raw = rule;
        rule = PermissionRuleNormalizer.Normalize(rule);
        if (string.IsNullOrEmpty(rule))
        {
            return;
        }

        ObservableCollection<PermissionRuleViewModel> targetList = ListFor(target);
        if (targetList.Any(e => e.Rule == rule))
        {
            Log.Information(
                "[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=preset skipped=duplicate", target, rule);
            return; // already in this bucket
        }

        foreach (PermissionBucket other in AllBuckets)
        {
            if (other == target)
            {
                continue;
            }

            ObservableCollection<PermissionRuleViewModel> otherList = ListFor(other);
            PermissionRuleViewModel? dup = otherList.FirstOrDefault(e => e.Rule == rule);
            if (dup is null)
            {
                continue;
            }

            if (Precedence(other) > Precedence(target))
            {
                // A higher-precedence bucket already decides this rule — adding it
                // to a lower bucket would be dead. Skip, and say why.
                ConflictResolutionMessage = string.Format(
                    Strings.TextPermConflictSkipped, rule, BucketName(other), BucketName(target));
                Log.Information(
                    "[Permissions.Conflict] skipped rule=\"{Rule}\" existingIn={Higher} attempted={Target}",
                    rule, other, target);
                return;
            }

            // The target bucket outranks the existing copy — prune the loser.
            otherList.Remove(dup);
            ConflictResolutionMessage = string.Format(
                Strings.TextPermConflictPruned, rule, BucketName(other), BucketName(target));
            Log.Information(
                "[Permissions.Conflict] pruned rule=\"{Rule}\" from={From} keptIn={Kept}", rule, other, target);
        }

        targetList.Add(new PermissionRuleViewModel(rule));

        if (string.Equals(raw, rule, StringComparison.Ordinal))
        {
            Log.Information("[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=preset", target, rule);
        }
        else
        {
            Log.Information(
                "[Permissions.Add] bucket={Bucket} rule=\"{Rule}\" via=preset normalizedFrom=\"{Raw}\"",
                target, rule, raw);
        }
    }

    private static readonly PermissionBucket[] AllBuckets =
        [PermissionBucket.Deny, PermissionBucket.Ask, PermissionBucket.Allow];

    private ObservableCollection<PermissionRuleViewModel> ListFor(PermissionBucket bucket) => bucket switch
    {
        PermissionBucket.Deny => DenyList,
        PermissionBucket.Ask => AskList,
        var _ => AllowList,
    };

    // deny > ask > allow (mirrors PermissionResolver evaluation order).
    private static int Precedence(PermissionBucket bucket) => bucket switch
    {
        PermissionBucket.Deny => 3,
        PermissionBucket.Ask => 2,
        var _ => 1,
    };

    private static string BucketName(PermissionBucket bucket) => bucket switch
    {
        PermissionBucket.Deny => Strings.HeaderDeny,
        PermissionBucket.Ask => Strings.HeaderAsk,
        var _ => Strings.HeaderAllow,
    };

    // -----------------------------------------------------------------------
    // Serialization
    // -----------------------------------------------------------------------

    public override JsonNode? ToJsonValue()
    {
        JsonObject obj = new();
        if (DefaultMode != null)
        {
            obj["defaultMode"] = DefaultMode;
        }

        if (AllowList.Count > 0)
        {
            obj["allow"] = ToJsonArray(AllowList);
        }

        if (DenyList.Count > 0)
        {
            obj["deny"] = ToJsonArray(DenyList);
        }

        if (AskList.Count > 0)
        {
            obj["ask"] = ToJsonArray(AskList);
        }

        // emit typed UI fields (single source of truth
        // post-promotion).
        if (DisableBypassPermissionsMode is { } db)
        {
            obj["disableBypassPermissionsMode"] = db;
        }

        if (AdditionalDirectories.Count > 0)
        {
            JsonArray arr = new();
            foreach (string dir in AdditionalDirectories)
            {
                if (!string.IsNullOrWhiteSpace(dir))
                    // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
                {
                    arr.Add((JsonNode?)JsonValue.Create(dir));
                }
            }

            if (arr.Count > 0)
            {
                obj["additionalDirectories"] = arr;
            }
        }

        // replay preserved sub-fields the editor doesn't
        // natively render (future schema additions).  Typed properties win
        // on key collision — they're the source of truth for fields the
        // user can edit; the preserved bag is the fallback for unknowns.
        foreach ((string key, JsonNode? value) in _preservedFields)
        {
            if (obj.ContainsKey(key))
            {
                continue;
            }

            obj[key] = value?.DeepClone();
        }

        return obj.Count > 0 ? obj : null;
    }

    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            // Reset per-list error text. These are populated by the manual Add commands
            // when the user types an invalid rule; without this clear, a previously failed
            // input on one scope would still display its red error message after the user
            // switched to a different scope (where the input box is empty and the error
            // is no longer relevant).
            NewAllowError = string.Empty;
            NewDenyError = string.Empty;
            NewAskError = string.Empty;

            JsonObject? scopeValue = layered.GetValueAt(editingScope) as JsonObject;

            // Capture the at-load baseline so OnResetToInherited can write it back
            // to the workspace before reloading.  Deep-clone so subsequent edits
            // don't mutate this snapshot in place.  Re-captured on every
            // LoadFromLayered call — including during reset — so the snapshot
            // tracks the most recent CLEAN state (after a save, after an external
            // reload, after a non-self-write workspace change).
            _baselinePermissionsValue = scopeValue?.DeepClone();

            AllowList.Clear();
            DenyList.Clear();
            AskList.Clear();

            // capture permissions sub-fields the editor doesn't
            // model natively (future schema additions). ToJsonValue re-emits
            // them so the save flush is byte-clean. Without this, every save
            // through the GUI would silently drop those fields.
            //
            // disableBypassPermissionsMode and
            // additionalDirectories were promoted from the preserved bag to
            // typed UI surface — extracted below into the editor's typed
            // properties and excluded from the preserved bag (single source
            // of truth post-promotion).
            _preservedFields.Clear();
            DisableBypassPermissionsMode = null;
            AdditionalDirectories.Clear();
            if (scopeValue != null)
            {
                foreach (KeyValuePair<string, JsonNode?> kv in scopeValue)
                {
                    switch (kv.Key)
                    {
                        case "defaultMode":
                        case "allow":
                        case "deny":
                        case "ask":
                            continue;
                        case "disableBypassPermissionsMode":
                            if (kv.Value is JsonValue dbv && dbv.TryGetValue(out bool dbBool))
                            {
                                DisableBypassPermissionsMode = dbBool;
                            }

                            continue;
                        case "additionalDirectories":
                            if (kv.Value is JsonArray adArr)
                            {
                                foreach (JsonNode? item in adArr)
                                {
                                    if (item is JsonValue jv && jv.TryGetValue(out string? s))
                                    {
                                        AdditionalDirectories.Add(s);
                                    }
                                }
                            }

                            continue;
                        default:
                            _preservedFields[kv.Key] = kv.Value?.DeepClone();
                            break;
                    }
                }
            }

            if (_client is not null)
            {
                // SDK-backed read path. The accessor's *At
                // overloads return the per-scope view (no merging) so the
                // editor binds to exactly the values stored at this scope.
                ConfigScope sdkScope = editingScope;

                DefaultMode = FormatDefaultMode(_client.Permissions.GetDefaultModeAt(sdkScope));
                AddRulesFromSdk(AllowList, _client.Permissions.AllowAt(sdkScope));
                AddRulesFromSdk(DenyList, _client.Permissions.DenyAt(sdkScope));
                AddRulesFromSdk(AskList, _client.Permissions.AskAt(sdkScope));
            }
            else if (scopeValue != null)
            {
                // Legacy path — used by unit-test fixtures that construct the
                // editor without an SDK client.
                DefaultMode = scopeValue["defaultMode"].AsStringOrNull();
                LoadList(AllowList, scopeValue["allow"] as JsonArray);
                LoadList(DenyList, scopeValue["deny"] as JsonArray);
                LoadList(AskList, scopeValue["ask"] as JsonArray);
            }
            else
            {
                DefaultMode = null;
            }

            // IsModified = true  when the editing scope has an explicit value
            // IsModified = false when no value is set at this scope
            //
            // Both load paths consult the layered probe to disambiguate
            // "no value at scope" from "explicit empty {}", preserving the
            // pre-migration contract.
            IsModified = scopeValue != null;
        }
        finally
        {
            _isLoading = false;
        }

        // Single rebuild after all collections are fully populated.
        RebuildCommonActions();

        // Fire count-label notifications once now that loading is done — we suppressed
        // the per-item notifications inside OnListChanged for efficiency, but the
        // expander headers still need to reflect the final loaded counts.
        OnPropertyChanged(nameof(AllowCountLabel));
        OnPropertyChanged(nameof(DenyCountLabel));
        OnPropertyChanged(nameof(AskCountLabel));
    }

    protected override void OnResetToInherited()
    {
        // Reset-bug fix (mirrors HooksEditorViewModel.OnResetToInherited,
        // commit 6861748).  The base ResetToInherited() set IsModified=false BEFORE
        // calling this method, which fired the SettingsGroupEditorViewModel.OnEditorPropertyChanged
        // live-write path with `value=null` — i.e. RemoveValue("permissions") on the
        // workspace.  We need to undo that destructive write so the SDK-backed read
        // in LoadFromLayered sees the at-load baseline state, not the just-removed
        // empty state.
        //
        // Restore via the same SDK SetValue/RemoveValue surface
        // SettingsGroupEditorViewModel uses, so the workspace's Changed event fires
        // once more — but the parent VM's _selfWriting guard is back to false by the
        // time we get here, so OnWorkspaceChanged will fire.  That's fine: the
        // rebuild is correct; we want the post-restore state visible.
        //
        // Prior shape always called Clear() on AllowList/DenyList/AskList, which
        // looked correct on paper ("revert to inherited") but in practice meant
        // a Reset after a single edit wiped every rule the user had on disk —
        // exactly the symptom seen during 3.10 smoke.
        if (_client is not null && _lastLayered is not null)
        {
            if (_baselinePermissionsValue is not null)
            {
                _client.SetValue("permissions", _baselinePermissionsValue.DeepClone(), _lastScope);
            }
            else
            {
                _client.RemoveValue("permissions", _lastScope);
            }
        }

        // Reload from the last-persisted scope value to restore the pre-edit state
        // (undo unsaved rule additions/removals) rather than clearing everything.
        if (_lastLayered != null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
        }
        else
        {
            // Fallback path — no layered snapshot captured yet.  Clear lists as the
            // prior implementation did so the UI reaches a defined empty state
            // rather than holding stale values.
            _isLoading = true;
            try
            {
                DefaultMode = null;
                AllowList.Clear();
                DenyList.Clear();
                AskList.Clear();
            }
            finally
            {
                _isLoading = false;
            }

            RebuildCommonActions();
            OnPropertyChanged(nameof(AllowCountLabel));
            OnPropertyChanged(nameof(DenyCountLabel));
            OnPropertyChanged(nameof(AskCountLabel));
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Rebuilds <see cref="ToolActionGroups"/> by filtering
    /// <see cref="AllToolGroups"/> to hide any rule already present in any
    /// scope (current or inherited ancestors).  Called once after load
    /// completes and after every list add/remove.
    /// </summary>
    private void RebuildCommonActions()
    {
        // 1. Collect rules currently shown in the editing-scope lists.
        //    Normalize so the comparison is form-invariant: stored rules are
        //    canonicalized on add (colon-form wildcard, forward-slash paths),
        //    while the taxonomy candidates below are authored in space form —
        //    normalize both sides so "already set" still matches after add.
        HashSet<string> alreadySet = new(StringComparer.OrdinalIgnoreCase);
        foreach (PermissionRuleViewModel r in AllowList.Concat(DenyList).Concat(AskList))
        {
            alreadySet.Add(PermissionRuleNormalizer.Normalize(r.Rule));
        }

        // 2. Sweep all scope entries in the layered value so rules inherited from
        //    ancestor scopes are also excluded.  This matches the "not already set in
        //    current or ancestors scope" requirement.
        if (_lastLayered != null)
        {
            foreach (ScopeEntry entry in _lastLayered.Entries)
            {
                if (entry.Value is not JsonObject perm)
                {
                    continue;
                }

                foreach (string listKey in new[] { "allow", "deny", "ask" })
                {
                    if (perm[listKey] is not JsonArray arr)
                    {
                        continue;
                    }

                    foreach (JsonNode? item in arr)
                    {
                        if (item is JsonValue jv && jv.TryGetValue(out string? s))
                        {
                            alreadySet.Add(PermissionRuleNormalizer.Normalize(s));
                        }
                    }
                }
            }
        }

        // 3. Filter at two nesting levels: drop empty operation groups, then
        //    drop tools whose every operation group emptied. The catch-all
        //    flag passes through unchanged so the View still renders the
        //    pinned-bottom styling on whichever entries remain.
        //    gate VisibleToolGroupsForPlatform FIRST so the
        //    Windows-only WSL group is omitted on non-Windows hosts before
        //    the rule-level filtering downstream.
        ToolActionGroups = VisibleToolGroupsForPlatform(AllToolGroups)
                           .Select(t => new ToolActionGroup(
                               t.Tool,
                               t.OperationGroups
                                .Select(g => new CommonActionGroup(
                                    g.Header,
                                    g.Items.Where(i => !alreadySet.Contains(PermissionRuleNormalizer.Normalize(i.Rule))).ToList()))
                                .Where(g => g.Items.Count > 0)
                                .ToList(),
                               t.IsCatchAll))
                           .Where(t => t.OperationGroups.Count > 0)
                           .ToList();

        OnPropertyChanged(nameof(ToolActionGroups));
        OnPropertyChanged(nameof(HasCommonActions));
    }

    private static void LoadList(ObservableCollection<PermissionRuleViewModel> target, JsonArray? source)
    {
        if (source == null)
        {
            return;
        }

        foreach (JsonNode? item in source)
        {
            if (item is JsonValue jv && jv.TryGetValue(out string? s))
            {
                target.Add(new PermissionRuleViewModel(s));
            }
        }
    }

    private static JsonArray ToJsonArray(IEnumerable<PermissionRuleViewModel> items)
    {
        JsonArray arr = new();
        foreach (PermissionRuleViewModel vm in items)
        {
            if (!string.IsNullOrEmpty(vm.Rule))
            {
                arr.Add((JsonNode?)JsonValue.Create(vm.Rule));
            }
        }

        return arr;
    }

    /// <summary>
    /// Append SDK <see cref="PermissionRule"/> records to a target list as
    /// <see cref="PermissionRuleViewModel"/> wrappers — used by the SDK-backed
    /// LoadFromLayered path so each row remains inline-editable in the GUI.
    /// </summary>
    private static void AddRulesFromSdk(
        ObservableCollection<PermissionRuleViewModel> target,
        IReadOnlyList<PermissionRule> source)
    {
        foreach (PermissionRule rule in source)
        {
            if (!string.IsNullOrEmpty(rule.Value))
            {
                target.Add(new PermissionRuleViewModel(rule.Value));
            }
        }
    }

    /// <summary>
    /// SDK enum → editor's string-typed <see cref="DefaultMode"/>. The editor's
    /// ComboBox is bound to the camelCase strings via <see cref="DefaultModeInfos"/>;
    /// keeping the mapping explicit prevents a future SDK-side enum addition
    /// from silently rendering a blank dropdown.
    /// </summary>
    private static string? FormatDefaultMode(PermissionDefaultMode? mode)
    {
        return mode switch
        {
            null => null,
            PermissionDefaultMode.Default => "default",
            PermissionDefaultMode.AcceptEdits => "acceptEdits",
            PermissionDefaultMode.Plan => "plan",
            PermissionDefaultMode.Auto => "auto",
            PermissionDefaultMode.DontAsk => "dontAsk",
            PermissionDefaultMode.BypassPermissions => "bypassPermissions",
            PermissionDefaultMode.Delegate => "delegate",
            var _ => null,
        };
    }
}