using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Locks the fix for the user-reported 2026-05-01 issue (3.10 manual test):
///
///   "Added WorktreeCreate command hook with matcher='Bash', type='command',
///    command='where bash'. Save shows 9 validation errors all on the new
///    hook, including 'All values fail against the false schema' and
///    'Required properties [prompt] are not present' — the user's hook is
///    a valid command hook, so why is the schema rejecting it?"
///
/// Root cause: JsonSchema.Net's <c>OutputFormat.List</c> emits a
/// <c>EvaluationResults</c> detail for EVERY anyOf branch even when one
/// sibling matched. The previous <c>SchemaRegistry.CollectSchemaErrors</c>
/// suppressed this leakage only via the early-out
/// <c>if (results.IsValid) return [];</c> — when pre-existing baseline
/// errors elsewhere kept root invalid, leaked anyOf-branch errors at NEW
/// instance paths slipped through the delta filter (which keys on
/// <c>(InstancePath, Message)</c>).
///
/// Fix: <c>CollectSchemaErrors</c> now pre-computes a set of "passing anyOf
/// branch roots" by their <c>EvaluationPath</c> and suppresses errors whose
/// path traverses any anyOf site with a passing sibling. Tests below pin
/// the contract.
/// </summary>
[TestClass]
public sealed class HookAnyOfLeakageDiagnosticTests
{
    private sealed class FailingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            throw new HttpRequestException("Tests must not hit network");
        }
    }

    public TestContext? TestContext { get; set; }

    private static SchemaRegistry CreateRegistry()
    {
        return new SchemaRegistry(new HttpClient(new FailingHttpHandler()));
    }

    [TestMethod]
    public async Task UserReported_WorktreeCreateCommandHook_Plus_UnknownEventBaseline()
    {
        // Baseline: the user's actual config shape. Several known events with
        // command hooks AND two unknown event names (PermissionDenied,
        // StopFailure) that they apparently received from an external tool.
        // These keep the root invalid (additionalProperties:false on /hooks
        // rejects them) so the gate at line 301 of SchemaRegistry doesn't
        // early-return — exposing the per-detail iteration to leaked
        // anyOf-branch errors.
        JsonObject baseline = new()
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray(CommandHookMatcher("gk.exe ai hook")),
                ["PostToolUse"] = new JsonArray(CommandHookMatcher("gk.exe ai hook")),
                ["Stop"] = new JsonArray(CommandHookMatcher("gk.exe ai hook")),
                ["PermissionDenied"] = new JsonArray(CommandHookMatcher("gk.exe ai hook")),
                ["StopFailure"] = new JsonArray(CommandHookMatcher("gk.exe ai hook")),
            },
        };

        // Edited: same baseline + a new, valid command hook on WorktreeCreate.
        JsonObject edited = (JsonObject)baseline.DeepClone()!;
        edited["hooks"]!.AsObject()["WorktreeCreate"] = new JsonArray(new JsonObject
        {
            ["matcher"] = "Bash",
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = "where bash",
            }),
        });

        SettingsDocument doc = new(ConfigScope.User, "settings.json",
            baseline.DeepClone()!.AsObject(),
            isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        ws.SetValue("hooks", edited["hooks"]!.DeepClone(), ConfigScope.User);

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        if (errors.Count != 0)
        {
            TestContext?.WriteLine($"Unexpected errors ({errors.Count}):");
            foreach (SchemaValidationError err in errors)
            {
                TestContext?.WriteLine($"  {err.InstancePath} | {err.Message}");
            }
        }

        Assert.AreEqual(0, errors.Count,
            "Adding a valid command hook on WorktreeCreate should produce zero net-new "
            + "validation errors. Failure means JsonSchema.Net's anyOf-branch leakage is "
            + "no longer being suppressed by SchemaRegistry.CollectSchemaErrors — check "
            + "the TryGetPassingAnyOfBranchPrefix / IsLeakedAnyOfBranchError helpers.");
        return;

        // Build a hookMatcher for a command hook (mirrors the user's actual
        // gk.exe wrapper present on every event).
        static JsonObject CommandHookMatcher(string command)
        {
            return new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            };
        }
    }

    [TestMethod]
    public async Task GenuineCommandHookFailure_StillEmitsRealErrors()
    {
        // Adversarial: ensure the suppression doesn't silently eat REAL
        // failures. A hook with type='command' but no 'command' field fails
        // EVERY anyOf branch, so no sibling passes — the errors must come
        // through. Without this counter-test, an over-aggressive suppressor
        // would silently swallow legitimate user mistakes.
        SettingsWorkspace ws = new([
            new SettingsDocument(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false)
        ]);
        ws.SetValue("hooks", new JsonObject
        {
            ["Stop"] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    // no 'command', no 'prompt', no 'url' — fails every branch
                }),
            }),
        }, ConfigScope.User);

        using SchemaRegistry registry = CreateRegistry();
        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "A hook that matches no anyOf branch must still emit errors — the "
            + "leaked-branch suppression should only fire when at least one sibling matched.");
    }
}