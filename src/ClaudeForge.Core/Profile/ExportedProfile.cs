using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Profile;

/// <summary>
/// Single-profile export artefact, byte-for-byte compatible with the
/// claudectx CLI tool's <c>ExportedProfile</c> Go struct (see
/// <c>https://github.com/foxj77/claudectx</c>'s
/// <c>internal/exporter/exporter.go</c>).  Lets a profile created in
/// either tool round-trip through the other without translation.
/// </summary>
/// <remarks>
/// <para>
/// JSON property names use snake_case (<c>claude_md</c>,
/// <c>mcp_servers</c>, <c>exported_at</c>) to match Go struct tags.
/// Empty / null <see cref="ClaudeMD"/> and <see cref="MCPServers"/>
/// MUST be omitted from the JSON output to mirror Go's
/// <c>,omitempty</c> serialiser behaviour — that is configured via
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> on the property
/// declarations.
/// </para>
/// <para>
/// <see cref="Version"/> is always <c>"1.0.0"</c>; the import path
/// rejects anything else (matching claudectx's strict-equality check).
/// Bump in lockstep with claudectx if/when the schema evolves.
/// </para>
/// </remarks>
public sealed record ExportedProfile
{
    /// <summary>Format version.  Both tools currently emit and require <c>"1.0.0"</c>.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = ExportedProfileFormat.CurrentVersion;

    /// <summary>The profile's logical name (also its directory name on disk).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Raw <c>settings.json</c> object (model, env, permissions, hooks,
    /// etc.).  Carried through as a <see cref="JsonNode"/> with no
    /// schema reshaping — claudectx and ClaudeForge both treat this
    /// as opaque so that schema additions on either side do not break
    /// import compatibility.
    /// </summary>
    /// <remarks>
    /// Defensively decorated with <see cref="JsonIgnoreCondition.WhenWritingNull"/>
    /// even though the export path validates non-null before
    /// serialisation — guards against a future code path that
    /// constructs <see cref="ExportedProfile"/> without setting
    /// <see cref="Settings"/> producing <c>"settings": null</c> on
    /// the wire (which claudectx would reject).
    /// </remarks>
    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Settings { get; init; }

    /// <summary>
    /// Optional global instruction file content.  Empty / null when
    /// the profile has no <c>CLAUDE.md</c>; omitted from the JSON in
    /// that case (matches claudectx <c>,omitempty</c>).
    /// </summary>
    [JsonPropertyName("claude_md")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaudeMD { get; init; }

    /// <summary>
    /// Optional <c>mcpServers</c> object (a map of server-name →
    /// configuration object).  Null when the profile has no
    /// <c>mcp.json</c>; omitted from the JSON in that case.
    /// </summary>
    [JsonPropertyName("mcp_servers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? MCPServers { get; init; }

    /// <summary>
    /// RFC 3339 / ISO 8601 UTC timestamp of when the export was produced.
    /// Stamped at <see cref="ProfileEngine.ExportProfileAsync"/> time.
    /// </summary>
    [JsonPropertyName("exported_at")]
    public string ExportedAt { get; init; } = string.Empty;
}

/// <summary>
/// Format constants for <see cref="ExportedProfile"/>.
/// Kept on a separate type so consumers can reference
/// <see cref="CurrentVersion"/> without instantiating a record.
/// </summary>
public static class ExportedProfileFormat
{
    /// <summary>The export-format version both tools agree on.</summary>
    public const string CurrentVersion = "1.0.0";
}