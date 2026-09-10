using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// <c>SchemaRefresher</c> — what the apps' <em>Check for schema updates</em> action reports.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Every registry here is built with an explicit handler or with none at all. Nothing in
/// this class may touch <c>SchemaRegistry.ProcessSourceOverride</c>: this assembly is
/// <c>[assembly: Parallelize(MethodLevel)]</c>, and setting that global would make every
/// registry another test constructs concurrently load bundled.
/// </para>
/// <para>
/// ⭐ The distinction the tests are really defending is <c>Unavailable</c> versus
/// <c>Unchanged</c>. Both leave the user on the copy they had; only one of them means the
/// check succeeded, and collapsing them would let "we could not reach the server" be reported
/// as "you are up to date".
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaRefresherTests
{
    public required TestContext TestContext { get; set; }

    private static readonly ProductDescriptor Checkable = SchemaRegistry.ClaudeCodeProduct;
    private static readonly ProductDescriptor NoUpstream = SchemaRegistry.ClaudeDesktopProduct;

    private static string Doc(string marker) =>
        $$"""
          {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
              "servedFor": { "type": "string", "description": "{{marker}}" }
            }
          }
          """;

    /// <summary>Serves a fixed document every time.</summary>
    private sealed class ConstantHandler(string marker) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Doc(marker), Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>Serves a different document on each call, so the digest moves.</summary>
    private sealed class DriftingHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int n = Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Doc($"revision-{n}"), Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Answers once, then behaves as an unreachable host.</summary>
    private sealed class GoesDownHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                throw new HttpRequestException("Simulated network unavailable");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Doc("first"), Encoding.UTF8, "application/json"),
            });
        }
    }

    private async Task<SchemaRefreshResult> SingleAsync(SchemaRegistry registry)
    {
        IReadOnlyList<SchemaRefreshResult> results = await SchemaRefresher.RefreshAsync(
            registry, [Checkable], TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, results.Count, "Premise: one checkable product in, one result out.");
        return results[0];
    }

    [TestMethod]
    public async Task UpstreamServingWhatIsAlreadyLoaded_IsUnchanged()
    {
        using SchemaRegistry registry = new(new HttpClient(new ConstantHandler("stable")));
        _ = await registry.GetSettingsNodeAsync(Checkable, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(SchemaSource.Fetched, registry.ProvenanceFor(Checkable.SchemaFileName)!.Source,
            "Premise: the first load must have come from the handler, or the comparison below "
            + "is between two bundled copies and would be unchanged for the wrong reason.");

        SchemaRefreshResult result = await SingleAsync(registry);

        Assert.AreEqual(SchemaRefreshStatus.Unchanged, result.Status);
    }

    [TestMethod]
    public async Task UpstreamServingSomethingNew_IsUpdated()
    {
        using SchemaRegistry registry = new(new HttpClient(new DriftingHandler()));
        _ = await registry.GetSettingsNodeAsync(Checkable, TestContext.CancellationTokenSource.Token);
        string firstSha = registry.ProvenanceFor(Checkable.SchemaFileName)!.ShortSha;

        SchemaRefreshResult result = await SingleAsync(registry);

        Assert.AreEqual(SchemaRefreshStatus.Updated, result.Status);
        Assert.AreNotEqual(firstSha, result.Provenance!.ShortSha,
            "Updated must mean the digest actually moved, not merely that a fetch happened.");
    }

    /// <summary>
    /// ⛔ A check that cannot reach upstream leaves the registry on the bundled copy — pressing
    /// the button can move a session backwards, and the status has to say so.
    /// </summary>
    [TestMethod]
    public async Task NetworkGoingDownMidSession_IsUnavailable_NotUnchanged()
    {
        using SchemaRegistry registry = new(new HttpClient(new GoesDownHandler()));
        _ = await registry.GetSettingsNodeAsync(Checkable, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(SchemaSource.Fetched, registry.ProvenanceFor(Checkable.SchemaFileName)!.Source,
            "Premise: the session starts on a FETCHED copy, which is the state that can be lost.");

        SchemaRefreshResult result = await SingleAsync(registry);

        Assert.AreEqual(SchemaRefreshStatus.Unavailable, result.Status,
            "Falling back to bundled after a failed re-fetch is not 'no updates' — the app is "
            + "now running on a different schema than it was a moment ago.");
        Assert.AreEqual(SchemaSource.Bundled, result.Provenance!.Source);
    }

    [TestMethod]
    public async Task AnOfflineRegistry_IsUnavailable()
    {
        // No HttpClient at all — the fetch step is skipped entirely.
        using SchemaRegistry registry = new();

        SchemaRefreshResult result = await SingleAsync(registry);

        Assert.AreEqual(SchemaRefreshStatus.Unavailable, result.Status);
    }

    /// <summary>
    /// A first check on a registry that has loaded nothing reports Updated, because there was
    /// no prior copy for it to be unchanged from.
    /// </summary>
    [TestMethod]
    public async Task AFreshRegistryWithAReachableUpstream_IsUpdated()
    {
        using SchemaRegistry registry = new(new HttpClient(new ConstantHandler("stable")));

        Assert.IsNull(registry.ProvenanceFor(Checkable.SchemaFileName),
            "Premise: nothing loaded yet.");

        SchemaRefreshResult result = await SingleAsync(registry);

        Assert.AreEqual(SchemaRefreshStatus.Updated, result.Status);
    }

    /// <summary>
    /// ⭐ A product with no upstream is omitted, not reported as up to date.
    /// </summary>
    /// <remarks>
    /// Claude Desktop's schema is hand-maintained. Listing it in the result — with any status —
    /// would tell the user a check happened for it, and none did.
    /// </remarks>
    [TestMethod]
    public async Task AProductWithNoUpstream_IsOmittedEntirely()
    {
        Assert.IsFalse(SchemaRefresher.IsCheckable(NoUpstream),
            "Premise: Claude Desktop has no published schema URL.");
        Assert.IsTrue(SchemaRefresher.IsCheckable(Checkable),
            "Premise: Claude Code does, or the assertion below passes vacuously.");

        using SchemaRegistry registry = new(new HttpClient(new ConstantHandler("stable")));

        IReadOnlyList<SchemaRefreshResult> results = await SchemaRefresher.RefreshAsync(
            registry, [NoUpstream, Checkable], TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, results.Count, "Only the checkable product belongs in the results.");
        Assert.AreEqual(Checkable.Id, results[0].Product.Id);
    }

    /// <summary>
    /// Results come back in the order the products were given, so a surface can pair them with
    /// its own section list without matching on identity.
    /// </summary>
    [TestMethod]
    public async Task ResultsKeepTheOrderTheProductsWereGivenIn()
    {
        ProductDescriptor second = new(
            "opencode-tui-probe", "Probe", "https://opencode.ai/tui.json", "opencode-tui.json",
            ArchiveFolder: "Probe");

        using SchemaRegistry registry = new(new HttpClient(new ConstantHandler("stable")));

        IReadOnlyList<SchemaRefreshResult> results = await SchemaRefresher.RefreshAsync(
            registry, [Checkable, second], TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(Checkable.Id, results[0].Product.Id);
        Assert.AreEqual(second.Id, results[1].Product.Id);
    }
}
