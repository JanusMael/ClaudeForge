using System.Net;
using System.Net.Http;
using Bennewitz.Ninja.AgentForge.Core.Updates;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Updates;

/// <summary>
/// The monorepo release problem: one repository, several apps, one release list. These cover
/// that each app finds its own newest release and never a sibling's.
/// </summary>
/// <remarks>
/// The bug being guarded is not hypothetical arithmetic — it is a wrong download link. Before
/// tag scoping, ClaudeForge asked for <c>/releases/latest</c>, got whatever shipped most
/// recently across the whole repository, and would have offered a ClaudeForge user
/// OpenCodeForge's version and OpenCodeForge's release page.
/// </remarks>
[TestClass]
public sealed class ReleaseTagSchemeTests
{
    private static readonly ReleaseTagScheme OpenCodeForge = new("opencodeforge-");

    private sealed class FakeHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static GithubReleaseChecker Checker(ReleaseTagScheme scheme, string json)
        => new(new HttpClient(new FakeHandler(json)), scheme, "https://fake-host/releases");

    /// <summary>
    /// A release list as GitHub returns it: newest first, both apps interleaved.
    /// </summary>
    /// <remarks>
    /// The ordering matters to the test's meaning. OpenCodeForge's release is <b>first</b>, so
    /// it is what <c>/releases/latest</c> would have returned — which is precisely the wrong
    /// answer for a ClaudeForge user.
    /// </remarks>
    private const string MixedReleases = """
        [
          { "tag_name": "opencodeforge-v2026.4.101", "html_url": "https://x/ocf-101" },
          { "tag_name": "v2026.3.810",               "html_url": "https://x/cf-810"  },
          { "tag_name": "opencodeforge-v2026.4.100", "html_url": "https://x/ocf-100" },
          { "tag_name": "v2026.3.724",               "html_url": "https://x/cf-724"  }
        ]
        """;

    /// <summary>
    /// ⭐ The regression test for the monorepo bug. ClaudeForge must resolve its own newest
    /// release even though a sibling's is at the top of the list.
    /// </summary>
    [TestMethod]
    public async Task ClaudeForge_ResolvesItsOwnLatest_NotTheSiblingAtTheTopOfTheList()
    {
        UpdateCheckResult result = await Checker(ReleaseTagScheme.Unprefixed, MixedReleases)
            .CheckAsync(new Version(2026, 3, 724));

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("v2026.3.810", result.LatestTagName,
            "An unprefixed scheme must skip opencodeforge-* entirely. Returning the sibling's "
            + "tag here is the exact bug: the banner offers the wrong app's version.");
        Assert.AreEqual("https://x/cf-810", result.ReleaseUrl,
            "The download link must point at this app's release page, not the sibling's.");
    }

    /// <summary>And the same list read by the other app gives the other answer.</summary>
    [TestMethod]
    public async Task OpenCodeForge_ResolvesItsOwnLatest_FromTheSameList()
    {
        UpdateCheckResult result = await Checker(OpenCodeForge, MixedReleases)
            .CheckAsync(new Version(2026, 4, 100));

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.AreEqual("opencodeforge-v2026.4.101", result.LatestTagName);
        Assert.AreEqual("https://x/ocf-101", result.ReleaseUrl);
    }

