namespace Bennewitz.Ninja.AgentForge.Core.Schema;

/// <summary>Where a loaded schema came from.</summary>
public enum SchemaSource
{
    /// <summary>The copy embedded in the binary.</summary>
    Bundled,

    /// <summary>Downloaded from the product's schema URL this session.</summary>
    Fetched,
}

/// <summary>
/// Which copy of a schema the app is actually using, and how to identify it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This exists because network-first made the question unanswerable from the outside.</b>
/// While bundled always won, "which schema is this?" had one answer: the one in the binary. Now
/// a settings page may take its shape from the binary or from a download that happened five
/// seconds ago, and nothing on screen distinguishes them. Neither can a bug report — which is
/// the case that actually matters, because "the editor shows a field I do not have" and "the
/// editor is missing a field I do have" are both explained by provenance and by nothing else.
/// </para>
/// <para>
/// ⚠ <b><see cref="Sha256"/> fingerprints the MERGED bytes</b> — after the external-<c>$ref</c>
/// strip and after the <c>*.overlay.json</c> merge — not the raw source. That is deliberate: the
/// merged document is what the editors read, so it is the thing two installs need to compare
/// when one of them renders a property the other does not. Hashing the raw download would
/// instead answer "did upstream change?", which the refresh script and its CI drift PR already
/// answer, and would report a difference between two installs whose behaviour is identical.
/// </para>
/// </remarks>
/// <param name="Source">Which copy won the load chain.</param>
/// <param name="FetchedUtc">
/// When the download completed, or <see langword="null"/> for <see cref="SchemaSource.Bundled"/>.
/// A bundled schema has no meaningful timestamp — it is as old as the binary, which the version
/// already states.
/// </param>
/// <param name="Sha256">Lower-case hex digest of the merged bytes.</param>
public sealed record SchemaProvenance(
    SchemaSource Source,
    DateTimeOffset? FetchedUtc,
    string Sha256)
{
    /// <summary>The digest, shortened for display. Full value stays available for a bug report.</summary>
    /// <remarks>
    /// Twelve hex characters. Enough that two schemas differing at all will differ here, short
    /// enough to sit in a badge — the same trade-off a short commit hash makes.
    /// </remarks>
    public string ShortSha => Sha256.Length <= 12 ? Sha256 : Sha256[..12];
}
