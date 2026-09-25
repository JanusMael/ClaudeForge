using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Hooks;

/// <summary>
/// The hook-event vocabulary is reachable through the SDK client
/// (<c>client.Hooks.KnownEvents</c>) so HEADLESS consumers — CLI tools, MCP
/// servers — get the schema-derived, curated-ordered event list without any GUI
/// or schema plumbing. Mirrors how <c>client.Models</c> surfaces the model catalog.
/// </summary>
public sealed class HooksKnownEventsTests : IDisposable
{
    private string _tempDir = null!;
    private string? _previousOverride;

    public HooksKnownEventsTests() => Setup();

    private void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-hooks-known-" + Guid.NewGuid().ToString("N"));
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
    public async Task KnownEvents_ExposesSchemaDerivedVocabulary()
    {
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<string> events = client.Hooks.KnownEvents.Select(e => e.Name).ToList();

        // The bundled schema defines the standard events; the accessor surfaces
        // them curated-ordered — no GUI or schema plumbing needed by the consumer.
        Assert.Contains("PreToolUse", events);
        Assert.Contains("PostToolUse", events);
        Assert.Contains("Stop", events);
        Assert.True(events.Count >= 10, "The bundled schema exposes the full hook-event set.");

        // Already curated-ordered — re-resolving the same set is a no-op (idempotent).
        Assert.Equal(HookEventCatalog.ResolveOrder(events).ToList(), events);
    }

    [Fact]
    public void KnownEvents_BeforeOpen_ReadsBundledSchema_WithDescriptions()
    {
        // A client that was never opened has no cached schema tree — this is the GUI's
        // situation (it builds the client via FromExistingWorkspace, not OpenAsync).
        // KnownEvents must STILL resolve from the bundled schema, curated-ordered AND
        // carrying descriptions, so a headless caller — and the editor's per-event
        // tooltips/labels — always get a usable, described list.
        //
        // Regression: this previously fell back to curated NAMES with null descriptions,
        // so the GUI's hook-event tooltips and detail label rendered blank.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        IReadOnlyList<HookEventInfo> known = client.Hooks.KnownEvents;

        List<string> names = known.Select(e => e.Name).ToList();
        Assert.Contains("PreToolUse", names);
        Assert.Contains("Stop", names);
        // Already curated-ordered — re-resolving the same set is a no-op (idempotent).
        Assert.Equal(HookEventCatalog.ResolveOrder(names).ToList(), names);

        // The fix: descriptions are present even before Open.
        HookEventInfo cwd = known.First(e => e.Name == "CwdChanged");
        Assert.False(string.IsNullOrWhiteSpace(cwd.Description),
            "KnownEvents must carry the schema description even when the client was never opened.");
        OrdinalAssert.Contains("working directory", cwd.Description!);
    }

    [Fact]
    public async Task KnownEvents_CarrySchemaDescriptions()
    {
        // Headless consumers get the schema DESCRIPTION too, not just the name —
        // e.g. so a CLI/MCP tool can explain an unfamiliar event like CwdChanged.
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        HookEventInfo cwd = client.Hooks.KnownEvents.First(e => e.Name == "CwdChanged");
        Assert.False(string.IsNullOrWhiteSpace(cwd.Description),
            "KnownEvents must carry the schema description, not just the name.");
        OrdinalAssert.Contains("working directory", cwd.Description!);
    }
}
