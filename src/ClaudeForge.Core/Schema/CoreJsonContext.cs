using System.Text.Json.Serialization;
using Bennewitz.Ninja.ClaudeForge.Core.Catalog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for types serialised in the Core layer.
/// Using source generation instead of reflection-based serialization ensures compatibility
/// with <c>PublishTrimmed=true</c> (Release builds).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ModelCatalogDto))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;