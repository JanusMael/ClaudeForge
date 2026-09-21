using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Which of Claude Code's settings deserve attention, and when.
/// </summary>
/// <remarks>
/// <para>
/// The matching mechanism is the shell's (<see cref="TableDangerClassifier"/>); the classification
/// here is this product's data — the same split as <c>OpenCodeDangerTable</c> and
/// <c>ClaudePageLayout</c>. Keys are the real top-level properties of the bundled
/// <c>claude-code-settings.json</c>, read from the schema rather than guessed, plus nested
/// refinements where one tier for a whole subtree would be wrong.
/// </para>
/// <para>
/// ⚠ <b>There are 142 of them, not the "~25" the plan estimated</b> — Claude Code's settings
/// surface has grown by roughly 5.7× since that figure was written. Most are presentation, which is
/// why they are grouped under shared sentences below rather than given 70 hand-written variations
/// of "this only changes what you see".
/// </para>
/// <para>
/// ⭐ <b>Every top-level key gets an entry even when the answer is "unremarkable".</b>
/// <c>ClaudeDangerTableTests</c> fails on any schema key with no entry, so a schema refresh that
/// introduces a setting cannot ship until somebody has triaged it. Silence would otherwise read as
/// "safe", which is the wrong default for a knob nobody has looked at.
/// </para>
///
/// <para>
/// <b>Two conventions this table follows, both chosen to keep the banner meaningful.</b>
/// </para>
/// <para>
/// ⛔ <b>1. <see cref="DangerRule.Unsafe"/> fires only when the held value is SPECIFICALLY the
/// boundary-weakening one</b> — <c>bypassPermissions</c>, a sandbox switched off, a bare <c>*</c>
/// in an allowlist, a secret-shaped name in <c>env</c>. It deliberately does NOT fire merely
/// because a powerful feature is configured at all. Marking every populated <c>hooks</c> block as
/// "wrong right now" would put a standing red banner on every real installation, and a banner
/// that is always on is a banner nobody reads. The tier still carries the weight: a Critical dot
/// with no banner says "this area decides what Claude may do", which is true and useful.
/// </para>
/// <para>
/// ⛔ <b>2. A <c>disableX</c> / <c>allowManagedXOnly</c> / <c>strictX</c> key points the SAFE
/// way, so it gets a tier but no predicate.</b> Its unsafe state is the absence of the setting,
/// and "unset" is also the default for every user who has never thought about it — so a predicate
/// here would flag the default configuration of every machine. Hardening knobs are worth a dot
/// (they are how you close a hole) and never a red banner.
/// </para>
///
/// <para>
/// ⓘ <b>Deprecated keys are Info</b>, matching OpenCode's table: <c>includeCoAuthoredBy</c> is
/// superseded by <c>attribution</c> and <c>voiceEnabled</c> by <c>voice.enabled</c>. They read like
/// live switches and are not.
/// </para>
/// </remarks>
public static class ClaudeDangerTable
{
    /// <summary>The classifier for <c>settings.json</c>.</summary>
    public static IDangerClassifier Settings { get; } = new TableDangerClassifier(BuildRules());

    // ── Value-currency helpers ────────────────────────────────────────────────
    //
    // ⚠ Values arrive in the editor value currency (IEditorValue): integers are `long`, objects are
    // IReadOnlyDictionary<string, object?>, arrays are IReadOnlyList<object?>. A predicate written
    // against `int` or `JsonNode` never fires and silently reports "safe".

    private static bool IsTrue(object? v) => v is true;

    private static bool IsFalse(object? v) => v is false;

    private static bool IsSetString(object? v) => v is string { Length: > 0 };

    private static IReadOnlyList<object?>? AsList(object? v) => v as IReadOnlyList<object?>;

    private static IReadOnlyDictionary<string, object?>? AsMap(object? v) =>
        v as IReadOnlyDictionary<string, object?>;

    private static bool IsNonEmptyList(object? v) => AsList(v) is { Count: > 0 };

