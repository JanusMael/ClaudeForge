using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Env;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Env;

/// <summary>
/// typed accessor for the settings.json <c>env</c> map.
/// Pins the IEnvAccessor contract end-to-end against a real on-disk
/// workspace.  Generic dictionary surface, typed convenience properties
/// (MaxThinkingTokens, MaxOutputTokens, DisableAutoMemory,
/// DisableAutoUpdater, AnthropicModel), null = remove semantics, lenient
/// parsing for legacy / hand-edited values.
/// </summary>
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

    [Fact]
    public async Task MaxThinkingTokens_RoundTripsAsInt()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.MaxThinkingTokens = 32000;

        Assert.Equal(32000, client.Env.MaxThinkingTokens);
        MessageAssert.Equal("32000", client.Env.Get(EnvVarKey.MaxThinkingTokens),
            "Must write as a base-10 string under the canonical env key.");
    }

    [Fact]
    public async Task MaxThinkingTokens_NullClearsKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.MaxThinkingTokens = 8000;
        client.Env.MaxThinkingTokens = null;

        Assert.Null(client.Env.MaxThinkingTokens);
        Assert.Null(client.Env.Get(EnvVarKey.MaxThinkingTokens));
    }

    [Fact]
    public async Task MaxThinkingTokens_InvalidStoredValue_ReturnsNullNotThrows()
    {
        // Lenient read: a hand-edited settings.json with
        // "MAX_THINKING_TOKENS": "abc" should yield null on the typed
        // getter, not throw.  Matches the rest of the SDK's
        // best-effort-read posture.
        using TestConfigClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.MaxThinkingTokens, "not-a-number");

        Assert.Null(client.Env.MaxThinkingTokens);
        // The raw string is still readable via the generic surface.
        Assert.Equal("not-a-number", client.Env.Get(EnvVarKey.MaxThinkingTokens));
    }

    // ── Typed: MaxOutputTokens ────────────────────────────────────────

    [Fact]
    public async Task MaxOutputTokens_RoundTripsAsInt()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.MaxOutputTokens = 8192;

        Assert.Equal(8192, client.Env.MaxOutputTokens);
        Assert.Equal("8192", client.Env.Get(EnvVarKey.MaxOutputTokens));
    }

    [Fact]
    public async Task MaxOutputTokens_UsesCorrectEnvKeyOnDisk()
    {
        // CLAUDE_CODE_MAX_OUTPUT_TOKENS — note the prefix.  Distinct
        // from MaxThinkingTokens which is bare MAX_THINKING_TOKENS.
        using TestConfigClient client = await OpenAsync();
        client.Env.MaxOutputTokens = 4096;
        await client.SaveAsync(force: true, CancellationToken.None);

        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"), TestContext.Current.CancellationToken);
        MessageAssert.Contains("\"CLAUDE_CODE_MAX_OUTPUT_TOKENS\": \"4096\"", json,
            "MaxOutputTokens must write under CLAUDE_CODE_MAX_OUTPUT_TOKENS, not MAX_OUTPUT_TOKENS.");
    }

    // ── Typed: DisableAutoMemory + DisableAutoUpdater (1/0 convention) ─

    [Fact]
    public async Task DisableAutoMemory_True_StoresAsOne()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.DisableAutoMemory = true;

        Assert.True(client.Env.DisableAutoMemory);
        MessageAssert.Equal("1", client.Env.Get(EnvVarKey.DisableAutoMemory),
            "Claude Code uses the \"1\" / \"0\" convention for env-var booleans.");
    }

    [Fact]
    public async Task DisableAutoMemory_False_StoresAsZero()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.DisableAutoMemory = false;

        Assert.False(client.Env.DisableAutoMemory);
        Assert.Equal("0", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [Fact]
    public async Task DisableAutoMemory_NullClearsKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.DisableAutoMemory = true;
        client.Env.DisableAutoMemory = null;

        Assert.Null(client.Env.DisableAutoMemory);
        Assert.Null(client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [Fact]
    public async Task DisableAutoMemory_LegacyTrueLiteral_ParsedAsNull()
    {
        // Strict parsing: only "1" / "0" are recognised.  "true" /
        // "false" are NOT what Claude Code uses — accepting them would
        // mask a typo.  Verify the stored "true" comes back as null on
        // the typed getter (the raw string is still readable).
        using TestConfigClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.DisableAutoMemory, "true");

        MessageAssert.Null(client.Env.DisableAutoMemory,
            "Strict 1/0 parsing — \"true\" must NOT be coerced.");
        Assert.Equal("true", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [Fact]
    public async Task DisableAutoUpdater_RoundTripsAsOneZero()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.DisableAutoUpdater = true;
        Assert.Equal("1", client.Env.Get(EnvVarKey.DisableAutoUpdater));

        client.Env.DisableAutoUpdater = false;
        Assert.Equal("0", client.Env.Get(EnvVarKey.DisableAutoUpdater));
    }

    // ── Typed: AnthropicModel (free-form string) ──────────────────────

    [Fact]
    public async Task AnthropicModel_RoundTripsAsString()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.AnthropicModel = "claude-opus-4-5";

        Assert.Equal("claude-opus-4-5", client.Env.AnthropicModel);
        Assert.Equal("claude-opus-4-5", client.Env.Get(EnvVarKey.AnthropicModel));
    }

    [Fact]
    public async Task AnthropicModel_NullClearsKey()
    {
        using TestConfigClient client = await OpenAsync();
        client.Env.AnthropicModel = "claude-haiku";
        client.Env.AnthropicModel = null;

        Assert.Null(client.Env.AnthropicModel);
    }

    // ── Reload round-trip ─────────────────────────────────────────────

    [Fact]
    public async Task TypedSetters_SurviveSaveAndReload()
    {
        // End-to-end: write via typed setters → save → reload → verify
        // both the typed getters AND the on-disk JSON shape.
        using (TestConfigClient writer = await OpenAsync())
        {
            writer.Env.MaxThinkingTokens = 32000;
            writer.Env.MaxOutputTokens = 8192;
            writer.Env.DisableAutoMemory = true;
            writer.Env.AnthropicModel = "sonnet";
            await writer.SaveAsync(force: true, CancellationToken.None);
        }

        using TestConfigClient reader = await OpenAsync();
        Assert.Equal(32000, reader.Env.MaxThinkingTokens);
        Assert.Equal(8192, reader.Env.MaxOutputTokens);
        Assert.True(reader.Env.DisableAutoMemory);
        Assert.Equal("sonnet", reader.Env.AnthropicModel);
    }

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