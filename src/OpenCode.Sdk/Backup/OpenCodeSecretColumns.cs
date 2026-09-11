using System.Collections.ObjectModel;

namespace Bennewitz.Ninja.OpenCode.Sdk.Backup;

/// <summary>
/// The columns of <c>opencode.db</c> that carry secret material — the reason the file is
/// treated as credential-bearing by backup and restore.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This list does not drive a redactor, and that is a decision rather than an omission.</b>
/// A backup includes <c>opencode.db</c> only behind an explicit opt-in, with an advisory that
/// the archive contains credentials, and <c>Sanitized</c> mode excludes the database outright —
/// the same shape this product already ships for <c>.credentials.json</c>. So nothing empties
/// these columns today; they are what makes the advisory true and the Sanitized exclusion
/// necessary.
/// </para>
/// <para>
/// ⚠ <b>Redacting in place was measured and deferred, not ruled out.</b> It needs a SQL engine:
/// ~1.8 MB of native payload per RID across six RIDs, a single-file publish that broke on the
/// first attempt, and a default transitive native package carrying a known high-severity
/// advisory. The trim gate was clean, so it stays feasible — see the Phase 14 decision block in
/// <c>docs/OPENCODEFORGE-PLAN.md</c> for the numbers, so a revisit starts from them.
/// </para>
/// <para>
/// ⛔⛔ <b>An explicit allow-list, not a name classifier, and <c>credential.value</c> is why.</b>
/// The single most sensitive column in the database is called <c>value</c> — a generic store
/// whose column name gives nothing away. Any name-based rule either misses it or, widened
/// enough to catch it, sweeps in half the database. So the pairs are named outright, and that
/// remains true whether the list drives an advisory or, later, a redactor.
/// </para>
/// <para>
/// ⚠ <b>The hazard is a list that quietly stops matching reality</b>, and it applies to an
/// advisory just as much as to a redactor. If OpenCode adds a credential table, "this archive
/// contains your credentials" has to still be true; if OpenCode ever <i>stops</i> storing
/// credentials here, the warning becomes noise nobody should be reading.
/// <see cref="OpenCodeDatabaseSchema"/> and its tests redden on either.
/// </para>
/// <para>
/// ✅ <b>Measured against 1.17.9 and re-measured against 1.18.18</b> — identical both times,
/// same four tables and same columns, across a minor-version bump. That is evidence the list is
/// stable enough for this design to be workable, not a promise that it will not move.
/// </para>
/// <para>
/// ⓘ <c>account.token_expiry</c> is deliberately <b>absent</b>: it is a timestamp, not a
/// credential, and blanking it would corrupt a restored account record without protecting
/// anything. The test that asserts every obviously-secret column is classified excludes it by
/// the same reasoning, and says so.
/// </para>
/// </remarks>
public static class OpenCodeSecretColumns
{
    /// <summary>
    /// Digest of the table-and-column set this list was written against.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Updating this constant is a deliberate act and must follow a review.</b> It moves
    /// only after someone has read the schema diff and confirmed that what the backup tells the
    /// user about this file is still accurate. Bumping it to make a red suite go green is the
    /// one way to defeat this entire mechanism.
    /// Produced by <c>scripts/refresh-opencode-db-schema.ps1</c>.
    /// </remarks>
    public const string ExpectedTableColumnDigest =
        "83e66c4f0b1c80ccb58de191df74f2d3de6de339d9c0dea4ea5933149647859c";

    /// <summary>Table name to the columns within it that carry secret material.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByTable { get; } =
        new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                // OAuth pairs for the signed-in account and the control-plane account.
                ["account"] = new ReadOnlyCollection<string>(["access_token", "refresh_token"]),
                ["control_account"] = new ReadOnlyCollection<string>(["access_token", "refresh_token"]),

                // The generic credential store. `value` holds the secret itself.
                ["credential"] = new ReadOnlyCollection<string>(["value"]),

                // The share-link secret: possession of it grants access to the shared session.
                ["session_share"] = new ReadOnlyCollection<string>(["secret"]),
            });

    /// <summary>Every <c>table.column</c> pair, for diagnostics and test messages.</summary>
    public static IEnumerable<string> QualifiedColumns()
        => ByTable.SelectMany(entry => entry.Value.Select(column => entry.Key + "." + column));
}
