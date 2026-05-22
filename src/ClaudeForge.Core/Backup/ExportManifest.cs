using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Metadata stamped into the <c>manifest.json</c> entry of an Export archive.
/// Distinct from <see cref="BackupManifest"/> because Exports contain merged *effective*
/// configs (not source documents) and are intended for distribution / migration rather
/// than in-place restore.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is always <c>"export"</c>; the Restore list uses it to filter
/// exports out of the restorable-backups list so a user cannot accidentally "restore"
/// an effective-config snapshot.
/// </remarks>
public sealed class ExportManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("kind")] public string Kind { get; set; } = "export";
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("platform")] public string Platform { get; set; } = string.Empty;
    [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("includesClaudeCode")]
    public bool IncludesClaudeCode { get; set; }

    [JsonPropertyName("includesClaudeDesktop")]
    public bool IncludesClaudeDesktop { get; set; }

    /// <summary>Human-readable note written into the "//" field of each JSON body.</summary>
    [JsonPropertyName("headerComment")]
    public string HeaderComment { get; set; } = string.Empty;
}