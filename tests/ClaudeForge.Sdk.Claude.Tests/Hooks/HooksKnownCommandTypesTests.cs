using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Hooks;

/// <summary>
/// The hook command-type vocabulary is reachable through the SDK client
/// (<c>client.Hooks.KnownCommandTypes</c>) so HEADLESS consumers — CLI tools, MCP
/// servers — get the schema-derived per-type help text and per-field descriptions without
/// any GUI or schema plumbing. The command-variant counterpart to
/// <see cref="HooksKnownEventsTests"/> (which covers the lifecycle events).
/// </summary>
public sealed class HooksKnownCommandTypesTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public HooksKnownCommandTypesTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-hooks-cmdtypes-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task KnownCommandTypes_ExposesSchemaVariants()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<string> types = client.Hooks.KnownCommandTypes.Select(v => v.Type).ToList();

        // The bundled schema defines the standard hook command variants; the accessor
        // surfaces them — no GUI or schema plumbing needed by the consumer.
        Assert.Contains("command", types);
        Assert.Contains("prompt", types);
        Assert.Contains("http", types);
    }

    [Fact]
    public async Task KnownCommandTypes_CarrySchemaDescriptions_ForTypeAndFields()
    {
        // Headless consumers get the schema DESCRIPTIONS too — the per-type help text
        // and the per-field tooltip text — not just the type name.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        HookCommandVariantInfo command = client.Hooks.KnownCommandTypes.First(v => v.Type == "command");
        OrdinalAssert.Contains("Bash command hook", command.Description!);

        HookFieldInfo ifField = command.Fields.First(f => f.Name == "if");
        Assert.False(string.IsNullOrWhiteSpace(ifField.Description),
            "KnownCommandTypes must carry field descriptions, not just field names.");
    }

    [Fact]
    public void KnownCommandTypes_BeforeOpen_StillResolvesFromBundledSchema()
    {
        // Unlike the lifecycle events (which read the cached schema node and so need an
        // open), the command variants read the bundled schema directly, so a headless
        // caller gets a usable list even before OpenAsync.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        Assert.Contains(client.Hooks.KnownCommandTypes, v => v.Type == "command");
    }
}
