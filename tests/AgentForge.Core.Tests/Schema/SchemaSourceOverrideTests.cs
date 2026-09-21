using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// <c>--schema-source</c>: forcing the load down one branch of the chain.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Why the override exists.</b> Network-first means a healthy machine always exercises the
/// fetch, so the bundled path — the one every offline, throttled or firewalled user gets — is the
/// one a developer never sees. The alternative to this switch is unplugging the network, which
/// disables everything else at the same time.
/// </para>
/// <para>
/// ⛔ <b>The fatal case is the reason this file exists rather than a manual check.</b>
/// <c>fetched</c> with no network must THROW, not quietly use bundled — a run that looked like it
/// exercised the network path and did not proves nothing, and its screenshot is indistinguishable
/// from success. That is the one case a live probe cannot produce on a working machine.
/// </para>
/// <para>
/// ⛔⛔ <b>This class is <c>[DoNotParallelize]</c> because two of its tests mutate
/// <c>SchemaRegistry.ProcessSourceOverride</c>, which is process-GLOBAL.</b> This assembly is
/// <c>[assembly: Parallelize(MethodLevel)]</c>, and measured: without the attribute those two
/// passed in isolation and failed in the full suite — and worse than failing, they would make
/// every registry another test constructed concurrently load bundled.
/// </para>
/// <para>
/// Same treatment, for the same reason, as <c>PlatformInfoTests</c> and
/// <c>PlatformPathsCacheTests</c> in this assembly: a class that mutates process-wide state by
/// design runs serially, isolated from the parallelized rest.
/// </para>
/// </remarks>
[DoNotParallelize]
[TestClass]
public sealed class SchemaSourceOverrideTests
{
    private const string Url = "https://json.schemastore.org/claude-code-settings.json";
    private const string File = "claude-code-settings.json";

    public required TestContext TestContext { get; set; }

    /// <summary>Fails every request, and records that it was asked.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("Simulated network unavailable");
        }
    }

    /// <summary>Serves a minimal valid schema, and records that it was asked.</summary>
    private sealed class ServingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "type": "object", "properties": { "served": { "type": "string" } } }""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    /// <summary>
    /// <c>bundled</c> does not merely prefer the bundled copy — it never asks the network.
    /// </summary>
    /// <remarks>
    /// ⚠ Asserting the provenance alone would pass on a machine that fetched and then happened to
    /// fall back. The handler's call count is what separates "prefers bundled" from "offline",
    /// and offline is what the flag promises.
    /// </remarks>
    [TestMethod]
    public async Task Bundled_NeverTouchesTheNetwork()
    {
        ServingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler), SchemaSourceOverride.Bundled);

        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(0, handler.Calls,
            "The network was contacted despite --schema-source bundled, so the flag only changes "
            + "which copy wins rather than staying offline.");
        Assert.AreEqual(SchemaSource.Bundled, registry.ProvenanceFor(File)!.Source);
    }

    /// <summary>⛔ <c>fetched</c> with no network is FATAL, not a silent fallback.</summary>
    [TestMethod]
    public async Task Fetched_WithNoNetwork_Throws()
    {
        OfflineHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler), SchemaSourceOverride.Fetched);

        await Assert.ThrowsExactlyAsync<SchemaUnavailableException>(
            () => registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token));

        Assert.IsTrue(handler.Calls > 0, "Premise: the fetch must have been attempted.");
    }

    [TestMethod]
    public async Task Fetched_WithAReachableNetwork_UsesTheFetchedCopy()
    {
        ServingHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler), SchemaSourceOverride.Fetched);

        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(SchemaSource.Fetched, registry.ProvenanceFor(File)!.Source);
        Assert.IsTrue(handler.Calls > 0);
    }

    /// <summary>With no override, an unreachable network still falls back to bundled.</summary>
    /// <remarks>
    /// The control case, and it is load-bearing. Without it,
    /// <see cref="Fetched_WithNoNetwork_Throws"/> could be passing because the load throws
    /// whenever the network is down — a serious regression wearing the costume of a working
    /// feature.
    /// </remarks>
    [TestMethod]
    public async Task WithNoOverride_AnUnreachableNetworkFallsBackToBundled()
    {
        OfflineHandler handler = new();
        using SchemaRegistry registry = new(new HttpClient(handler));

        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(SchemaSource.Bundled, registry.ProvenanceFor(File)!.Source);
    }

    /// <summary>
    /// The process-wide default reaches a registry that was given no explicit override.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>This is the path the flag actually uses.</b> A launch builds several registries — the
    /// window's and one per client — and only a process-wide value reaches all of them. A flag
    /// landing on the first alone would leave the pages on one source while save-validation used
    /// another, which is why the override is not a constructor argument alone.
    /// </remarks>
    [TestMethod]
    public async Task TheProcessDefault_ReachesARegistryWithNoExplicitOverride()
    {
        SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Bundled;
        try
        {
            ServingHandler handler = new();
            using SchemaRegistry registry = new(new HttpClient(handler));

            _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

            Assert.AreEqual(0, handler.Calls,
                "The process-wide override did not reach a registry constructed without one, so "
                + "the flag would only affect whichever registry happened to be passed it.");
            Assert.AreEqual(SchemaSource.Bundled, registry.ProvenanceFor(File)!.Source);
        }
        finally
        {
            SchemaRegistry.ProcessSourceOverride = null;
        }
    }

    /// <summary>⭐ An explicit override beats the process default.</summary>
    /// <remarks>
    /// Which is what lets the other tests here pin a branch through the constructor and stay
    /// independent of whatever the process default happens to be.
    /// </remarks>
    [TestMethod]
    public async Task AnExplicitOverride_BeatsTheProcessDefault()
    {
        SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Bundled;
        try
        {
            ServingHandler handler = new();
            using SchemaRegistry registry = new(new HttpClient(handler), SchemaSourceOverride.Fetched);

            _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

            Assert.AreEqual(SchemaSource.Fetched, registry.ProvenanceFor(File)!.Source,
                "The constructor argument lost to the process default.");
        }
        finally
        {
            SchemaRegistry.ProcessSourceOverride = null;
        }
    }

}
