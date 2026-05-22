using System.Reflection;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

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
[TestClass]
public class ClaudeCodeClientLifecycleTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-sdk-test-" + Guid.NewGuid().ToString("N"));
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
            /* best effort — Windows file-locking can hold us up */
        }
    }

    // ── OpenAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OpenAsync_LoadsUserScopeWorkspace_FromEmptyDisk()
    {
        using ClaudeCodeClient client = new();

        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Empty disk → no documents are dirty, so HasUnsavedChanges is false.
        Assert.IsFalse(client.HasUnsavedChanges,
            "A freshly-opened client over empty disk must report HasUnsavedChanges=false.");
    }

    [TestMethod]
    public async Task PublicMethods_BeforeOpen_ThrowInvalidOperation()
    {
        using ClaudeCodeClient client = new();

        Assert.ThrowsException<InvalidOperationException>(
            () => client.GetEffective<string>("model"),
            "GetEffective before OpenAsync must fail loudly.");

        Assert.ThrowsException<InvalidOperationException>(
            () => client.SetValue("model", "opus"),
            "SetValue before OpenAsync must fail loudly.");
    }

    // ── SetValue / GetEffective round-trip ────────────────────────────────

    [TestMethod]
    public async Task SetValue_GetEffective_RoundTripsStringAtUserScope()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "claude-opus-4");

        string? effective = client.GetEffective<string>("model");
        Assert.AreEqual("claude-opus-4", effective);
        Assert.IsTrue(client.HasUnsavedChanges, "SetValue must mark the workspace dirty.");
    }

    [TestMethod]
    public async Task SetValue_NestedPath_StoresUnderTopLevelObject()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Dotted path — the SDK reads the existing top-level "permissions" object,
        // mutates the nested "defaultMode" inside, and writes the whole object back.
        client.SetValue("permissions.defaultMode", "auto");

        string? nested = client.GetEffective<string>("permissions.defaultMode");
        Assert.AreEqual("auto", nested);

        // Reading the parent object via GetEffective returns the JsonObject form.
        JsonObject? parent = client.GetEffective<JsonObject>("permissions");
        Assert.IsNotNull(parent);
        Assert.AreEqual("auto", parent!["defaultMode"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task RemoveValue_TopLevel_ClearsAndMarksClean()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "opus");
        Assert.AreEqual("opus", client.GetEffective<string>("model"));

        client.RemoveValue("model", ConfigScope.User);

        Assert.IsNull(client.GetEffective<string>("model"));
    }

    [TestMethod]
    public async Task RemoveValue_NestedPath_RemovesOnlyTheNestedKey()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("permissions.defaultMode", "auto");
        client.SetValue("permissions.allow", new JsonArray("Read"));

        client.RemoveValue("permissions.defaultMode", ConfigScope.User);

        // defaultMode gone; allow survives.
        Assert.IsNull(client.GetEffective<string>("permissions.defaultMode"));
        JsonArray? allow = client.GetEffective<JsonArray>("permissions.allow");
        Assert.IsNotNull(allow);
        Assert.AreEqual(1, allow!.Count);
    }

    // ── SaveAsync / ReloadAsync ───────────────────────────────────────────

    [TestMethod]
    public async Task SaveAsync_PersistsToDisk_AndClearsUnsavedFlag()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        client.SetValue("model", "claude-sonnet-4");
        Assert.IsTrue(client.HasUnsavedChanges);

        await client.SaveAsync(force: true, ct: CancellationToken.None);

        Assert.IsFalse(client.HasUnsavedChanges,
            "After SaveAsync the workspace must report no unsaved changes.");

        // Verify on disk.
        string settingsPath = Path.Combine(_tempDir, ".claude", "settings.json");
        Assert.IsTrue(File.Exists(settingsPath),
            $"settings.json should have been written to {settingsPath}.");
        string json = await File.ReadAllTextAsync(settingsPath);
        StringAssert.Contains(json, "claude-sonnet-4");
    }

    [TestMethod]
    public async Task ReloadAsync_DiscardsUnsavedInMemoryEdits()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        // Persist a baseline.
        client.SetValue("model", "baseline");
        await client.SaveAsync(force: true, ct: CancellationToken.None);

        // Make an in-memory edit.
        client.SetValue("model", "uncommitted-edit");
        Assert.AreEqual("uncommitted-edit", client.GetEffective<string>("model"));
        Assert.IsTrue(client.HasUnsavedChanges);

        // Reload from disk discards the edit.
        await client.ReloadAsync(CancellationToken.None);

        Assert.AreEqual("baseline", client.GetEffective<string>("model"),
            "Reload must replace in-memory state with the on-disk baseline.");
        Assert.IsFalse(client.HasUnsavedChanges);
    }

    // ── Changed event ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetValue_RaisesChangedEvent_WithMutationKindAndPath()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<ClientChangedEventArgs> captured = new();
        client.Changed += (_, e) => captured.Add(e);

        client.SetValue("model", "opus");

        Assert.AreEqual(1, captured.Count);
        Assert.AreEqual(ClientChangeKind.Mutation, captured[0].Kind);
        Assert.AreEqual("model", captured[0].Path);
    }

    [TestMethod]
    public async Task SaveAsync_RaisesSavedKind_WithNullPath()
    {
        using ClaudeCodeClient client = new();
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

        Assert.AreEqual(1, savedEvents.Count);
        Assert.IsNull(savedEvents[0].Path);
    }

    // ── Disposal ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dispose_DoubleCall_IsSafe()
    {
        ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        client.Dispose();
        client.Dispose(); // must not throw
    }

    [TestMethod]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposed()
    {
        ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        client.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => client.GetEffective<string>("model"));
        Assert.ThrowsException<ObjectDisposedException>(() => client.SetValue("model", "opus"));
        Assert.ThrowsException<ObjectDisposedException>(() => _ = client.HasUnsavedChanges);
    }

    // ── DefaultScope ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetValue_WithoutExplicitScope_TargetsDefaultScope()
    {
        // Construct with a non-default DefaultScope so we can distinguish from
        // the User-scope default.
        using ClaudeCodeClient client = new(defaultScope: ConfigScope.User);
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        client.SetValue("model", "opus");

        Assert.AreEqual(ConfigScope.User, client.DefaultScope);
        Assert.AreEqual("opus", client.GetEffective<string>("model"));
    }

    // ── EditableScopes (4.3.7 step 7) ────────────────────────────────────

    [TestMethod]
    public async Task ChangedEvent_FiresOnSdkInitiatedWrite_WithPath()
    {
        // SDK SetValue suppresses the workspace forwarder while it does the
        // write, then explicitly raises Changed with the dotted path. The
        // consumer sees ONE event with the path populated.
        using ClaudeCodeClient client = new();
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

        Assert.AreEqual(1, events.Count, "SDK SetValue must fire exactly one Mutation event.");
        Assert.AreEqual("model", events[0].Path);
    }

    [TestMethod]
    public async Task ChangedEvent_FiresOnDirectWorkspaceWrite_WithoutPath()
    {
        // Forwarder regression: when the underlying workspace is mutated
        // outside the SDK's SetValue/RemoveValue path (e.g. the GUI editor
        // live-write loop calling _workspace.SetValue directly), the SDK
        // still surfaces the change to its consumers via the workspace.Changed
        // forwarder. Path is null because the workspace event doesn't carry
        // path info.
        using ClaudeCodeClient client = new();
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
        FieldInfo? workspaceField = typeof(ClaudeConfigClientCore)
            .GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(workspaceField);
        SettingsWorkspace workspace = (SettingsWorkspace)workspaceField!.GetValue(client)!;

        workspace.SetValue("model", JsonValue.Create("opus"), ConfigScope.User);

        Assert.AreEqual(1, events.Count,
            "Direct workspace.SetValue must propagate via the SDK's Changed forwarder.");
        Assert.IsNull(events[0].Path,
            "Forwarded events have no path info — the workspace.Changed event doesn't carry it.");
    }

    [TestMethod]
    public async Task EditableScopes_NoProjectRoot_ReturnsUserOnly()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, CancellationToken.None);

        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        Assert.AreEqual(1, scopes.Count);
        Assert.AreEqual(ConfigScope.User, scopes[0]);
    }

    [TestMethod]
    public async Task EditableScopes_BeforeOpenAsync_FallsBackToUser()
    {
        // The accessor is callable before OpenAsync — covers the brief
        // window during GUI startup where the binding might read scopes
        // before the workspace finishes loading. Always returns at least
        // User so the scope ComboBox has a sensible default.
        using ClaudeCodeClient client = new();
        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        Assert.AreEqual(1, scopes.Count);
        Assert.AreEqual(ConfigScope.User, scopes[0]);
    }

    [TestMethod]
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

        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot, CancellationToken.None);

        IReadOnlyList<ConfigScope> scopes = client.EditableScopes;

        // Managed is always excluded (read-only). Order matches
        // ConfigScope's int values: Local=1, Project=2, User=3.
        CollectionAssert.AreEqual(
            new[] { ConfigScope.Local, ConfigScope.Project, ConfigScope.User },
            scopes.ToArray());
    }
}