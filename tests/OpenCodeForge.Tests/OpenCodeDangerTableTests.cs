using System.Text.Json;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The danger table's two obligations: <b>cover every schema key</b>, and <b>get the predicates
/// right</b>.
///
/// <para>
/// ⭐ <b>The coverage test is the one that earns its keep long-term.</b> A danger table degrades
/// silently: when OpenCode adds a setting, an unclassified key simply reads as unremarkable, which
/// is the worst possible default for a knob nobody has triaged. Comparing the table against the
/// bundled schema's real top-level keys turns that silence into a build failure.
/// </para>
/// </summary>
[TestClass]
public sealed class OpenCodeDangerTableTests
{
    /// <summary>A scope stand-in — only <see cref="IEditorScope.Id"/> matters here.</summary>
    private sealed record Scope(string Id) : IEditorScope
    {
        public int Priority => 0;

        public string DisplayName => Id;

        public bool IsReadOnly => false;
    }

    private static readonly Scope ProjectScope = new(OpenCodeScopes.Project);
    private static readonly Scope GlobalScope = new(OpenCodeScopes.Global);

    // ── Coverage: the anti-rot guard ──────────────────────────────────────────

    [TestMethod]
    public void EveryConfigSchemaKeyIsClassified()
    {
        AssertEveryKeyClassified(ConfigSchemaKeys(), OpenCodeDangerTable.Config, "opencode-config.json");
    }

    [TestMethod]
    public void EveryTuiSchemaKeyIsClassified()
    {
        AssertEveryKeyClassified(TuiSchemaKeys(), OpenCodeDangerTable.Tui, "opencode-tui.json");
    }

    private static void AssertEveryKeyClassified(
        IReadOnlyCollection<string> keys, IDangerClassifier classifier, string schema)
    {
        // A scan that finds nothing proves nothing — the schemas are 36 and 13 keys.
        Assert.IsTrue(keys.Count >= 13,
            $"only {keys.Count} top-level keys read from {schema}; the schema reader is broken, "
            + "not the table. config.json has 36 and tui.json has 13.");

        List<string> unclassified = [];

        foreach (string key in keys)
        {
            DangerAssessment assessment = classifier.Classify(key, GlobalScope, currentValue: null);

            // Unremarkable is what an unmatched path returns, so it doubles as "nobody triaged
            // this". A key genuinely deserving no attention must still say so explicitly — the
            // table gives $schema a Neutral rule with an explanation for exactly this reason.
            if (assessment.Explanation is null)
            {
                unclassified.Add(key);
            }
        }

        if (unclassified.Count > 0)
        {
            Assert.Fail(
                $"{schema} has {unclassified.Count} top-level key(s) with no entry in "
                + $"OpenCodeDangerTable: {string.Join(", ", unclassified)}.\n\n"
                + "A key with no entry renders with no severity and no explanation, which reads "
                + "to the user as 'nothing to worry about'. Classify it — Neutral with a one-line "
                + "explanation is a valid answer, silence is not.");
        }
    }

    [TestMethod]
    public void EveryClassifiedPathExplainsItself()
    {
        foreach ((IDangerClassifier classifier, string name) in new[]
                 {
                     (OpenCodeDangerTable.Config, "Config"),
                     (OpenCodeDangerTable.Tui, "Tui"),
                 })
        {
            foreach (string pattern in classifier.ClassifiedPaths)
            {
                // Wildcards stand in for a real segment so the pattern matches itself.
                string path = pattern.Replace("*", "x", StringComparison.Ordinal);
                DangerAssessment a = classifier.Classify(path, GlobalScope, currentValue: null);

                Assert.IsFalse(string.IsNullOrWhiteSpace(a.Explanation),
                    $"{name} pattern '{pattern}' produced no explanation. Every rule must be able "
                    + "to tell the user why it matters.");
            }
        }
    }

