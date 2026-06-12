using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Catalog;

/// <summary>
/// Wire shape of <c>model-catalog.json</c>. Deserialized via the source-generated
/// <see cref="Schema.CoreJsonContext"/> (trim-safe). Mapped to the immutable
/// <see cref="ModelCatalog"/> domain model by <see cref="ModelCatalog.FromDto"/>.
/// </summary>
internal sealed class ModelCatalogDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("models")] public List<ModelDto>? Models { get; set; }
    [JsonPropertyName("aliases")] public Dictionary<string, string>? Aliases { get; set; }
    [JsonPropertyName("effortLevels")] public List<EffortLevelDto>? EffortLevels { get; set; }
    [JsonPropertyName("defaultModes")] public List<DefaultModeDto>? DefaultModes { get; set; }
}

internal sealed class ModelDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("legacy")] public bool Legacy { get; set; }
    [JsonPropertyName("supports1m")] public bool Supports1m { get; set; }
    [JsonPropertyName("supportedEffortLevels")] public List<string>? SupportedEffortLevels { get; set; }
    [JsonPropertyName("defaultEffortLevel")] public string? DefaultEffortLevel { get; set; }
    [JsonPropertyName("supportsAutoMode")] public bool SupportsAutoMode { get; set; }
}

internal sealed class EffortLevelDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("persists")] public bool Persists { get; set; }
}

internal sealed class DefaultModeDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("experimental")] public bool Experimental { get; set; }
    [JsonPropertyName("requiresAutoCapableModel")] public bool RequiresAutoCapableModel { get; set; }
    [JsonPropertyName("userScopeOnly")] public bool UserScopeOnly { get; set; }
}
