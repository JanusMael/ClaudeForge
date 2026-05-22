using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// regression tests for <see cref="HooksAccessor"/>'s
/// round-trip preservation of the no-matcher outer group.
/// </summary>
/// <remarks>
/// <para>
/// User-reported bug 2026-04-29: an unrelated edit
/// (<c>permissions.defaultMode</c>) produced 18 schema validation
/// errors at hook array indices that did not exist in the on-disk
/// settings.json. Root cause: <see cref="HooksAccessor.MaterializeFrom"/>
/// defaulted missing <c>matcher</c> to <c>"*"</c>, which caused the
/// editor's HookEventGroup grouping (which keys by matcher) to merge
/// the no-matcher outer group with the matcher="*" outer group on the
/// next save flush. The merged write produced phantom array entries
/// that the schema validator correctly flagged.
/// </para>
/// <para>
/// Fix: HooksAccessor now defaults missing matcher to empty string,
/// and Add/Remove omit the <c>matcher</c> key when it is empty. This
/// preserves the on-disk distinction between "matcher: \"*\"" and "no
/// matcher key" through every round-trip.
/// </para>
/// </remarks>
[TestClass]
public sealed class HooksAccessorRoundTripTests
{
    private static SettingsWorkspace MakeWorkspace(JsonObject hooksBlock)
    {
        JsonObject root = new() { ["hooks"] = (JsonObject)hooksBlock.DeepClone() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", root, isReadOnly: false);
        return new SettingsWorkspace([doc]);
    }

    private static ClaudeCodeClient MakeClient(SettingsWorkspace ws)
    {
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));
    }

    private static JsonObject Cmd(string command)
    {
        return new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
        };
    }

    private static JsonObject Group(string matcher, params JsonNode[] hooks)
    {
        return new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray(hooks),
        };
    }

    private static JsonObject GroupNoMatcher(params JsonNode[] hooks)
    {
        return new JsonObject
        {
            ["hooks"] = new JsonArray(hooks),
        };
    }

    [TestMethod]
    public void EventsAt_NoMatcherGroup_ReportsEmptyMatcherNotStar()
    {
        // The user's plugin-managed hooks have outer entries WITHOUT a
        // matcher key. MaterializeFrom must report these as empty-string
        // matcher (not as "*"), so the editor's HookEventGroup grouping
        // doesn't accidentally merge them with matcher="*" entries.
        JsonObject input = new()
        {
            ["Stop"] = new JsonArray(GroupNoMatcher(Cmd("gk-hook"))),
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        IReadOnlyList<HookEvent> events = client.Hooks.EventsAt(ConfigScope.User);

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(string.Empty, events[0].Matcher,
            "A hook from a no-matcher outer group must materialise with " +
            "Matcher == empty string, not \"*\".");
    }

    [TestMethod]
    public void Add_EmptyMatcher_DoesNotEmitMatcherKey()
    {
        // When adding a hook with empty Matcher, the SDK must produce an
        // outer entry WITHOUT a matcher key — preserving the schema's
        // optional-matcher contract.
        SettingsWorkspace ws = MakeWorkspace(new JsonObject()); // no hooks initially
        using ClaudeCodeClient client = MakeClient(ws);

        client.Hooks.Add(new HookEvent(
            EventName: "Stop",
            Matcher: string.Empty,
            CommandType: HookCommandType.Command,
            CommandValue: "echo hi"));

        JsonObject hooks = (JsonObject)client.GetScopeValue("hooks", ConfigScope.User)!;
        JsonArray stop = hooks["Stop"]!.AsArray();
        Assert.AreEqual(1, stop.Count);
        JsonObject outer = stop[0]!.AsObject();
        Assert.IsFalse(outer.ContainsKey("matcher"),
            "Adding a hook with empty matcher must NOT emit a matcher key. " +
            "If it does, the on-disk shape diverges from the user's input.");
    }

    [TestMethod]
    public void Add_ExplicitMatcher_EmitsMatcherKey()
    {
        // Symmetric: a non-empty matcher MUST emit the matcher key.
        SettingsWorkspace ws = MakeWorkspace(new JsonObject());
        using ClaudeCodeClient client = MakeClient(ws);

        client.Hooks.Add(new HookEvent(
            EventName: "Stop",
            Matcher: "Bash",
            CommandType: HookCommandType.Command,
            CommandValue: "echo hi"));

        JsonObject hooks = (JsonObject)client.GetScopeValue("hooks", ConfigScope.User)!;
        JsonObject outer = hooks["Stop"]!.AsArray()[0]!.AsObject();
        Assert.IsTrue(outer.ContainsKey("matcher"));
        Assert.AreEqual("Bash", outer["matcher"]!.GetValue<string>());
    }

    [TestMethod]
    public void RoundTrip_TwoOuterGroupsForSameEvent_PreservesBoth()
    {
        // The user's exact pattern: an event with a matcher="*" group AND
        // a no-matcher group. Materialise → re-Add must produce a
        // JsonObject byte-equal to the input (same outer-group count,
        // same hook counts per group, same matcher presence).
        JsonObject input = new()
        {
            ["Stop"] = new JsonArray(
                Group("*",
                    Cmd("hook-0"), Cmd("hook-1"), Cmd("hook-2"),
                    Cmd("hook-3"), Cmd("hook-4"), Cmd("hook-5")),
                GroupNoMatcher(Cmd("gk-stop"))),
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        // Round-trip: read, clear, re-add each event. Materialise FIRST to
        // a list (the LazyReadOnlyList re-evaluates on each enumeration —
        // after RemoveValue the list would yield zero entries).
        List<HookEvent> events = client.Hooks.EventsAt(ConfigScope.User).ToList();
        client.RemoveValue("hooks", ConfigScope.User);
        foreach (HookEvent evt in events)
        {
            client.Hooks.Add(evt);
        }

        JsonObject output = (JsonObject)client.GetScopeValue("hooks", ConfigScope.User)!;
        JsonArray stop = output["Stop"]!.AsArray();

        Assert.AreEqual(2, stop.Count,
            $"Stop must round-trip to exactly 2 outer groups. Got {stop.Count}.\n" +
            $"Output:\n{output.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");

        // First group: matcher="*" with 6 hooks
        JsonObject groupA = stop[0]!.AsObject();
        Assert.IsTrue(groupA.ContainsKey("matcher"));
        Assert.AreEqual("*", groupA["matcher"]!.GetValue<string>());
        Assert.AreEqual(6, groupA["hooks"]!.AsArray().Count);

        // Second group: no matcher key, 1 hook
        JsonObject groupB = stop[1]!.AsObject();
        Assert.IsFalse(groupB.ContainsKey("matcher"),
            "Second outer group must NOT have a matcher key.");
        Assert.AreEqual(1, groupB["hooks"]!.AsArray().Count);
    }

    [TestMethod]
    public void Add_PreservesPerHookFields_TimeoutAndStatusMessage()
    {
        // a hook entry can carry per-entry sub-fields the SDK
        // doesn't model (timeout, async, statusMessage, model, headers,
        // allowedEnvVars). Round-trip via Materialize → Add must preserve
        // them verbatim.
        JsonObject input = new()
        {
            ["Stop"] = new JsonArray(
                Group("Bash", new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "echo hi",
                    ["timeout"] = 30,
                    ["async"] = true,
                    ["statusMessage"] = "Running echo",
                })),
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        List<HookEvent> events = client.Hooks.EventsAt(ConfigScope.User).ToList();
        client.RemoveValue("hooks", ConfigScope.User);
        foreach (HookEvent evt in events)
        {
            client.Hooks.Add(evt);
        }

        JsonObject output = (JsonObject)client.GetScopeValue("hooks", ConfigScope.User)!;
        JsonObject hook = output["Stop"]!.AsArray()[0]!.AsObject()
            ["hooks"]!.AsArray()[0]!.AsObject();

        Assert.AreEqual(30, hook["timeout"]!.GetValue<int>());
        Assert.IsTrue(hook["async"]!.GetValue<bool>());
        Assert.AreEqual("Running echo", hook["statusMessage"]!.GetValue<string>());
    }

    [TestMethod]
    public void Add_PreservesPerHookFields_HttpTypeWithHeadersAndAllowedEnvVars()
    {
        // The http-type hook has unique fields (headers, allowedEnvVars)
        // beyond what the SDK models. Verify those preserve too.
        JsonObject input = new()
        {
            ["PreToolUse"] = new JsonArray(
                Group("Bash", new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = "https://example.com/hook",
                    ["headers"] = new JsonObject { ["Authorization"] = "Bearer x" },
                    ["allowedEnvVars"] = new JsonArray("SECRET_TOKEN"),
                    ["timeout"] = 60,
                })),
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        List<HookEvent> events = client.Hooks.EventsAt(ConfigScope.User).ToList();
        client.RemoveValue("hooks", ConfigScope.User);
        foreach (HookEvent evt in events)
        {
            client.Hooks.Add(evt);
        }

        JsonObject output = (JsonObject)client.GetScopeValue("hooks", ConfigScope.User)!;
        JsonObject hook = output["PreToolUse"]!.AsArray()[0]!.AsObject()
            ["hooks"]!.AsArray()[0]!.AsObject();

        Assert.IsTrue(hook.ContainsKey("headers"));
        Assert.AreEqual("Bearer x", hook["headers"]!.AsObject()["Authorization"]!.GetValue<string>());
        Assert.IsTrue(hook.ContainsKey("allowedEnvVars"));
        Assert.AreEqual(60, hook["timeout"]!.GetValue<int>());
    }

    [TestMethod]
    public void Remove_NoMatcherEntry_RemovesFromCorrectOuterGroup()
    {
        // Matcher equality must treat "missing matcher" the same way
        // throughout: Remove targeting an empty-Matcher hook should
        // actually find and remove the no-matcher outer group entry.
        JsonObject input = new()
        {
            ["Stop"] = new JsonArray(
                Group("*", Cmd("keep-me")),
                GroupNoMatcher(Cmd("remove-me"))),
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        IReadOnlyList<HookEvent> events = client.Hooks.EventsAt(ConfigScope.User);
        HookEvent noMatcher = events.First(e => e.Matcher == string.Empty);
        bool removed = client.Hooks.Remove(noMatcher);

        Assert.IsTrue(removed, "Remove must find and remove the no-matcher hook.");

        JsonArray stop = client.GetScopeValue("hooks", ConfigScope.User)!.AsObject()["Stop"]!.AsArray();
        Assert.AreEqual(1, stop.Count, "Only the matcher=\"*\" group should remain.");
        JsonObject only = stop[0]!.AsObject();
        Assert.AreEqual("*", only["matcher"]!.GetValue<string>());
        Assert.AreEqual("keep-me", only["hooks"]!.AsArray()[0]!.AsObject()["command"]!.GetValue<string>());
    }
}