    // ── Tiers ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("permission", AppSeverity.Critical)]
    [DataRow("permission.bash", AppSeverity.Critical)]
    [DataRow("share", AppSeverity.Critical)]
    [DataRow("snapshot", AppSeverity.Critical)]
    [DataRow("server.hostname", AppSeverity.Critical)]
    [DataRow("instructions", AppSeverity.Critical)]
    [DataRow("model", AppSeverity.Caution)]
    [DataRow("subagent_depth", AppSeverity.Caution)]
    [DataRow("lsp", AppSeverity.Caution)]
    [DataRow("autoupdate", AppSeverity.Info)]
    [DataRow("username", AppSeverity.Info)]
    [DataRow("server.port", AppSeverity.Info)]
    [DataRow("$schema", AppSeverity.Neutral)]
    public void ConfigTiersMatchTheReviewedTable(string path, AppSeverity expected)
    {
        Assert.AreEqual(expected,
            OpenCodeDangerTable.Config.Classify(path, GlobalScope, currentValue: null).Severity,
            $"tier for '{path}'");
    }

    [TestMethod]
    [DataRow("plugin", AppSeverity.Critical)]
    [DataRow("plugin_enabled", AppSeverity.Critical)]
    [DataRow("keybinds", AppSeverity.Info)]
    [DataRow("theme", AppSeverity.Info)]
    public void TuiTiersMatchTheReviewedTable(string path, AppSeverity expected)
    {
        Assert.AreEqual(expected,
            OpenCodeDangerTable.Tui.Classify(path, GlobalScope, currentValue: null).Severity,
            $"tier for '{path}'");
    }

    // ── Value predicates: safe value → safe, unsafe value → flagged ───────────

    [TestMethod]
    [DataRow("permission.bash", "allow", true)]
    [DataRow("permission.bash", "ask", false)]
    [DataRow("permission.bash", "deny", false)]
    [DataRow("permission.edit", "allow", true)]
    [DataRow("permission.edit", "ask", false)]
    [DataRow("permission.external_directory", "allow", true)]
    [DataRow("permission.webfetch", "allow", true)]
    [DataRow("permission.websearch", "allow", true)]
    [DataRow("permission", "allow", true)]
    [DataRow("permission", "ask", false)]
    [DataRow("share", "auto", true)]
    [DataRow("share", "manual", false)]
    [DataRow("share", "disabled", false)]
    [DataRow("enterprise.url", "https://corp.example", true)]
    [DataRow("enterprise.url", "", false)]
    [DataRow("server.hostname", "0.0.0.0", true)]
    [DataRow("server.hostname", "192.168.1.10", true)]
    [DataRow("server.hostname", "127.0.0.1", false)]
    [DataRow("server.hostname", "localhost", false)]
    [DataRow("server.hostname", "::1", false)]
    [DataRow("server.cors", "*", true)]
    [DataRow("server.cors", "https://app.example", false)]
    [DataRow("provider.anthropic.options.baseURL", "https://proxy.example", true)]
    [DataRow("provider.anthropic.options.baseURL", "", false)]
    public void StringPredicatesFlagOnlyTheUnsafeValue(string path, string value, bool expected)
    {
        Assert.AreEqual(expected,
            OpenCodeDangerTable.Config.Classify(path, GlobalScope, value).IsDangerNow,
            $"IsDangerNow for '{path}' = '{value}'");
    }

    [TestMethod]
    [DataRow("snapshot", false, true)]
    [DataRow("snapshot", true, false)]
    [DataRow("compaction.auto", false, true)]
    [DataRow("compaction.auto", true, false)]
    [DataRow("lsp", false, true)]
    [DataRow("lsp", true, false)]
    [DataRow("formatter", false, true)]
    [DataRow("formatter", true, false)]
    [DataRow("server.mdns", true, true)]
    [DataRow("server.mdns", false, false)]
    public void BoolPredicatesFlagOnlyTheUnsafeValue(string path, bool value, bool expected)
    {
        Assert.AreEqual(expected,
            OpenCodeDangerTable.Config.Classify(path, GlobalScope, value).IsDangerNow,
            $"IsDangerNow for '{path}' = {value}");
    }

