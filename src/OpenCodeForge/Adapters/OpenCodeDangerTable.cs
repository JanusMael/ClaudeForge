using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCodeForge.Adapters;

/// <summary>
/// Which of this product's settings deserve attention, and when.
/// </summary>
/// <remarks>
/// <para>
/// The matching mechanism is the shell's (<see cref="TableDangerClassifier"/>); the classification
/// here is this product's data — the same split as <see cref="OpenCodePageLayout"/>. Keys are the
/// real top-level properties of the bundled schemas, 36 for <c>config.json</c> and 13 for
/// <c>tui.json</c>, read from the schema rather than guessed, plus nested refinements where a
/// single tier for the whole subtree would be wrong.
/// </para>
/// <para>
/// ⭐ <b>Every top-level key gets an entry even when the answer is "unremarkable".</b>
/// <c>OpenCodeDangerTableTests</c> fails on any schema key with no entry, so a schema refresh
/// that introduces a setting cannot ship until somebody has triaged it. Silence would otherwise
/// read as "safe", which is the wrong default for a knob nobody has looked at.
/// </para>
/// <para>
/// ⚠ <b>Tier is inherited by descendants but value predicates are not</b> — see
/// <see cref="TableDangerClassifier"/>. That is why nested entries exist: without
/// <c>permission.bash</c>, the path would inherit <c>permission</c>'s Critical tier but nothing
/// would ever evaluate whether bash is actually set to <c>allow</c>.
/// </para>
/// <para>
/// ⓘ <b>Deprecated keys are Info deliberately, including <c>autoshare</c>.</b> It reads like a
/// privacy switch, and would be Critical if it were live — but OpenCode ignores it in favour of
/// <c>share</c>, so it cannot upload anything. Classified per the plan's reviewed table; if a
/// future OpenCode revives any of these, the tier moves with it.
/// </para>
/// </remarks>
public static class OpenCodeDangerTable
{
    // ── Value-currency helpers ────────────────────────────────────────────────
    //
    // ⚠ Values arrive in the editor value currency (IEditorValue): integers are `long`, objects
    // are IReadOnlyDictionary<string, object?>, arrays are IReadOnlyList<object?>. A predicate
    // written against `int` or `JsonNode` never fires and silently reports "safe", so every
    // helper below matches the currency types and nothing else.

