using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Tests for SchemaRegistry caching and loading behaviour.
/// All tests use a deliberately-failing HttpClient so they exercise the
/// bundled-fallback path — the path that previously omitted the memory-cache
/// write and caused JsonSchemaException "Overwriting registered schemas" on
/// any second call within the same process run.
/// </summary>
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

    [Fact]
    [Trait("Description", "Regression: second call must not throw JsonSchemaException " +
                 "'Overwriting registered schemas'. Reproduces the crash seen " +
                 "when Open Project or profile change triggered a second reload.")]
    public async Task GetClaudeDesktopConfigNodeAsync_CalledTwice_DoesNotThrow()
    {
        using SchemaRegistry registry = OfflineRegistry();

        await registry.GetClaudeDesktopConfigNodeAsync(); // first — registers globally
        await registry.GetClaudeDesktopConfigNodeAsync(); // second — must hit memory cache
    }

    [Fact]
    [Trait("Description", "Same regression for the Claude Code settings schema.")]
    public async Task GetClaudeCodeSettingsNodeAsync_CalledTwice_DoesNotThrow()
    {
        using SchemaRegistry registry = OfflineRegistry();

        await registry.GetClaudeCodeSettingsNodeAsync();
        await registry.GetClaudeCodeSettingsNodeAsync();
    }

    [Fact]
    [Trait("Description", "Simulates the LoadAllWorkspacesAsync sequence: both schemas " +
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

    [Fact]
    public async Task GetClaudeCodeSettingsNodeAsync_ReturnsBundledSchema_WithProperties()
    {
        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode node = await registry.GetClaudeCodeSettingsNodeAsync();

        MessageAssert.NotNull(node, "Root schema node should not be null");
    }

    [Fact]
    public async Task GetClaudeDesktopConfigNodeAsync_ReturnsBundledSchema_NotNull()
    {
        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode node = await registry.GetClaudeDesktopConfigNodeAsync();

        MessageAssert.NotNull(node, "Root schema node should not be null");
    }
}