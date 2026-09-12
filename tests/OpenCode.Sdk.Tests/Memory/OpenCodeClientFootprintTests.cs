using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Memory;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Memory;

/// <summary>
/// The two OpenCode clients report OPENCODE's footprint.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Both of them reported Claude's until 2026-09-12, and nothing failed.</b>
/// <c>AgentConfigClientCore</c> builds <c>new FootprintService()</c>, whose catalog defaults to
/// <see cref="FootprintCatalog.Default"/> — Claude Code's seven <c>~/.claude</c> categories — and
/// neither subclass overrode it. <see cref="OpenCodeFootprint.Catalog"/> had existed, been
/// measured and been tested for a whole phase; nothing connected it to a client.
/// </para>
/// <para>
/// ⚠ <b>The failure was invisible by construction, which is what these tests buy.</b> The rows
/// were real rows over real directories, so nothing threw, nothing logged, and a page rendering
/// them would have looked entirely healthy. <c>OpenCodeFootprintTests</c> could not catch it
/// because it constructs its own <see cref="FootprintService"/> with the catalog passed
/// explicitly — it proves the catalog is right, never that anything USES it.
/// </para>
/// <para>
/// ⛔ <b>And a delete would have hit the other agent.</b>
/// <c>DeleteFootprintCategoryAsync</c> resolves its target through the same catalog, so
/// "delete this OpenCode category" on the shipped client meant deleting
/// <c>~/.claude/projects</c>, <c>history.jsonl</c> or <c>todos/</c>. That is why this is tested
/// at the CLIENT rather than left to the page.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeClientFootprintTests
{
    private string _home = string.Empty;
    private string? _redirectOnEntry;

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(Path.GetTempPath(), "occf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        PlatformPaths.TestUserProfileOverride = _home;

        // Cleared for the reason OpenCodeFootprintTests documents: the config root resolves
        // through GlobalDirectory(env) and so honours this variable, and a developer with it
        // exported would otherwise see these fail on a perfectly good tree.
        _redirectOnEntry = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _redirectOnEntry);
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public async Task TheConfigClientReportsOpenCodesCategories_NotClaudes()
    {
        using OpenCodeClient client = new();

        IReadOnlyList<FootprintCategoryStats> stats =
            await client.GetFootprintStatsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            OpenCodeFootprint.Catalog.All.Select(c => c.Id).ToArray(),
            stats.Select(s => s.Category.Id).ToArray(),
            "The client must walk OpenCode's catalog, in OpenCode's deliberate prune order.");
    }

    [TestMethod]
    public async Task TheTuiClientReportsTheSameCatalog_BecauseItsFilesShareTheConfigRoot()
    {
        using OpenCodeTuiClient client = new();

        IReadOnlyList<FootprintCategoryStats> stats =
            await client.GetFootprintStatsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            OpenCodeFootprint.Catalog.All.Select(c => c.Id).ToArray(),
            stats.Select(s => s.Category.Id).ToArray(),
            "tui.json lives inside the config root, so the TUI's footprint IS OpenCode's — but "
            + "the answer still has to be OpenCode's rather than the neutral default's.");
    }

    [TestMethod]
    public async Task NoRowPointsIntoTheOtherAgentsDirectory()
    {
        // ⛔ The assertion that would have caught the original defect on its own. Every Claude
        // category resolves under ~/.claude; not one OpenCode row may.
        using OpenCodeClient client = new();

        IReadOnlyList<FootprintCategoryStats> stats =
            await client.GetFootprintStatsAsync(CancellationToken.None);

        string claudeHome = Path.Combine(_home, ".claude");
        foreach (FootprintCategoryStats row in stats)
        {
            Assert.IsFalse(
                row.AbsolutePath.StartsWith(claudeHome, StringComparison.OrdinalIgnoreCase),
                $"Category '{row.Category.Id}' resolved to {row.AbsolutePath}, inside Claude's "
                + "home. A delete on that row would remove the other agent's data.");
        }
    }

    [TestMethod]
    public async Task TheRowsResolveAgainstOpenCodesOwnRoots()
    {
        using OpenCodeClient client = new();

        IReadOnlyList<FootprintCategoryStats> stats =
            await client.GetFootprintStatsAsync(CancellationToken.None);

        string configRoot = OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess());
        string dataRoot = OpenCodePaths.DataDirectory();

        FootprintCategoryStats nodeModules = stats.Single(s => s.Category.Id == "node-modules");
        Assert.IsTrue(
            nodeModules.AbsolutePath.StartsWith(configRoot, StringComparison.Ordinal),
            $"node-modules must anchor under the config root; got {nodeModules.AbsolutePath}.");

        FootprintCategoryStats database = stats.Single(s => s.Category.Id == "session-database");
        Assert.IsTrue(
            database.AbsolutePath.StartsWith(dataRoot, StringComparison.Ordinal),
            $"session-database must anchor under the data root; got {database.AbsolutePath}.");
    }

    [TestMethod]
    public async Task TheClientHonoursAProfileSetAfterItWasConstructed()
    {
        // The AsyncLocal lesson, at the client rather than at the service: the footprint service
        // is cached for the client's lifetime, so a captured root would pin whichever sandbox was
        // current when the client was first touched. `roots:` is handed a METHOD GROUP for exactly
        // this reason, and passing `Roots()` instead would compile and fail only here.
        using OpenCodeClient client = new();

        string second = Path.Combine(Path.GetTempPath(), "occf2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(second);
        try
        {
            PlatformPaths.TestUserProfileOverride = second;

            IReadOnlyList<FootprintCategoryStats> stats =
                await client.GetFootprintStatsAsync(CancellationToken.None);

            Assert.IsTrue(
                stats.All(s => s.AbsolutePath.StartsWith(second, StringComparison.OrdinalIgnoreCase)),
                "Every row must sit under the profile current at CALL time, not at construction.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = _home;
            try
            {
                Directory.Delete(second, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }
}
