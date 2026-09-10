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
/// ⛔⛔ <b>Nothing here touches <c>SchemaRegistry.ProcessSourceOverride</c>, deliberately.</b> That
/// property is process-GLOBAL and this assembly runs test methods in PARALLEL, so a test which set
/// it would not merely be flaky — it would make every registry another test constructs
/// concurrently load bundled. Measured: an earlier version of this file exercised the process
/// default, passed in isolation, and failed in the full suite. Every test below pins its branch
/// through the CONSTRUCTOR, which is precisely why the constructor argument outranks the process
/// default. That the flag reaches the several registries a launch builds is verified end-to-end
/// against the running app, where process-global is the right scope for a process-wide switch.
/// </para>
/// </remarks>
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
}
