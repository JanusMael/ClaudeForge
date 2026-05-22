namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the agreement between <see cref="HooksEditorViewModel.KnownEventTypes"/>
/// (the events the GUI's left rail offers) and the Claude Code settings
/// schema's <c>hooks.properties</c> allowlist.
///
/// Two contracts:
///
/// 1. Every event the editor offers must be schema-accepted. The 2026-05-01
///    bug was the GUI offering tool-suffixed pseudo-events
///    (<c>PreBashToolUse</c>, <c>PostFileEditToolUse</c>, ...) that the
///    schema rejected — the user picked one and got blocked on save with
///    a confusing message. This test catches a recurrence at build time.
///
/// 2. (Looser) The editor should not silently drop schema-accepted events
///    — if a future schema upgrade adds a new hook event the editor will
///    miss it from its left rail until KnownEventTypes is updated. This is
///    less critical (users can still use the event via external edit), so
///    we only WARN via Inconclusive rather than fail.
/// </summary>
[TestClass]
public sealed class HooksKnownEventTypesParityTests
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

    private static SettingsWorkspace WorkspaceWithEvent(string eventName)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json", new JsonObject(), isReadOnly: false);
        SettingsWorkspace ws = new([doc]);
        JsonObject hooks = new()
        {
            [eventName] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "true",
                }),
            }),
        };
        ws.SetValue("hooks", hooks, ConfigScope.User);
        return ws;
    }

    [TestMethod]
    public async Task EveryKnownEventType_IsSchemaAccepted()
    {
        using SchemaRegistry registry = CreateRegistry();
        List<(string Event, string Path, string Message)> rejected = new();

        foreach (string eventName in HooksEditorViewModel.KnownEventTypes)
        {
            SettingsWorkspace ws = WorkspaceWithEvent(eventName);
            IReadOnlyList<SchemaValidationError> errors = await registry.ValidateWorkspaceAsync(ws, isClaudeCode: true);

            // The only acceptable error here is one that does NOT name our
            // event (e.g. a coincidentally-failing branch elsewhere). Any
            // error whose path mentions our event means the schema rejected
            // it — that's the bug we're guarding against.
            foreach (SchemaValidationError err in errors.Where(e => e.InstancePath.Contains("/hooks/" + eventName)))
            {
                rejected.Add((eventName, err.InstancePath, err.Message));
            }
        }

        Assert.AreEqual(0, rejected.Count,
            "KnownEventTypes contains events the schema rejects:\n"
            + string.Join("\n", rejected.Select(r => $"  {r.Event}: {r.Path} — {r.Message}")));
    }
}