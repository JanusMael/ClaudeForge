using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Catalog;

/// <summary>
/// Source-generated serializer for the model catalog's wire shape, so loading it stays safe under
/// <c>PublishTrimmed=true</c>. It was part of <c>CoreJsonContext</c> until plans/00008 step 6b-3
/// moved the catalog out of <c>AgentForge.Core</c>.
/// </summary>
[JsonSerializable(typeof(ModelCatalogDto))]
internal sealed partial class ModelCatalogJsonContext : JsonSerializerContext;