    /// <summary>
    /// A repository where only the sibling has ever shipped: no update, rather than a wrong one.
    /// </summary>
    [TestMethod]
    public async Task NoReleaseOfMyOwn_IsNoUpdate_NotTheSiblings()
    {
        UpdateCheckResult result = await Checker(
            OpenCodeForge,
            """[ { "tag_name": "v2026.3.810", "html_url": "https://x/cf-810" } ]""")
            .CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable,
            "OpenCodeForge has published nothing here. Offering ClaudeForge's release to an "
            + "OpenCodeForge user would install the wrong application.");
    }

    /// <summary>
    /// ⚠ The legacy contract, and the reason ClaudeForge's tags can never gain a prefix: its
    /// thirteen published releases are all bare <c>v…</c> tags.
    /// </summary>
    [TestMethod]
    [DataRow("v2026.3.810")]
    [DataRow("2026.3.810")]
    [DataRow("V2026.3.810")]
    public void Unprefixed_StillRecognisesTheTagsAlreadyPublished(string tag)
    {
        Assert.IsTrue(ReleaseTagScheme.Unprefixed.TryParseVersion(tag, out Version? v));
        Assert.AreEqual(new Version(2026, 3, 810), v);
    }

    /// <summary>
    /// The unprefixed scheme must not claim a prefixed tag. It falls out of requiring the
    /// remainder to parse as a version, which is why that requirement is not incidental.
    /// </summary>
    [TestMethod]
    [DataRow("opencodeforge-v2026.4.101")]
    [DataRow("someotherapp-v1.0.0")]
    [DataRow("nightly")]
    [DataRow("")]
    public void Unprefixed_DoesNotClaimAnotherAppsTag(string tag)
        => Assert.IsFalse(ReleaseTagScheme.Unprefixed.TryParseVersion(tag, out _));

    [TestMethod]
    public void PrefixedScheme_RequiresItsPrefix()
    {
        Assert.IsTrue(OpenCodeForge.TryParseVersion("opencodeforge-v1.2.3", out Version? v));
        Assert.AreEqual(new Version(1, 2, 3), v);
        Assert.IsFalse(OpenCodeForge.TryParseVersion("v1.2.3", out _),
            "A bare tag belongs to the app that shipped first, not to this one.");
    }

    /// <summary>
    /// A migration scheme accepts an old prefix while publishing a new one, so releases made
    /// before a rename stay visible.
    /// </summary>
    [TestMethod]
    public void AlsoRecognise_AcceptsAnOldPrefixWithoutPublishingIt()
    {
        ReleaseTagScheme renamed = new("newname-", "oldname-");

        Assert.AreEqual("newname-", renamed.PublishPrefix);
        Assert.IsTrue(renamed.TryParseVersion("oldname-v1.0.0", out _));
        Assert.IsTrue(renamed.TryParseVersion("newname-v2.0.0", out _));
    }

    // ── what the list endpoint no longer filters for us ──────────────────────

    /// <summary>
    /// ⚠⚠ <c>/releases/latest</c> excluded drafts and pre-releases by design; the list endpoint
    /// does not. Losing this check would silently start pushing beta builds to every user.
    /// </summary>
    [TestMethod]
    public async Task PrereleasesAndDrafts_AreExcluded()
    {
        UpdateCheckResult result = await Checker(ReleaseTagScheme.Unprefixed, """
            [
              { "tag_name": "v9.9.9", "prerelease": true  },
              { "tag_name": "v9.9.8", "draft": true       },
              { "tag_name": "v2.0.0", "html_url": "https://x/stable" }
            ]
            """).CheckAsync(new Version(1, 0, 0));

        Assert.AreEqual("v2.0.0", result.LatestTagName,
            "A pre-release or draft must never raise the banner — the old endpoint filtered "
            + "them for us and the list endpoint does not.");
    }

    /// <summary>
    /// A release that omits both flags is an ordinary release. Defaulting the other way would
    /// hide every release from a response that simply does not mention them.
    /// </summary>
    [TestMethod]
    public async Task MissingDraftAndPrereleaseFields_MeanOrdinaryRelease()
    {
        UpdateCheckResult result = await Checker(
            ReleaseTagScheme.Unprefixed, """[ { "tag_name": "v2.0.0" } ]""")
            .CheckAsync(new Version(1, 0, 0));

        Assert.IsTrue(result.IsUpdateAvailable);
    }

    /// <summary>
    /// Highest version wins, not first in the list. Publication order and version order come
    /// apart the moment two apps ship independently — or a patch to an old line ships late.
    /// </summary>
    [TestMethod]
    public async Task HighestVersionWins_NotListOrder()
    {
        UpdateCheckResult result = await Checker(ReleaseTagScheme.Unprefixed, """
            [
              { "tag_name": "v2.0.1", "html_url": "https://x/late-patch" },
              { "tag_name": "v3.0.0", "html_url": "https://x/newest"     }
            ]
            """).CheckAsync(new Version(1, 0, 0));

        Assert.AreEqual("v3.0.0", result.LatestTagName);
        Assert.AreEqual("https://x/newest", result.ReleaseUrl);
    }

    /// <summary>An error object where an array belongs is a no-update, not a crash.</summary>
    [TestMethod]
    public async Task NonArrayResponse_IsNoUpdate()
    {
        UpdateCheckResult result = await Checker(
            ReleaseTagScheme.Unprefixed, """{ "message": "rate limit exceeded" }""")
            .CheckAsync(new Version(1, 0, 0));

        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task EmptyReleaseList_IsNoUpdate()
    {
        Assert.IsFalse((await Checker(ReleaseTagScheme.Unprefixed, "[]")
            .CheckAsync(new Version(1, 0, 0))).IsUpdateAvailable);
    }

    // ── the User-Agent, which used to name one app for all of them ───────────

    /// <summary>
    /// ⚠ The User-Agent was hardcoded to <c>ClaudeForge/{version}</c> in shared code, so every
    /// app in the repository would have identified itself as ClaudeForge to GitHub.
    /// </summary>
    [TestMethod]
    [DataRow("ClaudeForge")]
    [DataRow("OpenCodeForge")]
    public void ProductionHttpClient_IdentifiesTheCallingApp(string appName)
    {
        using HttpClient client =
            GithubReleaseChecker.CreateDefaultProductionHttpClient(appName, "1.2.3");

        Assert.AreEqual(
            $"{appName}/1.2.3",
            client.DefaultRequestHeaders.UserAgent.ToString(),
            "The User-Agent must name the app doing the asking.");
    }

    [TestMethod]
    public void ReleasesUrl_TargetsTheListEndpoint_NotLatest()
    {
        string url = GithubReleaseChecker.ReleasesUrlFor(GithubReleaseChecker.DefaultRepository);

        StringAssert.EndsWith(url, "/releases?per_page=100");
        Assert.IsFalse(url.Contains("/releases/latest", StringComparison.Ordinal),
            "/releases/latest is repository-wide and cannot be scoped to one app.");
    }

    /// <summary>
    /// The scheme has no default, so a new app cannot silently inherit another's tags.
    /// </summary>
    [TestMethod]
    public void SchemeIsRequired()
    {
        using HttpClient http = new(new FakeHandler("[]"));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new GithubReleaseChecker(http, null!));
    }
}
