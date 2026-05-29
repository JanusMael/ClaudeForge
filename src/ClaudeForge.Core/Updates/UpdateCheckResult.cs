namespace Bennewitz.Ninja.ClaudeForge.Core.Updates;

/// <summary>
/// Outcome of a single <see cref="GithubReleaseChecker.CheckAsync"/> call.
///
/// <para>
/// The shape carries enough for the banner UI to render
/// (<see cref="LatestTagName"/> for display, <see cref="ReleaseUrl"/> for
/// "Open release page") plus the structured <see cref="LatestVersion"/>
/// for any post-result comparisons the caller wants to do
/// (e.g. against the persisted dismissed-versions list).
/// </para>
///
/// <para>
/// <see cref="IsUpdateAvailable"/> is <c>true</c> ONLY when the network
/// fetch succeeded, the response parsed cleanly, the tag converted to a
/// valid <see cref="Version"/>, AND that version was strictly greater
/// than the current app version.  Any other outcome — network failure,
/// timeout, rate-limit, malformed JSON, missing or unparseable
/// <c>tag_name</c>, same-or-older version — collapses to
/// <see cref="NoUpdate"/>.  Callers do not need to branch on the
/// failure mode; the silent-skip contract is total.
/// </para>
/// </summary>
public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string? LatestTagName,
    Version? LatestVersion,
    string? ReleaseUrl)
{
    /// <summary>
    /// Canonical "no update" result.  Used for the no-network path, the
    /// malformed-response path, the older-or-equal-version path, and the
    /// disabled-check path — every "do not prompt the user" outcome
    /// collapses to this single value.
    /// </summary>
    public static UpdateCheckResult NoUpdate() => new(false, null, null, null);

    /// <summary>
    /// Build an update-available result.  <paramref name="tagName"/> is
    /// the raw tag string from GitHub (e.g. <c>"v2026.2.524"</c>); it is
    /// what gets compared against the persisted dismissed-versions list
    /// AND what renders in the banner title.
    /// </summary>
    public static UpdateCheckResult UpdateAvailable(string tagName, Version version, string? releaseUrl) =>
        new(true, tagName, version, releaseUrl);
}
