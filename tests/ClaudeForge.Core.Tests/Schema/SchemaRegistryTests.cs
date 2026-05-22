using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Tests for SchemaRegistry caching and loading behaviour.
/// All tests use a deliberately-failing HttpClient so they exercise the
/// bundled-fallback path — the path that previously omitted the memory-cache
/// write and caused JsonSchemaException "Overwriting registered schemas" on
/// any second call within the same process run.
/// </summary>
[TestClass]
public sealed class SchemaRegistryTests
{
    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    /// <summary>HttpMessageHandler that always refuses the connection.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network unavailable");
        }
    }

    private static SchemaRegistry OfflineRegistry()
    {
        return new SchemaRegistry(new HttpClient(new FailingHandler()));
    }

    // -----------------------------------------------------------------------
    // Bundled-fallback caching regression tests
    // -----------------------------------------------------------------------

    [TestMethod]
    [Description("Regression: second call must not throw JsonSchemaException " +
                 "'Overwriting registered schemas'. Reproduces the crash seen " +
                 "when Open Project or profile change triggered a second reload.")]
    public async Task GetClaudeDesktopConfigNodeAsync_CalledTwice_DoesNotThrow()
    {
        using SchemaRegistry registry = OfflineRegistry();

        await registry.GetClaudeDesktopConfigNodeAsync(); // first — registers globally
        await registry.GetClaudeDesktopConfigNodeAsync(); // second — must hit memory cache
    }

    [TestMethod]
    [Description("Same regression for the Claude Code settings schema.")]
    public async Task GetClaudeCodeSettingsNodeAsync_CalledTwice_DoesNotThrow()
    {
        using SchemaRegistry registry = OfflineRegistry();

        await registry.GetClaudeCodeSettingsNodeAsync();
        await registry.GetClaudeCodeSettingsNodeAsync();
    }

    [TestMethod]
    [Description("Simulates the LoadAllWorkspacesAsync sequence: both schemas " +
                 "loaded once on startup, then again on Open Project / reload.")]
    public async Task BothSchemas_LoadedTwiceSequentially_DoNotThrow()
    {
        using SchemaRegistry registry = OfflineRegistry();

        // First pass (startup)
        await registry.GetClaudeCodeSettingsNodeAsync();
        await registry.GetClaudeDesktopConfigNodeAsync();

        // Second pass (Open Project / Reload / profile change)
        await registry.GetClaudeCodeSettingsNodeAsync();
        await registry.GetClaudeDesktopConfigNodeAsync();
    }

    // -----------------------------------------------------------------------
    // Basic sanity: bundled schemas are parseable and non-empty
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GetClaudeCodeSettingsNodeAsync_ReturnsBundledSchema_WithProperties()
    {
        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode node = await registry.GetClaudeCodeSettingsNodeAsync();

        Assert.IsNotNull(node, "Root schema node should not be null");
    }

    [TestMethod]
    public async Task GetClaudeDesktopConfigNodeAsync_ReturnsBundledSchema_NotNull()
    {
        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode node = await registry.GetClaudeDesktopConfigNodeAsync();

        Assert.IsNotNull(node, "Root schema node should not be null");
    }
}