    private static bool IsNonEmptyMap(object? v) => AsMap(v) is { Count: > 0 };

    /// <summary>A list holding an entry that is nothing but wildcards — i.e. an allowlist that
    /// allows everything, which is the same as having no allowlist while looking like one.</summary>
    private static bool HasBareWildcard(object? v) =>
        AsList(v)?.Any(e => e is string s && s.Length > 0 && s.Trim().All(c => c == '*')) == true;

    /// <summary>Words whose presence in a variable NAME means the value is a credential.</summary>
    /// <remarks>
    /// ⛔ <b><c>TOKENS</c> is deliberately absent while <c>CREDENTIALS</c> is present, and that
    /// asymmetry is domain knowledge rather than an oversight.</b> Claude Code's own environment
    /// has <c>MAX_OUTPUT_TOKENS</c> and <c>MAX_THINKING_TOKENS</c> — token BUDGETS, which the
    /// Essentials page writes itself. A substring test on "TOKEN" flags both, and a dot on a
    /// number the app set for you is exactly the false positive that teaches people to ignore
    /// dots. A name ending in <c>_CREDENTIALS</c> has no such benign reading.
    /// </remarks>
    private static readonly string[] SecretWords =
        ["KEY", "TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "CREDENTIALS"];

    /// <summary>
    /// A name that looks like it holds a credential — matched on word boundaries, not substrings.
    /// </summary>
    /// <remarks>
    /// Matches when the whole name ENDS WITH a secret word (so <c>OPENAI_APIKEY</c> and
    /// <c>ANTHROPIC_API_KEY</c> both hit) or when any underscore-separated segment IS one (so
    /// <c>SECRET_VALUE</c> hits). ⚠ Known and accepted miss: a plural like <c>SECRETS</c> does not
    /// match, because admitting plurals is what re-admits <c>..._TOKENS</c>.
    /// </remarks>
    private static bool LooksSecret(string name) =>
        SecretWords.Any(w => name.EndsWith(w, StringComparison.OrdinalIgnoreCase))
        || name.Split('_', '-', '.', ':')
               .Any(segment => SecretWords.Any(
                   w => string.Equals(segment, w, StringComparison.OrdinalIgnoreCase)));

    /// <summary>An <c>env</c> map holding a variable whose NAME looks like a credential.</summary>
    private static bool HasSecretShapedKey(object? v) =>
        AsMap(v)?.Keys.Any(LooksSecret) == true;

    /// <summary>A list naming a variable that looks like a credential.</summary>
    private static bool NamesSecretShapedVar(object? v) =>
        AsList(v)?.Any(e => e is string s && LooksSecret(s)) == true;

    /// <summary>A permission rule that grants a whole tool rather than a narrow pattern.</summary>
    private static bool HasUnscopedToolRule(object? v) =>
        AsList(v)?.Any(e => e is string s && IsUnscopedRule(s)) == true;

    private static bool IsUnscopedRule(string rule)
    {
        string trimmed = rule.Trim();

        // "Bash" grants every command; "Bash(git log:*)" grants one. A bare tool name, or one
        // whose argument pattern is only wildcards, is the unscoped form.
        int open = trimmed.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            return trimmed.Length > 0;
        }

        string inner = trimmed[(open + 1)..].TrimEnd(')').Trim();
        return inner.Length == 0 || inner.All(c => c == '*' || c == ':');
    }

