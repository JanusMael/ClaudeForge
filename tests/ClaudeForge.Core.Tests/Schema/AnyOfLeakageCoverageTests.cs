using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Smoke-tests the anyOf-leakage suppression in
/// <see cref="SchemaRegistry.CollectSchemaErrors"/> across every anyOf
/// site in the Claude Code settings schema, not just the
/// <c>$defs.hookCommand</c> site that surfaced the user-reported 2026-05-01
/// bug. The fix is path-shape agnostic — it walks every detail's
/// EvaluationPath looking for the literal <c>/anyOf/</c> segment — so
/// these tests also document which schema constructs use anyOf and lock
/// the regression behaviour at every one.
///
/// Each test here:
/// <list type="number">
///   <item>Sets up a baseline with unrelated noise (unknown event names)
///         to keep <c>results.IsValid=false</c>, defeating the early-out
///         in CollectSchemaErrors so the per-detail iteration runs.</item>
///   <item>Adds a value that matches exactly one branch of the anyOf
///         under test.</item>
///   <item>Asserts zero net-new errors. Pre-fix this would have leaked
///         the 2..6 non-matching branch failures into the user's save
///         dialog.</item>
/// </list>
///
/// As of 2026-05-01 the schema has 8 anyOf sites
/// (<c>$defs.hookCommand</c>, <c>allowedMcpServers/items</c>,
/// <c>deniedMcpServers/items</c>, <c>enabledPlugins/additionalProperties</c>,
/// <c>extraKnownMarketplaces.*.source</c>,
/// <c>strictKnownMarketplaces/items</c>,
/// <c>mcpServerInstanceConfigs.*.*.<env></c>,
/// <c>blockedMarketplaces/items</c>); the user-editable ones (those
/// surfaced via dedicated GUI editor pages) are covered below. The
/// managed-only ones (allowed/denied/strict/blocked) all share the same
/// path-shape and the suppressor handles them identically; they're
/// covered by the path-shape-agnostic
/// <c>HookAnyOfLeakageDiagnosticTests</c>.
/// </summary>
[TestClass]
public sealed class AnyOfLeakageCoverageTests
{
    private sealed class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw new HttpRequestException("Tests must not hit network");
        }
    }

    private static SchemaRegistry CreateRegistry()
    {
        return new SchemaRegistry(new HttpClient(new FailingHttpHandler()));
    }

    /// <summary>
    /// Baseline noise that keeps the document overall-invalid so
    /// <c>CollectSchemaErrors</c>' early-out doesn't mask leakage. We use
    /// unknown hook event names — the same shape that bit the user.
    /// </summary>
    private static JsonObject BaselineWithUnknownHooks()
    {
        return new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["PermissionDenied"] = new JsonArray(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = "noop",
                    }),
                }),
            },
        };
    }

    private static SettingsWorkspace WorkspaceFromBaselineWithEdit(
        JsonObject baseline,
        string key,
        JsonNode value)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json",
            baseline.DeepClone()!.AsObject(),
            isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        ws.SetValue(key, value, ConfigScope.User);
        return ws;
    }

    public TestContext? TestContext { get; set; }

    private static string FormatErrors(IReadOnlyList<SchemaValidationError> errors)
    {
        return string.Join("\n", errors.Select(e => $"  {e.InstancePath} | {e.Message}"));
    }

    // ── extraKnownMarketplaces.<id>.source — 7 branches ──────────────────
    // The Marketplaces editor lets the user pick a source kind from a
    // dropdown (url, hostPattern, github, git, npm, file, directory).
    // Each pick triggers an anyOf evaluation where 6 branches fail; pre-fix
    // those 6 failures leaked into the save dialog.

    [TestMethod]
    public async Task Marketplaces_GitHubSource_MatchesOneBranch_ZeroNetNewErrors()
    {
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "extraKnownMarketplaces",
            new JsonObject
            {
                ["my-marketplace"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["source"] = "github",
                        ["repo"] = "anthropic/claude-marketplace",
                    },
                },
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"GitHub-source marketplace matches exactly one anyOf branch; "
            + $"failures of the other 6 branches must be suppressed. Got:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task Marketplaces_UrlSource_MatchesOneBranch_ZeroNetNewErrors()
    {
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "extraKnownMarketplaces",
            new JsonObject
            {
                ["my-marketplace"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["source"] = "url",
                        ["url"] = "https://example.com/marketplace.json",
                    },
                },
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"URL-source marketplace matches exactly one anyOf branch. Got:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task Marketplaces_BogusSourceKind_StillEmitsRealError()
    {
        // Adversarial counter-test: a source with an unknown kind matches
        // no anyOf branch. The suppressor must NOT eat this failure.
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "extraKnownMarketplaces",
            new JsonObject
            {
                ["my-marketplace"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["source"] = "wibble", // not a valid kind
                        ["url"] = "anything",
                    },
                },
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "An unknown source kind matches no anyOf branch — the failure must come through, "
            + "or the suppressor is too aggressive and eating real user mistakes.");
    }

    // ── enabledPlugins.<id> — 3 branches: array | boolean | {not:{}} ────
    // The Enabled Plugins editor writes either a string array (selecting
    // specific plugins) or a boolean (enable-all / disable-all). Both
    // forms must validate clean.

    [TestMethod]
    public async Task EnabledPlugins_ArrayForm_MatchesOneBranch_ZeroNetNewErrors()
    {
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "enabledPlugins",
            new JsonObject
            {
                ["formatter@anthropic-tools"] = new JsonArray("prettier", "ruff"),
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"enabledPlugins array form matches exactly one anyOf branch; "
            + $"failures of the boolean and not-{{}} branches must be suppressed. Got:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task EnabledPlugins_BooleanForm_MatchesOneBranch_ZeroNetNewErrors()
    {
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "enabledPlugins",
            new JsonObject
            {
                ["formatter@anthropic-tools"] = true,
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"enabledPlugins boolean form matches exactly one anyOf branch. Got:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task EnabledPlugins_NumberForm_StillEmitsRealError()
    {
        // Adversarial: a number value matches none of the 3 branches
        // (array, boolean, {not:{}}). Must surface as an error.
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "enabledPlugins",
            new JsonObject
            {
                ["formatter@anthropic-tools"] = 42,
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "A number value matches none of enabledPlugins' anyOf branches — must error.");
    }

    // ── All-branches-fail collapse ────────────────────────────────────
    //
    // When NONE of an anyOf/oneOf's branches matches, JsonSchema.Net used
    // to emit one error per branch — a Marketplace `source` object that
    // matched nothing produced 6+ "Required X" / "Expected Y" lines for
    // one logical "doesn't match any allowed shape". The CollapseFailedAnyOfErrors
    // pass groups errors by instance path and emits one combined message
    // per path. The fix ships under `ValidateAllWorkspaceAsync` (which is
    // the post-reload banner's path) — `ValidateWorkspaceAsync` reaches
    // it too via the same CollectSchemaErrors helper.

    [TestMethod]
    public async Task Marketplaces_SourceMatchesNoBranch_ErrorsCollapseToOnePerPath()
    {
        // A `source` object whose shape matches no anyOf branch — missing
        // every required discriminator ("source" literal) and every shape's
        // required field (url / repo / package / path). Pre-fix this
        // produced ~13 errors at 2 instance paths; post-fix each path
        // collapses to one.
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "extraKnownMarketplaces",
            new JsonObject
            {
                ["bogus-marketplace"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["repository"] = "anthropic/claude-marketplace",
                        // No "source" discriminator, no url/repo/package/path
                    },
                },
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateAllWorkspaceAsync(ws, isClaudeCode: true);

        // Any errors that survived the leak suppression must now be
        // unique per (FilePath, InstancePath) pair — that's the contract
        // CollapseFailedAnyOfErrors enforces. A pre-fix run would have
        // 6+ duplicate-path entries for the source object alone.
        List<IGrouping<(string FilePath, string InstancePath), SchemaValidationError>> duplicates = errors
                                                                                                    .GroupBy(e => (e.FilePath, e.InstancePath))
                                                                                                    .Where(g => g.Count() > 1)
                                                                                                    .ToList();
        Assert.AreEqual(0, duplicates.Count,
            "Errors at the same instance path must be collapsed to one. Found duplicates:\n"
            + string.Join("\n", duplicates.Select(g =>
                $"  {g.Key.InstancePath} ({g.Count()} entries)")));
    }

    [TestMethod]
    public async Task Marketplaces_SourceMatchesNoBranch_CombinedMessageListsVariants()
    {
        // The collapsed message must still be informative — the user needs
        // to see WHICH variants the value failed to match.
        SettingsWorkspace ws = WorkspaceFromBaselineWithEdit(
            BaselineWithUnknownHooks(),
            "extraKnownMarketplaces",
            new JsonObject
            {
                ["bogus-marketplace"] = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["repository"] = "anthropic/claude-marketplace",
                    },
                },
            });

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateAllWorkspaceAsync(ws, isClaudeCode: true);

        // At least one error must reference the source path AND describe it
        // as "matches none of the N permitted variants" so the user knows
        // they're looking at a combinator failure, not a single-rule failure.
        List<SchemaValidationError> sourceErrors = errors
                                                   .Where(e => e.InstancePath.EndsWith("/source", StringComparison.Ordinal))
                                                   .ToList();
        Assert.IsTrue(sourceErrors.Count > 0,
            $"Expected at least one error at .../source. Got:\n{FormatErrors(errors)}");
        Assert.IsTrue(sourceErrors.Any(e =>
                e.Message.Contains("matches none", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("permitted variants", StringComparison.OrdinalIgnoreCase)),
            "Collapsed message must signal 'matches none of N variants' for clarity. Got:\n"
            + FormatErrors(sourceErrors));
    }

    [TestMethod]
    public async Task Collapse_SingleErrorPerPath_LeavesMessageUntouched()
    {
        // Negative case: when only ONE error exists at an instance path
        // (the common case for non-anyOf failures like a missing required
        // top-level field), the original message must pass through
        // unchanged — no spurious "matches none of" wrapper.
        SettingsDocument doc = new(ConfigScope.User, "settings.json",
            new JsonObject
            {
                ["model"] = 42, // single type-mismatch error
            },
            isReadOnly: false);
        SettingsWorkspace ws = new([doc]);

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateAllWorkspaceAsync(ws, isClaudeCode: true);

        List<SchemaValidationError> modelErrors = errors
                                                  .Where(e => e.InstancePath == "/model")
                                                  .ToList();
        Assert.IsTrue(modelErrors.Count > 0, "Model type-mismatch must produce at least one error.");
        Assert.IsFalse(modelErrors.Any(e => e.Message.Contains("matches none", StringComparison.OrdinalIgnoreCase)),
            "Single-error paths must not be wrapped in the multi-variant collapse message.");
    }
}