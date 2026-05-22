using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// Verifies the two fields
/// promoted from <c>PreservedFields</c> to typed properties:
/// <list type="bullet">
///   <item><see cref="McpServer.Description"/></item>
///   <item><see cref="IPermissionsAccessor.AdditionalDirectories"/></item>
/// </list>
/// </summary>
/// <remarks>
/// Stops B (more typed properties — Hook timeout, headers, etc.) and C
/// (UI affordances) are deferred.
/// </remarks>
[TestClass]
public sealed class TypedSurfaceStopATests
{
    private static SettingsWorkspace MakeWorkspace(JsonObject settings)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json", settings, isReadOnly: false);
        return new SettingsWorkspace([doc]);
    }

    private static ClaudeCodeClient MakeClient(SettingsWorkspace ws)
    {
        return ClaudeCodeClient.FromExistingWorkspace(
            ws, ConfigScope.User, new SchemaRegistry(new HttpClient()));
    }

    // ── McpServer.Description ─────────────────────────────────────────────

    [TestMethod]
    public void McpServer_Description_ReadsFromTypedProperty()
    {
        JsonObject input = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["s"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "x",
                    ["description"] = "test description",
                },
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        McpServer? server = client.McpServers.Get("s");
        Assert.IsNotNull(server);
        Assert.AreEqual("test description", server!.Description);
    }

    [TestMethod]
    public void McpServer_Description_NotInPreservedFields_AfterPromotion()
    {
        // After promoting Description to typed, it should NOT appear in
        // PreservedFields anymore — single source of truth.
        JsonObject input = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["s"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "x",
                    ["description"] = "test",
                    ["future"] = "value", // genuinely-unknown field
                },
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        McpServer server = client.McpServers.Get("s")!;
        Assert.AreEqual("test", server.Description);

        // PreservedFields should contain "future" but NOT "description".
        Assert.IsNotNull(server.PreservedFields);
        Assert.IsTrue(server.PreservedFields!.ContainsKey("future"));
        Assert.IsFalse(server.PreservedFields.ContainsKey("description"),
            "After promotion, description must not be in PreservedFields — typed property is single source of truth.");
    }

    [TestMethod]
    public void McpServer_Description_RoundTripsViaTypedProperty()
    {
        // Construct a fresh server programmatically (no on-disk JSON to
        // preserve) — the typed property is the only way to set
        // description.
        SettingsWorkspace ws = MakeWorkspace(new JsonObject());
        using ClaudeCodeClient client = MakeClient(ws);

        client.McpServers.Set("s", new McpServer("s", McpTransport.Stdio,
            Command: "echo",
            Description: "programmatically set"));

        JsonObject output = (JsonObject)client.GetScopeValue("mcpServers", ConfigScope.User)!;
        JsonObject entry = output["s"]!.AsObject();
        Assert.AreEqual("programmatically set", entry["description"]!.GetValue<string>());

        // Re-read via typed accessor.
        McpServer roundTripped = client.McpServers.Get("s")!;
        Assert.AreEqual("programmatically set", roundTripped.Description);
    }

    [TestMethod]
    public void McpServer_TypedDescription_WinsOverColliding_PreservedField()
    {
        // Defensive: if a caller manually injects "description" into
        // PreservedFields AND sets the typed Description, the typed value
        // must win (matches the McpServer collision-precedence contract).
        SettingsWorkspace ws = MakeWorkspace(new JsonObject());
        using ClaudeCodeClient client = MakeClient(ws);

        JsonObject preserved = new() { ["description"] = "stale" };
        McpServer server = new("s", McpTransport.Stdio,
            Command: "x",
            Description: "fresh")
        {
            PreservedFields = preserved,
        };
        client.McpServers.Set("s", server);

        JsonObject output = (JsonObject)client.GetScopeValue("mcpServers", ConfigScope.User)!;
        Assert.AreEqual("fresh",
            output["s"]!.AsObject()["description"]!.GetValue<string>(),
            "Typed Description must win on collision with a PreservedFields entry of the same key.");
    }

    // ── IPermissionsAccessor.AdditionalDirectories ────────────────────────

    [TestMethod]
    public void Permissions_AdditionalDirectories_ReadsFromEffectiveView()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["additionalDirectories"] = new JsonArray("/Users/alice/projects", "~/work"),
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectories;
        Assert.AreEqual(2, dirs.Count);
        CollectionAssert.AreEqual(
            new[] { "/Users/alice/projects", "~/work" },
            dirs.ToList());
    }

    [TestMethod]
    public void Permissions_AdditionalDirectoriesAt_ReadsFromSpecificScope()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["additionalDirectories"] = new JsonArray("/scope/specific"),
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectoriesAt(ConfigScope.User);
        Assert.AreEqual(1, dirs.Count);
        Assert.AreEqual("/scope/specific", dirs[0]);
    }

    [TestMethod]
    public void Permissions_AddAdditionalDirectory_AppendsToList()
    {
        SettingsWorkspace ws = MakeWorkspace(new JsonObject());
        using ClaudeCodeClient client = MakeClient(ws);

        client.Permissions.AddAdditionalDirectory("/foo");
        client.Permissions.AddAdditionalDirectory("/bar");
        client.Permissions.AddAdditionalDirectory("/foo"); // dedup — no-op

        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectoriesAt(ConfigScope.User);
        Assert.AreEqual(2, dirs.Count);
        CollectionAssert.AreEqual(new[] { "/foo", "/bar" }, dirs.ToList());
    }

    [TestMethod]
    public void Permissions_RemoveAdditionalDirectory_RemovesEntry()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["additionalDirectories"] = new JsonArray("/foo", "/bar", "/baz"),
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        Assert.IsTrue(client.Permissions.RemoveAdditionalDirectory("/bar"));
        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectoriesAt(ConfigScope.User);
        CollectionAssert.AreEqual(new[] { "/foo", "/baz" }, dirs.ToList());

        Assert.IsFalse(client.Permissions.RemoveAdditionalDirectory("/notthere"));
    }

    [TestMethod]
    public void Permissions_RemoveLastAdditionalDirectory_DropsKeyEntirely()
    {
        // When the array empties, the key should be removed from the JSON
        // (matches the same pattern Allow/Deny/Ask use). Include a sibling
        // field so the parent permissions object survives the nested
        // remove (otherwise SDK's nested-RemoveValue cascades and drops
        // the empty parent — correct behaviour, but obscures this test).
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["defaultMode"] = "default",
                ["additionalDirectories"] = new JsonArray("/only"),
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        client.Permissions.RemoveAdditionalDirectory("/only");

        JsonObject permissions = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.IsFalse(permissions.ContainsKey("additionalDirectories"),
            "Empty additionalDirectories must be removed from permissions, not left as []");
        Assert.IsTrue(permissions.ContainsKey("defaultMode"),
            "Sibling fields must survive the nested remove.");
    }

    [TestMethod]
    public void Permissions_AdditionalDirectories_PreservesOtherFields()
    {
        // Adding an additional directory must not disturb other permissions
        // sub-fields (allow/deny/ask/defaultMode/disableBypassPermissionsMode/etc).
        // The SDK's nested SetValue("permissions.additionalDirectories", ...)
        // path correctly merges into the existing permissions object.
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["defaultMode"] = "default",
                ["allow"] = new JsonArray("Read"),
                ["disableBypassPermissionsMode"] = "disable",
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        client.Permissions.AddAdditionalDirectory("/extra");

        JsonObject permissions = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.AreEqual("default", permissions["defaultMode"]!.GetValue<string>());
        Assert.AreEqual("Read", permissions["allow"]!.AsArray()[0]!.GetValue<string>());
        Assert.AreEqual("disable", permissions["disableBypassPermissionsMode"]!.GetValue<string>());
        Assert.AreEqual("/extra", permissions["additionalDirectories"]!.AsArray()[0]!.GetValue<string>());
    }
}