    /// <summary>
    /// Scopes whose files are committed to git, so a secret written there is published to everyone
    /// with repo access rather than kept locally.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>Compared case-insensitively against the scope id the APP supplies, not against a
    /// ladder rung name.</b> <see cref="ConfigScope.Id"/> lower-cases the rung name, and
    /// <c>OpenCodeDangerTable</c> shipped this predicate comparing ordinally against the
    /// capitalised constant — so its escalation never fired in the running app, behind 85 green
    /// tests that all built their scope from that same constant. Deriving the value from
    /// <see cref="ConfigScope"/> here means it cannot drift from what the adapter produces.
    /// </remarks>
    /// <remarks>
    /// ⚠ <b>Project only, deliberately NOT Local.</b> The ladder's four rungs are Managed / Local
    /// / Project / User, and those two adjacent ones differ in exactly the way that matters here:
    /// <c>.claude/settings.json</c> is committed, while <c>.claude/settings.local.json</c> is
    /// git-ignored by convention and is the file a secret is *supposed* to go in. Escalating Local
    /// too would put a red dot on the correct answer.
    /// </remarks>
    private static bool IsGitCommittedScope(string? scopeId) =>
        string.Equals(scopeId, ConfigScope.Project.Id, StringComparison.OrdinalIgnoreCase);

    // ── Table assembly ────────────────────────────────────────────────────────

    private static Dictionary<string, DangerRule> BuildRules()
    {
        Dictionary<string, DangerRule> rules = new(StringComparer.Ordinal);

        AddPermissionBoundary(rules);
        AddCredentialsAndExecution(rules);
        AddMcpAndPluginTrust(rules);
        AddRemoteAndBrowsing(rules);
        AddPrivacyAndRetention(rules);
        AddAuthRouting(rules);
        AddModelAndCost(rules);
        AddManagedGovernance(rules);
        AddWorkflowAndSkills(rules);
        AddPresentation(rules);

        return rules;
    }

    /// <summary>
    /// Add several keys sharing one tier and one sentence.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Dictionary{TKey, TValue}.Add"/> rather than the indexer on purpose: a key
    /// listed twice across two groups is an authoring mistake, and throwing at construction turns
    /// it into an immediate failure rather than a silent last-one-wins.
    /// </remarks>
    private static void AddAll(
        Dictionary<string, DangerRule> rules, AppSeverity tier, string why, params string[] keys)
    {
        foreach (string key in keys)
        {
            rules.Add(key, new DangerRule { Tier = tier, Why = why });
        }
    }

    // ── 🔴 The permission boundary itself ─────────────────────────────────────

    private static void AddPermissionBoundary(Dictionary<string, DangerRule> rules)
    {
        rules.Add("permissions", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Decides which tools Claude may run without asking you first.",
        });

