using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;

namespace Bennewitz.Ninja.OpenCodeForge.Adapters;

/// <summary>
/// How this product's schema keys are bucketed into settings pages, and in what order those pages
/// appear.
/// </summary>
/// <remarks>
/// <para>
/// The bucketing and ordering mechanism is the shell's (<see cref="SchemaPageLayout"/>); the tables
/// here are this product's data. Keys are the real top-level properties of the bundled schemas —
/// 36 for <c>config.json</c> and 13 for <c>tui.json</c> — read from the schema rather than guessed.
/// </para>
/// <para>
/// ⚠ <b>A key absent from these tables still renders.</b> It lands on the fallback page, so a
/// typo demotes a setting to "Advanced" rather than hiding it. And a page named in
/// <see cref="SchemaPageLayout.PropertyToPage"/> but missing from
/// <see cref="SchemaPageLayout.PageOrder"/> is appended sorted by title, which silently relocates
/// a whole page. <c>OpenCodePageLayoutTests</c> holds both.
/// </para>
/// </remarks>
public static class OpenCodePageLayout
{
    /// <summary>Fallback page for a key no table below claims.</summary>
    public const string FallbackPage = "Advanced";

    private static readonly Dictionary<string, string> ConfigPropertyToPage =
        new(StringComparer.Ordinal)
        {
            // ── Models & providers ──
            ["model"] = "Models",
            ["small_model"] = "Models",
            ["provider"] = "Models",
            ["enabled_providers"] = "Models",
            ["disabled_providers"] = "Models",

            // ── Agents & commands ──
            ["agent"] = "Agents",
            ["default_agent"] = "Agents",
            ["mode"] = "Agents",
            ["subagent_depth"] = "Agents",
            ["command"] = "Agents",

            // ── Access ──
            ["permission"] = "Permissions",
            ["tools"] = "Permissions",
            ["tool_output"] = "Permissions",

            // ── Context given to the model ──
            ["instructions"] = "Context",
            ["reference"] = "Context",
            ["references"] = "Context",
            ["attachment"] = "Context",
            ["compaction"] = "Context",

            // ── Extensions ──
            ["mcp"] = "Extensions",
            ["plugin"] = "Extensions",
            ["skills"] = "Extensions",
            ["formatter"] = "Extensions",
            ["lsp"] = "Extensions",

            // ── Sharing & snapshots ──
            ["share"] = "Sharing",
            ["autoshare"] = "Sharing",
            ["snapshot"] = "Sharing",

            // ── General ──
            ["username"] = "General",
            ["layout"] = "General",
            ["autoupdate"] = "General",
            ["shell"] = "General",
            ["watcher"] = "General",
            ["server"] = "General",
            ["logLevel"] = "General",

            // "enterprise" and "experimental" fall through to Advanced deliberately: both are
            // escape hatches whose contents are not stable enough to bucket by hand.
        };

    private static readonly string[] ConfigPageOrder =
    [
        "General",
        "Models",
        "Agents",
        "Permissions",
        "Context",
        "Extensions",
        "Sharing",
        FallbackPage,
    ];

    private static readonly Dictionary<string, string> ConfigPageDescriptions =
        new(StringComparer.Ordinal)
        {
            ["General"] = "Identity, updates, shell and server behaviour.",
            ["Models"] = "Which model answers, and which providers are available.",
            ["Agents"] = "Named agents, modes, and how deeply they may delegate.",
            ["Permissions"] = "What tools may run, and which invocations need asking.",
            ["Context"] = "Instructions, references and attachments given to the model.",
            ["Extensions"] = "MCP servers, plugins, skills, formatters and language servers.",
            ["Sharing"] = "Session sharing and snapshots.",
            [FallbackPage] = "Settings without a dedicated page, including escape hatches.",
        };

    private static readonly Dictionary<string, string> TuiPropertyToPage =
        new(StringComparer.Ordinal)
        {
            ["theme"] = "Appearance",
            ["diff_style"] = "Appearance",
            ["cursor"] = "Appearance",
            ["prompt"] = "Appearance",
            ["attention"] = "Appearance",
            ["keybinds"] = "Input",
            ["leader_timeout"] = "Input",
            ["mouse"] = "Input",
            ["scroll_speed"] = "Input",
            ["scroll_acceleration"] = "Input",
            ["plugin"] = "Extensions",
            ["plugin_enabled"] = "Extensions",
        };

    private static readonly string[] TuiPageOrder = ["Appearance", "Input", "Extensions", FallbackPage];

    private static readonly Dictionary<string, string> TuiPageDescriptions =
        new(StringComparer.Ordinal)
        {
            ["Appearance"] = "Theme, cursor, prompt and diff presentation.",
            ["Input"] = "Keybindings, leader timeout, mouse and scrolling.",
            ["Extensions"] = "Terminal-side plugins.",
            [FallbackPage] = "Settings without a dedicated page.",
        };

    /// <summary>Page layout for <c>opencode.json</c>.</summary>
    public static SchemaPageLayout Config { get; } = new()
    {
        PropertyToPage = ConfigPropertyToPage,
        PageOrder = ConfigPageOrder,
        PageDescriptions = ConfigPageDescriptions,
        FallbackPage = FallbackPage,
    };

    /// <summary>Page layout for <c>tui.json</c>.</summary>
    public static SchemaPageLayout Tui { get; } = new()
    {
        PropertyToPage = TuiPropertyToPage,
        PageOrder = TuiPageOrder,
        PageDescriptions = TuiPageDescriptions,
        FallbackPage = FallbackPage,
    };
}
