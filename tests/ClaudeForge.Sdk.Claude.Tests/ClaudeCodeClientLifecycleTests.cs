using System.Reflection;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.AgentForge.Sdk;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

/// <summary>
/// Integration tests for the SDK lifecycle methods (Open / Reload / Save) and
/// the generic escape hatch (GetEffective / SetValue / RemoveValue).
/// </summary>
/// <remarks>
/// <para>
/// Each test creates a fresh temp directory, points
/// <see cref="PlatformPaths.TestUserProfileOverride"/> at it, and operates
/// against real on-disk files. Cleanup runs in <see cref="TestCleanup"/> so
/// state never leaks across tests.
/// </para>
/// <para>
/// These tests exercise the production code paths end-to-end (real
/// <c>ConfigFileLoader</c>, real atomic temp+rename writes), giving the SDK's
/// 4.3.3 contract real-world coverage rather than a mocked-out approximation.
/// </para>
/// </remarks>
public class ClaudeCodeClientLifecycleTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public ClaudeCodeClientLifecycleTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-sdk-test-" + Guid.NewGuid().ToString("N"));
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
            /* best effort — Windows file-locking can hold us up */
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // ── OpenAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAsync_LoadsUserScopeWorkspace_FromEmptyDisk()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);

        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Empty disk → no documents are dirty, so HasUnsavedChanges is false.
        Assert.False(client.HasUnsavedChanges,
            "A freshly-opened client over empty disk must report HasUnsavedChanges=false.");
    }

    [Fact]
    public async Task PublicMethods_BeforeOpen_ThrowInvalidOperation()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);

        MessageAssert.Throws<InvalidOperationException>(
            () => client.GetEffective<string>("model"),
            "GetEffective before OpenAsync must fail loudly.");

        MessageAssert.Throws<InvalidOperationException>(
            () => client.SetValue("model", "opus"),
            "SetValue before OpenAsync must fail loudly.");
    }

    // ── SetValue / GetEffective round-trip ────────────────────────────────

    [Fact]
    public async Task SetValue_GetEffective_RoundTripsStringAtUserScope()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "claude-opus-4");

        string? effective = client.GetEffective<string>("model");
        Assert.Equal("claude-opus-4", effective);
        Assert.True(client.HasUnsavedChanges, "SetValue must mark the workspace dirty.");
    }

    [Fact]
    public async Task SetValue_NestedPath_StoresUnderTopLevelObject()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Dotted path — the SDK reads the existing top-level "permissions" object,
        // mutates the nested "defaultMode" inside, and writes the whole object back.
        client.SetValue("permissions.defaultMode", "auto");

        string? nested = client.GetEffective<string>("permissions.defaultMode");
        Assert.Equal("auto", nested);

        // Reading the parent object via GetEffective returns the JsonObject form.
        JsonObject? parent = client.GetEffective<JsonObject>("permissions");
        Assert.NotNull(parent);
        Assert.Equal("auto", parent!["defaultMode"]?.GetValue<string>());
    }

    [Fact]
    public async Task RemoveValue_TopLevel_ClearsAndMarksClean()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "opus");
        Assert.Equal("opus", client.GetEffective<string>("model"));

        client.RemoveValue("model", ConfigScope.User);

        Assert.Null(client.GetEffective<string>("model"));
    }

    [Fact]
    public async Task RemoveValue_NestedPath_RemovesOnlyTheNestedKey()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("permissions.defaultMode", "auto");
        client.SetValue("permissions.allow", new JsonArray("Read"));

        client.RemoveValue("permissions.defaultMode", ConfigScope.User);

        // defaultMode gone; allow survives.
        Assert.Null(client.GetEffective<string>("permissions.defaultMode"));
        JsonArray? allow = client.GetEffective<JsonArray>("permissions.allow");
        Assert.NotNull(allow);
        Assert.Single(allow!);
    }

    // ── SaveAsync / ReloadAsync ───────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_PersistsToDisk_AndClearsUnsavedFlag()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "claude-sonnet-4");
        Assert.True(client.HasUnsavedChanges);

        await client.SaveAsync(force: true, ct: CancellationToken.None);

        Assert.False(client.HasUnsavedChanges,
            "After SaveAsync the workspace must report no unsaved changes.");

        // Verify on disk.
        string settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Assert.True(File.Exists(settingsPath),
            $"settings.json should have been written to {settingsPath}.");
        string json = await File.ReadAllTextAsync(settingsPath);
        OrdinalAssert.Contains("claude-sonnet-4", json);
    }

    [Fact]
    public async Task ReloadAsync_DiscardsUnsavedInMemoryEdits()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Persist a baseline.
        client.SetValue("model", "baseline");
        await client.SaveAsync(force: true, ct: CancellationToken.None);

        // Make an in-memory edit.
        client.SetValue("model", "uncommitted-edit");
        Assert.Equal("uncommitted-edit", client.GetEffective<string>("model"));
        Assert.True(client.HasUnsavedChanges);

        // Reload from disk discards the edit.
        await client.ReloadAsync(CancellationToken.None);

        MessageAssert.Equal("baseline", client.GetEffective<string>("model"),
            "Reload must replace in-memory state with the on-disk baseline.");
        Assert.False(client.HasUnsavedChanges);
    }

    // ── Changed event ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetValue_RaisesChangedEvent_WithMutationKindAndPath()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<ClientChangedEventArgs> captured = new();
        client.Changed += (_, e) => captured.Add(e);

        client.SetValue("model", "opus");

        Assert.Single(captured);
        Assert.Equal(ClientChangeKind.Mutation, captured[0].Kind);
        Assert.Equal("model", captured[0].Path);
    }

    [Fact]
    public async Task SaveAsync_RaisesSavedKind_WithNullPath()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "opus");

        List<ClientChangedEventArgs> savedEvents = new();
        client.Changed += (_, e) =>
        {
            if (e.Kind == ClientChangeKind.Saved)
            {
                savedEvents.Add(e);
            }
        };

        await client.SaveAsync(force: true, CancellationToken.None);

        Assert.Single(savedEvents);
        Assert.Null(savedEvents[0].Path);
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_DoubleCall_IsSafe()
    {
        ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        client.Dispose();
        client.Dispose(); // must not throw
    }

    [Fact]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposed()
    {
        ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.GetEffective<string>("model"));
        Assert.Throws<ObjectDisposedException>(() => client.SetValue("model", "opus"));
        Assert.Throws<ObjectDisposedException>(() => _ = client.HasUnsavedChanges);
    }

    // ── DefaultScope ──────────────────────────────────────────────────────

    [Fact]
    public async Task SetValue_WithoutExplicitScope_TargetsDefaultScope()
    {
        // Construct with a non-default DefaultScope so we can distinguish from
        // the User-scope default.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty, defaultScope: ConfigScope.User);
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        client.SetValue("model", "opus");

        Assert.Equal(ConfigScope.User, client.DefaultScope);
        Assert.Equal("opus", client.GetEffective<string>("model"));
    }

    // ── EditableScopes (4.3.7 step 7) ────────────────────────────────────

    [Fact]
    public async Task ChangedEvent_FiresOnSdkInitiatedWrite_WithPath()
    {
        // SDK SetValue suppresses the workspace forwarder while it does the
        // write, then explicitly raises Changed with the dotted path. The
        // consumer sees ONE event with the path populated.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        List<ClientChangedEventArgs> events = new();
        client.Changed += (_, e) =>
        {
            if (e.Kind == ClientChangeKind.Mutation)
            {
                events.Add(e);
            }
        };

        client.SetValue("model", "opus");

        MessageAssert.Equal(1, events.Count, "SDK SetValue must fire exactly one Mutation event.");
        Assert.Equal("model", events[0].Path);
    }

    [Fact]
    public async Task ChangedEvent_FiresOnDirectWorkspaceWrite_WithoutPath()
    {
        // Forwarder regression: when the underlying workspace is mutated
        // outside the SDK's SetValue/RemoveValue path (e.g. the GUI editor
        // live-write loop calling _workspace.SetValue directly), the SDK
        // still surfaces the change to its consumers via the workspace.Changed
        // forwarder. Path is null because the workspace event doesn't carry
        // path info.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        List<ClientChangedEventArgs> events = new();
        client.Changed += (_, e) =>
        {
            if (e.Kind == ClientChangeKind.Mutation)
            {
                events.Add(e);
            }
        };

        // Reach the workspace via reflection — the SDK doesn't expose it
        // publicly. This test is the only thing in the SDK test project that
        // pokes at internals; production code paths use the public surface.
        FieldInfo? workspaceField = typeof(AgentConfigClientCore)
            .GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(workspaceField);
        SettingsWorkspace workspace = (SettingsWorkspace)workspaceField!.GetValue(client)!;

        workspace.SetValue("model", JsonValue.Create("opus"), ConfigScope.User);

        MessageAssert.Equal(1, events.Count,
            "Direct workspace.SetValue must propagate via the SDK's Changed forwarder.");
        MessageAssert.Null(events[0].Path,
            "Forwarded events have no path info — the workspace.Changed event doesn't carry it.");
    }

    [Fact]
    public async Task EditableScopes_NoProjectRoot_ReturnsUserOnly()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        Assert.Single(scopes);
        Assert.Equal(ConfigScope.User, scopes[0]);
    }

    [Fact]
    public async Task EditableScopes_BeforeOpenAsync_FallsBackToUser()
    {
        // The accessor is callable before OpenAsync — covers the brief
        // window during GUI startup where the binding might read scopes
        // before the workspace finishes loading. Always returns at least
        // User so the scope ComboBox has a sensible default.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        Assert.Single(scopes);
        Assert.Equal(ConfigScope.User, scopes[0]);
    }

    [Fact]
    public async Task EditableScopes_WithProjectRoot_IncludesProjectAndLocal()
    {
        // With a projectRoot supplied, the workspace loads Project + Local
        // documents in addition to User. EditableScopes orders widest →
        // narrowest (User → Project → Local).
        string projectRoot = Path.Combine(_tempDir, "myproj");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".claude", "settings.json"), "{}");
        await File.WriteAllTextAsync(
            Path.Combine(projectRoot, ".claude", "settings.local.json"), "{}");

        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot, CancellationToken.None);

        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        // Managed is always excluded (read-only). Order matches
        // ConfigScope's int values: Local=1, Project=2, User=3.
        Assert.Equal(
            new[] { ConfigScope.Local, ConfigScope.Project, ConfigScope.User },
            scopes.ToArray());
    }
}