using System.Text.Json;
using Bennewitz.Ninja.ClaudeForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Yesterday's schema-level diagnostics (HookSchemaShapeTests, HookValidationDeltaTests,
/// UserReportedHookValidationTests) all PASSED, proving that:
/// the validator works correctly, the delta filter strips pre-existing
/// errors, and the user's exact on-disk hook structure validates clean.
/// Yet the user observed errors at array indices that DO NOT EXIST in
/// their file. Therefore something between disk-load and validate is
/// inserting phantom entries.
/// </summary>
/// <remarks>
/// <para>
/// These tests reconstruct the user's hook structure faithfully:
/// <list type="bullet">
///   <item><c>Stop[0]</c> = matcher <c>"*"</c>, 6 inner command hooks (indices 0..5)</item>
///   <item><c>Stop[1]</c> = NO matcher, 1 inner command hook</item>
///   <item><c>SessionEnd[0]</c> = matcher <c>"*"</c>, 1 inner command hook (index 0 only)</item>
///   <item><c>SessionEnd[1]</c> = NO matcher, 1 inner command hook</item>
/// </list>
/// Then the tests exercise the editor's <c>LoadFromLayered</c> +
/// <c>ToJsonValue</c> round-trip and assert that the JSON is byte-equal
/// to what was loaded. If equality fails, the failure message dumps both
/// the input and output JSON so we can see exactly what mutated.
/// </para>
/// </remarks>
[TestClass]
public sealed class HooksEditorLoadPathMutationTests
{
    private static SchemaNode HooksSchema()
    {
        return new SchemaNode("hooks", "hooks") { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue LayeredWith(JsonObject obj)
    {
        ScopeEntry entry = new(ConfigScope.User, obj, "/fake");
        return new LayeredValue("hooks", [entry])
        {
            EffectiveValue = obj,
            EffectiveScope = ConfigScope.User,
        };
    }

    /// <summary>
    /// User's actual <c>hooks</c> block, but pared down to the two events
    /// (Stop, SessionEnd) where the user observed phantom-index errors.
    /// Each event has both a <c>matcher: "*"</c> outer group and a
    /// matcher-less outer group, mirroring the user's structure.
    /// </summary>
    private static JsonObject BuildUserStopAndSessionEnd()
    {
        // Stop[0]: matcher="*" with 6 inner hooks (the focal point).
        JsonObject stopMatcherStar = new()
        {
            ["matcher"] = "*",
            ["hooks"] = new JsonArray(
                Cmd("hook-a"), // index 0
                Cmd("hook-b"), // index 1
                Cmd("hook-c"), // index 2
                Cmd("hook-d"), // index 3
                Cmd("hook-e"), // index 4
                Cmd("hook-f")), // index 5  (NO INDEX 6)
        };
        // Stop[1]: NO matcher, 1 inner hook.
        JsonObject stopNoMatcher = new()
        {
            ["hooks"] = new JsonArray(Cmd("gk-stop")),
        };

        JsonObject sessionEndMatcherStar = new()
        {
            ["matcher"] = "*",
            ["hooks"] = new JsonArray(Cmd("session-end-marker")), // index 0 only
        };
        JsonObject sessionEndNoMatcher = new()
        {
            ["hooks"] = new JsonArray(Cmd("gk-session-end")),
        };

        return new JsonObject
        {
            ["Stop"] = new JsonArray(stopMatcherStar, stopNoMatcher),
            ["SessionEnd"] = new JsonArray(sessionEndMatcherStar, sessionEndNoMatcher),
        };

        static JsonObject Cmd(string command)
        {
            return new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
            };
        }
    }

    private static string PrettyPrint(JsonNode? node)
    {
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
               ?? "(null)";
    }

    // ── Concern A: load doesn't add phantom entries to the editor ─────────

    [TestMethod]
    public void LoadFromLayered_UserShape_PreservesEntryCounts()
    {
        // Sanity check: after LoadFromLayered, the editor's in-memory state
        // should hold exactly 7 Stop entries (6+1) and 2 SessionEnd entries
        // (1+1). NOT more — phantom entries would surface here as inflated
        // counts before any save round-trip muddies the picture.
        JsonObject input = BuildUserStopAndSessionEnd();
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        HookEventGroup stop = vm.EventGroups.First(g => g.EventName == "Stop");
        HookEventGroup sessionEnd = vm.EventGroups.First(g => g.EventName == "SessionEnd");

        Assert.AreEqual(7, stop.Hooks.Count,
            "Stop should hold 6 (matcher=*) + 1 (no-matcher) = 7 entries after load. " +
            $"Got {stop.Hooks.Count}. Inflated count means load-path is creating phantom entries.");
        Assert.AreEqual(2, sessionEnd.Hooks.Count,
            "SessionEnd should hold 1 (matcher=*) + 1 (no-matcher) = 2 entries after load. " +
            $"Got {sessionEnd.Hooks.Count}.");
    }

    // ── Concern B: ToJsonValue round-trip preserves on-disk shape ─────────

    [TestMethod]
    public void RoundTrip_UserShape_StopArrayPreservesSixHooksAtIndexZero()
    {
        // The user's report flagged an error at hooks/Stop/0/hooks/6 — a
        // 7th entry in Stop[0].hooks where only 6 should exist. Drive the
        // round-trip and prove that index 6 is OUT OF BOUNDS in the output.
        JsonObject input = BuildUserStopAndSessionEnd();
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(output, "ToJsonValue must return a JsonObject when there are entries.");

        JsonArray stop = output!["Stop"]!.AsArray();
        JsonObject stopFirst = stop[0]!.AsObject();
        JsonArray stopHooks = stopFirst["hooks"]!.AsArray();

        Assert.AreEqual(6, stopHooks.Count,
            "Stop[0].hooks must have exactly 6 entries after round-trip. Got " +
            $"{stopHooks.Count}.\n\nInput JSON:\n{PrettyPrint(input)}\n\nOutput JSON:\n{PrettyPrint(output)}");
    }

    [TestMethod]
    public void RoundTrip_UserShape_SessionEndArrayPreservesOneHookAtIndexZero()
    {
        JsonObject input = BuildUserStopAndSessionEnd();
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(output);

        JsonArray sessionEnd = output!["SessionEnd"]!.AsArray();
        JsonObject sessionEndFirst = sessionEnd[0]!.AsObject();
        JsonArray sessionEndHooks = sessionEndFirst["hooks"]!.AsArray();

        Assert.AreEqual(1, sessionEndHooks.Count,
            "SessionEnd[0].hooks must have exactly 1 entry after round-trip. Got " +
            $"{sessionEndHooks.Count}.\n\nInput:\n{PrettyPrint(input)}\n\nOutput:\n{PrettyPrint(output)}");
    }

    [TestMethod]
    public void RoundTrip_UserShape_PreservesBothOuterGroupsForStop()
    {
        // Lock the matcher-vs-no-matcher distinction. Stop should round-trip
        // to TWO outer groups: one with matcher "*", one with no matcher.
        JsonObject input = BuildUserStopAndSessionEnd();
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;
        JsonArray stop = output!["Stop"]!.AsArray();

        Assert.AreEqual(2, stop.Count,
            $"Stop must round-trip to 2 outer groups. Got {stop.Count}.\n\n" +
            $"Output:\n{PrettyPrint(output)}");

        // One group has matcher "*", the other has no matcher key.
        List<string?> matchers = stop.Select(g => g!.AsObject().ContainsKey("matcher")
            ? g.AsObject()["matcher"]!.GetValue<string>()
            : null).ToList();
        Assert.IsTrue(matchers.Contains("*"), $"Expected matcher '*' group. Got: [{string.Join(", ", matchers)}]");
        Assert.IsTrue(matchers.Contains(null), $"Expected no-matcher group. Got: [{string.Join(", ", matchers)}]");
    }

    // ── Concern C: full structural equality ─────────────────────────────

    [TestMethod]
    public void RoundTrip_UserShape_OutputDeepEqualsInput()
    {
        // The strongest contract: load and immediately round-trip must
        // produce a JsonObject byte-equal to the input. Any phantom entry,
        // dropped field, or matcher rewrite would surface here.
        JsonObject input = BuildUserStopAndSessionEnd();
        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;

        Assert.IsTrue(JsonNode.DeepEquals(input, output),
            "Load+ToJsonValue must round-trip byte-equal for the user's hook shape.\n\n" +
            $"Input:\n{PrettyPrint(input)}\n\nOutput:\n{PrettyPrint(output)}");
    }

    // ── Concern D: SDK-BACKED load path (the production code path) ────────

    /// <summary>
    /// Build a workspace whose User-scope settings.json contains the user's
    /// hook structure, wrap it in a ClaudeCodeClient via FromExistingWorkspace,
    /// open the SDK-backed Hooks editor against it, round-trip via ToJsonValue,
    /// and assert byte-equality with the input.
    /// </summary>
    /// <remarks>
    /// The legacy JSON-backed path (LayeredValue + JsonObject) round-trips
    /// cleanly. The SDK path has a known asymmetry: HooksAccessor.MaterializeFrom
    /// defaults missing-matcher to <c>"*"</c>, which conflicts with the
    /// editor's HookEventGroup.ToJson grouping that emits no matcher key
    /// when the matcher value is empty. Result: the user's two outer groups
    /// (one with matcher="*", one without) get FLATTENED into a single outer
    /// group with all entries clustered under matcher="*".
    /// </remarks>
    [TestMethod]
    public void SdkBackedRoundTrip_UserShape_PreservesTwoOuterGroupsForStop()
    {
        JsonObject input = BuildUserStopAndSessionEnd();

        // Wrap in a workspace + SDK client (production code path).
        JsonObject initialRoot = new() { ["hooks"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client);

        // Build a LayeredValue compatible with the legacy path so SetScopeState
        // is happy; the SDK branch reads via _client.Hooks.EventsAt regardless.
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;
        Assert.IsNotNull(output);

        JsonArray stop = output!["Stop"]!.AsArray();
        Assert.AreEqual(2, stop.Count,
            $"SDK-backed round-trip must preserve BOTH outer groups for Stop " +
            $"(matcher=* with 6 hooks AND no-matcher with 1 hook). Got " +
            $"{stop.Count} outer group(s).\n\nInput:\n{PrettyPrint(input)}\n\n" +
            $"Output:\n{PrettyPrint(output)}");
    }

    [TestMethod]
    public void SdkBackedLoad_UserShape_DoesNotMutateWorkspaceRoot()
    {
        // Critical: LoadFromLayered alone (no ToJsonValue, no save) must
        // not modify workspace.Root. If this test fails, some construction
        // or load-time wiring is writing the editor's reconstituted hooks
        // back into the workspace before any user edit happens — which
        // would directly explain the user's 18-error report on a save of
        // an unrelated key (the phantom entries already exist in
        // workspace.Root by the time validation runs).
        JsonObject input = BuildUserStopAndSessionEnd();

        JsonObject initialRoot = new() { ["hooks"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        // Snapshot BEFORE editor construction.
        JsonObject before = (JsonObject)doc.Root.DeepClone();

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        // Snapshot AFTER editor construction + load.
        JsonObject after = doc.Root;

        Assert.IsTrue(JsonNode.DeepEquals(before, after),
            "workspace.Root must not be mutated by HooksEditor construction or " +
            "LoadFromLayered. If this fails, the load path itself is writing " +
            "back to the workspace.\n\n" +
            $"Before:\n{PrettyPrint(before)}\n\nAfter:\n{PrettyPrint(after)}");
    }

    [TestMethod]
    public void SdkBackedLoad_UserShape_RoundTripFlushIsNoOp()
    {
        // HooksEditor.LoadFromLayered SETS IsModified=true on a clean
        // load (the documented "parity contract" — line ~233 of
        // HooksEditorViewModel.cs — true when the scope has any explicit
        // value, so Reset can clear an empty `"hooks": {}` placeholder).
        //
        // That parity contract is intentional, but it means
        // ApplyToWorkspace will flush hooks via ToJsonValue on EVERY
        // save (regardless of whether the user actually touched hooks).
        // For that flush to be safe, the SDK round-trip must be byte-
        // clean — otherwise phantom entries pile up.
        //
        // This test simulates exactly that: load, then write via the
        // SDK with the editor's reconstituted JSON. workspace.Root must
        // be byte-equal to what was loaded. Failure means the SDK
        // matcher round-trip is still lossy.
        JsonObject input = BuildUserStopAndSessionEnd();

        JsonObject initialRoot = new() { ["hooks"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        // Confirm the parity contract is in effect (proves the test is
        // actually exercising the post-load flush case).
        Assert.IsTrue(vm.IsModified,
            "HooksEditor sets IsModified=true on clean load (parity contract). " +
            "If false, this test isn't covering the flush path — see line ~233 " +
            "of HooksEditorViewModel.cs.");

        // Simulate ApplyToWorkspace's flush.
        JsonNode? flushed = vm.ToJsonValue();
        client.SetValue("hooks", flushed!);

        // The SDK round-trip must preserve the on-disk shape exactly.
        JsonNode afterFlush = doc.Root["hooks"]!;
        Assert.IsTrue(JsonNode.DeepEquals(input, afterFlush),
            "After load + ApplyToWorkspace flush, workspace.Root.hooks must be " +
            "byte-equal to the input. If not, phantom entries are being written " +
            "(this is the user's 18-error bug mechanism).\n\n" +
            $"Input:\n{PrettyPrint(input)}\n\nAfter flush:\n{PrettyPrint(afterFlush)}");
    }

    [TestMethod]
    public void SdkBackedRoundTrip_UserShape_StopFirstGroupHasExactlySixHooks()
    {
        // The smoking-gun assertion. After SDK round-trip, Stop[0].hooks
        // (the matcher="*" group) must have exactly 6 entries — NOT 7.
        // The 7th (gk.exe no-matcher hook) belongs in Stop[1], not merged
        // into Stop[0].
        JsonObject input = BuildUserStopAndSessionEnd();

        JsonObject initialRoot = new() { ["hooks"] = (JsonObject)input.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        using ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));

        HooksEditorViewModel vm = new(HooksSchema(), ConfigScope.User, client);
        vm.LoadFromLayered(LayeredWith(input), ConfigScope.User);

        JsonObject? output = vm.ToJsonValue() as JsonObject;
        JsonObject stopFirst = output!["Stop"]!.AsArray()[0]!.AsObject();
        JsonArray hooks = stopFirst["hooks"]!.AsArray();

        Assert.AreEqual(6, hooks.Count,
            $"Stop[0].hooks must have 6 entries after SDK round-trip. Got " +
            $"{hooks.Count}. Inflation to 7 entries means HooksAccessor's " +
            $"matcher=* default has merged the no-matcher gk.exe hook into " +
            $"this group.\n\nOutput:\n{PrettyPrint(output)}");
    }
}