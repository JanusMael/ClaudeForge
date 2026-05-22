using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// diagnostic suite — concern: when the workspace already had
/// schema-questionable hooks data on disk and the user changes an UNRELATED
/// field, does the validator leak those pre-existing errors into the
/// user-visible report?
/// </summary>
/// <remarks>
/// <para>
/// This is the user-reported scenario from manual testing 2026-04-29:
/// changing <c>permissions.defaultMode</c> produced 18 schema errors,
/// all about hooks the user didn't touch. SchemaRegistry has a
/// delta-filter (<c>ValidateWorkspaceAsync</c> lines 269-281) explicitly
/// designed to filter pre-existing errors out — it computes the error
/// set against <c>BaselineRoot</c> and subtracts those from the current
/// error set, reporting only deltas the user introduced.
/// </para>
/// <para>
/// These tests lock the contract that:
/// <list type="number">
///   <item>When hooks are unchanged between baseline and current, the
///         delta filter strips ALL hook-related errors regardless of
///         their schema validity (Concerns A, B).</item>
///   <item>When hooks ARE modified to introduce a new error, only the new
///         error is reported (Concern C).</item>
///   <item>When the user's actual reported pattern (a baseline already
///         containing schema-failing hooks plus an unrelated edit) is
///         simulated, the delta filter behaves correctly (Concern D).</item>
/// </list>
/// </para>
/// </remarks>
[TestClass]
public sealed class HookValidationDeltaTests
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
    /// Build a workspace whose baseline (the on-disk snapshot at load time)
    /// CONTAINS the supplied hooks structure verbatim, then dirty the
    /// document by writing an unrelated key. The user's scenario is exactly
    /// this shape: hooks were on disk, user changed something else, save
    /// triggered validation.
    /// </summary>
    private static SettingsWorkspace WorkspaceWithBaselineHooks(JsonObject hooksObject)
    {
        JsonObject initialRoot = new()
        {
            ["hooks"] = hooksObject,
        };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        // Unrelated edit (e.g. user changed permissions.defaultMode in the
        // app — but for these tests, anything that dirties the doc without
        // touching hooks works).
        ws.SetValue("model", JsonValue.Create("opus"), ConfigScope.User);
        return ws;
    }

    private static JsonArray HookEventArray(string matcher, params JsonNode[] innerHooks)
    {
        return new JsonArray(new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray(innerHooks),
        });
    }

    private static string FormatErrors(IReadOnlyList<SchemaValidationError> errors)
    {
        return errors.Count == 0
            ? "(none)"
            : string.Join("\n", errors.Select(e => $"  {e.DisplayPath}: {e.Message}"));
    }

    // ── Concern A: pre-existing VALID hooks don't leak through ─────────────

    [TestMethod]
    public async Task ValidHooksInBaseline_UnrelatedEdit_ReportsZeroErrors()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceWithBaselineHooks(new JsonObject
        {
            ["Stop"] = HookEventArray("Bash", new JsonObject
            {
                ["type"] = "command",
                ["command"] = "echo done",
            }),
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"Pre-existing valid hooks + unrelated edit must report zero errors.\n{FormatErrors(errors)}");
    }

    // ── Concern B: pre-existing INVALID hooks are filtered as baseline ────

    [TestMethod]
    public async Task InvalidHookInBaseline_UnrelatedEdit_DeltaFiltersErrors()
    {
        // This is the user's exact scenario. If the delta filter is correct,
        // the broken hooks were ALREADY in baseline → ALSO produce errors in
        // current (same shape) → set difference is empty → ZERO errors reported.
        //
        // If this test FAILS (errors > 0), then the delta filter is broken
        // or non-deterministic, which would explain the user's report. The
        // test failure message will show exactly which errors leaked and
        // their paths so we can target the root cause.
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceWithBaselineHooks(new JsonObject
        {
            ["Stop"] = HookEventArray("Bash",
                // Index 0: valid command hook
                new JsonObject { ["type"] = "command", ["command"] = "echo first" },
                // Index 1: malformed — type=command but no command field
                new JsonObject { ["type"] = "command" }),
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            "User scenario: pre-existing invalid hook + unrelated edit. Delta filter " +
            "MUST strip the pre-existing errors. If errors leaked through, the delta " +
            "filter is the regression source.\n" +
            FormatErrors(errors));
    }

    // ── Concern C: new errors the user actually introduced are reported ──

    [TestMethod]
    public async Task NewlyIntroducedInvalidHook_IsReported()
    {
        using SchemaRegistry registry = CreateRegistry();
        // Baseline: clean (no hooks).
        SettingsDocument doc = new(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc]);

        // User adds a new malformed hook via the editor.
        JsonObject hooks = new()
        {
            ["Stop"] = HookEventArray("Bash",
                new JsonObject { ["type"] = "command" /* no command */ }),
        };
        ws.SetValue("hooks", hooks, ConfigScope.User);

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "User-introduced invalid hooks MUST be reported (delta filter only strips baseline-equal errors).");
    }

    // ── Concern D: reproduce the user's exact 18-error report ────────────

    [TestMethod]
    public async Task UserReportedScenario_PreExistingHooksWithEditOfUnrelatedField_NoSpuriousErrors()
    {
        // Exact reproduction of the user's pattern from manual testing 2026-04-29.
        // Their settings.json has hooks for Stop and SessionEnd. Hook[6] in Stop
        // and hook[1] in SessionEnd produced (collectively) 18 anyOf-branch-failure
        // errors when saving an unrelated permissions.defaultMode change.
        //
        // Constructing realistic hooks data with both valid and questionable
        // entries, then editing an unrelated field. If the delta filter is
        // working, ZERO errors should be reported.
        using SchemaRegistry registry = CreateRegistry();

        // Build hooks as plain arrays (not yet attached to any parent), then
        // splat into HookEventArray. JsonNode children can only belong to one
        // parent, so building them in a separate JsonArray first and then
        // re-using them throws NodeAlreadyHasParent.
        List<JsonNode> stopHooks = [];
        for (int i = 0; i < 6; i++)
        {
            stopHooks.Add(new JsonObject
            {
                ["type"] = "command",
                ["command"] = $"echo hook-{i}",
            });
        }

        // Index 6: matches the user's pattern — a valid command hook that
        // generates anyOf branch-failure noise (Expected "prompt"/"agent"/"http"
        // and false-schema "command" rejection from the 3 non-matching
        // branches). The IsValid gate must filter that noise.
        stopHooks.Add(new JsonObject
        {
            ["type"] = "command",
            ["command"] = "scripted-thing",
        });

        List<JsonNode> sessionEndHooks =
        [
            new JsonObject { ["type"] = "command", ["command"] = "echo first" },
            new JsonObject { ["type"] = "command", ["command"] = "echo second" },
        ];

        SettingsWorkspace workspace = WorkspaceWithBaselineHooks(new JsonObject
        {
            ["Stop"] = HookEventArray("Bash", [.. stopHooks]),
            ["SessionEnd"] = HookEventArray("", [.. sessionEndHooks]),
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"User scenario reproduction must produce zero errors. If errors > 0, " +
            $"we've reproduced the user-reported bug and can debug it.\n{FormatErrors(errors)}");
    }

    // ── Concern E: validator is deterministic ─────────────────────────────

    [TestMethod]
    public async Task ValidateWorkspaceAsync_IsDeterministic()
    {
        // The delta filter relies on (InstancePath, Message) tuples being
        // identical across baseline and current evaluations. If the validator
        // produces different paths or messages run-to-run for the same input
        // (e.g. due to dictionary ordering or anyOf branch iteration order),
        // the filter would let errors through. Lock determinism here.
        using SchemaRegistry registry = CreateRegistry();
        JsonObject hookData = new()
        {
            ["Stop"] = HookEventArray("Bash",
                new JsonObject { ["type"] = "command", ["command"] = "echo" }),
        };

        SettingsWorkspace workspace1 = WorkspaceWithBaselineHooks((JsonObject)hookData.DeepClone());
        SettingsWorkspace workspace2 = WorkspaceWithBaselineHooks((JsonObject)hookData.DeepClone());

        IReadOnlyList<SchemaValidationError> errors1 = await registry.ValidateWorkspaceAsync(workspace1, isClaudeCode: true);
        IReadOnlyList<SchemaValidationError> errors2 = await registry.ValidateWorkspaceAsync(workspace2, isClaudeCode: true);

        Assert.AreEqual(errors1.Count, errors2.Count,
            "Validator must produce the same error count for identical input.");
        // Compare error tuples set-wise (order may differ but contents must match).
        HashSet<(string InstancePath, string Message)> set1 = errors1.Select(e => (e.InstancePath, e.Message)).ToHashSet();
        HashSet<(string InstancePath, string Message)> set2 = errors2.Select(e => (e.InstancePath, e.Message)).ToHashSet();
        Assert.IsTrue(set1.SetEquals(set2),
            "Validator must produce the same (InstancePath, Message) set for identical input.");
    }
}