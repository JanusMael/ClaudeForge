using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for types serialised in the Core layer.
/// Using source generation instead of reflection-based serialization ensures compatibility
/// with <c>PublishTrimmed=true</c> (Release builds).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;