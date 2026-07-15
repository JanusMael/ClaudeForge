namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// The "overlay ⊆ schema" guard. The editor now derives its live hook-event list
/// FRESH from the schema's <c>hooks.properties</c>; <see cref="HookEventCatalog.CuratedOrder"/>
/// is only a display-ordering overlay + offline fallback. This test fails the
/// build if a curated entry is no longer schema-accepted (a stale overlay entry),
/// and guards the original 2026-05-01 trap — a hardcoded list offering
/// tool-suffixed pseudo-events (<c>PreBashToolUse</c>, <c>PostFileEditToolUse</c>,
/// ...) the schema rejects — from creeping back in via the curated overlay.
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

        foreach (string eventName in HookEventCatalog.CuratedOrder)
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
            "HookEventCatalog.CuratedOrder contains events the schema rejects (stale overlay entry):\n"
            + string.Join("\n", rejected.Select(r => $"  {r.Event}: {r.Path} — {r.Message}")));
    }
}