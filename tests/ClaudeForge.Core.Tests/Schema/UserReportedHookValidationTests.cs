using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// The user's <c>~/.claude/settings.json</c> has:
/// <list type="bullet">
///   <item><c>hooks.Stop[0].hooks</c>: 6 entries, all <c>{type: command, command: "..."}</c></item>
///   <item><c>hooks.Stop[1].hooks</c>: 1 entry (no matcher), command type</item>
///   <item><c>hooks.SessionEnd[0].hooks</c>: 1 entry, command type</item>
///   <item><c>hooks.SessionEnd[1].hooks</c>: 1 entry (no matcher), command type</item>
/// </list>
/// All hooks ARE valid command-type hooks.  Yet validation reported 18
/// errors at <c>hooks → Stop → 0 → hooks → 6</c> (out of bounds — array
/// has indices 0..5) and <c>hooks → SessionEnd → 0 → hooks → 1</c>
/// (out of bounds — array has only index 0).
/// </summary>
/// <remarks>
/// <para>
/// This test class reconstructs the user's structure faithfully and
/// runs validation. If it reproduces the 18-error report, the bug is
/// in <c>SchemaRegistry.CollectSchemaErrors</c> error reporting at the
/// validator level. If it does not reproduce, the bug is elsewhere
/// (e.g. an editor side-effect mutating the workspace before validation).
/// </para>
/// <para>
/// The actual command strings are placeholder values — schema validation
/// for <c>{type: command, command: &lt;non-empty-string&gt;}</c> only
/// checks that <c>command</c> is a string with <c>minLength: 1</c>, so
/// the precise content does not affect validity.
/// </para>
/// </remarks>
[TestClass]
public sealed class UserReportedHookValidationTests
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
    /// Build the user's actual <c>hooks</c> block as observed in their
    /// settings.json on 2026-04-29. Six command hooks under Stop[0],
    /// one matcher-less command hook at Stop[1], symmetric SessionEnd.
    /// All other hook events have a single matcher-less command-type
    /// entry (the gk.exe hook).
    /// </summary>
    private static JsonObject BuildUserHooksBlock()
    {
        // Match the user's structure exactly.
        return new JsonObject
        {
            ["PreToolUse"] = new JsonArray(
                Group("Bash", Cmd("npx block-no-verify@1.1.2")),
                GroupNoMatcher(Cmd("gk.exe ai hook run --host claude-code"))),

            ["PostToolUse"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run --host claude-code"))),

            ["PreCompact"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run --host claude-code"))),

            // The user's actual Stop event — the focal point of the bug report.
            // 6 inner hooks at Stop[0].hooks (indices 0..5) plus a 2nd outer
            // matcher-less group at Stop[1] with 1 hook.
            ["Stop"] = new JsonArray(
                Group("*",
                    Cmd("placeholder-stop-format-typecheck"), // index 0
                    Cmd("placeholder-stop-check-console-log"), // index 1
                    Cmd("placeholder-stop-session-end"), // index 2
                    Cmd("placeholder-stop-evaluate-session"), // index 3
                    Cmd("placeholder-stop-cost-tracker"), // index 4
                    Cmd("placeholder-stop-desktop-notify")), // index 5 (NO index 6)
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),

            ["SubagentStop"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),

            ["Notification"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),

            // The user's actual SessionEnd — 1 inner hook (index 0 only;
            // NO index 1) plus matcher-less group at SessionEnd[1].
            ["SessionEnd"] = new JsonArray(
                Group("*", Cmd("placeholder-session-end-marker")), // index 0 only
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),

            ["SessionStart"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["StopFailure"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["SubagentStart"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["TaskCompleted"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["TeammateIdle"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["UserPromptSubmit"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["ConfigChange"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["CwdChanged"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["Elicitation"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["ElicitationResult"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["InstructionsLoaded"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["PermissionDenied"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["PermissionRequest"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["PostCompact"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
            ["PostToolUseFailure"] = new JsonArray(
                GroupNoMatcher(Cmd("gk.exe ai hook run"))),
        };

        // Helper: a valid command-type inner hook with a placeholder string.
        static JsonObject Cmd(string command)
        {
            return new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
            };
        }

        // Helper: outer group with matcher
        static JsonObject Group(string matcher, params JsonNode[] hooks)
        {
            return new JsonObject
            {
                ["matcher"] = matcher,
                ["hooks"] = new JsonArray(hooks),
            };
        }

        // Helper: outer group WITHOUT matcher (no key set)
        static JsonObject GroupNoMatcher(params JsonNode[] hooks)
        {
            return new JsonObject
            {
                ["hooks"] = new JsonArray(hooks),
            };
        }
    }

    private static SettingsWorkspace WorkspaceWithUserHooks()
    {
        JsonObject initialRoot = new()
        {
            ["hooks"] = BuildUserHooksBlock(),
        };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        // Simulate the user's edit: change permissions.defaultMode (anything
        // unrelated to hooks). The doc must be dirty for ValidateWorkspaceAsync
        // to visit it. "default" is in the schema's enum.
        ws.SetValue("permissions", new JsonObject { ["defaultMode"] = "default" }, ConfigScope.User);
        return ws;
    }

    [TestMethod]
    public async Task UserSettings_AllHooksValid_ChangePermissionsDefaultMode_ReportsZeroErrors()
    {
        // The user's hooks are ALL well-formed command-type entries. Their
        // edit was unrelated (permissions.defaultMode). Expected: zero
        // schema errors reported, because (a) the hooks themselves are
        // valid, and (b) even if validation produced errors, the delta
        // filter would strip them since baseline == current at hooks.
        //
        // If this test fails, we have reproduced the user's bug. The
        // failure message will dump every reported error so we can see
        // EXACTLY which path / message combo is leaking through.
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceWithUserHooks();

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        string formatted = errors.Count == 0
            ? "(none)"
            : string.Join("\n", errors.Select(e => $"  {e.DisplayPath}: {e.Message}"));

        Assert.AreEqual(0, errors.Count,
            $"User's actual hook structure must validate cleanly. Got {errors.Count} errors:\n{formatted}");
    }

    [TestMethod]
    public async Task UserSettings_BaselineSnapshot_NoErrorsAtPhantomIndices()
    {
        // The user's report cited paths "hooks → Stop → 0 → hooks → 6"
        // (Stop[0].hooks has only indices 0..5) and
        // "hooks → SessionEnd → 0 → hooks → 1" (SessionEnd[0].hooks has
        // only index 0). These are out-of-bounds indices.
        //
        // Validate the baseline (clean, exactly as on disk) directly to
        // see whether the validator itself produces phantom-index error
        // paths, independent of any delta filter or editor side effect.
        using SchemaRegistry registry = CreateRegistry();

        // Construct workspace where baseline IS the data we want to validate.
        // Skip the SetValue call — just check the validator on a dirty doc
        // whose Root equals the user's structure.
        JsonObject initialRoot = new() { ["hooks"] = BuildUserHooksBlock() };
        SettingsDocument doc = new(ConfigScope.User, "settings.json", initialRoot, isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        // Force-dirty by setting an unrelated key.
        ws.SetValue("model", JsonValue.Create("opus"), ConfigScope.User);

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        // Report any error whose InstancePath references an index >= the
        // actual array length (e.g. /hooks/Stop/0/hooks/6 when the array
        // has 6 entries).
        List<SchemaValidationError> phantomErrors = errors
                                                    .Where(e => e.InstancePath.Contains("/hooks/Stop/0/hooks/6")
                                                                || e.InstancePath.Contains("/hooks/SessionEnd/0/hooks/1"))
                                                    .ToList();

        string formatted = phantomErrors.Count == 0
            ? "(none)"
            : string.Join("\n", phantomErrors.Select(e => $"  {e.DisplayPath}: {e.Message}"));

        Assert.AreEqual(0, phantomErrors.Count,
            $"Validator must NOT report errors at out-of-bounds array indices. " +
            $"Got {phantomErrors.Count} phantom-index errors:\n{formatted}");
    }
}