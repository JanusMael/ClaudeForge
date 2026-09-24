using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

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
public sealed class TypedSurfaceStopATests
{
    private static SettingsWorkspace MakeWorkspace(JsonObject settings)
    {
        SettingsDocument doc = new(ConfigScope.User, "settings.json", settings, isReadOnly: false);
        return new SettingsWorkspace([doc], ClaudeMergePolicy.Instance);
    }

    private static ClaudeCodeClient MakeClient(SettingsWorkspace ws)
    {
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, new SchemaRegistry());
    }

    // ── McpServer.Description ─────────────────────────────────────────────

    [Fact]
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
        Assert.NotNull(server);
        Assert.Equal("test description", server!.Description);
    }

    [Fact]
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
        Assert.Equal("test", server.Description);

        // PreservedFields should contain "future" but NOT "description".
        Assert.NotNull(server.PreservedFields);
        Assert.True(server.PreservedFields!.ContainsKey("future"));
        Assert.False(server.PreservedFields.ContainsKey("description"),
            "After promotion, description must not be in PreservedFields — typed property is single source of truth.");
    }

    [Fact]
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
        Assert.Equal("programmatically set", entry["description"]!.GetValue<string>());

        // Re-read via typed accessor.
        McpServer roundTripped = client.McpServers.Get("s")!;
        Assert.Equal("programmatically set", roundTripped.Description);
    }

    [Fact]
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
        MessageAssert.Equal("fresh",
            output["s"]!.AsObject()["description"]!.GetValue<string>(),
            "Typed Description must win on collision with a PreservedFields entry of the same key.");
    }

    // ── IPermissionsAccessor.AdditionalDirectories ────────────────────────

    [Fact]
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
        Assert.Equal(2, dirs.Count);
        Assert.Equal(
            new[] { "/Users/alice/projects", "~/work" },
            dirs.ToList());
    }

    [Fact]
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
        Assert.Single(dirs);
        Assert.Equal("/scope/specific", dirs[0]);
    }

    [Fact]
    public void Permissions_AddAdditionalDirectory_AppendsToList()
    {
        SettingsWorkspace ws = MakeWorkspace(new JsonObject());
        using ClaudeCodeClient client = MakeClient(ws);

        client.Permissions.AddAdditionalDirectory("/foo");
        client.Permissions.AddAdditionalDirectory("/bar");
        client.Permissions.AddAdditionalDirectory("/foo"); // dedup — no-op

        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectoriesAt(ConfigScope.User);
        Assert.Equal(2, dirs.Count);
        Assert.Equal(new[] { "/foo", "/bar" }, dirs.ToList());
    }

    [Fact]
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

        Assert.True(client.Permissions.RemoveAdditionalDirectory("/bar"));
        IReadOnlyList<string> dirs = client.Permissions.AdditionalDirectoriesAt(ConfigScope.User);
        Assert.Equal(new[] { "/foo", "/baz" }, dirs.ToList());

        Assert.False(client.Permissions.RemoveAdditionalDirectory("/notthere"));
    }

    [Fact]
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
        Assert.False(permissions.ContainsKey("additionalDirectories"),
            "Empty additionalDirectories must be removed from permissions, not left as []");
        Assert.True(permissions.ContainsKey("defaultMode"),
            "Sibling fields must survive the nested remove.");
    }

    [Fact]
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
        Assert.Equal("default", permissions["defaultMode"]!.GetValue<string>());
        Assert.Equal("Read", permissions["allow"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("disable", permissions["disableBypassPermissionsMode"]!.GetValue<string>());
        Assert.Equal("/extra", permissions["additionalDirectories"]!.AsArray()[0]!.GetValue<string>());
    }
}