using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.Updates;

/// <summary>
/// Queries GitHub's Releases API for the newest release <i>belonging to this app</i> and
/// compares its tag to the current app version.
///
/// <para>
/// <b>Network contract:</b> uses the <c>/repos/{owner}/{repo}/releases</c> <b>list</b>
/// endpoint, filtered by a <see cref="ReleaseTagScheme"/>.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Why not <c>/releases/latest</c>, which this used to call.</b> That endpoint returns
/// the newest release of the <b>whole repository</b>. One repo hosting two apps therefore made
/// each app read whichever sibling shipped most recently — so the banner would offer
/// OpenCodeForge's version to a ClaudeForge user, and the download link would hand them the
/// wrong app. Scoping by tag is the only way to ask "the newest release of <i>this</i> app".
/// </para>
///
/// <para>
/// ⚠ <b>Drafts and pre-releases are now excluded explicitly, and that is load-bearing.</b>
/// <c>/releases/latest</c> filtered them for free; the list endpoint does not, so the fields
/// are read and honoured here. Dropping that check would silently start pushing beta tags to
/// every user — a regression with no visible cause at the call site.
/// </para>
///
/// <para>
/// <b>The newest release is the highest version, not the most recent publication.</b> Ordering
/// by version is what makes the answer independent of the order two apps happen to ship in.
/// </para>
///
/// <para>
/// ⚠ <b>One page, by design.</b> The request asks for 100 releases, newest first, and does not
/// paginate. If one app published more than 100 consecutive releases, a sibling's newest could
/// fall off the page and its check would report no update. That fails <i>quiet</i> rather than
/// wrong, which is the right direction, but it is a real ceiling rather than an oversight.
/// </para>
///
/// <para>
/// <b>Failure contract:</b> EVERY failure mode (DNS failure, TLS error,
/// timeout, HTTP non-2xx, rate-limit, malformed JSON, missing
/// <c>tag_name</c>, unparseable version, current ≥ latest) collapses to
/// <see cref="UpdateCheckResult.NoUpdate"/>.  Callers do not need to
/// distinguish — the silent-skip contract is total.  Failures log at
/// <c>Information</c> level (visible in the rolling log but not
/// user-facing) so a user tailing the log can investigate.
/// </para>
///
/// <para>
/// <b>HTTP client lifecycle:</b> this class does NOT own its
/// <see cref="HttpClient"/> — production code constructs the checker
/// with a process-wide singleton, and tests construct it with a fake
/// <see cref="HttpClient"/> whose <see cref="HttpMessageHandler"/>
/// returns canned responses (so the unit-test suite never hits the
/// network).  Static <see cref="HttpClient"/> is the recommended .NET
/// pattern — the anti-pattern is constructing one per call.
/// </para>
///
/// <para>
/// <b>Testability:</b> the <c>releasesUrl</c> parameter on the constructor exists so tests can
/// point the checker at a fake in-memory URL.  In practice every test injects a fake message
/// handler anyway, so the URL is mostly cosmetic in tests — but having the override available
/// makes debugging and integration-style tests (e.g. local-server smoke) tractable without
/// monkey-patching.
/// </para>
/// </summary>
public sealed class GithubReleaseChecker
{
    /// <summary>
    /// The repository that hosts every app in this solution, as <c>owner/name</c>.
    /// </summary>
    /// <remarks>
    /// Hard-coded by design — the check is scoped to ONE repository (the upstream fork that
    /// publishes signed releases), not a configurable "which fork do I track" setting. This
    /// names the <i>repository</i>, which several apps share; which releases within it belong
    /// to the running app is <see cref="ReleaseTagScheme"/>'s question, not this constant's.
    /// </remarks>
    public const string DefaultRepository = "JanusMael/ClaudeForge";

    private readonly HttpClient _http;
    private readonly string _releasesUrl;
    private readonly ReleaseTagScheme _tags;

