using Bennewitz.Ninja.OpenCode.Sdk.Backup;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Backup;

/// <summary>
/// Guards the backup redactor's allow-list against the captured shape of <c>opencode.db</c>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 14 keeps the user's session history by redacting a <b>copy</b> of the database rather
/// than excluding the file. The cost of that choice is that this project now owns a snapshot of
/// someone else's schema — and when OpenCode adds a secret column, a redactor built on the old
/// list ships <b>plaintext tokens</b> while every test still passes. These tests are the price
/// that makes the choice survivable, and they were written before the redactor for that reason.
/// </para>
/// <para>
/// ⛔ <b>Why none of this reads a live database.</b> CI has no OpenCode install, so a test that
/// queried <c>opencode.db</c> would either fail everywhere or skip everywhere — and a guard that
/// skips is a guard that never fires. The split instead mirrors how bundled schemas already work
/// here: a script captures from a live install into a committed artifact, and the suite guards
/// the invariants over that artifact. Drift surfaces as a red test the moment the artifact is
/// refreshed.
/// </para>
/// <para>
/// ⚠ <b>The digest test is the broad one, and it is meant to be noisy.</b> It fires on any
/// change to any table or column anywhere in the database, including changes with nothing to do
/// with secrets. That is deliberate: the alarm has to fire on a column nobody has classified
/// yet, so it cannot itself be narrowed by a name pattern.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeDatabaseSchemaTests
{
    /// <summary>
    /// Columns that look secret by name but are not, with the reason. Kept explicit so the
    /// classification test cannot be quietly widened to make a real finding disappear.
    /// </summary>
    private static readonly Dictionary<string, string> KnownNonSecrets = new(StringComparer.Ordinal)
    {
        ["account.token_expiry"] = "a timestamp, not a credential - blanking it corrupts the record",
        ["control_account.token_expiry"] = "a timestamp, not a credential",
        ["session.tokens_input"] = "an LLM usage counter",
        ["session.tokens_output"] = "an LLM usage counter",
        ["session.tokens_reasoning"] = "an LLM usage counter",
        ["session.tokens_cache_read"] = "an LLM usage counter",
        ["session.tokens_cache_write"] = "an LLM usage counter",
    };

    [TestMethod]
    public void EmbeddedSnapshotLoads()
    {
        OpenCodeDatabaseSchema schema = OpenCodeDatabaseSchema.Load();

        Assert.IsTrue(
            schema.Tables.Count >= 20,
            $"The snapshot reports only {schema.Tables.Count} tables. Every check below would "
            + "pass over a truncated or empty capture, so this is the anti-vacuity gate. "
            + "Re-run scripts/refresh-opencode-db-schema.ps1.");

        Assert.IsFalse(
            string.IsNullOrWhiteSpace(schema.OpenCodeVersion),
            "The snapshot must record which OpenCode build it came from.");
    }

    /// <summary>
    /// ⛔ The broad alarm: any table or column change anywhere reddens this until a human
    /// reviews the diff and moves the constant on purpose.
    /// </summary>
    [TestMethod]
    public void SchemaDigestMatchesTheReviewedConstant()
    {
        OpenCodeDatabaseSchema schema = OpenCodeDatabaseSchema.Load();
        string actual = OpenCodeDatabaseSchema.ComputeDigest(schema.Tables);

        Assert.AreEqual(
            OpenCodeSecretColumns.ExpectedTableColumnDigest,
            actual,
            $"opencode.db's shape has changed (snapshot is from OpenCode {schema.OpenCodeVersion}).\n"
            + "This is the alarm working. Before touching the constant:\n"
            + "  1. git diff src/OpenCode.Sdk/Assets/OpenCodeDatabaseSchema.json\n"
            + "  2. Decide whether any NEW column carries secret material.\n"
            + "  3. Add it to OpenCodeSecretColumns.ByTable if so.\n"
            + "  4. ONLY THEN update ExpectedTableColumnDigest.\n"
            + "Bumping the constant to get to green is the one way to defeat this mechanism.");
    }

    /// <summary>
    /// A redaction entry naming a column that does not exist silently redacts nothing, and
    /// nothing else in the system would notice.
    /// </summary>
    [TestMethod]
    public void EverySecretColumnActuallyExistsInTheSchema()
    {
        OpenCodeDatabaseSchema schema = OpenCodeDatabaseSchema.Load();
        List<string> missing = [];

        foreach ((string table, IReadOnlyList<string> columns) in OpenCodeSecretColumns.ByTable)
        {
            if (!schema.Tables.TryGetValue(table, out IReadOnlyList<string>? actual))
            {
                missing.Add($"table '{table}' is not in the schema at all");
                continue;
            }

            foreach (string column in columns)
            {
                if (!actual.Contains(column, StringComparer.Ordinal))
                {
                    missing.Add($"'{table}.{column}'");
                }
            }
        }

        Assert.AreEqual(
            0,
            missing.Count,
            "OpenCodeSecretColumns names columns that do not exist, so those entries redact "
            + "NOTHING while looking like protection:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The other direction: a column whose name announces it holds a secret must be classified.
    /// </summary>
    /// <remarks>
    /// ⚠ This cannot be the only check, and <c>credential.value</c> is the proof — the most
    /// sensitive column in the database is named <c>value</c> and matches no pattern worth
    /// writing. Name matching catches the obvious arrivals; the digest test above catches the
    /// rest by forcing a human to look.
    /// </remarks>
    [TestMethod]
    public void EveryObviouslySecretColumnIsClassified()
    {
        OpenCodeDatabaseSchema schema = OpenCodeDatabaseSchema.Load();

        string[] secretish =
        [
            "access_token", "refresh_token", "id_token", "bearer_token", "token",
            "secret", "secrets", "password", "passwd", "apikey", "api_key",
            "access_key", "private_key", "credential", "credentials",
        ];

        List<string> unclassified = [];

        foreach ((string table, IReadOnlyList<string> columns) in schema.Tables)
        {
            foreach (string column in columns)
            {
                string qualified = $"{table}.{column}";

                if (KnownNonSecrets.ContainsKey(qualified))
                {
                    continue;
                }

                bool looksSecret = secretish.Contains(column, StringComparer.OrdinalIgnoreCase);
                if (!looksSecret)
                {
                    continue;
                }

                bool classified =
                    OpenCodeSecretColumns.ByTable.TryGetValue(table, out IReadOnlyList<string>? declared)
                    && declared.Contains(column, StringComparer.Ordinal);

                if (!classified)
                {
                    unclassified.Add(qualified);
                }
            }
        }

        Assert.AreEqual(
            0,
            unclassified.Count,
            "These columns look like secrets and are not in OpenCodeSecretColumns, so a backup "
            + "would archive them in plaintext:\n  " + string.Join("\n  ", unclassified)
            + "\nAdd them to the allow-list, or to KnownNonSecrets with a reason if they are "
            + "genuinely not sensitive.");
    }

    /// <summary>
    /// Ties the C# digest implementation to the PowerShell one that produced the file. Two
    /// implementations of one format is a drift risk; this is what stops them disagreeing.
    /// </summary>
    [TestMethod]
    public void DigestOfEmbeddedSnapshotMatchesItsRecordedValue()
    {
        OpenCodeDatabaseSchema schema = OpenCodeDatabaseSchema.Load();

        Assert.AreEqual(
            schema.TableColumnDigest,
            OpenCodeDatabaseSchema.ComputeDigest(schema.Tables),
            "The digest recorded IN the snapshot by refresh-opencode-db-schema.ps1 does not "
            + "match what ComputeDigest produces over the same tables. The PowerShell and C# "
            + "implementations of the digest format have drifted apart.");
    }

    /// <summary>Guards the guard: an empty allow-list would satisfy every check above.</summary>
    [TestMethod]
    public void AllowListIsNotEmpty()
    {
        Assert.IsTrue(
            OpenCodeSecretColumns.ByTable.Count >= 4,
            "The allow-list has fewer than the four tables measured on both 1.17.9 and 1.18.18. "
            + "EverySecretColumnActuallyExistsInTheSchema passes vacuously over an empty list.");

        CollectionAssert.AreEquivalent(
            new[] { "account.access_token", "account.refresh_token", "control_account.access_token",
                "control_account.refresh_token", "credential.value", "session_share.secret" },
            OpenCodeSecretColumns.QualifiedColumns().ToArray(),
            "The measured secret-column set changed. Confirm against a real database before "
            + "editing this expectation.");
    }
}
