using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

/// <summary>
/// Verifies the four fields
/// promoted from <c>PreservedFields</c> to typed properties:
/// <list type="bullet">
///   <item><see cref="HookEvent.Timeout"/></item>
///   <item><see cref="HookEvent.Headers"/></item>
///   <item><see cref="HookEvent.AllowedEnvVars"/></item>
///   <item><see cref="IPermissionsAccessor.DisableBypassPermissionsMode"/></item>
/// </list>
/// </summary>
public sealed class TypedSurfaceStopBTests
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

    // ── HookEvent.Timeout ────────────────────────────────────────────────

    [Fact]
    public void HookEvent_Timeout_ReadsAsTypedInt()
    {
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = "echo go",
                                ["timeout"] = 30,
                            },
                        },
                    },
                },
            },
        };
        SettingsWorkspace ws = MakeWorkspace(input);
        using ClaudeCodeClient client = MakeClient(ws);

        HookEvent hook = client.Hooks.Events.Single();
        Assert.Equal(30, hook.Timeout);
    }

    [Fact]
    public void HookEvent_Timeout_AbsentResolvesNull()
    {
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "command", ["command"] = "echo" },
                        },
                    },
                },
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        HookEvent hook = client.Hooks.Events.Single();
        Assert.Null(hook.Timeout);
    }

    [Fact]
    public void HookEvent_Timeout_NotInPreservedFieldsAfterPromotion()
    {
        // After promotion, the typed property is the single source of truth.
        // PreservedFields carries only fields the SDK still doesn't model.
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "Bash",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "command",
                                ["command"] = "echo",
                                ["timeout"] = 60,
                                ["model"] = "sonnet", // genuinely-unknown
                            },
                        },
                    },
                },
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        HookEvent hook = client.Hooks.Events.Single();

        Assert.Equal(60, hook.Timeout);
        Assert.NotNull(hook.PreservedFields);
        Assert.True(hook.PreservedFields!.ContainsKey("model"));
        Assert.False(hook.PreservedFields.ContainsKey("timeout"),
            "After promotion, timeout must not be in PreservedFields.");
    }

    // ── HookEvent.Headers ────────────────────────────────────────────────

    [Fact]
    public void HookEvent_Headers_ReadsAsTypedDictionary()
    {
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "url",
                                ["url"] = "https://example.com/hook",
                                ["headers"] = new JsonObject
                                {
                                    ["Authorization"] = "Bearer ${API_TOKEN}",
                                    ["X-Source"] = "claude",
                                },
                            },
                        },
                    },
                },
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        HookEvent hook = client.Hooks.Events.Single();

        Assert.NotNull(hook.Headers);
        Assert.Equal(2, hook.Headers!.Count);
        Assert.Equal("Bearer ${API_TOKEN}", hook.Headers["Authorization"]);
        Assert.Equal("claude", hook.Headers["X-Source"]);
    }

    // ── HookEvent.AllowedEnvVars ─────────────────────────────────────────

    [Fact]
    public void HookEvent_AllowedEnvVars_ReadsAsTypedList()
    {
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "url",
                                ["url"] = "https://x.example",
                                ["allowedEnvVars"] = new JsonArray("API_TOKEN", "TENANT_ID"),
                            },
                        },
                    },
                },
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        HookEvent hook = client.Hooks.Events.Single();

        Assert.NotNull(hook.AllowedEnvVars);
        Assert.Equal(
            new[] { "API_TOKEN", "TENANT_ID" },
            hook.AllowedEnvVars!.ToArray());
    }

    // ── HookEvent round-trip via typed properties ────────────────────────

    [Fact]
    public void HookEvent_TypedFields_RoundTripViaAdd()
    {
        // Construct a fresh hook programmatically (no on-disk JSON to
        // preserve) — typed properties are the only way to set the new
        // fields.  Add → re-read → typed properties survive.
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(new JsonObject()));

        Dictionary<string, string> headers = new()
        {
            ["Authorization"] = "Bearer xyz",
        };
        string[] envVars = ["MY_TOKEN"];

        client.Hooks.Add(new HookEvent("Stop", "*", HookCommandType.Url, "https://x.example")
        {
            Timeout = 45,
            Headers = headers,
            AllowedEnvVars = envVars,
        });

        HookEvent roundTripped = client.Hooks.Events.Single();
        Assert.Equal(45, roundTripped.Timeout);
        Assert.NotNull(roundTripped.Headers);
        Assert.Equal("Bearer xyz", roundTripped.Headers!["Authorization"]);
        Assert.NotNull(roundTripped.AllowedEnvVars);
        Assert.Equal(new[] { "MY_TOKEN" }, roundTripped.AllowedEnvVars!.ToArray());
    }

    [Fact]
    public void HookEvent_PreservedFields_PromotionLeavesUnknownsBehind()
    {
        // Verifies PreservedFields ONLY contains genuinely-unknown fields
        // post-Stop-B.  All four promoted fields go to typed properties.
        JsonObject input = new()
        {
            ["hooks"] = new JsonObject
            {
                ["Stop"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "url",
                                ["url"] = "https://x.example",
                                ["timeout"] = 30,
                                ["headers"] = new JsonObject { ["k"] = "v" },
                                ["allowedEnvVars"] = new JsonArray("X"),
                                ["statusMessage"] = "running", // genuinely-unknown
                                ["async"] = true, // genuinely-unknown
                            },
                        },
                    },
                },
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        HookEvent hook = client.Hooks.Events.Single();

        Assert.NotNull(hook.PreservedFields);
        Assert.True(hook.PreservedFields!.ContainsKey("statusMessage"));
        Assert.True(hook.PreservedFields.ContainsKey("async"));
        Assert.False(hook.PreservedFields.ContainsKey("timeout"));
        Assert.False(hook.PreservedFields.ContainsKey("headers"));
        Assert.False(hook.PreservedFields.ContainsKey("allowedEnvVars"));
    }

    // ── IPermissionsAccessor.DisableBypassPermissionsMode ───────────────

    [Fact]
    public void Permissions_DisableBypassPermissionsMode_ReadsAsTypedBool()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = "disable",
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));

        Assert.True(client.Permissions.DisableBypassPermissionsMode);
    }

    [Fact]
    public void Permissions_DisableBypassPermissionsMode_AbsentResolvesNull()
    {
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(new JsonObject()));
        Assert.Null(client.Permissions.DisableBypassPermissionsMode);
    }

    [Fact]
    public void Permissions_DisableBypassPermissionsMode_RoundTripsViaSetter()
    {
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(new JsonObject()));

        client.Permissions.DisableBypassPermissionsMode = true;
        Assert.True(client.Permissions.DisableBypassPermissionsMode);

        // Verify on-disk shape: the schema value is the string "disable", NOT a bool.
        JsonObject perms = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.Equal("disable", perms["disableBypassPermissionsMode"]!.GetValue<string>());
    }

    [Fact]
    public void Permissions_DisableBypassPermissionsMode_NullSetterRemovesKey()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = "disable",
                ["defaultMode"] = "default",
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        Assert.True(client.Permissions.DisableBypassPermissionsMode);

        client.Permissions.DisableBypassPermissionsMode = null;
        Assert.Null(client.Permissions.DisableBypassPermissionsMode);

        // Sibling keys preserved (the setter removes only the targeted key,
        // not the whole permissions object).
        JsonObject perms = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.False(perms.ContainsKey("disableBypassPermissionsMode"));
        Assert.Equal("default", perms["defaultMode"]!.GetValue<string>());
    }

    [Fact]
    public void Permissions_DisableBypassPermissionsModeAt_PerScope()
    {
        // GetDisableBypassPermissionsModeAt reads only the explicitly-stored
        // value at the requested scope, not the merged effective view.
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = "disable",
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));

        Assert.True(client.Permissions.GetDisableBypassPermissionsModeAt(ConfigScope.User));
        Assert.Null(client.Permissions.GetDisableBypassPermissionsModeAt(ConfigScope.Project));
    }
}