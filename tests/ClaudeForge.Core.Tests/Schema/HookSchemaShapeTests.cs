using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// diagnostic suite — concern: WHICH HOOK SHAPES does the schema accept,
/// and which does it reject? Answers the question "if I synthesise hook X, do I
/// get validation errors?" by introducing the hook via <c>SetValue</c> against an
/// empty baseline so the delta filter does NOT mask the result.
/// </summary>
/// <remarks>
/// <para>
/// The Claude Code schema (<c>$defs.hookCommand</c>) uses <c>anyOf</c> across
/// 4 branches — <c>command</c>, <c>prompt</c>, <c>agent</c>, <c>http</c> — each
/// with <c>additionalProperties: false</c>. JsonSchema.Net's
/// <c>OutputFormat.List</c> can leak failures from non-matching branches into
/// the error list; the <c>SchemaRegistry.CollectSchemaErrors</c> gate at
/// "if (results.IsValid) return [];" is responsible for suppressing that noise
/// when at least one branch matched. These tests lock that gate.
/// </para>
/// <para>
/// Companion file <see cref="HookValidationDeltaTests"/> covers the
/// orthogonal concern of whether <c>ValidateWorkspaceAsync</c>'s delta filter
/// correctly strips PRE-EXISTING errors from the report when the user's
/// edits don't touch the offending subtree.
/// </para>
/// </remarks>
[TestClass]
public sealed class HookSchemaShapeTests
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
    /// Build a workspace whose baseline has NO hooks, then introduce the supplied
    /// hook via <c>SetValue</c>. The delta filter sees this as a NEW addition;
    /// any schema errors will be reported (not filtered as pre-existing).
    /// Use this fixture when you want to test schema acceptance/rejection of a
    /// specific hook shape.
    /// </summary>
    private static SettingsWorkspace WorkspaceIntroducingHook(string eventName, JsonNode innerHook)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc]);

        JsonObject hookEntry = new()
        {
            ["matcher"] = "Bash",
            ["hooks"] = new JsonArray(innerHook),
        };
        JsonObject hooks = new() { [eventName] = new JsonArray(hookEntry) };
        ws.SetValue("hooks", hooks, ConfigScope.User);
        return ws;
    }

    private static string FormatErrors(IReadOnlyList<SchemaValidationError> errors)
    {
        return errors.Count == 0
            ? "(none)"
            : string.Join("\n", errors.Select(e => $"  {e.DisplayPath}: {e.Message}"));
    }

    // ── Concern A: each documented hook type is accepted ───────────────────

    [TestMethod]
    public async Task Command_Hook_PassesValidation()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "command",
            ["command"] = "echo hello",
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"Valid command hook must pass. Got {errors.Count}:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task Prompt_Hook_PassesValidation()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "prompt",
            ["prompt"] = "Be careful with destructive commands",
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"Valid prompt hook must pass. Got {errors.Count}:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task Agent_Hook_PassesValidation()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "agent",
            ["prompt"] = "Verify the change is safe",
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"Valid agent hook must pass. Got {errors.Count}:\n{FormatErrors(errors)}");
    }

    [TestMethod]
    public async Task Http_Hook_PassesValidation()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "http",
            ["url"] = "https://hooks.example.com/notify",
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            $"Valid http hook must pass. Got {errors.Count}:\n{FormatErrors(errors)}");
    }

    // ── Concern B: malformed hooks are correctly flagged (when introduced) ──

    [TestMethod]
    public async Task Command_Hook_MissingCommand_IsFlagged()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "command",
            // command property intentionally missing
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "A command hook with no command field MUST fail validation when introduced as a new entry.");
    }

    [TestMethod]
    public async Task Hook_WithUnknownType_IsFlagged()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "totally-made-up-type",
            ["banana"] = "yellow",
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "A hook with an unknown type MUST fail validation (no anyOf branch matches).");
    }

    [TestMethod]
    public async Task PromptType_WithCommandField_IsFlagged()
    {
        // additionalProperties:false on each branch means a `prompt` hook
        // cannot also have a `command` field.
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "prompt",
            ["prompt"] = "explain",
            ["command"] = "echo", // forbidden on prompt branch
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "A prompt hook with a command field MUST fail validation.");
    }

    // ── Concern C: anyOf branch noise stays out of validation results ─────

    [TestMethod]
    public async Task ValidCommandHook_DoesNotLeakBranchFailureNoise()
    {
        // The user's reported 18 errors looked like JsonSchema.Net leakage
        // from non-matching anyOf branches. The CollectSchemaErrors gate
        // ("if (results.IsValid) return []") short-circuits when at least one
        // branch matched. A valid hook of ANY type must produce ZERO errors,
        // not ~3-6 errors from the other branches' failed evaluations.
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceIntroducingHook("Stop", new JsonObject
        {
            ["type"] = "command",
            ["command"] = "echo hello",
            ["timeout"] = 30,
        });

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.AreEqual(0, errors.Count,
            "A valid command hook must not leak anyOf branch failures into the user-visible error list.");
    }
}