using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Builds the categorized navigation groups from a flat list of schema nodes.
///
/// <para>
/// This app's half of the page-layout seam. The three tables below are Claude
/// knowledge — which setting belongs on which page, what order the pages appear in,
/// and what each page is for — and the arrangement that applies them is the neutral
/// <see cref="SchemaPageLayout"/>. Every layered-config product needs the same split
/// of a flat schema into themed pages; only the words differ, and the words are what
/// lives here.
/// </para>
/// </summary>
public static class NavigationTreeBuilder
{
    // Manual grouping: maps schema property names to their navigation group title.
    private static readonly Dictionary<string, string> PropertyToGroup = new(StringComparer.Ordinal)
    {
        // General
        { "cleanupPeriodDays", "General" },
        { "autoUpdatesChannel", "General" },
        { "autoMemoryEnabled", "General" },
        { "respectGitignore", "General" },
        { "enableTelemetry", "General" },
        { "verbose", "General" },
        { "debug", "General" },
        { "includeCoAuthoredBy", "Git & Attribution" },
        { "includeGitInstructions", "Git & Attribution" },
        { "attribution", "Git & Attribution" },

        // Model & Effort
        { "model", "Model & Effort" },
        { "availableModels", "Model & Effort" },
        { "modelOverrides", "Model & Effort" },
        { "effortLevel", "Model & Effort" },
        { "fastMode", "Model & Effort" },
        { "alwaysThinkingEnabled", "Advanced" },
        // `maxThinkingTokens` was mapped to "Model & Effort"
        // here, but the schema does not define a settings.json property
        // by that name (the runtime knob is the env-var
        // `MAX_THINKING_TOKENS`).  The mapping was orphaned — it never
        // matched a real schema property — and the new Essentials page
        // surfaces the env var directly via IEnvAccessor.  Removed the
        // dead entry to avoid confusion in future maintenance.

        // Permissions
        { "permissions", "Permissions" },

        // Environment
        { "env", "Environment" },
        { "language", "Environment" },
        { "plansDirectory", "Environment" },
        { "otelHeadersHelper", "Environment" },

        // MCP Servers
        { "mcpServers", "MCP Servers" },
        { "enableAllProjectMcpServers", "MCP Servers" },
        { "enabledMcpjsonServers", "MCP Servers" },
        { "disabledMcpjsonServers", "MCP Servers" },

        // Hooks
        { "hooks", "Hooks" },
        { "disableAllHooks", "Hooks" },
        { "allowedHttpHookUrls", "Hooks" },
        { "httpHookAllowedEnvVars", "Hooks" },

        // Plugins
        { "enabledPlugins", "Plugins" },
        { "extraKnownMarketplaces", "Plugins" },

        // Sandbox
        { "sandbox", "Sandbox" },

        // UI & Display
        { "outputStyle", "UI & Display" },
        { "spinnerVerbs", "UI & Display" },
        { "spinnerTips", "UI & Display" },
        { "progressBar", "UI & Display" },
        { "theme", "UI & Display" },
        { "terminalTheme", "UI & Display" },

        // Advanced
        { "forceLoginMethod", "Advanced" },
        { "worktree", "Advanced" },
        { "teammateMode", "Advanced" },
        { "showCostWarnings", "Advanced" },
        { "claudeMdExcludes", "Advanced" },
        { "companyAnnouncements", "Advanced" },
    };

    private static readonly string[] GroupOrder =
    [
        "General",
        "Model & Effort",
        "Permissions",
        "Environment",
        "MCP Servers",
        "Hooks",
        "Plugins",
        "Sandbox",
        "UI & Display",
        "Git & Attribution",
        "Advanced",
    ];

    // One-line page descriptions surfaced in the editor header next to the
    // group name. Kept short (≤ 15 words / ≤ ~110 chars) so they fit on a
    // single line on narrow windows; the goal is "what's on this page" not
    // a full reference. Keys must match the group titles above. Anything
    // not listed falls back to an empty description (the TextBlock has
    // IsVisible bound to non-empty).
    private static readonly Dictionary<string, string> GroupDescriptions =
        new(StringComparer.Ordinal)
        {
            { "General", "Cleanup, telemetry, auto-update channel, and other top-level toggles." },
            { "Model & Effort", "Default model, per-task overrides, effort level, and thinking budget." },
            { "Permissions", "Allow / deny / ask rules that gate which tools can run without prompting." },
            { "Environment", "Environment variables, language, plans directory, and OTEL headers helper." },
            { "MCP Servers", "Model Context Protocol servers and which project MCP files are auto-enabled." },
            { "Hooks", "Pre/post-tool-use hooks, the disable-all switch, and HTTP hook allow-list." },
            { "Plugins", "Enabled plugin identifiers and additional marketplace endpoints." },
            { "Sandbox", "Sandbox isolation policy that constrains what tools can read and modify." },
            { "UI & Display", "Theme, output style, spinner verbs / tips, and progress-bar style." },
            { "Git & Attribution", "Git workflow defaults — co-author lines, commit instructions, attribution text." },
            { "Advanced", "Login method, worktree, teammate mode, and other rarely-changed switches." },
        };

