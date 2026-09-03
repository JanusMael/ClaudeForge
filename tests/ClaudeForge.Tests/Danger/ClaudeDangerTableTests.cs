using System.Text.Json;

using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Danger;

/// <summary>
/// The Claude danger table's two obligations: <b>cover every schema key</b>, and <b>get the
/// predicates right</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The coverage test is the one that earns its keep long-term.</b> A danger table degrades
/// silently: when Claude Code adds a setting, an unclassified key simply reads as unremarkable,
/// which is the worst possible default for a knob nobody has triaged. Comparing the table against
/// the bundled schema's real top-level keys turns that silence into a build failure — and this
/// schema moves fast, which is how it reached 142 keys against a plan that budgeted ~25.
/// </para>
/// <para>
/// ⛔ <b>The scope-sensitive assertions use <see cref="ConfigScopeAdapter"/>, not a local test
/// double.</b> OpenCode's equivalent table shipped an escalation that never fired, because every
/// test built its scope from the same constant the predicate compared against — tautological with
/// respect to casing. The scope object here is the one the app really hands the classifier.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClaudeDangerTableTests
{
    private static readonly IEditorScope ProjectScope = ConfigScopeAdapter.For(ConfigScope.Project);
    private static readonly IEditorScope LocalScope = ConfigScopeAdapter.For(ConfigScope.Local);
    private static readonly IEditorScope UserScope = ConfigScopeAdapter.For(ConfigScope.User);

    // ── Coverage: the anti-rot guard ──────────────────────────────────────────

    [TestMethod]
    public void EverySettingsSchemaKeyIsClassified()
    {
        IReadOnlyCollection<string> keys = SettingsSchemaKeys();

        // A scan that finds nothing proves nothing.
        Assert.IsTrue(keys.Count >= 100,
            $"only {keys.Count} top-level keys read from claude-code-settings.json; the schema "
            + "reader is broken, not the table. It had 142 when this table was written.");

        List<string> unclassified = [];

        foreach (string key in keys)
        {
            // Unremarkable is what an unmatched path returns, so it doubles as "nobody triaged
            // this". A key genuinely deserving no attention must still say so explicitly.
            if (ClaudeDangerTable.Settings.Classify(key, UserScope, currentValue: null).Explanation is null)
            {
                unclassified.Add(key);
            }
        }

        if (unclassified.Count > 0)
        {
            Assert.Fail(
                $"claude-code-settings.json has {unclassified.Count} top-level key(s) with no "
                + $"entry in ClaudeDangerTable:\n  {string.Join("\n  ", unclassified)}\n\n"
                + "A key with no entry renders with no severity and no explanation, which reads to "
                + "the user as 'nothing to worry about'. Classify it — Neutral with a one-line "
                + "explanation is a valid answer, silence is not.");
        }
    }

    [TestMethod]
    public void EveryClassifiedPathExplainsItself()
    {
        foreach (string pattern in ClaudeDangerTable.Settings.ClassifiedPaths)
        {
            string path = pattern.Replace("*", "x", StringComparison.Ordinal);

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    ClaudeDangerTable.Settings.Classify(path, UserScope, null).Explanation),
                $"pattern '{pattern}' produced no explanation. Every rule must be able to tell "
                + "the user why it matters.");
        }
    }

    [TestMethod]
    public void NoClassifiedPathIsAStaleSchemaKey()
    {
        // A nested refinement is legitimately absent from the top-level list, so only single-segment
        // patterns are checked. This catches a key REMOVED from the schema whose rule lingers —
        // the opposite rot from the coverage test, and the one that makes a table look thorough.
        HashSet<string> keys = [.. SettingsSchemaKeys()];

        List<string> stale = [.. ClaudeDangerTable.Settings.ClassifiedPaths
            .Where(p => !p.Contains('.', StringComparison.Ordinal))
            .Where(p => !keys.Contains(p))];

        Assert.IsTrue(stale.Count == 0,
            $"{stale.Count} rule(s) name a top-level key the schema no longer has: "
            + $"{string.Join(", ", stale)}. Either the key was renamed upstream (move the rule) or "
            + "it is gone (delete it) — a rule matching nothing is dead weight that still reads as "
            + "coverage.");
    }

    // ── Tiers: the keys whose classification is the whole point ──────────────

    [TestMethod]
    [DataRow("permissions", AppSeverity.Critical)]
    [DataRow("permissions.defaultMode", AppSeverity.Critical)]
    [DataRow("permissions.allow", AppSeverity.Critical)]
    [DataRow("permissions.disableBypassPermissionsMode", AppSeverity.Critical)]
    [DataRow("sandbox", AppSeverity.Critical)]
    [DataRow("sandbox.enabled", AppSeverity.Critical)]
    [DataRow("hooks", AppSeverity.Critical)]
    [DataRow("apiKeyHelper", AppSeverity.Critical)]
    [DataRow("statusLine", AppSeverity.Critical)]
    [DataRow("enableAllProjectMcpServers", AppSeverity.Critical)]
    [DataRow("enabledPlugins", AppSeverity.Critical)]
    [DataRow("env", AppSeverity.Caution)]
    [DataRow("extraKnownMarketplaces", AppSeverity.Caution)]
    [DataRow("skipDangerousModePermissionPrompt", AppSeverity.Critical)]
    [DataRow("remoteControlAtStartup", AppSeverity.Critical)]
    [DataRow("respectGitignore", AppSeverity.Caution)]
    [DataRow("cleanupPeriodDays", AppSeverity.Caution)]
    [DataRow("fastMode", AppSeverity.Caution)]
    [DataRow("model", AppSeverity.Info)]
    [DataRow("includeCoAuthoredBy", AppSeverity.Info)]
    [DataRow("theme", AppSeverity.Neutral)]
    [DataRow("$schema", AppSeverity.Neutral)]
    public void KeyHasExpectedTier(string path, AppSeverity expected)
    {
        Assert.AreEqual(expected,
            ClaudeDangerTable.Settings.Classify(path, UserScope, null).Severity,
            $"'{path}' is classified at the wrong tier.");
    }

    // ── Predicates: is the value held right now the unsafe one? ──────────────

    [TestMethod]
    public void BypassPermissionsIsFlaggedAndAcceptEditsIsNot()
    {
        Assert.IsTrue(
            ClaudeDangerTable.Settings
                .Classify("permissions.defaultMode", UserScope, "bypassPermissions").IsDangerNow,
            "bypassPermissions approves every tool call and must read as unsafe right now.");

        Assert.IsFalse(
            ClaudeDangerTable.Settings
                .Classify("permissions.defaultMode", UserScope, "acceptEdits").IsDangerNow,
            "acceptEdits still prompts for anything beyond edits — flagging it would train the "
            + "user to ignore the banner.");
    }

    [TestMethod]
    public void AnUnscopedAllowRuleIsFlaggedButAScopedOneIsNot()
    {
        Assert.IsTrue(Allow("Bash").IsDangerNow, "A bare tool name grants every call to it.");
        Assert.IsTrue(Allow("Bash(*)").IsDangerNow, "A wildcard argument grants every command.");
        Assert.IsTrue(Allow("Bash(:*)").IsDangerNow, "So does a bare prefix wildcard.");

        Assert.IsFalse(Allow("Bash(git log:*)").IsDangerNow,
            "A scoped rule is the RECOMMENDED form. Flagging it would make the correct answer "
            + "look like the dangerous one.");
        Assert.IsFalse(Allow("Read(src/**)").IsDangerNow);

        static DangerAssessment Allow(string rule) =>
            ClaudeDangerTable.Settings.Classify("permissions.allow", UserScope, new object?[] { rule });
    }

    [TestMethod]
    public void ASandboxSwitchedOffIsFlaggedAndOneSwitchedOnIsNot()
    {
        Assert.IsTrue(ClaudeDangerTable.Settings.Classify("sandbox.enabled", UserScope, false).IsDangerNow);
        Assert.IsFalse(ClaudeDangerTable.Settings.Classify("sandbox.enabled", UserScope, true).IsDangerNow);
    }

    [TestMethod]
    public void ARespectedGitignoreIsSafeAndAnIgnoredOneIsNot()
    {
        Assert.IsTrue(ClaudeDangerTable.Settings.Classify("respectGitignore", UserScope, false).IsDangerNow,
            "With gitignore ignored the picker offers exactly the files secrets live in.");
        Assert.IsFalse(ClaudeDangerTable.Settings.Classify("respectGitignore", UserScope, true).IsDangerNow);
    }

    [TestMethod]
    public void OnlyASecretShapedEnvKeyIsFlagged()
    {
        Assert.IsTrue(Env("ANTHROPIC_API_KEY").IsDangerNow, "A key name is a credential name.");
        Assert.IsTrue(Env("MY_TOKEN").IsDangerNow);
        Assert.IsTrue(Env("db_password").IsDangerNow, "Case must not matter.");
        Assert.IsTrue(Env("OPENAI_APIKEY").IsDangerNow, "No separator before KEY must still hit.");

        Assert.IsFalse(Env("MAX_OUTPUT_TOKENS").IsDangerNow,
            "⚠ This one is load-bearing: 'TOKENS' contains 'TOKEN', but a token BUDGET is not a "
            + "credential — and MAX_OUTPUT_TOKENS is a real key the Essentials page writes itself. "
            + "A substring match flags it, and a dot on a value the app set for you is the false "
            + "positive that teaches people to ignore dots.");
        Assert.IsFalse(Env("MAX_THINKING_TOKENS").IsDangerNow);
        Assert.IsFalse(Env("DISABLE_TELEMETRY").IsDangerNow);

        static DangerAssessment Env(string name) =>
            ClaudeDangerTable.Settings.Classify(
                "env", UserScope, new Dictionary<string, object?> { [name] = "x" });
    }

    [TestMethod]
    public void ABareWildcardHookUrlIsFlaggedButARealPatternIsNot()
    {
        Assert.IsTrue(Urls("*").IsDangerNow,
            "An allowlist of '*' allows every destination while looking like a restriction.");
        Assert.IsFalse(Urls("https://hooks.example.com/*").IsDangerNow,
            "A pattern with a real host is the intended use.");

        static DangerAssessment Urls(string pattern) =>
            ClaudeDangerTable.Settings.Classify(
                "allowedHttpHookUrls", UserScope, new object?[] { pattern });
    }

    [TestMethod]
    public void AHardeningKeyNeverClaimsToBeWrongRightNow()
    {
        // Their unsafe state is ABSENCE, and absence is also the default for everyone who has
        // never thought about them — so a predicate here would flag every stock machine.
        foreach (string key in new[]
                 {
                     "permissions.disableBypassPermissionsMode", "disableAllHooks",
                     "disableSkillShellExecution", "disableRemoteControl", "disableSideloadFlags",
                     "allowManagedHooksOnly", "requireCoworkFullVmSandbox",
                 })
        {
            foreach (object? value in new object?[] { true, false, null })
            {
                Assert.IsFalse(ClaudeDangerTable.Settings.Classify(key, UserScope, value).IsDangerNow,
                    $"'{key}' points the SAFE way, so no value of it is 'wrong right now'. It "
                    + "still carries a tier, which is the signal that it matters.");
            }
        }
    }

    [TestMethod]
    public void AConfiguredHooksBlockIsNotItselfFlagged()
    {
        DangerAssessment a = ClaudeDangerTable.Settings.Classify(
            "hooks", UserScope, new Dictionary<string, object?> { ["PreToolUse"] = "x" });

        Assert.AreEqual(AppSeverity.Critical, a.Severity, "It still deserves the dot.");
        Assert.IsFalse(a.IsDangerNow,
            "Having hooks configured is the normal state of a real installation. A standing red "
            + "banner on every machine is a banner nobody reads.");
    }

    // ── Scope escalation, through the adapter the app supplies ────────────────

    /// <summary>
    /// The three escalating keys, asserted at all three writable scopes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>This is the assertion the OpenCode table could not make.</b> Escalation is only
    /// observable when the base tier is BELOW Critical — a key pinned at Critical escalates to
    /// Critical and nothing changes, which is a silent no-op that still reads as a configured
    /// feature. Writing this test is what exposed exactly that mistake in the first draft of
    /// this table.
    /// </para>
    /// <para>
    /// ⚠ <b>Local must NOT escalate.</b> <c>.claude/settings.local.json</c> is git-ignored by
    /// convention — it is the file a machine-local secret is *supposed* to live in, so reddening
    /// it would put the warning on the correct answer.
    /// </para>
    /// </remarks>
    [TestMethod]
    [DataRow("env")]
    [DataRow("extraKnownMarketplaces")]
    [DataRow("permissions.additionalDirectories")]
    public void AnEscalatingKeyIsWorseOnlyInTheCommittedProjectFile(string path)
    {
        object? value = path switch
        {
            "env" => new Dictionary<string, object?> { ["ANTHROPIC_API_KEY"] = "sk-live-xxx" },
            "extraKnownMarketplaces" => new Dictionary<string, object?> { ["internal"] = "x" },
            _ => new object?[] { "/etc" },
        };

        AppSeverity atUser = ClaudeDangerTable.Settings.Classify(path, UserScope, value).Severity;
        AppSeverity atLocal = ClaudeDangerTable.Settings.Classify(path, LocalScope, value).Severity;
        AppSeverity atProject = ClaudeDangerTable.Settings.Classify(path, ProjectScope, value).Severity;

        Assert.AreEqual(AppSeverity.Caution, atUser,
            $"Premise: '{path}' must be BELOW Critical at a private scope, or the escalation "
            + "below cannot be observed and the rule's EscalatesAt is a silent no-op.");
        Assert.AreEqual(AppSeverity.Caution, atLocal,
            "settings.local.json is git-ignored, so it is not published and must not escalate.");
        Assert.AreEqual(AppSeverity.Critical, atProject,
            $"'{path}' in the committed .claude/settings.json is shared with everyone who can "
            + "read the repo, and must escalate. If this fails, check the scope id the adapter "
            + $"reports ('{ProjectScope.Id}') against what IsGitCommittedScope compares.");
    }

    [TestMethod]
    public void ANullScopeDoesNotEscalate()
    {
        // An unknown scope is not evidence of danger, and the predicate must tolerate null.
        DangerAssessment a = ClaudeDangerTable.Settings.Classify(
            "extraKnownMarketplaces",
            scope: null,
            new Dictionary<string, object?> { ["internal"] = "x" });

        Assert.AreEqual(AppSeverity.Caution, a.Severity,
            "An unknown scope is not evidence of danger, so the key keeps its base tier rather "
            + "than escalating. The predicate must also tolerate the null rather than throwing.");
    }

    // ── Inheritance: the mechanism the per-top-level-key table relies on ─────

    [TestMethod]
    public void ANestedPathWithNoRuleInheritsItsAncestorsTier()
    {
        DangerAssessment a = ClaudeDangerTable.Settings.Classify(
            "sandbox.network.allowUnixSockets", UserScope, true);

        Assert.AreEqual(AppSeverity.Critical, a.Severity,
            "An unlisted path under sandbox must inherit sandbox's tier — that inheritance is what "
            + "makes a table of top-level keys cover a nested schema.");
        Assert.IsFalse(a.IsDangerNow,
            "But an INHERITED assessment must never claim a specific value is wrong: the "
            + "ancestor's predicate was written for the ancestor's value.");
    }

    private static IReadOnlyCollection<string> SettingsSchemaKeys()
    {
        using JsonDocument doc = JsonDocument.Parse(
            File.ReadAllText(SchemaPath("claude-code-settings.json")));

        return [.. doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name)];
    }

    private static string SchemaPath(string fileName) =>
        Path.Combine(FindRepoRoot(), "src", "AgentForge.Core", "Assets", "Schemas", fileName);

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
