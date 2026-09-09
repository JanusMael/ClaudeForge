using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Json.Schema;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Locks the load precedence of <see cref="SchemaRegistry.GetSchemaAsync"/>:
/// <b>memory cache → HTTPS fetch (+ strip, + overlay) → bundled resource (+ strip, + overlay)</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This file previously locked the OPPOSITE order</b>, and did so deliberately: bundled had
/// to outrank the network because only the bundled reader applied the <c>*.overlay.json</c>
/// sibling, so a fresher network copy would have silently dropped the hand-curated additions.
/// That constraint was an artifact of where the merge lived, not a requirement. The overlay and
/// the external-<c>$ref</c> strip now apply to whichever source wins, so network-first is safe —
/// and <see cref="TheOverlayIsAppliedToAFetchedCopy_NotOnlyTheBundledOne"/> is the test that
/// makes that claim rather than assuming it.
/// </para>
/// <para>
/// ⛔⛔ <b>Two of the old tests kept passing after the reorder, for a reason their names
/// disowned.</b> Both used an offline handler, so bundled won and their overlay assertions
/// stayed true — while their names still said "OutranksDiskCache" about a disk cache that no
/// longer exists. Green tests describing a departed mechanism are worse than absent ones, which
/// is why this file was rewritten rather than patched.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaLoadPrecedenceTests
{
    /// <summary>A property name no real schema will ever declare.</summary>
    private const string NetworkSentinelProperty = "zzzFetchedFromNetworkSentinel";

    private const string ClaudeCodeCacheFileName = "claude-code-settings.json";

    private string _fakeHome = string.Empty;

    public TestContext TestContext { get; set; } = null!;

    /// <summary>Refuses every request, so a fall-through to bundled is unmistakable.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("Simulated network unavailable");
        }
    }

    /// <summary>Serves one canned body to every request.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly string _body;

        public CannedHandler(string body)
        {
            _body = body;
        }

        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// A schema declaring one sentinel property and nothing else.
    /// </summary>
    /// <remarks>
    /// No <c>$id</c>: JsonSchema.Net registers by <c>$id</c> globally, and this document must
    /// never collide with the real schema.
    /// </remarks>
    private static string SentinelSchema(string propertyName) =>
        $$"""
          {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
              "{{propertyName}}": { "type": "string" }
            }
          }
          """;

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

    private static IReadOnlyList<string> TopLevelNames(JsonSchemaNode root) =>
        [.. SchemaTreeBuilder.BuildTopLevel(root).Select(n => n.Name)];

    // ── The order ─────────────────────────────────────────────────────

    [TestMethod]
    [Description("A reachable network outranks the bundled copy — the whole point of the reorder.")]
    public async Task FetchedSchema_OutranksBundled_WhenTheNetworkAnswers()
    {
        CannedHandler handler = new(SentinelSchema(NetworkSentinelProperty));
        using SchemaRegistry registry = new(new HttpClient(handler));

        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        IReadOnlyList<string> names = TopLevelNames(root);

        Assert.IsTrue(
            names.Contains(NetworkSentinelProperty, StringComparer.Ordinal),
            "The bundled copy won while the network was answering. The fetch is step 2 and must "
            + $"outrank the bundled resource. Got: {string.Join(", ", names)}");
        Assert.AreEqual(1, handler.Attempts, "The fetch should have been attempted exactly once.");
    }

    [TestMethod]
    [Description("With no network, the bundled copy is used — and still carries its overlay.")]
    public async Task BundledIsUsed_WhenTheNetworkIsUnavailable()
    {
        FailingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler));

        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        IReadOnlyList<string> names = TopLevelNames(root);

        Assert.IsFalse(
            names.Contains(NetworkSentinelProperty, StringComparer.Ordinal),
            "Premise: nothing should have been served from the network here.");
        Assert.IsTrue(
            names.Contains("model", StringComparer.Ordinal),
            $"The bundled Claude Code schema should expose 'model'. Got: {string.Join(", ", names)}");
        Assert.IsTrue(handler.Attempts > 0, "The network should have been tried before falling back.");
    }

    // ── The property that makes network-first safe ────────────────────

    /// <summary>
    /// ⭐⭐ The overlay is applied to a FETCHED base, not only to the bundled one.
    /// </summary>
    /// <remarks>
    /// This is the assertion that retires the old ordering's justification. The served body is
    /// upstream's shape — a bare <c>model</c> string with no <c>examples</c> and no
    /// <c>default</c> — and those two keys live ONLY in
    /// <c>claude-code-settings.overlay.json</c>. If <c>model</c> still promotes to
    /// <see cref="SchemaValueType.Enum"/>, the overlay reached a network copy.
    /// <para>
    /// Losing this regresses the editor from an AutoCompleteBox to a plain TextBox — visible,
    /// but easy to attribute to anything else.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TheOverlayIsAppliedToAFetchedCopy_NotOnlyTheBundledOne()
    {
        const string upstreamShape = """
                                     {
                                       "$schema": "http://json-schema.org/draft-07/schema#",
                                       "type": "object",
                                       "properties": {
                                         "model": { "type": "string" }
                                       }
                                     }
                                     """;

        CannedHandler handler = new(upstreamShape);
        using SchemaRegistry registry = new(new HttpClient(handler));

        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        SchemaNode? model = SchemaTreeBuilder
                            .BuildTopLevel(root)
                            .FirstOrDefault(n => string.Equals(n.Name, "model", StringComparison.Ordinal));

        Assert.IsNotNull(model, "The fetched schema declares 'model'; it should be in the tree.");

        // ⛔⛔ WITHOUT THIS, THE TEST IS A TAUTOLOGY. Measured: disabling the fetch branch
        // entirely left it green, because bundled+overlay promotes 'model' to Enum too. The
        // served body declares 'model' and NOTHING else, so a one-property tree is the proof
        // that the fetched copy is the one the overlay was applied to.
        IReadOnlyList<string> names = TopLevelNames(root);
        CollectionAssert.AreEqual(
            new[] { "model" },
            names.ToArray(),
            "The tree has more than the single property the served body declared, so BUNDLED "
            + $"won and this test is not looking at a fetched copy at all. Got: {string.Join(", ", names)}");

        Assert.AreEqual(
            SchemaValueType.Enum,
            model.ValueType,
            "'model' did not promote to Enum, so the overlay was NOT applied to the fetched "
            + "copy. The served body has no 'examples'/'default' — only the overlay does — so "
            + "this is the assertion that proves the overlay is source-independent. Without it, "
            + "network-first silently drops every hand-curated addition.");
    }

    /// <summary>
    /// ⛔⛔ A fetched schema has its external <c>$ref</c>s stripped.
    /// </summary>
    /// <remarks>
    /// Upstream <c>opencode-config.json</c> types four <c>model</c> properties with a
    /// <c>models.dev</c> <c>$ref</c>. Left in, schema evaluation throws through
    /// <c>ValidateWorkspaceAsync</c> → <c>SaveAsync</c> for any config that sets a model — so
    /// the moment a fetch can win, the runtime must strip exactly as the refresh scripts do.
    /// This is the test that would have caught shipping network-first without the strip.
    /// </remarks>
    [TestMethod]
    public async Task AFetchedSchemaIsStripped_SoAnExternalRefCannotReachTheEditor()
    {
        const string withExternalRef = """
                                       {
                                         "$schema": "http://json-schema.org/draft-07/schema#",
                                         "type": "object",
                                         "properties": {
                                           "someModel": {
                                             "description": "Kept.",
                                             "type": "string",
                                             "$ref": "https://models.dev/model-schema.json#/$defs/Model"
                                           }
                                         }
                                       }
                                       """;

        CannedHandler handler = new(withExternalRef);
        using SchemaRegistry registry = new(new HttpClient(handler));

        JsonSchema schema = await registry.GetSchemaAsync(
            "https://example.invalid/stripme.json", "stripme.json", TestContext.CancellationToken);

        SchemaNode? node = SchemaTreeBuilder
                           .BuildTopLevel(schema.Root!)
                           .FirstOrDefault(n => string.Equals(n.Name, "someModel", StringComparison.Ordinal));

        Assert.IsNotNull(node,
            "The property survived the strip only if it is still in the tree — a strip that ate "
            + "its sibling keys would leave an untyped schema that permits anything.");
        Assert.AreEqual(SchemaValueType.String, node.ValueType,
            "The 'type': 'string' sibling must survive, or the strip left a permissive hole.");
    }

    /// <summary>The strip is idempotent, which is why bundled can share the path.</summary>
    [TestMethod]
    public void StrippingIsIdempotent_AndLeavesRefFreeTextUntouched()
    {
        const string clean = """
                             {
                               "properties": { "a": { "type": "string" } }
                             }
                             """;

        Assert.AreEqual(clean, SchemaRegistry.StripExternalRefs(clean),
            "Text with no external $ref must come back byte-identical, or every bundled schema "
            + "is needlessly rewritten on load.");

        // Upstream's actual formatting: one key per line, the $ref last in its object.
        string once = SchemaRegistry.StripExternalRefs(
            """
            {
              "properties": {
                "a": {
                  "type": "string",
                  "$ref": "https://models.dev/x.json#/$defs/M"
                }
              }
            }
            """);

        Assert.AreEqual(once, SchemaRegistry.StripExternalRefs(once),
            "A second pass must change nothing.");
        Assert.IsFalse(once.Contains("$ref", StringComparison.Ordinal), "The $ref should be gone.");
        Assert.IsTrue(once.Contains("\"type\": \"string\"", StringComparison.Ordinal),
            "The sibling type must remain — a strip that ate it would leave a schema that "
            + "permits anything.");
    }

    /// <summary>
    /// ⛔ An external <c>$ref</c> the line-based strip cannot remove makes the source unusable,
    /// loudly.
    /// </summary>
    /// <remarks>
    /// <b>Found by a test, not by reasoning.</b> The first version of the idempotence test above
    /// put the <c>$ref</c> inline with <c>"type"</c> and failed — correctly: the strip deletes
    /// whole lines, so an inline reference survives. Upstream formats one key per line, which is
    /// the only reason that has never bitten. "Correct because of somebody else's whitespace"
    /// needs to fail loudly, because an external <c>$ref</c> reaching the editor throws on save.
    /// <para>
    /// A fetched copy in this state falls back to bundled; asserted here at the
    /// <see cref="SchemaRegistry.StripExternalRefs"/> boundary, where the shape is visible.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AnInlineExternalRef_SurvivesTheStrip_AndIsThereforeRefused()
    {
        const string inlineRef = """
                                 {
                                   "properties": {
                                     "a": { "type": "string", "$ref": "https://models.dev/x.json#/$defs/M" }
                                   }
                                 }
                                 """;

        Assert.IsTrue(
            SchemaRegistry.StripExternalRefs(inlineRef).Contains("$ref", StringComparison.Ordinal),
            "Premise: the line-based strip does NOT remove an inline $ref. If this now passes, "
            + "the strip became structural and the refusal path below is dead code.");
    }

    /// <summary>A served copy with an unstrippable ref falls back to bundled, not to a throw.</summary>
    [TestMethod]
    public async Task AFetchedCopyWithAnInlineRef_FallsBackToBundled()
    {
        const string inlineRef = """
                                 {
                                   "$schema": "http://json-schema.org/draft-07/schema#",
                                   "type": "object",
                                   "properties": {
                                     "model": { "type": "string", "$ref": "https://models.dev/x.json#/$defs/M" }
                                   }
                                 }
                                 """;

        CannedHandler handler = new(inlineRef);
        using SchemaRegistry registry = new(new HttpClient(handler));

        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        IReadOnlyList<string> names = TopLevelNames(root);

        Assert.IsTrue(names.Count > 1,
            "The bundled schema should have supplied the tree — it declares far more than the "
            + $"one property the refused copy did. Got: {names.Count} propert(ies).");
        Assert.IsTrue(handler.Attempts > 0, "Premise: the fetch was attempted.");
    }

    // ── No empty fallback ─────────────────────────────────────────────

    /// <summary>
    /// With neither network nor bundled copy, the loader throws rather than returning <c>{}</c>.
    /// </summary>
    /// <remarks>
    /// ⛔ An empty JSON Schema permits <em>everything</em>. The old fallback therefore did not
    /// degrade validation, it removed it — while every surface still reported success. This
    /// replaced the test that asserted a fall-through to the disk cache.
    /// </remarks>
    [TestMethod]
    public async Task NoBundledResourceAndNoNetwork_Throws_RatherThanValidatingNothing()
    {
        FailingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler));

        SchemaUnavailableException ex =
            await Assert.ThrowsExactlyAsync<SchemaUnavailableException>(
                () => registry.GetSchemaAsync(
                    "https://example.invalid/no-such-schema.json",
                    "no-such-bundled-schema.json",
                    TestContext.CancellationToken));

        Assert.AreEqual("no-such-bundled-schema.json", ex.CacheFileName);
    }

    // ── The offline latch ─────────────────────────────────────────────

    /// <summary>
    /// One failed probe stands for the whole process, so an offline launch pays one timeout.
    /// </summary>
    /// <remarks>
    /// Without this, every schema URL waits for its own failure on the startup path. Asserted
    /// by attempt COUNT across two different URLs, because a latch that merely caches the
    /// result per URL would look identical from the outside for a single URL.
    /// </remarks>
    [TestMethod]
    public async Task OneFailedProbe_SuppressesFurtherFetchAttempts()
    {
        FailingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler));

        await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        Assert.AreEqual(1, handler.Attempts, "Premise: the first load probes the network once.");

        // A DIFFERENT url and file, so nothing can be served from the memory cache.
        await registry.GetSchemaAsync(
            "https://example.invalid/opencode-config.json",
            "opencode-config.json",
            TestContext.CancellationToken);

        Assert.AreEqual(1, handler.Attempts,
            "The second schema probed the network again. Offline startup then costs one timeout "
            + "per schema instead of one per session.");
    }

    /// <summary>An explicit refresh retries the network even after the latch tripped.</summary>
    /// <remarks>
    /// The latch keeps startup fast; it must not refuse a deliberate user request. This is the
    /// release direction — a latch with no reset is a one-way door, which reads as working
    /// until someone reconnects and nothing changes.
    /// </remarks>
    [TestMethod]
    public async Task RefreshAsync_RetriesTheNetwork_AfterTheLatchTripped()
    {
        FailingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler));

        await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        int afterFirstLoad = handler.Attempts;

        await registry.RefreshAsync(
            SchemaRegistry.ClaudeCodeSettingsSchemaUrl,
            ClaudeCodeCacheFileName,
            TestContext.CancellationToken);

        Assert.IsTrue(handler.Attempts > afterFirstLoad,
            "RefreshAsync did not re-probe the network. It must clear the offline latch, or "
            + "'check for updates' silently does nothing for the rest of the session.");
    }

    // ── Metadata readers follow the winning copy ──────────────────────

    /// <summary>
    /// ⛔ The hook-metadata reader describes the copy that loaded, not always the bundled one.
    /// </summary>
    /// <remarks>
    /// These readers used to go to the bundled resource and were right by construction, because
    /// bundled always won. Now that a fetch can win, the static overload would describe a
    /// different document than the tree was built from — silently, since both parse fine. The
    /// served body declares no <c>$defs.hookCommand</c>, so the instance reader must report
    /// none while the static still finds the bundled ones.
    /// </remarks>
    [TestMethod]
    public async Task TheHookMetadataReader_FollowsTheCopyThatActuallyLoaded()
    {
        CannedHandler handler = new(SentinelSchema(NetworkSentinelProperty));
        using SchemaRegistry registry = new(new HttpClient(handler));

        Assert.IsTrue(
            SchemaRegistry.GetHookCommandVariants(ClaudeCodeCacheFileName).Count > 0,
            "Premise: the BUNDLED Claude Code schema does declare hook command variants.");

        await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);

        Assert.AreEqual(
            0,
            registry.GetHookCommandVariantsFor(ClaudeCodeCacheFileName).Count,
            "The instance reader still reported the bundled hook variants after a fetched copy "
            + "won the load. The hook editor would offer shapes the loaded schema does not "
            + "define, with nothing failing anywhere.");
    }

    // ── The timeout that bounds an offline launch ─────────────────────

    /// <summary>Serves a body, but only after a delay longer than any sane fetch timeout.</summary>
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// A slow network does not hold up the load: the fetch is abandoned and bundled is used.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>Written because a canary with a deliberately empty prediction found nothing
    /// guarding this.</b> Raising <c>FetchTimeout</c> from 3s to 60s reddened zero tests — and
    /// that constant is the only thing standing between an unreachable-but-not-refusing network
    /// and a frozen launch, because this chain is awaited from
    /// <c>AgentConfigClientCore.OpenAsync</c>. The <see cref="HttpClient"/>'s own timeout is 15s,
    /// so "just use the client's" is the wrong answer by five times over.
    /// <para>
    /// Asserted as ELAPSED TIME rather than by reading the constant: the property that matters
    /// is that a launch stays fast, and a value assertion would keep passing if the timeout
    /// stopped being applied at all.
    /// </para>
    /// </remarks>
    [TestMethod]
    [Timeout(15000)]
    public async Task ASlowNetworkDoesNotHoldUpTheLoad()
    {
        using SchemaRegistry registry = new(new HttpClient(new SlowHandler()));

        long startedAt = Environment.TickCount64;
        JsonSchemaNode root = await registry.GetClaudeCodeSettingsNodeAsync(TestContext.CancellationToken);
        long elapsedMs = Environment.TickCount64 - startedAt;

        Assert.IsTrue(
            elapsedMs < 10_000,
            $"The load took {elapsedMs}ms against a handler that waits 20s. The fetch timeout "
            + "is not bounding it, so every launch behind a black-holed network freezes for as "
            + "long as the network cares to stall.");

        Assert.IsTrue(
            TopLevelNames(root).Contains("model", StringComparer.Ordinal),
            "After abandoning the fetch the bundled copy must supply the schema.");
    }

    // ── The SDK must read the copy its own client loaded ──────────────

    /// <summary>
    /// <c>ClaudeConfigClientBase</c> uses the INSTANCE metadata readers, not the statics.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>A source-text seam guard, because the behavioural one measured nothing.</b> A canary
    /// that reverted the SDK to <c>SchemaRegistry.GetHookEvents(...)</c> reddened zero tests: the
    /// registry-level contract is covered by
    /// <see cref="TheHookMetadataReader_FollowsTheCopyThatActuallyLoaded"/>, but nothing observed
    /// which overload the SDK picks. The static reads bundled, so choosing it would make the hook
    /// editor describe a document the client did not load — silently, since both parse.
    /// <para>
    /// Source text rather than reflection: both overloads exist and are legitimately callable, so
    /// there is no per-type metadata that distinguishes "called the right one".
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheClaudeSdkReadsHookMetadataFromItsOwnRegistryInstance()
    {
        string path = Path.Combine(
            RepoRoot(), "src", "ClaudeForge.Sdk.Claude", "ClaudeConfigClientBase.cs");
        Assert.IsTrue(File.Exists(path), $"'{path}' not found.");

        string source = File.ReadAllText(path);

        foreach (string bundledOnly in new[]
        {
            "SchemaRegistry.GetHookEvents(",
            "SchemaRegistry.GetHookCommandVariants(",
        })
        {
            Assert.IsFalse(
                source.Contains(bundledOnly, StringComparison.Ordinal),
                $"ClaudeConfigClientBase calls the static '{bundledOnly}', which always reads the "
                + "BUNDLED schema. Use the instance overload on SchemaRegistryInstance so the "
                + "metadata describes whichever copy this client actually loaded.");
        }

        Assert.IsTrue(
            source.Contains("SchemaRegistryInstance.GetHookEventsFor(", StringComparison.Ordinal),
            "Premise: the file should be calling the instance overload. If this fails the scan "
            + "has lost its subject and the assertions above pass vacuously.");
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
