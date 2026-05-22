using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Pins the schema's behaviour when the user (via the editor or external edit)
/// places a hook under an event name the schema doesn't recognise. Two
/// concerns:
///
/// 1. The schema MUST reject — it has <c>additionalProperties: false</c> on
///    the <c>hooks</c> object, so unknown event names like
///    <c>PreBashToolUse</c> (a real-world user mistake reported 2026-05-01,
///    surfaced because the GUI's KnownEventTypes list incorrectly offered
///    that name as a valid choice) are not allowed.
///
/// 2. The validator's raw message ("All values fail against the false
///    schema") leaks JsonSchema.Net implementation jargon that means nothing
///    to a user. This test captures the InstancePath + Message produced so
///    the friendly-message translator (<see
///    cref="Bennewitz.Ninja.ClaudeForge.ViewModels.MainWindowViewModel.FriendlySchemaMessage"
///    />) can be authored against a known-real error shape.
/// </summary>
[TestClass]
public sealed class HookUnknownEventValidationTests
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

    private static SettingsWorkspace WorkspaceWithUnknownEvent(string eventName)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc]);

        JsonObject hookEntry = new()
        {
            ["matcher"] = "*",
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = "where bash",
            }),
        };
        JsonObject hooks = new() { [eventName] = new JsonArray(hookEntry) };
        ws.SetValue("hooks", hooks, ConfigScope.User);
        return ws;
    }

    [TestMethod]
    public async Task UnknownEventName_ProducesValidationError()
    {
        using SchemaRegistry registry = CreateRegistry();
        SettingsWorkspace workspace = WorkspaceWithUnknownEvent("PreBashToolUse");

        IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(workspace, isClaudeCode: true);

        Assert.IsTrue(errors.Count > 0,
            "Schema with additionalProperties:false on /hooks must reject the unknown event 'PreBashToolUse'.");

        // Pin the exact validator output. The friendly-message translator
        // in SchemaErrorMessages.Friendly is keyed on this (InstancePath,
        // Message) pair — if a JsonSchema.Net upgrade changes either
        // string, the translator silently stops matching and the user
        // sees the raw "false schema" message again. Make that loud here.
        SchemaValidationError? unknownEventError = errors.FirstOrDefault(e => e.InstancePath.Contains("PreBashToolUse"));
        Assert.IsNotNull(unknownEventError,
            "Expected at least one error whose path names the unknown event.");
        Assert.AreEqual("/hooks/PreBashToolUse", unknownEventError!.InstancePath,
            "Friendly translator keys on this exact path; if it changes, update SchemaErrorMessages.Friendly accordingly.");
        StringAssert.Contains(unknownEventError.Message, "false schema",
            "Friendly translator detects the unknown-hook-event case via this 'false schema' substring.");
    }

    public TestContext? TestContext { get; set; }
}