using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Updates;

/// <summary>
/// Queries GitHub's Releases API for the latest non-prerelease release of
/// the ClaudeForge repo and compares its tag to the current app version.
///
/// <para>
/// <b>Network contract:</b> uses the
/// <c>/repos/{owner}/{repo}/releases/latest</c> endpoint, which by design
/// excludes drafts and pre-releases.  So beta tags will not raise the
/// "update available" banner on the user's machine — only published
/// stable releases do.
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
/// <b>Testability:</b> the <see cref="ReleasesLatestUrl"/> parameter on
/// the constructor exists so tests can point the checker at a fake
/// in-memory URL.  In practice every test injects a fake message
/// handler anyway, so the URL is mostly cosmetic in tests — but having
/// the override available makes debugging and integration-style tests
/// (e.g. local-server smoke) tractable without monkey-patching.
/// </para>
/// </summary>
public sealed class GithubReleaseChecker
{
    /// <summary>
    /// GitHub Releases API endpoint for the canonical ClaudeForge repo.
    /// Hard-coded by design — the check is scoped to ONE repository
    /// (the upstream JanusMael fork that publishes signed releases),
    /// not a configurable "which fork do I track" setting.
    /// </summary>
    public const string DefaultReleasesLatestUrl =
        "https://api.github.com/repos/JanusMael/ClaudeForge/releases/latest";

    private readonly HttpClient _http;
    private readonly string _releasesLatestUrl;

    /// <summary>
    /// Construct a checker against a specific endpoint URL.  Pass an
    /// <see cref="HttpClient"/> whose <see cref="HttpMessageHandler"/>
    /// is either a real network stack (production) or a fake that
    /// returns canned JSON (unit tests).  GitHub requires a
    /// <c>User-Agent</c> header — the caller is responsible for
    /// setting it on the supplied <see cref="HttpClient"/> (see
    /// <see cref="CreateDefaultProductionHttpClient"/> for the
    /// canonical production setup).
    /// </summary>
    public GithubReleaseChecker(HttpClient http, string? releasesLatestUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _releasesLatestUrl = releasesLatestUrl ?? DefaultReleasesLatestUrl;
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
    /// <remarks>
    /// Returned client is intended to be held as a process-wide static
    /// singleton.  Construct once, share across every check call.
    /// </remarks>
    public static HttpClient CreateDefaultProductionHttpClient(string appVersion)
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeForge/{appVersion}");
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
                await _http.GetAsync(_releasesLatestUrl, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Information(
                    "[UpdateCheck] GitHub returned {Status} for {Url} — treating as no-update.",
                    (int)response.StatusCode, _releasesLatestUrl);
                return UpdateCheckResult.NoUpdate();
            }

            string json = await response.Content
                .ReadAsStringAsync(ct).ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
            {
                Log.Information(
                    "[UpdateCheck] Response had no tag_name field; treating as no-update.");
                return UpdateCheckResult.NoUpdate();
            }

            string? tag = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tag))
            {
                Log.Information(
                    "[UpdateCheck] tag_name was empty; treating as no-update.");
                return UpdateCheckResult.NoUpdate();
            }

            string? releaseUrl = doc.RootElement.TryGetProperty("html_url", out JsonElement urlElement)
                ? urlElement.GetString()
                : null;

            if (!TryParseTag(tag, out Version? parsed) || parsed is null)
            {
                Log.Information(
                    "[UpdateCheck] tag_name '{Tag}' did not parse to a Version; treating as no-update.",
                    tag);
                return UpdateCheckResult.NoUpdate();
            }

            Version latest = parsed;
            if (latest > currentVersion)
            {
                Log.Information(
                    "[UpdateCheck] Newer release found: {Tag} (current={Current}, latest={Latest}).",
                    tag, currentVersion, latest);
                return UpdateCheckResult.UpdateAvailable(tag, latest, releaseUrl);
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
                _releasesLatestUrl, ex.Message);
            return UpdateCheckResult.NoUpdate();
        }
    }

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
