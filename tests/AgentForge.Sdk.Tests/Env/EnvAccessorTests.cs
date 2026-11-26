using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Env;

/// <summary>
/// Accessor for the settings.json <c>env</c> map.
/// Pins the IEnvAccessor contract end-to-end against a real on-disk
/// workspace: the generic dictionary surface and null = remove semantics.
/// </summary>
/// <remarks>
/// ⓘ Claude Code's typed properties left with their keys in plans/00008
/// step 10; their tests are the class of the same name in
/// <c>ClaudeForge.Sdk.Claude.Tests</c>.
/// </remarks>
public sealed class EnvAccessorTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public EnvAccessorTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-env-acc-" + Guid.NewGuid().ToString("N"));
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

    private async Task<TestConfigClient> OpenAsync()
    {
        TestConfigClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    // ── Generic dictionary surface ────────────────────────────────────

    [Fact]
    public async Task Set_AndGet_RoundTripsArbitraryKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "custom-value");

        Assert.Equal("custom-value", client.Env.Get("CUSTOM_KEY"));
    }

    [Fact]
    public async Task Set_NullValue_RemovesKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "first");
        Assert.Equal("first", client.Env.Get("CUSTOM_KEY"));

        client.Env.Set("CUSTOM_KEY", null);

        MessageAssert.Null(client.Env.Get("CUSTOM_KEY"),
            "Setting null must remove the key from the env map.");
    }

    [Fact]
    public async Task Set_EmptyString_RemovesKey()
    {
        // Empty string == "remove" mirrors the IPermissionsAccessor null
        // semantics applied to a string-typed surface.  The runtime would
        // never act on an empty env value so making the SDK collapse it
        // to "remove" is the correct behaviour.
        using TestConfigClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "first");
        client.Env.Set("CUSTOM_KEY", string.Empty);

        Assert.Null(client.Env.Get("CUSTOM_KEY"));
    }

    [Fact]
    public async Task Get_UnsetKey_ReturnsNull()
    {
        using TestConfigClient client = await OpenAsync();
        Assert.Null(client.Env.Get("NEVER_SET"));
    }

    [Fact]
    public async Task All_ReflectsEverySetKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.Set("KEY_A", "a");
        client.Env.Set("KEY_B", "b");
        client.Env.Set("KEY_C", "c");

        IReadOnlyDictionary<string, string> snapshot = client.Env.All;

        Assert.Equal(3, snapshot.Count);
        Assert.Equal("a", snapshot["KEY_A"]);
        Assert.Equal("b", snapshot["KEY_B"]);
        Assert.Equal("c", snapshot["KEY_C"]);
    }

    [Fact]
    public async Task All_EmptyEnv_ReturnsEmptyDictionary()
    {
        using TestConfigClient client = await OpenAsync();

        IReadOnlyDictionary<string, string> snapshot = client.Env.All;

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task GetAt_AndAllAt_ReadFromSpecificScope()
    {
        // Locks the per-scope read path: GetAt(scope) reads only the
        // value stored at that scope, no merging across other scopes.
        using TestConfigClient client = await OpenAsync();
        client.Env.SetAt("SCOPED_KEY", "user-scope-value", ConfigScope.User);

        Assert.Equal("user-scope-value", client.Env.GetAt("SCOPED_KEY", ConfigScope.User));
        IReadOnlyDictionary<string, string> snapshot = client.Env.AllAt(ConfigScope.User);
        Assert.Single(snapshot);
        Assert.Equal("user-scope-value", snapshot["SCOPED_KEY"]);
    }

    // ── On-disk shape ─────────────────────────────────────────────────

    [Fact]
    public async Task Set_PersistsToSettingsJsonEnvObject()
    {
        // Verify the on-disk representation matches what the runtime
        // expects: nested under "env" as a string→string map.
        using TestConfigClient client = await OpenAsync();
        client.Env.Set("MY_VAR", "42");
        await client.SaveAsync(force: true, CancellationToken.None);

        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"), TestContext.Current.CancellationToken);
        OrdinalAssert.Contains("\"env\":", json);
        OrdinalAssert.Contains("\"MY_VAR\": \"42\"", json);
    }

    // ── Typed: MaxThinkingTokens ──────────────────────────────────────

    // ── Typed: MaxOutputTokens ────────────────────────────────────────

    // ── Typed: DisableAutoMemory + DisableAutoUpdater (1/0 convention) ─

    // ── Typed: AnthropicModel (free-form string) ──────────────────────

    // ── Reload round-trip ─────────────────────────────────────────────

    // ── Argument validation ───────────────────────────────────────────

    [Fact]
    public async Task Get_NullVarName_Throws()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null inputs (a subclass of ArgumentException) — catch the
        // base type so either is accepted.
        using TestConfigClient client = await OpenAsync();
        Assert.Throws<ArgumentNullException>(() => client.Env.Get(null!));
    }

    [Fact]
    public async Task Set_WhitespaceVarName_Throws()
    {
        // Whitespace-only takes the ThrowIfNullOrWhiteSpace branch that
        // throws plain ArgumentException (NOT ArgumentNullException, since
        // the input is non-null).
        using TestConfigClient client = await OpenAsync();
        Assert.Throws<ArgumentException>(() => client.Env.Set("   ", "v"));
    }
}