using System.Collections.ObjectModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bennewitz.Ninja.OpenCode.Sdk.Backup;

/// <summary>
/// The table-and-column shape of OpenCode's <c>opencode.db</c>, captured from a real install
/// and embedded so the backup redactor can be checked — and can check itself — against it.
/// </summary>
/// <remarks>
/// <para>
/// Phase 14 redacts secrets from a <b>copy</b> of the database rather than excluding the file,
/// which is what lets a restore return the user's session history. That choice is only safe
/// while the set of secret-bearing columns is known, and the schema belongs to upstream:
/// it changes on their schedule, without announcement, and a redactor built on a stale list
/// ships <b>plaintext tokens</b> while every existing test still passes.
/// </para>
/// <para>
/// ⛔ <b>That is the same silent-non-execution failure that ruled out the JSON-key classifier
/// for this job.</b> <c>SensitiveKeys</c> matches a property path in a document; a SQLite table
/// has no property path, so adding <c>auth</c> to it would have redacted nothing here and gone
/// green. The guard below exists so the database version of that mistake cannot be quiet.
/// </para>
/// <para>
/// Refresh with <c>scripts/refresh-opencode-db-schema.ps1</c>. A refresh that changes the shape
/// is <b>supposed</b> to redden <c>OpenCodeDatabaseSchemaTests</c> until someone updates
/// <see cref="OpenCodeSecretColumns.ExpectedTableColumnDigest"/> deliberately — reviewing the
/// diff for newly-arrived secret columns is the entire point of the alarm.
/// </para>
/// <para>
/// ⚠ Parsed with <see cref="JsonDocument"/> rather than <c>JsonSerializer.Deserialize</c>.
/// Everything under <c>src/</c> is <c>IsTrimmable</c>, and reflection-based deserialization
/// raises IL2026 under <c>PublishTrimmed</c>; the shape here is small and fixed, so a reader
/// costs less than a source-generated context.
/// </para>
/// </remarks>
public sealed class OpenCodeDatabaseSchema
{
    /// <summary>Logical name of the embedded snapshot.</summary>
    internal const string ResourceName =
        "Bennewitz.Ninja.OpenCode.Sdk.Assets.OpenCodeDatabaseSchema.json";

    private OpenCodeDatabaseSchema(
        int snapshotVersion,
        string openCodeVersion,
        string tableColumnDigest,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
    {
        SnapshotVersion = snapshotVersion;
        OpenCodeVersion = openCodeVersion;
        TableColumnDigest = tableColumnDigest;
        Tables = tables;
    }

    /// <summary>Format version of the snapshot file itself.</summary>
    public int SnapshotVersion { get; }

    /// <summary>The OpenCode build the snapshot was taken from, e.g. <c>1.18.18</c>.</summary>
    public string OpenCodeVersion { get; }

    /// <summary>Digest recorded at capture time, over the table-and-column set.</summary>
    public string TableColumnDigest { get; }

    /// <summary>Table name to its columns, in declared order.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Tables { get; }

    /// <summary>Reads the embedded snapshot.</summary>
    /// <exception cref="InvalidOperationException">The resource is missing or malformed.</exception>
    public static OpenCodeDatabaseSchema Load()
    {
        using Stream? stream = typeof(OpenCodeDatabaseSchema).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded resource '{ResourceName}' is missing. It is produced by "
                + "scripts/refresh-opencode-db-schema.ps1 and must be committed.");
        }

        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        var tables = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (JsonProperty table in root.GetProperty("tables").EnumerateObject())
        {
            List<string> columns = [];
            foreach (JsonElement column in table.Value.EnumerateArray())
            {
                if (column.GetString() is { } name)
                {
                    columns.Add(name);
                }
            }

            tables[table.Name] = new ReadOnlyCollection<string>(columns);
        }

        return new OpenCodeDatabaseSchema(
            root.GetProperty("snapshotVersion").GetInt32(),
            root.GetProperty("openCodeVersion").GetString() ?? "unknown",
            root.GetProperty("tableColumnDigest").GetString() ?? string.Empty,
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(tables));
    }

    /// <summary>
    /// Recomputes the digest from a table map. Must stay byte-identical to the PowerShell
    /// implementation in <c>scripts/refresh-opencode-db-schema.ps1</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Two implementations of one format, which is a drift risk of its own.</b> It is
    /// accepted because the capture has to run where sqlite3 is (a maintainer's machine) and the
    /// check has to run where the suite is (everywhere, including CI with no OpenCode install).
    /// <c>DigestOfEmbeddedSnapshotMatchesItsRecordedValue</c> ties the two together: it
    /// recomputes the digest here over the file the script wrote, so the two can never silently
    /// disagree about the format.
    /// <para>
    /// The format is one line per table, <c>name:col1,col2</c>, tables ordinal-sorted, joined
    /// with <c>\n</c>, hashed as UTF-8 SHA-256 and rendered lowercase hex. Ordinal matches
    /// SQLite's default BINARY collation, which is what ordered the capture.
    /// </para>
    /// </remarks>
    public static string ComputeDigest(IReadOnlyDictionary<string, IReadOnlyList<string>> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        StringBuilder builder = new();
        bool first = true;
        foreach (string table in tables.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append('\n');
            }

            first = false;
            builder.Append(table).Append(':').Append(string.Join(",", tables[table]));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
