using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Env;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Env;

/// <summary>
/// typed accessor for the settings.json <c>env</c> map.
/// Pins the IEnvAccessor contract end-to-end against a real on-disk
/// workspace.  Generic dictionary surface, typed convenience properties
/// (MaxThinkingTokens, MaxOutputTokens, DisableAutoMemory,
/// DisableAutoUpdater, AnthropicModel), null = remove semantics, lenient
/// parsing for legacy / hand-edited values.
/// </summary>
[TestClass]
public sealed class EnvAccessorTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-env-acc-" + Guid.NewGuid().ToString("N"));
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

    // ── Generic dictionary surface ────────────────────────────────────

    [TestMethod]
    public async Task Set_AndGet_RoundTripsArbitraryKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "custom-value");

        Assert.AreEqual("custom-value", client.Env.Get("CUSTOM_KEY"));
    }

    [TestMethod]
    public async Task Set_NullValue_RemovesKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "first");
        Assert.AreEqual("first", client.Env.Get("CUSTOM_KEY"));

        client.Env.Set("CUSTOM_KEY", null);

        Assert.IsNull(client.Env.Get("CUSTOM_KEY"),
            "Setting null must remove the key from the env map.");
    }

    [TestMethod]
    public async Task Set_EmptyString_RemovesKey()
    {
        // Empty string == "remove" mirrors the IPermissionsAccessor null
        // semantics applied to a string-typed surface.  The runtime would
        // never act on an empty env value so making the SDK collapse it
        // to "remove" is the correct behaviour.
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set("CUSTOM_KEY", "first");
        client.Env.Set("CUSTOM_KEY", string.Empty);

        Assert.IsNull(client.Env.Get("CUSTOM_KEY"));
    }

    [TestMethod]
    public async Task Get_UnsetKey_ReturnsNull()
    {
        using ClaudeCodeClient client = await OpenAsync();
        Assert.IsNull(client.Env.Get("NEVER_SET"));
    }

    [TestMethod]
    public async Task All_ReflectsEverySetKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set("KEY_A", "a");
        client.Env.Set("KEY_B", "b");
        client.Env.Set("KEY_C", "c");

        IReadOnlyDictionary<string, string> snapshot = client.Env.All;

        Assert.AreEqual(3, snapshot.Count);
        Assert.AreEqual("a", snapshot["KEY_A"]);
        Assert.AreEqual("b", snapshot["KEY_B"]);
        Assert.AreEqual("c", snapshot["KEY_C"]);
    }

    [TestMethod]
    public async Task All_EmptyEnv_ReturnsEmptyDictionary()
    {
        using ClaudeCodeClient client = await OpenAsync();

        IReadOnlyDictionary<string, string> snapshot = client.Env.All;

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(0, snapshot.Count);
    }

    [TestMethod]
    public async Task GetAt_AndAllAt_ReadFromSpecificScope()
    {
        // Locks the per-scope read path: GetAt(scope) reads only the
        // value stored at that scope, no merging across other scopes.
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.SetAt("SCOPED_KEY", "user-scope-value", ConfigScope.User);

        Assert.AreEqual("user-scope-value", client.Env.GetAt("SCOPED_KEY", ConfigScope.User));
        IReadOnlyDictionary<string, string> snapshot = client.Env.AllAt(ConfigScope.User);
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual("user-scope-value", snapshot["SCOPED_KEY"]);
    }

    // ── On-disk shape ─────────────────────────────────────────────────

    [TestMethod]
    public async Task Set_PersistsToSettingsJsonEnvObject()
    {
        // Verify the on-disk representation matches what the runtime
        // expects: nested under "env" as a string→string map.
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set("MY_VAR", "42");
        await client.SaveAsync(force: true, CancellationToken.None);

        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        StringAssert.Contains(json, "\"env\":");
        StringAssert.Contains(json, "\"MY_VAR\": \"42\"");
    }

    // ── Typed: MaxThinkingTokens ──────────────────────────────────────

    [TestMethod]
    public async Task MaxThinkingTokens_RoundTripsAsInt()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxThinkingTokens = 32000;

        Assert.AreEqual(32000, client.Env.MaxThinkingTokens);
        Assert.AreEqual("32000", client.Env.Get(EnvVarKey.MaxThinkingTokens),
            "Must write as a base-10 string under the canonical env key.");
    }

    [TestMethod]
    public async Task MaxThinkingTokens_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxThinkingTokens = 8000;
        client.Env.MaxThinkingTokens = null;

        Assert.IsNull(client.Env.MaxThinkingTokens);
        Assert.IsNull(client.Env.Get(EnvVarKey.MaxThinkingTokens));
    }

    [TestMethod]
    public async Task MaxThinkingTokens_InvalidStoredValue_ReturnsNullNotThrows()
    {
        // Lenient read: a hand-edited settings.json with
        // "MAX_THINKING_TOKENS": "abc" should yield null on the typed
        // getter, not throw.  Matches the rest of the SDK's
        // best-effort-read posture.
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.MaxThinkingTokens, "not-a-number");

        Assert.IsNull(client.Env.MaxThinkingTokens);
        // The raw string is still readable via the generic surface.
        Assert.AreEqual("not-a-number", client.Env.Get(EnvVarKey.MaxThinkingTokens));
    }

    // ── Typed: MaxOutputTokens ────────────────────────────────────────

    [TestMethod]
    public async Task MaxOutputTokens_RoundTripsAsInt()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxOutputTokens = 8192;

        Assert.AreEqual(8192, client.Env.MaxOutputTokens);
        Assert.AreEqual("8192", client.Env.Get(EnvVarKey.MaxOutputTokens));
    }

    [TestMethod]
    public async Task MaxOutputTokens_UsesCorrectEnvKeyOnDisk()
    {
        // CLAUDE_CODE_MAX_OUTPUT_TOKENS — note the prefix.  Distinct
        // from MaxThinkingTokens which is bare MAX_THINKING_TOKENS.
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxOutputTokens = 4096;
        await client.SaveAsync(force: true, CancellationToken.None);

        string json = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".claude", "settings.json"));
        StringAssert.Contains(json, "\"CLAUDE_CODE_MAX_OUTPUT_TOKENS\": \"4096\"",
            "MaxOutputTokens must write under CLAUDE_CODE_MAX_OUTPUT_TOKENS, not MAX_OUTPUT_TOKENS.");
    }

    // ── Typed: DisableAutoMemory + DisableAutoUpdater (1/0 convention) ─

    [TestMethod]
    public async Task DisableAutoMemory_True_StoresAsOne()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoMemory = true;

        Assert.IsTrue(client.Env.DisableAutoMemory);
        Assert.AreEqual("1", client.Env.Get(EnvVarKey.DisableAutoMemory),
            "Claude Code uses the \"1\" / \"0\" convention for env-var booleans.");
    }

    [TestMethod]
    public async Task DisableAutoMemory_False_StoresAsZero()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoMemory = false;

        Assert.IsFalse(client.Env.DisableAutoMemory);
        Assert.AreEqual("0", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [TestMethod]
    public async Task DisableAutoMemory_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoMemory = true;
        client.Env.DisableAutoMemory = null;

        Assert.IsNull(client.Env.DisableAutoMemory);
        Assert.IsNull(client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [TestMethod]
    public async Task DisableAutoMemory_LegacyTrueLiteral_ParsedAsNull()
    {
        // Strict parsing: only "1" / "0" are recognised.  "true" /
        // "false" are NOT what Claude Code uses — accepting them would
        // mask a typo.  Verify the stored "true" comes back as null on
        // the typed getter (the raw string is still readable).
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.DisableAutoMemory, "true");

        Assert.IsNull(client.Env.DisableAutoMemory,
            "Strict 1/0 parsing — \"true\" must NOT be coerced.");
        Assert.AreEqual("true", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [TestMethod]
    public async Task DisableAutoUpdater_RoundTripsAsOneZero()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoUpdater = true;
        Assert.AreEqual("1", client.Env.Get(EnvVarKey.DisableAutoUpdater));

        client.Env.DisableAutoUpdater = false;
        Assert.AreEqual("0", client.Env.Get(EnvVarKey.DisableAutoUpdater));
    }

    // ── Typed: AnthropicModel (free-form string) ──────────────────────

    [TestMethod]
    public async Task AnthropicModel_RoundTripsAsString()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.AnthropicModel = "claude-opus-4-5";

        Assert.AreEqual("claude-opus-4-5", client.Env.AnthropicModel);
        Assert.AreEqual("claude-opus-4-5", client.Env.Get(EnvVarKey.AnthropicModel));
    }

    [TestMethod]
    public async Task AnthropicModel_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.AnthropicModel = "claude-haiku";
        client.Env.AnthropicModel = null;

        Assert.IsNull(client.Env.AnthropicModel);
    }

    // ── Reload round-trip ─────────────────────────────────────────────

    [TestMethod]
    public async Task TypedSetters_SurviveSaveAndReload()
    {
        // End-to-end: write via typed setters → save → reload → verify
        // both the typed getters AND the on-disk JSON shape.
        using (ClaudeCodeClient writer = await OpenAsync())
        {
            writer.Env.MaxThinkingTokens = 32000;
            writer.Env.MaxOutputTokens = 8192;
            writer.Env.DisableAutoMemory = true;
            writer.Env.AnthropicModel = "sonnet";
            await writer.SaveAsync(force: true, CancellationToken.None);
        }

        using ClaudeCodeClient reader = await OpenAsync();
        Assert.AreEqual(32000, reader.Env.MaxThinkingTokens);
        Assert.AreEqual(8192, reader.Env.MaxOutputTokens);
        Assert.IsTrue(reader.Env.DisableAutoMemory);
        Assert.AreEqual("sonnet", reader.Env.AnthropicModel);
    }

    // ── Argument validation ───────────────────────────────────────────

    [TestMethod]
    public async Task Get_NullVarName_Throws()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException
        // for null inputs (a subclass of ArgumentException) — catch the
        // base type so either is accepted.
        using ClaudeCodeClient client = await OpenAsync();
        Assert.ThrowsException<ArgumentNullException>(() => client.Env.Get(null!));
    }

    [TestMethod]
    public async Task Set_WhitespaceVarName_Throws()
    {
        // Whitespace-only takes the ThrowIfNullOrWhiteSpace branch that
        // throws plain ArgumentException (NOT ArgumentNullException, since
        // the input is non-null).
        using ClaudeCodeClient client = await OpenAsync();
        Assert.ThrowsException<ArgumentException>(() => client.Env.Set("   ", "v"));
    }
}