    [TestMethod]
    [DataRow(0L, false)]
    [DataRow(1L, false)]
    [DataRow(2L, false)]
    [DataRow(3L, true)]
    [DataRow(9L, true)]
    public void SubagentDepthIsFlaggedPastTwo(long value, bool expected)
    {
        Assert.AreEqual(expected,
            OpenCodeDangerTable.Config.Classify("subagent_depth", GlobalScope, value).IsDangerNow,
            $"IsDangerNow for subagent_depth = {value}");
    }

    /// <summary>
    /// ⚠ <b>Integers arrive as <see cref="long"/> in the editor value currency.</b> A predicate
    /// written against <see cref="int"/> compiles, never matches, and silently reports safe — so
    /// the currency is asserted rather than assumed.
    /// </summary>
    [TestMethod]
    public void SubagentDepthPredicateIsNotFooledByAnIntBoxedAsInt32()
    {
        // The currency contract says long. If a future refactor starts handing out int, this test
        // is the one that says so instead of the feature quietly going dark.
        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify("subagent_depth", GlobalScope, 3L).IsDangerNow,
            "long 3 must be flagged — this is the currency the contract promises");

        Assert.IsFalse(
            OpenCodeDangerTable.Config.Classify("subagent_depth", GlobalScope, 3).IsDangerNow,
            "int 3 is NOT the documented currency; if this ever starts being flagged the adapter "
            + "changed what it hands out and every numeric predicate needs revisiting");
    }

    // ── Structured values ─────────────────────────────────────────────────────

    [TestMethod]
    public void ABarePermissionMapWithAnAllowCatchAllIsFlagged()
    {
        Dictionary<string, object?> allowAll = new(StringComparer.Ordinal) { ["*"] = "allow" };
        Dictionary<string, object?> askAll = new(StringComparer.Ordinal) { ["*"] = "ask" };
        Dictionary<string, object?> narrow = new(StringComparer.Ordinal) { ["bash"] = "allow" };

        Assert.IsTrue(OpenCodeDangerTable.Config.Classify("permission", GlobalScope, allowAll).IsDangerNow,
            "a '*' catch-all set to allow approves every tool");
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify("permission", GlobalScope, askAll).IsDangerNow,
            "'*' = ask is the safe shape");

        // A narrow grant is not the catch-all case; permission.bash is what flags it, and it does.
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify("permission", GlobalScope, narrow).IsDangerNow,
            "a per-tool grant is not a blanket allow at the container level");
        Assert.IsTrue(OpenCodeDangerTable.Config.Classify("permission.bash", GlobalScope, "allow").IsDangerNow,
            "...but the per-tool path itself must flag it");
    }

    [TestMethod]
    public void AnMcpServerIsFlaggedUnlessExplicitlyDisabled()
    {
        Dictionary<string, object?> enabled = new(StringComparer.Ordinal) { ["enabled"] = true };
        Dictionary<string, object?> disabled = new(StringComparer.Ordinal) { ["enabled"] = false };
        Dictionary<string, object?> unspecified = new(StringComparer.Ordinal) { ["command"] = "npx" };

        Assert.IsTrue(OpenCodeDangerTable.Config.Classify("mcp.linear", GlobalScope, enabled).IsDangerNow);
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify("mcp.linear", GlobalScope, disabled).IsDangerNow);

        // ⚠ Absence is not safety: an entry with no `enabled` key is enabled by default.
        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify("mcp.linear", GlobalScope, unspecified).IsDangerNow,
            "a server with no explicit `enabled` key still runs, so it must still be flagged");
    }

    [TestMethod]
    public void RemoteInstructionsAndSkillUrlsAreFlagged()
    {
        List<object?> remote = ["./AGENTS.md", "https://evil.example/prompt.md"];
        List<object?> localOnly = ["./AGENTS.md", "docs/house-style.md"];

        Assert.IsTrue(OpenCodeDangerTable.Config.Classify("instructions", GlobalScope, remote).IsDangerNow,
            "a remote instructions URL injects attacker-controllable text into every session");
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify("instructions", GlobalScope, localOnly).IsDangerNow);

        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify("skills.urls", GlobalScope, new List<object?> { "https://x" }).IsDangerNow);
        Assert.IsFalse(
            OpenCodeDangerTable.Config.Classify("skills.urls", GlobalScope, new List<object?>()).IsDangerNow);
    }

    [TestMethod]
    public void NonEmptyPluginListsAreFlaggedInBothSchemas()
    {
        List<object?> some = ["some-plugin"];
        List<object?> none = [];

        Assert.IsTrue(OpenCodeDangerTable.Config.Classify("plugin", GlobalScope, some).IsDangerNow);
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify("plugin", GlobalScope, none).IsDangerNow);
        Assert.IsTrue(OpenCodeDangerTable.Tui.Classify("plugin", GlobalScope, some).IsDangerNow);
        Assert.IsFalse(OpenCodeDangerTable.Tui.Classify("plugin", GlobalScope, none).IsDangerNow);

        Dictionary<string, object?> on = new(StringComparer.Ordinal) { ["gk-hooks"] = true };
        Dictionary<string, object?> off = new(StringComparer.Ordinal) { ["gk-hooks"] = false };
        Assert.IsTrue(OpenCodeDangerTable.Tui.Classify("plugin_enabled", GlobalScope, on).IsDangerNow);
        Assert.IsFalse(OpenCodeDangerTable.Tui.Classify("plugin_enabled", GlobalScope, off).IsDangerNow);
    }

    [TestMethod]
    public void AWildcardCorsEntryIsFlaggedInsideAList()
    {
        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify(
                "server.cors", GlobalScope, new List<object?> { "https://ok.example", "*" }).IsDangerNow,
            "cors is legitimately a list, and one wildcard entry is enough");
    }

    // ── Scope escalation — asserted at BOTH scopes, per the plan ──────────────

    [TestMethod]
    public void AnApiKeyIsWorseAtProjectScopeThanAtGlobalScope()
    {
        const string Path = "provider.anthropic.options.apiKey";

        DangerAssessment global = OpenCodeDangerTable.Config.Classify(Path, GlobalScope, "sk-live-xxx");
        DangerAssessment project = OpenCodeDangerTable.Config.Classify(Path, ProjectScope, "sk-live-xxx");

        Assert.AreEqual(AppSeverity.Caution, global.Severity,
            "a key in the user-global file is a local secret");
        Assert.AreEqual(AppSeverity.Critical, project.Severity,
            "the same key in a project file is committed to git and shared with the whole repo");

        Assert.IsTrue(global.IsDangerNow, "a secret in plaintext is still a secret in plaintext");
        Assert.IsTrue(project.IsDangerNow);
    }

    [TestMethod]
    public void AnUnsetApiKeyIsNotFlaggedAtEitherScope()
    {
        const string Path = "provider.anthropic.options.apiKey";

        Assert.IsFalse(OpenCodeDangerTable.Config.Classify(Path, GlobalScope, null).IsDangerNow);
        Assert.IsFalse(OpenCodeDangerTable.Config.Classify(Path, ProjectScope, null).IsDangerNow);
    }

    /// <summary>
    /// A <see langword="null"/> scope must never RAISE severity — an unknown scope is not evidence
    /// of danger, and the effective-settings view legitimately has no scope in hand.
    /// </summary>
    [TestMethod]
    public void ANullScopeDoesNotEscalate()
    {
        DangerAssessment a = OpenCodeDangerTable.Config.Classify(
            "provider.anthropic.options.apiKey", scope: null, currentValue: "sk-live-xxx");

        Assert.AreEqual(AppSeverity.Caution, a.Severity,
            "with no scope in hand the base tier must stand, not the escalated one");
    }

    // ── Matcher semantics ─────────────────────────────────────────────────────

    [TestMethod]
    public void AMoreSpecificPatternWinsOverItsAncestor()
    {
        // `permission` alone would not flag "ask"; `permission.bash` is what reads "allow".
        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify("permission.bash", GlobalScope, "allow").IsDangerNow);

        // A literal segment beats a wildcard at the same depth.
        DangerAssessment perAgent = OpenCodeDangerTable.Config.Classify(
            "agent.reviewer.permission.bash", GlobalScope, "allow");
        Assert.AreEqual(AppSeverity.Critical, perAgent.Severity);
        Assert.IsTrue(perAgent.IsDangerNow,
            "an agent granted bash:allow is red even when the global permission is safe");
    }

    /// <summary>
    /// A nested knob with no rule of its own takes its area's tier — which is what makes a
    /// per-top-level-key table cover a whole nested schema.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This deliberately does NOT assert the no-inherit guarantee for value predicates.</b>
    /// An earlier version did, and it was vacuous: <c>attachment</c> carries no <c>Unsafe</c>
    /// predicate, so breaking the mechanism on purpose left the assertion green. The guarantee is
    /// pinned in <c>TableDangerClassifierTests</c> against a table built to exercise it, where the
    /// assertion can actually fail.
    /// </remarks>
    [TestMethod]
    public void ADescendantInheritsItsAncestorsTier()
    {
        DangerAssessment nested = OpenCodeDangerTable.Config.Classify(
            "attachment.image.maxWidth", GlobalScope, 4096L);

        Assert.AreEqual(AppSeverity.Caution, nested.Severity,
            "a nested knob takes its area's tier, which is what makes a per-key table cover a "
            + "whole nested schema");
    }

    /// <summary>
    /// A permission key OpenCode adds later must still have its value checked, not merely inherit
    /// a red dot.
    /// </summary>
    /// <remarks>
    /// ⭐ This is the gap the <c>permission.*</c> wildcard closes, and it was found by the canary
    /// above failing to redden. Without that entry an unknown permission key would render
    /// Critical from <c>permission</c>'s inherited tier while reporting
    /// <c>IsDangerNow: false</c> — a red dot next to "nothing is wrong right now", for a tool
    /// that had just been granted unattended access.
    /// </remarks>
    [TestMethod]
    public void APermissionKeyTheTableDoesNotNameIsStillEvaluated()
    {
        Assert.IsTrue(
            OpenCodeDangerTable.Config.Classify("permission.some_future_tool", GlobalScope, "allow").IsDangerNow,
            "an unnamed permission key set to allow must be flagged");
        Assert.IsFalse(
            OpenCodeDangerTable.Config.Classify("permission.some_future_tool", GlobalScope, "ask").IsDangerNow);
    }

    [TestMethod]
    public void AnUnknownPathIsUnremarkableRatherThanAGuess()
    {
        DangerAssessment a = OpenCodeDangerTable.Config.Classify(
            "no_such_key_exists", GlobalScope, "whatever");

        Assert.AreEqual(DangerAssessment.Unremarkable, a);
    }

    // ── Schema readers ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>opencode-config.json</c> has no root <c>properties</c> — everything hangs off
    /// <c>$ref: #/$defs/Config</c>, which is why this cannot just read <c>properties</c>.
    /// </summary>
    private static IReadOnlyCollection<string> ConfigSchemaKeys()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(SchemaPath("opencode-config.json")));

        string reference = doc.RootElement.GetProperty("$ref").GetString()!;
        string defName = reference[(reference.LastIndexOf('/') + 1)..];

        return [.. doc.RootElement
            .GetProperty("$defs")
            .GetProperty(defName)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)];
    }

    private static IReadOnlyCollection<string> TuiSchemaKeys()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(SchemaPath("opencode-tui.json")));

        return [.. doc.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)];
    }

    private static string SchemaPath(string fileName) =>
        Path.Combine(FindRepoRoot(), "src", "AgentForge.Core", "Assets", "Schemas", fileName);

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
