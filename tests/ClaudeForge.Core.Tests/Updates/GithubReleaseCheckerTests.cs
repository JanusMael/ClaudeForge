using System.Net;
using System.Net.Http;
using Bennewitz.Ninja.ClaudeForge.Core.Updates;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Updates;

/// <summary>
/// Tests for <see cref="GithubReleaseChecker"/>.  All tests inject a fake
/// <see cref="HttpMessageHandler"/> so they never hit the network — the
/// checker's HTTP layer is exercised end-to-end against canned responses.
///
/// <para>
/// The class covers four kinds of contract:
/// </para>
/// <list type="number">
///   <item>Version comparison: newer triggers, same / older do not.</item>
///   <item>Tag-parsing edge cases: leading <c>v</c>, missing <c>v</c>,
///         four-part versions, unparseable garbage, empty string.</item>
///   <item>Network-error behaviour: every documented failure mode
///         collapses to <see cref="UpdateCheckResult.NoUpdate"/>.</item>
///   <item>Response-shape edge cases: missing <c>tag_name</c>, empty
///         <c>tag_name</c>, missing <c>html_url</c>.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class GithubReleaseCheckerTests
{
    /// <summary>
    /// Minimal <see cref="HttpMessageHandler"/> stub: takes a response-
    /// generator delegate and returns its output for every request.
    /// Generator can throw to simulate network failure.
    /// </summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static GithubReleaseChecker MakeChecker(string json) =>
        MakeChecker(HttpStatusCode.OK, json);

    private static GithubReleaseChecker MakeChecker(HttpStatusCode status, string? json = null)
    {
        HttpClient http = new(new FakeHandler(_ =>
        {
            HttpResponseMessage resp = new(status);
            if (json is not null)
            {
                resp.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }
            return resp;
        }));
        return new GithubReleaseChecker(http, "https://fake-host/releases/latest");
    }

    private static GithubReleaseChecker MakeThrowingChecker(Exception toThrow)
    {
        HttpClient http = new(new FakeHandler(_ => throw toThrow));
        return new GithubReleaseChecker(http, "https://fake-host/releases/latest");
    }

    // ── Version-comparison contracts ────────────────────────────────────

    [TestMethod]
    public async Task CheckAsync_NewerReleaseAvailable_ReturnsUpdateAvailable()
    {
        // Current 1.0.0; remote latest v2.0.0 → banner should fire.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v2.0.0","html_url":"https://github.com/foo/bar/releases/tag/v2.0.0"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsTrue(result.IsUpdateAvailable,
            "Newer remote version must trigger the banner.");
        Assert.AreEqual("v2.0.0", result.LatestTagName,
            "Result must carry the raw tag (load-bearing — drives the dismissed-versions persistence).");
        Assert.AreEqual(new Version(2, 0, 0), result.LatestVersion);
        Assert.AreEqual("https://github.com/foo/bar/releases/tag/v2.0.0", result.ReleaseUrl);
    }

    [TestMethod]
    public async Task CheckAsync_SameVersion_ReturnsNoUpdate()
    {
        // Running the exact version GitHub says is latest → no banner.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v1.0.0","html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable,
            "Same-version-as-current must NOT trigger the banner.");
    }

    [TestMethod]
    public async Task CheckAsync_OlderRemoteVersion_ReturnsNoUpdate()
    {
        // Local dev build that's somehow ahead of the public release — no banner.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v1.0.0","html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(2, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable,
            "Local version > remote latest must NOT trigger the banner.");
    }

    [TestMethod]
    public async Task CheckAsync_NewerBuildNumberOnly_ReturnsUpdateAvailable()
    {
        // Catches a strict ">" comparison: 1.0.0.5 vs 1.0.0.4 must trigger.
        // System.Version comparison is lexicographic on the 4 parts; this
        // pins the contract for 4-part build-number-style tags (which is
        // the shape the source generator produces).
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v2026.5.524.0","html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(2026, 5, 523, 0));

        Assert.IsTrue(result.IsUpdateAvailable,
            "Build-number-only bumps (last component) must trigger the banner.");
    }

    // ── Tag-parsing edge cases ──────────────────────────────────────────

    [TestMethod]
    public void TryParseTag_LowercaseVPrefix_Parses()
    {
        bool ok = GithubReleaseChecker.TryParseTag("v1.2.3", out Version? v);
        Assert.IsTrue(ok);
        Assert.AreEqual(new Version(1, 2, 3), v);
    }

    [TestMethod]
    public void TryParseTag_UppercaseVPrefix_Parses()
    {
        // Some repos use "V" — accept both.  Cheap to handle; expensive
        // to omit (would silently fail to trigger the banner for one tag).
        bool ok = GithubReleaseChecker.TryParseTag("V1.2.3", out Version? v);
        Assert.IsTrue(ok);
        Assert.AreEqual(new Version(1, 2, 3), v);
    }

    [TestMethod]
    public void TryParseTag_NoVPrefix_Parses()
    {
        // Tag without leading v should still parse — defensive.
        bool ok = GithubReleaseChecker.TryParseTag("1.2.3.4", out Version? v);
        Assert.IsTrue(ok);
        Assert.AreEqual(new Version(1, 2, 3, 4), v);
    }

    [TestMethod]
    public void TryParseTag_FourPartVersion_Parses()
    {
        // The auto-versioning source generator emits 4-part tags; this is
        // the production shape.
        bool ok = GithubReleaseChecker.TryParseTag("v2026.2.524.0", out Version? v);
        Assert.IsTrue(ok);
        Assert.AreEqual(new Version(2026, 2, 524, 0), v);
    }

    [TestMethod]
    public void TryParseTag_AlphaSuffix_ReturnsFalse()
    {
        // "v1.2.3-alpha" — pre-release-style tag.  System.Version doesn't
        // accept hyphenated suffixes.  Returning false collapses the
        // overall result to NoUpdate, which matches our "ignore
        // pre-releases" contract (defense-in-depth on top of the API's
        // own /releases/latest behaviour).
        bool ok = GithubReleaseChecker.TryParseTag("v1.2.3-alpha", out Version? v);
        Assert.IsFalse(ok);
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TryParseTag_NotAVersionAtAll_ReturnsFalse()
    {
        bool ok = GithubReleaseChecker.TryParseTag("nightly-build", out Version? v);
        Assert.IsFalse(ok);
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TryParseTag_Empty_ReturnsFalse()
    {
        bool ok = GithubReleaseChecker.TryParseTag(string.Empty, out Version? v);
        Assert.IsFalse(ok);
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TryParseTag_Whitespace_ReturnsFalse()
    {
        bool ok = GithubReleaseChecker.TryParseTag("  ", out Version? v);
        Assert.IsFalse(ok);
        Assert.IsNull(v);
    }

    // ── Network-error behaviour ────────────────────────────────────────

    [TestMethod]
    public async Task CheckAsync_HttpRequestException_ReturnsNoUpdate()
    {
        // DNS failure, TLS error, connection-reset — any HttpRequestException
        // must collapse to NoUpdate without throwing.
        GithubReleaseChecker checker = MakeThrowingChecker(
            new HttpRequestException("simulated network failure"));

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsNull(result.LatestTagName);
    }

    [TestMethod]
    public async Task CheckAsync_Timeout_ReturnsNoUpdate()
    {
        // TaskCanceledException covers BOTH the HttpClient timeout path
        // AND the caller-supplied CancellationToken path.  In production
        // we only care that we don't propagate.
        GithubReleaseChecker checker = MakeThrowingChecker(
            new TaskCanceledException("simulated timeout"));

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_403RateLimit_ReturnsNoUpdate()
    {
        // GitHub's rate-limit response is 403 with a JSON body explaining
        // the limit.  We don't read the body — just treat any non-2xx as
        // no-update.
        GithubReleaseChecker checker = MakeChecker(
            HttpStatusCode.Forbidden,
            """{"message":"API rate limit exceeded"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_404RepoNotFound_ReturnsNoUpdate()
    {
        // Hypothetical: the canonical repo got renamed/deleted/private.
        // Collapses to no-update; the user is not blocked by a missing repo.
        GithubReleaseChecker checker = MakeChecker(HttpStatusCode.NotFound);

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_MalformedJson_ReturnsNoUpdate()
    {
        // Truncated response body — JsonDocument.Parse throws JsonException.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v2.0.0"""); // deliberately truncated

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    // ── Response-shape edge cases ───────────────────────────────────────

    [TestMethod]
    public async Task CheckAsync_MissingTagNameField_ReturnsNoUpdate()
    {
        // Response with no tag_name at all — odd, but graceful.
        GithubReleaseChecker checker = MakeChecker("""{"html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_EmptyTagName_ReturnsNoUpdate()
    {
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"","html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task CheckAsync_MissingHtmlUrl_StillReportsUpdate()
    {
        // No html_url is unusual but shouldn't block surfacing the update.
        // Banner will show with a null URL — Open-release button can disable.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"v2.0.0"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("v2.0.0", result.LatestTagName);
        Assert.IsNull(result.ReleaseUrl);
    }

    [TestMethod]
    public async Task CheckAsync_UnparseableTagName_ReturnsNoUpdate()
    {
        // Tag like "alpha-1" doesn't fit System.Version — collapses to
        // no-update so we never show a banner we can't compare against.
        GithubReleaseChecker checker = MakeChecker(
            """{"tag_name":"alpha-1","html_url":"x"}""");

        UpdateCheckResult result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    // ── UpdateCheckResult contract ──────────────────────────────────────

    [TestMethod]
    public void NoUpdate_HasAllFieldsNullOrFalse()
    {
        // Locks the "no update" canonical shape — every field zeroed.
        UpdateCheckResult result = UpdateCheckResult.NoUpdate();
        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsNull(result.LatestTagName);
        Assert.IsNull(result.LatestVersion);
        Assert.IsNull(result.ReleaseUrl);
    }

    [TestMethod]
    public void UpdateAvailable_CarriesAllFields()
    {
        Version v = new(2, 0, 0);
        UpdateCheckResult result = UpdateCheckResult.UpdateAvailable("v2.0.0", v, "https://x");
        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("v2.0.0", result.LatestTagName);
        Assert.AreEqual(v, result.LatestVersion);
        Assert.AreEqual("https://x", result.ReleaseUrl);
    }
}
