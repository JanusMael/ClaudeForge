using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Models;

/// <summary>
/// Pins the <c>IClaudeConfigClient.Models</c> contract: the bundled catalog is
/// reachable through the client, the relationship queries delegate correctly,
/// and the default-mode gating (auto needs an auto-capable model AND User scope)
/// is computed where the catalog alone can't express it.
/// </summary>
[TestClass]
public sealed class ModelCatalogAccessorTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-models-acc-" + Guid.NewGuid().ToString("N"));
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

    private static async Task<ClaudeCodeClient> OpenAsync()
    {
        ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        return client;
    }

    [TestMethod]
    public async Task Models_IsReachable_AndPopulated()
    {
        using ClaudeCodeClient client = await OpenAsync();
        Assert.IsTrue(client.Models.AllModels.Count >= 6);
        Assert.IsTrue(client.Models.AllDefaultModes.Any(d => d.Id == "bypassPermissions"));
    }

    [TestMethod]
    public async Task EffortQueries_DelegateToCatalog()
    {
        using ClaudeCodeClient client = await OpenAsync();
        Assert.IsFalse(client.Models.IsEffortSupported("claude-sonnet-4-6", "xhigh"));
        Assert.IsTrue(client.Models.IsEffortSupported("claude-opus-4-8", "xhigh"));
        Assert.AreEqual("high", client.Models.NearestAnalogEffort("claude-sonnet-4-6", "xhigh"));
        CollectionAssert.DoesNotContain(client.Models.PersistableEffortLevels("claude-opus-4-8").ToList(), "max");
    }

    [TestMethod]
    public async Task IsDefaultModeAllowed_AutoGatedByModelAndScope()
    {
        using ClaudeCodeClient client = await OpenAsync();

        // auto: needs an auto-capable model AND User scope.
        Assert.IsTrue(client.Models.IsDefaultModeAllowed("auto", "claude-opus-4-8", ConfigScope.User));
        Assert.IsFalse(client.Models.IsDefaultModeAllowed("auto", "claude-opus-4-8", ConfigScope.Project),
            "auto is ignored outside User scope.");
        Assert.IsFalse(client.Models.IsDefaultModeAllowed("auto", "claude-haiku-4-5", ConfigScope.User),
            "Haiku does not support auto.");
        Assert.IsTrue(client.Models.IsDefaultModeAllowed("auto", null, ConfigScope.User),
            "Unset/unknown model is lenient — the default model is auto-capable.");

        // non-gated modes are allowed everywhere.
        Assert.IsTrue(client.Models.IsDefaultModeAllowed("default", "claude-haiku-4-5", ConfigScope.Project));
        Assert.IsTrue(client.Models.IsDefaultModeAllowed("bypassPermissions", null, ConfigScope.Project));
    }

    [TestMethod]
    public async Task ModelSuggestions_OmitsLegacyByDefault()
    {
        using ClaudeCodeClient client = await OpenAsync();
        IReadOnlyList<string> suggestions = client.Models.ModelSuggestions();
        CollectionAssert.Contains(suggestions.ToList(), "opus");
        CollectionAssert.DoesNotContain(suggestions.ToList(), "claude-opus-4-6", "Legacy ids are hidden by default.");
    }
}
