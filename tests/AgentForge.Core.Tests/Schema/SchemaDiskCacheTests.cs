using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// The disk cache as the MATERIALISED RESULT — not a tier in a precedence chain.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The shape being tested:</b> at launch the registry asks upstream whether anything changed
/// (a conditional GET, so <c>304</c> costs one header exchange), writes what it resolved as a
/// single stripped-and-overlaid artifact, and loads memory from that. Disk is where the answer is
/// KEPT; it is never itself a source, which is why provenance still reports only Fetched or
/// Bundled.
/// </para>
/// <para>
/// ⛔ <b>The rule with teeth is <see cref="OfflineKeepsAFetchedArtifact_RatherThanDowngradingToBundled"/>.</b>
/// Extracting bundled over a previously-fetched copy because today's launch is offline would hand
/// the user an OLDER schema than the one already on their machine, silently — no error, no badge
/// change, just different validation rules. Everything else here is hygiene; that one is data loss.
/// </para>
/// <para>
/// ⚠ Every test supplies its own sandbox directory. A registry built without one has NO disk cache
/// at all, which is the default precisely so the 34 test sites that construct a bare registry
/// cannot write into a real user profile.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaDiskCacheTests
{
    private const string File = "claude-code-settings.json";
    private const string Url = "https://example.invalid/claude-code-settings.json";

    private string _dir = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "schemacache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    // ── Handlers ───────────────────────────────────────────────────────────

    private sealed class OfflineHandler : HttpMessageHandler
    {
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new HttpRequestException("offline");
        }
    }

    /// <summary>Serves a body, and answers 304 when the request carries the matching ETag.</summary>
    private sealed class ConditionalHandler(string body, string etag) : HttpMessageHandler
    {
        internal int BodiesServed { get; private set; }
        internal int NotModifiedServed { get; private set; }
        internal string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();

            if (string.Equals(LastIfNoneMatch, etag, StringComparison.Ordinal))
            {
                NotModifiedServed++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            BodiesServed++;
            HttpResponseMessage ok = new(HttpStatusCode.OK) { Content = new StringContent(body) };
            ok.Headers.TryAddWithoutValidation("ETag", etag);
            return Task.FromResult(ok);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private SchemaRegistry Offline(OfflineHandler handler) =>
        new(new HttpClient(handler), sourceOverride: null, cacheDirectory: _dir);

    private string ArtifactPath => Path.Combine(_dir, File);

    private string SidecarPath => Path.Combine(_dir, File + ".meta.json");

    private JsonDocument ReadSidecar() => JsonDocument.Parse(System.IO.File.ReadAllText(SidecarPath));

    private string SidecarString(string property) =>
        ReadSidecar().RootElement.GetProperty(property).GetString() ?? string.Empty;

    // ── Offline: extract bundled, then reuse it ────────────────────────────

    [TestMethod]
    public async Task AFirstOfflineLaunch_ExtractsTheBundledCopyToDisk()
    {
        OfflineHandler handler = new();
        SchemaRegistry registry = Offline(handler);

        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.IsTrue(System.IO.File.Exists(ArtifactPath), "The resolved artifact must be written.");
        Assert.IsTrue(System.IO.File.Exists(SidecarPath), "Its sidecar must be written too.");
        Assert.AreEqual("Bundled", SidecarString("source"));

        // ⛔ The artifact is the RESOLVED document, not the raw bundled bytes: loading it later is
        // a plain parse, so it must already be stripped and overlaid.
        string written = System.IO.File.ReadAllText(ArtifactPath);
        Assert.IsFalse(written.Contains("\"$ref\": \"http", StringComparison.Ordinal),
            "External $refs must be stripped before the artifact is stored.");
    }

    [TestMethod]
    public async Task TheArtifactOnDisk_IsExactlyWhatTheRegistryValidatesAgainst()
    {
        OfflineHandler handler = new();
        SchemaRegistry registry = Offline(handler);
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        byte[] onDisk = System.IO.File.ReadAllBytes(ArtifactPath);
        string recorded = SidecarString("sha256");

        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(onDisk)).ToLowerInvariant(),
            recorded,
            "The sidecar digest must describe the bytes actually on disk, or integrity checking "
            + "is decorative.");

        Assert.AreEqual(recorded, registry.ProvenanceFor(File)!.Sha256,
            "The badge's digest and the cached artifact's must be the same number — otherwise the "
            + "provenance describes a document the app is not using.");
    }

    [TestMethod]
    public async Task ASecondOfflineLaunch_ReusesTheCachedArtifact()
    {
        OfflineHandler first = new();
        _ = await Offline(first).GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);
        DateTime writtenAt = System.IO.File.GetLastWriteTimeUtc(ArtifactPath);

        OfflineHandler second = new();
        SchemaRegistry registry = Offline(second);
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(writtenAt, System.IO.File.GetLastWriteTimeUtc(ArtifactPath),
            "An unchanged bundled source must not cause a rewrite on every launch.");
        Assert.AreEqual(SchemaSource.Bundled, registry.ProvenanceFor(File)!.Source);
    }

    // ── ⛔ The rule with teeth ─────────────────────────────────────────────

    [TestMethod]
    public async Task OfflineKeepsAFetchedArtifact_RatherThanDowngradingToBundled()
    {
        // A previous launch fetched a newer schema. Today there is no network.
        const string Fetched = """{"type":"object","properties":{"fetchedOnly":{"type":"string"}}}""";
        ConditionalHandler online = new(Fetched, "\"v1\"");
        SchemaRegistry day1 = new(new HttpClient(online), sourceOverride: null, cacheDirectory: _dir);
        _ = await day1.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("Fetched", SidecarString("source"));
        string fetchedArtifact = System.IO.File.ReadAllText(ArtifactPath);

        OfflineHandler offline = new();
        SchemaRegistry day2 = Offline(offline);
        _ = await day2.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("Fetched", SidecarString("source"),
            "⛔ Bundled overwrote a fetched artifact because the network was down. That silently "
            + "DOWNGRADES the user to an older schema, with nothing on screen to say so.");
        Assert.AreEqual(fetchedArtifact, System.IO.File.ReadAllText(ArtifactPath));
        Assert.AreEqual(SchemaSource.Fetched, day2.ProvenanceFor(File)!.Source,
            "The badge must still say the copy in use came from the network.");
    }

    [TestMethod]
    public async Task ANewerBundledCopy_DoesReplaceABundledSourcedArtifact()
    {
        // Simulate "an older build wrote this cache": same file, but recorded against bundled
        // bytes that no longer match what this build ships.
        OfflineHandler first = new();
        _ = await Offline(first).GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        string sidecar = System.IO.File.ReadAllText(SidecarPath);
        string stale = sidecar.Replace(
            SidecarString("bundledSourceSha256"),
            new string('0', 64),
            StringComparison.Ordinal);
        System.IO.File.WriteAllText(SidecarPath, stale);
        System.IO.File.WriteAllText(ArtifactPath, "{\"type\":\"object\"}");

        // Keep the digest honest so this exercises the STALENESS rule, not the integrity one.
        JsonDocument doc = JsonDocument.Parse(stale);
        string rehashed = stale.Replace(
            doc.RootElement.GetProperty("sha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"type\":\"object\"}"))).ToLowerInvariant(),
            StringComparison.Ordinal);
        System.IO.File.WriteAllText(SidecarPath, rehashed);

        OfflineHandler second = new();
        SchemaRegistry registry = Offline(second);
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreNotEqual("{\"type\":\"object\"}", System.IO.File.ReadAllText(ArtifactPath),
            "A build shipping different bundled bytes must refresh a bundled-sourced cache — "
            + "otherwise upgrading the app leaves the user on whatever the old build extracted.");
        Assert.AreEqual(
            64, SidecarString("bundledSourceSha256").Length,
            "…and must record the digest it actually built from.");
    }

    // ── Conditional GET ────────────────────────────────────────────────────

    [TestMethod]
    public async Task AMatchingETag_Costs304AndAdoptsTheCachedArtifact()
    {
        const string Body = """{"type":"object","properties":{"a":{"type":"string"}}}""";
        ConditionalHandler handler = new(Body, "\"abc\"");

        SchemaRegistry first = new(new HttpClient(handler), sourceOverride: null, cacheDirectory: _dir);
        _ = await first.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, handler.BodiesServed);
        DateTimeOffset? firstFetchedAt = first.ProvenanceFor(File)!.FetchedUtc;

        SchemaRegistry second = new(new HttpClient(handler), sourceOverride: null, cacheDirectory: _dir);
        _ = await second.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual("\"abc\"", handler.LastIfNoneMatch,
            "The second launch must replay the stored ETag, or the 304 path never engages.");
        Assert.AreEqual(1, handler.NotModifiedServed);
        Assert.AreEqual(1, handler.BodiesServed, "A 304 must not re-download the body.");

        Assert.AreEqual(firstFetchedAt, second.ProvenanceFor(File)!.FetchedUtc,
            "⚠ A 304 means the CONTENT is unchanged, so the fetch time it reports is when the "
            + "content was downloaded — not when we last asked.");
    }

    [TestMethod]
    public async Task AnETagIsNotReplayedForABundledSourcedArtifact()
    {
        // Offline first, so the cache is bundled-sourced.
        OfflineHandler offline = new();
        _ = await Offline(offline).GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        ConditionalHandler handler = new("""{"type":"object"}""", "\"zzz\"");
        SchemaRegistry registry = new(new HttpClient(handler), sourceOverride: null, cacheDirectory: _dir);
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.IsNull(handler.LastIfNoneMatch,
            "Replaying a tag recorded against a BUNDLED artifact invites a 304 meaning 'your copy "
            + "is current' about a copy that never came from this server.");
        Assert.AreEqual(1, handler.BodiesServed);
        Assert.AreEqual("Fetched", SidecarString("source"));
    }

    // ── Invalidation ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ACorruptArtifact_IsRebuiltRatherThanParsed()
    {
        OfflineHandler first = new();
        _ = await Offline(first).GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        // Truncation is the realistic corruption: a killed process mid-write.
        System.IO.File.WriteAllText(ArtifactPath, "{\"type\":\"obj");

        OfflineHandler second = new();
        SchemaRegistry registry = Offline(second);
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.IsTrue(System.IO.File.ReadAllText(ArtifactPath).Length > 100,
            "A digest mismatch must rebuild. Parsing a truncated schema yields one that permits "
            + "the wrong things and still reports success.");
    }

    // ── ⛔ --schema-source bundled must not disturb disk ───────────────────

    [TestMethod]
    public async Task ForcingBundled_LeavesAFetchedArtifactUntouched()
    {
        ConditionalHandler online = new("""{"type":"object","properties":{"x":{"type":"string"}}}""", "\"v1\"");
        SchemaRegistry fetched = new(new HttpClient(online), sourceOverride: null, cacheDirectory: _dir);
        _ = await fetched.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);
        string before = System.IO.File.ReadAllText(ArtifactPath);

        SchemaRegistry forced = new(
            new HttpClient(new OfflineHandler()),
            SchemaSourceOverride.Bundled,
            cacheDirectory: _dir);
        _ = await forced.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(before, System.IO.File.ReadAllText(ArtifactPath),
            "⛔ A debug flag must not mutate durable state — and writing here would clobber a "
            + "fetched artifact with the binary's older copy.");
        Assert.AreEqual("Fetched", SidecarString("source"));
        Assert.AreEqual(SchemaSource.Bundled, forced.ProvenanceFor(File)!.Source,
            "…while the forced registry still reports what IT used.");
    }

    [TestMethod]
    public async Task ARegistryWithNoCacheDirectory_WritesNothing()
    {
        SchemaRegistry registry = new(new HttpClient(new OfflineHandler()));
        _ = await registry.GetSchemaAsync(Url, File, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(0, Directory.GetFileSystemEntries(_dir).Length,
            "Null means no disk. This is what keeps the 34 bare-registry test sites off a real "
            + "user profile.");
    }
}