        rules.Add("permissions.defaultMode", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Sets what happens when no rule matches. Bypassing permissions approves every "
                  + "tool call, including ones that write files and run commands.",
            Unsafe = v => v is string s
                          && s.Contains("bypass", StringComparison.OrdinalIgnoreCase),
        });

        rules.Add("permissions.allow", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Tool calls matching these rules run with no prompt. An unscoped rule such as "
                  + "\"Bash\" grants every command, not one.",
            Unsafe = HasUnscopedToolRule,
        });

        rules.Add("permissions.ask", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Forces a prompt for these tools. Removing a rule here makes the surrounding "
                  + "allow rules apply instead.",
        });

        rules.Add("permissions.deny", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "The last line that stops a tool call outright. Removing a rule widens what "
                  + "Claude may do.",
        });

        rules.Add("permissions.additionalDirectories", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Grants file access outside the project, so tools can read and write there. In "
                  + "the committed project file it grants that to everyone who opens the repo.",
            Unsafe = IsNonEmptyList,
            EscalatesAt = IsGitCommittedScope,
        });

        rules.Add("permissions.disableBypassPermissionsMode", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Whether bypass-permissions mode can be turned on at all. This is the switch "
                  + "that takes the option away from the user.",
        });

        rules.Add("skipDangerousModePermissionPrompt", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Records that the bypass-permissions warning has been accepted, so it stops "
                  + "being shown.",
            Unsafe = IsTrue,
        });

        rules.Add("sandbox", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Confines what Claude's tools can reach on this machine.",
        });

        rules.Add("sandbox.enabled", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "With the sandbox off, tools act directly on the real filesystem and network.",
            Unsafe = IsFalse,
        });

        rules.Add("useAutoModeDuringPlan", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Auto-approves read-only tool calls while planning, without asking.",
            Unsafe = IsTrue,
        });

        rules.Add("autoMode", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Tunes the classifier that decides which tool calls auto mode approves for you.",
        });
    }

    // ── 🔴 Credentials, and anything that runs a command ─────────────────────

    private static void AddCredentialsAndExecution(Dictionary<string, DangerRule> rules)
    {
        rules.Add("hooks", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Runs your own commands automatically at lifecycle points, with your privileges.",
        });

        // ⚠ Caution at a private scope and Critical in the committed project file — the same
        // judgement OpenCode's table makes about an API key. A secret in ~/.claude/settings.json
        // is plaintext on your own disk; the identical secret in .claude/settings.json is
        // published to everyone who can read the repo. Escalation is only observable from a base
        // tier BELOW Critical, so a key pinned at Critical cannot express this at all.
        rules.Add("env", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Environment variables for every session, stored as plain text in this file.",
            Unsafe = HasSecretShapedKey,
            EscalatesAt = IsGitCommittedScope,
        });

        rules.Add("apiKeyHelper", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "A script that prints authentication values, run whenever credentials are needed.",
            Unsafe = IsSetString,
        });

        rules.Add("statusLine", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Runs a command of yours to draw the status line, repeatedly during a session.",
        });

        rules.Add("subagentStatusLine", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Runs a command of yours to draw a subagent's status line.",
        });

        rules.Add("fileSuggestion", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Runs a script of yours to produce @ file suggestions as you type.",
        });

        rules.Add("policyHelper", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "An executable that computes managed settings at startup, so it decides the "
                  + "policy the rest of this file is merged into.",
        });

        rules.Add("processWrapper", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Placed in front of every background process Claude Code starts.",
            Unsafe = IsSetString,
        });

        rules.Add("disableSkillShellExecution", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Whether skills and slash commands may run inline shell commands of their own.",
        });

        rules.Add("disableAllHooks", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Turns off all hooks and status-line execution — the switch that stops "
                  + "configured commands from running.",
        });

        rules.Add("defaultShell", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Which shell interprets your ! commands, and therefore how they are parsed.",
        });

        AddAll(rules, AppSeverity.Critical,
            "A command that prints cloud credentials, run automatically when they expire.",
            "awsAuthRefresh", "awsCredentialExport", "gcpAuthRefresh");

        rules.Add("otelHeadersHelper", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "A command that prints telemetry headers, which normally carry an auth token.",
            Unsafe = IsSetString,
        });

        rules.Add("allowedHttpHookUrls", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Limits where HTTP hooks may send data. An entry of just \"*\" allows every "
                  + "destination, which is the same as having no allowlist.",
            Unsafe = HasBareWildcard,
        });

        rules.Add("httpHookAllowedEnvVars", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Environment variables HTTP hooks may interpolate into outbound headers, so "
                  + "each one named here can leave the machine.",
            Unsafe = NamesSecretShapedVar,
        });
    }

    // ── 🔴 MCP servers and plugin trust — third-party code ───────────────────

    private static void AddMcpAndPluginTrust(Dictionary<string, DangerRule> rules)
    {
        rules.Add("enableAllProjectMcpServers", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Approves every MCP server a project declares, without asking. A cloned repo "
                  + "can then start servers of its choosing.",
            Unsafe = IsTrue,
        });

        rules.Add("allowAllClaudeAiMcps", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Loads claude.ai connectors alongside the deployed managed server list, "
                  + "widening what is reachable beyond what was deployed.",
            Unsafe = IsTrue,
        });

        rules.Add("managedMcpServers", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "MCP server configurations pushed to every user of this deployment.",
        });

        rules.Add("enabledMcpjsonServers", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Servers approved out of a project's .mcp.json — each one runs as a process or "
                  + "reaches a remote endpoint.",
        });

        rules.Add("enabledPlugins", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Third-party plugin code that loads into your sessions.",
        });

        rules.Add("extraKnownMarketplaces", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Adds a source Claude will install and run code from. In the committed project "
                  + "file, every collaborator inherits that trust decision.",
            Unsafe = IsNonEmptyMap,
            EscalatesAt = IsGitCommittedScope,
        });

        rules.Add("sshConfigs", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Pre-configured SSH connections distributed to users, so a session can reach "
                  + "those hosts.",
        });

        AddAll(rules, AppSeverity.Caution,
            "Restricts which MCP servers may be used. Narrowing this list is how you close the "
            + "surface; widening it opens it.",
            "allowedMcpServers", "deniedMcpServers", "disabledMcpjsonServers");

        AddAll(rules, AppSeverity.Caution,
            "Governs which plugin marketplaces and channel plugins are trusted, and therefore "
            + "whose code can be installed.",
            "strictKnownMarketplaces", "blockedMarketplaces", "pluginSuggestionMarketplaces",
            "allowedChannelPlugins", "strictPluginOnlyCustomization", "pluginConfigs");

        rules.Add("sshHostAllowlist", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Restricts which hosts a Desktop SSH session may reach.",
        });

        rules.Add("disableClaudeAiConnectors", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Stops claude.ai MCP connectors from being fetched and connected automatically.",
        });

        rules.Add("disableSideloadFlags", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Rejects the CLI flags that load plugins, agents and MCP config from arbitrary "
                  + "paths at startup.",
        });

        AddAll(rules, AppSeverity.Info,
            "A record of what you declined when prompted. Editing it only changes whether you are "
            + "asked again.",
            "skippedMarketplaces", "skippedPlugins");

        rules.Add("pluginTrustMessage", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Extra wording appended to the plugin trust warning. Advisory text only — it "
                  + "changes nothing about what a plugin may do.",
        });
    }

    // ── 🟠 Remote control and browsing — the machine reachable from elsewhere ─

    private static void AddRemoteAndBrowsing(Dictionary<string, DangerRule> rules)
    {
        rules.Add("remoteControlAtStartup", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Connects Remote Control automatically, so another device can drive this "
                  + "session from the moment it starts.",
            Unsafe = IsTrue,
        });

        rules.Add("skipWebFetchPreflight", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Skips the blocklist check before fetching a URL.",
            Unsafe = IsTrue,
        });

        rules.Add("disableRemoteControl", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Blocks Remote Control entirely — the switch that stops this machine being "
                  + "driven from another device.",
        });

        AddAll(rules, AppSeverity.Caution,
            "Governs whether Claude's tools may read and act on external web pages.",
            "disableBrowserExternalNavigation", "browserExternalPageTools");

        AddAll(rules, AppSeverity.Caution,
            "Turns off a surface through which Claude can act on your machine.",
            "disableAgentView", "disableMobileSimulatorTools", "disableDeepLinkRegistration",
            "requireCoworkFullVmSandbox");

        rules.Add("agentPushNotifEnabled", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Lets Claude send proactive notifications to your phone, which carry session "
                  + "content off this machine.",
            Unsafe = IsTrue,
        });

        rules.Add("channelsEnabled", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Allows channels for the organization, which share sessions with other people.",
        });
    }

    // ── 🟠 Privacy and what stays on disk ────────────────────────────────────

    private static void AddPrivacyAndRetention(Dictionary<string, DangerRule> rules)
    {
        rules.Add("respectGitignore", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "With this off, the @ picker offers git-ignored files — which is where local "
                  + "secrets usually live.",
            Unsafe = IsFalse,
        });

        rules.Add("enableArtifact", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Lets Claude publish session output as a web page on claude.ai.",
            Unsafe = IsTrue,
        });

        rules.Add("disableArtifact", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Turns off publishing session output as a web page.",
        });

        rules.Add("autoMemoryEnabled", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Writes context from your sessions to disk automatically, without being asked.",
            Unsafe = IsTrue,
        });

        rules.Add("cleanupPeriodDays", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "How long transcripts, snapshots and backups stay on disk. A long window keeps "
                  + "more of your history; a short one destroys it sooner.",
        });

        rules.Add("claudeMd", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Organization-managed instructions injected into every session's context.",
        });

        rules.Add("voice", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Voice dictation, which turns on the microphone and sends audio for "
                  + "transcription.",
        });

        AddAll(rules, AppSeverity.Info,
            "Where a category of local files is written. It moves data on your own disk and "
            + "nothing leaves the machine.",
            "autoMemoryDirectory", "plansDirectory");

        AddAll(rules, AppSeverity.Info,
            "Notification and recap preferences. They change what you are told, not what Claude "
            + "may do.",
            "inputNeededNotifEnabled", "preferredNotifChannel", "awaySummaryEnabled");

        rules.Add("voiceEnabled", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Deprecated alias for voice.enabled — set the voice object instead, which is "
                  + "what the app actually reads.",
        });

        rules.Add("claudeMdExcludes", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Skips CLAUDE.md files matching these globs, so their instructions are not "
                  + "loaded.",
        });

        rules.Add("footerLinksRegexes", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Matches turn output to render extra footer badges, so it reads your output "
                  + "without changing it.",
        });

        rules.Add("fileCheckpointingEnabled", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Snapshots edited files so /rewind can restore them. Turning it off means "
                  + "edits cannot be undone that way.",
        });

        rules.Add("feedbackSurveyRate", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "How often the session quality survey appears.",
        });
    }

    // ── 🟠 Where authentication is sent ──────────────────────────────────────

    private static void AddAuthRouting(Dictionary<string, DangerRule> rules)
    {
        rules.Add("forceLoginGatewayUrl", new DangerRule
        {
            Tier = AppSeverity.Critical,
            Why = "Locks login to this gateway, which then receives the authentication exchange.",
            Unsafe = IsSetString,
        });

        AddAll(rules, AppSeverity.Caution,
            "Constrains how and where you may sign in, and which organization the session "
            + "belongs to.",
            "forceLoginMethod", "forceLoginOrgUUID");
    }

    // ── 🟠 Model choice, effort and cost ─────────────────────────────────────

    private static void AddModelAndCost(Dictionary<string, DangerRule> rules)
    {
        rules.Add("modelOverrides", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Repoints model IDs at provider-specific endpoints, so prompts go somewhere "
                  + "other than the default.",
        });

        rules.Add("agent", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Replaces the main thread's system prompt and tool restrictions with that "
                  + "agent's.",
        });

        rules.Add("fastMode", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "Faster output at a higher per-token cost.",
        });

        rules.Add("effortLevel", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "How much reasoning Claude spends per turn, which is also how much you pay "
                  + "per turn.",
        });

        rules.Add("askUserQuestionTimeout", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "How long an unanswered question waits before continuing on its own with "
                  + "whatever was already selected.",
        });

        AddAll(rules, AppSeverity.Caution,
            "Restricts or extends which models may be selected.",
            "availableModels", "enforceAvailableModels");

        AddAll(rules, AppSeverity.Caution,
            "Pins which Claude Code versions may run. A pin that is too tight keeps you on a "
            + "build that no longer receives fixes.",
            "minimumVersion", "requiredMinimumVersion", "requiredMaximumVersion",
            "autoUpdatesChannel");

        AddAll(rules, AppSeverity.Info,
            "Which model is used for a particular job. It changes answer quality and cost, not "
            + "what Claude is permitted to do.",
            "model", "advisorModel", "fallbackModel", "teammateDefaultModel");

        AddAll(rules, AppSeverity.Info,
            "Reasoning and context-management behaviour. Affects how a turn is spent, not what "
            + "it may touch.",
            "alwaysThinkingEnabled", "autoCompactEnabled", "fastModePerSessionOptIn",
            "disableAutoMode");
    }

    // ── 🟠 Managed-settings governance ───────────────────────────────────────

    private static void AddManagedGovernance(Dictionary<string, DangerRule> rules)
    {
        AddAll(rules, AppSeverity.Caution,
            "Restricts what user and project settings may override, so it decides whose policy "
            + "wins.",
            "allowManagedHooksOnly", "allowManagedMcpServersOnly", "allowManagedPermissionRulesOnly",
            "parentSettingsBehavior", "wslInheritsWindowsSettings", "forceRemoteSettingsRefresh");

        rules.Add("companyAnnouncements", new DangerRule
        {
            Tier = AppSeverity.Neutral,
            Why = "Messages shown at startup. Display only.",
        });

        rules.Add("prUrlTemplate", new DangerRule
        {
            Tier = AppSeverity.Neutral,
            Why = "Builds the pull-request link shown in the footer.",
        });

        rules.Add("$schema", new DangerRule
        {
            Tier = AppSeverity.Neutral,
            Why = "Points editors at the schema for this file. It affects tooling only, never "
                  + "Claude's behaviour.",
        });
    }

    // ── 🟠 Skills, workflows and agent teams ─────────────────────────────────

    private static void AddWorkflowAndSkills(Dictionary<string, DangerRule> rules)
    {
        rules.Add("workflowSizeGuideline", new DangerRule
        {
            Tier = AppSeverity.Caution,
            Why = "How many agents a dynamic workflow aims to spawn, which is the main driver of "
                  + "what one costs.",
        });

        AddAll(rules, AppSeverity.Caution,
            "Removes a whole class of bundled customization, so skills or workflows you rely on "
            + "may stop being available.",
            "disableBundledSkills", "disableWorkflows");

        AddAll(rules, AppSeverity.Info,
            "Which skills Claude can see, and how much of the context window their listing may "
            + "use. Changes what is offered, not what is permitted.",
            "skillOverrides", "skillListingBudgetFraction", "skillListingMaxDescChars");

        AddAll(rules, AppSeverity.Info,
            "Agent-team and workflow ergonomics — how teammates are displayed and how a workflow "
            + "is triggered.",
            "teammateMode", "workflowKeywordTriggerEnabled");

        rules.Add("worktree", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "How --worktree sessions are set up.",
        });
    }

    // ── ⚪ Presentation and ergonomics ───────────────────────────────────────

    private static void AddPresentation(Dictionary<string, DangerRule> rules)
    {
        AddAll(rules, AppSeverity.Neutral,
            "Presentation only — it changes what you see, never what Claude may do.",
            "theme", "tui", "viewMode", "verbose", "language", "outputStyle",
            "syntaxHighlightingDisabled", "emojiCompletionEnabled", "autoScrollEnabled",
            "wheelScrollAccelerationEnabled", "terminalProgressBarEnabled", "showThinkingSummaries",
            "showTurnDuration", "showClearContextOnPlanAccept", "spinnerTipsEnabled",
            "spinnerTipsOverride", "spinnerVerbs", "diffTool");

        AddAll(rules, AppSeverity.Neutral,
            "Accessibility and input preferences. They change how you interact with the "
            + "interface, nothing about Claude's reach.",
            "axScreenReader", "prefersReducedMotion", "editorMode", "vimInsertModeRemaps",
            "externalEditorContext", "respondToBashCommands", "permissionExplainerEnabled");

        AddAll(rules, AppSeverity.Neutral,
            "IDE convenience. Connecting or installing the extension does not change what tools "
            + "may run.",
            "autoConnectIde", "autoInstallIdeExtension");

        AddAll(rules, AppSeverity.Neutral,
            "Whether Claude's own boilerplate appears in commits, PRs and its system prompt.",
            "attribution", "includeGitInstructions");

        rules.Add("includeCoAuthoredBy", new DangerRule
        {
            Tier = AppSeverity.Info,
            Why = "Deprecated — use attribution instead, which is what the app now reads.",
        });
    }
}
