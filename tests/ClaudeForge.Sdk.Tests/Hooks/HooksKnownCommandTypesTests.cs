using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Hooks;

/// <summary>
/// The hook command-type vocabulary is reachable through the SDK client
/// (<c>client.Hooks.KnownCommandTypes</c>) so HEADLESS consumers — CLI tools, MCP
/// servers — get the schema-derived per-type help text and per-field descriptions without
/// any GUI or schema plumbing. The command-variant counterpart to
/// <see cref="HooksKnownEventsTests"/> (which covers the lifecycle events).
/// </summary>
[TestClass]
public sealed class HooksKnownCommandTypesTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-hooks-cmdtypes-" + Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public async Task KnownCommandTypes_ExposesSchemaVariants()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<string> types = client.Hooks.KnownCommandTypes.Select(v => v.Type).ToList();

        // The bundled schema defines the standard hook command variants; the accessor
        // surfaces them — no GUI or schema plumbing needed by the consumer.
        CollectionAssert.Contains(types, "command");
        CollectionAssert.Contains(types, "prompt");
        CollectionAssert.Contains(types, "http");
    }

    [TestMethod]
    public async Task KnownCommandTypes_CarrySchemaDescriptions_ForTypeAndFields()
    {
        // Headless consumers get the schema DESCRIPTIONS too — the per-type help text
        // and the per-field tooltip text — not just the type name.
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        HookCommandVariantInfo command = client.Hooks.KnownCommandTypes.First(v => v.Type == "command");
        StringAssert.Contains(command.Description!, "Bash command hook");

        HookFieldInfo ifField = command.Fields.First(f => f.Name == "if");
        Assert.IsFalse(string.IsNullOrWhiteSpace(ifField.Description),
            "KnownCommandTypes must carry field descriptions, not just field names.");
    }

    [TestMethod]
    public void KnownCommandTypes_BeforeOpen_StillResolvesFromBundledSchema()
    {
        // Unlike the lifecycle events (which read the cached schema node and so need an
        // open), the command variants read the bundled schema directly, so a headless
        // caller gets a usable list even before OpenAsync.
        using ClaudeCodeClient client = new();
        Assert.IsTrue(client.Hooks.KnownCommandTypes.Any(v => v.Type == "command"));
    }
}
