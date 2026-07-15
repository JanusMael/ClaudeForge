using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;
using Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// End-to-end tests for the five strongly-typed accessors. Each test exercises
/// the public read/write surface against a real on-disk workspace and verifies
/// the resulting JSON shape round-trips through Save/Reload.
/// </summary>
[TestClass]
public class AccessorsTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-sdk-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = _previousOverride;
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            /* best-effort */
        }
    }

    private async Task<ClaudeCodeClient> OpenAsync()
    {
        ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    // ── Permissions ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Permissions_DefaultMode_RoundTripsViaCamelCase()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.DefaultMode = PermissionDefaultMode.AcceptEdits;

        Assert.AreEqual(PermissionDefaultMode.AcceptEdits, client.Permissions.DefaultMode);

        // Verify the on-disk JSON uses the documented camelCase string.
        await client.SaveAsync(force: true, CancellationToken.None);
        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        StringAssert.Contains(json, "\"defaultMode\": \"acceptEdits\"");
    }

    [TestMethod]
    public async Task Permissions_AddAllow_AppendsRule_AndDedupes()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(PermissionRule.Parse("Bash(git status)"));
        client.Permissions.AddAllow(PermissionRule.Parse("Read"));
        client.Permissions.AddAllow(PermissionRule.Parse("Bash(git status)")); // duplicate — must be a no-op

        IReadOnlyList<PermissionRule> allow = client.Permissions.Allow;
        Assert.AreEqual(2, allow.Count);
        Assert.IsTrue(allow.Any(r => r.Value == "Bash(git status)"));
        Assert.IsTrue(allow.Any(r => r.Value == "Read"));
    }

    [TestMethod]
    public async Task Permissions_RemoveAllow_DeletesRule_AndCleansEmptyArray()
    {
        using ClaudeCodeClient client = await OpenAsync();
        PermissionRule rule = PermissionRule.Parse("Bash(git status)");
        client.Permissions.AddAllow(rule);

        Assert.IsTrue(client.Permissions.RemoveAllow(rule));
        Assert.AreEqual(0, client.Permissions.Allow.Count);
        // RemoveAllow on an absent rule reports false.
        Assert.IsFalse(client.Permissions.RemoveAllow(rule));
    }

    [TestMethod]
    public async Task Permissions_Clear_RemovesEntirePermissionsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(PermissionRule.Parse("Read"));
        client.Permissions.DefaultMode = PermissionDefaultMode.Plan;

        client.Permissions.Clear();

        Assert.AreEqual(0, client.Permissions.Allow.Count);
        Assert.IsNull(client.Permissions.DefaultMode);
    }

    [TestMethod]
    public void PermissionRule_Parse_AcceptsValidShapes()
    {
        Assert.IsTrue(PermissionRule.TryParse("Read", out PermissionRule? _));
        Assert.IsTrue(PermissionRule.TryParse("Bash(git status)", out PermissionRule? _));
        Assert.IsTrue(PermissionRule.TryParse("WebFetch(domain:doc.org)", out PermissionRule? _));
        Assert.IsTrue(PermissionRule.TryParse("PowerShell(Get-Item *)", out PermissionRule? _));
        Assert.IsTrue(PermissionRule.TryParse("mcp__github", out PermissionRule? _));
    }

    [TestMethod]
    public void PermissionRule_Parse_RejectsInvalidShapes()
    {
        Assert.IsFalse(PermissionRule.TryParse("", out PermissionRule? _));
        Assert.IsFalse(PermissionRule.TryParse("BogusTool", out PermissionRule? _));
        Assert.IsFalse(PermissionRule.TryParse("Bash(*)", out PermissionRule? _)); // schema requires non-wildcard content
        Assert.IsFalse(PermissionRule.TryParse("Bash()", out PermissionRule? _));
        Assert.ThrowsException<FormatException>(() => PermissionRule.Parse("BogusTool"));
    }

    // ── Hooks ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Hooks_Add_FlattensInnerHookEntry()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo before"));
        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo also-before"));
        client.Hooks.Add(new HookEvent("PostToolUse", "*", HookCommandType.Prompt, "Now reflect"));

        IReadOnlyList<HookEvent> events = client.Hooks.Events;
        Assert.AreEqual(3, events.Count);
        Assert.IsTrue(events.Any(e => e is { EventName: "PreToolUse", CommandValue: "echo before" }));
        Assert.IsTrue(events.Any(e => e is { EventName: "PreToolUse", CommandValue: "echo also-before" }));
        Assert.IsTrue(events.Any(e => e is { EventName: "PostToolUse", CommandType: HookCommandType.Prompt }));
    }

    [TestMethod]
    public async Task Hooks_Remove_DeletesOnlyMatchingEntry()
    {
        using ClaudeCodeClient client = await OpenAsync();

        HookEvent first = new("PreToolUse", "Bash", HookCommandType.Command, "first");
        HookEvent second = new("PreToolUse", "Bash", HookCommandType.Command, "second");
        client.Hooks.Add(first);
        client.Hooks.Add(second);

        Assert.IsTrue(client.Hooks.Remove(first));

        IReadOnlyList<HookEvent> remaining = client.Hooks.Events;
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("second", remaining[0].CommandValue);
    }

    // ── McpServers ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task McpServers_Set_StdioRoundTripsArgsAndEnv()
    {
        using ClaudeCodeClient client = await OpenAsync();

        McpServer server = new(
            Name: "github",
            Transport: McpTransport.Stdio,
            Command: "npx",
            Args: ["-y", "@modelcontextprotocol/server-github"],
            Env: new Dictionary<string, string> { ["GH_TOKEN"] = "redacted" });

        client.McpServers.Set(server.Name, server);

        McpServer? read = client.McpServers.Get("github");
        Assert.IsNotNull(read);
        Assert.AreEqual(McpTransport.Stdio, read!.Transport);
        Assert.AreEqual("npx", read.Command);
        Assert.IsNotNull(read.Args);
        Assert.AreEqual(2, read.Args!.Count);
        Assert.AreEqual("-y", read.Args[0]);
        Assert.IsNotNull(read.Env);
        Assert.AreEqual("redacted", read.Env!["GH_TOKEN"]);
    }

    [TestMethod]
    public async Task McpServers_Set_StreamableHttpEmitsTypeAndUrl()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.McpServers.Set("remote", new McpServer(
            Name: "remote",
            Transport: McpTransport.StreamableHttp,
            Url: "https://example.com/mcp",
            Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer redacted" }));

        await client.SaveAsync(force: true, CancellationToken.None);
        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        StringAssert.Contains(json, "\"type\": \"streamable-http\"");
        StringAssert.Contains(json, "\"url\": \"https://example.com/mcp\"");
    }

    [TestMethod]
    public async Task McpServers_Remove_DeletesByName()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.McpServers.Set("a", new McpServer("a", McpTransport.Stdio, Command: "echo"));
        client.McpServers.Set("b", new McpServer("b", McpTransport.Stdio, Command: "echo"));

        Assert.IsTrue(client.McpServers.Remove("a"));
        Assert.IsFalse(client.McpServers.Remove("a")); // already gone

        IReadOnlyDictionary<string, McpServer> all = client.McpServers.All;
        Assert.AreEqual(1, all.Count);
        Assert.IsTrue(all.ContainsKey("b"));
    }

    [TestMethod]
    public async Task Permissions_AllowAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(new PermissionRule("Bash(git status)"));
        client.Permissions.DefaultMode = PermissionDefaultMode.AcceptEdits;

        IReadOnlyList<PermissionRule> allowAtUser = client.Permissions.AllowAt(ConfigScope.User);
        IReadOnlyList<PermissionRule> allowAtProject = client.Permissions.AllowAt(ConfigScope.Project);
        PermissionDefaultMode? modeAtUser = client.Permissions.GetDefaultModeAt(ConfigScope.User);
        PermissionDefaultMode? modeAtProject = client.Permissions.GetDefaultModeAt(ConfigScope.Project);

        Assert.AreEqual(1, allowAtUser.Count);
        Assert.AreEqual("Bash(git status)", allowAtUser[0].Value);
        Assert.AreEqual(0, allowAtProject.Count);
        Assert.AreEqual(PermissionDefaultMode.AcceptEdits, modeAtUser);
        Assert.IsNull(modeAtProject);
    }

    [TestMethod]
    public async Task Hooks_EventsAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo hi"));

        IReadOnlyList<HookEvent> atUser = client.Hooks.EventsAt(ConfigScope.User);
        IReadOnlyList<HookEvent> atProject = client.Hooks.EventsAt(ConfigScope.Project);

        Assert.AreEqual(1, atUser.Count);
        Assert.AreEqual("PreToolUse", atUser[0].EventName);
        Assert.AreEqual("Bash", atUser[0].Matcher);
        Assert.AreEqual(HookCommandType.Command, atUser[0].CommandType);
        Assert.AreEqual(0, atProject.Count);
    }

    [TestMethod]
    public async Task McpServers_GetAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.McpServers.Set("local",
            new McpServer("local", McpTransport.Stdio, Command: "node", Args: ["server.js"]));

        IReadOnlyDictionary<string, McpServer> atUser = client.McpServers.GetAt(ConfigScope.User);
        IReadOnlyDictionary<string, McpServer> atProject = client.McpServers.GetAt(ConfigScope.Project);

        Assert.AreEqual(1, atUser.Count);
        Assert.IsTrue(atUser.ContainsKey("local"));
        Assert.AreEqual(McpTransport.Stdio, atUser["local"].Transport);
        Assert.AreEqual(0, atProject.Count);
    }

    // ── Marketplaces ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Marketplaces_Set_ProducesSchemaCanonicalShape()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Marketplaces.Set(new MarketplaceEntry(
            "everything-claude-code",
            MarketplaceSourceKind.Github,
            "anthropic-experimental/everything-claude-code"));

        await client.SaveAsync(force: true, CancellationToken.None);
        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        StringAssert.Contains(json, "\"source\": \"github\"");
        StringAssert.Contains(json, "\"repository\": \"anthropic-experimental/everything-claude-code\"");
    }

    [TestMethod]
    public async Task Marketplaces_Get_ReadsBothSchemaCanonicalAndFlatShapes()
    {
        using ClaudeCodeClient client = await OpenAsync();

        // Schema-canonical: nested source object.
        client.Marketplaces.Set(new MarketplaceEntry("a", MarketplaceSourceKind.Url, "https://example.com/a"));
        MarketplaceEntry? a = client.Marketplaces.Get("a");
        Assert.IsNotNull(a);
        Assert.AreEqual(MarketplaceSourceKind.Url, a!.SourceKind);
        Assert.AreEqual("https://example.com/a", a.SourceValue);
    }

    [TestMethod]
    public async Task Marketplaces_GetAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Marketplaces.Set(new MarketplaceEntry(
            "user-only", MarketplaceSourceKind.Url, "https://u.example/m"));

        IReadOnlyList<MarketplaceEntry> atUser = client.Marketplaces.GetAt(ConfigScope.User);
        IReadOnlyList<MarketplaceEntry> atProject = client.Marketplaces.GetAt(ConfigScope.Project);

        Assert.AreEqual(1, atUser.Count);
        Assert.AreEqual("user-only", atUser[0].Name);
        Assert.AreEqual(MarketplaceSourceKind.Url, atUser[0].SourceKind);
        Assert.AreEqual(0, atProject.Count);
    }

    // ── EnabledPlugins ────────────────────────────────────────────────────

    [TestMethod]
    public async Task EnabledPlugins_Set_StoresPluginRefAndBool()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Plugins.Set(new EnabledPlugin("everything-claude-code/code-review", Enabled: true));
        client.Plugins.Set(new EnabledPlugin("anthropic/safety", Enabled: false));

        IReadOnlyList<EnabledPlugin> all = client.Plugins.All;
        Assert.AreEqual(2, all.Count);
        Assert.IsTrue(all.Any(p => p.PluginRef == "everything-claude-code/code-review" && p.Enabled));
        Assert.IsTrue(all.Any(p => p.PluginRef == "anthropic/safety" && !p.Enabled));
    }

    [TestMethod]
    public async Task EnabledPlugins_Remove_DeletesByRef()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Plugins.Set(new EnabledPlugin("a/b", true));
        client.Plugins.Set(new EnabledPlugin("c/d", false));

        Assert.IsTrue(client.Plugins.Remove("a/b"));
        Assert.AreEqual(1, client.Plugins.All.Count);
    }

    [TestMethod]
    public async Task EnabledPlugins_GetAt_ReadsScopeOnlyValues()
    {
        // Set a value at the User scope (the SDK default) and confirm
        // GetAt(User) sees it while GetAt(Project) does not. This is the
        // contract the GUI editor migration relies on:
        // the editor binds to one scope's view at a time, NOT the merged
        // effective view.
        using ClaudeCodeClient client = await OpenAsync();
        client.Plugins.Set(new EnabledPlugin("only-at-user/x", Enabled: true));

        IReadOnlyList<EnabledPlugin> atUser = client.Plugins.GetAt(ConfigScope.User);
        IReadOnlyList<EnabledPlugin> atProject = client.Plugins.GetAt(ConfigScope.Project);

        Assert.AreEqual(1, atUser.Count);
        Assert.AreEqual("only-at-user/x", atUser[0].PluginRef);
        Assert.IsTrue(atUser[0].Enabled);

        // The Project document was never loaded in this test (no project
        // root) — GetAt should return an empty list rather than falling
        // back to effective.
        Assert.AreEqual(0, atProject.Count);
    }

    [TestMethod]
    public async Task EnabledPlugins_Set_WithComponents_RoundTripsAsArray()
    {
        // The schema permits an array-of-strings value (enable specific plugin
        // components). The accessor must store it as a JSON array and surface it via
        // Components, not collapse it to a bool.
        using ClaudeCodeClient client = await OpenAsync();

        client.Plugins.Set(new EnabledPlugin("formatter/tools", Enabled: true, Components: ["alpha", "beta"]));

        EnabledPlugin? got = client.Plugins.Get("formatter/tools");
        Assert.IsNotNull(got);
        Assert.IsTrue(got!.Enabled);
        Assert.IsNotNull(got.Components);
        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, got.Components!.ToArray());

        // A plain-bool entry still reports null Components.
        client.Plugins.Set(new EnabledPlugin("plain/bool", Enabled: true));
        Assert.IsNull(client.Plugins.Get("plain/bool")!.Components);
    }

    [TestMethod]
    public async Task EnabledPlugins_All_SurfacesArrayValuedPlugins()
    {
        // Regression: the accessor formerly OMITTED non-bool values entirely, making
        // array-valued plugins invisible to headless consumers (and droppable by any
        // consumer that rewrote the whole block).
        using ClaudeCodeClient client = await OpenAsync();
        client.Plugins.Set(new EnabledPlugin("with/components", Enabled: true, Components: ["x"]));
        client.Plugins.Set(new EnabledPlugin("plain/flag", Enabled: false));

        IReadOnlyList<EnabledPlugin> all = client.Plugins.All;
        Assert.AreEqual(2, all.Count, "Both the array-valued and the bool-valued plugin must surface.");
        Assert.IsTrue(all.Any(p => p.PluginRef == "with/components" && p.Components is { Count: 1 }));
        Assert.IsTrue(all.Any(p => p.PluginRef == "plain/flag" && !p.Enabled && p.Components is null));
    }
}