    /// <summary>
    /// Claude's page layout, handed to the neutral arranger. <c>"Advanced"</c> is the
    /// catch-all for a property no table above claims — and it is also listed in
    /// <see cref="SchemaPageLayout.PageOrder"/>, so it keeps its declared position
    /// instead of being appended after the ordered pages.
    /// </summary>
    internal static readonly SchemaPageLayout Layout = new()
    {
        PropertyToPage = PropertyToGroup,
        PageOrder = GroupOrder,
        PageDescriptions = GroupDescriptions,
        FallbackPage = "Advanced",
    };

    /// <summary>
    /// Build ordered navigation groups with pre-wired <see cref="SettingsGroupEditorViewModel"/>s.
    /// Returns a list of (groupTitle, editorVM) pairs in display order.
    /// </summary>
    /// <param name="allNodes">All schema nodes for this product section.</param>
    /// <param name="workspace">The settings workspace to read/write values from.</param>
    /// <param name="browseDialog">Optional file-browse dialog factory.</param>
    /// <param name="sharedScope">
    /// Optional shared scope context; when provided, changing the scope dropdown on any
    /// page in this section immediately synchronises all other pages.  When <c>null</c>
    /// a private context is created for each group (used by unit tests and simple callers).
    /// </param>
    /// <param name="sdkClient">
    /// Optional <see cref="IAgentConfigClient"/> for the product section being built.
    /// When supplied, migrated editors drive their load through the
    /// SDK's typed accessors instead of raw <c>JsonNode</c> manipulation. The factory
    /// closure captures the client once per call so every group VM in this section
    /// shares the same instance.
    /// </param>
    public static IReadOnlyList<NavigationGroup> BuildGroups(
        IReadOnlyList<SchemaNode> allNodes,
        SettingsWorkspace workspace,
        Func<Task<string?>>? browseDialog = null,
        SharedScopeContext? sharedScope = null,
        ClaudeConfigClientBase? sdkClient = null,
        IUnsupportedShapeSink? unsupportedShapeSink = null)
    {
        // One factory per call: SDK-aware editors capture this client when
        // they are constructed. Sharing across both sections (Claude Code +
        // Desktop) would mis-route editor reads.
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault(sdkClient);
        // The factory reports any raw-JSON fallback (a schema shape it can't
        // classify) to this sink so the host can raise one aggregated load-time
        // notice. Editors are built eagerly in each SettingsGroupEditorViewModel
        // ctor, so the sink is fully populated by the time BuildGroups returns.
        factory.UnsupportedShapeSink = unsupportedShapeSink;
        // Bucketing and ordering are the shell's; the tables above are ours.
        List<NavigationGroup> result = [];
        foreach (SchemaPage page in Layout.Arrange(allNodes))
        {
            result.Add(BuildGroup(page, workspace, sharedScope, browseDialog, factory, sdkClient));
        }

        return result;
    }

    /// <summary>
    /// Build one arranged page's view-model. Extracted from the inline loop body so
    /// the wiring lives in one place.
    /// </summary>
    private static NavigationGroup BuildGroup(
        SchemaPage page,
        SettingsWorkspace workspace,
        SharedScopeContext? sharedScope,
        Func<Task<string?>>? browseDialog,
        DefaultEditorFactory factory,
        ClaudeConfigClientBase? sdkClient)
    {
        SharedScopeContext context = sharedScope ?? new SharedScopeContext();
        SettingsGroupEditorViewModel vm = new(
            page.Title,
            page.Nodes,
            workspace,
            context,
            browseDialog,
            factory,
            groupDescription: page.Description,
            sdkClient: sdkClient,
            tabCustomizer: ClaudeGroupTabCustomizer.Instance);
        return new NavigationGroup(page.Title, vm);
    }
}

/// <summary>A navigation group paired with its pre-built editor view-model.</summary>
public sealed record NavigationGroup(string Title, SettingsGroupEditorViewModel Editor);