    /// <summary>
    /// Construct a checker for one app's releases.  Pass an <see cref="HttpClient"/> whose
    /// <see cref="HttpMessageHandler"/> is either a real network stack (production) or a fake
    /// that returns canned JSON (unit tests).  GitHub requires a <c>User-Agent</c> header — the
    /// caller is responsible for setting it on the supplied <see cref="HttpClient"/> (see
    /// <see cref="CreateDefaultProductionHttpClient"/> for the canonical production setup).
    /// </summary>
    /// <param name="tags">
    /// Which releases in the repository belong to the running app.
    /// <para>
    /// ⚠ <b>Required, with no default on purpose.</b> A defaulted scheme is exactly how a new
    /// app silently adopts another's tags and starts offering its users the wrong download.
    /// Two call sites is a cheap price for that not being possible.
    /// </para>
    /// </param>
    public GithubReleaseChecker(HttpClient http, ReleaseTagScheme tags, string? releasesUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tags);
        _http = http;
        _tags = tags;
        _releasesUrl = releasesUrl ?? ReleasesUrlFor(DefaultRepository);
    }

    /// <summary>
    /// The releases-list endpoint for a repository, newest first, capped at one page of 100.
    /// </summary>
    public static string ReleasesUrlFor(string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        return $"https://api.github.com/repos/{repository}/releases?per_page=100";
    }

    /// <summary>
    /// Build the production <see cref="HttpClient"/>: 10-second timeout
    /// (the API endpoint should resolve in well under a second; 10s is
    /// the "the user's network is degraded" upper bound), and a
    /// User-Agent header that identifies the calling app + version per
    /// GitHub's API requirement.  GitHub rejects requests without a
    /// User-Agent with a 403; the rejection is silent to the user (we
    /// catch it like any other failure) but worth avoiding.
    /// </summary>
    /// <param name="appName">
    /// The running app's name, e.g. <c>ClaudeForge</c>. Part of the User-Agent, so it must be
    /// the app doing the asking rather than a hardcoded name — otherwise every app in the
    /// repository identifies itself as whichever one was written first.
    /// </param>
    /// <param name="appVersion">The running app's version.</param>
    /// <remarks>
    /// Returned client is intended to be held as a process-wide static
    /// singleton.  Construct once, share across every check call.
    /// </remarks>
    public static HttpClient CreateDefaultProductionHttpClient(string appName, string appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{appName}/{appVersion}");
        return client;
    }

    /// <summary>
    /// Fetch the latest release and decide whether to surface an update
    /// banner.  Returns <see cref="UpdateCheckResult.UpdateAvailable"/>
    /// only when the network fetch succeeded, the response parsed
    /// cleanly, the tag converted to a valid <see cref="Version"/>, AND
    /// that version is strictly greater than
    /// <paramref name="currentVersion"/>.  Every other outcome — and
    /// every exception inside the try/catch — collapses to
    /// <see cref="UpdateCheckResult.NoUpdate"/>.
    /// </summary>
    /// <param name="currentVersion">
    /// The running app's version.  Source-generated by the
    /// <c>AssemblyVersion</c> source generator (see
    /// <c>Directory.Build.props</c>'s <c>GenerateAutoVersionedAssemblyInfo</c>),
    /// so it is always populated in built artefacts — but defensively
    /// the caller can still pass <see cref="Version"/>.<see cref="Version()"/>
    /// (i.e. <c>0.0.0.0</c>) for unbuilt scenarios; in that case any
    /// real release will compare greater and the banner will fire.
    /// </param>
    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(_releasesUrl, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Information(
                    "[UpdateCheck] GitHub returned {Status} for {Url} — treating as no-update.",
                    (int)response.StatusCode, _releasesUrl);
                return UpdateCheckResult.NoUpdate();
            }

            string json = await response.Content
                .ReadAsStringAsync(ct).ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Log.Information(
                    "[UpdateCheck] Expected a release array; got {Kind}. Treating as no-update.",
                    doc.RootElement.ValueKind);
                return UpdateCheckResult.NoUpdate();
            }

            string? bestTag = null;
            string? bestUrl = null;
            Version? best = null;
            int mine = 0;

            foreach (JsonElement release in doc.RootElement.EnumerateArray())
            {
                // The list endpoint includes both, unlike /releases/latest. Skipping them here
                // is what keeps beta tags out of the user's update banner.
                if (IsTrue(release, "draft") || IsTrue(release, "prerelease"))
                {
                    continue;
                }

                string? tag = release.TryGetProperty("tag_name", out JsonElement tagElement)
                    ? tagElement.GetString()
                    : null;

                if (!_tags.TryParseVersion(tag, out Version? parsed) || parsed is null)
                {
                    continue;
                }

                mine++;
                if (best is not null && parsed <= best)
                {
                    continue;
                }

                best = parsed;
                bestTag = tag;
                bestUrl = release.TryGetProperty("html_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;
            }

            if (best is null || bestTag is null)
            {
                Log.Information(
                    "[UpdateCheck] No release matched tag prefix '{Prefix}' among {Count} "
                    + "release(s); treating as no-update.",
                    _tags.PublishPrefix, doc.RootElement.GetArrayLength());
                return UpdateCheckResult.NoUpdate();
            }

            if (best > currentVersion)
            {
                Log.Information(
                    "[UpdateCheck] Newer release found: {Tag} (current={Current}, latest={Latest}, "
                    + "matched {Mine} of {Total} releases).",
                    bestTag, currentVersion, best, mine, doc.RootElement.GetArrayLength());
                return UpdateCheckResult.UpdateAvailable(bestTag, best, bestUrl);
            }

            return UpdateCheckResult.NoUpdate();
        }
        catch (Exception ex) when (ex is HttpRequestException
                                          or TaskCanceledException
                                          or JsonException
                                          or InvalidOperationException)
        {
            Log.Information(
                ex,
                "[UpdateCheck] failed for {Url}: {Message} — treating as no-update.",
                _releasesUrl, ex.Message);
            return UpdateCheckResult.NoUpdate();
        }
    }

    /// <summary>
    /// Read a boolean field, treating a missing or non-boolean field as <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Absence means false for <c>draft</c> and <c>prerelease</c>: a response that omits them is
    /// describing an ordinary release, and defaulting the other way would hide every release.
    /// </remarks>
    private static bool IsTrue(JsonElement release, string field)
        => release.TryGetProperty(field, out JsonElement value)
           && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Strip a leading <c>v</c> / <c>V</c> from a release tag and parse
    /// the remainder as a <see cref="Version"/>.  Accepts both 3-part
    /// (<c>"v1.2.3"</c>) and 4-part (<c>"v1.2.3.4"</c>) forms, and the
    /// bare numeric form (<c>"1.2.3"</c>).  Returns <see langword="false"/>
    /// for anything that doesn't fit (e.g. <c>"alpha-1"</c>, empty
    /// string, etc.); the checker's caller treats false as "no update."
    /// </summary>
    /// <remarks>
    /// <see langword="public"/> so the cross-assembly
    /// <c>AppUpdateService</c> in the host app can use the same
    /// parse logic for the <c>--simulate-update &lt;version&gt;</c>
    /// debug-flag path (keeps real-check and simulated-check on the
    /// same tag-acceptance contract).  Also exposed for unit-test reach.
    /// </remarks>
    public static bool TryParseTag(string tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string body = (tag.StartsWith('v') || tag.StartsWith('V'))
            ? tag[1..]
            : tag;

        return Version.TryParse(body, out version);
    }
}
