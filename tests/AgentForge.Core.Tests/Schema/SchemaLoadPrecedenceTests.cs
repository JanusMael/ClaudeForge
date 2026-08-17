using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Locks the load precedence of <see cref="SchemaRegistry.GetSchemaAsync"/>:
/// <b>memory cache → bundled resource (+ overlay) → disk cache → HTTPS fetch → empty</b>.
/// The load-bearing part is that <b>bundled outranks the disk cache</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Bundled-before-disk is not an optimization, it is a
/// correctness requirement: the bundled schema has a sibling
/// <c>*.overlay.json</c> carrying hand-curated additions the upstream schemastore
/// copy does not have (<c>model.examples</c> driving the AutoCompleteBox,
/// <c>model.default</c> driving the "(inherits: …)" watermark). If a stale disk
/// cache — or a fresh network copy — could win, those additions would vanish and
/// the editors would silently degrade to plain text boxes.
/// </para>
/// <para>
/// <b>Why it was worth adding.</b> Nothing asserted this. The precedence was
/// recorded only in prose, and the prose was <i>wrong in three places</i> —
/// <see cref="SchemaRegistry"/>'s class summary and one of its method summaries
/// both stated the order as "memory → disk → HTTP → bundled fallback", the exact
/// inverse, and two promotion tests cited "SchemaRegistry prefers the on-disk
/// cache" as the reason for their design. A maintainer reading any of those and
/// "fixing" the code to match would have broken the overlay with a green suite.
/// A behavioural test is the only thing that makes the ordering self-defending.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaLoadPrecedenceTests
{
    /// <summary>A property name no real schema will ever declare.</summary>
    private const string DiskSentinelProperty = "zzzStaleDiskCacheSentinel";

    private const string ClaudeCodeCacheFileName = "claude-code-settings.json";

    private string _fakeHome = string.Empty;

    /// <summary>Refuses every request, so a fall-through to HTTPS is unmistakable.</summary>
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

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "cf-schema-precedence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp dir must not fail the test.
        }
    }

    /// <summary>
    /// Plants a schema in the disk cache that declares <see cref="DiskSentinelProperty"/>
    /// and nothing else. If the disk copy is ever preferred, that property shows up in
    /// the loaded tree — and the real top-level properties do not.
    /// </summary>
    private string PlantSentinelDiskCache()
    {
        string dir = PlatformPaths.SchemaCacheDirectory;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, ClaudeCodeCacheFileName);

        // No $id: JsonSchema.Net registers schemas globally by $id, and this document
        // must never collide with the real schema if it does get parsed.
        File.WriteAllText(
            path,
            $$"""
              {
                "$schema": "http://json-schema.org/draft-07/schema#",
                "type": "object",
                "properties": {
                  "{{DiskSentinelProperty}}": { "type": "string" }
                }
              }
              """,
            Encoding.UTF8);

        return path;
    }

    [TestMethod]
    [Description("Bundled resource must outrank a populated disk cache, or the "
                 + "hand-curated *.overlay.json additions silently disappear.")]
    public async Task BundledSchema_OutranksDiskCache_WhenBothExist()
    {
        PlantSentinelDiskCache();

        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);

        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);
        IReadOnlyList<string> names = [.. top.Select(n => n.Name)];

        Assert.IsFalse(
            names.Contains(DiskSentinelProperty, StringComparer.Ordinal),
            $"The disk cache won: '{DiskSentinelProperty}' reached the loaded schema. "
            + "GetSchemaAsync must read the bundled resource (step 2) before the disk "
            + "cache (step 3) — the bundled copy is the only one carrying the "
            + "*.overlay.json additions.");

        Assert.IsTrue(
            names.Contains("model", StringComparer.Ordinal),
            "The bundled Claude Code schema should expose a top-level 'model' property. "
            + $"Got: {string.Join(", ", names)}");
    }

    [TestMethod]
    [Description("The overlay-only additions must survive the chain, not just the "
                 + "bundled reader — this is what bundled-first is protecting.")]
    public async Task OverlayAdditions_SurviveTheLoadChain_DespiteAStaleDiskCache()
    {
        PlantSentinelDiskCache();

        using SchemaRegistry registry = OfflineRegistry();
        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);

        SchemaNode? model = SchemaTreeBuilder
                            .BuildTopLevel(root)
                            .FirstOrDefault(n => string.Equals(n.Name, "model", StringComparison.Ordinal));

        Assert.IsNotNull(model, "Bundled schema must expose 'model'.");

        // model.examples + model.default live ONLY in claude-code-settings.overlay.json,
        // so their presence here proves the merged bundled copy — not the sentinel disk
        // copy and not a bare upstream fetch — is what the chain returned.
        Assert.AreEqual(
            SchemaValueType.Enum,
            model.ValueType,
            "'model' should promote to Enum, which only happens when the overlay's "
            + "'examples'/'default' are present. Losing this regresses the editor from "
            + "an AutoCompleteBox to a plain TextBox.");
    }

    [TestMethod]
    [Description("A schema with no bundled resource must still fall through to the "
                 + "disk cache — bundled-first must not mean bundled-only.")]
    public async Task DiskCache_IsStillUsed_WhenNoBundledResourceExists()
    {
        const string unbundled = "no-such-bundled-schema.json";

        string dir = PlatformPaths.SchemaCacheDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, unbundled),
            $$"""
              {
                "$schema": "http://json-schema.org/draft-07/schema#",
                "type": "object",
                "properties": {
                  "{{DiskSentinelProperty}}": { "type": "string" }
                }
              }
              """,
            Encoding.UTF8);

        using SchemaRegistry registry = OfflineRegistry();
        Json.Schema.JsonSchema schema = await registry.GetSchemaAsync(
            "https://example.invalid/no-such-bundled-schema.json",
            unbundled,
            TestContext.CancellationToken);

        IReadOnlyList<string> names =
            [.. SchemaTreeBuilder.BuildTopLevel(schema.Root!).Select(n => n.Name)];

        Assert.IsTrue(
            names.Contains(DiskSentinelProperty, StringComparer.Ordinal),
            "With no bundled resource for this name, step 3 (disk cache) should have "
            + $"supplied the schema. Got: {string.Join(", ", names)}");
    }

    public TestContext TestContext { get; set; } = null!;
}