    private static bool IsAllow(object? v) =>
        v is string s && string.Equals(s, "allow", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(object? v) => v is false;

    private static bool IsTrue(object? v) => v is true;

    private static bool IsNonEmptyList(object? v) =>
        v is IReadOnlyList<object?> { Count: > 0 };

    private static bool IsSetString(object? v) => v is string { Length: > 0 };

    private static IReadOnlyDictionary<string, object?>? AsMap(object? v) =>
        v as IReadOnlyDictionary<string, object?>;

    /// <summary>
    /// A <c>permission</c> value that resolves to <c>allow</c> — either the bare string, or a map
    /// whose <c>*</c> catch-all is <c>allow</c>.
    /// </summary>
    private static bool PermissionResolvesToAllow(object? v)
    {
        if (IsAllow(v))
        {
            return true;
        }

        IReadOnlyDictionary<string, object?>? map = AsMap(v);
        return map is not null && map.TryGetValue("*", out object? star) && IsAllow(star);
    }

    /// <summary>Any list entry that is an <c>http(s)://</c> URL.</summary>
    private static bool HasRemoteUrl(object? v) =>
        v is IReadOnlyList<object?> list
        && list.Any(e => e is string s
                         && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                             || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)));

    /// <summary>A hostname that is not loopback — i.e. the agent is reachable off-box.</summary>
    private static bool IsNonLoopbackHost(object? v) =>
        v is string s
        && s.Length > 0
        && !string.Equals(s, "127.0.0.1", StringComparison.Ordinal)
        && !string.Equals(s, "::1", StringComparison.Ordinal)
        && !string.Equals(s, "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>A CORS setting containing a wildcard origin.</summary>
    private static bool HasWildcardOrigin(object? v) => v switch
    {
        string s => s.Contains('*', StringComparison.Ordinal),
        IReadOnlyList<object?> list => list.Any(e => e is string s && s.Contains('*', StringComparison.Ordinal)),
        _ => false,
    };

    /// <summary>An MCP server entry that is switched on.</summary>
    private static bool IsEnabledServer(object? v)
    {
        IReadOnlyDictionary<string, object?>? map = AsMap(v);

        // An entry with no `enabled` key is enabled by default, so absence is not safety.
        return map is not null && (!map.TryGetValue("enabled", out object? e) || e is not false);
    }

    /// <summary>Subagent depth past the point where delegated work multiplies noticeably.</summary>
    private static bool IsDeepSubagentDepth(object? v) => v is long n && n > 2;

    /// <summary>
    /// Scopes whose files are committed to git, so a secret written there is published to
    /// everyone with repo access rather than kept locally.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>Case-insensitive, and that is a fix rather than sloppiness.</b> The constant is a
    /// ladder RUNG NAME (<c>"Project"</c>); what arrives here is an
    /// <see cref="IEditorScope.Id"/>, which <see cref="ConfigScope.Id"/> produces by
    /// lower-casing that same name (<c>"project"</c>) — the interface documents ids in that form.
    /// An ordinal comparison therefore never matched in the running app, so this escalation was
    /// inert: a plaintext API key in a git-committed project file rendered Caution amber instead
    /// of Critical red.
    /// <para>
    /// ⚠ <b>Every table-level test stayed green through that</b>, because they construct their own
    /// scope from this very constant — tautological with respect to casing.
    /// <c>ScopeEscalationRealScopeTests</c> exists to compare against the adapter the app actually
    /// hands the classifier, which is the only version of this assertion that can fail.
    /// </para>
    /// </remarks>
    private static bool IsGitCommittedScope(string? scopeId) =>
        string.Equals(scopeId, OpenCodeScopes.Project, StringComparison.OrdinalIgnoreCase);

    // ── config.json — all 36 top-level keys, plus nested refinements ──────────

    private static readonly Dictionary<string, DangerRule> ConfigRules =
        new(StringComparer.Ordinal)
        {
            // ── 🔴 Access and execution ──
            ["permission"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Controls whether the agent asks before acting. Set to allow, it approves every tool automatically.",
                Unsafe = PermissionResolvesToAllow,
            },
            // ⭐ The wildcard is load-bearing, not redundant with the named tools below.
            // Value predicates are NOT inherited by descendants (see TableDangerClassifier), so
            // without this a permission key OpenCode adds later would take permission's Critical
            // tier and then never have its value checked — set to `allow`, it would render red
            // yet report "not dangerous right now". The named entries beat this one on
            // specificity and exist only for their more precise wording.
            ["permission.*"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Grants this capability without asking.",
                Unsafe = IsAllow,
            },
            ["permission.bash"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Runs shell commands unattended, with your credentials and your filesystem.",
                Unsafe = IsAllow,
            },
            ["permission.edit"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Writes to files without asking, so a bad edit run lands before you see it.",
                Unsafe = IsAllow,
            },
            ["permission.external_directory"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Reads and writes outside the working tree, beyond anything git can undo.",
                Unsafe = IsAllow,
            },
            ["permission.webfetch"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Fetches arbitrary URLs — both an exfiltration path and a prompt-injection surface.",
                Unsafe = IsAllow,
            },
            ["permission.websearch"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Sends queries out and pulls untrusted text back into the session.",
                Unsafe = IsAllow,
            },
            ["share"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "On auto, every session is uploaded to a shareable link.",
                Unsafe = v => v is string s && string.Equals(s, "auto", StringComparison.OrdinalIgnoreCase),
            },
            ["snapshot"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Filesystem snapshots are your undo. Disabled, a bad edit run has no way back.",
                Unsafe = IsFalse,
            },
            ["plugin"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "npm packages loaded into the agent process. There is no marketplace-trust layer.",
                Unsafe = IsNonEmptyList,
            },
            ["mcp"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Each server is executable code or a network endpoint the agent will talk to.",
            },
            ["mcp.*"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "This server runs as executable code or a remote endpoint whenever it is enabled.",
                Unsafe = IsEnabledServer,
            },
            ["provider"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Holds provider credentials and endpoints in the edited file.",
            },
            ["provider.*.options.apiKey"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "A plaintext secret in this file. At project scope it is committed to git and shared with everyone who can read the repo.",
                Unsafe = IsSetString,
                EscalatesAt = IsGitCommittedScope,
            },
            ["provider.*.options.baseURL"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Repoints the provider at another endpoint, which then receives your prompts and credentials.",
                Unsafe = IsSetString,
            },
            ["enterprise"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Routes the agent through an enterprise backend.",
            },
            ["enterprise.url"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Every session is routed through this host.",
                Unsafe = IsSetString,
            },
            ["instructions"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Text injected into every session's prompt. A remote URL means somebody else controls it.",
                Unsafe = HasRemoteUrl,
            },
            ["skills"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Skills are instructions the agent trusts. Remote ones are fetched and trusted too.",
            },
            ["skills.urls"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Remote skills — instructions fetched over the network and trusted as if local.",
                Unsafe = IsNonEmptyList,
            },
            ["server"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Exposes the agent over HTTP. How far it reaches depends on the settings below.",
            },
            ["server.hostname"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Binding past loopback puts the agent on the network, reachable by other machines.",
                Unsafe = IsNonLoopbackHost,
            },
            ["server.cors"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "A wildcard origin lets any web page in your browser drive the agent.",
                Unsafe = HasWildcardOrigin,
            },
            ["server.mdns"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Advertises the agent on the local network, so it no longer has to be found to be reached.",
                Unsafe = IsTrue,
            },

            // ── 🔴 Per-agent overrides reuse the permission tiers, scoped to one agent ──
            //
            // An agent granted `bash: allow` is red even when the global `permission` is safe,
            // which is precisely the case a per-key-only table would miss.
            ["agent.*.permission"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "This agent's own permissions, which override the global ones.",
                Unsafe = PermissionResolvesToAllow,
            },
            ["agent.*.permission.*"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Granted for this agent regardless of the global permission setting.",
                Unsafe = IsAllow,
            },

            // ── 🟠 Cost and quality ──
            ["agent"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Defines agents, each of which can carry its own model, limits and permissions.",
            },
            ["agent.*.temperature"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Changes output determinism for this agent.",
            },
            ["agent.*.top_p"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Changes output determinism for this agent.",
            },
            ["agent.*.steps"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Caps or extends how much work this agent does in one turn — a cost lever.",
            },
            ["model"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Sets the model every request bills against.",
            },
            ["small_model"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Sets the model used for cheap auxiliary calls; a large one here is billed often.",
            },
            ["subagent_depth"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Each level multiplies delegated work, so cost grows faster than the number suggests.",
                Unsafe = IsDeepSubagentDepth,
            },
            ["compaction"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Governs how conversation history is trimmed as it grows.",
            },
            ["compaction.auto"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Disabled, long sessions run into context overflow instead of being compacted.",
                Unsafe = IsFalse,
            },
            ["compaction.prune"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Controls what history is discarded, and so what the agent can still remember.",
            },
            ["tool_output"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Caps how much of a tool's output the agent gets to read.",
            },
            ["tool_output.max_lines"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Truncated tool output means the agent acts on a partial picture.",
            },
            ["tool_output.max_bytes"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Truncated tool output means the agent acts on a partial picture.",
            },
            ["attachment"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Governs how attached images and files are downscaled before being sent.",
            },
            ["tools"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Disabling a core tool changes what the agent can do, often silently.",
            },
            ["formatter"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Turned off, generated code lands unformatted and diffs get noisy.",
                Unsafe = IsFalse,
            },
            ["lsp"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Turned off, the agent loses type and diagnostic feedback and guesses more.",
                Unsafe = IsFalse,
            },
            ["disabled_providers"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Can silently break model resolution if it excludes the provider a model needs.",
            },
            ["enabled_providers"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Can silently break model resolution if it omits the provider a model needs.",
            },
            ["watcher"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Controls which file changes the agent notices.",
            },
            ["watcher.ignore"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Ignored paths are invisible to the agent, so edits there go unnoticed.",
            },
            ["references"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Git entries clone a repository onto this machine; local entries only read a path.",
            },
            ["experimental"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "Unstable by declaration — behaviour here can change between releases.",
            },

            // ── 🔵 Behaviour ──
            ["autoupdate"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Whether OpenCode updates itself.",
            },
            ["default_agent"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Which agent a new session starts with.",
            },
            ["shell"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Which shell runs commands. It does not change whether they are allowed to run.",
            },
            ["username"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "The name the agent addresses you by.",
            },
            ["logLevel"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "How much detail is written to the log.",
            },
            ["server.port"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Which port the local server listens on. Reach is set by hostname, not port.",
            },
            ["server.mdnsDomain"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "The domain used when mDNS advertising is on.",
            },
            ["command"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Custom slash commands. They run with the permissions already configured.",
            },
            ["$schema"] = new()
            {
                Tier = AppSeverity.Neutral,
                Why = "Points at the schema this file is validated against.",
            },

            // ── 🔵 Deprecated: ignored by OpenCode, so behaviour-only ──
            ["mode"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Deprecated and ignored; superseded by agent.",
            },
            ["autoshare"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Deprecated and ignored; superseded by share, which is where sharing is actually decided.",
            },
            ["reference"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Deprecated and ignored; superseded by references.",
            },
            ["layout"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Deprecated and ignored; TUI layout moved to tui.json.",
            },
        };

    // ── tui.json — all 13 top-level keys ──────────────────────────────────────
    //
    // Keybinds are behaviour-only on purpose: rebinding a key cannot grant a capability the
    // permission settings have not already granted.

    private static readonly Dictionary<string, DangerRule> TuiRules =
        new(StringComparer.Ordinal)
        {
            ["plugin"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "npm packages loaded into the terminal process — the same executable-code reasoning as config plugins.",
                Unsafe = IsNonEmptyList,
            },
            ["plugin_enabled"] = new()
            {
                Tier = AppSeverity.Critical,
                Why = "Switches terminal plugins on, and a plugin is executable code.",
                Unsafe = v => AsMap(v)?.Values.Any(e => e is true) ?? false,
            },
            ["theme"] = new() { Tier = AppSeverity.Info, Why = "Colour scheme for the terminal UI." },
            ["keybinds"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "Key bindings. Rebinding cannot grant the agent any capability it lacks.",
            },
            ["cursor"] = new() { Tier = AppSeverity.Info, Why = "Cursor appearance." },
            ["mouse"] = new() { Tier = AppSeverity.Info, Why = "Whether the mouse is active in the TUI." },
            ["scroll_speed"] = new() { Tier = AppSeverity.Info, Why = "How far the view moves per scroll." },
            ["scroll_acceleration"] = new() { Tier = AppSeverity.Info, Why = "Whether scrolling accelerates when held." },
            ["diff_style"] = new() { Tier = AppSeverity.Info, Why = "How diffs are rendered." },
            ["attention"] = new() { Tier = AppSeverity.Info, Why = "How the TUI signals it needs you." },
            ["prompt"] = new() { Tier = AppSeverity.Info, Why = "Prompt appearance and placement." },
            ["leader_timeout"] = new() { Tier = AppSeverity.Info, Why = "How long a leader key stays armed." },
            ["$schema"] = new()
            {
                Tier = AppSeverity.Neutral,
                Why = "Points at the schema this file is validated against.",
            },
        };

    /// <summary>Danger classification for <c>opencode.json</c>.</summary>
    public static IDangerClassifier Config { get; } = new TableDangerClassifier(ConfigRules);

    /// <summary>Danger classification for <c>tui.json</c>.</summary>
    public static IDangerClassifier Tui { get; } = new TableDangerClassifier(TuiRules);
}
