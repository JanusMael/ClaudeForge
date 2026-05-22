using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

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
[TestClass]
public sealed class TypedSurfaceStopBTests
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

    // ── HookEvent.Timeout ────────────────────────────────────────────────

    [TestMethod]
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
        Assert.AreEqual(30, hook.Timeout);
    }

    [TestMethod]
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
        Assert.IsNull(hook.Timeout);
    }

    [TestMethod]
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

        Assert.AreEqual(60, hook.Timeout);
        Assert.IsNotNull(hook.PreservedFields);
        Assert.IsTrue(hook.PreservedFields!.ContainsKey("model"));
        Assert.IsFalse(hook.PreservedFields.ContainsKey("timeout"),
            "After promotion, timeout must not be in PreservedFields.");
    }

    // ── HookEvent.Headers ────────────────────────────────────────────────

    [TestMethod]
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

        Assert.IsNotNull(hook.Headers);
        Assert.AreEqual(2, hook.Headers!.Count);
        Assert.AreEqual("Bearer ${API_TOKEN}", hook.Headers["Authorization"]);
        Assert.AreEqual("claude", hook.Headers["X-Source"]);
    }

    // ── HookEvent.AllowedEnvVars ─────────────────────────────────────────

    [TestMethod]
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

        Assert.IsNotNull(hook.AllowedEnvVars);
        CollectionAssert.AreEqual(
            new[] { "API_TOKEN", "TENANT_ID" },
            hook.AllowedEnvVars!.ToArray());
    }

    // ── HookEvent round-trip via typed properties ────────────────────────

    [TestMethod]
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
        Assert.AreEqual(45, roundTripped.Timeout);
        Assert.IsNotNull(roundTripped.Headers);
        Assert.AreEqual("Bearer xyz", roundTripped.Headers!["Authorization"]);
        Assert.IsNotNull(roundTripped.AllowedEnvVars);
        CollectionAssert.AreEqual(new[] { "MY_TOKEN" }, roundTripped.AllowedEnvVars!.ToArray());
    }

    [TestMethod]
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

        Assert.IsNotNull(hook.PreservedFields);
        Assert.IsTrue(hook.PreservedFields!.ContainsKey("statusMessage"));
        Assert.IsTrue(hook.PreservedFields.ContainsKey("async"));
        Assert.IsFalse(hook.PreservedFields.ContainsKey("timeout"));
        Assert.IsFalse(hook.PreservedFields.ContainsKey("headers"));
        Assert.IsFalse(hook.PreservedFields.ContainsKey("allowedEnvVars"));
    }

    // ── IPermissionsAccessor.DisableBypassPermissionsMode ───────────────

    [TestMethod]
    public void Permissions_DisableBypassPermissionsMode_ReadsAsTypedBool()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = true,
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));

        Assert.IsTrue(client.Permissions.DisableBypassPermissionsMode);
    }

    [TestMethod]
    public void Permissions_DisableBypassPermissionsMode_AbsentResolvesNull()
    {
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(new JsonObject()));
        Assert.IsNull(client.Permissions.DisableBypassPermissionsMode);
    }

    [TestMethod]
    public void Permissions_DisableBypassPermissionsMode_RoundTripsViaSetter()
    {
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(new JsonObject()));

        client.Permissions.DisableBypassPermissionsMode = true;
        Assert.IsTrue(client.Permissions.DisableBypassPermissionsMode);

        // Verify on-disk shape
        JsonObject perms = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.IsTrue(perms["disableBypassPermissionsMode"]!.GetValue<bool>());
    }

    [TestMethod]
    public void Permissions_DisableBypassPermissionsMode_NullSetterRemovesKey()
    {
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = true,
                ["defaultMode"] = "default",
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));
        Assert.IsTrue(client.Permissions.DisableBypassPermissionsMode);

        client.Permissions.DisableBypassPermissionsMode = null;
        Assert.IsNull(client.Permissions.DisableBypassPermissionsMode);

        // Sibling keys preserved (the setter removes only the targeted key,
        // not the whole permissions object).
        JsonObject perms = (JsonObject)client.GetScopeValue("permissions", ConfigScope.User)!;
        Assert.IsFalse(perms.ContainsKey("disableBypassPermissionsMode"));
        Assert.AreEqual("default", perms["defaultMode"]!.GetValue<string>());
    }

    [TestMethod]
    public void Permissions_DisableBypassPermissionsModeAt_PerScope()
    {
        // GetDisableBypassPermissionsModeAt reads only the explicitly-stored
        // value at the requested scope, not the merged effective view.
        JsonObject input = new()
        {
            ["permissions"] = new JsonObject
            {
                ["disableBypassPermissionsMode"] = true,
            },
        };
        using ClaudeCodeClient client = MakeClient(MakeWorkspace(input));

        Assert.IsTrue(client.Permissions.GetDisableBypassPermissionsModeAt(ConfigScope.User));
        Assert.IsNull(client.Permissions.GetDisableBypassPermissionsModeAt(ConfigScope.Project));
    }
}