using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Models;

/// <summary>
/// Pins the <c>IAgentConfigClient.Models</c> contract: the bundled catalog is
/// reachable through the client, the relationship queries delegate correctly,
/// and the default-mode gating (auto needs an auto-capable model AND User scope)
/// is computed where the catalog alone can't express it.
/// </summary>
public sealed class ModelCatalogAccessorTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public ModelCatalogAccessorTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-models-acc-" + Guid.NewGuid().ToString("N"));
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

    private static async Task<ClaudeCodeClient> OpenAsync()
    {
        ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task Models_IsReachable_AndPopulated()
    {
        using ClaudeCodeClient client = await OpenAsync();
        Assert.True(client.Models.AllModels.Count >= 6);
        Assert.Contains(client.Models.AllDefaultModes, d => d.Id == "bypassPermissions");
    }

    [Fact]
    public async Task EffortQueries_DelegateToCatalog()
    {
        using ClaudeCodeClient client = await OpenAsync();
        Assert.False(client.Models.IsEffortSupported("claude-sonnet-4-6", "xhigh"));
        Assert.True(client.Models.IsEffortSupported("claude-opus-4-8", "xhigh"));
        Assert.Equal("high", client.Models.NearestAnalogEffort("claude-sonnet-4-6", "xhigh"));
        Assert.DoesNotContain("max", client.Models.PersistableEffortLevels("claude-opus-4-8").ToList());
    }

    [Fact]
    public async Task IsDefaultModeAllowed_AutoGatedByModelAndScope()
    {
        using ClaudeCodeClient client = await OpenAsync();

        // auto: needs an auto-capable model AND User scope.
        Assert.True(client.Models.IsDefaultModeAllowed("auto", "claude-opus-4-8", ConfigScope.User));
        Assert.False(client.Models.IsDefaultModeAllowed("auto", "claude-opus-4-8", ConfigScope.Project),
            "auto is ignored outside User scope.");
        Assert.False(client.Models.IsDefaultModeAllowed("auto", "claude-haiku-4-5", ConfigScope.User),
            "Haiku does not support auto.");
        Assert.True(client.Models.IsDefaultModeAllowed("auto", null, ConfigScope.User),
            "Unset/unknown model is lenient — the default model is auto-capable.");

        // non-gated modes are allowed everywhere.
        Assert.True(client.Models.IsDefaultModeAllowed("default", "claude-haiku-4-5", ConfigScope.Project));
        Assert.True(client.Models.IsDefaultModeAllowed("bypassPermissions", null, ConfigScope.Project));
    }

    [Fact]
    public async Task ModelSuggestions_OmitsLegacyByDefault()
    {
        using ClaudeCodeClient client = await OpenAsync();
        IReadOnlyList<string> suggestions = client.Models.ModelSuggestions();
        Assert.Contains("opus", suggestions.ToList());
        MessageAssert.DoesNotContain("claude-opus-4-6", suggestions.ToList(), "Legacy ids are hidden by default.");
    }
}
