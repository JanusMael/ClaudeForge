using System.Collections.ObjectModel;

namespace Bennewitz.Ninja.OpenCode.Sdk.Backup;

/// <summary>
/// The columns of <c>opencode.db</c> that carry secret material, and must be emptied from the
/// copy a backup archives.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>An explicit allow-list, not a name classifier, and <c>credential.value</c> is why.</b>
/// The single most sensitive column in the database is called <c>value</c> — a generic store
/// whose column name gives nothing away. Any name-based rule either misses it or, widened
/// enough to catch it, redacts half the database. So the pairs are named outright.
/// </para>
/// <para>
/// ⚠ <b>The mirror hazard is a list that quietly stops matching reality.</b> This is a snapshot
/// of upstream's schema; when OpenCode adds a secret column, a redactor built on today's list
/// ships plaintext tokens and every existing test still passes.
/// <see cref="OpenCodeDatabaseSchema"/> and its tests exist for exactly that, which is why the
/// guard was built before the redactor rather than after it.
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
    /// only after someone has read the schema diff and confirmed no new column carries a secret.
    /// Bumping it to make a red suite go green is the one way to defeat this entire mechanism.
    /// Produced by <c>scripts/refresh-opencode-db-schema.ps1</c>.
    /// </remarks>
    public const string ExpectedTableColumnDigest =
        "83e66c4f0b1c80ccb58de191df74f2d3de6de339d9c0dea4ea5933149647859c";

    /// <summary>Table name to the columns within it that must be redacted.</summary>
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
