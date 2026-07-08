using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Hooks;

/// <summary>
/// The hook-event vocabulary is reachable through the SDK client
/// (<c>client.Hooks.KnownEvents</c>) so HEADLESS consumers — CLI tools, MCP
/// servers — get the schema-derived, curated-ordered event list without any GUI
/// or schema plumbing. Mirrors how <c>client.Models</c> surfaces the model catalog.
/// </summary>
[TestClass]
public sealed class HooksKnownEventsTests
{
    private string _tempDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "claudeforge-hooks-known-" + Guid.NewGuid().ToString("N"));
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
    public async Task KnownEvents_ExposesSchemaDerivedVocabulary()
    {
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        List<string> events = client.Hooks.KnownEvents.Select(e => e.Name).ToList();

        // The bundled schema defines the standard events; the accessor surfaces
        // them curated-ordered — no GUI or schema plumbing needed by the consumer.
        CollectionAssert.Contains(events, "PreToolUse");
        CollectionAssert.Contains(events, "PostToolUse");
        CollectionAssert.Contains(events, "Stop");
        Assert.IsTrue(events.Count >= 10, "The bundled schema exposes the full hook-event set.");

        // Already curated-ordered — re-resolving the same set is a no-op (idempotent).
        CollectionAssert.AreEqual(HookEventCatalog.ResolveOrder(events).ToList(), events);
    }

    [TestMethod]
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
        using ClaudeCodeClient client = new();
        IReadOnlyList<HookEventInfo> known = client.Hooks.KnownEvents;

        List<string> names = known.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "PreToolUse");
        CollectionAssert.Contains(names, "Stop");
        // Already curated-ordered — re-resolving the same set is a no-op (idempotent).
        CollectionAssert.AreEqual(HookEventCatalog.ResolveOrder(names).ToList(), names);

        // The fix: descriptions are present even before Open.
        HookEventInfo cwd = known.First(e => e.Name == "CwdChanged");
        Assert.IsFalse(string.IsNullOrWhiteSpace(cwd.Description),
            "KnownEvents must carry the schema description even when the client was never opened.");
        StringAssert.Contains(cwd.Description!, "working directory");
    }

    [TestMethod]
    public async Task KnownEvents_CarrySchemaDescriptions()
    {
        // Headless consumers get the schema DESCRIPTION too, not just the name —
        // e.g. so a CLI/MCP tool can explain an unfamiliar event like CwdChanged.
        using ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);

        HookEventInfo cwd = client.Hooks.KnownEvents.First(e => e.Name == "CwdChanged");
        Assert.IsFalse(string.IsNullOrWhiteSpace(cwd.Description),
            "KnownEvents must carry the schema description, not just the name.");
        StringAssert.Contains(cwd.Description!, "working directory");
    }
}
