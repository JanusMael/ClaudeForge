using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Hooks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Marketplaces;
using Bennewitz.Ninja.AgentForge.Sdk.McpServers;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Plugins;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

/// <summary>
/// End-to-end tests for the five strongly-typed accessors. Each test exercises
/// the public read/write surface against a real on-disk workspace and verifies
/// the resulting JSON shape round-trips through Save/Reload.
/// </summary>
public class AccessorsTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public AccessorsTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-sdk-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = _tempDir;
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private async Task<ClaudeCodeClient> OpenAsync()
    {
        ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    // ── Permissions ──────────────────────────────────────────────────────

    [Fact]
    public async Task Permissions_DefaultMode_RoundTripsViaCamelCase()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.DefaultMode = PermissionDefaultMode.AcceptEdits;

        Assert.Equal(PermissionDefaultMode.AcceptEdits, client.Permissions.DefaultMode);

        // Verify the on-disk JSON uses the documented camelCase string.
        await client.SaveAsync(force: true, CancellationToken.None);
        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        OrdinalAssert.Contains("\"defaultMode\": \"acceptEdits\"", json);
    }

    [Fact]
    public async Task Permissions_AddAllow_AppendsRule_AndDedupes()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(PermissionRule.Parse("Bash(git status)"));
        client.Permissions.AddAllow(PermissionRule.Parse("Read"));
        client.Permissions.AddAllow(PermissionRule.Parse("Bash(git status)")); // duplicate — must be a no-op

        IReadOnlyList<PermissionRule> allow = client.Permissions.Allow;
        Assert.Equal(2, allow.Count);
        Assert.Contains(allow, r => r.Value == "Bash(git status)");
        Assert.Contains(allow, r => r.Value == "Read");
    }

    [Fact]
    public async Task Permissions_RemoveAllow_DeletesRule_AndCleansEmptyArray()
    {
        using ClaudeCodeClient client = await OpenAsync();
        PermissionRule rule = PermissionRule.Parse("Bash(git status)");
        client.Permissions.AddAllow(rule);

        Assert.True(client.Permissions.RemoveAllow(rule));
        Assert.Empty(client.Permissions.Allow);
        // RemoveAllow on an absent rule reports false.
        Assert.False(client.Permissions.RemoveAllow(rule));
    }

    [Fact]
    public async Task Permissions_Clear_RemovesEntirePermissionsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(PermissionRule.Parse("Read"));
        client.Permissions.DefaultMode = PermissionDefaultMode.Plan;

        client.Permissions.Clear();

        Assert.Empty(client.Permissions.Allow);
        Assert.Null(client.Permissions.DefaultMode);
    }

    [Fact]
    public void PermissionRule_Parse_AcceptsValidShapes()
    {
        Assert.True(PermissionRule.TryParse("Read", out PermissionRule? _));
        Assert.True(PermissionRule.TryParse("Bash(git status)", out PermissionRule? _));
        Assert.True(PermissionRule.TryParse("WebFetch(domain:doc.org)", out PermissionRule? _));
        Assert.True(PermissionRule.TryParse("PowerShell(Get-Item *)", out PermissionRule? _));
        Assert.True(PermissionRule.TryParse("mcp__github", out PermissionRule? _));
    }

    [Fact]
    public void PermissionRule_Parse_RejectsInvalidShapes()
    {
        Assert.False(PermissionRule.TryParse("", out PermissionRule? _));
        Assert.False(PermissionRule.TryParse("BogusTool", out PermissionRule? _));
        Assert.False(PermissionRule.TryParse("Bash(*)", out PermissionRule? _)); // schema requires non-wildcard content
        Assert.False(PermissionRule.TryParse("Bash()", out PermissionRule? _));
        Assert.Throws<FormatException>(() => PermissionRule.Parse("BogusTool"));
    }

    // ── Hooks ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hooks_Add_FlattensInnerHookEntry()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo before"));
        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo also-before"));
        client.Hooks.Add(new HookEvent("PostToolUse", "*", HookCommandType.Prompt, "Now reflect"));

        IReadOnlyList<HookEvent> events = client.Hooks.Events;
        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e is { EventName: "PreToolUse", CommandValue: "echo before" });
        Assert.Contains(events, e => e is { EventName: "PreToolUse", CommandValue: "echo also-before" });
        Assert.Contains(events, e => e is { EventName: "PostToolUse", CommandType: HookCommandType.Prompt });
    }

    [Fact]
    public async Task Hooks_Remove_DeletesOnlyMatchingEntry()
    {
        using ClaudeCodeClient client = await OpenAsync();

        HookEvent first = new("PreToolUse", "Bash", HookCommandType.Command, "first");
        HookEvent second = new("PreToolUse", "Bash", HookCommandType.Command, "second");
        client.Hooks.Add(first);
        client.Hooks.Add(second);

        Assert.True(client.Hooks.Remove(first));

        IReadOnlyList<HookEvent> remaining = client.Hooks.Events;
        Assert.Single(remaining);
        Assert.Equal("second", remaining[0].CommandValue);
    }

    // ── McpServers ────────────────────────────────────────────────────────

    [Fact]
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
        Assert.NotNull(read);
        Assert.Equal(McpTransport.Stdio, read!.Transport);
        Assert.Equal("npx", read.Command);
        Assert.NotNull(read.Args);
        Assert.Equal(2, read.Args!.Count);
        Assert.Equal("-y", read.Args[0]);
        Assert.NotNull(read.Env);
        Assert.Equal("redacted", read.Env!["GH_TOKEN"]);
    }

    [Fact]
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
        OrdinalAssert.Contains("\"type\": \"streamable-http\"", json);
        OrdinalAssert.Contains("\"url\": \"https://example.com/mcp\"", json);
    }

    [Fact]
    public async Task McpServers_Remove_DeletesByName()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.McpServers.Set("a", new McpServer("a", McpTransport.Stdio, Command: "echo"));
        client.McpServers.Set("b", new McpServer("b", McpTransport.Stdio, Command: "echo"));

        Assert.True(client.McpServers.Remove("a"));
        Assert.False(client.McpServers.Remove("a")); // already gone

        IReadOnlyDictionary<string, McpServer> all = client.McpServers.All;
        Assert.Single(all);
        Assert.True(all.ContainsKey("b"));
    }

    [Fact]
    public async Task Permissions_AllowAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Permissions.AddAllow(new PermissionRule("Bash(git status)"));
        client.Permissions.DefaultMode = PermissionDefaultMode.AcceptEdits;

        IReadOnlyList<PermissionRule> allowAtUser = client.Permissions.AllowAt(ConfigScope.User);
        IReadOnlyList<PermissionRule> allowAtProject = client.Permissions.AllowAt(ConfigScope.Project);
        PermissionDefaultMode? modeAtUser = client.Permissions.GetDefaultModeAt(ConfigScope.User);
        PermissionDefaultMode? modeAtProject = client.Permissions.GetDefaultModeAt(ConfigScope.Project);

        Assert.Single(allowAtUser);
        Assert.Equal("Bash(git status)", allowAtUser[0].Value);
        Assert.Empty(allowAtProject);
        Assert.Equal(PermissionDefaultMode.AcceptEdits, modeAtUser);
        Assert.Null(modeAtProject);
    }

    [Fact]
    public async Task Hooks_EventsAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Hooks.Add(new HookEvent("PreToolUse", "Bash", HookCommandType.Command, "echo hi"));

        IReadOnlyList<HookEvent> atUser = client.Hooks.EventsAt(ConfigScope.User);
        IReadOnlyList<HookEvent> atProject = client.Hooks.EventsAt(ConfigScope.Project);

        Assert.Single(atUser);
        Assert.Equal("PreToolUse", atUser[0].EventName);
        Assert.Equal("Bash", atUser[0].Matcher);
        Assert.Equal(HookCommandType.Command, atUser[0].CommandType);
        Assert.Empty(atProject);
    }

    [Fact]
    public async Task McpServers_GetAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.McpServers.Set("local",
            new McpServer("local", McpTransport.Stdio, Command: "node", Args: ["server.js"]));

        IReadOnlyDictionary<string, McpServer> atUser = client.McpServers.GetAt(ConfigScope.User);
        IReadOnlyDictionary<string, McpServer> atProject = client.McpServers.GetAt(ConfigScope.Project);

        Assert.Single(atUser);
        Assert.True(atUser.ContainsKey("local"));
        Assert.Equal(McpTransport.Stdio, atUser["local"].Transport);
        Assert.Empty(atProject);
    }

    // ── Marketplaces ──────────────────────────────────────────────────────

    [Fact]
    public async Task Marketplaces_Set_ProducesSchemaCanonicalShape()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Marketplaces.Set(new MarketplaceEntry(
            "everything-claude-code",
            MarketplaceSourceKind.Github,
            "anthropic-experimental/everything-claude-code"));

        await client.SaveAsync(force: true, CancellationToken.None);
        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        OrdinalAssert.Contains("\"source\": \"github\"", json);
        OrdinalAssert.Contains("\"repository\": \"anthropic-experimental/everything-claude-code\"", json);
    }

    [Fact]
    public async Task Marketplaces_Get_ReadsBothSchemaCanonicalAndFlatShapes()
    {
        using ClaudeCodeClient client = await OpenAsync();

        // Schema-canonical: nested source object.
        client.Marketplaces.Set(new MarketplaceEntry("a", MarketplaceSourceKind.Url, "https://example.com/a"));
        MarketplaceEntry? a = client.Marketplaces.Get("a");
        Assert.NotNull(a);
        Assert.Equal(MarketplaceSourceKind.Url, a!.SourceKind);
        Assert.Equal("https://example.com/a", a.SourceValue);
    }

    [Fact]
    public async Task Marketplaces_GetAt_ReadsScopeOnlyValues()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Marketplaces.Set(new MarketplaceEntry(
            "user-only", MarketplaceSourceKind.Url, "https://u.example/m"));

        IReadOnlyList<MarketplaceEntry> atUser = client.Marketplaces.GetAt(ConfigScope.User);
        IReadOnlyList<MarketplaceEntry> atProject = client.Marketplaces.GetAt(ConfigScope.Project);

        Assert.Single(atUser);
        Assert.Equal("user-only", atUser[0].Name);
        Assert.Equal(MarketplaceSourceKind.Url, atUser[0].SourceKind);
        Assert.Empty(atProject);
    }

    // ── EnabledPlugins ────────────────────────────────────────────────────

    [Fact]
    public async Task EnabledPlugins_Set_StoresPluginRefAndBool()
    {
        using ClaudeCodeClient client = await OpenAsync();

        client.Plugins.Set(new EnabledPlugin("everything-claude-code/code-review", Enabled: true));
        client.Plugins.Set(new EnabledPlugin("anthropic/safety", Enabled: false));

        IReadOnlyList<EnabledPlugin> all = client.Plugins.All;
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.PluginRef == "everything-claude-code/code-review" && p.Enabled);
        Assert.Contains(all, p => p.PluginRef == "anthropic/safety" && !p.Enabled);
    }

    [Fact]
    public async Task EnabledPlugins_Remove_DeletesByRef()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Plugins.Set(new EnabledPlugin("a/b", true));
        client.Plugins.Set(new EnabledPlugin("c/d", false));

        Assert.True(client.Plugins.Remove("a/b"));
        Assert.Single(client.Plugins.All);
    }

    [Fact]
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

        Assert.Single(atUser);
        Assert.Equal("only-at-user/x", atUser[0].PluginRef);
        Assert.True(atUser[0].Enabled);

        // The Project document was never loaded in this test (no project
        // root) — GetAt should return an empty list rather than falling
        // back to effective.
        Assert.Empty(atProject);
    }

    [Fact]
    public async Task EnabledPlugins_Set_WithComponents_RoundTripsAsArray()
    {
        // The schema permits an array-of-strings value (enable specific plugin
        // components). The accessor must store it as a JSON array and surface it via
        // Components, not collapse it to a bool.
        using ClaudeCodeClient client = await OpenAsync();

        client.Plugins.Set(new EnabledPlugin("formatter/tools", Enabled: true, Components: ["alpha", "beta"]));

        EnabledPlugin? got = client.Plugins.Get("formatter/tools");
        Assert.NotNull(got);
        Assert.True(got!.Enabled);
        Assert.NotNull(got.Components);
        Assert.Equal(new[] { "alpha", "beta" }, got.Components!.ToArray());

        // A plain-bool entry still reports null Components.
        client.Plugins.Set(new EnabledPlugin("plain/bool", Enabled: true));
        Assert.Null(client.Plugins.Get("plain/bool")!.Components);
    }

    [Fact]
    public async Task EnabledPlugins_All_SurfacesArrayValuedPlugins()
    {
        // Regression: the accessor formerly OMITTED non-bool values entirely, making
        // array-valued plugins invisible to headless consumers (and droppable by any
        // consumer that rewrote the whole block).
        using ClaudeCodeClient client = await OpenAsync();
        client.Plugins.Set(new EnabledPlugin("with/components", Enabled: true, Components: ["x"]));
        client.Plugins.Set(new EnabledPlugin("plain/flag", Enabled: false));

        IReadOnlyList<EnabledPlugin> all = client.Plugins.All;
        MessageAssert.Equal(2, all.Count, "Both the array-valued and the bool-valued plugin must surface.");
        Assert.Contains(all, p => p.PluginRef == "with/components" && p.Components is { Count: 1 });
        Assert.Contains(all, p => p.PluginRef == "plain/flag" && !p.Enabled && p.Components is null);
    }
}