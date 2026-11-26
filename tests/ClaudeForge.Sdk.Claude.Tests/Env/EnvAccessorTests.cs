using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Env;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Env;

/// <summary>
/// Claude Code's typed env properties (MaxThinkingTokens, MaxOutputTokens,
/// DisableAutoMemory, DisableAutoUpdater, AnthropicModel) end-to-end against
/// a real on-disk workspace: null = remove semantics, lenient parsing for
/// legacy / hand-edited values.
/// </summary>
/// <remarks>
/// ⓘ The typed half of what was one class in <c>AgentForge.Sdk.Tests</c>. It
/// moved with <see cref="ClaudeEnvAccessorExtensions"/> in plans/00008 step 10;
/// the generic dictionary surface's tests stayed there, under the same name.
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

    private async Task<ClaudeCodeClient> OpenAsync()
    {
        ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    // ── Generic dictionary surface ────────────────────────────────────

    // ── On-disk shape ─────────────────────────────────────────────────

    // ── Typed: MaxThinkingTokens ──────────────────────────────────────

    [Fact]
    public async Task MaxThinkingTokens_RoundTripsAsInt()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxThinkingTokens = 32000;

        Assert.Equal(32000, client.Env.MaxThinkingTokens);
        MessageAssert.Equal("32000", client.Env.Get(EnvVarKey.MaxThinkingTokens),
            "Must write as a base-10 string under the canonical env key.");
    }

    [Fact]
    public async Task MaxThinkingTokens_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
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
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.MaxThinkingTokens, "not-a-number");

        Assert.Null(client.Env.MaxThinkingTokens);
        // The raw string is still readable via the generic surface.
        Assert.Equal("not-a-number", client.Env.Get(EnvVarKey.MaxThinkingTokens));
    }

    // ── Typed: MaxOutputTokens ────────────────────────────────────────

    [Fact]
    public async Task MaxOutputTokens_RoundTripsAsInt()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.MaxOutputTokens = 8192;

        Assert.Equal(8192, client.Env.MaxOutputTokens);
        Assert.Equal("8192", client.Env.Get(EnvVarKey.MaxOutputTokens));
    }

    [Fact]
    public async Task MaxOutputTokens_UsesCorrectEnvKeyOnDisk()
    {
        // CLAUDE_CODE_MAX_OUTPUT_TOKENS — note the prefix.  Distinct
        // from MaxThinkingTokens which is bare MAX_THINKING_TOKENS.
        using ClaudeCodeClient client = await OpenAsync();
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
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoMemory = true;

        Assert.True(client.Env.DisableAutoMemory);
        MessageAssert.Equal("1", client.Env.Get(EnvVarKey.DisableAutoMemory),
            "Claude Code uses the \"1\" / \"0\" convention for env-var booleans.");
    }

    [Fact]
    public async Task DisableAutoMemory_False_StoresAsZero()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoMemory = false;

        Assert.False(client.Env.DisableAutoMemory);
        Assert.Equal("0", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [Fact]
    public async Task DisableAutoMemory_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
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
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.Set(EnvVarKey.DisableAutoMemory, "true");

        MessageAssert.Null(client.Env.DisableAutoMemory,
            "Strict 1/0 parsing — \"true\" must NOT be coerced.");
        Assert.Equal("true", client.Env.Get(EnvVarKey.DisableAutoMemory));
    }

    [Fact]
    public async Task DisableAutoUpdater_RoundTripsAsOneZero()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.DisableAutoUpdater = true;
        Assert.Equal("1", client.Env.Get(EnvVarKey.DisableAutoUpdater));

        client.Env.DisableAutoUpdater = false;
        Assert.Equal("0", client.Env.Get(EnvVarKey.DisableAutoUpdater));
    }

    // ── Typed: AnthropicModel (free-form string) ──────────────────────

    [Fact]
    public async Task AnthropicModel_RoundTripsAsString()
    {
        using ClaudeCodeClient client = await OpenAsync();
        client.Env.AnthropicModel = "claude-opus-4-5";

        Assert.Equal("claude-opus-4-5", client.Env.AnthropicModel);
        Assert.Equal("claude-opus-4-5", client.Env.Get(EnvVarKey.AnthropicModel));
    }

    [Fact]
    public async Task AnthropicModel_NullClearsKey()
    {
        using ClaudeCodeClient client = await OpenAsync();
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
        using (ClaudeCodeClient writer = await OpenAsync())
        {
            writer.Env.MaxThinkingTokens = 32000;
            writer.Env.MaxOutputTokens = 8192;
            writer.Env.DisableAutoMemory = true;
            writer.Env.AnthropicModel = "sonnet";
            await writer.SaveAsync(force: true, CancellationToken.None);
        }

        using ClaudeCodeClient reader = await OpenAsync();
        Assert.Equal(32000, reader.Env.MaxThinkingTokens);
        Assert.Equal(8192, reader.Env.MaxOutputTokens);
        Assert.True(reader.Env.DisableAutoMemory);
        Assert.Equal("sonnet", reader.Env.AnthropicModel);
    }

    // ── Argument validation ───────────────────────────────────────────
}