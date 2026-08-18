using System.Text.Json;
using System.Text.Json.Serialization;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Backup;

/// <summary>
/// Metadata stamped into the <c>manifest.json</c> entry of an Export archive.
/// Distinct from <see cref="BackupManifest"/> because Exports contain merged *effective*
/// configs (not source documents) and are intended for distribution / migration rather
/// than in-place restore.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Kind"/> is always <c>"export"</c>; the Restore list uses it to filter
/// exports out of the restorable-backups list so a user cannot accidentally "restore"
/// an effective-config snapshot.
/// </para>
/// <para>
/// <b>Schema v2 replaced two booleans with <see cref="Clients"/>.</b> v1 described the
/// products an export covered as <c>includesClaudeCode</c> / <c>includesClaudeDesktop</c>,
/// which cannot name a third product — and <see cref="BackupManifest"/>, its neighbour in
/// this folder, already used a list. The two adjacent persisted formats contradicted each
/// other. <see cref="TryRead"/> maps v1 onto the list so an archive written by a shipped
/// build still reports its products.
/// </para>
/// </remarks>
public sealed class ExportManifest
{
    /// <summary>
    /// Current on-disk schema version, written by every new export.
    /// <para>
    /// <b>2</b> since the products became <see cref="Clients"/>. Bump it whenever the
    /// on-disk shape changes incompatibly, and extend <see cref="TryRead"/> in the same
    /// commit — a version this code cannot map is rejected, not guessed at.
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// The last schema version that described its products as a pair of booleans.
    /// Anything at or below it is normalised by <see cref="TryRead"/>; a manifest with no
    /// <c>schemaVersion</c> at all deserialises to <c>0</c> and is treated the same way.
    /// </summary>
    internal const int BooleanProductsSchemaVersion = 1;

    [JsonPropertyName("kind")] public string Kind { get; set; } = "export";
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
    [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = string.Empty;

    /// <summary>
    /// The products this export covers, named by <see cref="ProductDescriptor.ArchiveFolder"/>
    /// — the same vocabulary as <see cref="BackupManifest.Clients"/>, and the same strings
    /// that prefix every config entry path inside the archive.
    /// </summary>
    /// <remarks>
    /// That last part is the invariant worth protecting: a reader uses this list to know
    /// which folders an export contains. Both sides derive from
    /// <see cref="ProductDescriptor.ArchiveFolder"/> so they cannot drift apart.
    /// </remarks>
    [JsonPropertyName("clients")]
    public List<string> Clients { get; set; } = new();

    /// <summary>
    /// <b>Schema v1 only — read, never written.</b> Populated when
    /// <see cref="TryRead"/> parses an archive produced before <see cref="Clients"/>
    /// existed, and mapped onto that list. <see langword="null"/> on anything written by
    /// this build, and omitted from the JSON when null so a v2 export does not carry a
    /// dead field.
    /// </summary>
    [JsonPropertyName("includesClaudeCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludesClaudeCode { get; set; }

    /// <inheritdoc cref="LegacyIncludesClaudeCode"/>
    [JsonPropertyName("includesClaudeDesktop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyIncludesClaudeDesktop { get; set; }

    /// <summary>Human-readable note written into the "//" field of each JSON body.</summary>
    [JsonPropertyName("headerComment")]
    public string HeaderComment { get; set; } = string.Empty;

    /// <summary>
    /// Parses an export archive's <c>manifest.json</c>, normalising schema v1's two
    /// booleans onto <see cref="Clients"/>. Returns <see langword="null"/> for malformed
    /// JSON, for a manifest that is not an export, and for a schema version newer than
    /// this build understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No caller in the app today</b> — nothing reads an export back; the Export command
    /// only writes. It exists anyway for two reasons. First, the format is <i>persisted on
    /// users' disks</i>, and the boolean → folder mapping is cheap to write now and
    /// archaeology later. Second, without a read path the written shape cannot be
    /// round-trip tested at all, so v1 tolerance would be an untested claim. When v1
    /// archives are old enough to abandon, delete <see cref="LegacyIncludesClaudeCode"/>,
    /// <see cref="LegacyIncludesClaudeDesktop"/> and the v1 branch below <i>together</i>.
    /// </para>
    /// <para>
    /// The <see cref="Kind"/> check is the mirror of the one in
    /// <c>BackupEngine.GetCachedOrParseManifest</c>: both manifests are named
    /// <c>manifest.json</c>, so either reader can be handed the other's file.
    /// </para>
    /// </remarks>
    public static ExportManifest? TryRead(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        ExportManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(utf8Json, BackupJsonContext.Default.ExportManifest);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest == null || !string.Equals(manifest.Kind, "export", StringComparison.Ordinal))
        {
            return null;
        }

        if (manifest.SchemaVersion > CurrentSchemaVersion)
        {
            return null;
        }

        // The version is the primary gate: at v2 and above `clients` is authoritative, so a
        // stale boolean cannot override a real product list.
        //
        // The second clause is the safety net, and it exists because of a trap worth naming:
        // `SchemaVersion` has a property initialiser, and System.Text.Json leaves an
        // initialised value untouched when the JSON omits the field. A manifest with no
        // `schemaVersion` therefore reads as CurrentSchemaVersion, not 0 — so a
        // version-gated migration alone would silently ignore its booleans and report an
        // export that covers nothing. Distinguishing absent from 2 would mean making the
        // field nullable; falling back when the list came out empty costs nothing and cannot
        // give a wrong answer, because an export with genuinely no products has no boolean
        // set to true either.
        if (manifest.SchemaVersion <= BooleanProductsSchemaVersion
            || (manifest.Clients.Count == 0 && manifest.HasLegacyProductFields))
        {
            manifest.Clients = LegacyProductFolders(manifest);
        }

        return manifest;
    }

    /// <summary>
    /// Whether the parsed JSON actually carried either v1 boolean. Distinguishes "absent"
    /// from "present and false", which the nullable backing fields exist to preserve.
    /// </summary>
    private bool HasLegacyProductFields =>
        LegacyIncludesClaudeCode.HasValue || LegacyIncludesClaudeDesktop.HasValue;

    /// <summary>
    /// The v1 booleans expressed as archive folder names.
    /// </summary>
    /// <remarks>
    /// Naming Claude's two products here is deliberate and not a product assumption: those
    /// two booleans only ever existed for Claude Code and Claude Desktop, so the mapping is
    /// a statement about archives already written, not about which products the app may
    /// host. It reads <see cref="ProductDescriptor.ArchiveFolder"/> rather than repeating
    /// the literals so it tracks the one property the writer and the restore layout share.
    /// </remarks>
    private static List<string> LegacyProductFolders(ExportManifest manifest)
    {
        List<string> folders = new(2);
        if (manifest.LegacyIncludesClaudeCode == true)
        {
            folders.Add(SchemaRegistry.ClaudeCodeProduct.ArchiveFolder);
        }

        if (manifest.LegacyIncludesClaudeDesktop == true)
        {
            folders.Add(SchemaRegistry.ClaudeDesktopProduct.ArchiveFolder);
        }

        return folders;
    }
}
