using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> covering every DTO that is
/// serialised to (or read from) a backup / export archive. Using source generation
/// instead of reflection-based APIs is required because Release builds set
/// <c>PublishTrimmed=true</c> — reflection-based serialisation silently produces
/// empty objects once unreferenced setters are trimmed.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(BackupWorktreeEntry))]
[JsonSerializable(typeof(SanitizationErrorPlaceholder))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;

/// <summary>
/// Body of the JSON placeholder substituted for a file that
/// <see cref="BackupEngine.RedactFileForSharing"/> couldn't safely
/// process during a <see cref="BackupMode.Sanitized"/> backup
/// (locked file, malformed JSON, etc.).  Serialised via
/// <see cref="BackupJsonContext"/> so escaping is always correct —
/// previously hand-rolled escaping missed control characters and
/// embedded quotes in filenames (M2 fix, 2026-05-14).
/// </summary>
/// <param name="ClaudeForgeSanitizationError">Failure reason
/// (<c>io-failure</c> / <c>redaction-failed-malformed-json</c>).</param>
/// <param name="File">The original filename (NEVER the full path —
/// we don't disclose directory layout to a support workflow).</param>
internal sealed record SanitizationErrorPlaceholder(
    [property: JsonPropertyName("_claudeforge_sanitization_error")]
    string ClaudeForgeSanitizationError,
    [property: JsonPropertyName("_file")] string File);